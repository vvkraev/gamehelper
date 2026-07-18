using System.IO;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class CraftConditionCountEvaluatorTests
{
    static CraftConditionCountEvaluatorTests()
    {
        AffixLibrary.ReloadFromDisk(FindRepoFile("affix_library.json"));
        CraftedModLibrary.ReloadFromDisk(FindRepoFile("crafted_mods.json"));
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

    private static CraftWholeModifierAffixData WholeMember(string name, string stat, double minRoll) =>
        new()
        {
            AffixType = "Suffix Modifier",
            AffixName = name,
            SelectedAffixNames = new List<string> { name },
            AffixTier = 1,
            Lines = new List<CraftWholeModifierLine>
            {
                new() { StatTemplate = stat, MinRoll = minRoll, MinRolls = new List<double> { minRoll } },
            },
        };

    private static CraftSingleAffixData SingleMember(string name, string stat, double minRoll) =>
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

    private static CraftConditionPlan BuildUserPlan(double frostbiteMinRoll = 6)
    {
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
                                    WholeMember("of Frostbite", "+ to Level of all Cold Spell Skills", 6),
                                    WholeMember("of Inferno", "+ to Level of all Fire Spell Skills", 6),
                                    WholeMember("of Grief", "+ to Level of all Physical Spell Skills", 6),
                                    WholeMember("of the Wizard", "+ to Level of all Spell Skills", 5),
                                    WholeMember("of Thunder", "+ to Level of all Lightning Spell Skills", 6),
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
                            Single = SingleMember("of Grief", "+ to Level of all Physical Spell Skills", 6),
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
        var mem = plan.OrAlternatives[0].Clauses[0].Count!.Members[2];
        Assert.False(
            ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(
                mem, item, plan.ExpectedItemClass, AffixLibrary.GetEntries(), out _,
                mem.EffectiveWholeAffixNames()[0]));
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
                                    new CraftWholeModifierAffixData
                                    {
                                        AffixType = "Prefix Modifier",
                                        AffixName = "Entombing",
                                        SelectedAffixNames = new List<string> { "Entombing" },
                                        AffixTier = 1,
                                        Lines = new List<CraftWholeModifierLine>
                                        {
                                            new() { StatTemplate = "Adds # to # Cold damage to Attacks", MinRoll = 21, MinRolls = new List<double> { 21 } },
                                        },
                                    },
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
                                    new CraftWholeModifierAffixData
                                    {
                                        AffixType = "Prefix Modifier",
                                        AffixName = "Entombing",
                                        SelectedAffixNames = new List<string> { "Entombing" },
                                        AffixTier = 1,
                                        Lines = new List<CraftWholeModifierLine>
                                        {
                                            new() { StatTemplate = "Adds # to # Cold damage to Attacks", MinRoll = 23, MinRolls = new List<double> { 23 } },
                                        },
                                    },
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

        CraftWholeModifierAffixData WM(string name, string stat, double minRoll) => new()
        {
            AffixType = "Suffix Modifier",
            AffixName = name,
            SelectedAffixNames = new List<string> { name },
            AffixTier = 1,
            Lines = new List<CraftWholeModifierLine>
            {
                new() { StatTemplate = stat, MinRoll = minRoll, MinRolls = new List<double> { minRoll } },
            },
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
                                    WM("of Battle",              "+# to Level of all Melee Skills",      3),
                                    WM("of the Overseer",        "+# to Level of all Minion Skills",     3),
                                    WM("of the Sharpshooter",    "+# to Level of all Projectile Skills", 3),
                                    WM("of the Sorcerer",        "+# to Level of all Spell Skills",      3),
                                    WM("of Unmaking",            "#% increased Critical Hit Chance",    35),
                                    WM("Countess'",              "+# to Spirit",                        47),
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
                                    new CraftWholeModifierAffixData
                                    {
                                        AffixType = "Suffix Modifier",
                                        AffixName = "of the Sorcerer",
                                        SelectedAffixNames = new List<string> { "of the Sorcerer" },
                                        AffixTier = 1,
                                        Lines = new List<CraftWholeModifierLine>
                                        {
                                            new() { StatTemplate = "+# to Level of all Spell Skills", MinRoll = 4, MinRolls = new List<double> { 4 } },
                                        },
                                    },
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

    // ── Тесты: fractured-аффиксы не засчитываются при проверке условия крафта ─

    private const string FracturedRunicWandClipboard = """
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

    /// <summary>
    /// Руник зафиксирован (Fractured). Условие: «Руник T1, % increased Spell Damage ≥ 50».
    /// Ожидание: false — fractured-аффикс не засчитывается как результат крафта.
    /// </summary>
    [Fact]
    public void FracturedAffix_DoesNotSatisfySingleCondition()
    {
        var item = ItemParser.Parse(FracturedRunicWandClipboard);
        Assert.True(item.Affixes.Any(a => a.IsFractured && a.Name == "Runic"), "Предмет должен иметь fractured Runic");

        var plan = new CraftConditionPlan
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
                            Kind = CraftClauseKind.Single,
                            Single = new CraftSingleAffixData
                            {
                                AffixType = "Prefix Modifier",
                                AffixName = "Runic",
                                SelectedAffixNames = new List<string> { "Runic" },
                                AffixTier = 1,
                                StatTemplate = "% increased Spell Damage",
                                MinRoll = 50,
                                MinRolls = new List<double> { 50 },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Fractured-аффикс не должен засчитываться. Объяснение: {expl}");
    }

    /// <summary>
    /// Руник зафиксирован (Fractured). Условие: хотя бы 1 совпадение из Count-клоза с Руником.
    /// Ожидание: false — fractured-аффикс не засчитывается.
    /// </summary>
    [Fact]
    public void FracturedAffix_DoesNotSatisfyCountCondition()
    {
        var item = ItemParser.Parse(FracturedRunicWandClipboard);

        var plan = new CraftConditionPlan
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
                                    new CraftWholeModifierAffixData
                                    {
                                        AffixType = "Prefix Modifier",
                                        AffixName = "Runic",
                                        SelectedAffixNames = new List<string> { "Runic" },
                                        AffixTier = 1,
                                        Lines = new List<CraftWholeModifierLine>
                                        {
                                            new() { StatTemplate = "% increased Spell Damage", MinRoll = 50, MinRolls = new List<double> { 50 } },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Fractured-аффикс не должен засчитываться в Count. Объяснение: {expl}");
    }

    // ── StatLineMatchesTemplate: встроенный диапазон тира (N–M) ───────────────────────────

    /// <summary>
    /// Парсер из «Text N(min-max)% text» кладёт StatText = «Text % text» (число и диапазон исчезают).
    /// Шаблон библиотеки хранит «Text (min–max)% text».
    /// StatLineMatchesTemplate должен находить совпадение.
    ///
    /// Покрывает шесть структурных категорий:
    ///   EMBED_BEFORE_PERCENT  — «Notable … grant (5–10)% increased …»  (регрессия попытки 21)
    ///   LEAD_RANGE_BEFORE_%   — «(25–34)% increased Spell Damage»
    ///   LEAD_RANGE_SPACE      — «(1–2) Life Regeneration per second»
    ///   PLUS_RANGE_SPACE      — «+(10–14) to maximum Mana»
    ///   PLUS_RANGE_PERCENT    — «+(25–50)% Surpassing chance …»
    ///   EMBED_BEFORE_SPACE    — «Gain (2–3) Mana per enemy killed»
    /// </summary>
    [Theory]
    // EMBED_BEFORE_PERCENT — регрессия попытки 21 (Time-Lost Sapphire «of Unmaking» / «of Potency» CD)
    [InlineData(
        "Notable Passive Skills in Radius also grant % increased Critical Spell Damage Bonus",
        "Notable Passive Skills in Radius also grant (5–10)% increased Critical Spell Damage Bonus")]
    [InlineData(
        "Notable Passive Skills in Radius also grant % increased Critical Damage Bonus",
        "Notable Passive Skills in Radius also grant (5–10)% increased Critical Damage Bonus")]
    [InlineData(
        "% increased Effect of Small Passive Skills in Radius",
        "(15–25)% increased Effect of Small Passive Skills in Radius")]
    [InlineData(
        "Gain % of Damage as Extra Lightning Damage",
        "Gain (13–15)% of Damage as Extra Lightning Damage")]
    // LEAD_RANGE_BEFORE_% (уже работало через bNoRoll, остаётся покрыто)
    [InlineData("% increased Spell Damage",              "(25–34)% increased Spell Damage")]
    [InlineData("% increased Evasion Rating",            "(40–56)% increased Evasion Rating")]
    // LEAD_RANGE_SPACE
    [InlineData("Life Regeneration per second",          "(1–2) Life Regeneration per second")]
    [InlineData("Mana Regeneration per second",          "(3–5) Mana Regeneration per second")]
    // PLUS_RANGE_SPACE
    [InlineData("+ to maximum Mana",                     "+(10–14) to maximum Mana")]
    [InlineData("+ to maximum Life",                     "+(40–60) to maximum Life")]
    [InlineData("+ to Intelligence",                     "+(5–8) to Intelligence")]
    // PLUS_RANGE_PERCENT
    [InlineData(
        "+% Surpassing chance to fire an additional Arrow",
        "+(25–50)% Surpassing chance to fire an additional Arrow")]
    // EMBED_BEFORE_SPACE
    [InlineData("Gain Mana per enemy killed",            "Gain (2–3) Mana per enemy killed")]
    public void StatLineMatchesTemplate_EmbeddedTierRange_MatchesParsedStat(string parsedStat, string template)
    {
        Assert.True(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsedStat, template),
            $"parsedStat='{parsedStat}' template='{template}'");
    }

    /// <summary>Одна и та же структура с диапазоном, но разный текст стата — не должно совпадать.</summary>
    [Theory]
    [InlineData(
        "Notable Passive Skills in Radius also grant % increased Critical Spell Damage Bonus",
        "Notable Passive Skills in Radius also grant (5–10)% increased Critical Damage Bonus")]
    [InlineData("Life Regeneration per second",  "(1–2) Mana Regeneration per second")]
    [InlineData("+ to maximum Mana",             "+(10–14) to maximum Life")]
    [InlineData("% increased Spell Damage",      "(25–34)% increased Physical Damage")]
    [InlineData("Gain Mana per enemy killed",    "Gain (2–3) Life per enemy killed")]
    public void StatLineMatchesTemplate_EmbeddedTierRange_DoesNotMatchDifferentStat(string parsedStat, string template)
    {
        Assert.False(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsedStat, template),
            $"Ожидали НЕ совпадение: parsedStat='{parsedStat}' template='{template}'");
    }

    // ── ItemClassMatches: «Jewels» матчит «Time-Lost Sapphire Jewels» ───────────────────

    /// <summary>
    /// В игре класс предмета репортируется как «Jewels» для всех суб-типов джувелов.
    /// Библиотека хранит специфичный класс «Time-Lost Sapphire Jewels».
    /// ItemClassMatches должен принимать игровой класс как суффикс библиотечного.
    /// </summary>
    [Theory]
    [InlineData("Jewels",        "Time-Lost Sapphire Jewels")]
    [InlineData("Jewels",        "Sapphire Jewels")]
    [InlineData("Jewels",        "Jewels")]
    [InlineData("Rings",         "Rings")]
    [InlineData("Body Armours",  "Body Armours")]
    [InlineData("Wands",         "Wands")]
    public void ItemClassMatches_GameClassMatchesLibrarySubtype(string gameClass, string expectedClass)
    {
        var item = new ParsedItem { ItemClass = gameClass };
        Assert.True(
            ParsedItemCraftEvaluator.ItemClassMatches(item, expectedClass),
            $"gameClass='{gameClass}' expectedClass='{expectedClass}'");
    }

    [Theory]
    [InlineData("Jewels",   "Rings")]
    [InlineData("Helmets",  "Body Armours")]
    [InlineData("Wands",    "Amulets")]
    [InlineData("Rings",    "Jewels")]
    public void ItemClassMatches_DifferentClasses_DoNotMatch(string gameClass, string expectedClass)
    {
        var item = new ParsedItem { ItemClass = gameClass };
        Assert.False(
            ParsedItemCraftEvaluator.ItemClassMatches(item, expectedClass),
            $"gameClass='{gameClass}' expectedClass='{expectedClass}'");
    }

    // ── Регрессия попытки 21: Time-Lost Sapphire с «of Unmaking» не засчитывался ─────────

    private const string SapphireAttempt21Clipboard = """
        Item Class: Jewels
        Rarity: Rare
        Oblivion Creed
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 61
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 9(5-10)% increased Critical Spell Damage Bonus
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    private static CraftWholeModifierAffixData SapphireMember(string name, string stat, double minRoll) => new()
    {
        AffixType = "Suffix Modifier",
        AffixName = name,
        SelectedAffixNames = new List<string> { name },
        AffixTier = 1,
        Lines = new List<CraftWholeModifierLine>
        {
            new() { StatTemplate = stat, MinRoll = minRoll, MinRolls = new List<double> { minRoll } },
        },
    };

    private const string StatOfUnmaking = "Notable Passive Skills in Radius also grant (5–10)% increased Critical Spell Damage Bonus";
    private const string StatOfPotencyCD = "Notable Passive Skills in Radius also grant (5–10)% increased Critical Damage Bonus";

    /// <summary>
    /// COUNT≥1 из {of Potency (CD, fractured), of Unmaking (CS, regular)}.
    /// «of Potency» фрактурный — не засчитывается. «of Unmaking» обычный суффикс — засчитывается.
    /// Класс в буфере «Jewels», в условии «Time-Lost Sapphire Jewels» — должен проходить после фикса.
    /// Регрессия: до фикса StatLineMatchesTemplate возвращал false для «(5–10)%» шаблона.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_OfUnmaking_Regular_Satisfies_CountCondition()
    {
        var item = ItemParser.Parse(SapphireAttempt21Clipboard);
        Assert.Equal("Jewels", item.ItemClass);
        Assert.True(item.Affixes.Any(a => a.IsFractured && a.Name == "of Potency"),
            "Должен быть fractured of Potency");
        Assert.True(item.Affixes.Any(a => !a.IsFractured && a.Name == "of Unmaking"),
            "Должен быть regular of Unmaking");

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                    SapphireMember("of Potency",  StatOfPotencyCD,  5),
                                    SapphireMember("of Unmaking", StatOfUnmaking,   5),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"COUNT должен сработать через «of Unmaking» (regular). Объяснение: {expl}");
    }

    // ── Регрессия попытки 136: фрактурный «of Unmaking», регулярный «of Potency» (CD) ──

    private const string SapphireAttempt136Clipboard = """
        Item Class: Jewels
        Rarity: Rare
        Hypnotic Shine
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    /// <summary>
    /// «of Unmaking» фрактурный — не засчитывается.
    /// «of Potency» (CD Bonus) регулярный — засчитывается.
    /// Проблема: FindEntryByNameAndTierTypeCompatible возвращал первую запись «of Potency»
    /// (Small Passive Skills), а её статшаблон не совпадал с CD Bonus → idx=-1 → false.
    /// Фикс: перебираем всех кандидатов и выбираем тот, чьи статы совпадают с Lines в условии.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_OfPotencyCD_Regular_Satisfies_CountCondition()
    {
        var item = ItemParser.Parse(SapphireAttempt136Clipboard);
        Assert.True(item.Affixes.Any(a => a.IsFractured && a.Name == "of Unmaking"),
            "Должен быть fractured of Unmaking");
        Assert.True(item.Affixes.Any(a => !a.IsFractured && a.Name == "of Potency"),
            "Должен быть regular of Potency");

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                    SapphireMember("of Potency",  StatOfPotencyCD,  5),
                                    SapphireMember("of Unmaking", StatOfUnmaking,   5),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"COUNT должен сработать через «of Potency» (CD, regular). Объяснение: {expl}");
    }

    /// <summary>
    /// Тот же предмет, но условие требует «of Unmaking» с порогом 10 (максимум).
    /// Фактический ролл 9 — не дотягивает.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_OfUnmaking_MinRoll10_DoesNotMatch_Roll9()
    {
        var item = ItemParser.Parse(SapphireAttempt21Clipboard);

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                    SapphireMember("of Unmaking", StatOfUnmaking, 10),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out _),
            "Ролл 9 < 10, условие не должно сработать");
    }

    /// <summary>
    /// Fractured «of Potency» (CD) не засчитывается в COUNT, даже если диапазон правильный.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_FracturedOfPotency_DoesNotSatisfyCountAlone()
    {
        var item = ItemParser.Parse(SapphireAttempt21Clipboard);

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                    SapphireMember("of Potency", StatOfPotencyCD, 5),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out _),
            "Fractured «of Potency» не должен засчитываться как результат крафта");
    }

    /// <summary>
    /// Регрессия попытка 136: «of Potency» (CD) регулярный суффикс + «of Unmaking» (CS) фрактурный.
    /// Условие COUNT≥1 из {of Potency (CD), of Unmaking (CS)} должно совпасть через of Potency.
    /// До фикса: FindEntryByNameAndTierTypeCompatible возвращал первую запись «of Potency» (Small Passive Skills),
    /// ResolveStatIndexInEntry для StatOfPotencyCD возвращал -1 → false.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_OfPotencyCD_TryWholeModifierAnyLine_Disambiguates()
    {
        var item = ItemParser.Parse(SapphireAttempt136Clipboard);

        var wholemem = SapphireMember("of Potency", StatOfPotencyCD, 5);

        var lib = AffixLibrary.GetEntries();
        bool result = ParsedItemCraftEvaluator.TryWholeModifierAnyLineFullySatisfied(
            wholemem, item, "Time-Lost Sapphire Jewels", lib);

        Assert.True(result,
            "TryWholeModifierAnyLineFullySatisfied должен найти «of Potency» CD-вариант через disambiguation");
    }

    /// <summary>
    /// Тот же сценарий через полный TryEvaluate — итоговая интеграция.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_Attempt136_FullEvaluate_Satisfies()
    {
        var item = ItemParser.Parse(SapphireAttempt136Clipboard);

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                    SapphireMember("of Potency",  StatOfPotencyCD,  5),
                                    SapphireMember("of Unmaking", StatOfUnmaking,   5),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out _),
            "Попытка 136: «of Potency» CD должен засчитаться как цель крафта");
    }

    // ── Регрессия: «+1 Suffix Modifier allowed» в шаблоне StatLineMatchesTemplate ────────────

    /// <summary>
    /// Парсер извлекает «1» из «+1 Suffix Modifier allowed» в RolledValue,
    /// оставляя StatText = «+ Suffix Modifier allowed».
    /// Шаблон в библиотеке/условии: «+1 Suffix Modifier allowed» (без «#»).
    /// StatLineMatchesTemplate должен вернуть true через bNoHashNoFixed-ветку.
    /// </summary>
    [Theory]
    [InlineData("+ Suffix Modifier allowed", "+1 Suffix Modifier allowed")]
    [InlineData("+ Prefix Modifier allowed", "+1 Prefix Modifier allowed")]
    public void StatLineMatchesTemplate_FixedNumericInTemplate_MatchesParsedStat(string parsedStat, string template)
    {
        Assert.True(
            ParsedItemCraftEvaluator.StatLineMatchesTemplate(parsedStat, template),
            $"parsedStat='{parsedStat}' template='{template}'");
    }

    // ── Регрессия: «of Osmosis» / «of Mind» не засчитывались в COUNT ────────────────────────

    private const string SapphireWithOsmosisClipboard = """
        Item Class: Jewels
        Rarity: Rare
        Oblivion Creed
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 7(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Osmosis" (Tier: 1) — Mana }
        Notable Passive Skills in Radius also grant Recover 1% of maximum Mana on Kill
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    private const string SapphireWithMindClipboard = """
        Item Class: Jewels
        Rarity: Rare
        Oblivion Creed
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 7(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Mind" (Tier: 1) — Mana }
        Notable Passive Skills in Radius also grant 1% of Damage is taken from Mana before Life
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    /// <summary>
    /// Регрессия: «of Osmosis» имеет фиксированный стат без переката (affixRanges: null),
    /// парсер ставит RolledValue=null → TryGetOrderedRollValues возвращал false → COUNT не засчитывал.
    /// Фикс: fallback-извлечение числа из StatText через FixedNumericInStat.
    /// </summary>
    [Fact]
    public void Count_TimeLostSapphire_OfOsmosis_FixedValueStat_CountsInCountCondition()
    {
        var item = ItemParser.Parse(SapphireWithOsmosisClipboard);
        Assert.True(item.Affixes.Any(a => a.Name == "of Osmosis"),
            "Должен быть «of Osmosis» в парсенном предмете");

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                IncludeFractured = true,
                                Members =
                                {
                                    SapphireMember("of Potency",  StatOfPotencyCD,   5),
                                    SapphireMember("of Osmosis",
                                        "Notable Passive Skills in Radius also grant Recover 1% of maximum Mana on Kill",
                                        1),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"COUNT должен сработать через «of Osmosis» (fixed-value stat). Объяснение: {expl}");
    }

    [Fact]
    public void Count_TimeLostSapphire_OfMind_FixedValueStat_CountsInCountCondition()
    {
        var item = ItemParser.Parse(SapphireWithMindClipboard);
        Assert.True(item.Affixes.Any(a => a.Name == "of Mind"),
            "Должен быть «of Mind» в парсенном предмете");

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                                IncludeFractured = true,
                                Members =
                                {
                                    SapphireMember("of Potency", StatOfPotencyCD, 5),
                                    SapphireMember("of Mind",
                                        "Notable Passive Skills in Radius also grant 1% of Damage is taken from Mana before Life",
                                        1),
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"COUNT должен сработать через «of Mind» (fixed-value stat). Объяснение: {expl}");
    }

    // ── Регрессия: entryCondition Single с «Crafted Prefix Modifier» и «+1 Suffix Modifier allowed» ──

    private const string SapphireWithCraftedSuffixSlotClipboard = """
        Item Class: Jewels
        Rarity: Rare
        Gale Spark
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 7(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Osmosis" (Tier: 1) — Mana }
        Notable Passive Skills in Radius also grant Recover 1% of maximum Mana on Kill
        { Crafted Prefix Modifier }
        +1 Suffix Modifier allowed
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    /// <summary>
    /// Регрессия: entryCondition для шага OmenActivation с Single-клозом «+1 Suffix Modifier allowed»
    /// (Crafted Prefix Modifier, пустое имя) всегда возвращал false — уходил в ветку провала (аннул).
    /// Причины: 1) ItemParser не распознавал «Crafted Prefix Modifier» → тип пустой → AffixTypesCompatible false;
    ///           2) Если бы тип стал «Crafted Prefix Modifier» — Tier=0 ≠ 1 → тоже skip.
    /// Фикс: ParseAffixHeader обрабатывает Crafted-типы; TryGetRollValuesForNamedAffix принимает Tier=0 как «неизвестен».
    /// </summary>
    [Fact]
    public void Single_CraftedPrefixModifier_SuffixSlot_Matches_WhenOnItem()
    {
        var item = ItemParser.Parse(SapphireWithCraftedSuffixSlotClipboard);
        Assert.NotNull(item);
        Assert.True(item.Affixes.Any(a => a.Type == "Crafted Prefix Modifier"),
            "Парсер должен распознать «Crafted Prefix Modifier»");

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
            OrAlternatives =
            {
                new CraftAndGroup
                {
                    Clauses =
                    {
                        new CraftClause
                        {
                            Kind = CraftClauseKind.Single,
                            Single = new CraftSingleAffixData
                            {
                                AffixType = "Crafted Prefix Modifier",
                                AffixName = "",
                                SelectedAffixNames = new List<string> { "" },
                                AffixTier = 1,
                                Lines = new List<CraftWholeModifierLine>
                                {
                                    new() { StatTemplate = "+1 Suffix Modifier allowed", MinRoll = 0, MinRolls = new List<double> { 0 } },
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Single «Crafted Prefix Modifier» с «+1 Suffix Modifier allowed» должно совпасть. Объяснение: {expl}");
    }

    [Fact]
    public void Single_CraftedPrefixModifier_SuffixSlot_DoesNotMatch_WhenAbsent()
    {
        // Предмет без крафтованного мода
        var item = ItemParser.Parse(SapphireAttempt21Clipboard);

        var plan = new CraftConditionPlan
        {
            ExpectedItemClass = "Time-Lost Sapphire Jewels",
            OrAlternatives =
            {
                new CraftAndGroup
                {
                    Clauses =
                    {
                        new CraftClause
                        {
                            Kind = CraftClauseKind.Single,
                            Single = new CraftSingleAffixData
                            {
                                AffixType = "Crafted Prefix Modifier",
                                AffixName = "",
                                SelectedAffixNames = new List<string> { "" },
                                AffixTier = 1,
                                Lines = new List<CraftWholeModifierLine>
                                {
                                    new() { StatTemplate = "+1 Suffix Modifier allowed", MinRoll = 0, MinRolls = new List<double> { 0 } },
                                },
                            },
                        },
                    },
                },
            },
        };

        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out _),
            "Без крафтованного мода условие не должно выполняться");
    }

    // ── Рецепт «3 natural suffix last divine» (TLS, Count≥3 из 10) ───────────
    // Тесты точно воспроизводят условия рецепта из файла
    // «Time-Lost Sapphire Jewels 3 natural suffix last divine.json».
    // Статы берутся из библиотеки (формат #%), minRoll — из рецепта.

    private const string TlsStat_Potency     = "Notable Passive Skills in Radius also grant #% increased Critical Damage Bonus";
    private const string TlsStat_Unmaking    = "Notable Passive Skills in Radius also grant #% increased Critical Spell Damage Bonus";
    private const string TlsStat_Enchanting  = "Notable Passive Skills in Radius also grant #% increased Cast Speed";
    private const string TlsStat_Annihil     = "Notable Passive Skills in Radius also grant #% increased Critical Hit Chance";
    private const string TlsStat_Supremacy   = "#% increased Effect of Notable Passive Skills in Radius";
    private const string TlsStat_Annihilat   = "Notable Passive Skills in Radius also grant #% increased Critical Hit Chance for Spells";
    private const string TlsStat_Lengthening = "Notable Passive Skills in Radius also grant #% increased Skill Effect Duration";
    private const string TlsStat_Generation  = "Notable Passive Skills in Radius also grant Meta Skills gain #% increased Energy";
    private const string TlsStat_Mind        = "Notable Passive Skills in Radius also grant 1% of Damage is taken from Mana before Life";
    private const string TlsStat_Osmosis     = "Notable Passive Skills in Radius also grant Recover 1% of maximum Mana on Kill";

    /// <summary>Воспроизводит план из рецепта: Count≥3 из 10, IncludeFractured=true.</summary>
    private static CraftConditionPlan BuildRecipe3NaturalSuffixPlan() => new()
    {
        ExpectedItemClass = "Time-Lost Sapphire Jewels",
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
                            MinMatchCount = 3,
                            IncludeFractured = true,
                            Members =
                            {
                                SapphireMember("of Potency",     TlsStat_Potency,     10),
                                SapphireMember("of Unmaking",    TlsStat_Unmaking,    10),
                                SapphireMember("of Enchanting",  TlsStat_Enchanting,   2),
                                SapphireMember("of Annihilation",TlsStat_Annihil,      7),
                                SapphireMember("of Supremacy",   TlsStat_Supremacy,   20),
                                SapphireMember("of Annihilating",TlsStat_Annihilat,    7),
                                SapphireMember("of Lengthening", TlsStat_Lengthening,  5),
                                SapphireMember("of Generation",  TlsStat_Generation,   4),
                                SapphireMember("of Mind",        TlsStat_Mind,         1),
                                SapphireMember("of Osmosis",     TlsStat_Osmosis,      1),
                            },
                        },
                    },
                },
            },
        },
    };

    // Три натуральных суффикса, все роллы на максимуме (Unmaking=10, Potency=10, Annihilation=7)
    private const string TlsClip_3Natural_AllMax = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Annihilation" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 7(3-7)% increased Critical Hit Chance
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        """;

    // Тот же предмет, но Annihilation выбил 6 (minRoll=7) → COUNT=2 < 3
    private const string TlsClip_3Natural_AnnihilRollLow = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Annihilation" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 6(3-7)% increased Critical Hit Chance
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        """;

    // Фрактурный Potency + два натуральных (IncludeFractured=true) → COUNT=3
    private const string TlsClip_FracturedPotency_TwoNatural = """
        Item Class: Jewels
        Rarity: Rare
        Carrion Hope
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Annihilation" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 7(3-7)% increased Critical Hit Chance
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // Mind (fixed 1%) + Osmosis (fixed 1%) + Potency (10%) — два fixed-value суффикса легко проходят
    private const string TlsClip_FixedValueSuffixes = """
        Item Class: Jewels
        Rarity: Rare
        Dragon Ornament
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Suffix Modifier "of Mind" (Tier: 1) — Mana }
        Notable Passive Skills in Radius also grant 1% of Damage is taken from Mana before Life
        { Suffix Modifier "of Osmosis" (Tier: 1) — Mana }
        Notable Passive Skills in Radius also grant Recover 1% of maximum Mana on Kill
        { Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 10(5-10)% increased Critical Damage Bonus
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        """;

    /// <summary>3 натуральных суффикса, все роллы на максимуме → COUNT=3 ≥ 3 → стоп-условие выполнено.</summary>
    [Fact]
    public void Recipe3NaturalSuffix_ThreeNaturalAtMaxRoll_Passes()
    {
        var plan = BuildRecipe3NaturalSuffixPlan();
        var item = ItemParser.Parse(TlsClip_3Natural_AllMax);
        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Unmaking(10)+Potency(10)+Annihilation(7) — все три совпадают, COUNT=3. Объяснение: {expl}");
    }

    /// <summary>Annihilation выбил 6, minRoll=7 → не засчитывается → COUNT=2 < 3 → крафт продолжается.</summary>
    [Fact]
    public void Recipe3NaturalSuffix_AnnihilationRoll6BelowMin7_Fails()
    {
        var plan = BuildRecipe3NaturalSuffixPlan();
        var item = ItemParser.Parse(TlsClip_3Natural_AnnihilRollLow);
        Assert.False(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Annihilation ролл=6, minRoll=7 → не засчитывается → COUNT=2. Объяснение: {expl}");
    }

    /// <summary>IncludeFractured=true: фрактурный Potency(10) засчитывается наравне с обычными → COUNT=3.</summary>
    [Fact]
    public void Recipe3NaturalSuffix_FracturedPotencyCountsWithIncludeFractured_Passes()
    {
        var plan = BuildRecipe3NaturalSuffixPlan();
        var item = ItemParser.Parse(TlsClip_FracturedPotency_TwoNatural);
        Assert.True(item.Affixes.Any(a => a.IsFractured && a.Name == "of Potency"),
            "Предмет должен иметь фрактурный of Potency");
        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"IncludeFractured=true: фрактурный Potency+Unmaking+Annihilation = 3. Объяснение: {expl}");
    }

    /// <summary>Mind и Osmosis — fixed-value суффиксы (нет диапазона), всегда засчитываются при ≥1.</summary>
    [Fact]
    public void Recipe3NaturalSuffix_FixedValueSuffixesMindAndOsmosis_CountCorrectly()
    {
        var plan = BuildRecipe3NaturalSuffixPlan();
        var item = ItemParser.Parse(TlsClip_FixedValueSuffixes);
        Assert.True(
            CraftConditionEvaluator.TryEvaluate(plan, item, out var expl),
            $"Mind(fixed 1%)+Osmosis(fixed 1%)+Potency(10%) — все три засчитываются. Объяснение: {expl}");
    }
}
