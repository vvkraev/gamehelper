using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Забирает таблетки из Fragment Stash (вкладка Fragment → Tablets → тип → страницы).
/// Предполагает, что стэш уже открыт.
/// </summary>
public sealed class FragmentStashFillService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs  { get; set; } = 80;
    public int TransferDelayMs     { get; set; } = 300;
    /// <summary>Задержка после клика по вкладке/иконке/странице (ожидание загрузки), мс.</summary>
    public int TabSwitchDelayMs    { get; set; } = 500;
    /// <summary>Задержка ожидания буфера обмена после Ctrl+C, мс.</summary>
    public int ClipboardDelayMs    { get; set; } = 220;
    /// <summary>Пропускать предметы из очереди рефорджа при заполнении инвентаря.</summary>
    public bool SkipReforgeQueueItems { get; set; } = false;

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
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
            catch { return string.Empty; }
        }).Task;

    /// <summary>
    /// Читает ячейку стэша через Ctrl+C и возвращает true если предмет есть в очереди рефорджа.
    /// Сравнение по BaseType + набору модов (аналогично SmartRepricingService).
    /// </summary>
    private async Task<bool> IsCellInReforgeQueueAsync(ScreenRect cell, CancellationToken ct)
    {
        if (TabletReforgeQueue.Count == 0) return false;

        var (x, y) = cell.GetInteriorPoint(2);
        Win32Input.MoveTo(x, y);
        await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        await ClearClipboardAsync().ConfigureAwait(false);
        Win32Input.SendCtrlC();
        await Task.Delay(ClipboardDelayMs, ct).ConfigureAwait(false);
        var text = await ReadClipboardAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text)) return false;

        var parsed = ItemParser.Parse(text);
        if (parsed is null) return false;

        var baseType = (parsed.Base ?? "").Trim();
        var modSet   = new HashSet<string>(TabletListingsIndex.ExtractMods(parsed));

        return TabletReforgeQueue.All.Any(e =>
            string.Equals(e.BaseType, baseType, StringComparison.OrdinalIgnoreCase) &&
            new HashSet<string>(e.Mods).SetEquals(modSet));
    }

    private async Task ClickAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetInteriorPoint(2);
        Win32Input.MoveTo(x, y);
        await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
    }

    private async Task CtrlClickAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetInteriorPoint(2);
        Win32Input.MoveTo(x, y);
        await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.SendCtrlLeftClick();
        await DelayAsync(TransferDelayMs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Навигирует внутри открытого стэша к Fragment → Tablets → тип → страницы
    /// и Ctrl+ЛКМ забирает предметы.
    /// </summary>
    /// <param name="fragmentTabRect">Кнопка вкладки Fragment в навигации стэша.</param>
    /// <param name="tabletsSubTabRect">Кнопка под-вкладки Tablets.</param>
    /// <param name="typeIconRect">Иконка конкретного типа таблетки. Width==0 → не кликать (все типы).</param>
    /// <param name="pageRects">Кнопки страниц (1-6). Пусто → одна страница без переключения.</param>
    /// <param name="gridCells">Ячейки сетки предметов на текущей странице.</param>
    /// <param name="maxCount">Максимальное число Ctrl+ЛКМ (0 = все страницы).</param>
    /// <returns>Количество сделанных Ctrl+ЛКМ (попыток извлечения).</returns>
    public async Task<int> FillAsync(
        ScreenRect fragmentTabRect,
        ScreenRect tabletsSubTabRect,
        ScreenRect typeIconRect,
        IReadOnlyList<ScreenRect> pageRects,
        IReadOnlyList<ScreenRect> gridCells,
        int maxCount,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (gridCells.Count == 0)
        {
            log?.Report("[Fragment] Ячейки сетки не заданы — останавливаемся.");
            return 0;
        }

        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        // ── 1. Клик по вкладке Fragment ───────────────────────────────────
        if (fragmentTabRect.Width > 0)
        {
            log?.Report("[Fragment] Открываем вкладку Fragment...");
            await ClickAsync(fragmentTabRect, ct).ConfigureAwait(false);
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
        }

        // ── 2. Клик по под-вкладке Tablets ────────────────────────────────
        if (tabletsSubTabRect.Width > 0)
        {
            log?.Report("[Fragment] Открываем под-вкладку Tablets...");
            await ClickAsync(tabletsSubTabRect, ct).ConfigureAwait(false);
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
        }

        // ── 3. Клик по иконке типа ────────────────────────────────────────
        if (typeIconRect.Width > 0)
        {
            log?.Report("[Fragment] Выбираем тип...");
            await ClickAsync(typeIconRect, ct).ConfigureAwait(false);
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
        }

        // ── 4. Перебираем страницы ─────────────────────────────────────────
        var taken = 0;
        var pages = pageRects.Count > 0 ? pageRects : (IReadOnlyList<ScreenRect>)[default];

        try
        {
            for (var pageIdx = 0; pageIdx < pages.Count; pageIdx++)
            {
                ct.ThrowIfCancellationRequested();

                // Переключаем страницу (если есть кнопки)
                if (pageRects.Count > 0)
                {
                    log?.Report($"[Fragment] Страница {pageIdx + 1}/{pages.Count}...");
                    await ClickAsync(pages[pageIdx], ct).ConfigureAwait(false);
                    await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
                }

                // Ctrl+ЛКМ по ячейкам
                foreach (var cell in gridCells)
                {
                    ct.ThrowIfCancellationRequested();

                    if (maxCount > 0 && taken >= maxCount)
                    {
                        log?.Report($"[Fragment] Достигнут лимит {maxCount} — стоп.");
                        return taken;
                    }

                    if (SkipReforgeQueueItems && await IsCellInReforgeQueueAsync(cell, ct).ConfigureAwait(false))
                    {
                        log?.Report("[Fragment] Ячейка — в очереди рефорджа, пропускаем.");
                        continue;
                    }

                    await CtrlClickAsync(cell, ct).ConfigureAwait(false);
                    taken++;
                }

                log?.Report($"[Fragment] Страница {pageIdx + 1} готова ({taken} Ctrl+ЛКМ всего).");
            }
        }
        finally
        {
            // Освобождаем Ctrl на случай прерывания в середине Ctrl+ЛКМ
            Win32Input.ReleaseCtrl();
        }

        log?.Report($"[Fragment] Завершено: {taken} Ctrl+ЛКМ.");
        return taken;
    }
}
