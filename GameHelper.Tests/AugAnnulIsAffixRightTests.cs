using System.IO;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Проверяет логику IsAffixRight из AugAnnulCraftService через публичный TryEvaluate
/// на частичных планах (точно так же, как делает FilterClauseForType внутри сервиса).
///
/// Сценарий: Body Armours, крафт Aug+Annul.
/// Условие содержит COUNT-клоз с混смешанными членами (Prefix и Suffix),
/// что соответствует реальным данным из settings.json пользователя.
/// </summary>
public sealed class AugAnnulIsAffixRightTests
{
    static AugAnnulIsAffixRightTests()
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

    // ── Тестовые предметы (clipboard text) ──────────────────────────────────────────

    // Magic Body Armour: RIGHT prefix (Unfaltering T1, 103% ES) + WRONG suffix (of Immortality T2)
    private const string RightPrefixWrongSuffix = """
        Item Class: Body Armours
        Rarity: Magic
        Unfaltering Vile Robe of Immortality
        --------
        Energy Shield: 342 (augmented)
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Unfaltering" (Tier: 1) — Energy Shield }
        103(101-110)% increased Energy Shield
        { Suffix Modifier "of Immortality" (Tier: 2) — Life }
        32.1(29.1-33) Life Regeneration per second
        """;

    // Magic Body Armour: WRONG prefix (Vigorous T3) + RIGHT suffix (of the Ice T2, 38% Cold)
    private const string WrongPrefixRightSuffix = """
        Item Class: Body Armours
        Rarity: Magic
        Vigorous Vile Robe of the Ice
        --------
        Energy Shield: 171
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Vigorous" (Tier: 3) — Life }
        +180(175-189) to maximum Life
        { Suffix Modifier "of the Ice" (Tier: 2) — Elemental, Cold, Resistance }
        +38(36-40)% to Cold Resistance
        """;

    // Magic Body Armour: RIGHT prefix (Unfaltering T1) + RIGHT suffix (of the Ice T2, 38%)
    private const string RightPrefixRightSuffix = """
        Item Class: Body Armours
        Rarity: Magic
        Unfaltering Vile Robe of the Ice
        --------
        Energy Shield: 342 (augmented)
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Unfaltering" (Tier: 1) — Energy Shield }
        103(101-110)% increased Energy Shield
        { Suffix Modifier "of the Ice" (Tier: 2) — Elemental, Cold, Resistance }
        +38(36-40)% to Cold Resistance
        """;

    // Magic Body Armour: только prefix, без suffix (состояние R,E)
    private const string OnlyRightPrefix = """
        Item Class: Body Armours
        Rarity: Magic
        Unfaltering Vile Robe
        --------
        Energy Shield: 342 (augmented)
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Unfaltering" (Tier: 1) — Energy Shield }
        103(101-110)% increased Energy Shield
        """;

    // Magic Body Armour: WRONG prefix + WRONG suffix
    private const string WrongPrefixWrongSuffix = """
        Item Class: Body Armours
        Rarity: Magic
        Vigorous Vile Robe of Immortality
        --------
        Energy Shield: 171
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Vigorous" (Tier: 3) — Life }
        +180(175-189) to maximum Life
        { Suffix Modifier "of Immortality" (Tier: 2) — Life }
        32.1(29.1-33) Life Regeneration per second
        """;

    // ── Построитель планов ────────────────────────────────────────────────────────────

    private static CraftWholeModifierAffixData MakeMember(
        string affixType, string affixName, List<string> selectedNames,
        int tier, string stat, double minRoll) =>
        new()
        {
            AffixType = affixType,
            AffixName = affixName,
            SelectedAffixNames = selectedNames,
            AffixTier = tier,
            Lines = new List<CraftWholeModifierLine>
            {
                new() { StatTemplate = stat, MinRoll = minRoll, MinRolls = new List<double> { minRoll } },
            },
        };

    /// <summary>
    /// Реальный план из settings.json (COUNT≥1, 7 членов, смешанные типы).
    /// Prefix-члены: Resplendent T1, Unfaltering T1, Princess'/Queen's T2.
    /// Suffix-члены: of Haast/of the Ice T2, of Magma/of Tzteosh T2, of Ephij/of the Lightning T2.
    /// </summary>
    private static CraftConditionPlan BuildRealPlan(int minMatchCount = 1) => new()
    {
        ExpectedItemClass = "Body Armours",
        OrAlternatives =
        {
            new CraftAndGroup
            {
                Clauses =
                {
                    new CraftClause
                    {
                        Kind = CraftClauseKind.Count,
                        Count = new CraftCountAffixData
                        {
                            MinMatchCount = minMatchCount,
                            Members =
                            {
                                MakeMember("Prefix Modifier", "Resplendent", ["Resplendent"], 1, "+# to maximum Energy Shield", 91),
                                MakeMember("Prefix Modifier", "Unfaltering", ["Unfaltering"], 1, "#% increased Energy Shield", 101),
                                MakeMember("Prefix Modifier", "Princess'",   ["Princess'", "Queen's"], 2, "+# to Spirit", 54),
                                MakeMember("Suffix Modifier", "of Haast",    ["of Haast", "of the Ice"],           2, "+#% to Cold Resistance",      36),
                                MakeMember("Suffix Modifier", "of Magma",    ["of Magma", "of Tzteosh"],           2, "+#% to Fire Resistance",      36),
                                MakeMember("Suffix Modifier", "of Ephij",    ["of Ephij", "of the Lightning"],     2, "+#% to Lightning Resistance", 36),
                            },
                        },
                    },
                },
            },
        },
    };

