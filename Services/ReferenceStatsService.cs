using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>Одна строка наблюдений в справочнике.</summary>
public sealed record ReferenceEntry(
    string Outcome,
    int Count,
    string? Notes  = null,
    decimal Price  = 0m,   // ex-цена (для категории Перековка)
    decimal Roi    = 0m,   // RoI 3→1 в % (для категории Перековка)
    bool IsSummary = false // итоговая строка — не участвует в расчёте вероятности
);

/// <summary>Одна категория — один JSON-файл из docs/stats/ или виртуальная.</summary>
public sealed record ReferenceCategory(
    string DisplayName,
    string CategoryPath,
    string Updated,
    int TotalSamples,
    IReadOnlyList<ReferenceEntry> Entries,
    string FilePath);

/// <summary>View-model строки для DataGrid во вкладке Справочник.</summary>
public sealed class ReferenceEntryRow
{
    public string Outcome        { get; init; } = "";
    public int    Count          { get; init; }
    public string ProbabilityPct { get; init; } = "";
    public string Ci95           { get; init; } = "";
    public string Price          { get; init; } = "";
    public string Roi            { get; init; } = "";
    public string Notes          { get; init; } = "";

    // Числовые ключи для корректной сортировки в DataGrid
    public double ProbabilitySort { get; init; }
    public double PriceSort       { get; init; }
    public double RoiSort         { get; init; }

    public static ReferenceEntryRow From(ReferenceEntry e, int total)
    {
        var p  = (!e.IsSummary && total > 0) ? e.Count * 100.0 / total : 0.0;
        var ci = (!e.IsSummary && total > 0 && e.Count > 0)
            ? 1.96 * Math.Sqrt(p / 100.0 * (1.0 - p / 100.0) / total) * 100.0
            : 0.0;
        return new ReferenceEntryRow
        {
            Outcome         = e.Outcome,
            Count           = e.IsSummary ? 0 : e.Count,
            ProbabilityPct  = e.IsSummary ? "" : $"{p:F1}",
            Ci95            = ci >= 0.05 ? $"±{ci:F1}" : "",
            Price           = e.Price > 0 ? $"{e.Price:0.##}" : "",
            Roi             = e.Roi   > 0 ? $"{e.Roi:F0}%" : "",
            Notes           = e.Notes ?? "",
            ProbabilitySort = e.IsSummary ? -1.0 : p,
            PriceSort       = (double)e.Price,
            RoiSort         = (double)e.Roi,
        };
    }
}

/// <summary>Загружает JSON-файлы категорий из папки docs/stats/.</summary>
public static class ReferenceStatsService
{
    public static List<ReferenceCategory> LoadAll(string statsDir)
    {
        if (!Directory.Exists(statsDir)) return [];
        var result = new List<ReferenceCategory>();
        foreach (var file in Directory.GetFiles(statsDir, "*.json").Order())
        {
            try
            {
                var cat = LoadFile(file);
                if (cat != null) result.Add(cat);
            }
            catch { /* пропустить повреждённые файлы */ }
        }
        return result;
    }

    public static ReferenceCategory? LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc    = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var displayName  = Str(root, "display_name");
        var categoryPath = Str(root, "category_path");
        var updated      = Str(root, "updated");
        var total        = root.TryGetProperty("total_samples", out var ts) ? ts.GetInt32() : 0;

        var entries = new List<ReferenceEntry>();
        if (root.TryGetProperty("entries", out var arr))
        {
            foreach (var e in arr.EnumerateArray())
            {
                entries.Add(new ReferenceEntry(
                    Outcome: Str(e, "outcome"),
                    Count:   e.TryGetProperty("count", out var c) ? c.GetInt32() : 0,
                    Notes:   e.TryGetProperty("notes", out var n) ? n.GetString() : null));
            }
        }

        return new ReferenceCategory(displayName, categoryPath, updated, total, entries, path);
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
}
