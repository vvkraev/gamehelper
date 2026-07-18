namespace GameHelper.Services;

/// <summary>OCR-детектор текущей игровой локации.</summary>
public static class LocationDetector
{
    /// <summary>
    /// Распознаёт название локации в заданной области экрана.
    /// Возвращает пустую строку если область не задана или OCR не дал результата.
    /// </summary>
    public static async Task<string> DetectAsync(ScreenRect area, CancellationToken ct)
    {
        if (area.Width <= 0 || area.Height <= 0) return "";
        return await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(area, log: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет, содержит ли распознанный текст ожидаемое название (без учёта регистра и пробелов).
    /// </summary>
    public static bool LocationMatchesExpected(string detectedText, string expectedLocation)
    {
        if (string.IsNullOrWhiteSpace(expectedLocation)) return false;
        var norm   = WindowsOcrTextLocator.NormalizeForMatch(detectedText);
        var target = WindowsOcrTextLocator.NormalizeForMatch(expectedLocation);
        return !string.IsNullOrEmpty(target) && norm.Contains(target, StringComparison.Ordinal);
    }
}
