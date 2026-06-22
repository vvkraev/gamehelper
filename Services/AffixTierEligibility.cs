namespace GameHelper.Services;

/// <summary>
/// Фильтр допустимых тиров аффикса для заданного ilvl предмета и орба.
/// Реализует шаг 3 алгоритма из docs/AFFIX_TIER_SELECTION_MECHANICS.md.
/// </summary>
public static class AffixTierEligibility
{
    /// <summary>
    /// Возвращает тиры семейства, доступные для выбора орбом.
    ///
    /// Фильтр: <c>tier.modifier_level &lt;= itemIlvl AND tier.modifier_level &gt;= orb.MinModifierLevel</c>
    ///
    /// Исключение T1: если ни один тир не проходит фильтр, возвращается T1 семейства
    /// независимо от его <c>modifier_level</c> (если T1 существует).
    ///
    /// Если орб не выбирает тиры (<see cref="OrbCraftProperties.SelectsTier"/> = false),
    /// возвращается пустой список.
    /// </summary>
    /// <param name="familyEntries">Все тиры одного семейства (один AffixName + ItemClass + AffixSubClass).</param>
    /// <param name="itemIlvl">Item level предмета.</param>
    /// <param name="orb">Свойства орба.</param>
    public static IReadOnlyList<AffixLibraryEntry> GetEligibleTiers(
        IReadOnlyList<AffixLibraryEntry> familyEntries,
        int itemIlvl,
        OrbCraftProperties orb)
    {
        if (!orb.SelectsTier || familyEntries.Count == 0)
            return Array.Empty<AffixLibraryEntry>();

        var eligible = familyEntries
            .Where(e => IsEligible(e, itemIlvl, orb.MinModifierLevel))
            .ToList();

        if (eligible.Count > 0)
            return eligible;

        // T1 исключение: если ни один тир не прошёл фильтр — берём T1 семейства.
        var t1 = familyEntries.FirstOrDefault(e => e.AffixTier == 1);
        return t1 is not null
            ? new[] { t1 }
            : Array.Empty<AffixLibraryEntry>();
    }

    /// <summary>
    /// Проверяет, проходит ли тир базовый фильтр ilvl и MinModifierLevel орба.
    /// <c>AffixTierLevel == null</c> трактуется как 1 (нет явного ограничения).
    /// </summary>
    public static bool IsEligible(AffixLibraryEntry entry, int itemIlvl, int minModifierLevel)
    {
        var ml = entry.AffixTierLevel ?? 1;
        return ml <= itemIlvl && ml >= minModifierLevel;
    }
}
