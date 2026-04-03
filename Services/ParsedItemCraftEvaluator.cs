using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Проверка распарсенного предмета на соответствие цели крафта: класс, тип модификатора, строка стата из библиотеки и порог переката.
/// </summary>
public static class ParsedItemCraftEvaluator
{
    private static readonly Regex LabeledRollParts = new(
        @"\b([A-Za-z]\w*)\s*:\s*([\d.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool ItemClassMatches(ParsedItem item, string expectedItemClass) =>
        string.Equals(item.ItemClass.Trim(), expectedItemClass.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Тип из условия/библиотеки и тип с предмета считаются одним семейством для поиска по имени+тиру
    /// (например <c>Desecrated Prefix Modifier</c> в плане и <c>Prefix Modifier</c> в Ctrl+Alt+C — один и тот же слот).
    /// </summary>
    public static bool AffixTypesCompatibleForNamedMatch(string typeFromPlan, string typeOnItem)
    {
        var p = (typeFromPlan ?? "").Trim();
        var a = (typeOnItem ?? "").Trim();
        if (string.Equals(p, a, StringComparison.Ordinal))
            return true;

        static bool IsPrefixFamily(string t) =>
            string.Equals(t, "Prefix Modifier", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase);

        static bool IsSuffixFamily(string t) =>
            string.Equals(t, "Suffix Modifier", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase);

        if (IsPrefixFamily(p) && IsPrefixFamily(a))
            return true;
        if (IsSuffixFamily(p) && IsSuffixFamily(a))
            return true;
        return false;
    }

    /// <summary>
    /// Строка стата из парсера совпадает с шаблоном из библиотеки (нормализация, затем точное совпадение или вхождение длинной подстроки).
    /// Короткие общие фрагменты вроде «ritual altars in map» намеренно не считаются совпадением — иначе разные статы путаются.
    /// </summary>
    public static bool StatLineMatchesTemplate(string parsedStatText, string libraryTemplate)
    {
        var a = NormalizeStat(parsedStatText);
        var b = NormalizeStat(libraryTemplate);
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        // Только короткая сторона достаточно длинная — иначе Contains даёт ложные совпадения между разными аффиксами.
        const int minLenForSubstringMatch = 44;
        var shorter = a.Length < b.Length ? a : b;
        var longer = a.Length < b.Length ? b : a;
        if (shorter.Length < minLenForSubstringMatch)
            return false;

        // Длинная строка не должна «поглощать» по Contains другую при большой разнице длин — иначе разные статы путаются (в т.ч. COUNT).
        if (longer.Length - shorter.Length > 12)
            return false;

        return longer.Contains(shorter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Условие остановки крафта: нужный префикс/суффикс, строка стата и значение переката ≥ minRoll.
    /// </summary>
    public static bool TryMatchCraftTarget(
        ParsedItem? item,
        string expectedItemClass,
        AffixLibraryEntry entry,
        string selectedAffixStat,
        double minRoll,
        out string explanation)
    {
        explanation = "";
        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан (парсер вернул пустой или невалидный результат).";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        var statIdx = AffixCraftPatternBuilder.GetStatIndex(entry, selectedAffixStat);
        var rangeStr = statIdx >= 0 && statIdx < entry.AffixRanges.Count ? entry.AffixRanges[statIdx] : null;
        var slots = CraftAffixCascadeHelper.CountSlotsFromRangeString(rangeStr);
        var mins = Enumerable.Repeat(minRoll, slots).ToList();

        if (!TryGetRollValuesForNamedAffix(
                item,
                expectedItemClass,
                entry.AffixType,
                entry.AffixName,
                entry.AffixTier,
                selectedAffixStat,
                slots,
                out var values,
                out var rollExpl))
        {
            explanation = string.IsNullOrEmpty(rollExpl)
                ? $"На предмете нет модификатора «{entry.AffixName}» ({entry.AffixType}, T{entry.AffixTier}) со строчкой «{selectedAffixStat}»."
                : rollExpl;
            return false;
        }

        var pass = RollVectorMeetsMins(values, mins, out var failIdx);
        var minStr = string.Join(", ", mins.Select(FormatMin));
        var valStr = string.Join(", ", values.Select(FormatMin));
        explanation = pass
            ? $"Найден модификатор «{entry.AffixName}», стата «{selectedAffixStat}», значения [{valStr}] ≥ [{minStr}]."
            : $"Найден модификатор «{entry.AffixName}», стата «{selectedAffixStat}», слот {failIdx + 1}: значение не достигает порога (есть [{valStr}], нужно ≥ [{minStr}]).";
        return pass;
    }

    /// <summary>
    /// Ищет модификатор по типу, имени и тиру; из строки стата извлекает ровно <paramref name="expectedSlotCount"/> чисел (X, Y, …).
    /// </summary>
    public static bool TryGetRollValuesForNamedAffix(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string affixName,
        int affixTier,
        string statTemplate,
        int expectedSlotCount,
        out List<double> values,
        out string explanation)
    {
        values = new List<double>();
        explanation = "";

        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан.";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        if (expectedSlotCount < 1)
            expectedSlotCount = 1;

        foreach (var affix in item.Affixes)
        {
            if (!AffixTypesCompatibleForNamedMatch(affixType, affix.Type))
                continue;
            if (!string.Equals(affix.Name, affixName.Trim(), StringComparison.Ordinal))
                continue;
            if (affix.Tier != affixTier)
                continue;

            foreach (var line in affix.EffectDetails)
            {
                if (!StatLineMatchesTemplate(line.StatText, statTemplate))
                    continue;

                if (!TryGetOrderedRollValues(line, out var vals, out var rollNote))
                {
                    explanation = $"Строка стата найдена, но не удалось извлечь числа. {rollNote}";
                    return false;
                }

                if (vals.Count != expectedSlotCount)
                {
                    explanation =
                        $"Ожидалось {expectedSlotCount} перекат(ов), в строке предмета — {vals.Count} ({string.Join(", ", vals)}).";
                    return false;
                }

                values = vals;
                return true;
            }

            explanation = $"Модификатор «{affixName}» найден, но без подходящей строки стата «{statTemplate}».";
            return false;
        }

        return false;
    }

    /// <summary>
    /// Перекаты по множеству записей библиотеки: класс + тип + строка стата (все подходящие имя/тир из affix_library.json).
    /// </summary>
    public static bool TryGetRollValuesForTypeAndStat(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string statTemplate,
        IReadOnlyList<AffixLibraryEntry> libraryEntries,
        int expectedSlotCount,
        out List<double> values,
        out string explanation)
    {
        values = new List<double>();
        explanation = "";

        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан.";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        if (expectedSlotCount < 1)
            expectedSlotCount = 1;

        var candidates = CraftAffixCascadeHelper.GetCandidateNameTiers(
            expectedItemClass,
            affixType,
            statTemplate,
            libraryEntries);
        if (candidates.Count == 0)
        {
            explanation =
                $"В библиотеке нет аффиксов для класса «{expectedItemClass}», типа «{affixType}» и стата «{statTemplate}».";
            return false;
        }

        foreach (var affix in item.Affixes)
        {
            if (!string.Equals(affix.Type, affixType.Trim(), StringComparison.Ordinal))
                continue;

            var key = (affix.Name.Trim(), affix.Tier);
            if (!candidates.Contains(key))
                continue;

            foreach (var line in affix.EffectDetails)
            {
                if (!StatLineMatchesTemplate(line.StatText, statTemplate))
                    continue;
                if (!TryGetOrderedRollValues(line, out var vals, out _))
                    continue;
                if (vals.Count != expectedSlotCount)
                    continue;
                values = vals;
                return true;
            }
        }

        explanation =
            $"На предмете нет модификатора из заданного набора (тип «{affixType}», стата «{statTemplate}», нужно {expectedSlotCount} перекат(ов)).";
        return false;
    }

    /// <summary>
    /// Перекаты по типу модификатора и строке стата без привязки к <c>affix_library.json</c>.
    /// Используется как fallback, когда библиотека не содержит нужного имени/тира, но на предмете строка явно есть.
    /// </summary>
    public static bool TryGetRollValuesForTypeAndStatNoLibrary(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string statTemplate,
        out List<double> values,
        out string explanation)
    {
        values = new List<double>();
        explanation = "";

        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан.";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        foreach (var affix in item.Affixes)
        {
            if (!string.Equals(affix.Type, affixType.Trim(), StringComparison.Ordinal))
                continue;

            foreach (var line in affix.EffectDetails)
            {
                if (!StatLineMatchesTemplate(line.StatText, statTemplate))
                    continue;
                if (!TryGetOrderedRollValues(line, out var vals, out var note))
                {
                    explanation = $"Строка стата найдена, но не удалось извлечь числа. {note}";
                    return false;
                }

                values = vals;
                return true;
            }
        }

        explanation =
            $"На предмете нет модификатора типа «{affixType}» со строчкой стата «{statTemplate}».";
        return false;
    }

    public static bool RollVectorMeetsMins(IReadOnlyList<double> actual, IReadOnlyList<double> mins, out int failIndex)
    {
        failIndex = -1;
        if (actual.Count != mins.Count)
            return false;
        for (var i = 0; i < actual.Count; i++)
        {
            if (actual[i] < mins[i])
            {
                failIndex = i;
                return false;
            }
        }

        return true;
    }

    /// <summary>Числа переката в порядке появления в строке (X, Y, …).</summary>
    public static bool TryGetOrderedRollValues(AffixEffectLine line, out List<double> values, out string note)
    {
        values = new List<double>();
        note = "";
        var raw = line.RolledValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            note = "RolledValue пуст.";
            return false;
        }

        values = ParseRollValues(raw);
        if (values.Count == 0)
        {
            note = $"Не удалось разобрать числа из «{raw}».";
            return false;
        }

        return true;
    }

    private static List<double> ParseRollValues(string rolled)
    {
        var list = new List<double>();
        if (rolled.Contains(';', StringComparison.Ordinal))
        {
            foreach (Match m in LabeledRollParts.Matches(rolled))
            {
                if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }

            if (list.Count > 0)
                return list;
        }

        if (double.TryParse(rolled, NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
        {
            list.Add(single);
            return list;
        }

        if (double.TryParse(rolled, NumberStyles.Float, CultureInfo.CurrentCulture, out single))
        {
            list.Add(single);
            return list;
        }

        return list;
    }

    private static string NormalizeStat(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t;
    }

    private static string FormatMin(double v) =>
        v == Math.Truncate(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Все строки целого модификатора на одном аффиксе (имя+тир из плана) удовлетворяют порогам.</summary>
    public static bool TryEvaluateWholeModifierAffix(
        CraftWholeModifierAffixData whole,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail)
    {
        detail = "";
        if (whole.Lines.Count == 0)
        {
            detail = "целый модификатор: нет строк стата.";
            return false;
        }

        var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            lib,
            expectedItemClass,
            whole.AffixType,
            whole.AffixName,
            whole.AffixTier);
        if (entry is null)
        {
            detail =
                $"В библиотеке нет записи «{whole.AffixName}» ({whole.AffixType}, T{whole.AffixTier}) для класса «{expectedItemClass}».";
            return false;
        }

        foreach (var line in whole.Lines)
        {
            var idx = ResolveStatIndexInEntry(entry, line.StatTemplate);
            if (idx < 0)
            {
                detail = $"Строка «{line.StatTemplate}» не входит в выбранную запись библиотеки «{whole.AffixName}».";
                return false;
            }

            var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, idx);
            line.EnsureMinRollsSize(slots);
            if (!TryGetRollValuesForNamedAffix(
                    item,
                    expectedItemClass,
                    whole.AffixType,
                    whole.AffixName,
                    whole.AffixTier,
                    line.StatTemplate,
                    slots,
                    out var actual,
                    out var expl))
            {
                detail = string.IsNullOrEmpty(expl) ? $"Нет модификатора «{whole.AffixName}» со строкой «{line.StatTemplate}»." : expl;
                return false;
            }

            var mins = line.GetEffectiveMinRolls(slots).ToList();
            if (!RollVectorMeetsMins(actual, mins, out var failIdx))
            {
                detail =
                    $"«{whole.AffixName}», «{line.StatTemplate}»: слот {failIdx + 1} — ниже порога (есть [{string.Join(", ", actual.Select(FormatMin))}], нужно ≥ [{string.Join(", ", mins.Select(FormatMin))}]).";
                return false;
            }
        }

        return true;
    }

    /// <summary>Для ветвления: есть ли хотя бы одна строка целого модификатора, полностью удовлетворяющая порогам.</summary>
    public static bool TryWholeModifierAnyLineFullySatisfied(
        CraftWholeModifierAffixData whole,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib)
    {
        var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            lib,
            expectedItemClass,
            whole.AffixType,
            whole.AffixName,
            whole.AffixTier);
        if (entry is null)
            return false;

        foreach (var line in whole.Lines)
        {
            var idx = ResolveStatIndexInEntry(entry, line.StatTemplate);
            if (idx < 0)
                continue;
            var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, idx);
            line.EnsureMinRollsSize(slots);
            if (!TryGetRollValuesForNamedAffix(
                    item,
                    expectedItemClass,
                    whole.AffixType,
                    whole.AffixName,
                    whole.AffixTier,
                    line.StatTemplate,
                    slots,
                    out var actual,
                    out _))
                continue;
            var mins = line.GetEffectiveMinRolls(slots).ToList();
            if (RollVectorMeetsMins(actual, mins, out _))
                return true;
        }

        return false;
    }

    private static int ResolveStatIndexInEntry(AffixLibraryEntry entry, string statTemplate)
    {
        var idx = AffixCraftPatternBuilder.GetStatIndex(entry, statTemplate);
        if (idx >= 0)
            return idx;
        for (var i = 0; i < entry.AffixStats.Count; i++)
        {
            if (StatLineMatchesTemplate(entry.AffixStats[i], statTemplate) ||
                StatLineMatchesTemplate(statTemplate, entry.AffixStats[i]))
                return i;
        }

        return -1;
    }
}
