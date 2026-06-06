using System.Globalization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Вспомогательные списки для UI: тип модификатора и строки стата при заданном классе предмета.</summary>
public static class CraftAffixCascadeHelper
{
    /// <summary>Число независимых перекатов в строке стата (по полю affixRanges в библиотеке).</summary>
    public static int CountSlotsFromRangeString(string? affixRange)
    {
        if (string.IsNullOrWhiteSpace(affixRange))
            return 1;
        var parts = affixRange.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length <= 1 ? 1 : parts.Length;
    }

    /// <summary>Подписи слотов из строки диапазона, напр. <c>X(2-3); Y(4-6)</c> → X, Y.</summary>
    public static IReadOnlyList<string> GetRollSlotLabels(string? affixRange, int slotCount)
    {
        if (slotCount <= 1)
            return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(affixRange))
            return Enumerable.Range(0, slotCount).Select(i => (i + 1).ToString()).ToList();

        var parts = affixRange.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var labels = new List<string>();
        foreach (var p in parts)
        {
            var t = p.Trim();
            var open = t.IndexOf('(', StringComparison.Ordinal);
            labels.Add(open > 0 ? t[..open].Trim() : (labels.Count + 1).ToString());
        }

        while (labels.Count < slotCount)
            labels.Add((labels.Count + 1).ToString());
        return labels.Take(slotCount).ToList();
    }

    /// <summary>Первая подходящая строка affixRanges для пары (класс, тип, шаблон стата).</summary>
    public static string? GetTierRangeStringForStat(
        string itemClass,
        string affixType,
        string statTemplate,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)))
                continue;
            if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                continue;
            for (var i = 0; i < e.AffixStats.Count; i++)
            {
                var st = e.AffixStats[i];
                if (string.Equals(st.Trim(), statTemplate.Trim(), StringComparison.Ordinal) ||
                    ParsedItemCraftEvaluator.StatLineMatchesTemplate(st, statTemplate))
                {
                    return i < e.AffixRanges.Count ? e.AffixRanges[i] : null;
                }
            }
        }

        return null;
    }

    public static int GetRollSlotCountForStat(
        string itemClass,
        string affixType,
        string statTemplate,
        IReadOnlyList<AffixLibraryEntry> entries) =>
        CountSlotsFromRangeString(GetTierRangeStringForStat(itemClass, affixType, statTemplate, entries));

    public static List<string> GetAffixTypesForItemClass(string itemClass, IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (string.IsNullOrEmpty(itemClass))
            return new List<string>();

        return entries
            .Where(e => e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)))
            .Select(e => e.AffixType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Все уникальные строки стата из библиотеки для пары (класс, тип модификатора).</summary>
    public static List<string> GetStatTemplatesForClassAndType(
        string itemClass,
        string affixType,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (string.IsNullOrEmpty(itemClass) || string.IsNullOrEmpty(affixType))
            return new List<string>();

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries.Where(en =>
                     en.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)) &&
                     string.Equals(en.AffixType, affixType, StringComparison.Ordinal)))
        {
            foreach (var st in e.AffixStats)
                set.Add(st);
        }

        return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Записи библиотеки с не менее чем двумя строками стата (для UI «целый модификатор»).
    /// </summary>
    public static List<AffixLibraryEntry> GetMultiStatEntriesForClassAndType(
        string itemClass,
        string affixType,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        if (string.IsNullOrEmpty(itemClass) || string.IsNullOrEmpty(affixType))
            return new List<AffixLibraryEntry>();

        return entries
            .Where(e => e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)) &&
                        string.Equals(e.AffixType, affixType, StringComparison.Ordinal) &&
                        e.AffixStats.Count >= 2)
            .OrderBy(e => e.AffixName, StringComparer.Ordinal)
            .ThenBy(e => e.AffixTier)
            .ToList();
    }

    /// <summary>Число перекатов для строки стата по индексу в записи библиотеки.</summary>
    public static int GetRollSlotCountForEntryStat(AffixLibraryEntry entry, int statIndex)
    {
        var range = statIndex >= 0 && statIndex < entry.AffixRanges.Count ? entry.AffixRanges[statIndex] : null;
        return CountSlotsFromRangeString(range);
    }

    /// <summary>
    /// Множество пар (имя, тир) из библиотеки: записи с данным классом, типом и строкой стата.
    /// </summary>
    public static HashSet<(string Name, int Tier)> GetCandidateNameTiers(
        string itemClass,
        string affixType,
        string statTemplate,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        var set = new HashSet<(string, int)>();
        if (string.IsNullOrEmpty(itemClass) || string.IsNullOrEmpty(affixType) || string.IsNullOrEmpty(statTemplate))
            return set;

        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)))
                continue;
            if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                continue;

            var match = e.AffixStats.Any(st =>
                string.Equals(st.Trim(), statTemplate.Trim(), StringComparison.Ordinal) ||
                ParsedItemCraftEvaluator.StatLineMatchesTemplate(st, statTemplate));
            if (match)
                set.Add((e.AffixName.Trim(), e.AffixTier));
        }

        return set;
    }

    private static readonly Regex NumericTokensInRange = new(@"[\d.]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Все числа из строки affixRanges (включая X(37-55); Y(63-94)) — для объединения границ ползунка.</summary>
    public static void AccumulateNumericBoundsFromRangeString(string? range, ref bool any, ref double globalMin, ref double globalMax)
    {
        if (string.IsNullOrWhiteSpace(range))
            return;
        foreach (Match m in NumericTokensInRange.Matches(range))
        {
            if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                continue;
            if (!any)
            {
                any = true;
                globalMin = globalMax = v;
            }
            else
            {
                globalMin = Math.Min(globalMin, v);
                globalMax = Math.Max(globalMax, v);
            }
        }
    }

    public static int FindStatIndexInEntry(AffixLibraryEntry e, string statTemplate)
    {
        for (var i = 0; i < e.AffixStats.Count; i++)
        {
            var st = e.AffixStats[i];
            if (string.Equals(st.Trim(), statTemplate.Trim(), StringComparison.Ordinal) ||
                ParsedItemCraftEvaluator.StatLineMatchesTemplate(st, statTemplate))
                return i;
        }

        return -1;
    }

    /// <summary>Минимум и максимум по всем числам в affixRanges выбранной строки стата для выбранных имён и тира.</summary>
    public static (double Min, double Max) GetUnionRollBoundsForSingleStat(
        string itemClass,
        string affixType,
        string statTemplate,
        IEnumerable<string> affixNames,
        int affixTier,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        var any = false;
        var gMin = 0.0;
        var gMax = 0.0;
        foreach (var rawName in affixNames)
        {
            var name = (rawName ?? "").Trim();
            if (name.Length == 0)
                continue;
            foreach (var e in entries)
            {
                if (!e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)))
                    continue;
                if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                    continue;
                if (e.AffixTier != affixTier)
                    continue;
                if (!string.Equals(e.AffixName.Trim(), name, StringComparison.Ordinal))
                    continue;
                var si = FindStatIndexInEntry(e, statTemplate);
                if (si < 0)
                    continue;
                var r = si < e.AffixRanges.Count ? e.AffixRanges[si] : null;
                AccumulateNumericBoundsFromRangeString(r, ref any, ref gMin, ref gMax);
            }
        }

        if (!any)
            return (0, 100);
        if (gMin > gMax)
            (gMin, gMax) = (gMax, gMin);
        return (gMin, gMax);
    }

    public static List<int> GetDistinctTiersForClassTypeStat(
        string itemClass,
        string affixType,
        string statTemplate,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        var set = new HashSet<int>();
        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)))
                continue;
            if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                continue;
            if (FindStatIndexInEntry(e, statTemplate) < 0)
                continue;
            set.Add(e.AffixTier);
        }

        return set.OrderBy(x => x).ToList();
    }

    public static List<string> GetAffixNamesForClassTypeStatTier(
        string itemClass,
        string affixType,
        string statTemplate,
        int affixTier,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(c => string.Equals(c, itemClass, StringComparison.Ordinal)))
                continue;
            if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                continue;
            if (e.AffixTier != affixTier)
                continue;
            if (FindStatIndexInEntry(e, statTemplate) < 0)
                continue;
            names.Add(e.AffixName.Trim());
        }

        return names.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>Одинаковый набор affixStats (порядок и строки) — для мультивыбора имён в целом модификаторе.</summary>
    public static bool EntriesShareSameAffixStats(AffixLibraryEntry a, AffixLibraryEntry b)
    {
        if (a.AffixStats.Count != b.AffixStats.Count)
            return false;
        for (var i = 0; i < a.AffixStats.Count; i++)
        {
            if (!string.Equals(a.AffixStats[i].Trim(), b.AffixStats[i].Trim(), StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public static AffixLibraryEntry? FindMultiStatEntryByNameTierType(
        string itemClass,
        string affixType,
        string affixName,
        int affixTier,
        IReadOnlyList<AffixLibraryEntry> entries) =>
        AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            entries,
            itemClass,
            affixType,
            affixName,
            affixTier);

    /// <summary>Объединение числовых границ по строке стата целого модификатора для набора имён (одна запись-эталон по stat index).</summary>
    public static (double Min, double Max) GetUnionRollBoundsForWholeLine(
        string itemClass,
        string affixType,
        AffixLibraryEntry referenceEntry,
        int statIndex,
        IEnumerable<string> affixNames,
        int affixTier,
        IReadOnlyList<AffixLibraryEntry> entries)
    {
        var any = false;
        var gMin = 0.0;
        var gMax = 0.0;
        if (statIndex < 0 || statIndex >= referenceEntry.AffixStats.Count)
            return (0, 100);
        var refStat = referenceEntry.AffixStats[statIndex].Trim();
        foreach (var rawName in affixNames)
        {
            var name = (rawName ?? "").Trim();
            if (name.Length == 0)
                continue;
            var e = FindMultiStatEntryByNameTierType(itemClass, affixType, name, affixTier, entries);
            if (e is null || !EntriesShareSameAffixStats(referenceEntry, e))
                continue;
            var r = statIndex < e.AffixRanges.Count ? e.AffixRanges[statIndex] : null;
            AccumulateNumericBoundsFromRangeString(r, ref any, ref gMin, ref gMax);
        }

        if (!any)
            return (0, 100);
        if (gMin > gMax)
            (gMin, gMax) = (gMax, gMin);
        return (gMin, gMax);
    }
}
