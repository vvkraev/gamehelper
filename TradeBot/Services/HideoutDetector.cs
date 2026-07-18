namespace TradeBot.Services;

public sealed class HideoutDetector(ScreenRect merchantRegion, int timeoutMs = 30_000)
{
    public async Task<bool> WaitForMerchantAsync(CancellationToken ct, Action<string>? debugLog = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var lastText = "";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var text = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(merchantRegion, null, ct);
            var normalized = WindowsOcrTextLocator.NormalizeForMatch(text);
            if (normalized != lastText)
            {
                debugLog?.Invoke($"  [OCR] «{text.Trim()}»");
                lastText = normalized;
            }
            if (normalized.Contains("MERCHANT", StringComparison.Ordinal))
                return true;
            await Task.Delay(200, ct);
        }
        return false;
    }
}
