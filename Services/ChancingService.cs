using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Результат одной попытки шансинга.</summary>
public enum ChancingOutcome
{
    /// <summary>Unique tablet появился в ячейке.</summary>
    UniqueProduced,
    /// <summary>Таблет уничтожен, OoC потрачен, ячейка пустая.</summary>
    ItemDestroyed,
}

/// <summary>Запись об одной попытке шансинга.</summary>
public sealed record ChancingAttempt(
    DateTime Timestamp,
    string InputBase,
    bool UsedOmen,
    ChancingOutcome Outcome,
    string? UniqueName,
    string? UniqueBase,
    string? UniqueAffix,
    decimal TabletCostEx,
    decimal OocCostEx,
    decimal OmenCostEx,
    decimal? RevenueEx)
{
    public decimal TotalCostEx => TabletCostEx + OocCostEx + OmenCostEx;
    public decimal ProfitEx    => (RevenueEx ?? 0m) - TotalCostEx;

    /// <summary>Ключ для группировки: "Mastered Domain (Forest)", "Visions of Paradise" и т.п.</summary>
    public string DisplayKey => UniqueName is null ? "(destroyed)"
        : UniqueAffix is not null ? $"{UniqueName} ({UniqueAffix})"
        : UniqueName;
}

/// <summary>Агрегированная статистика сессии шансинга.</summary>
public sealed class ChancingSessionStats
{
    private readonly List<ChancingAttempt> _attempts = [];

    public IReadOnlyList<ChancingAttempt> Attempts => _attempts;
    public int TotalAttempts  => _attempts.Count;
    public int UniqueCount    => _attempts.Count(a => a.Outcome == ChancingOutcome.UniqueProduced);
    public int DestroyedCount => _attempts.Count(a => a.Outcome == ChancingOutcome.ItemDestroyed);
    public double UniqueRatePct => TotalAttempts == 0 ? 0 : UniqueCount * 100.0 / TotalAttempts;

    public decimal TotalCostEx    => _attempts.Sum(a => a.TotalCostEx);
    public decimal TotalRevenueEx => _attempts.Sum(a => a.RevenueEx ?? 0m);
    public decimal NetProfitEx    => TotalRevenueEx - TotalCostEx;
    public decimal EvPerAttemptEx => TotalAttempts == 0 ? 0 : TotalRevenueEx / TotalAttempts;
    public double  RoiPct         => TotalCostEx == 0 ? 0 : (double)(NetProfitEx / TotalCostEx * 100);

    /// <summary>Количество каждого уникального результата, отсортировано по убыванию.</summary>
    public IReadOnlyList<(string Key, int Count, decimal? Revenue)> BreakdownByUnique =>
        _attempts
            .Where(a => a.Outcome == ChancingOutcome.UniqueProduced)
            .GroupBy(a => a.DisplayKey)
            .Select(g => (Key: g.Key, Count: g.Count(), Revenue: g.First().RevenueEx))
            .OrderByDescending(x => x.Count)
            .ToList();

    public void Add(ChancingAttempt attempt) => _attempts.Add(attempt);
    public void Clear() => _attempts.Clear();

