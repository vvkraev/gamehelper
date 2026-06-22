using System.Text.Json.Serialization;

namespace GameHelper.Services;

public sealed class AffixStatsData
{
    // v4: PerClass key is "ItemClass|SubType" for items with a subtype, or just "ItemClass" otherwise.
    public int Version { get; set; } = 4;

    [JsonPropertyName("processedLogFiles")]
    public HashSet<string> ProcessedLogFiles { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("perClass")]
    public Dictionary<string, ClassStats> PerClass { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Строит ключ для <see cref="PerClass"/>:
    /// "ItemClass|SubType" если подтип не пустой, иначе просто "ItemClass".
    /// </summary>
    public static string MakeClassKey(string itemClass, string itemSubType)
        => string.IsNullOrEmpty(itemSubType) ? itemClass : $"{itemClass}|{itemSubType}";

    public ClassStats GetOrCreate(string itemClass, string itemSubType = "")
    {
        var key = MakeClassKey(itemClass, itemSubType);
        if (!PerClass.TryGetValue(key, out var s))
            PerClass[key] = s = new ClassStats();
        return s;
    }

    /// <summary>Returns (count of appearances, total item snapshots for this class+subtype).</summary>
    public (int count, int total) GetCounts(string itemClass, string itemSubType, string affixName)
    {
        var key = MakeClassKey(itemClass, itemSubType);
        if (!PerClass.TryGetValue(key, out var cs)) return (0, 0);
        cs.AffixCounts.TryGetValue(affixName, out var count);
        return (count, cs.TotalSnapshots);
    }

    /// <summary>
    /// При загрузке файла версии ниже 4 сбрасывает статистику — старые данные не имеют
    /// разбивки по подтипу и смешивают несовместимые пулы аффиксов.
    /// </summary>
    public void MigrateIfNeeded()
    {
        if (Version < 4)
        {
            PerClass.Clear();
            ProcessedLogFiles.Clear();
            Version = 4;
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
