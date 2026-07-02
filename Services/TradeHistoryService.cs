using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>Одно правило из X-Rate-Limit-* хедера GGG API.</summary>
public sealed record RateLimitRule(int Current, int Limit, int WindowSec, int PenaltySec)
{
    public bool IsExceeded => Current >= Limit;
    public bool IsNearLimit => Current >= Limit - 1;

    public string WindowLabel => WindowSec switch
    {
        60    => "1 мин",
        600   => "10 мин",
        10800 => "3 ч",
        _     => $"{WindowSec} с",
    };
}

/// <summary>Одна запись о продаже из Merchant History.</summary>
public sealed class SaleRecord
{
    public string ItemId { get; set; } = "";
    public DateTime Time { get; set; }
    public decimal PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string TypeLine { get; set; } = "";
    public string BaseType { get; set; } = "";
    public int ItemLevel { get; set; }
    public List<string> ExplicitMods { get; set; } = [];
    public List<string> FracturedMods { get; set; } = [];
    /// <summary>Стоимость крафта в div (орбы) — вводится вручную или копируется из CraftLedger.</summary>
    public decimal CraftCostDiv { get; set; }
    /// <summary>Стоимость базы предмета в div — вводится пользователем вручную.</summary>
    public decimal BaseCostDiv { get; set; }
    /// <summary>Ссылка на запись журнала крафта (<see cref="CraftLedgerEntry.Id"/>). null — не привязано.</summary>
    public string? CraftLedgerId { get; set; }
}

/// <summary>
/// Загружает историю продаж из <c>sales_history.json</c> и с эндпойнта
/// <c>GET /api/trade2/history/{league}</c>. Новые записи сливаются с локальными.
/// </summary>
public static class TradeHistoryService
{
    public static readonly string FilePath =
        Path.Combine(ProjectPaths.GetProjectRoot(), "sales_history.json");

    private static readonly HttpClient _http = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Последнее известное состояние rate limit по данным ответа сервера.
    /// Обновляется после каждого запроса. null = запросов ещё не было.
    /// </summary>
    public static RateLimitRule[]? LastRateLimit { get; private set; }

