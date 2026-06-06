using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Окно Market Ratio в PoE2: I HAVE — отдаём (здесь Exalted Orb), I WANT — получаем (Divine Orb).
/// Открывает выбор валюты ЛКМ по областям, затем ищет подписи в списке через OCR и кликает ЛКМ.
/// </summary>
public static class MarketRatioExaltedDivineAutomation
{
    private const string LabelSellExalted = "Exalted Orb";
    private const string LabelBuyDivine = "Divine Orb";

    public const string PairIdExaltedDivine = "exalted_divine";

    /// <summary>
    /// Выставляет пару: продаём Exalted Orb (I HAVE), покупаем Divine Orb (I WANT).
    /// После успеха — OCR областей курса и золота и строка в <see cref="ExchangeRateCsvLog"/>.
    /// </summary>
    public static async Task<bool> RunAsync(
        ScreenRect iHaveClickRect,
        ScreenRect iWantClickRect,
        ScreenRect currencyPickerListRect,
        int mouseActionDelayMs,
        DateTime scanStartedUtc,
        ScreenRect? rateReadoutRect,
        ScreenRect? goldFeeReadoutRect,
        ScreenRect? depthHoverRect,
        ScreenRect? orderBookOcrRect,
        int depthHoverOffsetXPx,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        log?.Report("[Market ratio] Пара Exalted Orb (I HAVE) → Divine Orb (I WANT).");

        if (!await MarketRatioPickerClickHelper.ClickLeftInRectCenterAsync(iHaveClickRect, "I HAVE — открыть выбор (продаём Exalted)", mouseActionDelayMs, log, cancellationToken).ConfigureAwait(false))
            return false;

        await Task.Delay(MarketRatioPickerClickHelper.PickerOpenDelayMs(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);

        if (!await MarketRatioPickerClickHelper.OcrClickCurrencyLineAsync(currencyPickerListRect, LabelSellExalted, mouseActionDelayMs, log, cancellationToken).ConfigureAwait(false))
        {
            log?.Report($"[Market ratio] Не найдено в списке: {LabelSellExalted}");
            return false;
        }

        await Task.Delay(MarketRatioPickerClickHelper.PickerOpenDelayMs(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);

        if (!await MarketRatioPickerClickHelper.ClickLeftInRectCenterAsync(iWantClickRect, "I WANT — открыть выбор (покупаем Divine)", mouseActionDelayMs, log, cancellationToken).ConfigureAwait(false))
            return false;

        await Task.Delay(MarketRatioPickerClickHelper.PickerOpenDelayMs(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);

        if (!await MarketRatioPickerClickHelper.OcrClickCurrencyLineAsync(currencyPickerListRect, LabelBuyDivine, mouseActionDelayMs, log, cancellationToken).ConfigureAwait(false))
        {
            log?.Report($"[Market ratio] Не найдено в списке: {LabelBuyDivine}");
            return false;
        }

        await Task.Delay(MarketRatioPickerClickHelper.JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        log?.Report("[Market ratio] Пара выставлена; снимаем курс и золото с экрана.");

        await Task.Delay(Math.Clamp(mouseActionDelayMs * 3, 280, 1000), cancellationToken).ConfigureAwait(false);

        if (rateReadoutRect is { Width: > 0, Height: > 0 } rr && goldFeeReadoutRect is { Width: > 0, Height: > 0 } gr)
        {
            var rateRaw = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(rr, log, cancellationToken).ConfigureAwait(false);
            var goldRaw = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(gr, log, cancellationToken).ConfigureAwait(false);
            var (ratioLeft, ratioRight) = MarketRatioReadoutParser.TryParseRatio(rateRaw);
            var goldNum = MarketRatioReadoutParser.TryParseGoldFee(goldRaw);

            ExchangeRateCsvLog.AppendRow(
                scanStartedUtc,
                PairIdExaltedDivine,
                rateRaw,
                goldRaw,
                ratioLeft,
                ratioRight,
                goldNum);

            log?.Report(
                $"[Market ratio] CSV {ExchangeRateCsvLog.FileName}: scan={scanStartedUtc:O}, курс «{rateRaw}», золото «{goldRaw}» " +
                $"(разбор: {ratioLeft?.ToString() ?? "—"}:{ratioRight?.ToString() ?? "—"}, gold={goldNum?.ToString() ?? "—"}).");
        }
        else
            log?.Report("[Market ratio] Области «курс» и «золото» не заданы — запись в CSV пропущена (задайте на вкладке «Сбор информации»).");

        if (depthHoverRect is { Width: > 0, Height: > 0 } dh && orderBookOcrRect is { Width: > 0, Height: > 0 } ob)
        {
            var offX = Math.Clamp(depthHoverOffsetXPx, -1200, 1200);
            var (depthText, parsed) = await MarketRatioOrderBookDepthCapture.TryCaptureParsedAsync(
                    dh,
                    ob,
                    offX,
                    mouseActionDelayMs,
                    log,
                    cancellationToken)
                .ConfigureAwait(false);
            OrderBookOcrLogFormatter.ReportParsedOrderBook(log, depthText, parsed);
            var bestAvailR = PickBestRatioRight(parsed.AvailableOffers);
            var bestCompR = PickBestRatioRight(parsed.CompetingOffers);
            var spread = CurrencyPairArbitrageCalculator.TryComputeAskMinusBidSpread(bestAvailR, bestCompR);

            OrderBookSnapshotCsvLog.AppendSnapshot(scanStartedUtc, PairIdExaltedDivine, depthText, parsed, spread);

            log?.Report(
                $"[Стакан] Запись: {OrderBookSnapshotCsvLog.SummaryFileName}, сырой текст в Log/order_book_raw/; " +
                $"разбор строк: avail={parsed.AvailableOffers.Count}, comp={parsed.CompetingOffers.Count}; " +
                $"лучшие R: ask={bestAvailR?.ToString() ?? "—"}, bid={bestCompR?.ToString() ?? "—"}; spread(ask−bid)={spread?.ToString() ?? "—"}.");
        }
        else
            log?.Report("[Стакан] Область наведения Market Ratio или область OCR стакана не заданы — шаг стакана пропущен.");

        return true;
    }

    private static double? PickBestRatioRight(IReadOnlyList<OrderBookOfferRow> rows)
    {
        foreach (var r in rows)
        {
            if (r.Stock is not { } st || st <= 0)
                continue;
            if (double.IsNaN(r.RatioRight) || double.IsInfinity(r.RatioRight) || r.RatioRight <= 0)
                continue;
            return r.RatioRight;
        }

        return null;
    }
}
