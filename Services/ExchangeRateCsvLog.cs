using System.Globalization;
using System.IO;
using System.Text;

namespace GameHelper.Services;

using GameHelper;

/// <summary>Общий CSV журнал курсов валют в <c>Log/currency_exchange_rates.csv</c>.</summary>
public static class ExchangeRateCsvLog
{
    private static readonly object Gate = new();

    public const string FileName = "currency_exchange_rates.csv";

    public static string GetFilePath() => Path.Combine(ProjectPaths.GetLogDirectory(), FileName);

    /// <param name="scanStartedUtc">Момент старта сканирования (UTC).</param>
    public static void AppendRow(
        DateTime scanStartedUtc,
        string pairId,
        string rateText,
        string goldFeeText,
        int? ratioLeft,
        int? ratioRight,
        int? goldFeeNumeric)
    {
        var line = string.Join(
            ',',
            CsvEscape(scanStartedUtc.ToString("o", CultureInfo.InvariantCulture)),
            CsvEscape(pairId),
            CsvEscape(rateText),
            CsvEscape(goldFeeText),
            CsvEscape(ratioLeft?.ToString(CultureInfo.InvariantCulture)),
            CsvEscape(ratioRight?.ToString(CultureInfo.InvariantCulture)),
            CsvEscape(goldFeeNumeric?.ToString(CultureInfo.InvariantCulture)));

        const string header =
            "scan_started_utc,pair_id,rate_text,gold_fee_text,ratio_left,ratio_right,gold_fee_numeric";

        lock (Gate)
        {
            var path = GetFilePath();
            var needHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            if (needHeader)
            {
                File.WriteAllText(
                    path,
                    header + Environment.NewLine + line + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            else
            {
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
    }

    private static string CsvEscape(string? value)
    {
        value ??= "";
        var mustQuote = value.IndexOfAny("\",\r\n".ToCharArray()) >= 0;
        if (!mustQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
