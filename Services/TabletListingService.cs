using GameHelper.Native;
using System.Text.Json;

namespace GameHelper.Services;

/// <summary>
/// Запись об одном листинге для лога listings.jsonl.
/// EstimatedPrice — дробная оценка модели; ListingPrice — фактическая целочисленная цена листинга.
/// Всегда листингуем в divine (целые). По умолчанию ceiling: если не продаётся — это тоже сигнал.
/// </summary>
public sealed record ListingRecord(
    string Timestamp,
    int    Col,
    int    Row,
    double EstimatedPrice,  // оценка модели (дробная)
    int    ListingPrice,    // фактически выставленная цена (ceiling, ≥ MinListingPrice)
    string ItemText,
    string Flow);           // "full" | "fast_same_price"

/// <summary>Режим округления оценки модели до целого divine при листинге.</summary>
public enum ListingRoundMode
{
    /// <summary>Округление вверх (по умолчанию). Выше рынка — медленнее продаётся, больше данных.</summary>
    Ceiling,
    /// <summary>Математическое округление — ближе к оценке модели.</summary>
    Round,
    /// <summary>Округление вниз — агрессивное ценообразование, быстрее продажи.</summary>
    Floor,
}

/// <summary>
/// Выставляет табличку на продажу через магазин Ange.
///
/// Два режима:
///   Full  — Ctrl+ЛКМ из инвентаря → диалог «Set Item Price» → цена → валюта → LIST ITEM
///   Fast  — Ctrl+ПКМ из инвентаря → товар сразу выставляется по цене последнего листинга
///
/// Шорткаты:
///   1. Валюта запоминается дропдауном: при той же валюте повторный выбор пропускается.
///   2. Та же цена что в прошлый раз → Fast-режим (Ctrl+ПКМ), диалог не нужен.
/// </summary>
public sealed class TabletListingService
{
    private const double DelayJitterFraction = 0.20;

    public int ActionDelayMs    { get; set; } = 300;
    public int ClipboardDelayMs { get; set; } = 220;
    public int DialogSettleMs   { get; set; } = 500;
    public int DropdownSettleMs { get; set; } = 700;

    /// <summary>Минимальная цена листинга в divine (по умолчанию 1).</summary>
    public int MinListingPrice { get; set; } = 1;

    /// <summary>Режим округления оценки модели. По умолчанию Ceiling.</summary>
    public ListingRoundMode RoundMode { get; set; } = ListingRoundMode.Ceiling;

    // Состояние между вызовами — шорткаты
    private int?  _lastListingPrice;
    private bool  _lastCurrencyWasDivine;

    /// <summary>Сбросить состояние шорткатов (вызывать перед новой сессией листинга).</summary>
    public void ResetState()
    {
        _lastListingPrice      = null;
        _lastCurrencyWasDivine = false;
    }

