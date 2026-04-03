using GameHelper;

namespace GameHelper.Services;

/// <summary>Построение <see cref="CraftConditionPlan"/> из полей старых настроек (один аффикс).</summary>
public static class CraftConditionMigration
{
    public static CraftConditionPlan FromLegacy(AppSettings s, IReadOnlyList<AffixLibraryEntry> entries)
    {
        var list = entries as List<AffixLibraryEntry> ?? entries.ToList();
        var plan = new CraftConditionPlan { ExpectedItemClass = s.CraftItemClass?.Trim() ?? "" };
        if (string.IsNullOrEmpty(plan.ExpectedItemClass))
            return plan;
        if (string.IsNullOrEmpty(s.CraftAffixType) || s.CraftAffixTier <= 0 || string.IsNullOrEmpty(s.CraftAffixStat))
            return plan;

        var entry = AffixCraftPatternBuilder.FindEntry(
            list,
            plan.ExpectedItemClass,
            s.CraftAffixType,
            s.CraftAffixStat,
            s.CraftAffixTier);
        if (entry is null)
            return plan;

        var idx = AffixCraftPatternBuilder.GetStatIndex(entry, s.CraftAffixStat);
        var range = idx >= 0 && idx < entry.AffixRanges.Count ? entry.AffixRanges[idx] : null;
        var allowDec = AffixCraftPatternBuilder.RangeAllowsDecimal(range);
        AffixCraftPatternBuilder.TryParseMinRoll(
            string.IsNullOrEmpty(s.MinRollInput) ? "0" : s.MinRollInput,
            allowDec,
            out var min);

        var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(
            plan.ExpectedItemClass,
            entry.AffixType,
            s.CraftAffixStat,
            list);
        var minRolls = Enumerable.Repeat(min, slots).ToList();

        plan.OrAlternatives.Add(new CraftAndGroup
        {
            Clauses = new List<CraftClause>
            {
                new CraftClause
                {
                    Kind = CraftClauseKind.Single,
                    Single = new CraftSingleAffixData
                    {
                        AffixType = entry.AffixType,
                        AffixName = "",
                        AffixTier = 0,
                        StatTemplate = s.CraftAffixStat,
                        MinRoll = min,
                        MinRolls = minRolls,
                    },
                },
            },
        });

        return plan;
    }
}
