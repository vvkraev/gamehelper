using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Человекочитаемый вывод разобранного стакана в лог (AVAILABLE / COMPETING, столбцы Ratio и Stock).</summary>
public static class OrderBookOcrLogFormatter
{
    public static void ReportParsedOrderBook(IProgress<string>? log, string? rawOcr, OrderBookOcrParseResult parsed)
    {
        if (log == null)
            return;

        log.Report("[Стакан] Разбор OCR — по секциям, столбцы Ratio (курс) и Stock (размер):");
        AppendSection(log, "AVAILABLE TRADES", parsed.AvailableOffers);
        AppendSection(log, "COMPETING TRADES", parsed.CompetingOffers);
        ReportRowCountHint(log, "AVAILABLE", parsed.AvailableOffers.Count);
        ReportRowCountHint(log, "COMPETING", parsed.CompetingOffers.Count);

        if (parsed.AvailableOffers.Count == 0 && parsed.CompetingOffers.Count == 0
            && !string.IsNullOrWhiteSpace(rawOcr))
        {
            var hint = TruncateOneLine(rawOcr!, maxChars: 280);
            log.Report($"[Стакан] Таблицу разобрать не удалось; сырой текст (обрезка): «{hint}»");
        }
    }

    private static void AppendSection(IProgress<string> log, string title, IReadOnlyList<OrderBookOfferRow> rows)
    {
        log.Report($"--- {title} ---");
        if (rows.Count == 0)
        {
            log.Report("  (нет распознанных строк)");
            return;
        }

        const int ratioCol = 22;
        var sb = new StringBuilder();
        sb.Append("  ").Append("Ratio".PadRight(ratioCol)).Append("Stock");
        log.Report(sb.ToString());

        foreach (var r in rows)
        {
            sb.Clear();
            sb.Append("  ").Append(FormatRatioColumn(r).PadRight(ratioCol)).Append(FormatStockColumn(r.Stock));
            log.Report(sb.ToString());
        }
    }

    private static string FormatRatioColumn(OrderBookOfferRow r)
    {
        var core = $"{FormatRatioNumber(r.RatioLeft)} : {FormatRatioNumber(r.RatioRight)}";
        return r.RatioInequalityPrefix switch
        {
            '<' => $"< {core}",
            '>' => $"> {core}",
            _ => core
        };
    }

    private static string FormatRatioNumber(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
            return "—";
        if (Math.Abs(x - Math.Round(x, 6)) < 1e-9)
            return Math.Round(x, 0).ToString("0", CultureInfo.InvariantCulture);
        return x.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static string FormatStockColumn(int? stock) =>
        stock is { } s ? s.ToString(CultureInfo.InvariantCulture) : "—";

    private static void ReportRowCountHint(IProgress<string> log, string section, int count)
    {
        const int expected = 6;
        if (count == 0 || count == expected)
            return;
        log.Report($"[Стакан] {section}: распознано {count} строк (в UI PoE2 обычно {expected}; при расхождении проверьте OCR или сырой файл в Log/order_book_raw/).");
    }

    private static string TruncateOneLine(string s, int maxChars)
    {
        var one = Regex.Replace(s.Trim(), @"\s+", " ");
        return one.Length <= maxChars ? one : one.Substring(0, maxChars) + "…";
    }
}
