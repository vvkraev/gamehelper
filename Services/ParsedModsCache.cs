using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>
/// Кэш разобранных модов продаж (<c>parsed_mods_cache.json</c>).
/// Ключ — <see cref="SaleRecord.ItemId"/>; значение — список <see cref="ParsedModInfo"/> для всех модов записи.
/// </summary>
public static class ParsedModsCache
{
    public static string FilePath =>
        Path.Combine(ProjectPaths.GetProjectRoot(), "parsed_mods_cache.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static Dictionary<string, List<ParsedModInfo>> LoadFromFile()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, List<ParsedModInfo>>>(json, Opts) ?? [];
        }
        catch { return []; }
    }

    public static void Save(Dictionary<string, List<ParsedModInfo>> cache) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(cache, Opts));
}
