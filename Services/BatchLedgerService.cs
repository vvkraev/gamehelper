using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHelper.Services;

public sealed class BatchLedger
{
    [JsonPropertyName("batchId")]      public string BatchId { get; set; } = "";
    [JsonPropertyName("pipelineName")] public string PipelineName { get; set; } = "";
    [JsonPropertyName("createdAt")]    public string CreatedAt { get; set; } = "";
    [JsonPropertyName("status")]       public string Status { get; set; } = "in_progress";
    [JsonPropertyName("runs")]         public List<BatchRun> Runs { get; set; } = [];

    [JsonIgnore] public int TotalDoneCount   => Runs.Sum(r => r.DoneCount);
    [JsonIgnore] public int TotalFailedCount => Runs.Sum(r => r.FailedCount);
    [JsonIgnore] public decimal TotalCostDiv => Runs.Sum(r => r.TotalCostDiv);
    [JsonIgnore] public decimal AvgCostPerItemDiv =>
        TotalDoneCount > 0 ? Math.Round(TotalCostDiv / TotalDoneCount, 2) : 0m;
}

public sealed class BatchRun
{
    [JsonPropertyName("runAt")]        public string RunAt { get; set; } = "";
    [JsonPropertyName("doneCount")]    public int DoneCount { get; set; }
    [JsonPropertyName("failedCount")]  public int FailedCount { get; set; }
    [JsonPropertyName("totalCostDiv")] public decimal TotalCostDiv { get; set; }
    [JsonPropertyName("breakdown")]    public List<BatchCostBreakdown> Breakdown { get; set; } = [];
}

public sealed class BatchCostBreakdown
{
    [JsonPropertyName("currency")]     public string CurrencyName { get; set; } = "";
    [JsonPropertyName("uses")]         public int TotalUses { get; set; }
    [JsonPropertyName("costDiv")]      public decimal CostDiv { get; set; }
}

public static class BatchLedgerService
{
    public static string FilePath =>
        Path.Combine(ProjectPaths.GetProjectRoot(), "batch_ledger.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static List<BatchLedger> LoadAll()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<BatchLedger>>(json, Opts) ?? [];
        }
        catch { return []; }
    }

    public static void Save(List<BatchLedger> batches) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(batches, Opts));

    /// <summary>
    /// Добавляет запись о запуске в батч с данным именем. Создаёт батч если не существует.
    /// Возвращает обновлённый батч.
    /// </summary>
    public static BatchLedger AppendRun(string batchId, BatchRunResult result)
    {
        var all = LoadAll();
        var batch = all.FirstOrDefault(b => string.Equals(b.BatchId, batchId, StringComparison.OrdinalIgnoreCase));

        if (batch is null)
        {
            batch = new BatchLedger
            {
                BatchId      = batchId,
                PipelineName = result.PipelineName,
                CreatedAt    = DateTime.Now.ToString("yyyy-MM-dd"),
            };
            all.Add(batch);
        }

        // Группируем расходы по валютам для этого запуска
        var costByName = result.Items
            .SelectMany(i => i.CostRecords)
            .GroupBy(r => r.CurrencyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BatchCostBreakdown
            {
                CurrencyName = g.Key,
                TotalUses    = g.Sum(r => r.Attempts),
                CostDiv      = g.Sum(r => r.CostDiv),
            })
            .OrderByDescending(b => b.CostDiv)
            .ToList();

        batch.Runs.Add(new BatchRun
        {
            RunAt        = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            DoneCount    = result.DoneCount,
            FailedCount  = result.FailedCount,
            TotalCostDiv = result.TotalCostDiv,
            Breakdown    = costByName,
        });

        Save(all);
        return batch;
    }
}
