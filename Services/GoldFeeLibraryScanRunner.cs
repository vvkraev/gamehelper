using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// По очереди выставляет I WANT для каждой валюты из списка и OCR области золота; обновляет <see cref="GoldFeeLibraryStore"/>.
/// I HAVE не трогаем — заранее выставьте в игре нужную валюту «отдаю».
/// </summary>
public static class GoldFeeLibraryScanRunner
{
    public sealed class Result
    {
        public int Total { get; init; }
        public int Appended { get; init; }
        public int SkippedDuplicatePair { get; init; }
        public int OcrGoldFailed { get; init; }
        public int PickerFailed { get; init; }
    }

    public static async Task<Result> RunAsync(
        ScreenRect iWantClickRect,
        ScreenRect currencyPickerListRect,
        ScreenRect goldFeeReadoutRect,
        IReadOnlyList<string> currencyLabels,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        if (goldFeeReadoutRect is not { Width: > 0, Height: > 0 })
        {
            log?.Report("[Золото I WANT] Область золота не задана — сканирование невозможно.");
            return new Result();
        }

        var appended = 0;
        var skippedDup = 0;
        var goldFail = 0;
        var pickFail = 0;
        var mouseMs = Math.Max(0, mouseActionDelayMs);

        for (var i = 0; i < currencyLabels.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = currencyLabels[i];
            log?.Report($"[Золото I WANT] ({i + 1}/{currencyLabels.Count}) «{label}»…");

            if (!await MarketRatioPickerClickHelper.ClickLeftInRectCenterAsync(
                    iWantClickRect,
                    "I WANT — открыть выбор",
                    mouseMs,
                    log,
                    cancellationToken).ConfigureAwait(false))
            {
                pickFail++;
                continue;
            }

            await Task.Delay(MarketRatioPickerClickHelper.PickerOpenDelayMs(mouseMs), cancellationToken).ConfigureAwait(false);

            if (!await MarketRatioPickerClickHelper.OcrClickCurrencyLineAsync(
                    currencyPickerListRect,
                    label,
                    mouseMs,
                    log,
                    cancellationToken).ConfigureAwait(false))
            {
                log?.Report($"[Золото I WANT] Не найдено в списке: «{label}»");
                pickFail++;
                continue;
            }

            await Task.Delay(Math.Clamp(mouseMs * 3, 280, 1000), cancellationToken).ConfigureAwait(false);

            var goldRaw = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(goldFeeReadoutRect, log, cancellationToken)
                .ConfigureAwait(false);
            var goldNum = MarketRatioReadoutParser.TryParseGoldFee(goldRaw);
            if (goldNum is not { } fee)
            {
                log?.Report($"[Золото I WANT] Не разобрать золото из «{goldRaw}»");
                goldFail++;
                continue;
            }

            var scanUtc = DateTime.UtcNow;
            if (GoldFeeLibraryStore.TryAppendIfNewCurrencyFeePair(label, fee, scanUtc))
            {
                appended++;
                log?.Report($"[Золото I WANT] Запись: «{label}» → {fee} золота, UTC {scanUtc:O}");
            }
            else
            {
                skippedDup++;
                log?.Report($"[Золото I WANT] Пара «{label}»+{fee} уже в файле — строка не добавлена.");
            }
        }

        return new Result
        {
            Total = currencyLabels.Count,
            Appended = appended,
            SkippedDuplicatePair = skippedDup,
            OcrGoldFailed = goldFail,
            PickerFailed = pickFail
        };
    }
}
