using System.IO;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>Проверяет CraftClause.Negate — инверсию результата клоза на уровне AND-группы.</summary>
public sealed class CraftClauseNegateTests
{
    static CraftClauseNegateTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "affix_library.json");
            if (File.Exists(path)) { AffixLibrary.ReloadFromDisk(path); break; }
            dir = dir.Parent;
        }
    }

    // Предмет с одним суффиксом "of the Ice" (Cold Resistance)
    private const string ItemWithColdSuffix = """
        Item Class: Body Armours
        Rarity: Magic
        Vile Robe of the Ice
        --------
        Energy Shield: 202
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        --------
        { Suffix Modifier "of the Ice" (Tier: 2) — Cold Resistance }
        38(36-40)% to Cold Resistance
        """;

    // Предмет без суффикса Cold
    private const string ItemWithoutColdSuffix = """
        Item Class: Body Armours
        Rarity: Magic
        Vile Robe
        --------
        Energy Shield: 202
        --------
        Requires: Level 65, 121 Int
        --------
        Item Level: 79
        """;

    private static CraftConditionPlan PlanWithNegatedClause(bool negate) => new()
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
                        Kind = CraftClauseKind.Single,
                        Negate = negate,
                        Single = new CraftSingleAffixData
                        {
                            AffixType = "Suffix Modifier",
                            SelectedAffixNames = { "of the Ice" },
                            Lines = { new CraftWholeModifierLine { StatTemplate = "+#% to Cold Resistance", MinRoll = 0 } },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public void Negate_False_ItemHasCold_Passes()
    {
        var item = ItemParser.Parse(ItemWithColdSuffix);
        Assert.True(CraftConditionEvaluator.TryEvaluate(PlanWithNegatedClause(false), item, out _));
    }

    [Fact]
    public void Negate_False_ItemMissesCold_Fails()
    {
        var item = ItemParser.Parse(ItemWithoutColdSuffix);
        Assert.False(CraftConditionEvaluator.TryEvaluate(PlanWithNegatedClause(false), item, out _));
    }

    [Fact]
    public void Negate_True_ItemHasCold_Fails()
    {
        // NOT(has cold suffix) — предмет имеет суффикс → клоз NOT завален → всё false
        var item = ItemParser.Parse(ItemWithColdSuffix);
        Assert.False(CraftConditionEvaluator.TryEvaluate(PlanWithNegatedClause(true), item, out var expl));
        Assert.Contains("НЕ", expl);
    }

    [Fact]
    public void Negate_True_ItemMissesCold_Passes()
    {
        // NOT(has cold suffix) — предмет не имеет суффикса → NOT выполнен → true
        var item = ItemParser.Parse(ItemWithoutColdSuffix);
        Assert.True(CraftConditionEvaluator.TryEvaluate(PlanWithNegatedClause(true), item, out _));
    }

    [Fact]
    public void FormatSummary_ShowsNot()
    {
        var summary = CraftConditionEvaluator.FormatSummary(PlanWithNegatedClause(true));
        Assert.Contains("НЕ(", summary);
    }
}
