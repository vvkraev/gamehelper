using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class ItemParserSubTypeTests
{
    [Theory]
    [InlineData("Level 70, 121 Str",             "Armour")]
    [InlineData("Level 70, 121 Dex",             "Evasion")]
    [InlineData("Level 70, 121 Int",             "Energy Shield")]
    [InlineData("Level 70, 95 Str, 62 Dex",     "Armour/Evasion")]
    [InlineData("Level 70, 95 Str, 62 Int",     "Armour/Energy Shield")]
    [InlineData("Level 70, 95 Dex, 62 Int",     "Evasion/Energy Shield")]
    [InlineData("Level 65, 121 (unmet) Int",     "Energy Shield")]  // (unmet) не мешает
    [InlineData("Level 70, 60 Str, 60 Dex, 60 Int", "")]  // три атрибута → пусто
    [InlineData("Level 70",                      "")]  // нет атрибутов → пусто
    [InlineData("",                              "")]  // пустая строка
    public void ParseItemSubType_ReturnsExpected(string requirements, string expected)
    {
        var result = ItemParser.ParseItemSubType(requirements);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParsedItem_HasItemSubType_AfterParsing()
    {
        // Body Armour Str+Dex → Armour/Evasion
        var itemText =
            "Item Class: Body Armours\r\n" +
            "Rarity: Rare\r\n" +
            "Dusk Veil\r\n" +
            "Ringmail\r\n" +
            "--------\r\n" +
            "Evasion Rating: 193\r\n" +
            "--------\r\n" +
            "Requires: Level 70, 62 Str, 95 Dex\r\n" +
            "--------\r\n" +
            "Item Level: 78\r\n";

        var parsed = ItemParser.Parse(itemText);

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsValid);
        Assert.Equal("Armour/Evasion", parsed.ItemSubType);
    }
}
