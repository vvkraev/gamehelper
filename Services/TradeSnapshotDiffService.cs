using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

public class TradeSnapshotDiffService
{
    private readonly string _projectRoot;

    public TradeSnapshotDiffService(string projectRoot) => _projectRoot = projectRoot;

    /// <summary>
    /// Строит строки таблицы для сессии: один элемент = один уникальный item id.
    /// </summary>
    public List<TrackingItemRow> BuildRows(TrackingSession session)
    {
        if (session.Snapshots.Count == 0) return new();

        // Загружаем все снимки: snapshot index → dict(id → detail)
        var snapshots = session.Snapshots
            .Select(s => LoadSnapshot(s.FilePath))
            .ToList();

        // Собираем все уникальные id
        var allIds = snapshots
            .SelectMany(s => s.Keys)
            .Distinct()
            .ToList();

        var rows = new List<TrackingItemRow>();
        int n = snapshots.Count;

        foreach (var id in allIds)
        {
            var presence = new bool[n];
            TrackingItemDetail? lastDetail = null;

            for (int i = 0; i < n; i++)
            {
                if (snapshots[i].TryGetValue(id, out var detail))
                {
                    presence[i] = true;
                    lastDetail = detail;
                }
            }

            var status = ComputeStatus(presence);
            var row = new TrackingItemRow
            {
                Id = id,
                Name = lastDetail?.Name ?? id,
                Price = lastDetail?.PriceAmount ?? 0,
                Currency = lastDetail?.PriceCurrency ?? "",
                Status = status,
                StatusLabel = BuildStatusLabel(status, presence, session.Snapshots),
                Presence = presence,
                Detail = lastDetail,
            };
            rows.Add(row);
        }

        // Сортировка: сначала по статусу, потом по цене убыванию
        return rows
            .OrderBy(r => StatusOrder(r.Status))
            .ThenByDescending(r => r.Price)
            .ToList();
    }

    private static ItemStatus ComputeStatus(bool[] presence)
    {
        int n = presence.Length;
        if (n == 0) return ItemStatus.New;

        bool first = presence[0];
        bool last = presence[n - 1];

        // Вернулся: был, потом пропадал, потом снова есть
        bool wasAbsent = false;
        bool wasPresent = false;
        for (int i = 0; i < n - 1; i++)
        {
            if (presence[i]) wasPresent = true;
            if (wasPresent && !presence[i]) wasAbsent = true;
        }
        if (wasAbsent && last) return ItemStatus.Returned;

        // Новый: только в последних снимках, не был в начале
        if (!first && last) return ItemStatus.New;

        // Ушёл: был, последний снимок — нет
        if (!last) return ItemStatus.Gone;

        // Висит N+: присутствует во всех снимках начиная с появления
        int consecutiveFromEnd = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            if (presence[i]) consecutiveFromEnd++;
            else break;
        }
        if (consecutiveFromEnd >= 2) return ItemStatus.Lingering;

