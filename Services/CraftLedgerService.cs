using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHelper.Services;

/// <summary>Запись журнала крафта из <c>craft_ledger.json</c>.</summary>
public sealed class CraftLedgerEntry
{
    [JsonPropertyName("id")]           public string Id { get; set; } = "";
    [JsonPropertyName("createdAt")]    public string CreatedAt { get; set; } = "";
    [JsonPropertyName("status")]       public string Status { get; set; } = "";
    [JsonPropertyName("saleRecordId")] public string? SaleRecordId { get; set; }
    [JsonPropertyName("totalCostDiv")] public decimal TotalCostDiv { get; set; }
    [JsonPropertyName("item")]         public LedgerItemSummary? Item { get; set; }
    [JsonPropertyName("steps")]        public List<LedgerStep> Steps { get; set; } = [];
    [JsonPropertyName("listing")]      public LedgerListing? Listing { get; set; }
    [JsonPropertyName("notes")]        public List<string> Notes { get; set; } = [];

    /// <summary>ID батча — одинаковый для всех предметов одного запуска BatchPipelineRunner.</summary>
    [JsonPropertyName("batchId")]      public string? BatchId { get; set; }
    /// <summary>Индекс ячейки стэша в батче (0-based).</summary>
    [JsonPropertyName("cellIndex")]    public int? CellIndex { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public string DisplayName => Item is not null
        ? $"{Item.Name} ({Item.BaseType}, iLvl {Item.Ilvl})"
        : Id;
}

public sealed class LedgerItemSummary
{
    [JsonPropertyName("name")]     public string Name { get; set; } = "";
    [JsonPropertyName("baseType")] public string BaseType { get; set; } = "";
    [JsonPropertyName("ilvl")]     public int Ilvl { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LedgerStep
{
    [JsonPropertyName("phase")]       public int Phase { get; set; }
    [JsonPropertyName("label")]       public string Label { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("costDiv")]     public decimal CostDiv { get; set; }
    [JsonPropertyName("resultState")] public string ResultState { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class LedgerListing
{
    [JsonPropertyName("askPriceDiv")] public decimal AskPriceDiv { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Загрузка и сохранение <c>craft_ledger.json</c>.</summary>
public static class CraftLedgerService
{
    public static string FilePath =>
        Path.Combine(ProjectPaths.GetProjectRoot(), "craft_ledger.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static List<CraftLedgerEntry> LoadFromFile()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<CraftLedgerEntry>>(json, Opts) ?? [];
        }
        catch { return []; }
    }

    public static void Save(List<CraftLedgerEntry> entries) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, Opts));

    /// <summary>
    /// Связывает запись журнала с продажей: записывает SaleRecordId и меняет status → sold.
    /// Сохраняет файл.
    /// </summary>
    public static void LinkToSale(List<CraftLedgerEntry> entries, string ledgerId, string saleItemId)
    {
        var entry = entries.FirstOrDefault(e => e.Id == ledgerId);
        if (entry is null) return;
        entry.SaleRecordId = saleItemId;
        if (string.Equals(entry.Status, "listed", StringComparison.OrdinalIgnoreCase))
            entry.Status = "sold";
        Save(entries);
    }

    /// <summary>Снимает привязку записи к продаже.</summary>
    public static void Unlink(List<CraftLedgerEntry> entries, string ledgerId)
    {
        var entry = entries.FirstOrDefault(e => e.Id == ledgerId);
        if (entry is null) return;
        entry.SaleRecordId = null;
        if (string.Equals(entry.Status, "sold", StringComparison.OrdinalIgnoreCase))
            entry.Status = "listed";
        Save(entries);
    }
}
