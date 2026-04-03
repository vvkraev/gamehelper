using System.IO;
using System.Text.Json;
using GameHelper;

namespace GameHelper.Services;

/// <summary>
/// Текстовая библиотека аффиксов: загрузка/сохранение JSON, пополнение при разборе предмета.
/// </summary>
public static class AffixLibrary
{
    private static readonly object Gate = new();
    private static List<AffixLibraryEntry> _entries = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string FilePath => Path.Combine(ProjectPaths.GetProjectRoot(), "affix_library.json");

    /// <summary>Перечитать файл с диска (в т.ч. после правок в блокноте).</summary>
    public static void ReloadFromDisk()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    _entries = new List<AffixLibraryEntry>();
                    return;
                }

                var json = File.ReadAllText(FilePath);
                var root = JsonSerializer.Deserialize<AffixLibraryFile>(json, JsonOptions);
                _entries = root?.Entries ?? new List<AffixLibraryEntry>();
            }
            catch
            {
                _entries = new List<AffixLibraryEntry>();
            }
        }
    }

    /// <summary>Сохранить текущее состояние в файл.</summary>
    public static void SaveToDisk()
    {
        lock (Gate)
        {
            try
            {
                var root = new AffixLibraryFile { Version = 1, Entries = _entries };
                var json = JsonSerializer.Serialize(root, JsonOptions);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // нет прав и т.п.
            }
        }
    }

    /// <summary>Снимок текущих записей в памяти (после <see cref="ReloadFromDisk"/> или <see cref="MergeFromParsedItem"/>).</summary>
    public static IReadOnlyList<AffixLibraryEntry> GetEntries()
    {
        lock (Gate)
            return _entries.ToList();
    }

    public static int EntryCount
    {
        get
        {
            lock (Gate)
                return _entries.Count;
        }
    }

    /// <summary>
    /// Обновить библиотеку по разобранному предмету: новые сочетания (тип;имя;тир;статы) добавляются,
    /// для существующих — дополняется Item Class и при необходимости снижается Affix Tier Level.
    /// Перед слиянием перечитывает файл с диска.
    /// </summary>
    /// <returns>Число добавленных новых записей.</returns>
    public static int MergeFromParsedItem(ParsedItem? item)
    {
        if (item is not { IsValid: true } || string.IsNullOrWhiteSpace(item.ItemClass))
            return 0;

        var ilvl = item.ItemLevel;
        var added = 0;

        lock (Gate)
        {
            ReloadFromDiskUnlocked();

            foreach (var affix in item.Affixes)
            {
                if (string.IsNullOrWhiteSpace(affix.Name))
                    continue;

                var stats = new List<string>();
                var ranges = new List<string?>();
                FillStatsAndRanges(affix, stats, ranges);

                var ix = FindEntryIndexUnlocked(affix.Type, affix.Name, affix.Tier, stats);
                if (ix < 0)
                {
                    _entries.Add(new AffixLibraryEntry
                    {
                        ItemClasses = new List<string> { item.ItemClass.Trim() },
                        AffixType = affix.Type.Trim(),
                        AffixName = affix.Name.Trim(),
                        AffixTier = affix.Tier,
                        AffixTierLevel = ilvl,
                        AffixStats = stats,
                        AffixRanges = ranges,
                    });
                    added++;
                }
                else
                {
                    var e = _entries[ix];
                    var ic = item.ItemClass.Trim();
                    if (e.ItemClasses.All(c => !string.Equals(c, ic, StringComparison.Ordinal)))
                        e.ItemClasses.Add(ic);

                    if (e.AffixTierLevel == null)
                        e.AffixTierLevel = ilvl;
                    else if (ilvl < e.AffixTierLevel.Value)
                        e.AffixTierLevel = ilvl;
                }
            }

            SaveToDiskUnlocked();
        }

        return added;
    }

    private static void ReloadFromDiskUnlocked()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _entries = new List<AffixLibraryEntry>();
                return;
            }

            var json = File.ReadAllText(FilePath);
            var root = JsonSerializer.Deserialize<AffixLibraryFile>(json, JsonOptions);
            _entries = root?.Entries ?? new List<AffixLibraryEntry>();
        }
        catch
        {
            _entries = new List<AffixLibraryEntry>();
        }
    }

    private static void SaveToDiskUnlocked()
    {
        try
        {
            var root = new AffixLibraryFile { Version = 1, Entries = _entries };
            var json = JsonSerializer.Serialize(root, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // ignored
        }
    }

    private static int FindEntryIndexUnlocked(string type, string name, int tier, IReadOnlyList<string> stats)
    {
        var normStats = NormalizeStats(stats);
        for (var i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.AffixTier != tier)
                continue;
            if (!string.Equals(e.AffixName, name.Trim(), StringComparison.Ordinal))
                continue;
            if (!string.Equals(e.AffixType, type.Trim(), StringComparison.Ordinal))
                continue;
            if (!StatsEqual(NormalizeStats(e.AffixStats), normStats))
                continue;
            return i;
        }

        return -1;
    }

    private static List<string> NormalizeStats(IReadOnlyList<string>? stats)
    {
        var res = new List<string>();
        if (stats == null)
            return res;
        foreach (var s in stats)
        {
            var t = (s ?? "").Trim();
            if (t.Length == 0)
                continue;
            res.Add(t);
        }

        return res;
    }

    private static bool StatsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static void FillStatsAndRanges(AffixInfo affix, List<string> stats, List<string?> ranges)
    {
        if (affix.EffectDetails.Count > 0)
        {
            foreach (var d in affix.EffectDetails)
            {
                stats.Add(d.StatText);
                ranges.Add(d.Range);
            }

            return;
        }

        foreach (var line in affix.Effects)
        {
            stats.Add(line);
            ranges.Add(null);
        }
    }

    private sealed class AffixLibraryFile
    {
        public int Version { get; set; } = 1;
        public List<AffixLibraryEntry> Entries { get; set; } = new();
    }
}
