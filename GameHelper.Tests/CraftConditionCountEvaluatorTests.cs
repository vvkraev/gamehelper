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
}
