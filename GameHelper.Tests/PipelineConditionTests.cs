using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Тесты условий из пайплайна «Новый рецепт» (Time-Lost Sapphire, батч-крафт).
/// Каждый тест привязан к конкретному шагу пайплайна (Pipelines/Новый рецепт.json).
/// Проверяются entryCondition и loopUntil, которые управляют ветвлением батча.
/// </summary>
public sealed class PipelineConditionTests
{
    // ── Статы для 6 основных суффиксов (шаги 0, 4) ────────────────────────────

    private const string StatPotency     = "Notable Passive Skills in Radius also grant #% increased Critical Damage Bonus";
    private const string StatAnnihilating = "Notable Passive Skills in Radius also grant #% increased Critical Hit Chance for Spells";
    private const string StatUnmaking    = "Notable Passive Skills in Radius also grant #% increased Critical Spell Damage Bonus";
    private const string StatLengthening = "Notable Passive Skills in Radius also grant #% increased Skill Effect Duration";
    private const string StatOsmosis     = "Notable Passive Skills in Radius also grant Recover 1% of maximum Mana on Kill";
    private const string StatMind        = "Notable Passive Skills in Radius also grant 1% of Damage is taken from Mana before Life";

    // ── Дополнительные статы для 10 членов (шаги 7, 8, 12) ───────────────────

    private const string StatSupremacy   = "#% increased Effect of Notable Passive Skills in Radius";
    private const string StatEnchanting  = "Notable Passive Skills in Radius also grant #% increased Cast Speed";
    private const string StatGeneration  = "Notable Passive Skills in Radius also grant Meta Skills gain #% increased Energy";
    private const string StatAnnihilation = "Notable Passive Skills in Radius also grant #% increased Critical Hit Chance";

    private static CraftWholeModifierAffixData Suf(string name, string stat, double minRoll) => new()
    {
        AffixType = "Suffix Modifier",
        AffixName = name,
        SelectedAffixNames = [name],
        AffixTier = 1,
        Lines = [new() { StatTemplate = stat, MinRoll = minRoll, MinRolls = [minRoll] }],
    };

    // ── Планы ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Шаг 0 loopUntil / шаг 4 entryCondition:
    /// Count≥2 из 6 суффиксов, IncludeFractured=true.
    /// </summary>
    private static CraftClause Count6_Min2() => new()
    {
        Kind = CraftClauseKind.Count,
        Count = new CraftCountAffixData
        {
            MinMatchCount = 2,
            IncludeFractured = true,
            Members =
            {
                Suf("of Potency",     StatPotency,      5),
                Suf("of Annihilating",StatAnnihilating,  3),
                Suf("of Unmaking",    StatUnmaking,      5),
                Suf("of Lengthening", StatLengthening,   3),
                Suf("of Osmosis",     StatOsmosis,       1),
                Suf("of Mind",        StatMind,          1),
            },
        },
    };

    /// <summary>
    /// Шаги 7, 8, 12 entryCondition/loopUntil:
    /// Count≥3 из 10 суффиксов, IncludeFractured=true.
    /// </summary>
    private static CraftClause Count10_Min3() => new()
    {
        Kind = CraftClauseKind.Count,
        Count = new CraftCountAffixData
        {
            MinMatchCount = 3,
            IncludeFractured = true,
            Members =
            {
                Suf("of Potency",     StatPotency,       5),
                Suf("of Annihilating",StatAnnihilating,  3),
                Suf("of Unmaking",    StatUnmaking,      5),
                Suf("of Lengthening", StatLengthening,   3),
                Suf("of Osmosis",     StatOsmosis,       1),
                Suf("of Mind",        StatMind,          1),
                Suf("of Supremacy",   StatSupremacy,    15),
                Suf("of Enchanting",  StatEnchanting,    1),
                Suf("of Generation",  StatGeneration,    2),
                Suf("of Annihilation",StatAnnihilation,  3),
            },
        },
    };

