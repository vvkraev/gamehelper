using System.Text.Json.Serialization;

namespace GameHelper.Services;

public sealed class AffixStatsData
{
    public int Version { get; set; } = 3; // v3: fractured affixes excluded from counts

    [JsonPropertyName("processedLogFiles")]
    public HashSet<string> ProcessedLogFiles { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("perClass")]
    public Dictionary<string, ClassStats> PerClass { get; set; } = new(StringComparer.Ordinal);

    public ClassStats GetOrCreate(string itemClass)
    {
        if (!PerClass.TryGetValue(itemClass, out var s))
            PerClass[itemClass] = s = new ClassStats();
        return s;
    }

    /// <summary>Returns (count of appearances, total item snapshots for this class).</summary>
    public (int count, int total) GetCounts(string itemClass, string affixName)
    {
        if (!PerClass.TryGetValue(itemClass, out var cs)) return (0, 0);
        cs.AffixCounts.TryGetValue(affixName, out var count);
        return (count, cs.TotalSnapshots);
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
