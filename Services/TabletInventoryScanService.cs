using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Результат сканирования одной ячейки.</summary>
public sealed record TabletScanCellResult(
    int CellIndex,
    ScreenRect Cell,
    string ItemText)   // пустая строка = ячейка пустая
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(ItemText);
}

/// <summary>
/// Сканирует ячейки инвентаря через Ctrl+Alt+C и возвращает тексты предметов.
/// Необязательный первый шаг: OCR-поиск метки NPC → Ctrl+ЛКМ по ней.
/// </summary>
public sealed class TabletInventoryScanService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int HoverSettleMs      { get; set; } = 120;
    public int ClipboardDelayMs   { get; set; } = 220;

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

    /// <summary>
    /// Сканирует ячейки инвентаря:
    /// 1. Если <paramref name="npcClickTarget"/> задан — Ctrl+ЛКМ по нему (открывает UI NPC).
    /// 2. Для каждой ячейки из <paramref name="cells"/>: hover → Ctrl+Alt+C → читает буфер.
    /// </summary>
    /// <param name="npcClickTarget">Координата клика по NPC (например найденный OCR-результат). Null = пропустить.</param>
    /// <param name="cells">Ячейки инвентаря для сканирования.</param>
    /// <returns>Список результатов (один элемент на ячейку).</returns>
    public async Task<IReadOnlyList<TabletScanCellResult>> ScanAsync(
        (int x, int y)? npcClickTarget,
        IReadOnlyList<ScreenRect> cells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        // ── Шаг 1: Ctrl+ЛКМ по NPC (если задан) ─────────────────────────
        if (npcClickTarget is { } target)
        {
            log?.Report($"[Скан] Ctrl+ЛКМ по ({target.x},{target.y}) ...");
            Win32Input.MoveTo(target.x, target.y);
            await DelayAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            Win32Input.SendCtrlLeftClick();
            await DelayAsync(MouseActionDelayMs * 3, ct).ConfigureAwait(false);
        }

        // ── Шаг 2: Сканирование ячеек ────────────────────────────────────
        var results = new List<TabletScanCellResult>(cells.Count);

        try
        {
            for (var i = 0; i < cells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var cell = cells[i];
                log?.Report($"[Скан] Ячейка {i + 1}/{cells.Count}...");

                await ClearClipboardAsync().ConfigureAwait(false);
                var (hx, hy) = cell.GetInteriorPoint(1);
                Win32Input.MoveTo(hx, hy);
                await Task.Delay(HoverSettleMs, ct).ConfigureAwait(false);
                Win32Input.SendCtrlAltC();
                await DelayAsync(ClipboardDelayMs, ct).ConfigureAwait(false);

                var text = await ReadClipboardAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(text))
                {
                    // Одна попытка повтора
                    Win32Input.SendCtrlAltC();
                    await DelayAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
                    text = await ReadClipboardAsync().ConfigureAwait(false);
                }

                results.Add(new TabletScanCellResult(i, cell, text ?? string.Empty));

                if (!string.IsNullOrWhiteSpace(text))
                    log?.Report($"[Скан] Ячейка {i + 1}: есть предмет.");
                else
                    log?.Report($"[Скан] Ячейка {i + 1}: пусто.");
            }
        }
        finally
        {
            Win32Input.ReleaseCtrlAlt();
        }

        log?.Report($"[Скан] Готово: {results.Count(r => !r.IsEmpty)} из {results.Count} ячеек с предметами.");
        return results;
    }
}