    /// <summary>
    /// Шаг 3 loopUntil: наличие крафтованного «+1 Suffix Modifier allowed».
    /// </summary>
    private static CraftClause SingleCraftedPlusSuffix() => new()
    {
        Kind = CraftClauseKind.Single,
        Single = new CraftSingleAffixData
        {
            AffixType          = "Crafted Prefix Modifier",
            AffixName          = "",
            SelectedAffixNames = new List<string> { "" },
            AffixTier          = 1,
            Lines = [new() { StatTemplate = "+1 Suffix Modifier allowed", MinRoll = 0, MinRolls = [0] }],
        },
    };

    private static CraftConditionPlan Plan(params CraftClause[] clauses) => new()
    {
        ExpectedItemClass = "Time-Lost Sapphire Jewels",
        OrAlternatives = { new CraftAndGroup { Clauses = clauses.ToList() } },
    };

    private static bool Check(CraftConditionPlan plan, string clipboard, out string expl) =>
        CraftConditionEvaluator.TryEvaluate(plan, ItemParser.Parse(clipboard), out expl);

    // ── Буферы предметов ──────────────────────────────────────────────────────

    // Предмет с фрактурным of Potency(5) + натуральным of Unmaking(5) → 2 суффикса в списке 6
    private const string Item_FracPotency5_Unmaking5 = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Spell Damage Bonus
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // Потенси(5) фрактурный + Annihilating(2) ниже minRoll=3 → только 1 совпадение
    private const string Item_FracPotency5_Annihilating2 = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Annihilating" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 2(3-7)% increased Critical Hit Chance for Spells
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // Три суффикса из списка (все ≥ minRoll): фрак Potency(5) + Unmaking(5) + Annihilating(3)
    private const string Item_FracPotency5_Unmaking5_Annihilating3 = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Annihilating" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 3(3-7)% increased Critical Hit Chance for Spells
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // 3 суффикса + крафтованный «+1 Suffix Modifier allowed» (шаг 8)
    private const string Item_3Suf_CraftedPlusSuffix = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Annihilating" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 3(3-7)% increased Critical Hit Chance for Spells
        { Crafted Prefix Modifier }
        +1 Suffix Modifier allowed
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // 3 суффикса + 2 натуральных префикса (шаг 12)
    private const string Item_3Suf_2Prefix = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Authoritative" (Tier: 1) — Minion }
        Small Passive Skills in Radius also grant Minions deal 1(1-2)% increased Damage
        { Prefix Modifier "Expanding" (Tier: 1) — Curse }
        Notable Passive Skills in Radius also grant 3(3-6)% increased Area of Effect of Curses
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Annihilating" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 3(3-7)% increased Critical Hit Chance for Spells
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // 3 суффикса + только 1 префикс (шаг 12 не пропускает)
    private const string Item_3Suf_1Prefix = """
        Item Class: Jewels
        Rarity: Rare
        Rapture Vessel
        Time-Lost Sapphire
        --------
        Radius: Small
        --------
        Item Level: 79
        --------
        { Prefix Modifier "Authoritative" (Tier: 1) — Minion }
        Small Passive Skills in Radius also grant Minions deal 1(1-2)% increased Damage
        { Fractured Suffix Modifier "of Potency" (Tier: 1) — Damage, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Damage Bonus
        { Suffix Modifier "of Unmaking" (Tier: 1) — Damage, Caster, Critical }
        Notable Passive Skills in Radius also grant 5(5-10)% increased Critical Spell Damage Bonus
        { Suffix Modifier "of Annihilating" (Tier: 1) — Critical }
        Notable Passive Skills in Radius also grant 3(3-7)% increased Critical Hit Chance for Spells
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        --------
        Fractured Item
        """;

    // ── Шаг 0: loopUntil (chaosCraft останавливается) ─────────────────────────

