using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHelper.Services;

/// <summary>
/// Одно событие переоценки предмета в магазине.
/// </summary>
public sealed class RepricingEvent
{
    public string Timestamp { get; set; } = "";
    public int    NewPrice  { get; set; }
    /// <summary>"divine" | "chaos" — валюта после переоценки.</summary>
    public string Currency  { get; set; } = "divine";
}

/// <summary>
/// Запись о выставленной на продажу табличке.
/// Создаётся при листинге, обновляется при переоценке и при сопоставлении с продажей.
/// </summary>
public sealed class TabletListingEntry
{
    /// <summary>Уникальный ключ: "YYYY-MM-DDTHH:mm:ss|col|row".</summary>
    public string Id              { get; set; } = "";
    public string Timestamp       { get; set; } = "";
    public int    Col             { get; set; }
    public int    Row             { get; set; }
    public double EstimatedPrice  { get; set; }
    public int    InitialPrice    { get; set; }
    /// <summary>"divine" | "chaos" — валюта первого листинга.</summary>
    public string InitialCurrency { get; set; } = "divine";
    public string BaseType        { get; set; } = "";
    /// <summary>Нормализованные строки модов (trim + lowercase), упорядочены для сравнения.</summary>
    public List<string> Mods      { get; set; } = new();
    public string ItemText        { get; set; } = "";

    public List<RepricingEvent> Repricings { get; set; } = new();

    public bool    Sold               { get; set; }
    public string? SaleId             { get; set; }
    public string? SaleTime           { get; set; }
    public decimal? SalePriceAmount   { get; set; }
    public string? SalePriceCurrency  { get; set; }
    public int?   MinutesToSale       { get; set; }
    /// <summary>true — продан быстро по начальной цене (бот выкупил, недооценка).</summary>
    public bool Miss              { get; set; }
    /// <summary>true — продан после хотя бы одной переоценки (нашли потолок).</summary>
    public bool SoldAfterReprice  { get; set; }
}

