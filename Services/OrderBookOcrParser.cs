using System.Globalization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>Результат разбора текста стакана (OCR) на секции Available / Competing.</summary>
public sealed class OrderBookOcrParseResult
{
    public IReadOnlyList<OrderBookOfferRow> AvailableOffers { get; init; } = Array.Empty<OrderBookOfferRow>();
    public IReadOnlyList<OrderBookOfferRow> CompetingOffers { get; init; } = Array.Empty<OrderBookOfferRow>();
}

/// <summary>Одна строка таблицы Ratio / Stock (по возможности). <see cref="RatioInequalityPrefix"/> — хвост стакана в игре (&lt; / &gt;).</summary>
public readonly record struct OrderBookOfferRow(
    double RatioLeft,
    double RatioRight,
    int? Stock,
    string SourceLine,
    char? RatioInequalityPrefix = null);

/// <summary>Эвристический разбор OCR стакана (доступно / конкурирующие сделки).</summary>
public static class OrderBookOcrParser
{
    private static readonly Regex RatioRegex = new(
        @"(?<prefix>[<>]?)\s*(?<left>\d+)\s*[:：]\s*(?<right>[\d.]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Хвост книги: «&lt; 1 : 222.50» / «&gt; 1 : 186» — сначала эти совпадения, иначе внутреннее «1 : …» даст дубликат.</summary>
    private static readonly Regex RatioInequalityRegex = new(
        @"(?<pre>[<>])\s*(?<left>\d+)\s*[:：]\s*(?<right>[\d.]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RatioBulletRegex = new(
        @"\d+\s*[•]\s*[\d.]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RatioColonRegex = new(
        @"\d+\s*[:：]\s*[\d.]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RatioLooseDecimalRegex = new(
        @"\b(\d+)\s+(\d+\.\d+)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StockTailRegex = new(
        @"(\d[\d\s]{0,18})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>AVAILABLE … Ratio|Ration … Stock … до COMPETING (включая вариант COMPETITING).</summary>
    private static readonly Regex AvailableSectionRegex = new(
        @"(?is)AVAILABLE\s+TRADES\s+(?:Ration|Ratio)\s+(?<ratios>.+?)\s+Stock\s+(?<stocks>.+?)(?=\s+COMPET(?:ING|ITING)\s+TRADES\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CompetingSectionRegex = new(
        @"(?is)COMPET(?:ING|ITING)\s+TRADES\s+(?:Ration|Ratio)\s+(?<ratios>.+?)\s+Stock\s+(?<stocks>.+?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>«1 . 204» от OCR → «1 : 204»; не трогает «222.50» (нет пробелов вокруг точки).</summary>
    private static readonly Regex OcrDotBetweenDigitsRegex = new(
        @"(\d)\s+\.\s+(\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>OCR часто теряет двоеточие в курсе: «1 221» → «1 : 221» (используется только для ratio-ячейки).</summary>
    private static readonly Regex OcrMissingRatioColonRegex = new(
        @"(?<!\d)([<>]?\s*\d)\s+(\d{2,4}(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static OrderBookOcrParseResult TryParse(string? rawOcr)
    {
        if (string.IsNullOrWhiteSpace(rawOcr))
            return Empty();

        var collapsed = Regex.Replace(rawOcr.Trim(), @"\s+", " ");

        // Есть обе секции — только «плоский» разбор (многострочный даёт ложные пары на одной строке OCR).
        if (IndexOfCompetingTradesHeader(collapsed) >= 0)
            return TryParseFlatBlob(rawOcr);

        var multiline = TryParseMultiline(rawOcr);
        var flat = TryParseFlatBlob(rawOcr);

        var multilineScore = multiline.AvailableOffers.Count + multiline.CompetingOffers.Count;
        var flatScore = flat.AvailableOffers.Count + flat.CompetingOffers.Count;

        if (flatScore > multilineScore)
            return flat;

        if (multiline.AvailableOffers.Count > 0 || multiline.CompetingOffers.Count > 0)
            return multiline;

        return flat;
    }

    private static OrderBookOcrParseResult Empty() =>
        new() { AvailableOffers = Array.Empty<OrderBookOfferRow>(), CompetingOffers = Array.Empty<OrderBookOfferRow>() };

    private static OrderBookOcrParseResult TryParseMultiline(string rawOcr)
    {
        var available = new List<OrderBookOfferRow>();
        var competing = new List<OrderBookOfferRow>();

        var norm = rawOcr.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = norm.Split('\n', StringSplitOptions.None);

        List<OrderBookOfferRow>? target = null;
        foreach (var line in lines)
        {
            var L = line.Trim();
            if (L.Length == 0)
                continue;

            if (ContainsAvailableHeader(L))
            {
                target = available;
                continue;
            }

            if (ContainsCompetingHeader(L))
            {
                target = competing;
                continue;
            }

            if (target == null)
                continue;

            if (IsHeaderOrNoise(L))
                continue;

            if (TryParseOfferLine(L, out var row))
                target.Add(row);
        }

        return new OrderBookOcrParseResult { AvailableOffers = available, CompetingOffers = competing };
    }

    /// <summary>
    /// Один «кирпич» текста от OCR: AVAILABLE TRADES Ratio … Stock … COMPETING TRADES Ratio … Stock …
    /// Разделитель курса — двоеточие или точка «•» (частая ошибка OCR вместо «:»).
    /// </summary>
    private static OrderBookOcrParseResult TryParseFlatBlob(string rawOcr)
    {
        var available = new List<OrderBookOfferRow>();
        var competing = new List<OrderBookOfferRow>();

        var s = Regex.Replace(rawOcr.Trim(), @"\s+", " ");
        s = OcrDotBetweenDigitsRegex.Replace(s, "$1 : $2");
        s = MergeLeadingNumericRunIntoAvailableAfterRatioHeader(s);

        var am = AvailableSectionRegex.Match(s);
        if (am.Success)
            AppendZippedRatioStockRows(am.Groups["ratios"].Value.Trim(), am.Groups["stocks"].Value.Trim(), available);
        else
        {
            var competingIdx = IndexOfCompetingTradesHeader(s);
            var availChunk = competingIdx >= 0 ? s.Substring(0, competingIdx).Trim() : s;
            ParseFlatSectionInto(availChunk, available);
        }

        var cm = CompetingSectionRegex.Match(s);
        if (cm.Success)
            AppendZippedRatioStockRows(cm.Groups["ratios"].Value.Trim(), cm.Groups["stocks"].Value.Trim(), competing);
        else
        {
            var competingIdx = IndexOfCompetingTradesHeader(s);
            if (competingIdx >= 0)
                ParseFlatSectionInto(s.Substring(competingIdx).Trim(), competing);
        }

        return new OrderBookOcrParseResult { AvailableOffers = available, CompetingOffers = competing };
    }

    internal static OrderBookOcrParseResult TryParseFlatBlobForTests(string rawOcr) => TryParseFlatBlob(rawOcr);

    /// <summary>Если до AVAILABLE идёт блок цифр/курсов, Windows OCR часто выносит его вперёд — вставляем после заголовка Ratio.</summary>
    private static string MergeLeadingNumericRunIntoAvailableAfterRatioHeader(string s)
    {
        var ci = CultureInfo.InvariantCulture.CompareInfo;
        var availIdx = ci.IndexOf(s, "AVAILABLE TRADES", CompareOptions.IgnoreCase);
        if (availIdx <= 0)
            return s;

        var prefix = s.Substring(0, availIdx).Trim();
        if (prefix.Length < 3 || !Regex.IsMatch(prefix, @"\d"))
            return s.Substring(availIdx).Trim();

        var tail = s.Substring(availIdx).Trim();
        var m = Regex.Match(tail, @"^(AVAILABLE\s+TRADES\s+)(Ration|Ratio)(\s+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return tail;

        return m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + prefix + " " + tail.Substring(m.Length).TrimStart();
    }

    private static void AppendZippedRatioStockRows(string ratioPart, string stockPart, List<OrderBookOfferRow> target)
    {
        var ratios = ExtractFlatRatios(ratioPart);
        var stocks = ExtractFlatStocks(stockPart);
        var n = Math.Min(ratios.Count, stocks.Count);
        for (var i = 0; i < n; i++)
        {
            var (L, R, ineq) = ratios[i];
            var st = StockDoubleToInt(stocks[i]);
            target.Add(new OrderBookOfferRow(L, R, st, $"flat[{i}]", ineq));
        }
    }

    /// <summary>OCR часто даёт COMPETITING или обрезает TRADES.</summary>
    private static int IndexOfCompetingTradesHeader(string s)
    {
        ReadOnlySpan<string> keys =
        [
            "COMPETING TRADES",
            "COMPETITING TRADES",
            "COMPETING TRADE",
            "COMPETITING TRADE"
        ];

        var best = -1;
        foreach (var k in keys)
        {
            var i = CultureInfo.InvariantCulture.CompareInfo.IndexOf(s, k, CompareOptions.IgnoreCase);
            if (i >= 0 && (best < 0 || i < best))
                best = i;
        }

        return best;
    }

    private static void ParseFlatSectionInto(string chunk, List<OrderBookOfferRow> target)
    {
        if (string.IsNullOrWhiteSpace(chunk))
            return;

        var ratioWord = FindRatioColumnHeaderIndex(chunk, 0);
        var stockWord = FindStockColumnHeaderIndex(chunk, Math.Max(0, ratioWord + 1));
        if (ratioWord < 0 || stockWord < 0 || stockWord <= ratioWord)
            return;

        var ratioHeaderLen = GetRatioHeaderWordLength(chunk, ratioWord);
        var stockHeaderLen = GetStockHeaderWordLength(chunk, stockWord);

        var ratioEnd = ratioWord + ratioHeaderLen;
        var ratioPart = chunk.Substring(ratioEnd, stockWord - ratioEnd).Trim();
        var stockEnd = stockWord + stockHeaderLen;
        var stockPart = chunk.Substring(stockEnd).Trim();

        AppendZippedRatioStockRows(ratioPart, stockPart, target);
    }

    private static int FindWholeWordIndex(string s, string word, int startIndex = 0)
    {
        if (startIndex < 0 || startIndex >= s.Length)
            return -1;
        var m = Regex.Match(s.Substring(startIndex), @"\b" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase);
        return m.Success ? startIndex + m.Index : -1;
    }

    /// <summary>Windows OCR иногда читает заголовок колонки как «Ration».</summary>
    private static int FindRatioColumnHeaderIndex(string s, int startIndex)
    {
        var ration = FindWholeWordIndex(s, "Ration", startIndex);
        var ratio = FindWholeWordIndex(s, "Ratio", startIndex);
        if (ration < 0)
            return ratio;
        if (ratio < 0)
            return ration;
        return Math.Min(ration, ratio);
    }

    /// <summary>Редко: St0ck.</summary>
    private static int FindStockColumnHeaderIndex(string s, int startIndex)
    {
        var stock = FindWholeWordIndex(s, "Stock", startIndex);
        if (stock >= 0)
            return stock;
        return FindWholeWordIndex(s, "St0ck", startIndex);
    }

    private static int GetRatioHeaderWordLength(string chunk, int idx)
    {
        if (idx >= 0 && idx < chunk.Length)
        {
            if (Regex.IsMatch(chunk[idx..], @"^Ration\b", RegexOptions.IgnoreCase))
                return 6;
            if (Regex.IsMatch(chunk[idx..], @"^Ratio\b", RegexOptions.IgnoreCase))
                return 5;
        }

        return 5;
    }

    private static int GetStockHeaderWordLength(string chunk, int idx)
    {
        if (idx >= 0 && idx < chunk.Length)
        {
            if (Regex.IsMatch(chunk[idx..], @"^St0ck\b", RegexOptions.IgnoreCase))
                return 5;
            if (Regex.IsMatch(chunk[idx..], @"^Stock\b", RegexOptions.IgnoreCase))
                return 5;
        }

        return 5;
    }

    private static bool SpansOverlap(int a0, int a1, int b0, int b1) =>
        Math.Max(a0, b0) < Math.Min(a1, b1);

    private static List<(double L, double R, char? Ineq)> ExtractFlatRatios(string ratioPart)
    {
        var spans = new List<(int lo, int hi, double L, double R, char? ineq)>();

        void TryAddSpan(int lo, int hi, double L, double R, char? ineq) =>
            spans.Add((lo, hi, L, R, ineq));

        bool Overlaps(int lo, int hi) =>
            spans.Exists(t => SpansOverlap(t.lo, t.hi, lo, hi));

        foreach (Match m in RatioInequalityRegex.Matches(ratioPart))
        {
            if (!double.TryParse(m.Groups["left"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var L))
                continue;
            if (!double.TryParse(m.Groups["right"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var R))
                continue;
            var pre = m.Groups["pre"].Value;
            var ch = pre.Length > 0 && (pre[0] is '<' or '>') ? pre[0] : (char?)null;
            TryAddSpan(m.Index, m.Index + m.Length, L, R, ch);
        }

        foreach (Match m in RatioBulletRegex.Matches(ratioPart))
        {
            if (Overlaps(m.Index, m.Index + m.Length))
                continue;
            if (!TryParseRatioToken(m.Value, out var L, out var R))
                continue;
            TryAddSpan(m.Index, m.Index + m.Length, L, R, null);
        }

        foreach (Match m in RatioColonRegex.Matches(ratioPart))
        {
            if (Overlaps(m.Index, m.Index + m.Length))
                continue;
            if (!TryParseRatioToken(m.Value, out var L, out var R))
                continue;
            TryAddSpan(m.Index, m.Index + m.Length, L, R, null);
        }

        foreach (Match m in RatioLooseDecimalRegex.Matches(ratioPart))
        {
            if (Overlaps(m.Index, m.Index + m.Length))
                continue;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var L))
                continue;
            if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var R))
                continue;
            TryAddSpan(m.Index, m.Index + m.Length, L, R, null);
        }

        spans.Sort((a, b) => a.lo.CompareTo(b.lo));
        var list = new List<(double L, double R, char? Ineq)>();
        foreach (var t in spans)
            list.Add((t.L, t.R, t.ineq));
        return list;
    }

    private static bool TryParseRatioToken(string token, out double L, out double R)
    {
        L = R = 0;
        var sepIdx = token.IndexOfAny(['•', ':', '：']);
        if (sepIdx < 0)
            return false;
        if (!double.TryParse(token.AsSpan(0, sepIdx).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out L))
            return false;
        if (!double.TryParse(token.AsSpan(sepIdx + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out R))
            return false;
        return true;
    }

    /// <summary>
    /// Разбор колонки Stock в «плоском» OCR: отдельные строки таблицы — отдельные числа;
    /// внутри одного значения PoE2 группирует по 3 цифры («1 000», «16 872», «910 950»).
    /// Нельзя жадно склеивать все «\d + \d\d\d», иначе «7 421 116» станет одним числом.
    /// </summary>
    private static List<double> ExtractFlatStocks(string stockPart)
    {
        var cut = stockPart.IndexOf("COMPETING", StringComparison.OrdinalIgnoreCase);
        if (cut >= 0)
            stockPart = stockPart.Substring(0, cut).Trim();

        var chips = new List<string>();
        foreach (var seg in stockPart.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seg.Length > 0 && seg.All(char.IsDigit))
                chips.Add(seg);
        }

        if (chips.Count == 0)
            return [];

        var merged = new List<double>();
        for (var i = 0; i < chips.Count;)
        {
            if (i + 1 < chips.Count
                && TryMergePoE2SpaceGroupedStock(chips[i], chips[i + 1], out var combined))
            {
                merged.Add(combined);
                i += 2;
            }
            else if (double.TryParse(chips[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var one))
            {
                merged.Add(one);
                i++;
            }
            else
            {
                i++;
            }
        }

        return merged;
    }

    /// <summary>«A BBB» как одно целое (BBB — ровно 3 цифры), без склейки «7 421».</summary>
    private static bool TryMergePoE2SpaceGroupedStock(string left, string right, out double value)
    {
        value = 0;
        if (right.Length != 3 || !right.All(char.IsDigit))
            return false;
        if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            || !int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            return false;

        var allow = left.Length == 2
            || (left.Length == 1 && left != "7")
            || (left.Length == 3 && a >= 900);

        if (!allow)
            return false;

        var v = (long)a * 1000L + b;
        if (v is < 0 or > int.MaxValue)
            return false;
        value = v;
        return true;
    }

    private static int? StockDoubleToInt(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
            return null;
        if (d is < int.MinValue or > int.MaxValue)
            return null;
        return (int)Math.Round(d, MidpointRounding.AwayFromZero);
    }

    private static bool ContainsAvailableHeader(string L) =>
        L.Contains("AVAILABLE", StringComparison.OrdinalIgnoreCase)
        && L.Contains("TRADE", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCompetingHeader(string L) =>
        L.Contains("COMPETING", StringComparison.OrdinalIgnoreCase)
        && L.Contains("TRADE", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeaderOrNoise(string L)
    {
        if (L.Equals("Ratio", StringComparison.OrdinalIgnoreCase)
            || L.Equals("Ration", StringComparison.OrdinalIgnoreCase)
            || L.Equals("Stock", StringComparison.OrdinalIgnoreCase)
            || L.Equals("St0ck", StringComparison.OrdinalIgnoreCase))
            return true;
        if (L.StartsWith("MARKET", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>Одна строка таблицы из OCR всей полосы (Ratio+Stock вместе).</summary>
    public static bool TryParseLooseRowLine(string? s, out OrderBookOfferRow row)
    {
        row = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var L = Regex.Replace(s.Trim(), @"\s+", " ");
        L = OcrDotBetweenDigitsRegex.Replace(L, "$1 : $2");
        return TryParseOfferLine(L, out row);
    }

    /// <summary>OCR по отдельным ячейкам Ratio и Stock (сетка по заголовкам).</summary>
    public static bool TryParseDataCells(string? ratioCellText, string? stockCellText, out OrderBookOfferRow row)
    {
        row = default;
        var ratioText = PreprocessOcrCell(ratioCellText, isRatioCell: true);
        var stockText = PreprocessOcrCell(stockCellText, isRatioCell: false);

        if (!TryParseRatioCell(ratioText, out var left, out var right, out var ineq))
            return false;

        var stock = TryParseStockCell(stockText);
        row = new OrderBookOfferRow(left, right, stock, $"grid-cells: {ratioText} | {stockText}", ineq);
        return true;
    }

    private static string PreprocessOcrCell(string? s, bool isRatioCell)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        var t = Regex.Replace(s.Trim(), @"\s+", " ");
        t = OcrDotBetweenDigitsRegex.Replace(t, "$1 : $2");
        if (isRatioCell)
            t = OcrMissingRatioColonRegex.Replace(t, "$1 : $2");
        return t;
    }

    private static bool TryParseRatioCell(string ratioText, out double left, out double right, out char? ineq)
    {
        left = right = 0;
        ineq = null;
        if (string.IsNullOrWhiteSpace(ratioText))
            return false;

        var m = RatioRegex.Match(ratioText);
        if (!m.Success)
            return false;
        if (!double.TryParse(m.Groups["left"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out left))
            return false;
        if (!double.TryParse(m.Groups["right"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out right))
            return false;

        var pref = m.Groups["prefix"].Value;
        if (pref.Length > 0 && (pref[0] is '<' or '>'))
            ineq = pref[0];
        return true;
    }

    private static int? TryParseStockCell(string stockText)
    {
        if (string.IsNullOrWhiteSpace(stockText))
            return null;
        var digits = Regex.Replace(stockText, @"[^\d\s]", " ").Trim();
        digits = Regex.Replace(digits, @"\s+", " ");
        digits = digits.Replace(" ", "", StringComparison.Ordinal).Replace("\u00A0", "", StringComparison.Ordinal);
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : null;
    }

    private static bool TryParseOfferLine(string L, out OrderBookOfferRow row)
    {
        row = default;
        var m = RatioRegex.Match(L);
        if (m.Success)
        {
            if (!double.TryParse(m.Groups["left"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var left))
                return false;
            if (!double.TryParse(m.Groups["right"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
                return false;

            int? stock = null;
            var sm = StockTailRegex.Match(L);
            if (sm.Success)
            {
                var digits = sm.Groups[1].Value.Replace(" ", "", StringComparison.Ordinal).Replace("\u00A0", "", StringComparison.Ordinal);
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var st))
                    stock = st;
            }

            var pref = m.Groups["prefix"].Value;
            char? ineq = pref.Length > 0 && (pref[0] is '<' or '>') ? pref[0] : null;
            row = new OrderBookOfferRow(left, right, stock, L, ineq);
            return true;
        }

        var bm = RatioBulletRegex.Match(L);
        if (bm.Success && TryParseRatioToken(bm.Value, out var bl, out var br))
        {
            int? stock = null;
            var sm = StockTailRegex.Match(L);
            if (sm.Success)
            {
                var digits = sm.Groups[1].Value.Replace(" ", "", StringComparison.Ordinal).Replace("\u00A0", "", StringComparison.Ordinal);
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var st))
                    stock = st;
            }

            row = new OrderBookOfferRow(bl, br, stock, L, null);
            return true;
        }

        return false;
    }
}