    /// <summary>Шаг 0 loopUntil: 2 суффикса из 6 → хаос-крафт должен остановиться.</summary>
    [Fact]
    public void Step0_LoopUntil_TwoSuffixesMatch_ChaosCraftStops()
    {
        var plan = Plan(Count6_Min2());
        Assert.True(Check(plan, Item_FracPotency5_Unmaking5, out var expl),
            $"Фрак Potency(5)+Unmaking(5) = 2 из 6 ≥ minMatch=2 → должно остановить хаос. {expl}");
    }

    /// <summary>Шаг 0 loopUntil: только 1 суффикс ≥ minRoll (Annihilating ролл 2 < 3) → хаос продолжается.</summary>
    [Fact]
    public void Step0_LoopUntil_OnlyOneSuffixAboveMin_ChaosCraftContinues()
    {
        var plan = Plan(Count6_Min2());
        Assert.False(Check(plan, Item_FracPotency5_Annihilating2, out var expl),
            $"Annihilating ролл=2 ниже minRoll=3 → COUNT=1 < 2 → хаос не останавливается. {expl}");
    }

    // ── Шаг 4: entryCondition (omenActivation пропускается) ───────────────────

    /// <summary>Шаг 4 entryCondition: 2 суффикса из 6 AND крафтованный мод → оба условия.</summary>
    [Fact]
    public void Step4_Entry_TwoSuffixesAndCraftedMod_AllowsOmenActivation()
    {
        var plan = Plan(Count6_Min2(), SingleCraftedPlusSuffix());
        Assert.True(Check(plan, Item_3Suf_CraftedPlusSuffix, out var expl),
            $"3 суффикса + крафт.мод: Count6≥2 AND crftd → активация омена разрешена. {expl}");
    }

    /// <summary>Шаг 4 entryCondition: суффиксы есть но крафтованный мод отсутствует → заблокировано.</summary>
    [Fact]
    public void Step4_Entry_TwoSuffixesButNoCraftedMod_BlocksOmenActivation()
    {
        var plan = Plan(Count6_Min2(), SingleCraftedPlusSuffix());
        Assert.False(Check(plan, Item_FracPotency5_Unmaking5, out var expl),
            $"Нет крафтованного мода → entryCondition не выполнено. {expl}");
    }

    // ── Шаг 7: entryCondition (checkItem три суффикса) ───────────────────────

    /// <summary>Шаг 7 entryCondition: Count≥3 из 10 — предмет с тремя суффиксами → пройдено.</summary>
    [Fact]
    public void Step7_Entry_ThreeSuffixesFromTen_AllowsCheck()
    {
        var plan = Plan(Count10_Min3());
        Assert.True(Check(plan, Item_FracPotency5_Unmaking5_Annihilating3, out var expl),
            $"Potency(5)+Unmaking(5)+Annihilating(3) = 3 из 10 → checkItem разрешён. {expl}");
    }

    /// <summary>Шаг 7 entryCondition: только 2 суффикса из 10 → предмет не попадает в checkItem.</summary>
    [Fact]
    public void Step7_Entry_OnlyTwoSuffixesFromTen_BlocksCheck()
    {
        var plan = Plan(Count10_Min3());
        Assert.False(Check(plan, Item_FracPotency5_Unmaking5, out var expl),
            $"Только 2 из 10 → Count=2 < 3 → checkItem заблокирован. {expl}");
    }

    // ── Шаг 8: entryCondition (удаление «неподходящего» третьего суффикса) ───

