using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Collects catalyst reforge output statistics from session log files.
/// Tracks processed files so subsequent runs only parse new logs.
/// File: catalyst_reforge_stats.json
/// </summary>
public static class CatalystReforgeStatsScanner
{
    private static readonly string _statsFile =
        Path.Combine(ProjectPaths.GetProjectRoot(), "catalyst_reforge_stats.json");

    // Matches lines like: "[Reforge]   → Flesh Catalyst → Neural Catalyst"
    private static readonly Regex _reforgeOutputRegex =
        new(@"→ .+ → (.+Catalyst)\s*$", RegexOptions.Compiled);

    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static volatile CatalystReforgeStatsData _current = new();

    public static CatalystReforgeStatsData Current => _current;

    public static Task InitializeAsync() => RunScanAsync();

    public static Task ScanNewLogsAsync() => RunScanAsync();

    private static async Task RunScanAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var data = await LoadAsync().ConfigureAwait(false);
            var changed = await ScanUnprocessedAsync(data).ConfigureAwait(false);
            if (changed)
                await SaveAsync(data).ConfigureAwait(false);
            _current = data;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<bool> ScanUnprocessedAsync(CatalystReforgeStatsData data)
    {
        var logDir = ProjectPaths.GetLogDirectory();
        if (!Directory.Exists(logDir)) return false;

        var files = Directory.GetFiles(logDir, "session_*.txt");
        var changed = false;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (data.ProcessedLogFiles.Contains(name)) continue;
            await ProcessFileAsync(file, data).ConfigureAwait(false);
            data.ProcessedLogFiles.Add(name);
            changed = true;
        }

        return changed;
    }

    private static async Task ProcessFileAsync(string filePath, CatalystReforgeStatsData data)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath).ConfigureAwait(false);
            foreach (var line in lines)
            {
                var m = _reforgeOutputRegex.Match(line);
                if (!m.Success) continue;
                var output = m.Groups[1].Value.Trim();
                data.OutputCounts.TryGetValue(output, out var n);
                data.OutputCounts[output] = n + 1;
                data.TotalReforges++;
            }
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"CatalystReforgeStatsScanner: ошибка при обработке {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    private static async Task<CatalystReforgeStatsData> LoadAsync()
    {
        if (!File.Exists(_statsFile)) return new CatalystReforgeStatsData();
        try
        {
            var json = await File.ReadAllTextAsync(_statsFile).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CatalystReforgeStatsData>(json, _jsonOptions) ?? new CatalystReforgeStatsData();
        }
        catch
        {
            return new CatalystReforgeStatsData();
        }
    }

    private static Task SaveAsync(CatalystReforgeStatsData data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        return File.WriteAllTextAsync(_statsFile, json);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

public class CatalystReforgeStatsData
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("totalReforges")]
    public int TotalReforges { get; set; }

    [JsonPropertyName("processedLogFiles")]
    public HashSet<string> ProcessedLogFiles { get; set; } = [];

    [JsonPropertyName("outputCounts")]
    public Dictionary<string, int> OutputCounts { get; set; } = [];
}
