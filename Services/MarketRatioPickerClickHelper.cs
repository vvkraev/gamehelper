using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Общие клики по Market Ratio: открытие области и выбор строки валюты в списке через OCR.</summary>
public static class MarketRatioPickerClickHelper
{
    public static int JitterDelay(int baseMs, Random? rng = null)
    {
        rng ??= Random.Shared;
        if (baseMs <= 0)
            return 0;
        var lo = Math.Max(0, baseMs - 15);
        var hi = baseMs + 25;
        return rng.Next(lo, hi + 1);
    }

    public static int PickerOpenDelayMs(int mouseActionDelayMs) =>
        Math.Clamp(mouseActionDelayMs * 5, 350, 1200);

    public static async Task<bool> ClickLeftInRectCenterAsync(
        ScreenRect rect,
        string stepLabel,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var (x, y) = rect.GetInteriorPoint(inset: 2);
        log?.Report($"[Market ratio] {stepLabel}: ЛКМ в ({x},{y}), область {rect.Width}×{rect.Height}");
        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        if (!Win32Input.MoveTo(x, y))
        {
            log?.Report($"[Market ratio] SetCursorPos({x},{y}) не удался.");
            return false;
        }

        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async Task<bool> OcrClickCurrencyLineAsync(
        ScreenRect pickerListRect,
        string currencyLabelForOcr,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var norm = WindowsOcrTextLocator.NormalizeForMatch(currencyLabelForOcr);
        if (string.IsNullOrEmpty(norm))
            return false;

        log?.Report($"[Market ratio] OCR в списке валют: «{currencyLabelForOcr}» (нормализовано {norm}), область {pickerListRect.X},{pickerListRect.Y} {pickerListRect.Width}×{pickerListRect.Height}");

        var match = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(pickerListRect, norm, log, cancellationToken)
            .ConfigureAwait(false);
        if (match is not { } found)
            return false;

        var (cx, cy) = found.BoundsOnScreen.GetInteriorPoint(inset: 1);
        log?.Report($"[Market ratio] ЛКМ по «{found.MatchedLineText}» → ({cx},{cy})");
        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        if (!Win32Input.MoveTo(cx, cy))
            return false;
        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