/// <summary>
/// Управляет персистентным индексом листингов таблеток.
/// Файл: vault/tabflow/listings_index.json
/// </summary>
public static class TabletListingsIndex
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented            = true,
        PropertyNamingPolicy     = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition   = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly object _lock = new();

    /// <summary>Порог быстрой продажи в минутах: продажа быстрее — считается промазом.</summary>
    public const int MissThresholdMinutes = 10;

    // ── Путь к файлу ────────────────────────────────────────────────────────

    private static string IndexPath =>
        Path.Combine(ProjectPaths.GetProjectRoot(), "vault", "tabflow", "listings_index.json");

    // ── Load / Save ──────────────────────────────────────────────────────────

    public static List<TabletListingEntry> Load()
    {
        try
        {
            var path = IndexPath;
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<TabletListingEntry>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    private static void Save(List<TabletListingEntry> entries)
    {
        try
        {
            var path = IndexPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOpts));
        }
        catch { }
    }

    // ── Публичное API ────────────────────────────────────────────────────────

    /// <summary>
    /// Создать запись при выставлении таблички на продажу.
    /// Вызывается из TabletListingService сразу после листинга.
    /// </summary>
    public static void AddEntry(TabletListingEntry entry)
    {
        lock (_lock)
        {
            var entries = Load();
            entries.Add(entry);
            Save(entries);
        }
    }

    /// <summary>
    /// Залогировать переоценку: найти незаконченную запись по base_type + mods,
    /// добавить событие переоценки.
    /// Вызывается из RepricingService после успешной записи новой цены.
    /// </summary>
    public static void LogReprice(string baseType, IEnumerable<string> mods, int newPrice,
                                   string currency = "divine")
    {
        lock (_lock)
        {
            var entries = Load();
            var match   = FindUnsold(entries, baseType, mods);
            if (match is null) return;

            match.Repricings.Add(new RepricingEvent
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                NewPrice  = newPrice,
                Currency  = currency,
            });
            Save(entries);
        }
    }

    /// <summary>
    /// Залогировать переоценку по id записи (используется SmartRepricingService).
    /// </summary>
    public static void LogRepriceById(string id, int newPrice, string currency = "divine")
    {
        lock (_lock)
        {
            var entries = Load();
            var match   = entries.FirstOrDefault(e => e.Id == id);
            if (match is null) return;

            match.Repricings.Add(new RepricingEvent
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                NewPrice  = newPrice,
                Currency  = currency,
            });
            Save(entries);
        }
    }

    // ── Вспомогательные ─────────────────────────────────────────────────────

    /// <summary>
    /// Извлечь нормализованные строки модов из ParsedItem.
    /// </summary>
    public static List<string> ExtractMods(ParsedItem item) =>
        item.Affixes
            .SelectMany(a => a.Effects)
            .Select(NormalizeMod)
            .Where(m => m.Length > 0)
            .Distinct()
            .OrderBy(m => m)
            .ToList();

    /// <summary>
    /// Нормализовать один мод для сравнения: trim + lowercase.
    /// </summary>
    public static string NormalizeMod(string mod) =>
        mod.Trim().ToLowerInvariant();

    private static HashSet<string> ModSet(IEnumerable<string> mods) =>
        mods.Select(NormalizeMod).ToHashSet();

    /// <summary>
    /// Найти последнюю незаконченную запись с совпадающим base_type и набором модов.
    /// </summary>
    private static TabletListingEntry? FindUnsold(
        List<TabletListingEntry> entries, string baseType, IEnumerable<string> mods)
    {
        var target = ModSet(mods);
        return entries
            .Where(e => !e.Sold && string.Equals(e.BaseType, baseType, StringComparison.OrdinalIgnoreCase))
            .Where(e => ModSet(e.Mods).SetEquals(target))
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();
    }

    // ── Sold ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Пометить листинг как проданный по данным из sales_history.
    /// </summary>
    public static void MarkSold(string id, SoldInfo info)
    {
        lock (_lock)
        {
            var entries = Load();
            var entry   = entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return;

            entry.Sold               = true;
            entry.SaleId             = info.SaleId;
            entry.SaleTime           = info.SaleTime;
            entry.SalePriceAmount    = info.SalePriceAmount;
            entry.SalePriceCurrency  = info.SalePriceCurrency;
            entry.MinutesToSale      = info.MinutesToSale;
            entry.Miss               = info.Miss;
            entry.SoldAfterReprice   = info.SoldAfterReprice;
            Save(entries);
        }
    }

    /// <summary>Публичный парсер timestamp для использования в SoldDetector.</summary>
    public static DateTime? TryParseTimestamp(string? ts) => TryParseTs(ts);

    /// <summary>
    /// Преобразует 0-based индекс ячейки в (col, row) 1-based при column-major раскладке.
    /// Формула: col = cellIdx / gridRows + 1, row = cellIdx % gridRows + 1.
    /// </summary>
    public static (int Col, int Row) CellIndexToColRow(int cellIdx, int gridRows) =>
        (cellIdx / gridRows + 1, cellIdx % gridRows + 1);

    // ── Архивирование ────────────────────────────────────────────────────────

    /// <summary>
    /// Переносит проданные записи старше <paramref name="keepDays"/> дней в архивные файлы
    /// (listings_index_archive_YYYY-MM.json, по одному на месяц).
    /// Возвращает количество перенесённых записей.
    /// </summary>
    public static int ArchiveOldEntries(int keepDays = 30)
    {
        lock (_lock)
        {
            var entries   = Load();
            var cutoff    = DateTime.Now.AddDays(-keepDays);
            var toArchive = entries
                .Where(e => e.Sold && (TryParseTs(e.SaleTime ?? e.Timestamp) ?? DateTime.MinValue) < cutoff)
                .ToList();

            if (toArchive.Count == 0) return 0;

            var toKeep = entries.Except(toArchive).ToList();
            var dir    = Path.GetDirectoryName(IndexPath)!;

            foreach (var grp in toArchive.GroupBy(e =>
            {
                var dt = TryParseTs(e.SaleTime ?? e.Timestamp) ?? DateTime.Now;
                return $"{dt:yyyy-MM}";
            }))
            {
                var archivePath = Path.Combine(dir, $"listings_index_archive_{grp.Key}.json");
                List<TabletListingEntry> existing = new();
                if (File.Exists(archivePath))
                {
                    try { existing = JsonSerializer.Deserialize<List<TabletListingEntry>>(
                              File.ReadAllText(archivePath), JsonOpts) ?? new(); }
                    catch { }
                }
                existing.AddRange(grp);
                File.WriteAllText(archivePath, JsonSerializer.Serialize(existing, JsonOpts));
            }

            Save(toKeep);
            return toArchive.Count;
        }
    }

    private static DateTime? TryParseTs(string? ts)
    {
        if (string.IsNullOrWhiteSpace(ts)) return null;
        return DateTime.TryParse(ts.Replace("Z", "+00:00"), out var dt) ? dt.ToLocalTime() : null;
    }
}
