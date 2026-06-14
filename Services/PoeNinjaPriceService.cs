using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

public sealed record PoeNinjaPrice(decimal DivineValue, decimal ExaltedValue);

/// <summary>
/// Загружает цены с poe.ninja вручную (кнопка в UI) и хранит полную историю снэпшотов
/// в <c>poe_ninja_prices.json</c>. При старте цены читаются из файла без сетевых запросов.
/// <see cref="GetPrice"/> всегда возвращает самую свежую известную цену предмета.
/// </summary>
public static class PoeNinjaPriceService
{
    public static readonly string FilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "poe_ninja_prices.json");

    private static readonly HttpClient _http = new();

    // Хронологический список снэпшотов. Новые добавляются в конец.
    private static readonly List<PriceSnapshot> _history = [];

    // Кэш последней известной цены каждого предмета (пересчитывается после каждого изменения истории).
    private static Dictionary<string, PoeNinjaPrice> _latestPrices = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static DateTime? LastFetchedAt { get; private set; }
    public static int SnapshotCount => _history.Count;
    public static int ItemCount => _latestPrices.Count;

    /// <summary>Вызывается в UI-потоке после обновления цен.</summary>
    public static event Action? PricesUpdated;

    // Типы проверены вручную — Verisium и Expedition возвращают 0 позиций для PoE2 Standard.
    // Breach содержит все катализаторы и брич-камни.
    private static readonly string[] ApiTypes = ["Currency", "Runes", "Delirium", "Breach"];

    // ── Загрузка из локального файла при старте ───────────────────────────

    /// <summary>
    /// Читает <c>poe_ninja_prices.json</c> и восстанавливает историю цен.
    /// Сетевых запросов нет. Вызывать один раз при запуске.
    /// </summary>
    public static void LoadFromFile()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            var file = JsonSerializer.Deserialize<PriceHistoryFile>(json, JsonOpts);
            if (file?.Entries == null) return;

            _history.Clear();
            _history.AddRange(file.Entries);
            RebuildLatestPrices();
            PricesUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"PoeNinja: ошибка чтения файла цен: {ex.Message}");
        }
    }

    // ── Загрузка из сети ──────────────────────────────────────────────────

    /// <summary>
    /// Запрашивает актуальные цены с poe.ninja (4 типа), добавляет новый снэпшот в историю
    /// и сохраняет файл. Старые снэпшоты не удаляются.
    /// </summary>
    public static async Task FetchAsync(string league, CancellationToken ct = default)
    {
        var prices = new Dictionary<string, PoeNinjaPrice>();
        foreach (var type in ApiTypes)
        {
            ct.ThrowIfCancellationRequested();
            var entries = await FetchTypeAsync(league, type, ct).ConfigureAwait(false);
            foreach (var (k, v) in entries)
                prices[k] = v;
        }

        var snapshot = new PriceSnapshot
        {
            FetchedAt = DateTime.Now,
            League    = league,
            Prices    = prices,
        };
        _history.Add(snapshot);
        LastFetchedAt = snapshot.FetchedAt;
        RebuildLatestPrices();
        SaveToFile();
        PricesUpdated?.Invoke();
    }

    // ── Поиск цены ────────────────────────────────────────────────────────

    /// <summary>Возвращает самую свежую известную цену по отображаемому имени предмета.</summary>
    public static PoeNinjaPrice? GetPrice(string itemName)
    {
        var key = NormalizeName(itemName);
        return _latestPrices.TryGetValue(key, out var price) ? price : null;
    }

    // ── Внутренние методы ─────────────────────────────────────────────────

    /// <summary>
    /// Перебирает снэпшоты от старых к новым — каждый следующий перезаписывает цену,
    /// поэтому в итоге в <see cref="_latestPrices"/> для каждого предмета остаётся последняя.
    /// </summary>
    private static void RebuildLatestPrices()
    {
        var latest = new Dictionary<string, PoeNinjaPrice>();
        foreach (var snap in _history)
            foreach (var (k, v) in snap.Prices)
                latest[k] = v;
        _latestPrices = latest;
        LastFetchedAt = _history.Count > 0 ? _history[^1].FetchedAt : null;
    }

    private static void SaveToFile()
    {
        try
        {
            var file = new PriceHistoryFile { Entries = _history };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(file, JsonOpts));
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"PoeNinja: ошибка сохранения файла цен: {ex.Message}");
        }
    }

    private static async Task<Dictionary<string, PoeNinjaPrice>> FetchTypeAsync(
        string league, string type, CancellationToken ct)
    {
        var slug = league.Replace(" ", "").ToLowerInvariant();
        var url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={Uri.EscapeDataString(league)}&type={type}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Referer",
            $"https://poe.ninja/poe2/economy/{slug}/{type.ToLowerInvariant()}");

        try
        {
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                SessionLogger.Info($"PoeNinja: {type} HTTP {(int)resp.StatusCode}");
                return [];
            }
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SessionLogger.Info($"PoeNinja: {type} ошибка: {ex.Message}");
            return [];
        }
    }

    // Response shape:
    //   items[]  → { id, name }
    //   lines[]  → { id, primaryValue }   (price in divine)
    //   core.rates.exalted                (how many exalted per 1 divine)
    private static Dictionary<string, PoeNinjaPrice> ParseResponse(string json)
    {
        var result = new Dictionary<string, PoeNinjaPrice>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("items", out var itemsEl))
                foreach (var item in itemsEl.EnumerateArray())
                    if (item.TryGetProperty("id", out var idEl) && item.TryGetProperty("name", out var nameEl))
                        nameMap[idEl.GetString() ?? ""] = nameEl.GetString() ?? "";

            decimal exaltedPerDivine = 1m;
            if (root.TryGetProperty("core", out var coreEl) &&
                coreEl.TryGetProperty("rates", out var ratesEl) &&
                ratesEl.TryGetProperty("exalted", out var exEl))
                exaltedPerDivine = exEl.GetDecimal();

            if (!root.TryGetProperty("lines", out var linesEl)) return result;
            foreach (var line in linesEl.EnumerateArray())
            {
                if (!line.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString() ?? "";
                if (!nameMap.TryGetValue(id, out var name)) continue;

                decimal divine = 0m;
                if (line.TryGetProperty("primaryValue", out var pvEl))
                    divine = pvEl.GetDecimal();

                var exalted = Math.Round(divine * exaltedPerDivine, 1);
                var key = NormalizeName(name);
                if (!string.IsNullOrEmpty(key))
                    result[key] = new PoeNinjaPrice(divine, exalted);
            }
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"PoeNinja parse failed: {ex.Message}");
        }
        return result;
    }

    internal static string NormalizeName(string name)
    {
        var s = name.ToLowerInvariant();
        s = Regex.Replace(s, @"[^\w\s]", " ");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    // ── Форматы файла ─────────────────────────────────────────────────────

    private sealed class PriceHistoryFile
    {
        public List<PriceSnapshot> Entries { get; set; } = [];
    }

    private sealed class PriceSnapshot
    {
        public DateTime FetchedAt { get; set; }
        public string League { get; set; } = "";
        public Dictionary<string, PoeNinjaPrice> Prices { get; set; } = [];
    }
}
