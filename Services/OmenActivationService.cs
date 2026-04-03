using System.Drawing;
using GameHelper.Native;

namespace GameHelper.Services;

public sealed class OmenActivationService
{
    public const string OmenSinistralExaltationName = "Omen of Sinistral Exaltation";
    public const string OmenDextralExaltationName = "Omen of Dextral Exaltation";
    public const string OmenGreaterExaltationName = "Omen of Greater Exaltation";

    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public bool TraceInputToLog { get; set; }

    // CV параметры (можно будет вынести в UI, если понадобится)
    public int BorderThicknessPx { get; set; } = 3;
    public int BorderInsetPx { get; set; } = 1;
    public double RedPixelRatioThreshold { get; set; } = 0.18; // доля "красных" пикселей на границе

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
        Win32Input.MoveTo(x, y);
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

    private static void ClearClipboardSafe()
    {
        try { System.Windows.Clipboard.Clear(); } catch { /* ignore */ }
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    private async Task ClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearClipboardSafe);

    private async Task<string> ReadClipboardTextAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);

    private async Task<string> ReadOmenClipboardWithRetryAsync(ScreenRect cell, IProgress<string>? log, CancellationToken ct, string tag)
    {
        async Task<string> OnceAsync()
        {
            await ClearClipboardAsync().ConfigureAwait(false);

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !cell.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, $"{tag}: MoveTo omen cell", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            Win32Input.SendCtrlC();
            await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);

            var text = await ReadClipboardTextAsync().ConfigureAwait(false);
            SessionLogger.InfoClipboard(tag, text);
            return text;
        }

        var first = await OnceAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        log?.Report($"{tag}: буфер пуст, retry через 200ms…");
        await Task.Delay(200, ct).ConfigureAwait(false);
        return await OnceAsync().ConfigureAwait(false);
    }

    private static bool ClipboardLooksLikeOmen(string clip, string omenName)
    {
        var t = (clip ?? "").Replace("\r\n", "\n").Trim();
        if (t.Length == 0)
            return false;
        // минимальные маркеры
        if (!t.Contains("Item Class: Omen", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!t.Contains("Rarity: Currency", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!t.Contains(omenName, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Красная рамка вокруг ячейки — омен считается визуально активированным (см. ASCII-флоу).</summary>
    public bool IsOmenCellVisuallyActivated(ScreenRect cell) => CellHasRedBorder(cell);

    private bool CellHasRedBorder(ScreenRect cell)
    {
        // Берём узкие полосы вдоль границы ячейки и считаем долю "красных" пикселей.
        var inset = Math.Max(0, BorderInsetPx);
        var th = Math.Max(1, BorderThicknessPx);

        var x = cell.X + inset;
        var y = cell.Y + inset;
        var w = Math.Max(1, cell.Width - 2 * inset);
        var h = Math.Max(1, cell.Height - 2 * inset);

        // если ячейка слишком маленькая — fallback: считаем, что рамки нет
        if (w < 6 || h < 6)
            return false;

        // Верх + низ + лево + право
        var regions = new List<Rectangle>
        {
            new(x, y, w, Math.Min(th, h)),
            new(x, y + Math.Max(0, h - th), w, Math.Min(th, h)),
            new(x, y, Math.Min(th, w), h),
            new(x + Math.Max(0, w - th), y, Math.Min(th, w), h),
        };

        long red = 0;
        long total = 0;

        using var bmp = new Bitmap(Math.Max(w, 1), Math.Max(h, 1));
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }

        foreach (var r in regions)
        {
            // переводим в координаты bmp
            var rr = new Rectangle(
                Math.Clamp(r.X - x, 0, w - 1),
                Math.Clamp(r.Y - y, 0, h - 1),
                Math.Clamp(r.Width, 1, w),
                Math.Clamp(r.Height, 1, h));

            for (var yy = rr.Y; yy < rr.Y + rr.Height; yy++)
            for (var xx = rr.X; xx < rr.X + rr.Width; xx++)
            {
                var c = bmp.GetPixel(xx, yy);
                total++;
                // "красный": R сильно больше G/B
                if (c.R > 160 && c.R - Math.Max(c.G, c.B) > 60)
                    red++;
            }
        }

        if (total == 0)
            return false;
        var ratio = red / (double)total;
        return ratio >= RedPixelRatioThreshold;
    }

    public sealed record OmenBulkResult(
        string OmenName,
        int CheckedCells,
        int EmptyCellsMarked,
        int OmenCellsFound,
        int NewlyActivated,
        IReadOnlyList<ScreenRect> OmenCells);

    /// <summary>
    /// Активирует ВСЕ найденные омены указанного типа в заданных ячейках (если они не активированы).
    /// Возвращает список ячеек, где был обнаружен нужный омен (активный или успешно активированный).
    /// Пустые ячейки (по Ctrl+C) можно кэшировать через <paramref name="markEmptyCells"/>.
    /// </summary>
    public async Task<OmenBulkResult> ActivateAllAsync(
        IReadOnlyList<ScreenRect> omenCells,
        string omenName,
        IProgress<string>? log,
        CancellationToken ct,
        ISet<ScreenRect>? skipCells = null,
        ISet<ScreenRect>? markEmptyCells = null)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        var found = new List<ScreenRect>();
        var newly = 0;
        var checkedCount = 0;
        var emptyMarked = 0;

        for (var i = 0; i < omenCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = omenCells[i];
            if (skipCells != null && skipCells.Contains(cell))
                continue;

            checkedCount++;
            log?.Report($"Омен: bulk-активация «{omenName}» — ячейка {i + 1} / {omenCells.Count}…");

            var clip = await ReadOmenClipboardWithRetryAsync(cell, log, ct, $"Омен: Ctrl+C (bulk {i + 1}/{omenCells.Count})").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(clip))
            {
                if (markEmptyCells != null && markEmptyCells.Add(cell))
                    emptyMarked++;
                continue;
            }

            if (!ClipboardLooksLikeOmen(clip, omenName))
                continue;

            // Нужный омен найден
            found.Add(cell);
            if (CellHasRedBorder(cell))
                continue; // уже активен

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !cell.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, "Омен: MoveTo ячейка перед bulk-активацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (bulk-активация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (CellHasRedBorder(cell))
                newly++;
        }

        log?.Report($"Омен: bulk «{omenName}»: найдено {found.Count}, активировано сейчас {newly}, помечено пустых {emptyMarked}.");
        return new OmenBulkResult(omenName, checkedCount, emptyMarked, found.Count, newly, found);
    }

    /// <summary>
    /// Деактивирует омены во всех переданных ячейках (если активны по красной рамке).
    /// </summary>
    public async Task<int> DeactivateAllAsync(
        IReadOnlyList<ScreenRect> omenCells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        var deactivated = 0;
        for (var i = 0; i < omenCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = omenCells[i];
            if (!CellHasRedBorder(cell))
                continue;

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !cell.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, "Омен: MoveTo ячейка перед bulk-деактивацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (bulk-деактивация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (!CellHasRedBorder(cell))
                deactivated++;
        }

        if (deactivated > 0)
            log?.Report($"Омен: bulk-деактивация: выключено {deactivated}.");
        return deactivated;
    }

    /// <summary>
    /// Активирует первый подходящий омен в области и возвращает ячейку, в которой он был найден/активирован.
    /// Возвращает <c>null</c>, если подходящего омена нет (закончились).
    /// </summary>
    public async Task<ScreenRect?> ActivateFirstAsync(
        IReadOnlyList<ScreenRect> omenCells,
        string omenName,
        IProgress<string>? log,
        CancellationToken ct,
        ISet<ScreenRect>? skipCells = null,
        ISet<ScreenRect>? markEmptyCells = null)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        for (var i = 0; i < omenCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = omenCells[i];
            if (skipCells != null && skipCells.Contains(cell))
                continue;
            log?.Report($"Омен: поиск «{omenName}» — ячейка {i + 1} / {omenCells.Count}…");

            var clip = await ReadOmenClipboardWithRetryAsync(cell, log, ct, $"Омен: Ctrl+C (ячейка {i + 1}/{omenCells.Count})").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(clip))
            {
                markEmptyCells?.Add(cell);
                continue;
            }
            if (!ClipboardLooksLikeOmen(clip, omenName))
                continue;

            if (CellHasRedBorder(cell))
            {
                log?.Report("Омен уже активирован (красная рамка).");
                return cell;
            }

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !cell.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, "Омен: MoveTo ячейка перед активацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (активация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (CellHasRedBorder(cell))
            {
                log?.Report("Омен активирован (красная рамка появилась).");
                return cell;
            }

            log?.Report("Омен найден, но красная рамка не обнаружена после ПКМ.");
            return cell; // нашли нужный омен по имени — считаем выбранной ячейкой, даже если CV не увидел рамку
        }

        log?.Report($"Нужный омен не найден: {omenName}");
        return null;
    }

    /// <summary>
    /// Пытается деактивировать омен в указанной ячейке (toggle по ПКМ), проверяя красную рамку.
    /// </summary>
    public async Task<bool> DeactivateAsync(ScreenRect cell, IProgress<string>? log, CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        if (!CellHasRedBorder(cell))
        {
            log?.Report("Омен: деактивация не требуется (красной рамки нет).");
            return true;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !cell.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, "Омен: MoveTo ячейка перед деактивацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (деактивация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (!CellHasRedBorder(cell))
            {
                log?.Report("Омен деактивирован (красная рамка пропала).");
                return true;
            }
        }

        log?.Report("Омен: не удалось подтвердить деактивацию (рамка осталась).");
        return false;
    }

    public async Task<bool> EnsureOmenActiveAsync(
        IReadOnlyList<ScreenRect> omenCells,
        string omenName,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        for (var i = 0; i < omenCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = omenCells[i];
            log?.Report($"Омен: проверка ячейки {i + 1} / {omenCells.Count}…");

            var clip = await ReadOmenClipboardWithRetryAsync(cell, log, ct, $"Омен: Ctrl+C (ячейка {i + 1}/{omenCells.Count})").ConfigureAwait(false);
            if (!ClipboardLooksLikeOmen(clip, omenName))
                continue;

            // Есть нужный омен. Проверяем рамку.
            if (CellHasRedBorder(cell))
            {
                log?.Report("Омен уже активирован (красная рамка).");
                return true;
            }

            // Активируем (ПКМ по ячейке) и проверяем снова.
            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            LogMove(log, "Омен: MoveTo ячейка перед активацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (активация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (CellHasRedBorder(cell))
            {
                log?.Report("Омен активирован (красная рамка появилась).");
                return true;
            }

            log?.Report("Омен найден, но красная рамка не обнаружена после ПКМ.");
        }

        log?.Report($"Нужный омен не найден/не активирован: {omenName}");
        return false;
    }
}

