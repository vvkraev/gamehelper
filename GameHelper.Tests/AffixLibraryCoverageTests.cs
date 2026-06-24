using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Охватывает все 1401 комбинаций (familyId × itemClass), используя слабейший тир каждой.
/// До фикса разбивки конкатенированных статов: 27 тестов (multi-stat семейства) падают.
/// После фикса: все 1401 проходят.
/// </summary>
public sealed class AffixLibraryCoverageTests
{
    static AffixLibraryCoverageTests()
    {
        AffixLibrary.ReloadFromDisk(FindRepoFile("affix_library.json"));
    }

    private static string FindRepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, name);
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"{name} not found above {AppContext.BaseDirectory}");
    }

    // Разбивает конкатенированный стат на отдельные части.
    // "+(17–20) to maximum Mana" — один стат, не разбивается.
    // "(15–19)% increased Spell Damage+(17–20) to maximum Mana" — два стата.
    private static readonly Regex SplitRe = new(
        @"(?<=[a-zA-Z\d])(?=\+[\(\d])|(?<=[a-zA-Z])(?=\(\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string[] SplitStat(string stat) =>
        SplitRe.Split(stat)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

    // Превращает библиотечный стат с диапазоном в строку клипборда с минимальным перекатом.
    // "+(17–20) to maximum Mana" → "+17(17-20) to maximum Mana"
    // "(15–19)% increased Spell Damage" → "15(15-19)% increased Spell Damage"
    // "Adds (1–2) to (3–4) Cold Damage" → "Adds 1(1-2) to 3(3-4) Cold Damage"
    // "Adds 1 to (4–6) Lightning Damage" → "Adds 1(1-1) to 4(4-6) Lightning Damage"
    // "(5–5.9)% leech" → "5(5-5.9)% leech"  (decimal ranges supported)
    private static readonly Regex ParenRange = new(
        @"\((\d+(?:\.\d+)?)[–\-](\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches standalone integers not preceded by '(', '[', digit, '-', en-dash, or '.'
    // and not followed by a decimal part '.N' or by '('
    // Used to wrap fixed-minimum values (e.g. the '1' in "Adds 1 to (4–6)") as N(N-N)
    private static readonly Regex BareRollNumber = new(
        @"(?<![(\[\d\-–.])\b(\d+)\b(?!\.\d)(?!\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches N(min-max) patterns (including decimal) in the formatted clipboard stat
    private static readonly Regex RollPattern = new(
        @"\d+(?:\.\d+)?\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string FormatStatAtMinRoll(string libStat)
    {
        // Step 1: replace ALL (MIN–MAX) ranges with MIN(MIN-MAX)
        var s = ParenRange.Replace(libStat, m2 => $"{m2.Groups[1].Value}({m2.Groups[1].Value}-{m2.Groups[2].Value})");
        // Step 2: wrap remaining bare standalone integers N as N(N-N) so ItemParser treats them as rolls
        s = BareRollNumber.Replace(s, m2 => $"{m2.Groups[1].Value}({m2.Groups[1].Value}-{m2.Groups[1].Value})");
        return s;
    }

    private static string BuildClipboard(string itemClass, AffixLibraryEntry entry, string[] splitStats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Item Class: {itemClass}");
        sb.AppendLine("Rarity: Magic");
        sb.AppendLine("Test Item");
        sb.AppendLine("--------");
        sb.AppendLine($"{{ {entry.AffixType} \"{entry.AffixName}\" (Tier: {entry.AffixTier}) — Test }}");
        foreach (var s in splitStats)
            sb.AppendLine(FormatStatAtMinRoll(s));
        return sb.ToString().TrimEnd();
    }

    // Генерирует тест-данные: для каждого (familyId × itemClass) берём слабейший тир.
    public static IEnumerable<object[]> WeakestTierPerFamilyAndClass()
    {
        var lib = AffixLibrary.GetEntries();

        // Группируем по (familyId, itemClass) → слабейший тир (наибольший номер тира)
        var weakest = new Dictionary<(string family, string cls), AffixLibraryEntry>();
        foreach (var e in lib)
        {
            if (string.IsNullOrEmpty(e.FamilyId))
                continue;
            foreach (var ic in e.ItemClasses)
            {
                var key = (e.FamilyId, ic);
                if (!weakest.TryGetValue(key, out var cur) || e.AffixTier > cur.AffixTier)
                    weakest[key] = e;
            }
        }

        foreach (var ((family, itemClass), entry) in weakest.OrderBy(kv => kv.Key.cls).ThenBy(kv => kv.Key.family))
        {
            // After SplitConcatenatedStats, AffixStats is already split; use all parts.
            var splitStats = entry.AffixStats.ToArray();

            // Skip placeholder entries (e.g. AffixName="TBD", empty stats) — no real data to test.
            if (splitStats.All(s => string.IsNullOrWhiteSpace(s)))
                continue;
            var clipboard = BuildClipboard(itemClass, entry, splitStats);
            // Нормализуем каждую часть до формы с # для использования в условии (как это делает UI)
            var normalizedTemplates = splitStats
                .Select(CraftAffixCascadeHelper.NormalizeStatToTemplate)
                .ToArray();

            yield return new object[]
            {
                $"{itemClass}/{family}/T{entry.AffixTier}",  // display name
                itemClass,
                entry.AffixType,
                entry.AffixName,
                entry.AffixTier,
                normalizedTemplates,
                splitStats,
                clipboard,
            };
        }
    }

    /// <summary>
    /// Для каждого стата из слабейшего тира семейства: Item парсится, аффикс находится,
    /// каждый стат матчится индивидуально через <see cref="ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStat"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(WeakestTierPerFamilyAndClass))]
    public void WeakestTier_EachSplitStat_FoundByEvaluator(
        string displayName,
        string itemClass,
        string affixType,
        string affixName,
        int affixTier,
        string[] normalizedTemplates,
        string[] rawSplitStats,
        string clipboard)
    {
        var lib = AffixLibrary.GetEntries();
        var item = ItemParser.Parse(clipboard);

        Assert.True(item.IsValid,
            $"[{displayName}] Item не распознан. Clipboard:\n{clipboard}");

        var targetAffix = item.Affixes.FirstOrDefault(a =>
            string.Equals(a.Name, affixName, StringComparison.Ordinal) &&
            a.Tier == affixTier);

        Assert.True(targetAffix is not null,
            $"[{displayName}] Аффикс \"{affixName}\" T{affixTier} не найден на предмете. " +
            $"Найденные аффиксы: {string.Join(", ", item.Affixes.Select(a => $"\"{a.Name}\" T{a.Tier}"))}");
        Assert.NotNull(targetAffix);

        Assert.True(rawSplitStats.Length == targetAffix.EffectDetails.Count,
            $"[{displayName}] Ожидалось {rawSplitStats.Length} строк стата, " +
            $"найдено {targetAffix.EffectDetails.Count}. " +
            $"Строки: {string.Join(" | ", targetAffix.EffectDetails.Select(d => d.StatText))}");

        for (var i = 0; i < normalizedTemplates.Length; i++)
        {
            var template = normalizedTemplates[i];

            // Count N(min-max) patterns in the formatted clipboard stat to determine expected roll slots.
            // FormatStatAtMinRoll expands all ranges and bare fixed values → each becomes N(a-b).
            var formatted = FormatStatAtMinRoll(rawSplitStats[i]);
            var rollCount = RollPattern.Matches(formatted).Count;

            // Stats with no numeric rolls (e.g. "Map contains an additional Essence") —
            // TryGetRollValuesForTypeAndStat requires at least one roll, so just verify the
            // template exists in the library via GetCandidateNameTiers.
            if (rollCount == 0)
            {
                var candidates = CraftAffixCascadeHelper.GetCandidateNameTiers(itemClass, affixType, template, lib);
                Assert.True(candidates.Count > 0,
                    $"[{displayName}] Стат [{i}] \"{template}\" — нет кандидатов в библиотеке (roll-less стат). " +
                    $"Raw stat: \"{rawSplitStats[i]}\"");
                continue;
            }

            var totalRolls = rollCount;

            var found = ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStat(
                item, itemClass, affixType, template, lib, totalRolls,
                out var rolls, out var explanation);

            Assert.True(found,
                $"[{displayName}] Стат [{i}] \"{template}\" не найден. " +
                $"Причина: {explanation}. Raw stat: \"{rawSplitStats[i]}\"");

            Assert.True(rolls.Count >= 1,
                $"[{displayName}] Стат [{i}] найден, но перекат не извлечён.");

            // Verify the first range's minimum appears among the returned rolls
            var minRollMatch = ParenRange.Match(rawSplitStats[i]);
            if (minRollMatch.Success && double.TryParse(minRollMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var expectedMin))
            {
                Assert.True(rolls.Any(r => Math.Abs(r - expectedMin) < 0.001),
                    $"[{displayName}] Стат [{i}] \"{template}\": " +
                    $"ожидался перекат {expectedMin} среди [{string.Join(", ", rolls)}]. " +
                    $"Raw stat: \"{rawSplitStats[i]}\".");
            }
        }
    }
}
