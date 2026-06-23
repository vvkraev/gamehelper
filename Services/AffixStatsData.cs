using System.Text.Json.Serialization;

namespace GameHelper.Services;

public sealed class AffixStatsData
{
    // v5: PerClass key is "ItemClass|SubType|OrbName" (or "ItemClass|OrbName" / "ItemClass" when absent).
    public int Version { get; set; } = 5;

    [JsonPropertyName("processedLogFiles")]
    public HashSet<string> ProcessedLogFiles { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("perClass")]
    public Dictionary<string, ClassStats> PerClass { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Строит ключ для <see cref="PerClass"/>:
    /// "ItemClass|SubType|OrbName", пропуская пустые сегменты.
    /// </summary>
    public static string MakeClassKey(string itemClass, string itemSubType, string orbName = "")
    {
        var parts = new List<string>(3) { itemClass };
        if (!string.IsNullOrEmpty(itemSubType)) parts.Add(itemSubType);
        if (!string.IsNullOrEmpty(orbName)) parts.Add(orbName);
        return string.Join("|", parts);
    }

    public ClassStats GetOrCreate(string itemClass, string itemSubType = "", string orbName = "")
    {
        var key = MakeClassKey(itemClass, itemSubType, orbName);
        if (!PerClass.TryGetValue(key, out var s))
            PerClass[key] = s = new ClassStats();
        return s;
    }

    /// <summary>Returns (count of appearances, total item snapshots for this class+subtype+orb).</summary>
    public (int count, int total) GetCounts(string itemClass, string itemSubType, string affixName, string orbName = "")
    {
        var key = MakeClassKey(itemClass, itemSubType, orbName);
        if (!PerClass.TryGetValue(key, out var cs)) return (0, 0);
        cs.AffixCounts.TryGetValue(affixName, out var count);
        return (count, cs.TotalSnapshots);
    }

    /// <summary>
    /// При загрузке данных старой версии сбрасывает статистику — данные пересобираются
    /// из лог-файлов с новым форматом ключа.
    /// </summary>
    public void MigrateIfNeeded()
    {
        if (Version < 5)
        {
            PerClass.Clear();
            ProcessedLogFiles.Clear();
            Version = 5;
        }
    }
}

public sealed class ClassStats
{
    [JsonPropertyName("totalSnapshots")]
    public int TotalSnapshots { get; set; }

    /// <summary>Количество предметов, на которых встречался аффикс с данным именем (любой вариант).</summary>
    [JsonPropertyName("affixCounts")]
    public Dictionary<string, int> AffixCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Количество предметов, на которых встречался конкретный стат-шаблон аффикса.
    /// Ключ: <c>"AffixName|normalizedStatText"</c> — позволяет различать, например,
    /// «Thrud's — Speed» от «Thrud's — Critical» у одного и того же аффикса.
    /// </summary>
    [JsonPropertyName("statTemplateCounts")]
    public Dictionary<string, int> StatTemplateCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Точное количество для данного аффикса + шаблона стата.
    /// Шаблон нормализуется так же, как при сохранении (lowercase, trim, без '#').
    /// </summary>
    public int GetStatCount(string affixName, string statTemplate)
    {
        StatTemplateCounts.TryGetValue(MakeStatKey(affixName, statTemplate), out var c);
        return c;
    }

    /// <summary>Формирует ключ словаря из имени аффикса и шаблона стата.</summary>
    public static string MakeStatKey(string affixName, string statTemplate)
    {
        var norm = statTemplate.Trim().ToLowerInvariant()
            .Replace("#", "", StringComparison.Ordinal).Trim();
        while (norm.Contains("  ", StringComparison.Ordinal))
            norm = norm.Replace("  ", " ", StringComparison.Ordinal);
        return affixName + "|" + norm;
    }
}
