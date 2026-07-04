using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>Append-only JSONL лог проданных предметов. Один JSON-объект на строку.</summary>
public class SoldLogStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

    public SoldLogStore(string projectRoot)
        => _path = Path.Combine(projectRoot, "tracking_sold_log.jsonl");

    public void Append(SoldLogEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, _opts);
        File.AppendAllText(_path, line + "\n", Encoding.UTF8);
    }

    public List<SoldLogEntry> LoadAll()
    {
        var result = new List<SoldLogEntry>();
        if (!File.Exists(_path)) return result;
        foreach (var line in File.ReadLines(_path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<SoldLogEntry>(line);
                if (entry is not null) result.Add(entry);
            }
            catch { }
        }
        return result;
    }

    public List<SoldLogEntry> LoadBySession(Guid sessionId)
        => LoadAll().FindAll(e => e.SessionId == sessionId);
}
