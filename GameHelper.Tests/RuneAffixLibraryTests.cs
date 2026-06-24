using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Проверяет все 71 рунных семейств аффиксов (по одному слабейшему тиру на семейство),
/// а также разрешение коллизии двух аффиксов с одинаковым именем и тиром.
/// </summary>
public sealed class RuneAffixLibraryTests
{
    static RuneAffixLibraryTests()
    {
        RuneAffixLibrary.ReloadFromDisk(FindRepoFile("rune_affix_overrides.json"));
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

    // Representative item class for each rune group.
    private static readonly Dictionary<string, string> GroupItemClass = new()
    {
        ["destruction"]  = "Bows",
        ["chronomancy"]  = "Boots",
        ["marksman"]     = "Gloves",
        ["decay"]        = "Gloves",
        ["soul"]         = "Body Armours",
        ["berserking"]   = "Helmets",
    };

    private static readonly Regex ParenRange = new(
        @"\((\d+(?:\.\d+)?)[–\-](\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BareRollNumber = new(
        @"(?<![(\[\d\-–.])\b(\d+)\b(?!\.\d)(?!\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string FormatStatAtMinRoll(string libStat)
    {
        var s = ParenRange.Replace(libStat, m => $"{m.Groups[1].Value}({m.Groups[1].Value}-{m.Groups[2].Value})");
        s = BareRollNumber.Replace(s, m => $"{m.Groups[1].Value}({m.Groups[1].Value}-{m.Groups[1].Value})");
        return s;
    }

    private static string BuildClipboard(string itemClass, string affixType, string affixName, int affixTier, IList<string> stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Item Class: {itemClass}");
        sb.AppendLine("Rarity: Magic");
        sb.AppendLine("Test Item");
        sb.AppendLine("--------");
        sb.AppendLine($"{{ {affixType} \"{affixName}\" (Tier: {affixTier}) }}");
        foreach (var s in stats)
            sb.AppendLine(FormatStatAtMinRoll(s));
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Для каждого рунного семейства (уникальный нормализованный первый стат внутри группы)
    /// берём слабейший тир — наибольший номер тира (наименьший ilvl).
    /// </summary>
    public static IEnumerable<object[]> WeakestTierPerRuneFamily()
    {
        foreach (var (group, ic) in GroupItemClass)
        {
            var entries = RuneAffixLibrary.GetEntries(group, ic);

            // Group by (affixType, normalised first stat) → pick highest AffixTier number (weakest).
            var families = new Dictionary<(string Type, string NormStat), AffixLibraryEntry>();
            foreach (var e in entries)
            {
                var key = (e.AffixType, CraftAffixCascadeHelper.NormalizeStatToTemplate(e.AffixStats.FirstOrDefault() ?? ""));
                if (!families.TryGetValue(key, out var cur) || e.AffixTier > cur.AffixTier)
                    families[key] = e;
            }

            foreach (var entry in families.Values.OrderBy(e => e.AffixName).ThenBy(e => e.AffixTier))
            {
                var displayName = $"{group}/{entry.AffixName}/T{entry.AffixTier}/{entry.AffixStats[0][..Math.Min(40, entry.AffixStats[0].Length)]}";
                var clipboard = BuildClipboard(ic, entry.AffixType, entry.AffixName, entry.AffixTier, entry.AffixStats);
                yield return [displayName, group, ic, entry.AffixType, entry.AffixName, entry.AffixTier, entry.AffixStats.ToArray(), clipboard];
            }
        }
    }

    /// <summary>
    /// Слабейший тир каждого рунного семейства: предмет парсится, аффикс находится,
    /// каждый стат матчится через <see cref="ParsedItemCraftEvaluator.TryGetRollValuesForNamedAffix"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(WeakestTierPerRuneFamily))]
    public void WeakestTier_EachStat_FoundByEvaluator(
        string displayName,
        string group,
        string itemClass,
        string affixType,
        string affixName,
        int affixTier,
        string[] stats,
        string clipboard)
    {
        _ = group; // used only for display name generation

        var item = ItemParser.Parse(clipboard);

        Assert.True(item!.IsValid,
            $"[{displayName}] Item не распознан. Clipboard:\n{clipboard}");

        var targetAffix = item.Affixes.FirstOrDefault(a =>
            string.Equals(a.Name, affixName, StringComparison.Ordinal) && a.Tier == affixTier);

        Assert.True(targetAffix is not null,
            $"[{displayName}] Аффикс \"{affixName}\" T{affixTier} не найден. " +
            $"Аффиксы: {string.Join(", ", item.Affixes.Select(a => $"\"{a.Name}\" T{a.Tier}"))}");

        Assert.True(stats.Length == targetAffix!.EffectDetails.Count,
            $"[{displayName}] Ожидалось {stats.Length} строк стата, " +
            $"найдено {targetAffix.EffectDetails.Count}. " +
            $"Строки: {string.Join(" | ", targetAffix.EffectDetails.Select(d => d.StatText))}");

        for (var i = 0; i < stats.Length; i++)
        {
            var template = CraftAffixCascadeHelper.NormalizeStatToTemplate(stats[i]);
            var rollCount = ParenRange.Matches(FormatStatAtMinRoll(stats[i])).Count;

            if (rollCount == 0)
                continue; // нет числовых перекатов — только проверяем что аффикс найден выше

            var found = ParsedItemCraftEvaluator.TryGetRollValuesForNamedAffix(
                item, itemClass, affixType, affixName, affixTier,
                template, rollCount, out var rolls, out var explanation);

            Assert.True(found,
                $"[{displayName}] Стат [{i}] \"{template}\" не найден. " +
                $"Причина: {explanation}. Raw: \"{stats[i]}\"");

            Assert.True(rolls.Count >= 1,
                $"[{displayName}] Стат [{i}] найден, но переката нет.");

            var minMatch = ParenRange.Match(stats[i]);
            if (minMatch.Success && double.TryParse(minMatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var expectedMin))
            {
                Assert.True(rolls.Any(r => Math.Abs(r - expectedMin) < 0.001),
                    $"[{displayName}] Стат [{i}] \"{template}\": " +
                    $"ожидался минимальный перекат {expectedMin} среди [{string.Join(", ", rolls)}]. " +
                    $"Raw: \"{stats[i]}\"");
            }
        }
    }

    /// <summary>
    /// Два аффикса с одинаковым именем и тиром (разные статы) на одном предмете —
    /// каждый должен находиться по своему стату.
    /// Воспроизводит сценарий Vorana's Carnage на шлеме: несколько "of the Berserker" T1.
    /// </summary>
    [Fact]
    public void TwoSameNameSameTier_BothFoundByEvaluator()
    {
        const string itemClass = "Helmets";
        const string affixType = "Suffix Modifier";
        const string affixName = "of the Berserker";
        const int tier = 1;

        // Two distinct T1 "of the Berserker" suffixes that can coexist on the same item.
        const string stat1 = "(40–60)% chance to build an additional Combo on Hit";
        const string stat2 = "Warcry Skills have (30–50)% increased Area of Effect";

        var clipboard = new StringBuilder()
            .AppendLine($"Item Class: {itemClass}")
            .AppendLine("Rarity: Rare")
            .AppendLine("Test Helm")
            .AppendLine("--------")
            .AppendLine($"{{ {affixType} \"{affixName}\" (Tier: {tier}) }}")
            .AppendLine(FormatStatAtMinRoll(stat1))
            .AppendLine($"{{ {affixType} \"{affixName}\" (Tier: {tier}) }}")
            .AppendLine(FormatStatAtMinRoll(stat2))
            .ToString().TrimEnd();

        var item = ItemParser.Parse(clipboard);
        Assert.True(item!.IsValid, $"Item не распознан:\n{clipboard}");
        Assert.Equal(2, item.Affixes.Count(a => a.Name == affixName && a.Tier == tier));

        var template1 = CraftAffixCascadeHelper.NormalizeStatToTemplate(stat1);
        var template2 = CraftAffixCascadeHelper.NormalizeStatToTemplate(stat2);

        var found1 = ParsedItemCraftEvaluator.TryGetRollValuesForNamedAffix(
            item, itemClass, affixType, affixName, tier, template1, 1, out var rolls1, out var expl1);

        Assert.True(found1, $"Первый аффикс ({stat1}) не найден. Причина: {expl1}");
        Assert.True(rolls1.Any(r => Math.Abs(r - 40) < 0.001),
            $"Ожидался минимальный перекат 40, получено [{string.Join(", ", rolls1)}]");

        var found2 = ParsedItemCraftEvaluator.TryGetRollValuesForNamedAffix(
            item, itemClass, affixType, affixName, tier, template2, 1, out var rolls2, out var expl2);

        Assert.True(found2, $"Второй аффикс ({stat2}) не найден. Причина: {expl2}");
        Assert.True(rolls2.Any(r => Math.Abs(r - 30) < 0.001),
            $"Ожидался минимальный перекат 30, получено [{string.Join(", ", rolls2)}]");
    }
}
