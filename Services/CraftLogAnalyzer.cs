using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Результат одного успешного крафта, извлечённый из лог-файла сессии.</summary>
public sealed record CraftCompletion(
    /// <summary>TypeLine magic-предмета (e.g., "Rapturous Vile Robe") — совпадает с SaleRecord.TypeLine.</summary>
    string TypeLine,
    string ItemClass,
    IReadOnlyDictionary<string, int> OrbCounts,
    DateTime Timestamp);

/// <summary>
/// Парсит лог-файлы сессий из папки Log/, извлекает успешные крафты:
/// какой предмет получился и сколько каких орбов потрачено.
/// </summary>
public static class CraftLogAnalyzer
{
    private static readonly Regex TimestampRx =
        new(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\t", RegexOptions.Compiled);

    private static readonly Regex OrbRx =
        new(@"применяем (.+?)(?:\s*[\.\(]|$)", RegexOptions.Compiled);

    public static List<CraftCompletion> AnalyzeRecentLogs(int days = 4)
    {
        var logDir = ProjectPaths.GetLogDirectory();
        var cutoff = DateTime.Now.AddDays(-days);

        var files = Directory.EnumerateFiles(logDir, "session_*.txt")
            .Concat(Directory.EnumerateFiles(logDir, "session_*_wip.tmp"))
            .Where(f => File.GetLastWriteTime(f) >= cutoff)
            .OrderBy(f => f);

        var all = new List<CraftCompletion>();
        foreach (var f in files)
            all.AddRange(ParseFile(f));
        return all;
    }

    private static List<CraftCompletion> ParseFile(string path)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch { return []; }

        var results    = new List<CraftCompletion>();
        var orbCounts  = new Dictionary<string, int>(StringComparer.Ordinal);
        ParsedItem? currentItem = null;
        bool collecting = false;
        var clipLines   = new List<string>();

        void FlushClipboard()
        {
            if (!collecting) return;
            collecting = false;
            if (clipLines.Count == 0) { clipLines.Clear(); return; }

            var parsed = ItemParser.Parse(string.Join("\n", clipLines));
            clipLines.Clear();

            // Учитываем только magic-предметы — именно они продаются как синие базы
            if (parsed is { IsValid: true, Rarity: "Magic" })
                currentItem = parsed;
        }

        foreach (var raw in lines)
        {
            if (TimestampRx.IsMatch(raw))
            {
                FlushClipboard();

                var tab = raw.IndexOf('\t');
                if (tab < 0) continue;
                var content = raw[(tab + 1)..];

                if (content.Contains("применяем "))
                {
                    var m = OrbRx.Match(content);
                    if (m.Success)
                    {
                        var orb = m.Groups[1].Value.Trim();
                        orbCounts[orb] = orbCounts.GetValueOrDefault(orb) + 1;
                    }
                }
                else if (content.Contains("Условие выполнено") && currentItem != null)
                {
                    // Для magic-предмета Name = TypeLine (e.g. "Rapturous Vile Robe")
                    var typeLine = currentItem.Name;
                    if (!string.IsNullOrEmpty(typeLine))
                    {
                        DateTime.TryParse(raw[..tab], out var ts);
                        results.Add(new CraftCompletion(
                            typeLine,
                            currentItem.ItemClass,
                            new Dictionary<string, int>(orbCounts),
                            ts));
                    }
                    orbCounts.Clear();
                    currentItem = null;
                }
                else if (content.Contains("--- запуск крафта"))
                {
                    orbCounts.Clear();
                    currentItem = null;
                }
                else if (content.StartsWith("[Буфер —"))
                {
                    collecting = true;
                    clipLines.Clear();
                }
            }
            else if (collecting)
            {
                clipLines.Add(raw);
            }
        }

        FlushClipboard();
        return results;
    }

    /// <summary>Стоимость крафта в div по ценам PoeNinja.</summary>
    public static decimal CalcCostDiv(CraftCompletion c)
    {
        var total = 0m;
        foreach (var (orb, count) in c.OrbCounts)
        {
            var price = PoeNinjaPriceService.GetPrice(orb)?.DivineValue ?? 0m;
            total += count * price;
        }
        return total;
    }
}
