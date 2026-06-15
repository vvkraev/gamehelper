using System.IO;
using System.Text.Json;

namespace GameHelper.Services;

public sealed class NetworthSnapshot
{
    public DateTime ScannedAt { get; set; }
    public List<NetworthGroupResult> Groups { get; set; } = [];
}

/// <summary>
/// Сохраняет снэпшоты Networth в Log/networth/networth_YYYY-MM-DD_HH-mm-ss.json
/// и загружает последний при старте.
/// </summary>
public static class NetworthSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string GetDirectory() =>
        Path.Combine(ProjectPaths.GetLogDirectory(), "networth");

    public static void Save(List<NetworthGroupResult> groups)
    {
        try
        {
            var snapshot = new NetworthSnapshot
            {
                ScannedAt = DateTime.Now,
                Groups    = groups,
            };
            var dir = GetDirectory();
            Directory.CreateDirectory(dir);
            var fileName = $"networth_{snapshot.ScannedAt:yyyy-MM-dd_HH-mm-ss}.json";
            File.WriteAllText(
                Path.Combine(dir, fileName),
                JsonSerializer.Serialize(snapshot, JsonOpts));
            SessionLogger.Info($"[Networth] Снэпшот сохранён: {fileName}");
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"[Networth] Ошибка сохранения снэпшота: {ex.Message}");
        }
    }

    public static NetworthSnapshot? LoadLatest()
    {
        var dir = GetDirectory();
        if (!Directory.Exists(dir)) return null;

        var latest = Directory.EnumerateFiles(dir, "networth_*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (latest is null) return null;

        try
        {
            var json = File.ReadAllText(latest);
            return JsonSerializer.Deserialize<NetworthSnapshot>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"[Networth] Ошибка загрузки снэпшота: {ex.Message}");
            return null;
        }
    }
}
