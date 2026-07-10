using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Интерпретатор модов из GGG Trade API.
/// Обрабатывает два формата:
/// 1. Trade-search формат: "AffixName P1 — stat text" (explicitMods из /api/trade2/fetch)
/// 2. Plain формат: "stat text" (explicitMods из /api/trade2/history)
///
/// В trade-search формате один физический аффикс может давать несколько строк (multi-stat).
/// Например, Medved's P1 возвращается двумя строками:
///   "Medved's P1 — +22 to maximum Life"
///   "Medved's P1 — +131 to maximum Mana"
/// Резолвер группирует такие строки по (affixName, tierCode) перед поиском в библиотеке.
/// </summary>
public static class AffixResolver
{
    // "Medved's P1 — +22 to maximum Life" или "of Unmaking S1 — 10% increased ..."
    // Тир: P1..P3 (prefix), S1..S3 (suffix)
    private static readonly Regex ModLabelRe =
        new(@"^(.+?)\s+([PS]\d+)\s+[—\-]\s+(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// Разбирает строку в формате "AffixName Tier — statText".
    /// Возвращает null если строка не соответствует формату (plain stat).
    /// </summary>
    public static (string affixName, string tierCode, string statText)? ParseLabel(string raw)
    {
        var m = ModLabelRe.Match(raw);
        return m.Success
            ? (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)
            : null;
    }

    /// <summary>
    /// Ищет запись библиотеки, у которой ВСЕ шаблоны affixStats покрыты хотя бы одной строкой из группы.
    /// Используется для multi-stat аффиксов, где несколько строк = один аффикс.
    /// </summary>
    public static AffixLibraryEntry? FindGroupMatch(
        IReadOnlyList<string> statLines,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.AffixStats.Count == 0) continue;
            var allCovered = true;
            foreach (var template in entry.AffixStats)
            {
                var covered = false;
                foreach (var line in statLines)
                {
                    if (ParsedItemCraftEvaluator.StatLineMatchesTemplate(line, template))
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered) { allCovered = false; break; }
            }
            if (allCovered) return entry;
        }
        return null;
    }

    /// <summary>
    /// Главная точка входа: разрешает список сырых строк мода в список <see cref="ParsedModInfo"/>.
    /// Автоматически определяет формат и группирует multi-stat аффиксы.
    /// </summary>
    /// <param name="rawLines">Строки из explicitMods / fracturedMods / etc.</param>
    /// <param name="isFractured">Пометить все результаты как fractured.</param>
    /// <param name="library">Записи из affix_library.json.</param>
    /// <param name="runeEntries">Записи из rune_affix_overrides.json (все группы).</param>
    public static List<ParsedModInfo> Resolve(
        IEnumerable<string> rawLines,
        bool isFractured,
        IReadOnlyList<AffixLibraryEntry> library,
        IReadOnlyList<AffixLibraryEntry> runeEntries)
    {
        var result = new List<ParsedModInfo>();
        var lines = rawLines.ToList();
        if (lines.Count == 0) return result;

        // Парсим каждую строку: пытаемся извлечь (affixName, tierCode, statText)
        var parsed = lines
            .Select(raw => (raw: SaleModParser.StripMarkup(raw), label: ParseLabel(SaleModParser.StripMarkup(raw))))
            .ToList();

        // Если ни одна строка не имеет prefix-формата — plain mode, без группировки
        if (!parsed.Any(p => p.label.HasValue))
        {
            foreach (var (raw, _) in parsed)
                result.Add(ResolvePlain(raw, isFractured, library, runeEntries));
            return result;
        }

        // Группируем по (affixName, tierCode), сохраняя порядок первого вхождения
        var order = new List<(string affixName, string tierCode)>();
        var groups = new Dictionary<(string, string), List<string>>();
        var ungrouped = new List<string>();

        foreach (var (raw, label) in parsed)
        {
            if (label.HasValue)
            {
                var key = (label.Value.affixName, label.Value.tierCode);
                if (!groups.ContainsKey(key)) { groups[key] = []; order.Add(key); }
                groups[key].Add(label.Value.statText);
            }
            else
            {
                ungrouped.Add(raw);
            }
        }

        // Резолвим каждую группу
        foreach (var key in order)
        {
            var statLines = groups[key];
            var (affixName, tierCode) = key;
            var isPrefix = tierCode.StartsWith("P", StringComparison.Ordinal);
            int.TryParse(tierCode.AsSpan(1), out var tier);

            var match = FindGroupMatch(statLines, library)
                     ?? FindGroupMatch(statLines, runeEntries);

            result.Add(new ParsedModInfo
            {
                Raw        = statLines.Count == 1 ? statLines[0] : string.Join(" / ", statLines),
                Stripped   = statLines.Count == 1 ? statLines[0] : string.Join(" / ", statLines),
                AffixName  = match?.AffixName ?? affixName,
                AffixType  = match?.AffixType ?? (isPrefix ? "Prefix Modifier" : "Suffix Modifier"),
                AffixTier  = match?.AffixTier ?? tier,
                FamilyId   = match?.FamilyId,
                IsFractured = isFractured,
                Unmatched  = match is null,
            });
        }

        // Строки без prefix-формата (редкий случай в смешанном массиве)
        foreach (var raw in ungrouped)
            result.Add(ResolvePlain(raw, isFractured, library, runeEntries));

        return result;
    }

    public static ParsedModInfo ResolvePlainPublic(
        string raw, bool isFractured,
        IReadOnlyList<AffixLibraryEntry> library,
        IReadOnlyList<AffixLibraryEntry> runeEntries)
        => ResolvePlain(raw, isFractured, library, runeEntries);

    private static ParsedModInfo ResolvePlain(
        string raw, bool isFractured,
        IReadOnlyList<AffixLibraryEntry> library,
        IReadOnlyList<AffixLibraryEntry> runeEntries)
    {
        var match = SaleModParser.FindLibraryMatch(raw, library)
                 ?? SaleModParser.FindLibraryMatch(raw, runeEntries);
        return new ParsedModInfo
        {
            Raw        = raw,
            Stripped   = raw,
            AffixName  = match?.AffixName ?? "",
            AffixType  = match?.AffixType ?? "",
            AffixTier  = match?.AffixTier ?? 0,
            FamilyId   = match?.FamilyId,
            IsFractured = isFractured,
            Unmatched  = match is null,
        };
    }
}
