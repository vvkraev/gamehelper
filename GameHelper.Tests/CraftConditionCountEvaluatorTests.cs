using System.IO;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class CraftConditionCountEvaluatorTests
{
    static CraftConditionCountEvaluatorTests()
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

    private const string GriefPlus5Clipboard = """
        Item Class: Wands
        Rarity: Rare
        Test Wand
        --------
        { Fractured Prefix Modifier "Runic" (Tier: 1) — Damage, Caster }
        106(105-119)% increased Spell Damage
        { Suffix Modifier "of Grief" (Tier: 1) — Physical, Caster, Gem }
        +5 to Level of all Physical Spell Skills
        --------
        Fractured Item
        """;

    private static CraftConditionPlan BuildUserPlan(double frostbiteMinRoll = 6)
    {
        CraftSingleAffixData Member(
            string name,
            string stat,
            double minRoll) =>
            new()
            {
                AffixType = "Suffix Modifier",
                AffixName = name,
                SelectedAffixNames = new List<string> { name },
                AffixTier = 1,
                StatTemplate = stat,
                MinRoll = minRoll,
                MinRolls = new List<double> { minRoll },
            };

        return new CraftConditionPlan
        {
            ExpectedItemClass = "Wands",
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
                                MinMatchCount = 1,
                                Members =
                                {
                                    Member("of Frostbite", "+ to Level of all Cold Spell Skills", 6),
                                    Member("of Inferno", "+ to Level of all Fire Spell Skills", 6),
                                    Member("of Grief", "+ to Level of all Physical Spell Skills", 6),
                                    Member("of the Wizard", "+ to Level of all Spell Skills", 5),
                                    Member("of Thunder", "+ to Level of all Lightning Spell Skills", 6),
                                },
                            },
                        },
                    },
                },
                new CraftAndGroup
                {
                    Clauses =
                    {
                        new CraftClause
                        {
                            Kind = CraftClauseKind.Single,
                            Single = Member("of Grief", "+ to Level of all Physical Spell Skills", 6),
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void Count_with_min_roll_above_library_max_does_not_match_plus5_grief()
    {
        var item = ItemParser.Parse(GriefPlus5Clipboard);
        var plan = BuildUserPlan();
        Assert.False(CraftConditionEvaluator.TryEvaluate(plan, item, out _));
        Assert.False(
            CraftConditionEvaluator.TryEvaluateSingleAffixClause(
                plan.OrAlternatives[0].Clauses[0].Count!.Members[2],
                item,
                plan.ExpectedItemClass,
                out _));
    }

    [Fact]
    public void Count_matches_after_clamp_to_library_max_roll_5()
    {
        var item = ItemParser.Parse(GriefPlus5Clipboard);
        var plan = BuildUserPlan();
        CraftConditionPlanNormalizer.NormalizeInPlace(plan, AffixLibrary.GetEntries());
        Assert.True(CraftConditionEvaluator.TryEvaluate(plan, item, out var expl));
        Assert.Contains("COUNT", expl, StringComparison.Ordinal);
    }

    // ── Тесты на матчинг шаблонов с '#'-плейсхолдером ──────────────────────────────

    /// <summary>
    /// Парсер для «+3 to Level…» кладёт число в RolledValue, в StatText остаётся «+ to Level…».
    /// StatLineMatchesTemplate должен матчить «+ to Level…» с шаблоном «+# to Level…».
    /// Регрессия: до фикса возвращал false → условие крафта не срабатывало.
    /// </summary>
    [Theory]
    [InlineData("+ to Level of all Spell Skills",     "+# to Level of all Spell Skills")]
    [InlineData("+ to Level of all Melee Skills",     "+# to Level of all Melee Skills")]
    [InlineData("+ to Level of all Projectile Skills","+# to Level of all Projectile Skills")]
    [InlineData("+ to Spirit",                        "+# to Spirit")]
    [InlineData("+ to Intelligence",                  "+# to Intelligence")]
    [InlineData("% increased Critical Hit Chance",    "#% increased Critical Hit Chance")]
    public void StatLineMatchesTemplate_HashPlaceholder_MatchesParsedStat(string parsedStat, string template)
    {
        Assert.True(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsedStat, template),
            $"Ожидали совпадение: parsedStat='{parsedStat}' template='{template}'");
    }

    [Theory]
    [InlineData("+ to Level of all Spell Skills",  "+# to Level of all Melee Skills")]
    [InlineData("+ to maximum Life",               "+# to maximum Mana")]
    public void StatLineMatchesTemplate_HashPlaceholder_DoesNotMatchDifferentStat(string parsedStat, string template)
    {
        Assert.False(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsedStat, template),
            $"Ожидали НЕ совпадение: parsedStat='{parsedStat}' template='{template}'");
    }

    // ── Тест на мультиролл-стат «Adds X to Y …» ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Adds X to Y Cold damage to Attacks", "Adds # to # Cold damage to Attacks")]
    [InlineData("Adds X to Y fire Damage to Attacks", "Adds # to # fire Damage to Attacks")]
    [InlineData("X to Y Added Cold Damage",           "# to # Added Cold Damage")]
    public void StatLineMatchesTemplate_MultiRoll_LetterPlaceholders_MatchHashTemplate(string parsed, string template)
    {
        Assert.True(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsed, template),
            $"parsedStat='{parsed}' template='{template}'");
    }

    [Theory]
    [InlineData("Adds X to Y Cold damage to Attacks", "Adds # to # Fire damage to Attacks")]
    public void StatLineMatchesTemplate_MultiRoll_LetterPlaceholders_DoesNotMatchDifferentStat(string parsed, string template)
    {
        Assert.False(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsed, template),
            $"parsedStat='{parsed}' template='{template}'");
    }

    // Точный сценарий Попытки 125: кольцо с Entombing T1 «Adds 22(21-24) to 34(32-37) Cold damage to Attacks»,
    // условие COUNT≥1 с шаблоном «Adds # to # Cold damage to Attacks», minRoll=21.
    private const string EntombingRingClipboard = """
        Item Class: Rings
        Rarity: Rare
        Entropy Knot
        Refined Breach Ring
        --------
        Requires: Level 60
        --------
        Item Level: 75
        --------
        { Implicit Modifier }
        +25% to Maximum Quality — Unscalable Value
        --------
        { Prefix Modifier "Entombing" (Tier: 1) — Damage, Elemental, Cold, Attack }
        Adds 22(21-24) to 34(32-37) Cold damage to Attacks
        """;

    [Fact]
    public void Count_MultiRoll_EntombingRingT1_MinRoll21_Matches()
    {
        var item = ItemParser.Parse(EntombingRingClipboard);

        CraftSingleAffixData Member(string name, string stat, double minRoll) => new()
        {
            AffixType = "Prefix Modifier",
            AffixName = name,
            SelectedAffixNames = new List<string> { name },
            AffixTier = 1,
            StatTemplate = stat,
            MinRoll = minRoll,
            MinRolls = new List<double> { minRoll },
        };

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Rings",
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
                                MinMatchCount = 1,
                                Members =
                                {
                                    Member("Entombing", "Adds # to # Cold damage to Attacks", 21),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Должно сработать: Entombing T1 roll=22 ≥ 21. Объяснение: {expl}");
    }

    [Fact]
    public void Count_MultiRoll_EntombingRingT1_MinRoll23_DoesNotMatch()
    {
        var item = ItemParser.Parse(EntombingRingClipboard);

        CraftSingleAffixData Member(string name, string stat, double minRoll) => new()
        {
            AffixType = "Prefix Modifier",
            AffixName = name,
            SelectedAffixNames = new List<string> { name },
            AffixTier = 1,
            StatTemplate = stat,
            MinRoll = minRoll,
            MinRolls = new List<double> { minRoll },
        };

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Rings",
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
                                MinMatchCount = 1,
                                Members =
                                {
                                    Member("Entombing", "Adds # to # Cold damage to Attacks", 23),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out _),
            "roll=22 < 23, не должно совпасть");
    }

    // Точный сценарий из лога: амулет с «+3 to Level of all Spell Skills», условие COUNT с шаблоном «+# to…»
    private const string SorcererAmuletClipboard = """
        Item Class: Amulets
        Rarity: Rare
        Carrion Pendant
        Absent Amulet
        --------
        Item Level: 79
        --------
        { Suffix Modifier "of the Sorcerer" (Tier: 1) — Caster, Gem }
        +3 to Level of all Spell Skills
        """;

    [Fact]
    public void Count_HashTemplate_AmuletSpellLevel3_MinRoll3_Matches()
    {
        var item = ItemParser.Parse(SorcererAmuletClipboard);

        CraftSingleAffixData Member(string name, string stat, double minRoll) => new()
        {
            AffixType = "Suffix Modifier",
            AffixName = name,
            SelectedAffixNames = new List<string> { name },
            AffixTier = 1,
            StatTemplate = stat,
            MinRoll = minRoll,
            MinRolls = new List<double> { minRoll },
        };

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Amulets",
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
                                MinMatchCount = 1,
                                Members =
                                {
                                    Member("of Battle",         "+# to Level of all Melee Skills",      3),
                                    Member("of the Overseer",   "+# to Level of all Minion Skills",     3),
                                    Member("of the Sharpshooter","+# to Level of all Projectile Skills",3),
                                    Member("of the Sorcerer",   "+# to Level of all Spell Skills",      3),
                                    Member("of Unmaking",       "#% increased Critical Hit Chance",    35),
                                    Member("Countess'",         "+# to Spirit",                        47),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Условие должно сработать, но не сработало. Объяснение: {expl}");
        Assert.Contains("COUNT", expl, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_HashTemplate_AmuletSpellLevel3_MinRoll4_DoesNotMatch()
    {
        var item = ItemParser.Parse(SorcererAmuletClipboard);

        CraftSingleAffixData Member(string name, string stat, double minRoll) => new()
        {
            AffixType = "Suffix Modifier",
            AffixName = name,
            SelectedAffixNames = new List<string> { name },
            AffixTier = 1,
            StatTemplate = stat,
            MinRoll = minRoll,
            MinRolls = new List<double> { minRoll },
        };

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Amulets",
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
                                MinMatchCount = 1,
                                Members =
                                {
                                    Member("of the Sorcerer", "+# to Level of all Spell Skills", 4),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out _),
            "+3 не должен удовлетворять порогу ≥4");
    }
}
