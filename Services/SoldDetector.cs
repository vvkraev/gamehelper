namespace GameHelper.Services;

/// <summary>
/// Сопоставляет записи из sales_history с незакрытыми листингами в listings_index.
/// Если находит совпадение — помечает листинг как проданный.
/// </summary>
public static class SoldDetector
{
    /// <summary>
    /// Сравнивает свежие продажи с активными листингами.
    /// Возвращает количество вновь закрытых записей.
    /// </summary>
    public static int DetectAndMark(List<SaleRecord> sales)
    {
        var entries = TabletListingsIndex.Load();
        var unsold  = entries.Where(e => !e.Sold).ToList();
        if (unsold.Count == 0) return 0;

        // Только продажи таблеток
        var tabletSales = sales
            .Where(s => s.BaseType.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (tabletSales.Count == 0) return 0;

        int marked = 0;

        foreach (var sale in tabletSales)
        {
            var saleTime = sale.Time.ToLocalTime();

            // Кандидаты: тот же BaseType, листинг раньше продажи, ещё не закрыты
            var candidates = unsold
                .Where(e => !e.Sold)
                .Where(e => string.Equals(e.BaseType, sale.BaseType, StringComparison.OrdinalIgnoreCase))
                .Where(e => TabletListingsIndex.TryParseTimestamp(e.Timestamp) is { } ts && ts < saleTime)
                .ToList();

            if (candidates.Count == 0) continue;

            // Выбираем по максимальному пересечению модов, при равенстве — самый старый листинг
            var saleMods  = NormSet(sale.ExplicitMods.Concat(sale.FracturedMods));
            var best      = candidates
                .OrderByDescending(e => ModOverlap(e.Mods, saleMods))
                .ThenBy(e => e.Timestamp)
                .First();

            // Принимаем матч только если хотя бы 2 мода совпали
            if (ModOverlap(best.Mods, saleMods) < 2) continue;

            var listingTime = TabletListingsIndex.TryParseTimestamp(best.Timestamp);
            var minutesToSale = listingTime.HasValue
                ? (int)(saleTime - listingTime.Value).TotalMinutes
                : (int?)null;

            TabletListingsIndex.MarkSold(best.Id, new SoldInfo(
                SaleId:           sale.ItemId,
                SaleTime:         saleTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                SalePriceAmount:  sale.PriceAmount,
                SalePriceCurrency: sale.PriceCurrency,
                MinutesToSale:    minutesToSale,
                Miss:             minutesToSale.HasValue && minutesToSale.Value <= TabletListingsIndex.MissThresholdMinutes,
                SoldAfterReprice: best.Repricings.Count > 0
            ));

            best.Sold = true; // не матчить повторно в этом проходе
            marked++;
        }

        return marked;
    }

    private static HashSet<string> NormSet(IEnumerable<string> mods) =>
        mods.Select(m => m.Trim().ToLowerInvariant()).Where(m => m.Length > 0).ToHashSet();

    private static int ModOverlap(List<string> listingMods, HashSet<string> saleMods)
    {
        var listingSet = NormSet(listingMods);
        return listingSet.Count(m => saleMods.Contains(m));
    }
}

/// <summary>Данные о продаже для передачи в TabletListingsIndex.MarkSold.</summary>
public sealed record SoldInfo(
    string  SaleId,
    string  SaleTime,
    decimal SalePriceAmount,
    string  SalePriceCurrency,
    int?    MinutesToSale,
    bool    Miss,
    bool    SoldAfterReprice);
