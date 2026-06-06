using System.Text;

namespace GameHelper.Services;

/// <summary>
/// Разбор стакана только по вручную заданным ячейкам (6×2).
/// Ячейки задаются в settings.json абсолютными экранными координатами.
/// </summary>
public static class OrderBookManualCellsRecognizer
{
    private const int Rows = 6;
    private const int CellsPerSection = 12; // 6 rows × 2 cols

    public readonly record struct RecognizeResult(
        string ScenarioName,
        string CollapsedRaw,
        OrderBookOcrParseResult Parsed,
        int ParsedRowsScore);

    public static async Task<RecognizeResult?> TryRecognizeFromSettingsAsync(
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var s = SettingsStore.Load();
        var scenarios = new List<ScenarioLayout>();

        var bothAvail = NormalizeCells(s.MarketRatioOrderBookBothAvailableCells);
        var bothComp = NormalizeCells(s.MarketRatioOrderBookBothCompetingCells);
        if (bothAvail != null && bothComp != null)
        {
            scenarios.Add(new ScenarioLayout(
                "both",
                bothAvail,
                bothComp));
        }

        var availOnly = NormalizeCells(s.MarketRatioOrderBookAvailableOnlyCells);
        if (availOnly != null)
            scenarios.Add(new ScenarioLayout("available_only", availOnly, null));

        var compOnly = NormalizeCells(s.MarketRatioOrderBookCompetingOnlyCells);
        if (compOnly != null)
            scenarios.Add(new ScenarioLayout("competing_only", null, compOnly));

        if (scenarios.Count == 0)
        {
            log?.Report("[Стакан] Ручные ячейки 6×2 не заданы в settings.json (marketRatioOrderBook*Cells).");
            return null;
        }

        RecognizeResult? best = null;
        foreach (var sc in scenarios)
        {
            var r = await RunScenarioAsync(sc, cancellationToken).ConfigureAwait(false);
            if (best == null || r.ParsedRowsScore > best.Value.ParsedRowsScore)
                best = r;
        }

        return best;
    }

    private static async Task<RecognizeResult> RunScenarioAsync(ScenarioLayout sc, CancellationToken cancellationToken)
    {
        var available = new List<OrderBookOfferRow>();
        var competing = new List<OrderBookOfferRow>();
        var raw = new StringBuilder();

        if (sc.AvailableCells is { } ac)
            await FillSectionAsync(ac, available, raw, "A", cancellationToken).ConfigureAwait(false);
        if (sc.CompetingCells is { } cc)
            await FillSectionAsync(cc, competing, raw, "C", cancellationToken).ConfigureAwait(false);

        var score = CountValidRows(available) + CountValidRows(competing);
        return new RecognizeResult(
            sc.Name,
            raw.ToString().Trim(),
            new OrderBookOcrParseResult { AvailableOffers = available, CompetingOffers = competing },
            score);
    }

    private static async Task FillSectionAsync(
        IReadOnlyList<ScreenRect> cells,
        List<OrderBookOfferRow> target,
        StringBuilder raw,
        string tag,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < Rows; i++)
        {
            var ratioRect = cells[i * 2];
            var stockRect = cells[i * 2 + 1];

            var ratioText = await RecognizeCellAsync(ratioRect, cancellationToken).ConfigureAwait(false);
            var stockText = await RecognizeCellAsync(stockRect, cancellationToken).ConfigureAwait(false);
            raw.Append(tag).Append(i + 1).Append(": ").Append(ratioText).Append(" | ").Append(stockText).Append(' ');

            if (OrderBookOcrParser.TryParseDataCells(ratioText, stockText, out var row))
                target.Add(row);
            else
                target.Add(new OrderBookOfferRow(double.NaN, double.NaN, null, $"manual-empty[{tag}{i + 1}]"));
        }
    }

    private static async Task<string> RecognizeCellAsync(ScreenRect rect, CancellationToken cancellationToken)
    {
        using var bmp = ScreenCaptureHelper.CaptureRegion(rect);
        return await WindowsOcrTextLocator.RecognizeBitmapCollapsedAsync(bmp, cancellationToken).ConfigureAwait(false);
    }

    private static int CountValidRows(IReadOnlyList<OrderBookOfferRow> rows)
    {
        var n = 0;
        foreach (var r in rows)
        {
            if (r.Stock is > 0 && !double.IsNaN(r.RatioRight) && !double.IsInfinity(r.RatioRight))
                n++;
        }

        return n;
    }

    private static List<ScreenRect>? NormalizeCells(List<ScreenRect>? cells)
    {
        if (cells is not { Count: CellsPerSection })
            return null;
        if (cells.Any(c => c.Width <= 0 || c.Height <= 0))
            return null;
        return cells.ToList();
    }

    private readonly record struct ScenarioLayout(
        string Name,
        IReadOnlyList<ScreenRect>? AvailableCells,
        IReadOnlyList<ScreenRect>? CompetingCells);
}

