using System.Text.Json.Serialization;

namespace GameHelper.Services;

public sealed class DesecrateStatsData
{
    public int Version { get; set; } = 1;

    /// <summary>Суммарное число строк JSONL, обработанных в последний прогон.
    /// Используется для обнаружения новых строк без повторного сканирования.</summary>
    [JsonPropertyName("processedLineCount")]
    public Dictionary<string, int> ProcessedLineCount { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Статистика по классам предметов. Ключ — библиотечный класс предмета (напр. "Time-Lost Sapphire Jewels").</summary>
    [JsonPropertyName("perClass")]
    public Dictionary<string, DesecrateClassStats> PerClass { get; set; } = new(StringComparer.Ordinal);

    public DesecrateClassStats GetOrCreate(string libraryClass)
    {
        if (!PerClass.TryGetValue(libraryClass, out var s))
            PerClass[libraryClass] = s = new DesecrateClassStats();
        return s;
    }
}

public sealed class DesecrateClassStats
{
    /// <summary>Число reveal-попыток (строк лога), в которых удалось найти десекрейт-аффикс.</summary>
    [JsonPropertyName("totalReveals")]
    public int TotalReveals { get; set; }

    /// <summary>Сколько раз выпал аффикс с данным именем (по AffixName из библиотеки).</summary>
    [JsonPropertyName("affixCounts")]
    public Dictionary<string, int> AffixCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Сколько раз выпал конкретный текст стата (нормализованный, без значений).</summary>
    [JsonPropertyName("statCounts")]
    public Dictionary<string, int> StatCounts { get; set; } = new(StringComparer.Ordinal);
}
