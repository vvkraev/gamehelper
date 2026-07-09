using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHelper.Services;

/// <summary>
/// Загружает crafted_mods.json — крафтед-модификаторы (верстак крафта).
/// Хранится отдельно от affix_library.json, который содержит только натуральные моды.
/// Формат файла идентичен affix_library.json: { "version": 1, "entries": [...] }.
/// </summary>
public static class CraftedModLibrary
{
    private static List<AffixLibraryEntry> _entries = new();
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling  = JsonCommentHandling.Skip,
        AllowTrailingCommas  = true,
    };

    public static string FilePath =>
        Path.Combine(ProjectPaths.GetProjectRoot(), "crafted_mods.json");

    public static void ReloadFromDisk() => ReloadFromDisk(FilePath);

    public static void ReloadFromDisk(string filePath)
    {
        lock (Gate)
        {
            _entries = new List<AffixLibraryEntry>();
            try
            {
                if (!File.Exists(filePath)) return;
                var json = File.ReadAllText(filePath);
                var root = JsonSerializer.Deserialize<CraftedModFile>(json, JsonOptions);
                if (root?.Entries is null) return;
                foreach (var e in root.Entries)
                {
                    if (e is null) continue;
                    // Нормализуем — единственный класс на запись (аналогично AffixLibrary.MigrateMultiClassEntries)
                    if (e.ItemClasses is { Count: > 1 })
                    {
                        foreach (var cls in e.ItemClasses)
                            _entries.Add(CloneWithClass(e, cls));
                    }
                    else
                    {
                        _entries.Add(e);
                    }
                }
            }
            catch { /* молча — библиотека пуста */ }
        }
    }

    public static IReadOnlyList<AffixLibraryEntry> GetEntries()
    {
        lock (Gate)
        {
            if (_entries.Count == 0 && File.Exists(FilePath))
                ReloadFromDisk();
            return _entries;
        }
    }

    private static AffixLibraryEntry CloneWithClass(AffixLibraryEntry src, string itemClass) =>
        new()
        {
            ItemClasses    = [itemClass],
            AffixType      = src.AffixType,
            AffixName      = src.AffixName,
            AffixTier      = src.AffixTier,
            AffixTierLevel = src.AffixTierLevel,
            AffixStats     = new List<string>(src.AffixStats ?? []),
            AffixRanges    = new List<string?>(src.AffixRanges ?? []),
            Weight         = src.Weight,
            FamilyId       = src.FamilyId,
        };

    private sealed class CraftedModFile
    {
        public int Version { get; set; } = 1;
        [JsonPropertyName("entries")]
        public List<AffixLibraryEntry>? Entries { get; set; }
    }
}
