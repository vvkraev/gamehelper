using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace GameHelper.Services;

/// <summary>
/// Стакан Market Ratio: якоря по заголовкам секций и колонок, фиксированные 6 строк на секцию,
/// OCR по ячейкам (без одной «простыни» текста).
/// </summary>
public static class OrderBookGridOcrRecognizer
{
    public const int RowsPerSection = 6;

    /// <summary>Разбор из файла (тесты и отладка).</summary>
    public static async Task<OrderBookOcrParseResult> TryParseFromImageFileAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            return Empty();

        using var bmp = new Bitmap(absolutePath);
        return await TryParseAsync(bmp, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<OrderBookOcrParseResult> TryParseAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width < 32 || bitmap.Height < 32)
            return Empty();

        var (lines, _) = await WindowsOcrTextLocator.RecognizeBitmapGeometryAsync(bitmap, cancellationToken).ConfigureAwait(false);
        if (lines.Count == 0)
            return Empty();

        var sorted = lines.OrderBy(l => l.Bounds.Y).ToList();
        var w = bitmap.Width;
        var h = bitmap.Height;

        var availTitle = sorted.FirstOrDefault(l => IsAvailableSectionTitle(l.NormText));
        var compTitle = sorted.FirstOrDefault(l => IsCompetingSectionTitle(l.NormText));
        var hasAvailTitle = IsMeaningfulGeomLine(availTitle);
        var hasCompTitle = IsMeaningfulGeomLine(compTitle);

        var colHeaders = FindColumnHeaderCandidates(sorted).OrderBy(l => l.Bounds.Y).ToList();

        var availColHdr = default(WindowsOcrTextLocator.OcrImageLine);
        var foundAvailHdr = false;
        if (hasAvailTitle)
        {
            var minY = availTitle.Bounds.Y + availTitle.Bounds.Height;
            var maxY = hasCompTitle ? compTitle.Bounds.Y - 2 : double.MaxValue;
            availColHdr = colHeaders.FirstOrDefault(ch => ch.Bounds.Y >= minY && ch.Bounds.Y < maxY);
            foundAvailHdr = IsMeaningfulGeomLine(availColHdr);
        }
        else if (hasCompTitle)
        {
            availColHdr = colHeaders.FirstOrDefault(ch => ch.Bounds.Y < compTitle.Bounds.Y - 4);
            foundAvailHdr = IsMeaningfulGeomLine(availColHdr);
        }
        else if (colHeaders.Count > 0)
        {
            availColHdr = colHeaders[0];
            foundAvailHdr = IsMeaningfulGeomLine(availColHdr);
        }

        var compColHdr = default(WindowsOcrTextLocator.OcrImageLine);
        var foundCompHdr = false;
        if (hasCompTitle)
        {
            var minY = compTitle.Bounds.Y + compTitle.Bounds.Height;
            compColHdr = colHeaders.FirstOrDefault(ch => ch.Bounds.Y >= minY);
            foundCompHdr = IsMeaningfulGeomLine(compColHdr);
        }

        var available = new List<OrderBookOfferRow>();
        var competing = new List<OrderBookOfferRow>();

        if (foundAvailHdr)
        {
            var gridTop = availColHdr.Bounds.Y + availColHdr.Bounds.Height + 3;
            double gridBottom;
            if (hasCompTitle)
                gridBottom = compTitle.Bounds.Y - 4;
            else if (foundCompHdr)
                gridBottom = compColHdr.Bounds.Y - 4;
            else
                gridBottom = h - 2;

            if (gridBottom > gridTop + 20)
            {
                var splitX = ResolveColumnSplitX(availColHdr, w);
                await FillSectionAsync(bitmap, gridTop, gridBottom, splitX, available, cancellationToken).ConfigureAwait(false);
            }
        }

        if (foundCompHdr)
        {
            var gridTop = compColHdr.Bounds.Y + compColHdr.Bounds.Height + 3;
            var gridBottom = h - 2.0;
            if (gridBottom > gridTop + 20)
            {
                var splitX = ResolveColumnSplitX(compColHdr, w);
                await FillSectionAsync(bitmap, gridTop, gridBottom, splitX, competing, cancellationToken).ConfigureAwait(false);
            }
        }

