using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GameHelper.Services;

public class TrackingStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public TrackingStore(string projectRoot)
        => _path = Path.Combine(projectRoot, "tracking_sessions.json");

    public List<TrackingSession> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TrackingSession>>(json) ?? new();
        }
        catch { return new(); }
    }

    public void Save(List<TrackingSession> sessions)
        => File.WriteAllText(_path, JsonSerializer.Serialize(sessions, _opts), Encoding.UTF8);
}