    /// <summary>
    /// Строит частичный план (только prefix- или suffix-члены из COUNT, minMatchCount=1).
    /// Это точная копия того, что делает AugAnnulCraftService.FilterClauseForType внутри IsAffixRight.
    /// </summary>
    private static CraftConditionPlan BuildPartialPlan(CraftConditionPlan fullPlan, bool forPrefix)
    {
        var filteredClauses = new List<CraftClause>();
        foreach (var alt in fullPlan.OrAlternatives)
        {
            foreach (var clause in alt.Clauses)
            {
                if (clause.Kind != CraftClauseKind.Count || clause.Count is null)
                    continue;

                var matchingMembers = clause.Count.Members
                    .Where(m => IsTypeMatch(m.AffixType, forPrefix))
                    .ToList();

                if (matchingMembers.Count == 0)
                    continue;

                filteredClauses.Add(new CraftClause
                {
                    Kind = CraftClauseKind.Count,
                    Count = new CraftCountAffixData { Members = matchingMembers, MinMatchCount = 1 }
                });
            }
        }

        return new CraftConditionPlan
        {
            ExpectedItemClass = fullPlan.ExpectedItemClass,
            OrAlternatives = { new CraftAndGroup { Clauses = filteredClauses } }
        };
    }

    private static bool IsTypeMatch(string affixType, bool isPrefix) =>
        isPrefix
            ? string.Equals(affixType, "Prefix Modifier", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(affixType, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase)
            : string.Equals(affixType, "Suffix Modifier", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(affixType, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase);

    // ── COUNT≥1 тесты (стандартный план) ──────────────────────────────────────────────

    [Fact]
    public void Count1_RightPrefix_WrongSuffix_FullPlanMatches()
    {
        var item = ItemParser.Parse(RightPrefixWrongSuffix);
        Assert.True(CraftConditionEvaluator.TryEvaluate(BuildRealPlan(), item, out var expl),
            "COUNT≥1: Unfaltering T1 должен дать match. " + expl);
    }

    [Fact]
    public void Count1_WrongPrefix_RightSuffix_FullPlanMatches()
    {
        var item = ItemParser.Parse(WrongPrefixRightSuffix);
        Assert.True(CraftConditionEvaluator.TryEvaluate(BuildRealPlan(), item, out var expl),
            "COUNT≥1: of the Ice T2 38% ≥ 36 должен дать match. " + expl);
    }

    [Fact]
    public void Count1_WrongPrefixWrongSuffix_FullPlanNoMatch()
    {
        var item = ItemParser.Parse(WrongPrefixWrongSuffix);
        Assert.False(CraftConditionEvaluator.TryEvaluate(BuildRealPlan(), item, out _),
            "COUNT≥1: Vigorous + of Immortality — ни один не в условии.");
    }

    // ── IsAffixRight-логика: частичные планы для prefix/suffix ───────────────────────

    [Fact]
    public void IsAffixRight_RightPrefixItem_PrefixPartialPlanMatches()
    {
        var plan = BuildRealPlan();
        var item = ItemParser.Parse(RightPrefixWrongSuffix);
        var prefixPartial = BuildPartialPlan(plan, forPrefix: true);

        Assert.True(CraftConditionEvaluator.TryEvaluate(prefixPartial, item, out var expl),
            "Prefix partial: Unfaltering T1 должен быть RIGHT. " + expl);
    }

    [Fact]
    public void IsAffixRight_RightPrefixItem_SuffixPartialPlanNoMatch()
    {
        var plan = BuildRealPlan();
        var item = ItemParser.Parse(RightPrefixWrongSuffix);
        var suffixPartial = BuildPartialPlan(plan, forPrefix: false);

        Assert.False(CraftConditionEvaluator.TryEvaluate(suffixPartial, item, out _),
            "Suffix partial: of Immortality T2 не в условии → suffix WRONG.");
    }

    [Fact]
    public void IsAffixRight_WrongPrefixRightSuffix_PrefixPartialNoMatch()
    {
        var plan = BuildRealPlan();
        var item = ItemParser.Parse(WrongPrefixRightSuffix);
        var prefixPartial = BuildPartialPlan(plan, forPrefix: true);

        Assert.False(CraftConditionEvaluator.TryEvaluate(prefixPartial, item, out _),
            "Prefix partial: Vigorous T3 не в условии → prefix WRONG.");
    }

    [Fact]
    public void IsAffixRight_WrongPrefixRightSuffix_SuffixPartialMatches()
    {
        var plan = BuildRealPlan();
        var item = ItemParser.Parse(WrongPrefixRightSuffix);
        var suffixPartial = BuildPartialPlan(plan, forPrefix: false);

        Assert.True(CraftConditionEvaluator.TryEvaluate(suffixPartial, item, out var expl),
            "Suffix partial: of the Ice T2 38% ≥ 36 → suffix RIGHT. " + expl);
    }

    [Fact]
    public void IsAffixRight_OnlyRightPrefix_PrefixPartialMatches_SuffixPartialEmpty()
    {
        var plan = BuildRealPlan();
        var item = ItemParser.Parse(OnlyRightPrefix);

        var prefixPartial = BuildPartialPlan(plan, forPrefix: true);
        Assert.True(CraftConditionEvaluator.TryEvaluate(prefixPartial, item, out var expl),
            "Prefix partial: Unfaltering T1 (один аффикс) → RIGHT. " + expl);

        var suffixPartial = BuildPartialPlan(plan, forPrefix: false);
        Assert.False(CraftConditionEvaluator.TryEvaluate(suffixPartial, item, out _),
            "Suffix partial: суффикса нет вовсе → NOT RIGHT.");
    }

    // ── COUNT≥2 тесты: оба типа нужны ─────────────────────────────────────────────────

    /// <summary>
    /// COUNT≥2 с теми же 6 членами (3 prefix + 3 suffix).
    /// На Magic-предмете максимум 1 prefix + 1 suffix → для выполнения COUNT≥2
    /// нужны оба: 1 правильный prefix + 1 правильный suffix.
    /// </summary>
    [Fact]
    public void Count2_RightPrefixWrongSuffix_FullPlanNoMatch()
    {
        var item = ItemParser.Parse(RightPrefixWrongSuffix);
        Assert.False(CraftConditionEvaluator.TryEvaluate(BuildRealPlan(minMatchCount: 2), item, out _),
            "COUNT≥2: только prefix совпадает (1 из 2 нужных) → false.");
    }

    [Fact]
    public void Count2_WrongPrefixRightSuffix_FullPlanNoMatch()
    {
        var item = ItemParser.Parse(WrongPrefixRightSuffix);
        Assert.False(CraftConditionEvaluator.TryEvaluate(BuildRealPlan(minMatchCount: 2), item, out _),
            "COUNT≥2: только suffix совпадает (1 из 2 нужных) → false.");
    }

    [Fact]
    public void Count2_BothRight_FullPlanMatches()
    {
        var item = ItemParser.Parse(RightPrefixRightSuffix);
        Assert.True(CraftConditionEvaluator.TryEvaluate(BuildRealPlan(minMatchCount: 2), item, out var expl),
            "COUNT≥2: Unfaltering T1 + of the Ice T2 → оба совпадают → true. " + expl);
    }

    /// <summary>
    /// Ключевой тест алгоритма: даже при COUNT≥2 в оригинале,
    /// IsAffixRight для ОТДЕЛЬНОГО prefix/suffix использует MinMatchCount=1 в частичном плане.
    /// Это позволяет алгоритму Aug+Annul правильно оценить каждый аффикс независимо.
    /// </summary>
    [Fact]
    public void Count2_IsAffixRight_UsesMinMatchCount1_ForEachTypeIndependently()
    {
        var plan = BuildRealPlan(minMatchCount: 2);
        var itemRP_WS = ItemParser.Parse(RightPrefixWrongSuffix);
        var itemWP_RS = ItemParser.Parse(WrongPrefixRightSuffix);

        var prefixPartial = BuildPartialPlan(plan, forPrefix: true);
        var suffixPartial = BuildPartialPlan(plan, forPrefix: false);

        // Unfaltering prefix → RIGHT (даже хотя полное COUNT≥2 не выполняется из-за wrong suffix)
        Assert.True(CraftConditionEvaluator.TryEvaluate(prefixPartial, itemRP_WS, out var p1),
            "Prefix partial для COUNT≥2 плана должен дать true для правильного prefix. " + p1);

        // of Immortality suffix → WRONG
        Assert.False(CraftConditionEvaluator.TryEvaluate(suffixPartial, itemRP_WS, out _),
            "Suffix partial: of Immortality не в условии.");

        // Vigorous prefix → WRONG
        Assert.False(CraftConditionEvaluator.TryEvaluate(prefixPartial, itemWP_RS, out _),
            "Prefix partial: Vigorous не в условии.");

        // of the Ice suffix → RIGHT
        Assert.True(CraftConditionEvaluator.TryEvaluate(suffixPartial, itemWP_RS, out var s2),
            "Suffix partial для COUNT≥2 плана должен дать true для правильного suffix. " + s2);
    }
}
