using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>
/// Collects affix frequency statistics from chaos-craft log files.
/// Processed file names are persisted so subsequent runs only process new logs.
/// </summary>
public static class AffixStatsScanner
{
    private static readonly string _statsFile =
        Path.Combine(ProjectPaths.GetProjectRoot(), "affix_stats.json");

    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static volatile AffixStatsData _current = new();

    public static AffixStatsData Current => _current;

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

    private static async Task<bool> ScanUnprocessedAsync(AffixStatsData data)
    {
        var logDir = ProjectPaths.GetLogDirectory();
        if (!Directory.Exists(logDir)) return false;

        var files = Directory.GetFiles(logDir, "craft_*.txt");
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

    private static string ExtractOrbName(string text)
    {
        const string marker = "Орб: ";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return "";
        var nl = text.IndexOf('\n', idx);
        return (nl > idx ? text[(idx + marker.Length)..nl] : text[(idx + marker.Length)..]).Trim();
    }

    // Орбы, которые реролят состав аффиксов — только их снапшоты имеют смысл для весов.
    // Divine Orb, Fracturing Orb и т.п. меняют только числа или фиксируют аффикс, а не ролят пул.
    private static bool IsRerollOrb(string orbName) =>
        string.IsNullOrEmpty(orbName)
        || orbName.Contains("Chaos", StringComparison.OrdinalIgnoreCase)
        || orbName.Contains("Augmentation", StringComparison.OrdinalIgnoreCase);

    private static async Task ProcessFileAsync(string filePath, AffixStatsData data)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var orbName = ExtractOrbName(text);
            if (!IsRerollOrb(orbName)) return;
            foreach (var snapshot in ExtractSnapshots(text))
            {
                var item = ItemParser.Parse(snapshot);
                if (!item.IsValid || string.IsNullOrWhiteSpace(item.ItemClass)) continue;

                // Armour-slot items: subtype = defence layer (Armour / Evasion / Energy Shield / …).
                // Jewels: resolve specific library class from the base type name so coloured variants
                // (Sapphire Jewels, Time-Lost Sapphire Jewels, …) get their own stats bucket.
                // Generic "Jewel" / "Time-Lost Jewel" stay under "Jewels" but use the base name as subtype.
                string statsClass, subType;
                if (item.ItemClass == "Jewels")
                {
                    var effectiveBase = string.IsNullOrEmpty(item.Base) ? item.Name : item.Base;
                    (statsClass, subType) = ResolveJewelClassAndSubType(effectiveBase);
                }
                else if (AffixStatsData.SubTypeClasses.Contains(item.ItemClass))
                {
                    statsClass = item.ItemClass;
                    subType = item.ItemSubType;
                }
                else
                {
                    statsClass = item.ItemClass;
                    subType = "";
                }
                var cs = data.GetOrCreate(statsClass, subType, orbName);
                cs.TotalSnapshots++;
                foreach (var affix in item.Affixes)
                {
                    if (string.IsNullOrEmpty(affix.Name)) continue;
                    if (affix.IsFractured) continue; // зафиксирован — не результат хаос-ролла

                    cs.AffixCounts.TryGetValue(affix.Name, out var n);
                    cs.AffixCounts[affix.Name] = n + 1;

                    // Статистика по конкретному шаблону стата — позволяет различать варианты одного аффикса.
                    foreach (var effect in affix.EffectDetails)
                    {
                        if (string.IsNullOrWhiteSpace(effect.StatText)) continue;
                        var key = ClassStats.MakeStatKey(affix.Name, effect.StatText);
                        cs.StatTemplateCounts.TryGetValue(key, out var sn);
                        cs.StatTemplateCounts[key] = sn + 1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"AffixStatsScanner: ошибка при обработке {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    private static List<string> ExtractSnapshots(string text)
    {
        const string startMarker = "Содержимое буфера обмена после Ctrl+Alt+C:";
        const string endMarker   = "Пояснение проверки:";

        var list = new List<string>();
        var pos = 0;

        while (true)
        {
            var si = text.IndexOf(startMarker, pos, StringComparison.Ordinal);
            if (si < 0) break;
            var nl = text.IndexOf('\n', si);
            if (nl < 0) break;
            var ei = text.IndexOf(endMarker, nl, StringComparison.Ordinal);
            if (ei < 0) break;

            var snippet = text[(nl + 1)..ei].Trim();
            if (!string.IsNullOrEmpty(snippet))
                list.Add(snippet);
            pos = ei;
        }

        return list;
    }

    private static async Task<AffixStatsData> LoadAsync()
    {
        if (!File.Exists(_statsFile)) return new AffixStatsData();
        try
        {
            var json = await File.ReadAllTextAsync(_statsFile).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<AffixStatsData>(json);
            // При смене версии сбрасываем все данные — полный пересчёт из лог-файлов.
            if (data == null || data.Version != new AffixStatsData().Version)
                return new AffixStatsData();
            return data;
        }
        catch
        {
            return new AffixStatsData();
        }
    }

    private static Task SaveAsync(AffixStatsData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(_statsFile, json);
    }

    /// <summary>
    /// Возвращает (statsClass, subType) для джевела по его базовому типу.
    /// Цветные варианты (Sapphire Jewel, Time-Lost Sapphire, …) имеют отдельные классы в библиотеке —
    /// используем их напрямую. Для базовых «Jewel» / «Time-Lost Jewel» subType разделяет статистику.
    /// </summary>
    private static (string statsClass, string subType) ResolveJewelClassAndSubType(string effectiveBase) =>
        effectiveBase switch
        {
            "Sapphire Jewel"      => ("Sapphire Jewels",           ""),
            "Time-Lost Sapphire"  => ("Time-Lost Sapphire Jewels", ""),
            "Ruby Jewel"          => ("Ruby Jewels",               ""),
            "Time-Lost Ruby"      => ("Time-Lost Ruby Jewels",     ""),
            "Emerald Jewel"       => ("Emerald Jewels",            ""),
            "Time-Lost Emerald"   => ("Time-Lost Emerald Jewels",  ""),
            "Diamond Jewel"       => ("Diamond Jewels",            ""),
            "Time-Lost Diamond"   => ("Time-Lost Diamond Jewels",  ""),
            _                     => ("Jewels",                    effectiveBase),
        };
}
