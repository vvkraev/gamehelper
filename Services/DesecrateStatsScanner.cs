using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Читает JSONL-логи десекрейт-ревилов из trade_data/ и строит статистику частоты
/// аффиксов десекрейт-пула. Результат сохраняется в desecrate_stats.json.
/// </summary>
public static class DesecrateStatsScanner
{
    private static readonly string _statsFile =
        Path.Combine(ProjectPaths.GetProjectRoot(), "desecrate_stats.json");

    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static volatile DesecrateStatsData _current = new();

    public static DesecrateStatsData Current => _current;

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

    private static async Task<bool> ScanUnprocessedAsync(DesecrateStatsData data)
    {
        var tradeDir = Path.Combine(ProjectPaths.GetProjectRoot(), "trade_data");
        if (!Directory.Exists(tradeDir)) return false;

        var files = Directory.GetFiles(tradeDir, "*desecrate*log*.jsonl");
        var changed = false;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var allLines = await File.ReadAllLinesAsync(file).ConfigureAwait(false);
            data.ProcessedLineCount.TryGetValue(name, out var knownCount);

            if (allLines.Length <= knownCount) continue;

            for (var i = knownCount; i < allLines.Length; i++)
            {
                var line = allLines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                ProcessLine(line, data);
            }

            data.ProcessedLineCount[name] = allLines.Length;
            changed = true;
        }

        return changed;
    }

    private static void ProcessLine(string jsonLine, DesecrateStatsData data)
    {
        try
        {
            var node = JsonNode.Parse(jsonLine);
            if (node is null) return;

            var mods = node["mods"]?.AsArray();
            if (mods is null || mods.Count == 0) return;

            // Восстанавливаем библиотечный класс из полей лога
            var item = node["item"]?.GetValue<string>() ?? "";
            var itemClass = node["item_class"]?.GetValue<string>() ?? "";
            var libraryClass = ResolveLibraryClass(item, itemClass);
            if (string.IsNullOrEmpty(libraryClass)) return;

            // Загружаем десекрейт-записи для этого класса один раз
            var desecrateEntries = GetDesecrateEntries(libraryClass);
            if (desecrateEntries.Count == 0) return;

            var cs = data.GetOrCreate(libraryClass);

            foreach (var modNode in mods)
            {
                var modText = modNode?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(modText)) continue;

                var match = FindDesecrateMatch(modText, desecrateEntries);
                if (match is null) continue;

                cs.TotalReveals++;
                cs.AffixCounts.TryGetValue(match.AffixName, out var nc);
                cs.AffixCounts[match.AffixName] = nc + 1;

                var statKey = NormalizeStat(modText);
                cs.StatCounts.TryGetValue(statKey, out var sc);
                cs.StatCounts[statKey] = sc + 1;

                break; // в каждом reveal ровно один десекрейт-аффикс
            }
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"DesecrateStatsScanner: ошибка в строке: {ex.Message}");
        }
    }

    /// <summary>
    /// Пытается сопоставить текст мода с любой записью десекрейт-пула.
    /// Учитывает шаблоны с "#" (заменяет на паттерн числа) и точные совпадения.
    /// </summary>
    public static AffixLibraryEntry? FindDesecrateMatch(string modText, List<AffixLibraryEntry> entries)
    {
        var normalizedMod = NormalizeStat(modText);

        foreach (var entry in entries)
        {
            foreach (var stat in entry.AffixStats)
            {
                if (string.IsNullOrEmpty(stat)) continue;

                if (stat.Contains('#'))
                {
                    // Шаблон с "#": превращаем в regex, где "#" = любое число
                    if (MatchesTemplate(normalizedMod, stat))
                        return entry;
                }
                else
                {
                    if (string.Equals(NormalizeStat(stat), normalizedMod, StringComparison.OrdinalIgnoreCase))
                        return entry;
                }
            }
        }
        return null;
    }

    private static bool MatchesTemplate(string normalizedText, string template)
    {
        // Escape regex special chars, then replace "\#" with number pattern
        var escaped = Regex.Escape(NormalizeStat(template));
        var pattern = "^" + escaped.Replace(@"\#", @"[\d.,]+") + "$";
        try
        {
            return Regex.IsMatch(normalizedText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeStat(string s) =>
        s.Trim().ToLowerInvariant();

    public static List<AffixLibraryEntry> GetDesecrateEntries(string libraryClass)
    {
        return AffixLibrary.GetEntries()
            .Where(e => e.AffixType.Contains("Desecrated", StringComparison.OrdinalIgnoreCase)
                     && e.ItemClasses.Contains(libraryClass, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Маппинг из полей лога (item + item_class) в библиотечный класс предмета.</summary>
    private static string ResolveLibraryClass(string item, string itemClass)
    {
        // Конкретные джевелы
        return item switch
        {
            "Time-Lost Sapphire" => "Time-Lost Sapphire Jewels",
            "Time-Lost Ruby"     => "Time-Lost Ruby Jewels",
            "Time-Lost Emerald"  => "Time-Lost Emerald Jewels",
            "Time-Lost Diamond"  => "Time-Lost Diamond Jewels",
            "Sapphire Jewel"     => "Sapphire Jewels",
            "Ruby Jewel"         => "Ruby Jewels",
            "Emerald Jewel"      => "Emerald Jewels",
            "Diamond Jewel"      => "Diamond Jewels",
            "Ring"               => "Rings",
            "Amulet"             => "Amulets",
            "Belt"               => "Belts",
            _ => ResolveFromClass(item, itemClass),
        };
    }

    private static string ResolveFromClass(string item, string itemClass) =>
        itemClass switch
        {
            "Jewels"      => string.IsNullOrEmpty(item) ? "Jewels" : item,
            "Rings"       => "Rings",
            "Amulets"     => "Amulets",
            "Belts"       => "Belts",
            _ when !string.IsNullOrEmpty(itemClass) => itemClass,
            _ => "",
        };

    private static async Task<DesecrateStatsData> LoadAsync()
    {
        if (!File.Exists(_statsFile)) return new DesecrateStatsData();
        try
        {
            var json = await File.ReadAllTextAsync(_statsFile).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<DesecrateStatsData>(json);
            if (data is null || data.Version != new DesecrateStatsData().Version)
                return new DesecrateStatsData();
            return data;
        }
        catch
        {
            return new DesecrateStatsData();
        }
    }

    private static Task SaveAsync(DesecrateStatsData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(_statsFile, json);
    }
}
