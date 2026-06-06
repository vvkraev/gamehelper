using System.Globalization;
using System.IO;
using System.Text;

namespace GameHelper.Services;

using GameHelper;

/// <summary>
/// Журнал снимков стакана: сырой текст в файл + строка сводки в CSV (спред через <see cref="CurrencyPairArbitrageCalculator"/>).
/// </summary>
public static class OrderBookSnapshotCsvLog
{
    private static readonly object Gate = new();

    public const string SummaryFileName = "market_order_book_snapshots.csv";

    public static string GetSummaryPath() => Path.Combine(ProjectPaths.GetLogDirectory(), SummaryFileName);

    private static string GetRawDirectory() => Path.Combine(ProjectPaths.GetLogDirectory(), "order_book_raw");

    /// <summary>Пишет сырой OCR в <c>Log/order_book_raw/</c> и добавляет строку в сводный CSV.</summary>
    public static void AppendSnapshot(
        DateTime scanStartedUtc,
        string pairId,
        string rawOcr,
        OrderBookOcrParseResult parsed,
        double? askMinusBidSpread)
    {
        Directory.CreateDirectory(GetRawDirectory());
        var safePair = string.Join("_", pairId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(safePair))
            safePair = "pair";

        var stamp = scanStartedUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var rawFile = $"ob_{stamp}_{safePair}.txt";
        var rawPath = Path.Combine(GetRawDirectory(), rawFile);
        File.WriteAllText(rawPath, rawOcr ?? "", new UTF8Encoding(true));

        var bestAvail = parsed.AvailableOffers.Count > 0 ? parsed.AvailableOffers[0].RatioRight : (double?)null;
        var bestComp = parsed.CompetingOffers.Count > 0 ? parsed.CompetingOffers[0].RatioRight : (double?)null;

        var line = string.Join(
            ',',
            CsvEscape(scanStartedUtc.ToString("o", CultureInfo.InvariantCulture)),
            CsvEscape(pairId),
            CsvEscape(rawFile),
            CsvEscape(parsed.AvailableOffers.Count.ToString(CultureInfo.InvariantCulture)),
            CsvEscape(parsed.CompetingOffers.Count.ToString(CultureInfo.InvariantCulture)),
            CsvEscape(bestAvail?.ToString("G9", CultureInfo.InvariantCulture)),
            CsvEscape(bestComp?.ToString("G9", CultureInfo.InvariantCulture)),
            CsvEscape(askMinusBidSpread?.ToString("G9", CultureInfo.InvariantCulture)));

        const string header =
            "scan_started_utc,pair_id,raw_text_file,available_rows_parsed,competing_rows_parsed,best_available_ratio_right,best_competing_ratio_right,ask_minus_bid_spread";

        lock (Gate)
        {
            var summaryPath = GetSummaryPath();
            var needHeader = !File.Exists(summaryPath) || new FileInfo(summaryPath).Length == 0;
            if (needHeader)
            {
                File.WriteAllText(
                    summaryPath,
                    header + Environment.NewLine + line + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            else
            {
                File.AppendAllText(summaryPath, line + Environment.NewLine, new UTF8Encoding(false));
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
