using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHelper.Services;

/// <summary>
/// Загружает rune_affix_overrides.json и предоставляет аффиксы рун в виде AffixLibraryEntry.
/// Руны добавляют пул семейств модификаторов к предмету — несколько аффиксов из пула
/// могут присутствовать одновременно (каждый — отдельное семейство).
/// </summary>
public static class RuneAffixLibrary
{
    private static RuneAffixFile? _data;
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string FilePath =>
        Path.Combine(ProjectPaths.GetProjectRoot(), "rune_affix_overrides.json");

    private static void EnsureLoaded()
    {
        if (_data != null) return;
        LoadUnlocked();
    }

    private static void LoadUnlocked()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _data = new RuneAffixFile();
                return;
            }
            var json = File.ReadAllText(FilePath);
            _data = JsonSerializer.Deserialize<RuneAffixFile>(json, JsonOptions) ?? new RuneAffixFile();
        }
        catch
        {
            _data = new RuneAffixFile();
        }
    }

    /// <summary>
    /// Возвращает руны, применимые к данному классу предметов.
    /// Ключ = runeGroup ("destruction", "chronomancy", …), значение = отображаемое имя руны.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetRunesForItemClass(string itemClass)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_data?.Runes == null) return result;
            foreach (var (key, rune) in _data.Runes)
            {
                if (rune.ItemClasses.Contains(itemClass, StringComparer.Ordinal))
                    result[key] = rune.RuneName;
            }
            return result;
        }
    }

    /// <summary>
    /// Возвращает аффиксы указанной руны для данного класса предметов в виде AffixLibraryEntry.
    /// Каждая запись получает уникальный FamilyId, чтобы несколько аффиксов из пула руны
    /// могли присутствовать на одном предмете одновременно.
    /// </summary>
    public static IReadOnlyList<AffixLibraryEntry> GetEntries(string runeGroup, string itemClass)
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (_data?.Runes == null || !_data.Runes.TryGetValue(runeGroup, out var rune))
                return [];
            if (!rune.ItemClasses.Contains(itemClass, StringComparer.Ordinal))
                return [];

            var result = new List<AffixLibraryEntry>(rune.Affixes.Count);
            for (var i = 0; i < rune.Affixes.Count; i++)
            {
                var a = rune.Affixes[i];
                result.Add(new AffixLibraryEntry
                {
                    ItemClasses    = [itemClass],
                    AffixType      = a.AffixType,
                    AffixName      = a.AffixName,
                    AffixTier      = a.AffixTier,
                    AffixTierLevel = a.AffixTierLevel,
                    AffixStats     = new List<string>(a.AffixStats),
                    AffixRanges    = new List<string?>(a.AffixRanges),
                    Weight         = a.Weight,
                    FamilyId       = $"rune:{runeGroup}:{i}",
                });
            }
            return result;
        }
    }
}

internal sealed class RuneAffixFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, RuneGroup> Runes { get; set; } = new();
}

internal sealed class RuneGroup
{
    public string RuneName { get; set; } = "";
    public List<string> ItemClasses { get; set; } = new();
    public List<RuneAffixEntry> Affixes { get; set; } = new();
}

internal sealed class RuneAffixEntry
{
    public string AffixType { get; set; } = "";
    public string AffixName { get; set; } = "";
    public int AffixTier { get; set; }
    public int AffixTierLevel { get; set; }
    public List<string> AffixStats { get; set; } = new();
    public List<string?> AffixRanges { get; set; } = new();
    public int Weight { get; set; } = 1;
}
