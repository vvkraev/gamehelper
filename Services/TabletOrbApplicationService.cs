using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Применяет орб (ПКМ+Shift) ко всем ячейкам таблеток в инвентаре.
/// Предполагает, что стэш с орбом уже открыт на Currency-вкладке.
/// </summary>
public sealed class TabletOrbApplicationService
{
    private const double DelayJitterFraction = 0.20;

    public int ActionDelayMs { get; set; } = 400;

    private int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        return delta <= 0 ? baseMs : Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private Task DelayAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    /// <summary>
    /// Применяет орб из <paramref name="orbRect"/> ко всем ячейкам <paramref name="cells"/>.
    /// Паттерн: ПКМ на орб (с Shift) → ЛКМ на каждую ячейку.
    /// </summary>
    public async Task ApplyAsync(
        ScreenRect orbRect,
        IReadOnlyList<ScreenRect> cells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // Шаг 1: переместиться к орбу и зажать Shift + ПКМ
        var (ox, oy) = orbRect.GetInteriorPoint(1);
        log?.Report($"[Орб] MoveTo орб ({ox},{oy})");
        Win32Input.MoveTo(ox, oy);
        await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

        log?.Report("[Орб] ShiftDown + ПКМ (орб на курсор)");
        Win32Input.ShiftDown();
        try
        {
            Win32Input.ClickRight();
            await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);

            // Шаг 2: ЛКМ на каждую таблетку
            for (var i = 0; i < cells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var cell = cells[i];
                var (cx, cy) = cell.GetInteriorPoint(1);
                log?.Report($"[Орб] Ячейка {i + 1}/{cells.Count} → ЛКМ ({cx},{cy})");
                Win32Input.MoveTo(cx, cy);
                await Task.Delay(40, ct).ConfigureAwait(false);
                Win32Input.ClickLeft();
                await DelayAsync(ActionDelayMs, ct).ConfigureAwait(false);
            }

            log?.Report($"[Орб] Готово — {cells.Count} ячеек.");
        }
        finally
        {
            Win32Input.ShiftUp();
            log?.Report("[Орб] ShiftUp.");
        }
    }
}
