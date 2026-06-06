using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public class OrderBookOcrParserTests
{
    private static void AssertMatchesUserExpectedTable(OrderBookOcrParseResult r)
    {
        Assert.Equal(6, r.AvailableOffers.Count);
        Assert.Equal(6, r.CompetingOffers.Count);

        AssertRow(r.AvailableOffers[0], 1, 204, 7, null);
        AssertRow(r.AvailableOffers[1], 1, 220, 421, null);
        AssertRow(r.AvailableOffers[2], 1, 221, 116, null);
        AssertRow(r.AvailableOffers[3], 1, 222, 40, null);
        AssertRow(r.AvailableOffers[4], 1, 222.50, 1000, null);
        AssertRow(r.AvailableOffers[5], 1, 222.50, 16872, '<');

        AssertRow(r.CompetingOffers[0], 1, 191.30, 57036, null);
        AssertRow(r.CompetingOffers[1], 1, 190, 33820, null);
        AssertRow(r.CompetingOffers[2], 1, 187, 6919, null);
        AssertRow(r.CompetingOffers[3], 1, 186.50, 74974, null);
        AssertRow(r.CompetingOffers[4], 1, 186, 83328, null);
        AssertRow(r.CompetingOffers[5], 1, 186, 910950, '>');
    }

    private static void AssertRow(OrderBookOfferRow row, double left, double right, int stock, char? ineq)
    {
        Assert.True(Math.Abs(left - row.RatioLeft) < 1e-9, $"left want {left} got {row.RatioLeft}");
        Assert.True(Math.Abs(right - row.RatioRight) < 1e-6, $"right want {right} got {row.RatioRight}");
        Assert.Equal(stock, row.Stock);
        Assert.Equal(ineq, row.RatioInequalityPrefix);
    }

    private const string IdealCollapsedLine =
        "AVAILABLE TRADES Ratio 1 : 204 1 : 220 1 : 221 1 : 222 1 : 222.50 < 1 : 222.50 Stock 7 421 116 40 1 000 16 872 COMPETING TRADES Ratio 1 : 191.30 1 : 190 1 : 187 1 : 186.50 1 : 186 > 1 : 186 Stock 57 036 33 820 6 919 74 974 83 328 910 950";

    [Fact]
    public void Available_section_regex_matches_ideal_string()
    {
        const string pattern =
            @"(?is)AVAILABLE\s+TRADES\s+(?:Ration|Ratio)\s+(?<ratios>.+?)\s+Stock\s+(?<stocks>.+?)(?=\s+COMPET(?:ING|ITING)\s+TRADES\b)";
        var s = IdealCollapsedLine;
        var m = System.Text.RegularExpressions.Regex.Match(s, pattern);
        Assert.True(m.Success, "Available regex should match ideal OCR line.");
        Assert.Contains("222.50", m.Groups["ratios"].Value, StringComparison.Ordinal);
        Assert.StartsWith("7", m.Groups["stocks"].Value.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ideal_collapsed_line_user_table()
    {
        var r = OrderBookOcrParser.TryParse(IdealCollapsedLine);
        AssertMatchesUserExpectedTable(r);
    }

    [Fact]
    public void TryParseFlatBlob_direct_ideal_has_six_and_six()
    {
        var r = OrderBookOcrParser.TryParseFlatBlobForTests(IdealCollapsedLine);
        Assert.Equal(6, r.AvailableOffers.Count);
        Assert.Equal(6, r.CompetingOffers.Count);
    }

    [Fact]
    public void Parse_ration_and_competiting_headers_user_table()
    {
        var s =
            "AVAILABLE TRADES Ration 1 : 204 1 : 220 1 : 221 1 : 222 1 : 222.50 < 1 : 222.50 Stock 7 421 116 40 1 000 16 872 COMPETITING TRADES Ration 1 : 191.30 1 : 190 1 : 187 1 : 186.50 1 : 186 > 1 : 186 Stock 57 036 33 820 6 919 74 974 83 328 910 950";
        var r = OrderBookOcrParser.TryParse(s);
        AssertMatchesUserExpectedTable(r);
    }

    [Fact]
    public async Task Grid_parse_chunk_png_beats_collapsed_text_and_fills_rows()
    {
        var path = FindChunkPngPath();
        Assert.True(File.Exists(path), $"Not found: {path}");

        var ocr = await WindowsOcrTextLocator.RecognizeImageFileCollapsedAsync(path);
        Assert.False(string.IsNullOrWhiteSpace(ocr));

        var textParsed = OrderBookOcrParser.TryParse(ocr);
        var gridParsed = await OrderBookGridOcrRecognizer.TryParseFromImageFileAsync(path);

        var gridScore = gridParsed.AvailableOffers.Count + gridParsed.CompetingOffers.Count;
        var textScore = textParsed.AvailableOffers.Count + textParsed.CompetingOffers.Count;
        Assert.True(
            gridScore >= textScore,
            $"Сетка должна распознать не меньше строк, чем разбор одной строки OCR: grid={gridScore}, text={textScore}.");

        Assert.True(gridParsed.AvailableOffers.Count >= 5, $"available rows: {gridParsed.AvailableOffers.Count}");
        Assert.True(gridParsed.CompetingOffers.Count >= 5, $"competing rows: {gridParsed.CompetingOffers.Count}");

        // Знак < / > на реальном OCR может не распознаться стабильно;
        // главное — что сетка даёт фиксированные слоты и не хуже flat-текста по числу строк.
    }

    private static string FindChunkPngPath()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "img", "Chunk.png");
        if (File.Exists(direct))
            return direct;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "img", "Chunk.png");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("img/Chunk.png not found (copy via GameHelper.Tests.csproj).");
    }
}
