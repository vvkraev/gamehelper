using System.Runtime.InteropServices;
using GameHelper.Native;

namespace GameHelper.Services;

public sealed class SharpenService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public bool TraceInputToLog { get; set; }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0)
            return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        if (delta <= 0)
            return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static Task DelayJitterAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    private void LogMove(IProgress<string>? log, string label, int x, int y)
    {
        if (TraceInputToLog)
            log?.Report($"[Ввод] {label}: SetCursorPos({x},{y})");

        if (!Win32Input.MoveTo(x, y))
        {
            var err = Marshal.GetLastWin32Error();
            log?.Report($"[Ввод] ОШИБКА SetCursorPos ({x},{y}): Win32 код {err}");
        }
    }

    private void LogMouse(IProgress<string>? log, string label)
    {
        if (!TraceInputToLog)
            return;
        if (Win32Input.TryGetCursorPos(out var x, out var y))
            log?.Report($"[Ввод] {label} @ позиция курсора ({x},{y})");
        else
            log?.Report($"[Ввод] {label}");
    }

    public async Task RunAsync(
        ScreenRect sharpenArea,
        IReadOnlyList<ScreenRect> itemCells,
        int clicksPerCell,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (clicksPerCell < 1)
            clicksPerCell = 1;

        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        // ПКМ по области "заточка"
        var (sx, sy) = sharpenArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        LogMove(log, "MoveTo заточка (случайная точка)", sx, sy);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        LogMouse(log, "ПКМ (заточка)");
        Win32Input.ClickRight();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        // Shift + 20 кликов ЛКМ по каждой ячейке
        Win32Input.ShiftDown();
        try
        {
            for (var ci = 0; ci < itemCells.Count; ci++)
            {
                ct.ThrowIfCancellationRequested();
                var cell = itemCells[ci];
                log?.Report($"Заточка: ячейка {ci + 1} / {itemCells.Count}…");

                for (var i = 0; i < clicksPerCell; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (ix, iy) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                    LogMove(log, "MoveTo ячейка предмета (случайная точка)", ix, iy);
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                    LogMouse(log, $"ЛКМ (заточка) {i + 1}/{clicksPerCell}");
                    Win32Input.ClickLeft();
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Win32Input.ShiftUp();
        }
    }
}