    /// <summary>Сохраняет всю накопленную статистику в JSON-файл.</summary>
    public void SaveToJson(string path, decimal divineEx)
    {
        var doc = new
        {
            last_updated    = DateTime.Now.ToString("O"),
            summary = new
            {
                total_attempts  = TotalAttempts,
                unique_count    = UniqueCount,
                destroyed_count = DestroyedCount,
                unique_rate_pct = Math.Round(UniqueRatePct, 2),
                total_cost_ex   = Math.Round((double)TotalCostEx,    1),
                total_revenue_ex = Math.Round((double)TotalRevenueEx, 1),
                net_profit_ex   = Math.Round((double)NetProfitEx,     1),
                roi_pct         = Math.Round(RoiPct,                  1),
                ev_per_attempt_ex = Math.Round((double)EvPerAttemptEx, 1),
            },
            breakdown = BreakdownByUnique.Select(b => new
            {
                key     = b.Key,
                count   = b.Count,
                revenue_ex = b.Revenue.HasValue ? Math.Round((double)b.Revenue.Value, 1) : (double?)null,
            }),
            attempts = _attempts.Select(a => new
            {
                timestamp   = a.Timestamp.ToString("O"),
                input_base  = a.InputBase,
                used_omen   = a.UsedOmen,
                outcome     = a.Outcome.ToString(),
                unique_name = a.UniqueName,
                unique_base = a.UniqueBase,
                affix       = a.UniqueAffix,
                revenue_ex  = a.RevenueEx.HasValue ? Math.Round((double)a.RevenueEx.Value, 1) : (double?)null,
                total_cost_ex = Math.Round((double)a.TotalCostEx, 1),
                profit_ex   = Math.Round((double)a.ProfitEx,      1),
            }),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(doc,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Сохраняет статистику в формат docs/stats/ для вкладки Справочник.
    /// Если файл уже существует — накапливает данные поверх предыдущих сессий.
    /// </summary>
    public void SaveReferenceJson(string path, string displayName, string categoryPath)
    {
        // Загружаем уже накопленные данные (если файл есть)
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var notes  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var prevTotal = 0;

        if (File.Exists(path))
        {
            try
            {
                var existing = ReferenceStatsService.LoadFile(path);
                if (existing != null)
                {
                    prevTotal = existing.TotalSamples;
                    foreach (var e in existing.Entries)
                    {
                        counts[e.Outcome] = e.Count;
                        if (!string.IsNullOrEmpty(e.Notes))
                            notes[e.Outcome] = e.Notes!;
                    }
                }
            }
            catch { /* повреждённый файл — начинаем заново */ }
        }

        // Прибавляем текущую сессию
        foreach (var attempt in _attempts)
        {
            counts.TryGetValue(attempt.DisplayKey, out var cur);
            counts[attempt.DisplayKey] = cur + 1;
        }

        var entries = counts
            .Select(kv => new
            {
                outcome = kv.Key,
                count   = kv.Value,
                notes   = notes.TryGetValue(kv.Key, out var n) ? n : "",
            })
            .OrderByDescending(x => x.count)
            .ToList();

        var doc = new
        {
            display_name  = displayName,
            category_path = categoryPath,
            updated       = DateTime.Today.ToString("yyyy-MM-dd"),
            total_samples = prevTotal + TotalAttempts,
            entries,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(doc,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Форматированная сводка для лога/UI.</summary>
    public string FormatSummary(decimal divineEx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Шансинг: {TotalAttempts} попыток ===");
        sb.AppendLine($"  Unique: {UniqueCount} ({UniqueRatePct:F1}%)  |  Destroyed: {DestroyedCount}");
        sb.AppendLine($"  Затраты:  {TotalCostEx:F0} ex ({TotalCostEx / divineEx:F2} div)");
        sb.AppendLine($"  Выход:    {TotalRevenueEx:F0} ex ({TotalRevenueEx / divineEx:F2} div)");
        var sign = NetProfitEx >= 0 ? "+" : "";
        sb.AppendLine($"  Прибыль:  {sign}{NetProfitEx:F0} ex ({sign}{NetProfitEx / divineEx:F2} div)  ROI: {sign}{RoiPct:F1}%");
        sb.AppendLine($"  EV/попытку: {EvPerAttemptEx:F1} ex");

        if (BreakdownByUnique.Count > 0)
        {
            sb.AppendLine("  Результаты:");
            foreach (var (key, count, revenue) in BreakdownByUnique)
            {
                var rev = revenue.HasValue ? $"~{revenue.Value:F0} ex" : "? ex";
                sb.AppendLine($"    {key,-38} {count,3}x  ({rev})");
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Сервис шансинга tablet'ов через Orb of Chance.
/// Поддерживает два режима:
///  - стандартный: tablet → unique ИЛИ уничтожение (49% вероятность потери)
///  - с Omen of the Ancients: ВСЕГДА unique, cross-base (любой unique tablet из расширенного пула)
/// Основной фокус — статистика и оценка прибыльности.
/// </summary>
public sealed class ChancingService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs   { get; set; } = 220;
    public int HoverSettleMs      { get; set; } = 120;
    public int PostApplyWaitMs    { get; set; } = 400;  // пауза после применения орба до чтения результата

    /// <summary>Имя базы таблета на входе (напр. "Delirium Tablet", "Irradiated Tablet").</summary>
    public string InputBase { get; set; } = "Irradiated Tablet";

    /// <summary>Использовать Omen of the Ancients: гарантирует unique, позволяет cross-base результат.</summary>
    public bool UseOmen { get; set; } = false;

    // ── Цены ресурсов в Ex (загружаются из poe_ninja_prices.json) ──────────
    public decimal TabletCostEx { get; set; } = 47m;   // Irradiated median по 50 листингам (2026-06-19)
    public decimal OocCostEx    { get; set; } = 9.9m;  // poe.ninja
    public decimal OmenCostEx   { get; set; } = 3.7m;  // poe.ninja
    public decimal DivineEx     { get; set; } = 209.1m;

    /// <summary>
    /// Цены уникальных результатов в Ex.
    /// Ключи: точное имя уникального ("Visions of Paradise"),
    ///        или с аффиксом для вариантов ("Mastered Domain (Forest)").
    /// </summary>
    public Dictionary<string, decimal> UniquePrices { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Irradiated Tablet uniques (trade2 API + meetthemarket.gg, 2026-06-19)
        ["Mastered Domain (Forest)"]       = 322m,  // 13 chaos
        ["Mastered Domain (Mountain)"]     = 20m,
        ["Mastered Domain (Grass)"]        = 16m,
        ["Mastered Domain (Swamp)"]        = 10m,
        ["Mastered Domain (Water)"]        = 9m,
        ["Mastered Domain (Desert)"]       = 9m,
        ["Visions of Paradise"]            = 895m,
        ["Unforeseen Consequences"]        = 484m,
        ["Wraeclast Besieged"]             = 73m,
        ["Freedom of Faith"]               = 60m,
        ["Cruel Hegemony"]                 = 1m,  // нет спроса, 10шт = 1 OoC
        ["Season of the Hunt"]             = 1m,  // нет спроса, 10шт = 1 OoC
        ["Clear Skies"]                    = 1m,  // нет спроса, 10шт = 1 OoC
        // Precursor Tablet unique
        ["The Grand Project"]              = 856m,
        // Остальные tablet uniques (Delirium/Breach/Ritual/Temple/Abyss):
        // заполнять по мере сбора статистики с Omen of the Ancients
    };

    public ChancingSessionStats SessionStats { get; } = new();

    // ── Вспомогательные методы ──────────────────────────────────────────────

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        return delta <= 0 ? baseMs : Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static Task DelayAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    private static Task ClearClipboardAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        }).Task;

    private static Task<string> ReadClipboardAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync<string>(() =>
        {
            try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
            catch { return string.Empty; }
        }).Task;

    // ── Публичные методы ────────────────────────────────────────────────────

    /// <summary>
    /// Одна попытка шансинга: применяет OoC (и опционально Omen) к ячейке таблета,
    /// читает результат через Ctrl+Alt+C и возвращает запись о попытке.
    /// </summary>
    /// <param name="oocRect">Ячейка стака OoC в инвентаре или сташе.</param>
    /// <param name="tabletRect">Ячейка таблета в сташе.</param>
    /// <param name="omenRect">Ячейка Omen of the Ancients (null если не используется).</param>
    public async Task<ChancingAttempt> ChanceSingleAsync(
        ScreenRect oocRect,
        ScreenRect tabletRect,
        ScreenRect? omenRect,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        var started = DateTime.Now;
        var useOmen = omenRect.HasValue;

        // ── Шаг 1: Omen (если задан) — ПКМ по Omen → ЛКМ по таблету ─────────
        if (useOmen && omenRect.HasValue)
        {
            var (ox, oy) = omenRect.Value.GetInteriorPoint(2);
            log?.Report("Omen: ПКМ на ячейку Omen of the Ancients");
            Win32Input.MoveTo(ox, oy);
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickRight();
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            var (tx1, ty1) = tabletRect.GetInteriorPoint(2);
            log?.Report("Omen: ЛКМ на ячейку таблета");
            Win32Input.MoveTo(tx1, ty1);
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await DelayAsync(PostApplyWaitMs, ct).ConfigureAwait(false);
        }

        // ── Шаг 2: OoC — ПКМ по OoC → ЛКМ по таблету ───────────────────────
        {
            var (ox, oy) = oocRect.GetInteriorPoint(2);
            log?.Report("OoC: ПКМ на стак Orb of Chance");
            Win32Input.MoveTo(ox, oy);
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickRight();
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            var (tx, ty) = tabletRect.GetInteriorPoint(2);
            log?.Report("OoC: ЛКМ на ячейку таблета");
            Win32Input.MoveTo(tx, ty);
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await DelayAsync(PostApplyWaitMs, ct).ConfigureAwait(false);
        }

        // ── Шаг 3: читаем результат (Ctrl+Alt+C) ─────────────────────────────
        var clip = await ReadCellClipboardAsync(tabletRect, log, ct, "результат").ConfigureAwait(false);

        return ParseAttemptResult(clip, started, useOmen, log);
    }

    /// <summary>
    /// Шансинг всей сетки за два прохода:
    /// 1) ПКМ на OoC → Shift+ЛКМ по каждой ячейке → Shift отпускаем
    /// 2) Ctrl+Alt+C по каждой ячейке → читаем результат
    /// </summary>
    public async Task RunGridAsync(
        ScreenRect oocRect,
        IReadOnlyList<ScreenRect> tabletCells,
        ScreenRect? omenRect,   // не используется (omen активируется вручную), нужен для совместимости
        IProgress<string>? log,
        Action<ChancingAttempt, ChancingSessionStats>? onAttemptDone,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        var sessionStart = DateTime.Now;

        // ── Фаза 1: применение OoC — ПКМ + Shift+ЛКМ по сетке ──────────────
        log?.Report($"Фаза 1: применение OoC к {tabletCells.Count} ячейкам...");

        var (ox, oy) = oocRect.GetInteriorPoint(2);
        Win32Input.MoveTo(ox, oy);
        await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickRight();
        await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        Win32Input.ShiftDown();
        try
        {
            for (var i = 0; i < tabletCells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (tx, ty) = tabletCells[i].GetInteriorPoint(2);
                Win32Input.MoveTo(tx, ty);
                await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                Win32Input.ClickLeft();
                await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                log?.Report($"  [{i + 1}/{tabletCells.Count}] OoC применён");
            }
        }
        finally
        {
            Win32Input.ShiftUp();
        }

        // Пауза после применения
        await Task.Delay(WithJitter(PostApplyWaitMs), ct).ConfigureAwait(false);

        // ── Фаза 2: сканирование результатов — Ctrl+Alt+C по каждой ячейке ──
        log?.Report("Фаза 2: сканирование результатов...");

        for (var i = 0; i < tabletCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var clip = await ReadCellClipboardAsync(tabletCells[i], log, ct, $"ячейка {i + 1}").ConfigureAwait(false);
            var attempt = ParseAttemptResult(clip, sessionStart, UseOmen, log);
            SessionStats.Add(attempt);
            onAttemptDone?.Invoke(attempt, SessionStats);
            SessionLogger.Info($"Шансинг [{i + 1}/{tabletCells.Count}]: {attempt.DisplayKey} | {(attempt.RevenueEx.HasValue ? $"{attempt.RevenueEx:F0} ex" : "уничтожен")} | profit: {attempt.ProfitEx:F0} ex");
        }

        log?.Report(SessionStats.FormatSummary(DivineEx));
    }

    // ── Приватные хелперы ───────────────────────────────────────────────────

    private async Task<string> ReadCellClipboardAsync(
        ScreenRect cell, IProgress<string>? log, CancellationToken ct, string tag)
    {
        await ClearClipboardAsync().ConfigureAwait(false);
        var (hx, hy) = cell.GetInteriorPoint(1);
        Win32Input.MoveTo(hx, hy);
        await Task.Delay(HoverSettleMs, ct).ConfigureAwait(false);
        Win32Input.SendCtrlAltC();
        await DelayAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
        var text = await ReadClipboardAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(text)) return text;

        // Retry — буфер может быть пуст из-за задержки рендера
        log?.Report($"{tag}: буфер пуст, повтор Ctrl+Alt+C ...");
        await Task.Delay(700, ct).ConfigureAwait(false);
        Win32Input.SendCtrlAltC();
        await DelayAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
        return await ReadClipboardAsync().ConfigureAwait(false);
    }

    private ChancingAttempt ParseAttemptResult(string clip, DateTime timestamp, bool usedOmen, IProgress<string>? log)
    {
        var omenCost = usedOmen ? OmenCostEx : 0m;

        // Пустой буфер или не Unique → ячейка пустая → таблет уничтожен
        if (string.IsNullOrWhiteSpace(clip) || !clip.Contains("Rarity: Unique", StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("Результат: ячейка пустая — таблет уничтожен");
            return new ChancingAttempt(timestamp, InputBase, usedOmen,
                ChancingOutcome.ItemDestroyed,
                null, null, null,
                TabletCostEx, OocCostEx, omenCost, null);
        }

        // Парсим имя и базу: строки сразу после "Rarity: Unique"
        var lines = clip.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rarityIdx = Array.FindIndex(lines, l => l.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase));
        var uniqueName = rarityIdx >= 0 && rarityIdx + 1 < lines.Length ? lines[rarityIdx + 1] : null;
        var uniqueBase = rarityIdx >= 0 && rarityIdx + 2 < lines.Length ? lines[rarityIdx + 2] : null;

        // Для Mastered Domain — извлечь биом из аффикса (trade2 API отдаёт "[Biome|Forest]", игра — "Forest Map")
        string? affix = null;
        if (string.Equals(uniqueName, "Mastered Domain", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(clip, @"\[Biome\|(\w+)\]", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(clip, @"counts as a (\w+) Map", RegexOptions.IgnoreCase);
            affix = m.Success ? m.Groups[1].Value : null;
        }

        var revenue = LookupPrice(uniqueName, affix);
        var attempt = new ChancingAttempt(timestamp, InputBase, usedOmen,
            ChancingOutcome.UniqueProduced,
            uniqueName, uniqueBase, affix,
            TabletCostEx, OocCostEx, omenCost, revenue);

        log?.Report($"Результат: UNIQUE — {attempt.DisplayKey} [{uniqueBase}] {(revenue.HasValue ? $"≈{revenue:F0} ex" : "(цена неизвестна)")} | profit: {attempt.ProfitEx:F0} ex");
        return attempt;
    }

    private decimal? LookupPrice(string? uniqueName, string? affix)
    {
        if (uniqueName is null) return null;

        // Пробуем ключи с аффиксом, без аффикса, и generic Mastered Domain
        if (affix is not null && UniquePrices.TryGetValue($"{uniqueName} ({affix})", out var p1)) return p1;
        if (affix is not null && UniquePrices.TryGetValue($"{uniqueName} {affix}", out var p2)) return p2;
        if (UniquePrices.TryGetValue(uniqueName, out var p3)) return p3;
        return null;
    }
}
