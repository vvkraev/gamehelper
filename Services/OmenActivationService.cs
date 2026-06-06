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
    /// <summary>Толщина полос, по которым ищем красную рамку активации (внутри области после <see cref="BorderInsetPx"/>).</summary>
    public int BorderThicknessPx { get; set; } = 2;
    /// <summary>Отступ от краёв прямоугольника ячейки внутрь — сначала ужимаем область, затем по её периметру снимаем рамку.</summary>
    public int BorderInsetPx { get; set; } = 2;
    /// <summary>Доля пикселей рамки, попадающих под <see cref="LooksLikeExaltationOmenBorderPixel"/>.</summary>
    public double RedPixelRatioThreshold { get; set; } = 0.16;

    /// <summary>
    /// Дополнительный захват пикселей за пределами внутреннего прямоугольника ячейки (расплыв рамки за слот).
    /// </summary>
    public int BorderBleedOutPx { get; set; } = 3;

    /// <summary>
    /// «Полый» центр: со всех сторон соседи дают меньше свечения на общую грань, чем на внешние рёбра.
    /// Если mean(к соседу)/mean(наружу) &lt; этого коэффициента — ячейка считается выключенной (только при 4 соседях).
    /// </summary>
    public double NeighborFacingHighlightRatioMax { get; set; } = 0.72;

    private enum BorderStripEdge { Top, Bottom, Left, Right }

    private static int HorizontalOverlap(ScreenRect a, ScreenRect b)
    {
        var lo = Math.Max(a.X, b.X);
        var hi = Math.Min(a.X + a.Width, b.X + b.Width);
        return Math.Max(0, hi - lo);
    }

    private static int VerticalOverlap(ScreenRect a, ScreenRect b)
    {
        var lo = Math.Max(a.Y, b.Y);
        var hi = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return Math.Max(0, hi - lo);
    }

    private static int? FindNeighborUp(IReadOnlyList<ScreenRect> cells, int i)
    {
        var c = cells[i];
        int? best = null;
        var bestAbs = int.MaxValue;
        var minOv = (int)(Math.Max(1, Math.Min(c.Width, c.Height)) * 0.42);
        for (var j = 0; j < cells.Count; j++)
        {
            if (j == i)
                continue;
            var o = cells[j];
            if (HorizontalOverlap(c, o) < minOv)
                continue;
            var gap = c.Y - (o.Y + o.Height);
            if (gap < -10 || gap > c.Height + 12)
                continue;
            var a = Math.Abs(gap);
            if (a < bestAbs)
            {
                bestAbs = a;
                best = j;
            }
        }

        return best;
    }

    private static int? FindNeighborDown(IReadOnlyList<ScreenRect> cells, int i)
    {
        var c = cells[i];
        int? best = null;
        var bestAbs = int.MaxValue;
        var minOv = (int)(Math.Max(1, Math.Min(c.Width, c.Height)) * 0.42);
        for (var j = 0; j < cells.Count; j++)
        {
            if (j == i)
                continue;
            var o = cells[j];
            if (HorizontalOverlap(c, o) < minOv)
                continue;
            var gap = o.Y - (c.Y + c.Height);
            if (gap < -10 || gap > c.Height + 12)
                continue;
            var a = Math.Abs(gap);
            if (a < bestAbs)
            {
                bestAbs = a;
                best = j;
            }
        }

        return best;
    }

    private static int? FindNeighborLeft(IReadOnlyList<ScreenRect> cells, int i)
    {
        var c = cells[i];
        int? best = null;
        var bestAbs = int.MaxValue;
        var minOv = (int)(Math.Max(1, Math.Min(c.Width, c.Height)) * 0.42);
        for (var j = 0; j < cells.Count; j++)
        {
            if (j == i)
                continue;
            var o = cells[j];
            if (VerticalOverlap(c, o) < minOv)
                continue;
            var gap = c.X - (o.X + o.Width);
            if (gap < -10 || gap > c.Width + 12)
                continue;
            var a = Math.Abs(gap);
            if (a < bestAbs)
            {
                bestAbs = a;
                best = j;
            }
        }

        return best;
    }

    private static int? FindNeighborRight(IReadOnlyList<ScreenRect> cells, int i)
    {
        var c = cells[i];
        int? best = null;
        var bestAbs = int.MaxValue;
        var minOv = (int)(Math.Max(1, Math.Min(c.Width, c.Height)) * 0.42);
        for (var j = 0; j < cells.Count; j++)
        {
            if (j == i)
                continue;
            var o = cells[j];
            if (VerticalOverlap(c, o) < minOv)
                continue;
            var gap = o.X - (c.X + c.Width);
            if (gap < -10 || gap > c.Width + 12)
                continue;
            var a = Math.Abs(gap);
            if (a < bestAbs)
            {
                bestAbs = a;
                best = j;
            }
        }

        return best;
    }

    private static bool TryGetFourOrthogonalNeighbors(
        IReadOnlyList<ScreenRect> cells,
        int i,
        out int up,
        out int down,
        out int left,
        out int right)
    {
        up = down = left = right = -1;
        var u = FindNeighborUp(cells, i);
        var d = FindNeighborDown(cells, i);
        var l = FindNeighborLeft(cells, i);
        var r = FindNeighborRight(cells, i);
        if (u is null || d is null || l is null || r is null)
            return false;
        up = u.Value;
        down = d.Value;
        left = l.Value;
        right = r.Value;
        return true;
    }

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

    /// <summary>MoveTo в (x,y) только если курсор ещё не внутри <paramref name="region"/>.</summary>
    private void MoveToRandomInteriorIfOutside(ScreenRect region, IProgress<string>? log, string traceLabel, int x, int y)
    {
        if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !region.ContainsPoint(cx, cy, inset: 1))
            LogMove(log, traceLabel, x, y);
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
            MoveToRandomInteriorIfOutside(cell, log, $"{tag}: MoveTo omen cell", x, y);
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
    public bool IsOmenCellVisuallyActivated(ScreenRect cell) =>
        IsOmenCellVisuallyActivated(cell, null, -1);

    /// <summary>
    /// То же с контекстом сетки: при 4 ортогональных соседях отсекается «полый» центр (только bleed соседей).
    /// </summary>
    public bool IsOmenCellVisuallyActivated(
        ScreenRect cell,
        IReadOnlyList<ScreenRect>? gridCells,
        int gridIndex)
    {
        var ratio = ComputeBorderHighlightRatio(cell);
        if (ratio < RedPixelRatioThreshold)
            return false;
        if (gridCells is null || gridIndex < 0 || gridIndex >= gridCells.Count)
            return true;
        if (LooksLikeDeactivatedHollowCenter(gridCells, gridIndex))
            return false;
        return true;
    }

    /// <summary>
    /// Пиксель похож на подсветку рамки экзальт-омена: чистый красный, оранжево-жёлтое «пламя»
    /// (важно для золотистой иконки рядом с рамкой, см. ячейка ~20 на тренировочных скринах).
    /// </summary>
    private static bool LooksLikeExaltationOmenBorderPixel(Color c)
    {
        var r = c.R;
        var g = c.G;
        var b = c.B;
        if (r <= 90 && g <= 90 && b <= 90)
            return false;
        // яркая золотая заливка иконки, не рамка
        if (r > 215 && g > 200 && b > 95)
            return false;
        // насыщенная «кровавая» рамка без сильного жёлтого (часть тренировочных скринов)
        if (r > 172 && g < 128 && b < 132 && r - Math.Max(g, b) > 48)
            return true;
        if (r > 160 && r - Math.Max(g, b) > 60)
            return true;
        if (r > 145 && r - Math.Max(g, b) > 42)
            return true;
        if (r > 155 && g > 52 && g < 215 && r >= g - 38 && r > b + 15)
            return true;
        if (r > 168 && g > 58 && g < 208 && r + g > 238 && r > b + 18)
            return true;
        if (r > 188 && g > 82 && g < 232 && r + g > 268 && r > b + 26)
            return true;
        return false;
    }

    private double SampleScreenRectHighlightRatio(int sx, int sy, int sw, int sh)
    {
        if (sw < 1 || sh < 1)
            return 0;
        using var bmp = new Bitmap(sw, sh);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(sx, sy, 0, 0, new Size(sw, sh), CopyPixelOperation.SourceCopy);
        }

        long red = 0;
        var tot = (long)sw * sh;
        for (var yy = 0; yy < sh; yy++)
        for (var xx = 0; xx < sw; xx++)
        {
            if (LooksLikeExaltationOmenBorderPixel(bmp.GetPixel(xx, yy)))
                red++;
        }

        return tot > 0 ? red / (double)tot : 0;
    }

    /// <summary>Полоса по внутреннему прямоугольнику ячейки (без bleed), для сравнения соседей.</summary>
    private double ComputeInnerStripHighlightRatio(ScreenRect cell, BorderStripEdge edge)
    {
        var edgeInset = Math.Max(0, BorderInsetPx);
        var frameTh = Math.Max(1, BorderThicknessPx);
        var ix = cell.X + edgeInset;
        var iy = cell.Y + edgeInset;
        var iw = cell.Width - 2 * edgeInset;
        var ih = cell.Height - 2 * edgeInset;
        if (iw < 6 || ih < 6)
            return 0;
        var th = Math.Min(frameTh, Math.Min(iw, ih) / 2);
        if (th < 1)
            return 0;
        var midH = ih - 2 * th;
        if (midH < 1)
            return 0;

        return edge switch
        {
            BorderStripEdge.Top => SampleScreenRectHighlightRatio(ix, iy, iw, th),
            BorderStripEdge.Bottom => SampleScreenRectHighlightRatio(ix, iy + ih - th, iw, th),
            BorderStripEdge.Left => SampleScreenRectHighlightRatio(ix, iy + th, th, midH),
            BorderStripEdge.Right => SampleScreenRectHighlightRatio(ix + iw - th, iy + th, th, midH),
            _ => 0
        };
    }

    /// <summary>Средняя доля по трём внутренним полосам, исключая грань, обращённую к центру 3×3 (на кропе «наружу» не уходит в пустой край картинки).</summary>
    private double AverageInnerStripHighlightRatioExcludingFacing(ScreenRect cell, BorderStripEdge facingTowardCenter)
    {
        double a, b, c;
        switch (facingTowardCenter)
        {
            case BorderStripEdge.Bottom:
                a = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Top);
                b = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Left);
                c = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Right);
                break;
            case BorderStripEdge.Top:
                a = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Bottom);
                b = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Left);
                c = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Right);
                break;
            case BorderStripEdge.Right:
                a = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Top);
                b = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Bottom);
                c = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Left);
                break;
            case BorderStripEdge.Left:
                a = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Top);
                b = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Bottom);
                c = ComputeInnerStripHighlightRatio(cell, BorderStripEdge.Right);
                break;
            default:
                return 0;
        }

        return (a + b + c) / 3.0;
    }

    /// <summary>
    /// Доля подсветки по периметру: внутренний прямоугольник + расширение полос на <see cref="BorderBleedOutPx"/> (расплыв за слот).
    /// </summary>
    private double ComputeBorderHighlightRatio(ScreenRect cell)
    {
        var edgeInset = Math.Max(0, BorderInsetPx);
        var bleed = Math.Max(0, BorderBleedOutPx);
        var frameTh = Math.Max(1, BorderThicknessPx);

        var ix = cell.X + edgeInset;
        var iy = cell.Y + edgeInset;
        var iw = cell.Width - 2 * edgeInset;
        var ih = cell.Height - 2 * edgeInset;
        if (iw < 6 || ih < 6)
            return 0;

        var th = Math.Min(frameTh, Math.Min(iw, ih) / 2);
        if (th < 1)
            return 0;
        var midH = ih - 2 * th;
        if (midH < 1)
            return 0;

        var capX = ix - bleed;
        var capY = iy - bleed;
        var capW = iw + 2 * bleed;
        var capH = ih + 2 * bleed;

        var srcX = capX;
        var srcY = capY;
        var skipX = 0;
        var skipY = 0;
        var copyW = capW;
        var copyH = capH;
        if (srcX < 0)
        {
            skipX = -srcX;
            copyW -= skipX;
            srcX = 0;
        }

        if (srcY < 0)
        {
            skipY = -srcY;
            copyH -= skipY;
            srcY = 0;
        }

        if (copyW < 1 || copyH < 1)
            return 0;

        var innerBmpX = ix - srcX;
        var innerBmpY = iy - srcY;

        long red = 0;
        long total = 0;

        using var bmp = new Bitmap(copyW, copyH);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(srcX, srcY, 0, 0, new Size(copyW, copyH), CopyPixelOperation.SourceCopy);
        }

        void AccumRect(int rx, int ry, int rw, int rh)
        {
            if (rw < 1 || rh < 1)
                return;
            rx = Math.Clamp(rx, 0, copyW - 1);
            ry = Math.Clamp(ry, 0, copyH - 1);
            rw = Math.Min(rw, copyW - rx);
            rh = Math.Min(rh, copyH - ry);
            if (rw < 1 || rh < 1)
                return;
            for (var yy = ry; yy < ry + rh; yy++)
            for (var xx = rx; xx < rx + rw; xx++)
            {
                total++;
                if (LooksLikeExaltationOmenBorderPixel(bmp.GetPixel(xx, yy)))
                    red++;
            }
        }

        var topY = Math.Max(0, innerBmpY - bleed);
        var topH = innerBmpY + th - topY;
        AccumRect(innerBmpX, topY, iw, topH);

        var botY = innerBmpY + ih - th;
        var botH = Math.Min(copyH - botY, th + bleed);
        AccumRect(innerBmpX, botY, iw, botH);

        var leftX = Math.Max(0, innerBmpX - bleed);
        var leftW = innerBmpX + th - leftX;
        AccumRect(leftX, innerBmpY + th, leftW, midH);

        var rightX = innerBmpX + iw - th;
        var rightW = Math.Min(copyW - rightX, th + bleed);
        AccumRect(rightX, innerBmpY + th, rightW, midH);

        return total > 0 ? red / (double)total : 0;
    }

    /// <summary>
    /// Центр без собственной рамки, но края красные от соседей: у соседей на грани к нам слабее, чем наружу.
    /// </summary>
    private bool LooksLikeDeactivatedHollowCenter(IReadOnlyList<ScreenRect> cells, int i)
    {
        if (!TryGetFourOrthogonalNeighbors(cells, i, out var up, out var down, out var left, out var right))
            return false;

        var inward = (
            ComputeInnerStripHighlightRatio(cells[up], BorderStripEdge.Bottom)
            + ComputeInnerStripHighlightRatio(cells[down], BorderStripEdge.Top)
            + ComputeInnerStripHighlightRatio(cells[left], BorderStripEdge.Right)
            + ComputeInnerStripHighlightRatio(cells[right], BorderStripEdge.Left)) / 4.0;

        var outward = (
            AverageInnerStripHighlightRatioExcludingFacing(cells[up], BorderStripEdge.Bottom)
            + AverageInnerStripHighlightRatioExcludingFacing(cells[down], BorderStripEdge.Top)
            + AverageInnerStripHighlightRatioExcludingFacing(cells[left], BorderStripEdge.Right)
            + AverageInnerStripHighlightRatioExcludingFacing(cells[right], BorderStripEdge.Left)) / 4.0;

        if (outward < 0.028)
            return false;
        return inward < outward * NeighborFacingHighlightRatioMax;
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
            if (IsOmenCellVisuallyActivated(cell, omenCells, i))
                continue; // уже активен

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(cell, log, "Омен: MoveTo ячейка перед bulk-активацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (bulk-активация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (IsOmenCellVisuallyActivated(cell, omenCells, i))
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
            if (!IsOmenCellVisuallyActivated(cell, omenCells, i))
                continue;

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(cell, log, "Омен: MoveTo ячейка перед bulk-деактивацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (bulk-деактивация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (!IsOmenCellVisuallyActivated(cell, omenCells, i))
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

            if (IsOmenCellVisuallyActivated(cell, omenCells, i))
            {
                log?.Report("Омен уже активирован (красная рамка).");
                return cell;
            }

            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(cell, log, "Омен: MoveTo ячейка перед активацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (активация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (IsOmenCellVisuallyActivated(cell, omenCells, i))
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
    public async Task<bool> DeactivateAsync(
        ScreenRect cell,
        IProgress<string>? log,
        CancellationToken ct,
        IReadOnlyList<ScreenRect>? gridCells = null,
        int gridIndex = -1)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        if (!IsOmenCellVisuallyActivated(cell, gridCells, gridIndex))
        {
            log?.Report("Омен: деактивация не требуется (красной рамки нет).");
            return true;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(cell, log, "Омен: MoveTo ячейка перед деактивацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (деактивация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (!IsOmenCellVisuallyActivated(cell, gridCells, gridIndex))
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
            if (IsOmenCellVisuallyActivated(cell, omenCells, i))
            {
                log?.Report("Омен уже активирован (красная рамка).");
                return true;
            }

            // Активируем (ПКМ по ячейке) и проверяем снова.
            var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(cell, log, "Омен: MoveTo ячейка перед активацией", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogMouse(log, "Омен: ПКМ (активация)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(150, ct).ConfigureAwait(false);

            if (IsOmenCellVisuallyActivated(cell, omenCells, i))
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