    /// <summary>
    /// Шаг 8 entryCondition: Count≥3 AND крафтованный +1-суффикс AND суффиксов≥3
    /// Все три AND-условия выполнены → удаление разрешено.
    /// </summary>
    [Fact]
    public void Step8_Entry_AllThreeAndClauses_AllowsRemoval()
    {
        var plan = Plan(
            Count10_Min3(),
            SingleCraftedPlusSuffix(),
            new CraftClause
            {
                Kind = CraftClauseKind.AffixCount,
                AffixCount = new AffixCountData { Scope = AffixCountScope.Suffixes, Min = 3, Max = 0 },
            });

        Assert.True(Check(plan, Item_3Suf_CraftedPlusSuffix, out var expl),
            $"3 суф.совпадают + крафтованный мод + 3 суф.итого → удаление разрешено. {expl}");
    }

    /// <summary>
    /// Шаг 8 entryCondition: те же три AND-условия, но крафтованный мод отсутствует → заблокировано.
    /// </summary>
    [Fact]
    public void Step8_Entry_MissingCraftedMod_BlocksRemoval()
    {
        var plan = Plan(
            Count10_Min3(),
            SingleCraftedPlusSuffix(),
            new CraftClause
            {
                Kind = CraftClauseKind.AffixCount,
                AffixCount = new AffixCountData { Scope = AffixCountScope.Suffixes, Min = 3, Max = 0 },
            });

        Assert.False(Check(plan, Item_FracPotency5_Unmaking5_Annihilating3, out var expl),
            $"Нет крафтованного мода → AND-условие не выполнено. {expl}");
    }

    // ── Шаг 12: entryCondition (финальная проверка перед десекрейтом) ─────────

    /// <summary>
    /// Шаг 12 entryCondition: Count≥3 из 10 AND префиксов≥2 → десекрейт разрешён.
    /// </summary>
    [Fact]
    public void Step12_Entry_ThreeSuffixesAndTwoPrefixes_AllowsDesecrate()
    {
        var plan = Plan(
            Count10_Min3(),
            new CraftClause
            {
                Kind = CraftClauseKind.AffixCount,
                AffixCount = new AffixCountData { Scope = AffixCountScope.Prefixes, Min = 2, Max = 0 },
            });

        Assert.True(Check(plan, Item_3Suf_2Prefix, out var expl),
            $"3 суффикса + 2 префикса → Count≥3 AND prefixes≥2 → десекрейт разрешён. {expl}");
    }

    /// <summary>
    /// Шаг 12 entryCondition: 3 суффикса но только 1 префикс → prefixes≥2 не выполнено → заблокировано.
    /// </summary>
    [Fact]
    public void Step12_Entry_OnlyOnePrefix_BlocksDesecrate()
    {
        var plan = Plan(
            Count10_Min3(),
            new CraftClause
            {
                Kind = CraftClauseKind.AffixCount,
                AffixCount = new AffixCountData { Scope = AffixCountScope.Prefixes, Min = 2, Max = 0 },
            });

        Assert.False(Check(plan, Item_3Suf_1Prefix, out var expl),
            $"Только 1 префикс → prefixCount=1 < 2 → десекрейт заблокирован. {expl}");
    }

    // ── Шаг 3: loopUntil (ждём появления крафтованного +1-суффикс слота) ─────

    /// <summary>Шаг 3 loopUntil: крафтованный мод присутствует → ожидание завершено.</summary>
    [Fact]
    public void Step3_LoopUntil_CraftedModPresent_Stops()
    {
        var plan = Plan(SingleCraftedPlusSuffix());
        Assert.True(Check(plan, Item_3Suf_CraftedPlusSuffix, out var expl),
            $"Крафтованный «+1 Suffix Modifier allowed» присутствует → ожидание завершено. {expl}");
    }

    /// <summary>Шаг 3 loopUntil: крафтованного мода нет → продолжаем ждать.</summary>
    [Fact]
    public void Step3_LoopUntil_NoCraftedMod_Continues()
    {
        var plan = Plan(SingleCraftedPlusSuffix());
        Assert.False(Check(plan, Item_FracPotency5_Unmaking5_Annihilating3, out var expl),
            $"Крафтованного мода нет → ожидание продолжается. {expl}");
    }
}
