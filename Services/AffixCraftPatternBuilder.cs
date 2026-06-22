using System.Globalization;

using System.Linq;

namespace GameHelper.Services;

/// <summary>
/// Поиск записи в библиотеке и сборка строки шаблона с <c>n</c> для <see cref="AffixMatch"/>.
/// </summary>
public static class AffixCraftPatternBuilder
{
    public static AffixLibraryEntry? FindEntry(
        IReadOnlyList<AffixLibraryEntry> entries,
        string itemClass,
        string affixType,
        string affixStat,
        int tier)
    {
        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(x => string.Equals(x, itemClass, StringComparison.Ordinal)))
                continue;
            if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                continue;
            if (e.AffixTier != tier)
                continue;
            if (e.AffixStats.Any(s => string.Equals(s, affixStat, StringComparison.Ordinal)))
                return e;
        }

        return null;
    }

    /// <summary>Запись по классу предмета, типу модификатора, имени и тиру (для режима «целый модификатор»).</summary>
    public static AffixLibraryEntry? FindEntryByNameAndTier(
        IReadOnlyList<AffixLibraryEntry> entries,
        string itemClass,
        string affixType,
        string affixName,
        int tier)
    {
        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(x => string.Equals(x, itemClass, StringComparison.Ordinal)))
                continue;
            if (!string.Equals(e.AffixType, affixType, StringComparison.Ordinal))
                continue;
            if (e.AffixTier != tier)
                continue;
            if (string.Equals(e.AffixName.Trim(), affixName.Trim(), StringComparison.Ordinal))
                return e;
        }

        return null;
    }

    /// <summary>
    /// Как <see cref="FindEntryByNameAndTier"/>, но если точного совпадения типа нет — ищет запись с тем же именем/тиром
    /// и совместимым типом префикса/суффикса (Prefix ↔ Desecrated Prefix и т.д.).
    /// </summary>
    public static AffixLibraryEntry? FindEntryByNameAndTierTypeCompatible(
        IReadOnlyList<AffixLibraryEntry> entries,
        string itemClass,
        string affixType,
        string affixName,
        int tier)
    {
        var exact = FindEntryByNameAndTier(entries, itemClass, affixType, affixName, tier);
        if (exact is not null)
            return exact;

        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(x => string.Equals(x, itemClass, StringComparison.Ordinal)))
                continue;
            if (!ParsedItemCraftEvaluator.AffixTypesCompatibleForNamedMatch(affixType, e.AffixType))
                continue;
            if (e.AffixTier != tier)
                continue;
            if (string.Equals(e.AffixName.Trim(), affixName.Trim(), StringComparison.Ordinal))
                return e;
        }

        return null;
    }

    /// <summary>Запись по классу предмета, типу и имени без фильтрации по тиру (имя однозначно задаёт тир в PoE2).</summary>
    public static AffixLibraryEntry? FindEntryByNameTypeAnyTier(
        IReadOnlyList<AffixLibraryEntry> entries,
        string itemClass,
        string affixType,
        string affixName)
    {
        foreach (var e in entries)
        {
            if (!e.ItemClasses.Any(x => string.Equals(x, itemClass, StringComparison.Ordinal)))
                continue;
            if (!ParsedItemCraftEvaluator.AffixTypesCompatibleForNamedMatch(affixType, e.AffixType))
                continue;
            if (string.Equals(e.AffixName.Trim(), affixName.Trim(), StringComparison.Ordinal))
                return e;
        }

        return null;
    }

    public static int GetStatIndex(AffixLibraryEntry e, string affixStat)
    {
        for (var i = 0; i < e.AffixStats.Count; i++)
        {
            if (string.Equals(e.AffixStats[i], affixStat, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Строит шаблон: при наличии диапазона тира — <c>+n(мин-макс)…</c> или <c>n(мин-макс)…</c>; иначе подстрока без <c>n</c> или с <c>n</c> в тексте.
    /// </summary>
    public static string BuildPattern(AffixLibraryEntry e, int statIndex)
    {
        if (statIndex < 0 || statIndex >= e.AffixStats.Count)
            return "";

        var stat = e.AffixStats[statIndex].Trim();
        var range = statIndex < e.AffixRanges.Count ? e.AffixRanges[statIndex]?.Trim() : null;

        if (string.IsNullOrEmpty(range))
        {
            if (stat.Contains('n', StringComparison.Ordinal))
                return stat;
            if (stat.StartsWith("+ ", StringComparison.Ordinal))
                return "+n" + stat[1..];
            return stat;
        }

        if (stat.StartsWith("%", StringComparison.Ordinal))
            return $"+n({range}){stat}";

        return $"n({range}){stat}";
    }

    /// <summary>
    /// Диапазон тира задаёт дробный порог, если любая из границ содержит десятичную точку.
    /// </summary>
    public static bool RangeAllowsDecimal(string? range)
    {
        if (string.IsNullOrEmpty(range))
            return false;
        var parts = range.Split('-', 2);
        return parts.Length > 0 && parts[0].Contains('.', StringComparison.Ordinal)
               || (parts.Length > 1 && parts[1].Contains('.', StringComparison.Ordinal));
    }

    public static bool TryParseMinRoll(string text, bool allowDecimal, out double value)
    {
        value = 0;
        text = text.Trim();
        if (text.Length == 0)
            return false;

        if (allowDecimal)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
        {
            value = iv;
            return true;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out iv))
        {
            value = iv;
            return true;
        }

        return false;
    }
}