    private int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        return delta <= 0 ? baseMs : Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private Task DelayAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    private static async Task ClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });

    private static async Task<string> ReadClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
            catch { return string.Empty; }
        });

    /// <summary>
    /// Выставляет одну табличку.
    /// Автоматически выбирает режим Full или Fast на основе истории.
    /// Цена округляется вверх (ceiling) до целого divine, но не меньше <see cref="MinListingPrice"/>.
    /// </summary>
    /// <param name="inventoryCell">Ячейка инвентаря с табличкой.</param>
    /// <param name="estimatedPrice">Оценка модели (дробная, в divine).</param>
    /// <param name="col">Столбец ячейки (1-based) — для лога.</param>
    /// <param name="row">Строка ячейки (1-based) — для лога.</param>
    /// <param name="itemText">Текст предмета из буфера — для лога. Может быть пустым.</param>
    /// <param name="logPath">Путь к файлу listings.jsonl.</param>
    /// <summary>
    /// Возвращает true если предмет выставлен, false если ячейка пустая (пропущена).
    /// </summary>
    public async Task<bool> ListAsync(
        ScreenRect inventoryCell,
        double estimatedPrice,
        int col,
        int row,
        string itemText,
        string logPath,
        ScreenRect priceInputRect,
        ScreenRect currencyDropdownRect,
        ScreenRect divineOrbOcrRect,
        ScreenRect listItemBtnRect,
        IProgress<string>? log,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        // Если itemText не передан — читаем через Ctrl+Alt+C перед листингом
        if (string.IsNullOrWhiteSpace(itemText))
        {
            var (hx, hy) = inventoryCell.GetInteriorPoint(1);
            Win32Input.MoveTo(hx, hy);
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            await ClearClipboardAsync().ConfigureAwait(false);
            Win32Input.SendCtrlAltC();
            await Task.Delay(WithJitter(ClipboardDelayMs), ct).ConfigureAwait(false);
            Win32Input.ReleaseCtrlAlt();
            itemText = await ReadClipboardAsync().ConfigureAwait(false);
        }

        // Пустая ячейка — предмет уже залистирован или отсутствует
        if (string.IsNullOrWhiteSpace(itemText))
        {
            log?.Report($"[Листинг] [{col},{row}] ячейка пустая — пропускаем");
            return false;
        }

        int rawRounded = RoundMode switch {
            ListingRoundMode.Round => (int)Math.Round(estimatedPrice),
            ListingRoundMode.Floor => (int)Math.Floor(estimatedPrice),
            _                     => (int)Math.Ceiling(estimatedPrice),
        };
        int listingPrice = Math.Max(MinListingPrice, rawRounded);

        bool samePrice = _lastListingPrice.HasValue && _lastListingPrice.Value == listingPrice;

        string flow;
        if (samePrice)
        {
            // ── Шорткат 2: Ctrl+ПКМ — мгновенный листинг по цене последнего ──
            flow = "fast_same_price";
            log?.Report($"[Листинг] Ctrl+ПКМ (шорткат, цена={listingPrice}d)");
            var (ix, iy) = inventoryCell.GetInteriorPoint(1);
            Win32Input.MoveTo(ix, iy);
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.SendCtrlRightClick();
            await Task.Delay(WithJitter(DialogSettleMs), ct).ConfigureAwait(false);
        }
        else
        {
            // ── Full: Ctrl+ЛКМ → диалог → цена → валюта → LIST ITEM ──────────
            flow = "full";

            // 1. Перенос из инвентаря в магазин
            var (ix, iy) = inventoryCell.GetInteriorPoint(1);
            log?.Report($"[Листинг] Ctrl+ЛКМ ({ix},{iy}) → диалог");
            Win32Input.MoveTo(ix, iy);
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.SendCtrlLeftClick();
            await Task.Delay(WithJitter(DialogSettleMs), ct).ConfigureAwait(false);

            // 2. Поле цены
            var (px, py) = priceInputRect.GetInteriorPoint(1);
            Win32Input.MoveTo(px, py);
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.SendCtrlA();
            await DelayAsync(80, ct).ConfigureAwait(false);
            Win32Input.TypeText(listingPrice.ToString());
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

            // 3. Валюта — шорткат 1: пропускаем если дропдаун уже на Divine Orb
            if (!_lastCurrencyWasDivine)
            {
                log?.Report("[Листинг] Выбираем Divine Orb...");
                var (dx, dy) = currencyDropdownRect.GetInteriorPoint(1);
                Win32Input.MoveTo(dx, dy);
                await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
                Win32Input.ClickLeft();
                await Task.Delay(WithJitter(DropdownSettleMs), ct).ConfigureAwait(false);

                var normalized = WindowsOcrTextLocator.NormalizeForMatch("divine orb");
                var match = await WindowsOcrTextLocator
                    .TryFindNormalizedSubstringAsync(divineOrbOcrRect, normalized, null, ct)
                    .ConfigureAwait(false);

                if (match is not { } found)
                    throw new InvalidOperationException("Divine Orb не найден в дропдауне.");

                var (dox, doy) = found.BoundsOnScreen.GetInteriorPoint(1);
                Win32Input.MoveTo(dox, doy);
                await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
                Win32Input.ClickLeft();
                await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            }
            else
            {
                log?.Report("[Листинг] Валюта Divine Orb — шорткат, пропускаем дропдаун.");
            }

            // 4. LIST ITEM
            var (lx, ly) = listItemBtnRect.GetInteriorPoint(1);
            Win32Input.MoveTo(lx, ly);
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

            _lastCurrencyWasDivine = true;
        }

        _lastListingPrice = listingPrice;
        log?.Report($"[Листинг] [{col},{row}] оценка={estimatedPrice:F2}d → листинг={listingPrice}d  ({flow})");

        // ── Запись в listings.jsonl ────────────────────────────────────────
        var ts = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        await AppendListingAsync(logPath, new ListingRecord(
            ts, col, row, estimatedPrice, listingPrice, itemText, flow)).ConfigureAwait(false);

        // ── Запись в listings_index.json ───────────────────────────────────
        try
        {
            var parsed = ItemParser.Parse(itemText);
            if (parsed.IsValid)
            {
                var mods = TabletListingsIndex.ExtractMods(parsed);
                TabletListingsIndex.AddEntry(new TabletListingEntry
                {
                    Id             = $"{ts}|{col}|{row}",
                    Timestamp      = ts,
                    Col            = col,
                    Row            = row,
                    EstimatedPrice = estimatedPrice,
                    InitialPrice   = listingPrice,
                    BaseType       = parsed.Base,
                    Mods           = mods,
                    ItemText       = itemText,
                });
            }
        }
        catch { /* не ронять листинг из-за ошибки индекса */ }

        return true;
    }

    private static async Task AppendListingAsync(string logPath, ListingRecord rec)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(logPath);
            if (dir is not null) System.IO.Directory.CreateDirectory(dir);
            var line = JsonSerializer.Serialize(rec, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            await System.IO.File.AppendAllTextAsync(logPath, line + Environment.NewLine).ConfigureAwait(false);
        }
        catch { /* не ронять листинг из-за ошибки лога */ }
    }

}