    public static List<SaleRecord> LoadFromFile()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<SaleRecord>>(json, JsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"TradeHistory: ошибка чтения файла: {ex.Message}");
            return [];
        }
    }

    public static void Save(List<SaleRecord> records)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(records, JsonOpts));
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"TradeHistory: ошибка сохранения файла: {ex.Message}");
        }
    }

    /// <summary>
    /// Запрашивает последние 100 продаж с API, мёрджит с <paramref name="existing"/>
    /// (дедупликация по <c>item_id</c>), возвращает объединённый список и количество новых.
    /// </summary>
    public static async Task<(List<SaleRecord> merged, int newCount)> FetchAndMergeAsync(
        string league, string poesessid, List<SaleRecord> existing, CancellationToken ct = default)
    {
        var url = $"https://www.pathofexile.com/api/trade2/history/{Uri.EscapeDataString(league)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"POESESSID={poesessid}");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/125.0.0.0 Safari/537.36");
        request.Headers.Add("Referer", "https://www.pathofexile.com/trade2/history");

        using var resp = await _http.SendAsync(request, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        // Обновляем состояние rate limit из хедеров (присутствуют в любом ответе, включая 429).
        LastRateLimit = ParseRateLimitHeaders(resp.Headers);

        if ((int)resp.StatusCode == 429)
        {
            // Retry-After указывает секунды до сброса штрафа.
            int retryAfterSec = 0;
            if (resp.Headers.TryGetValues("Retry-After", out var ra) &&
                int.TryParse(ra.FirstOrDefault(), out var s))
                retryAfterSec = s;

            var retryAt = DateTime.Now.AddSeconds(retryAfterSec > 0 ? retryAfterSec : 3600);
            throw new InvalidOperationException(
                $"Rate limit превышен. Следующий запрос возможен в {retryAt:HH:mm:ss}.");
        }

        if (!resp.IsSuccessStatusCode)
        {
            string? apiMsg = null;
            try
            {
                using var errDoc = JsonDocument.Parse(body);
                if (errDoc.RootElement.TryGetProperty("error", out var err))
                    apiMsg = err.GetProperty("message").GetString();
            }
            catch { /* ignore parse failure — use raw body */ }
            throw new InvalidOperationException($"API {(int)resp.StatusCode}: {apiMsg ?? body}");
        }

        using var doc = JsonDocument.Parse(body);
        var result = doc.RootElement.GetProperty("result");

        var existingIds = new HashSet<string>(existing.Select(r => r.ItemId));
        var newRecords = new List<SaleRecord>();

        foreach (var entry in result.EnumerateArray())
        {
            var itemId = entry.GetProperty("item_id").GetString() ?? "";
            if (!existingIds.Add(itemId)) continue;

            var item = entry.GetProperty("item");
            var priceEl = entry.GetProperty("price");

            newRecords.Add(new SaleRecord
            {
                ItemId = itemId,
                Time = entry.GetProperty("time").GetDateTime(),
                PriceAmount = priceEl.GetProperty("amount").GetDecimal(),
                PriceCurrency = priceEl.GetProperty("currency").GetString() ?? "",
                ItemName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                TypeLine = item.TryGetProperty("typeLine", out var t) ? t.GetString() ?? "" : "",
                BaseType = item.TryGetProperty("baseType", out var bt) ? bt.GetString() ?? "" : "",
                ItemLevel = item.TryGetProperty("ilvl", out var il) ? il.GetInt32() : 0,
                ExplicitMods = GetStringList(item, "explicitMods"),
                FracturedMods = GetStringList(item, "fracturedMods"),
            });
        }

        // Новые записи идут первыми (порядок «новейшие сначала» сохраняется)
        var merged = newRecords.Concat(existing).ToList();
        return (merged, newRecords.Count);
    }

    private static List<string> GetStringList(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var arr)) return [];
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.GetString() is { } v) list.Add(v);
        return list;
    }

    /// <summary>
    /// Парсит хедеры X-Rate-Limit-{rules} и X-Rate-Limit-{rules}-State из ответа GGG API.
    /// Формат: "limit:window:penalty" через запятую (например "5:60:120,10:600:120,15:10800:3600").
    /// State: "current:window:penalty_active".
    /// </summary>
    private static RateLimitRule[]? ParseRateLimitHeaders(HttpResponseHeaders headers)
    {
        try
        {
            // Определяем имя правила из X-Rate-Limit-Rules (обычно "client")
            string ruleKey = "client";
            if (headers.TryGetValues("X-Rate-Limit-Rules", out var rules))
                ruleKey = rules.FirstOrDefault() ?? "client";

            if (!headers.TryGetValues($"X-Rate-Limit-{ruleKey}", out var limitValues) ||
                !headers.TryGetValues($"X-Rate-Limit-{ruleKey}-State", out var stateValues))
                return null;

            var limits = (limitValues.FirstOrDefault() ?? "").Split(',');
            var states = (stateValues.FirstOrDefault() ?? "").Split(',');

            var result = new List<RateLimitRule>();
            for (int i = 0; i < Math.Min(limits.Length, states.Length); i++)
            {
                var lp = limits[i].Split(':');
                var sp = states[i].Split(':');
                if (lp.Length < 3 || sp.Length < 3) continue;
                if (!int.TryParse(lp[0], out int limit) ||
                    !int.TryParse(lp[1], out int window) ||
                    !int.TryParse(lp[2], out int penalty) ||
                    !int.TryParse(sp[0], out int current)) continue;
                result.Add(new RateLimitRule(current, limit, window, penalty));
            }
            return result.Count > 0 ? result.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }
}
