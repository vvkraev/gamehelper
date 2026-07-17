using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Цикл «3 в 1» для планшеток достигших флор-цены:
///   1. Сбросить содержимое инвентаря в стэш (если после сброса из Ange что-то осталось).
///   2. Перейти во Fragment Stash → под-вкладка Tablets.
///   3. Ctrl+ЛКМ ячейки стэша до набора targetCount (по умолчанию FragmentFillCount=60).
///   4. ReforgeService.RunAsync() — рефордж батча.
///   5. Ctrl+ЛКМ весь инвентарь → стэш (аффинити маршрутизирует сама).
///   6. TabletReforgeQueue.Clear().
/// </summary>
public sealed class TabletReforgeService
{
    private const double DelayJitterFraction = 0.25;

    public int ActionDelayMs      { get; set; } = 300;
    public int TransferDelayMs    { get; set; } = 300;
    public int TabSwitchDelayMs   { get; set; } = 500;

    private readonly ReforgeService _reforge;

    public TabletReforgeService(ReforgeService reforge) => _reforge = reforge;

    /// <summary>
    /// Полный цикл рефорджа: стэш → инвентарь → рефордж → стэш.
    /// </summary>
    /// <param name="inventoryCells">Ячейки инвентаря игрока (60 штук).</param>
    /// <param name="fragmentStashTabRect">Кнопка вкладки Fragment в стэше.</param>
    /// <param name="fragmentSubTabTabletsRect">Под-вкладка Tablets внутри Fragment.</param>
    /// <param name="fragmentGridCells">Ячейки сетки текущей страницы Fragment Stash.</param>
    /// <param name="slot1">Слот 1 реформ-станка.</param>
    /// <param name="slot2">Слот 2 реформ-станка.</param>
    /// <param name="slot3">Слот 3 реформ-станка.</param>
    /// <param name="confirmRect">Кнопка Reforge.</param>
    /// <param name="resultRect">Слот результата.</param>
    /// <param name="stashCfg">Конфигурация открытия стэша (OCR-поиск + IsOpen-проверка).</param>
    /// <param name="targetCount">Сколько планшеток взять из стэша (обычно 60).</param>
    /// <param name="log">Прогресс.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task RunAsync(
        IReadOnlyList<ScreenRect> inventoryCells,
        ScreenRect fragmentStashTabRect,
        ScreenRect fragmentSubTabTabletsRect,
        IReadOnlyList<ScreenRect> fragmentGridCells,
        ScreenRect slot1, ScreenRect slot2, ScreenRect slot3,
        ScreenRect confirmRect, ScreenRect resultRect,
        StashOpenConfig stashCfg,
        int targetCount,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        try
        {
            // ── Шаг 1: Открываем стэш и сбрасываем инвентарь ─────────────
            if (!await GameUiHelper.EnsureStashOpenAsync(stashCfg, log, ct).ConfigureAwait(false))
            {
                log?.Report("[Рефордж] Не удалось открыть стэш — прерываем рефордж.");
                return;
            }
            log?.Report("[Рефордж] Сбрасываем инвентарь в стэш...");
            await DumpInventoryToStashAsync(inventoryCells, ct);

            // ── Шаг 2: Открыть Fragment Stash → под-вкладка Tablets ─────────────
            log?.Report("[Рефордж] Переходим во Fragment Stash → Tablets...");
            await NavigateToFragmentTabletsAsync(fragmentStashTabRect, fragmentSubTabTabletsRect, ct);

            // ── Шаг 3: Взять targetCount планшеток из стэша в инвентарь ─────────
            log?.Report($"[Рефордж] Берём {targetCount} планшеток из стэша...");
            int taken = await TakeTabletsFromStashAsync(fragmentGridCells, inventoryCells.Count, targetCount, log, ct);
            log?.Report($"[Рефордж] Взято: {taken}");

            if (taken < 3)
            {
                log?.Report("[Рефордж] Недостаточно планшеток для рефорджа (нужно ≥3). Отмена.");
                return;
            }

            // ── Шаг 4: ReforgeService ─────────────────────────────────────────────
            log?.Report("[Рефордж] Запускаем рефордж...");
            var reason = await _reforge.RunAsync(
                inventoryCells, selectedTypeIds: [],
                slot1, slot2, slot3, confirmRect, resultRect,
                maxOps: 0, log, onAttempt: null, ct);
            log?.Report($"[Рефордж] Завершено: {reason}");

            // ── Шаг 5: Сбросить результаты в стэш ───────────────────────────────
            log?.Report("[Рефордж] Сбрасываем результаты в стэш...");
            await NavigateToFragmentTabletsAsync(fragmentStashTabRect, fragmentSubTabTabletsRect, ct);
            await DumpInventoryToStashAsync(inventoryCells, ct);

            // ── Шаг 6: Очистить очередь ──────────────────────────────────────────
            TabletReforgeQueue.Clear();
            log?.Report("[Рефордж] Очередь очищена. Цикл завершён.");
        }
        finally
        {
            Win32Input.ReleaseCtrlAlt();
        }
    }

    /// <summary>
    /// Только сброс инвентаря в стэш — без рефорджа.
    /// Вызывается когда после Delist предметы оказались в инвентаре,
    /// но очередь рефорджа ещё не накопила targetCount.
    /// </summary>
    public async Task DumpInventoryToStashAsync(
        IReadOnlyList<ScreenRect> inventoryCells,
        ScreenRect fragmentStashTabRect,
        ScreenRect fragmentSubTabTabletsRect,
        StashOpenConfig stashCfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        try
        {
            if (!await GameUiHelper.EnsureStashOpenAsync(stashCfg, log, ct).ConfigureAwait(false))
            {
                log?.Report("[Рефордж] Не удалось открыть стэш — сброс прерван.");
                return;
            }
            log?.Report("[Рефордж] Сбрасываем снятые предметы в стэш...");
            await NavigateToFragmentTabletsAsync(fragmentStashTabRect, fragmentSubTabTabletsRect, ct);
            await DumpInventoryToStashAsync(inventoryCells, ct);
            log?.Report($"[Рефордж] Сброшено в стэш. Очередь: {TabletReforgeQueue.Count} шт.");
        }
        finally
        {
            Win32Input.ReleaseCtrlAlt();
        }
    }

    // ── Навигация ─────────────────────────────────────────────────────────────

    private async Task NavigateToFragmentTabletsAsync(
        ScreenRect tabRect, ScreenRect subTabRect, CancellationToken ct)
    {
        if (tabRect.Width > 0 && tabRect.Height > 0)
        {
            var (tx, ty) = tabRect.GetInteriorPoint(1);
            Win32Input.MoveTo(tx, ty);
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
        }
        if (subTabRect.Width > 0 && subTabRect.Height > 0)
        {
            var (sx, sy) = subTabRect.GetInteriorPoint(1);
            Win32Input.MoveTo(sx, sy);
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(WithJitter(TabSwitchDelayMs), ct).ConfigureAwait(false);
        }
    }

    // ── Перенос планшеток из стэша в инвентарь ───────────────────────────────

    private async Task<int> TakeTabletsFromStashAsync(
        IReadOnlyList<ScreenRect> stashCells, int inventorySize, int target, IProgress<string>? log, CancellationToken ct)
    {
        int taken = 0;
        foreach (var cell in stashCells)
        {
            ct.ThrowIfCancellationRequested();
            if (taken >= target || taken >= inventorySize) break;

            // Ctrl+Alt+C для проверки есть ли что-то в ячейке
            var (hx, hy) = cell.GetInteriorPoint(1);
            Win32Input.MoveTo(hx, hy);
            await Task.Delay(WithJitter(ActionDelayMs), ct).ConfigureAwait(false);

            await ClearClipboardAsync().ConfigureAwait(false);
            Win32Input.SendCtrlAltC();
            Win32Input.ReleaseCtrlAlt();
            await Task.Delay(WithJitter(ActionDelayMs), ct).ConfigureAwait(false);

            var text = await ReadClipboardAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Предмет есть — берём его в инвентарь
            Win32Input.MoveTo(hx, hy);
            await Task.Delay(WithJitter(ActionDelayMs), ct).ConfigureAwait(false);
            Win32Input.SendCtrlLeftClick();
            Win32Input.CtrlUp();
            await Task.Delay(WithJitter(TransferDelayMs), ct).ConfigureAwait(false);
            taken++;

            if (taken % 10 == 0)
                log?.Report($"[Рефордж] Взято {taken}/{target}...");
        }
        return taken;
    }

    // ── Сброс инвентаря в стэш ───────────────────────────────────────────────

    private async Task DumpInventoryToStashAsync(IReadOnlyList<ScreenRect> inventoryCells, CancellationToken ct)
    {
        foreach (var cell in inventoryCells)
        {
            ct.ThrowIfCancellationRequested();

            var (hx, hy) = cell.GetInteriorPoint(1);
            Win32Input.MoveTo(hx, hy);
            await Task.Delay(WithJitter(ActionDelayMs), ct).ConfigureAwait(false);

            await ClearClipboardAsync().ConfigureAwait(false);
            Win32Input.SendCtrlAltC();
            Win32Input.ReleaseCtrlAlt();
            await Task.Delay(WithJitter(ActionDelayMs), ct).ConfigureAwait(false);

            var text = await ReadClipboardAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;

            Win32Input.MoveTo(hx, hy);
            await Task.Delay(WithJitter(ActionDelayMs), ct).ConfigureAwait(false);
            Win32Input.SendCtrlLeftClick();
            Win32Input.CtrlUp();
            await Task.Delay(WithJitter(TransferDelayMs), ct).ConfigureAwait(false);
        }
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private int WithJitter(int ms)
    {
        if (ms <= 0) return 0;
        var d = (int)Math.Round(ms * DelayJitterFraction);
        return d <= 0 ? ms : Math.Max(0, ms + Random.Shared.Next(-d, d + 1));
    }

    private static async Task ClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });

    private static async Task<string> ReadClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                return System.Windows.Clipboard.ContainsText()
                    ? System.Windows.Clipboard.GetText() : string.Empty;
            }
            catch { return string.Empty; }
        });
}