        return ItemStatus.Stable;
    }

    private static string BuildStatusLabel(ItemStatus status, bool[] presence, List<SnapshotRef> snapshots)
    {
        int n = presence.Length;
        return status switch
        {
            ItemStatus.Gone => $"ушёл ({SnapshotDate(LastTrueIndex(presence), snapshots)})",
            ItemStatus.New => $"новый ({SnapshotDate(FirstTrueIndex(presence), snapshots)})",
            ItemStatus.Returned => $"вернулся ({SnapshotDate(LastTrueIndex(presence), snapshots)})",
            ItemStatus.Lingering => $"висит {CountConsecutiveFromEnd(presence)} скана",
            ItemStatus.Stable => $"стабильно ({n})",
            _ => ""
        };
    }

    private static int LastTrueIndex(bool[] a)
    {
        for (int i = a.Length - 1; i >= 0; i--)
            if (a[i]) return i;
        return -1;
    }

    private static int FirstTrueIndex(bool[] a)
    {
        for (int i = 0; i < a.Length; i++)
            if (a[i]) return i;
        return -1;
    }

    private static int CountConsecutiveFromEnd(bool[] a)
    {
        int c = 0;
        for (int i = a.Length - 1; i >= 0; i--)
        {
            if (a[i]) c++;
            else break;
        }
        return c;
    }

    private static string SnapshotDate(int idx, List<SnapshotRef> snapshots)
    {
        if (idx < 0 || idx >= snapshots.Count) return "?";
        return snapshots[idx].ImportedAt.ToString("MM-dd HH:mm");
    }

    /// <summary>
    /// Сравнивает два соседних снимка и возвращает предметы, которые пропали (вероятно проданы).
    /// Вызывается при добавлении нового снимка в сессию.
    /// </summary>
    public List<TrackingItemDetail> FindDisappeared(SnapshotRef previous, SnapshotRef current)
    {
        var prev = LoadSnapshot(previous.FilePath);
        var curr = LoadSnapshot(current.FilePath);
        var result = new List<TrackingItemDetail>();
        foreach (var (id, detail) in prev)
            if (!curr.ContainsKey(id))
                result.Add(detail);
        return result;
    }

    private static int StatusOrder(ItemStatus s) => s switch
    {
        ItemStatus.Gone     => 0,
        ItemStatus.Returned => 1,
        ItemStatus.New      => 2,
        ItemStatus.Lingering=> 3,
        ItemStatus.Stable   => 4,
        _ => 9
    };

    private Dictionary<string, TrackingItemDetail> LoadSnapshot(string relPath)
    {
        var result = new Dictionary<string, TrackingItemDetail>();
        var fullPath = Path.Combine(_projectRoot, relPath);
        if (!File.Exists(fullPath)) return result;

        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(fullPath));
            var listings = doc?["listings"]?.AsArray();
            if (listings is null) return result;

            foreach (var node in listings)
            {
                if (node is null) continue;
                var id = node["id"]?.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(id)) continue;

                result[id] = new TrackingItemDetail
                {
                    Id = id,
                    Name = node["name"]?.GetValue<string>() ?? "",
                    BaseType = node["base_type"]?.GetValue<string>() ?? "",
                    ListedAt = node["listed_at"]?.GetValue<string>() ?? "",
                    PriceAmount = node["price_divine"]?.GetValue<double>() ?? 0,
                    PriceCurrency = node["price_currency"]?.GetValue<string>() ?? "",
                    Quality = node["quality"]?.GetValue<int>() ?? 0,
                    Ilvl = node["ilvl"]?.GetValue<int>() ?? 0,
                    Corrupted = node["corrupted"]?.GetValue<bool>() ?? false,
                    ModsFractured = ReadStringList(node["mods_fractured"]?.AsArray()),
                    ModsDesecrated = ReadStringList(node["mods_desecrated"]?.AsArray()),
                    ModsImplicit = ReadStringList(node["mods_implicit"]?.AsArray()),
                    ModsExplicit = ReadStringList(node["mods_explicit"]?.AsArray()),
                    ModsCrafted = ReadStringList(node["mods_crafted"]?.AsArray()),
                };
            }
        }
        catch { }

        return result;
    }

    private static List<string> ReadStringList(JsonArray? arr)
    {
        var list = new List<string>();
        if (arr is null) return list;
        foreach (var n in arr)
            if (n is not null) list.Add(n.GetValue<string>());
        return list;
    }

    /// <summary>Извлекает тип мода (Суф/Преф) из строки вида "Name S1 — desc".</summary>
    public static string ModKind(string mod)
    {
        var m = Regex.Match(mod, @"\s+([SP])(\d+)\s+—");
        if (!m.Success) return "";
        return m.Groups[1].Value == "S" ? "Суф" : "Преф";
    }

    public static string ModTier(string mod)
    {
        var m = Regex.Match(mod, @"\s+([SP]\d+)\s+—");
        return m.Success ? m.Groups[1].Value : "";
    }
}
