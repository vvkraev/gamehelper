using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class ItemParserTests
{
    // ── Вспомогательный метод ─────────────────────────────────────────────────

    private static string Lines(params string[] lines) =>
        string.Join("\r\n", lines);

    private const string Sep = "--------";

    // ── Невалидные входные данные ─────────────────────────────────────────────

    [Fact]
    public void Parse_Null_ReturnsNull() =>
        Assert.Null(ItemParser.Parse(null!));

    [Fact]
    public void Parse_Empty_ReturnsNull() =>
        Assert.Null(ItemParser.Parse(""));

    [Fact]
    public void Parse_Whitespace_ReturnsNull() =>
        Assert.Null(ItemParser.Parse("   \r\n  "));

    [Fact]
    public void Parse_MissingItemClass_ReturnsNull()
    {
        var text = Lines("Rarity: Rare", "Some Item", "Base");
        Assert.Null(ItemParser.Parse(text));
    }

    // ── Базовые поля (Normal-предмет) ─────────────────────────────────────────

    [Fact]
    public void Parse_NormalItem_BasicFields()
    {
        var text = Lines(
            "Item Class: Boots",
            "Rarity: Normal",
            "Daggerfoot Shoes",
            Sep,
            "Evasion Rating: 119",
            "Energy Shield: 45",
            Sep,
            "Requires: Level 80, 59 Dex, 59 Int",
            Sep,
            "Item Level: 82");

        var item = ItemParser.Parse(text);

        Assert.NotNull(item);
        Assert.True(item!.IsValid);
        Assert.Equal("Boots", item.ItemClass);
        Assert.Equal("Normal", item.Rarity);
        Assert.Equal("Daggerfoot Shoes", item.Name);
        Assert.Equal("", item.Base);
        Assert.Equal(82, item.ItemLevel);
        Assert.Equal("Level 80, 59 Dex, 59 Int", item.Requirements);
        Assert.Equal("Evasion/Energy Shield", item.ItemSubType);
    }

    // ── Magic-предмет ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_MagicItem_NoArtisticName_SingleNameLine()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Magic",
            "Wolfskin Mantle",
            Sep,
            "Armour: 294",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 80");

        var item = ItemParser.Parse(text);

        Assert.NotNull(item);
        Assert.Equal("Wolfskin Mantle", item!.Name);
        Assert.Equal("", item.Base);
        Assert.Equal("Magic", item.Rarity);
    }

    [Fact]
    public void Parse_MagicItem_WithAffixAndEffect()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Magic",
            "Pope's Vile Robe",
            Sep,
            "Energy Shield: 261 (augmented)",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 79",
            Sep,
            "{ Prefix Modifier \"Pope's\" (Tier: 1) — Life, Defences }",
            "42(39-42)% increased Energy Shield",
            "+42(42-49) to maximum Life");

        var item = ItemParser.Parse(text);

        Assert.NotNull(item);
        Assert.Single(item!.Affixes);
        var affix = item.Affixes[0];
        Assert.Equal("Prefix Modifier", affix.Type);
        Assert.Equal("Pope's", affix.Name);
        Assert.Equal(1, affix.Tier);
        Assert.Equal(2, affix.Effects.Count);
        Assert.Contains("Life", affix.Tags);
        Assert.Contains("Defences", affix.Tags);
    }

    // ── Rare-предмет: имя + база ──────────────────────────────────────────────

    [Fact]
    public void Parse_RareItem_TwoNameLines_NameAndBase()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 406 (augmented)",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80");

        var item = ItemParser.Parse(text);

        Assert.NotNull(item);
        Assert.Equal("Behemoth Shroud", item!.Name);
        Assert.Equal("Wolfskin Mantle", item.Base);
    }

    // ── Аффиксы: тип, имя, тир, теги ─────────────────────────────────────────

    [Fact]
    public void Parse_PrefixAndSuffix_TypesAndTiers()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 406",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Prefix Modifier \"Crusader's\" (Tier: 3) — Defences }",
            "+26(21-27) to Armour",
            "{ Suffix Modifier \"of the Polar Bear\" (Tier: 3) — Elemental, Cold, Resistance }",
            "+32(31-35)% to Cold Resistance");

        var item = ItemParser.Parse(text)!;

        Assert.Equal(2, item.Affixes.Count);

        var prefix = item.Affixes[0];
        Assert.Equal("Prefix Modifier", prefix.Type);
        Assert.Equal("Crusader's", prefix.Name);
        Assert.Equal(3, prefix.Tier);
        Assert.Equal(new[] { "Defences" }, prefix.Tags);

        var suffix = item.Affixes[1];
        Assert.Equal("Suffix Modifier", suffix.Type);
        Assert.Equal("of the Polar Bear", suffix.Name);
        Assert.Equal(3, suffix.Tier);
        Assert.Contains("Cold", suffix.Tags);
        Assert.Contains("Resistance", suffix.Tags);
    }

    [Fact]
    public void Parse_SixAffixes_AllParsed()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 723 (augmented)",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Prefix Modifier \"Crusader's\" (Tier: 3) — Defences }",
            "+26(21-27) to Armour",
            "+9(9-10) to maximum Energy Shield",
            "27(27-32)% increased Armour and Energy Shield",
            "{ Prefix Modifier \"Rapturous\" (Tier: 2) — Life }",
            "+198(190-199) to maximum Life",
            "{ Prefix Modifier \"Inspired\" (Tier: 2) — Defences }",
            "99(92-100)% increased Armour and Energy Shield",
            "{ Suffix Modifier \"of the Polar Bear\" (Tier: 3) — Elemental, Cold, Resistance }",
            "+32(31-35)% to Cold Resistance",
            "{ Suffix Modifier \"of the Troll\" (Tier: 6) — Life }",
            "9.7(9.1-13) Life Regeneration per second",
            "{ Suffix Modifier \"of the Kiln\" (Tier: 5) — Elemental, Fire, Resistance }",
            "+24(21-25)% to Fire Resistance");

        var item = ItemParser.Parse(text)!;

        Assert.Equal(6, item.Affixes.Count);
        Assert.Equal(3, item.Affixes.Count(a => a.Type == "Prefix Modifier"));
        Assert.Equal(3, item.Affixes.Count(a => a.Type == "Suffix Modifier"));
    }

    // ── Fractured и Desecrated модификаторы ──────────────────────────────────

    [Fact]
    public void Parse_FracturedPrefix_IsFracturedTrueTypePrefix()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Shadow Veil",
            "Vile Robe",
            Sep,
            "Energy Shield: 300",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Fractured Prefix Modifier \"Rapturous\" (Tier: 2) — Life }",
            "+198(190-199) to maximum Life");

        var item = ItemParser.Parse(text)!;

        Assert.Single(item.Affixes);
        Assert.Equal("Prefix Modifier", item.Affixes[0].Type);
        Assert.True(item.Affixes[0].IsFractured);
        Assert.Equal("Rapturous", item.Affixes[0].Name);
    }

    [Fact]
    public void Parse_FracturedSuffix_IsFracturedTrueSuffix()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Shadow Veil",
            "Vile Robe",
            Sep,
            "Energy Shield: 300",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Fractured Suffix Modifier \"of the Kiln\" (Tier: 5) — Fire }",
            "+24(21-25)% to Fire Resistance");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("Suffix Modifier", item.Affixes[0].Type);
        Assert.True(item.Affixes[0].IsFractured);
    }

    [Fact]
    public void Parse_DesecratedPrefix_TypeIsDesecratedPrefix()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Shadow Veil",
            "Vile Robe",
            Sep,
            "Energy Shield: 300",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Desecrated Prefix Modifier \"Ancient\" (Tier: 1) — Defences }",
            "99(92-100)% increased Armour");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("Desecrated Prefix Modifier", item.Affixes[0].Type);
        Assert.False(item.Affixes[0].IsFractured);
    }

    [Fact]
    public void Parse_DesecratedSuffix_TypeIsDesecratedSuffix()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Shadow Veil",
            "Vile Robe",
            Sep,
            "Energy Shield: 300",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Desecrated Suffix Modifier \"of Cold\" (Tier: 2) — Cold, Resistance }",
            "+32(31-35)% to Cold Resistance");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("Desecrated Suffix Modifier", item.Affixes[0].Type);
    }

    // ── EffectDetails: StatText, Range, RolledValue ───────────────────────────

    [Fact]
    public void EffectLine_RollNoSign_StatTextIsAfterRoll()
    {
        // "42(39-42)% increased Energy Shield" → StatText = "% increased Energy Shield"
        var item = ParseSingleEffect("42(39-42)% increased Energy Shield");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Equal("% increased Energy Shield", detail.StatText);
        Assert.Equal("39-42", detail.Range);
        Assert.Equal("42", detail.RolledValue);
    }

    [Fact]
    public void EffectLine_PlusRollNoPercent_StatTextHasSpacedPlus()
    {
        // "+26(21-27) to Armour" → StatText = "+ to Armour"
        var item = ParseSingleEffect("+26(21-27) to Armour");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Equal("+ to Armour", detail.StatText);
        Assert.Equal("21-27", detail.Range);
        Assert.Equal("26", detail.RolledValue);
    }

    [Fact]
    public void EffectLine_PlusRollPercent_StatTextHasPlusPercent()
    {
        // "+32(31-35)% to Cold Resistance" → StatText = "+% to Cold Resistance"
        var item = ParseSingleEffect("+32(31-35)% to Cold Resistance");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Equal("+% to Cold Resistance", detail.StatText);
        Assert.Equal("31-35", detail.Range);
        Assert.Equal("32", detail.RolledValue);
    }

    [Fact]
    public void EffectLine_FloatRoll_ParsedCorrectly()
    {
        // "9.7(9.1-13) Life Regeneration per second"
        var item = ParseSingleEffect("9.7(9.1-13) Life Regeneration per second");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Equal("Life Regeneration per second", detail.StatText);
        Assert.Equal("9.1-13", detail.Range);
        Assert.Equal("9.7", detail.RolledValue);
    }

    [Fact]
    public void EffectLine_MultipleRolls_UsesPlaceholders()
    {
        // "1(1-2) to 38(33-40) Added Cold Damage" → "X to Y Added Cold Damage"
        var item = ParseSingleEffect("1(1-2) to 38(33-40) Added Cold Damage");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Contains("X", detail.StatText);
        Assert.Contains("Y", detail.StatText);
        Assert.Contains("Added Cold Damage", detail.StatText);
        Assert.Contains("X(1-2)", detail.Range);
        Assert.Contains("Y(33-40)", detail.Range);
    }

    [Fact]
    public void EffectLine_FlatPlusNoRange_StatTextAndRolledValue()
    {
        // "+4 to Spirit" (no parentheses) → StatText = "+ to Spirit", RolledValue = "4"
        var item = ParseSingleEffect("+4 to Spirit");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Equal("+ to Spirit", detail.StatText);
        Assert.Equal("4", detail.RolledValue);
        Assert.Equal("4", detail.Range);
    }

    [Fact]
    public void EffectLine_PlainText_NoNumbers_StatTextIsRaw()
    {
        // Строка без числа → StatText = raw, Range = null
        var item = ParseSingleEffect("Grant a random Boon");
        var detail = item.Affixes[0].EffectDetails[0];

        Assert.Equal("Grant a random Boon", detail.StatText);
        Assert.Null(detail.Range);
        Assert.Null(detail.RolledValue);
    }

    // ── Quality, Sockets ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_Quality_ExtractedFromCharacteristicsSection()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Quality: +1% (augmented)",
            "Armour: 730 (augmented)",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("+1% (augmented)", item.Quality);
        Assert.DoesNotContain("Quality: +1% (augmented)", item.Characteristics);
    }

    [Fact]
    public void Parse_Sockets_ExtractedCorrectly()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 723",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Sockets: S S",
            Sep,
            "Item Level: 80");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("S S", item.Sockets);
    }

    // ── Rune augments ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_RuneLines_GoToAugments_NotCharacteristics()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 723",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Sockets: S S",
            Sep,
            "Item Level: 80",
            Sep,
            "+14% to Lightning Resistance (rune)",
            "30% faster start of Energy Shield Recharge (rune)");

        var item = ItemParser.Parse(text)!;

        Assert.Equal(2, item.Augments.Count);
        Assert.Contains("+14% to Lightning Resistance (rune)", item.Augments);
        Assert.Contains("30% faster start of Energy Shield Recharge (rune)", item.Augments);
        Assert.DoesNotContain("+14% to Lightning Resistance (rune)", item.Characteristics);
    }

    // ── Состояние предмета ────────────────────────────────────────────────────

    [Fact]
    public void Parse_CorruptedState_SetCorrectly()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 406",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Prefix Modifier \"Rapturous\" (Tier: 2) — Life }",
            "+198(190-199) to maximum Life",
            Sep,
            "Corrupted");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("Corrupted", item.State);
    }

    [Fact]
    public void Parse_SanctifiedState_SetCorrectly()
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 406",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "Sanctified");

        var item = ItemParser.Parse(text)!;

        Assert.Equal("Sanctified", item.State);
    }

    // ── Stack Size ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_StackSize_SimpleValue()
    {
        var text = Lines(
            "Item Class: Stackable Currency",
            "Rarity: Normal",
            "Chaos Orb",
            Sep,
            "Stack Size: 12/20",
            Sep,
            "Item Level: 1");

        var item = ItemParser.Parse(text)!;

        Assert.Equal(12, item.StackSize);
    }

    [Fact]
    public void Parse_StackSize_WithCommaThousandSeparator()
    {
        var text = Lines(
            "Item Class: Stackable Currency",
            "Rarity: Normal",
            "Gold",
            Sep,
            "Stack Size: 1,700/5,000",
            Sep,
            "Item Level: 1");

        var item = ItemParser.Parse(text)!;

        Assert.Equal(1700, item.StackSize);
    }

    // ── Порядок секций ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ItemLevelBeforeRequires_BothParsed()
    {
        // В буфере Item Level до Requires — парсер должен найти оба
        var text = Lines(
            "Item Class: Boots",
            "Rarity: Normal",
            "Iron Greaves",
            Sep,
            "Armour: 50",
            Sep,
            "Item Level: 10",
            Sep,
            "Requires: Level 12, 30 Str");

        var item = ItemParser.Parse(text)!;

        Assert.Equal(10, item.ItemLevel);
        Assert.Equal("Level 12, 30 Str", item.Requirements);
        Assert.Equal("Armour", item.ItemSubType);
    }

    [Fact]
    public void Parse_AffixWithMultipleEffectLines_AllInEffectsList()
    {
        // "Crusader's" has 3 effect lines
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Behemoth Shroud",
            "Wolfskin Mantle",
            Sep,
            "Armour: 406",
            Sep,
            "Requires: Level 65, 67 Str, 67 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Prefix Modifier \"Crusader's\" (Tier: 3) — Defences }",
            "+26(21-27) to Armour",
            "+9(9-10) to maximum Energy Shield",
            "27(27-32)% increased Armour and Energy Shield");

        var item = ItemParser.Parse(text)!;

        Assert.Single(item.Affixes);
        Assert.Equal(3, item.Affixes[0].Effects.Count);
        Assert.Equal(3, item.Affixes[0].EffectDetails.Count);
    }

    // ── Вспомогательный метод: строит минимальный предмет с одним аффиксом и одним эффектом

    private static ParsedItem ParseSingleEffect(string effectLine)
    {
        var text = Lines(
            "Item Class: Body Armours",
            "Rarity: Rare",
            "Shadow Veil",
            "Vile Robe",
            Sep,
            "Energy Shield: 300",
            Sep,
            "Requires: Level 65, 121 Int",
            Sep,
            "Item Level: 80",
            Sep,
            "{ Prefix Modifier \"Test\" (Tier: 1) — Test }",
            effectLine);

        return ItemParser.Parse(text)!;
    }
}
