using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Сценарий сбора данных о текущем курсе обмена: фокус на игру, открытие торговли по OCR имени (если настроено), задел под дальнейший OCR/CSV.
/// </summary>
public static class ExchangeRateInfoCollectionScan
{
    public static async Task RunAsync(
        ScreenRect? traderNameSearchRect,
        string npcName,
        ScreenRect? marketRatioIHaveClickRect,
        ScreenRect? marketRatioIWantClickRect,
        ScreenRect? marketRatioCurrencyPickerListRect,
        ScreenRect? marketRatioRateReadoutRect,
        ScreenRect? marketRatioGoldFeeReadoutRect,
        ScreenRect? marketRatioDepthHoverRect,
        ScreenRect? marketRatioOrderBookOcrRect,
        int marketRatioDepthHoverOffsetXPx,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var scanStartedUtc = DateTime.UtcNow;
        log?.Report($"[Сбор курса] Старт сценария (время запуска UTC {scanStartedUtc:O}).");

        await Task.Delay(Math.Clamp(mouseActionDelayMs * 2, 120, 600), cancellationToken).ConfigureAwait(false);
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(Math.Clamp(mouseActionDelayMs * 3, 200, 900), cancellationToken).ConfigureAwait(false);

        if (traderNameSearchRect is { Width: > 0, Height: > 0 } rect)
        {
            log?.Report("[Сбор курса] Шаг: открыть окно торговли (OCR имени + Ctrl+ЛКМ).");
            var opened = await TraderNpcNameOpenTradeAction.RunAsync(rect, npcName, mouseActionDelayMs, log, cancellationToken)
                .ConfigureAwait(false);
            if (!opened)
                log?.Report("[Сбор курса] Не удалось открыть торговлю по имени — проверьте область OCR и имя NPC.");
        }
        else
        {
            log?.Report(
                "[Сбор курса] Область OCR имени торговца не задана — шаг открытия торговли пропущен. Задайте область на вкладке «Настройки областей» (блок «Торговец (OCR имени)»).");
        }

        if (marketRatioIHaveClickRect is { Width: > 0, Height: > 0 } ih
            && marketRatioIWantClickRect is { Width: > 0, Height: > 0 } iw
            && marketRatioCurrencyPickerListRect is { Width: > 0, Height: > 0 } pl)
        {
            log?.Report("[Сбор курса] Шаг: Market Ratio — Exalted Orb (I HAVE) и Divine Orb (I WANT).");
            await MarketRatioExaltedDivineAutomation.RunAsync(
                    ih,
                    iw,
                    pl,
                    mouseActionDelayMs,
                    scanStartedUtc,
                    marketRatioRateReadoutRect,
                    marketRatioGoldFeeReadoutRect,
                    marketRatioDepthHoverRect,
                    marketRatioOrderBookOcrRect,
                    marketRatioDepthHoverOffsetXPx,
                    log,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            log?.Report(
                "[Сбор курса] Области Market Ratio не заданы полностью (I HAVE / I WANT / список валют) — выбор пары на вкладке «Сбор информации» пропущен.");
        }

        log?.Report("[Сбор курса] Сценарий завершён.");
    }
}
