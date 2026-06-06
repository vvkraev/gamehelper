using System.Text.Json.Serialization;

namespace GameHelper.Services;

public sealed class AffixStatsData
{
    public int Version { get; set; } = 1;

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

    [JsonPropertyName("affixCounts")]
    public Dictionary<string, int> AffixCounts { get; set; } = new(StringComparer.Ordinal);
}
