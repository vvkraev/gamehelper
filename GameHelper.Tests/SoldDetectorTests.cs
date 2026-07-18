using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class SoldDetectorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateTime T(string iso) => DateTime.Parse(iso).ToLocalTime();

    private static TabletListingEntry Listing(
        string id, string baseType, string[] mods,
        string timestamp, bool repricedOnce = false)
    {
        var e = new TabletListingEntry
        {
            Id        = id,
            Timestamp = timestamp,
            BaseType  = baseType,
            Mods      = mods.ToList(),
        };
        if (repricedOnce)
            e.Repricings.Add(new RepricingEvent { Timestamp = timestamp, NewPrice = 100 });
        return e;
    }

    private static SaleRecord Sale(
        string itemId, string baseType, string[] explicitMods,
        DateTime time, decimal price = 100m)
        => new()
        {
            ItemId       = itemId,
            BaseType     = baseType,
            ExplicitMods = explicitMods.ToList(),
            Time         = time.ToUniversalTime(),
            PriceAmount  = price,
            PriceCurrency = "divine",
        };

    private static List<(TabletListingEntry Entry, SoldInfo Info)> Match(
        List<SaleRecord> sales, List<TabletListingEntry> entries)
        => SoldDetector.FindMatches(sales, entries);

    // ── Нет таблеток среди продаж ─────────────────────────────────────────────

    [Fact]
    public void NoTabletSales_ReturnsEmpty()
    {
        var entries = new List<TabletListingEntry>
        {
            Listing("e1", "Ritual Tablet", ["mod a", "mod b", "mod c"], "2026-07-01T10:00:00"),
        };
        var sales = new List<SaleRecord>
        {
            Sale("s1", "Rare Ring", ["mod a", "mod b", "mod c"], T("2026-07-01T12:00:00")),
        };

        Assert.Empty(Match(sales, entries));
    }

    // ── Нет листингов ─────────────────────────────────────────────────────────

    [Fact]
    public void NoListings_ReturnsEmpty()
    {
        var sales = new List<SaleRecord>
        {
            Sale("s1", "Ritual Tablet", ["mod a", "mod b", "mod c"], T("2026-07-01T12:00:00")),
        };

        Assert.Empty(Match(sales, new List<TabletListingEntry>()));
    }

    // ── Нормальный матч ───────────────────────────────────────────────────────

    [Fact]
    public void ExactMatch_MarksEntryAndReturnsSoldInfo()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00");
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"), price: 200m);

        var matches = Match([sale], [entry]);

        Assert.Single(matches);
        var (matched, info) = matches[0];
        Assert.Equal("e1", matched.Id);
        Assert.True(matched.Sold);
        Assert.Equal("s1", info.SaleId);
        Assert.Equal(200m, info.SalePriceAmount);
        Assert.Equal("divine", info.SalePriceCurrency);
        Assert.Equal(120, info.MinutesToSale);
    }

    // ── Пересечение < 2 → нет матча ──────────────────────────────────────────

    [Fact]
    public void ModOverlapBelowTwo_NoMatch()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00");
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "unique boss"],          // только 1 мод совпадает
            T("2026-07-01T12:00:00"));

        Assert.Empty(Match([sale], [entry]));
    }

    // ── Листинг ПОСЛЕ продажи → нет матча ────────────────────────────────────

    [Fact]
    public void ListingAfterSale_NoMatch()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T14:00:00");  // листинг после продажи
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        Assert.Empty(Match([sale], [entry]));
    }

    // ── Miss-флаг: продажа ≤ 10 мин ──────────────────────────────────────────

    [Fact]
    public void SaleWithin10Min_SetssMissTrue()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T12:00:00");
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:08:00"));  // 8 минут после листинга

        var matches = Match([sale], [entry]);

        Assert.Single(matches);
        Assert.True(matches[0].Info.Miss);
        Assert.Equal(8, matches[0].Info.MinutesToSale);
    }

    [Fact]
    public void SaleAfter11Min_MissFalse()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T12:00:00");
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:11:00"));  // 11 минут

        var matches = Match([sale], [entry]);

        Assert.Single(matches);
        Assert.False(matches[0].Info.Miss);
    }

    // ── SoldAfterReprice-флаг ─────────────────────────────────────────────────

    [Fact]
    public void EntryHasReprice_SoldAfterRepriceTrue()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00", repricedOnce: true);
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        var matches = Match([sale], [entry]);

        Assert.Single(matches);
        Assert.True(matches[0].Info.SoldAfterReprice);
    }

    // ── Два листинга — выбирается с большим пересечением ─────────────────────

    [Fact]
    public void BestMatch_PreferMoreModOverlap()
    {
        var weak   = Listing("e_weak", "Ritual Tablet",
            ["increased effect", "more monsters", "unrelated"],
            "2026-07-01T09:00:00");
        var strong = Listing("e_strong", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T09:30:00");

        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        var matches = Match([sale], [weak, strong]);

        Assert.Single(matches);
        Assert.Equal("e_strong", matches[0].Entry.Id);
    }

    // ── При равном пересечении — самый старый листинг ─────────────────────────

    [Fact]
    public void BestMatch_TieBreakByOldestTimestamp()
    {
        var older = Listing("e_older", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T08:00:00");
        var newer = Listing("e_newer", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T09:00:00");

        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        var matches = Match([sale], [newer, older]);

        Assert.Single(matches);
        Assert.Equal("e_older", matches[0].Entry.Id);
    }

    // ── Две продажи не матчат одну запись дважды ──────────────────────────────

    [Fact]
    public void TwoSales_SameEntry_MatchedOnce()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00");

        var s1 = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T11:00:00"));
        var s2 = Sale("s2", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        var matches = Match([s1, s2], [entry]);

        Assert.Single(matches);
    }

    // ── Две продажи разных типов — каждая матчит свой листинг ────────────────

    [Fact]
    public void TwoSales_TwoEntries_BothMatched()
    {
        var ritual  = Listing("e_rit", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00");
        var breach  = Listing("e_brc", "Breach Tablet",
            ["breach domain", "splinter chance", "pack size"],
            "2026-07-01T10:00:00");

        var sRitual = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));
        var sBreach = Sale("s2", "Breach Tablet",
            ["breach domain", "splinter chance", "pack size"],
            T("2026-07-01T12:00:00"));

        var matches = Match([sRitual, sBreach], [ritual, breach]);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Entry.Id == "e_rit");
        Assert.Contains(matches, m => m.Entry.Id == "e_brc");
    }

    // ── BaseType: case-insensitive ────────────────────────────────────────────

    [Fact]
    public void BaseType_CaseInsensitive_Matches()
    {
        var entry = Listing("e1", "ritual tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00");
        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        var matches = Match([sale], [entry]);

        Assert.Single(matches);
    }

    // ── Уже закрытый листинг не матчится ─────────────────────────────────────

    [Fact]
    public void AlreadySoldEntry_Skipped()
    {
        var entry = Listing("e1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            "2026-07-01T10:00:00");
        entry.Sold = true;

        var sale = Sale("s1", "Ritual Tablet",
            ["increased effect", "more monsters", "pack size"],
            T("2026-07-01T12:00:00"));

        Assert.Empty(Match([sale], [entry]));
    }
}