        return new OrderBookOcrParseResult { AvailableOffers = available, CompetingOffers = competing };
    }

    /// <summary>
    /// Стабильная сетка секции: всегда 6 строк × 2 столбца (Ratio/Stock) внутри найденного блока.
    /// Для каждой строки OCR делается по всей полосе и по двум ячейкам.
    /// </summary>
    private static async Task FillSectionAsync(
        Bitmap bitmap,
        double gridTop,
        double gridBottom,
        double splitX,
        List<OrderBookOfferRow> target,
        CancellationToken cancellationToken)
    {
        var rowH = (gridBottom - gridTop) / RowsPerSection;
        if (rowH < 6)
            return;

        var w = bitmap.Width;
        var inset = 1;
        // Вёрстка стакана стабильна: разрез колонок около середины.
        // На практике точнее фиксированная доля ширины, чем шумный splitX из OCR заголовков.
        var splitXi = (int)Math.Round(w * 0.52);
        splitXi = (int)Math.Clamp(splitXi, inset * 2, w - inset * 2);
        var minRatioW = (int)(w * 0.26);
        if (splitXi < minRatioW)
            splitXi = minRatioW;
        if (w - splitXi < Math.Max(minRatioW / 2, 40))
            splitXi = w - Math.Max(minRatioW / 2, 40);

        var vPad = Math.Clamp(rowH * 0.12, 1.5, 8.0);
        for (var i = 0; i < RowsPerSection; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var y0 = gridTop + i * rowH + inset - vPad;
            var y1 = i == RowsPerSection - 1
                ? gridBottom - inset * 2
                : gridTop + (i + 1) * rowH - inset + vPad;
            if (y1 <= y0 + 2)
                continue;

            var yi0 = (int)Math.Round(y0);
            var yi1 = (int)Math.Round(Math.Max(y1, y0 + 4));

            var ratioRect = Rectangle.FromLTRB(
                inset,
                yi0,
                Math.Max(splitXi - inset, inset + 1),
                yi1);
            var stockRect = Rectangle.FromLTRB(
                splitXi + inset,
                yi0,
                w - inset,
                yi1);

            using var ratioCrop = TryCrop(bitmap, ratioRect);
            using var stockCrop = TryCrop(bitmap, stockRect);
            if (ratioCrop == null || stockCrop == null)
            {
                target.Add(EmptySlot(i));
                continue;
            }

            var ratioText = await RecognizeCellTextAsync(ratioCrop, cancellationToken).ConfigureAwait(false);
            var stockText = await RecognizeCellTextAsync(stockCrop, cancellationToken).ConfigureAwait(false);
            if (OrderBookOcrParser.TryParseDataCells(ratioText, stockText, out var row))
            {
                target.Add(row);
                continue;
            }

            target.Add(EmptySlot(i));
        }
    }

    private static OrderBookOfferRow EmptySlot(int rowIndex) =>
        new(double.NaN, double.NaN, null, $"grid-empty[{rowIndex}]", null);

    private static async Task<string> RecognizeCellTextAsync(Bitmap crop, CancellationToken cancellationToken)
    {
        if (crop.Width < 200 || crop.Height < 18)
        {
            using var scaled = ScreenCaptureHelper.ScaleByIntegerFactor(crop, 2);
            return await WindowsOcrTextLocator.RecognizeBitmapCollapsedAsync(scaled, cancellationToken).ConfigureAwait(false);
        }

        return await WindowsOcrTextLocator.RecognizeBitmapCollapsedAsync(crop, cancellationToken).ConfigureAwait(false);
    }

    private static Bitmap? TryCrop(Bitmap src, Rectangle r)
    {
        var bounds = new Rectangle(0, 0, src.Width, src.Height);
        r.Intersect(bounds);
        if (r.Width < 4 || r.Height < 4)
            return null;
        try
        {
            return src.Clone(r, PixelFormat.Format32bppArgb);
        }
        catch
        {
            return null;
        }
    }

    private static double ResolveColumnSplitX(WindowsOcrTextLocator.OcrImageLine header, int imageWidth)
    {
        if (header.Words is not { Count: > 0 })
            return imageWidth * 0.52;

        double? stockLeft = null;
        double? ratioRight = null;
        foreach (var wg in header.Words)
        {
            var nw = WindowsOcrTextLocator.NormalizeForMatch(wg.Text);
            if (nw.Contains("STOCK", StringComparison.Ordinal) || nw.Contains("ST0CK", StringComparison.Ordinal))
            {
                var x = wg.Bounds.X;
                stockLeft = stockLeft is null ? x : Math.Min(stockLeft.Value, x);
            }

            if (nw.Contains("RATION", StringComparison.Ordinal)
                || (nw.Contains("RATIO", StringComparison.Ordinal) && !nw.Contains("RATION", StringComparison.Ordinal)))
            {
                var rx = wg.Bounds.X + wg.Bounds.Width;
                ratioRight = ratioRight is null ? rx : Math.Max(ratioRight.Value, rx);
            }
        }

        if (stockLeft.HasValue && ratioRight.HasValue)
            return (ratioRight.Value + stockLeft.Value) / 2.0;
        if (stockLeft.HasValue)
            return stockLeft.Value - 8;
        return imageWidth * 0.52;
    }

    private static bool IsAvailableSectionTitle(string norm) =>
        norm.Contains("AVAILABLE", StringComparison.Ordinal) && norm.Contains("TRADE", StringComparison.Ordinal);

    private static bool IsCompetingSectionTitle(string norm) =>
        (norm.Contains("COMPETITING", StringComparison.Ordinal) || norm.Contains("COMPETING", StringComparison.Ordinal))
        && norm.Contains("TRADE", StringComparison.Ordinal);

    private static bool IsColumnHeaderLine(string norm) =>
        (norm.Contains("STOCK", StringComparison.Ordinal) || norm.Contains("ST0CK", StringComparison.Ordinal))
        && (norm.Contains("RATION", StringComparison.Ordinal)
            || (norm.Contains("RATIO", StringComparison.Ordinal) && !norm.Contains("RATION", StringComparison.Ordinal)));

    /// <summary>Не принимать «AVAILABLE TRADES … Ratio … Stock» за строку только заголовков колонок.</summary>
    private static bool IsColumnHeaderCandidate(string norm) =>
        IsColumnHeaderLine(norm)
        && !IsAvailableSectionTitle(norm)
        && !IsCompetingSectionTitle(norm);

    /// <summary>OCR часто кладёт «Ratio» и «Stock» на две соседние строки — склеиваем короткие промежутки.</summary>
    private static List<WindowsOcrTextLocator.OcrImageLine> FindColumnHeaderCandidates(IReadOnlyList<WindowsOcrTextLocator.OcrImageLine> sortedByY)
    {
        var result = new List<WindowsOcrTextLocator.OcrImageLine>();
        var heights = sortedByY.Select(l => l.Bounds.Height).Where(h => h > 1).OrderBy(h => h).ToList();
        var medianH = heights.Count > 0 ? heights[heights.Count / 2] : 14.0;
        var mergeGap = Math.Max(10.0, medianH * 1.55);

        for (var i = 0; i < sortedByY.Count; i++)
        {
            if (IsColumnHeaderCandidate(sortedByY[i].NormText))
                result.Add(sortedByY[i]);

            var union = sortedByY[i].Bounds;
            var parts = new List<string> { sortedByY[i].Text ?? "" };
            for (var j = i + 1; j < Math.Min(i + 5, sortedByY.Count); j++)
            {
                var prevBottom = union.Y + union.Height;
                var gap = sortedByY[j].Bounds.Y - prevBottom;
                if (gap > mergeGap)
                    break;
                union = UnionRect(union, sortedByY[j].Bounds);
                parts.Add(sortedByY[j].Text ?? "");
                var mergedText = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                var mergedNorm = WindowsOcrTextLocator.NormalizeForMatch(mergedText);
                if (IsColumnHeaderCandidate(mergedNorm))
                {
                    result.Add(new WindowsOcrTextLocator.OcrImageLine(mergedText, mergedNorm, union, null));
                    break;
                }
            }
        }

        return DeduplicateColumnHeaders(result);
    }

    private static List<WindowsOcrTextLocator.OcrImageLine> DeduplicateColumnHeaders(List<WindowsOcrTextLocator.OcrImageLine> raw)
    {
        raw.Sort((a, b) => a.Bounds.Y.CompareTo(b.Bounds.Y));
        var kept = new List<WindowsOcrTextLocator.OcrImageLine>();
        foreach (var line in raw)
        {
            var dup = kept.Exists(
                k => Math.Abs(k.Bounds.Y - line.Bounds.Y) < 6 && Math.Abs(k.Bounds.Height - line.Bounds.Height) < 10);
            if (!dup)
                kept.Add(line);
        }

        return kept;
    }

    private static Windows.Foundation.Rect UnionRect(Windows.Foundation.Rect a, Windows.Foundation.Rect b)
    {
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
        var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Windows.Foundation.Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool IsMeaningfulGeomLine(WindowsOcrTextLocator.OcrImageLine line) =>
        line.Bounds.Width > 4 && line.Bounds.Height > 4;

    private static OrderBookOcrParseResult Empty() =>
        new()
        {
            AvailableOffers = Array.Empty<OrderBookOfferRow>(),
            CompetingOffers = Array.Empty<OrderBookOfferRow>()
        };
}
