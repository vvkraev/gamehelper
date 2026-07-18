using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>
/// Планшетки, снятые с продажи ниже флора и ожидающие рефорджа.
/// Файл: reforge_queue.json рядом с exe.
/// Когда Count ≥ FragmentFillCount (60) → запускается TabletReforgeService.
/// </summary>
public static class TabletReforgeQueue
{
    private static readonly string _path = Path.Combine(
        AppContext.BaseDirectory, "reforge_queue.json");

    private static readonly JsonSerializerOptions _json =
        new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static List<ReforgeQueueEntry> _entries = [];
    private static bool _loaded;

    public static int Count { get { EnsureLoaded(); return _entries.Count; } }

    public static IReadOnlyList<ReforgeQueueEntry> All { get { EnsureLoaded(); return _entries; } }

    public static void Add(string listingId, string baseType, List<string> mods)
    {
        EnsureLoaded();
        if (_entries.Any(e => e.ListingId == listingId)) return;
        _entries.Add(new ReforgeQueueEntry(listingId, baseType, mods, DateTime.UtcNow.ToString("o")));
        Save();
    }

    public static void Clear()
    {
        _entries = [];
        Save();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            _entries = JsonSerializer.Deserialize<List<ReforgeQueueEntry>>(json, _json) ?? [];
        }
        catch { _entries = []; }
    }

    private static void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, _json));
    }
}

public sealed record ReforgeQueueEntry(
    string ListingId,
    string BaseType,
    List<string> Mods,
    string AddedAt);
