namespace GameHelper.Services;

/// <summary>Приведение плана после десериализации JSON: заполнение <see cref="CraftSingleAffixData.SelectedAffixNames"/> из устаревших полей.</summary>
public static class CraftConditionPlanNormalizer
{
    public static void NormalizeInPlace(CraftConditionPlan plan, IReadOnlyList<AffixLibraryEntry> entries)
    {
        foreach (var or in plan.OrAlternatives)
        foreach (var c in or.Clauses)
        {
            if (c.Single is { } sg)
            {
                NormalizeSingle(sg, plan.ExpectedItemClass, entries);
                sg.EnsureLinesFromLegacy();
                ClampMinRollToLibrary(sg, plan.ExpectedItemClass, entries);
                ClampSingleLinesToLibrary(sg, plan.ExpectedItemClass, entries);
            }

            if (c.Count?.Members != null)
            {
                foreach (var m in c.Count.Members)
                {
                    if (m.SelectedAffixNames.Count == 0 && !string.IsNullOrWhiteSpace(m.AffixName))
                        m.SelectedAffixNames.Add(m.AffixName.Trim());
                    ClampWholeModifierLinesToLibrary(m, plan.ExpectedItemClass, entries);
                }
            }

            if (c.Whole is { } w)
            {
                if (w.SelectedAffixNames.Count == 0 && !string.IsNullOrWhiteSpace(w.AffixName))
                    w.SelectedAffixNames.Add(w.AffixName.Trim());
            }
        }
    }

    /// <summary>
    /// Порог не выше максимума по affix_library (иначе при фикс. перекате «5» в JSON UI давал слайдер до 6).
    /// </summary>
    public static void ClampMinRollToLibrary(
        CraftSingleAffixData s,
        string itemClass,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(itemClass) ||
            string.IsNullOrWhiteSpace(s.AffixType) ||
            string.IsNullOrWhiteSpace(s.StatTemplate) ||
            s.AffixTier < 1)
            return;

        var names = s.EffectiveAffixNames();
        if (names.Count == 0)
            return;

        var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(
            itemClass, s.AffixType, s.StatTemplate, entries);
        s.EnsureMinRollsSize(slots);
        var (_, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForSingleStat(
            itemClass, s.AffixType, s.StatTemplate, names, s.AffixTier, entries);
        if (s.MinRoll > hi)
            s.MinRoll = hi;
        for (var i = 0; i < s.MinRolls.Count; i++)
        {
            if (s.MinRolls[i] > hi)
                s.MinRolls[i] = hi;
        }
    }

    private static void ClampSingleLinesToLibrary(
        CraftSingleAffixData s,
        string itemClass,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (s.Lines.Count == 0 || string.IsNullOrWhiteSpace(itemClass) || string.IsNullOrWhiteSpace(s.AffixType) || s.AffixTier < 1)
            return;
        var names = s.EffectiveAffixNames().ToList();
        if (names.Count == 0)
            return;
        var refEntry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            entries, itemClass, s.AffixType, names[0], s.AffixTier);
        if (refEntry is null)
            return;
        foreach (var line in s.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.StatTemplate))
                continue;
            var si = CraftAffixCascadeHelper.FindStatIndexInEntry(refEntry, line.StatTemplate);
            if (si < 0)
                continue;
            var (_, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForWholeLine(
                itemClass, s.AffixType, refEntry, si, names, s.AffixTier, entries);
            if (hi <= 0)
                continue;
            if (line.MinRoll > hi)
                line.MinRoll = hi;
            for (var i = 0; i < line.MinRolls.Count; i++)
                if (line.MinRolls[i] > hi)
                    line.MinRolls[i] = hi;
        }
    }

    private static void ClampWholeModifierLinesToLibrary(
        CraftWholeModifierAffixData m,
        string itemClass,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(itemClass) || string.IsNullOrWhiteSpace(m.AffixType) || m.AffixTier < 1)
            return;
        var names = m.EffectiveWholeAffixNames().ToList();
        if (names.Count == 0)
            return;
        var refEntry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            entries, itemClass, m.AffixType, names[0], m.AffixTier);
        if (refEntry is null)
            return;
        foreach (var line in m.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.StatTemplate))
                continue;
            var si = CraftAffixCascadeHelper.FindStatIndexInEntry(refEntry, line.StatTemplate);
            if (si < 0)
                continue;
            var (_, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForWholeLine(
                itemClass, m.AffixType, refEntry, si, names, m.AffixTier, entries);
            if (hi <= 0)
                continue;
            if (line.MinRoll > hi)
                line.MinRoll = hi;
            for (var i = 0; i < line.MinRolls.Count; i++)
                if (line.MinRolls[i] > hi)
                    line.MinRolls[i] = hi;
        }
    }

    private static void NormalizeSingle(
        CraftSingleAffixData s,
        string itemClass,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (s.SelectedAffixNames.Count > 0)
            return;
        if (!string.IsNullOrWhiteSpace(s.AffixName))
        {
            s.SelectedAffixNames.Add(s.AffixName.Trim());
            return;
        }

        if (s.AffixTier < 1 || string.IsNullOrWhiteSpace(itemClass) ||
            string.IsNullOrWhiteSpace(s.AffixType) || string.IsNullOrWhiteSpace(s.StatTemplate))
            return;

        var names = CraftAffixCascadeHelper.GetAffixNamesForClassTypeStatTier(
            itemClass,
            s.AffixType,
            s.StatTemplate,
            s.AffixTier,
            entries);
        if (names.Count == 1)
            s.SelectedAffixNames.Add(names[0]);
    }
}
