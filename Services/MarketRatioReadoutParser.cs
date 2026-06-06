using System.Globalization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Парсинг текста курса «N : M» и комиссии в золоте из результата OCR.</summary>
public static class MarketRatioReadoutParser
{
    /// <summary>Ищет первое вхождение «число : число» (латинская или Unicode-двоеточие).</summary>
    public static (int? Left, int? Right) TryParseRatio(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var m = Regex.Match(raw, @"(\d+)\s*[:：]\s*(\d+)");
        if (!m.Success)
            return (null, null);

        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
            return (null, null);
        if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            return (null, null);
        return (a, b);
    }

    /// <summary>Первое целое в строке; если только «?» без цифр — null.</summary>
    public static int? TryParseGoldFee(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.Contains('?', StringComparison.Ordinal) && !Regex.IsMatch(raw, @"\d"))
            return null;

        var m = Regex.Match(raw, @"\d+");
        if (!m.Success)
            return null;

        return int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
