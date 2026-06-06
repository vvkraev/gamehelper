using System.Drawing;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Наведение на панель Market Ratio, удержание Alt, сдвиг курсора вправо — чтобы открылся полный стакан для OCR.
/// </summary>
public static class MarketRatioOrderBookDepthCapture
{
    private static int JitterDelay(int baseMs, Random? rng = null)
    {
        rng ??= Random.Shared;
        if (baseMs <= 0)
            return 0;
        var lo = Math.Max(0, baseMs - 15);
        var hi = baseMs + 25;
        return rng.Next(lo, hi + 1);
    }

    /// <summary>
    /// Курсор в центр <paramref name="hoverRect"/> → пауза → Alt вниз → пауза → +<paramref name="offsetXPx"/> px по X → пауза → OCR <paramref name="orderBookOcrRect"/> → Alt вверх.
    /// </summary>
    public static async Task<string> TryCaptureRawAsync(
        ScreenRect hoverRect,
        ScreenRect orderBookOcrRect,
        int offsetXPx,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var (hx, hy) = hoverRect.GetInteriorPoint(inset: 2);
        log?.Report($"[Стакан] Наведение на Market Ratio ({hx},{hy}), затем Alt и сдвиг +{offsetXPx} px.");

        try
        {
            await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
            if (!Win32Input.MoveTo(hx, hy))
            {
                log?.Report("[Стакан] SetCursorPos (hover) не удался.");
                return "";
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            Win32Input.AltDown();
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            if (!Win32Input.TryGetCursorPos(out var cx, out var cy))
            {
                log?.Report("[Стакан] GetCursorPos не удался перед сдвигом.");
                return "";
            }

            var nx = cx + offsetXPx;
            if (!Win32Input.MoveTo(nx, cy))
            {
                log?.Report($"[Стакан] SetCursorPos ({nx},{cy}) не удался.");
                return "";
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            using var bitmap = ScreenCaptureHelper.CaptureRegion(orderBookOcrRect);
            var text = await WindowsOcrTextLocator.RecognizeBitmapCollapsedAsync(bitmap, cancellationToken).ConfigureAwait(false);
            log?.Report(string.IsNullOrWhiteSpace(text)
                ? "[Стакан] OCR области стакана пустой."
                : $"[Стакан] OCR стакана: {text.Length} симв.");
            return text;
        }
        finally
        {
            Win32Input.AltUp();
            log?.Report("[Стакан] Alt отпущен.");
        }
    }

    /// <summary>
    /// Как <see cref="TryCaptureRawAsync"/>, плюс разбор стакана: сетка по заголовкам + ячейки, при слабом результате — текстовый <see cref="OrderBookOcrParser"/>.
    /// </summary>
    public static async Task<(string CollapsedRaw, OrderBookOcrParseResult Parsed)> TryCaptureParsedAsync(
        ScreenRect hoverRect,
        ScreenRect orderBookOcrRect,
        int offsetXPx,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var (hx, hy) = hoverRect.GetInteriorPoint(inset: 2);
        log?.Report($"[Стакан] Наведение на Market Ratio ({hx},{hy}), затем Alt и сдвиг +{offsetXPx} px.");

        try
        {
            await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
            if (!Win32Input.MoveTo(hx, hy))
            {
                log?.Report("[Стакан] SetCursorPos (hover) не удался.");
                return ("", new OrderBookOcrParseResult());
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            Win32Input.AltDown();
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            if (!Win32Input.TryGetCursorPos(out var cx, out var cy))
            {
                log?.Report("[Стакан] GetCursorPos не удался перед сдвигом.");
                return ("", new OrderBookOcrParseResult());
            }

            var nx = cx + offsetXPx;
            if (!Win32Input.MoveTo(nx, cy))
            {
                log?.Report($"[Стакан] SetCursorPos ({nx},{cy}) не удался.");
                return ("", new OrderBookOcrParseResult());
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            var manual = await OrderBookManualCellsRecognizer.TryRecognizeFromSettingsAsync(log, cancellationToken).ConfigureAwait(false);
            if (manual is { } m)
            {
                log?.Report(
                    $"[Стакан] Разбор (ручные ячейки): сценарий={m.ScenarioName}, " +
                    $"avail={m.Parsed.AvailableOffers.Count}, comp={m.Parsed.CompetingOffers.Count}, валидных строк={m.ParsedRowsScore}.");
                log?.Report(string.IsNullOrWhiteSpace(m.CollapsedRaw)
                    ? "[Стакан] OCR по ячейкам пустой."
                    : $"[Стакан] OCR по ячейкам: {m.CollapsedRaw.Length} симв.");
                return (m.CollapsedRaw, m.Parsed);
            }

            // Fallback, если ручные ячейки не заданы.
            using var bitmap = ScreenCaptureHelper.CaptureRegion(orderBookOcrRect);
            var raw = await WindowsOcrTextLocator.RecognizeBitmapCollapsedAsync(bitmap, cancellationToken).ConfigureAwait(false);
            var gridParsed = await OrderBookGridOcrRecognizer.TryParseAsync(bitmap, cancellationToken).ConfigureAwait(false);
            var textParsed = OrderBookOcrParser.TryParse(raw);
            var parsed = PickBetterOrderBookParse(gridParsed, textParsed);
            log?.Report(
                $"[Стакан] Разбор fallback: сетка {gridParsed.AvailableOffers.Count}+{gridParsed.CompetingOffers.Count}, текст {textParsed.AvailableOffers.Count}+{textParsed.CompetingOffers.Count} → выбрано {parsed.AvailableOffers.Count}+{parsed.CompetingOffers.Count}.");
            log?.Report(string.IsNullOrWhiteSpace(raw)
                ? "[Стакан] OCR области стакана пустой."
                : $"[Стакан] OCR стакана: {raw.Length} симв.");

            return (raw, parsed);
        }
        finally
        {
            Win32Input.AltUp();
            log?.Report("[Стакан] Alt отпущен.");
        }
    }

    private static OrderBookOcrParseResult PickBetterOrderBookParse(OrderBookOcrParseResult grid, OrderBookOcrParseResult text)
    {
        var sg = grid.AvailableOffers.Count + grid.CompetingOffers.Count;
        var st = text.AvailableOffers.Count + text.CompetingOffers.Count;
        if (sg > st)
            return grid;
        if (st > sg)
            return text;
        return grid;
    }
}
