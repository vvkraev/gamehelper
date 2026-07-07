using System;
using System.Collections.Generic;

namespace GameHelper.Services;

public record PricePoint(DateTime Time, double Price, string Currency);

public class SoldLogEntry
{
    public Guid SessionId { get; set; }
    public string SessionName { get; set; } = "";
    public DateTime SoldAtScan { get; set; }   // время снимка, в котором предмет пропал
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public double PriceDivine { get; set; }
    public string PriceCurrency { get; set; } = "";
    public int Ilvl { get; set; }
    public string ListedAt { get; set; } = "";
    public List<string> ModsFractured { get; set; } = new();
    public List<string> ModsDesecrated { get; set; } = new();
    public List<string> ModsExplicit { get; set; } = new();
    public List<string> ModsCrafted { get; set; } = new();
}

public class TrackingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string BaseType { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<SnapshotRef> Snapshots { get; set; } = new();
}

public class SnapshotRef
{
    public string FilePath { get; set; } = "";   // относительный путь от корня проекта
    public DateTime ImportedAt { get; set; }
    public int ItemCount { get; set; }

    public string DisplayLabel =>
        $"{ImportedAt:dd.MM HH:mm} · {ItemCount} пред.  ({System.IO.Path.GetFileName(FilePath)})";
}

public enum ItemStatus
{
    Stable,    // присутствует во всех снимках
    Gone,      // был, потом пропал
    New,       // появился, не было раньше
    Returned,  // был → пропал → вернулся
    Lingering, // присутствует N+ сканов подряд без продажи (N >= 2)
}

public class TrackingItemDetail
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseType { get; set; } = "";
    public string ListedAt { get; set; } = "";
    public string SellerAccount { get; set; } = "";
    public double PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = "";
    public int Quality { get; set; }
    public int Ilvl { get; set; }
    public bool Corrupted { get; set; }
    public bool Sanctified { get; set; }
    public int Sockets { get; set; }
    public List<string> ModsFractured { get; set; } = new();
    public List<string> ModsDesecrated { get; set; } = new();
    public List<string> ModsImplicit { get; set; } = new();
    public List<string> ModsExplicit { get; set; } = new();
    public List<string> ModsCrafted { get; set; } = new();
}

public class TrackingItemRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public string Currency { get; set; } = "";
    public ItemStatus Status { get; set; }
    public string StatusLabel { get; set; } = "";

    /// <summary>presence[i] = true если предмет есть в Snapshots[i]</summary>
    public bool[] Presence { get; set; } = Array.Empty<bool>();

    /// <summary>Детали из последнего снимка, где предмет присутствует.</summary>
    public TrackingItemDetail? Detail { get; set; }

    /// <summary>Все точки цены по снимкам (только там, где предмет присутствовал).</summary>
    public List<PricePoint> PriceHistory { get; set; } = new();

    // Вычисляемые свойства для привязки XAML
    public string PriceLabel => Currency == "divine" ? $"{Price}d"
                              : Currency == "mirror"  ? $"{Price}m"
                              : $"{Price} {Currency}";

    public string PresenceStr => string.Join("", System.Linq.Enumerable.Select(Presence, p => p ? "✓" : "—"));

    public string PriceDeltaLabel
    {
        get
        {
            if (PriceHistory.Count < 2) return "";
            var delta = PriceHistory[^1].Price - PriceHistory[^2].Price;
            if (Math.Abs(delta) < 0.001) return "";
            return delta > 0 ? $"+{delta:0.#}d" : $"−{Math.Abs(delta):0.#}d";
        }
    }

    public string PriceDeltaDir
    {
        get
        {
            if (PriceHistory.Count < 2) return "None";
            var delta = PriceHistory[^1].Price - PriceHistory[^2].Price;
            if (Math.Abs(delta) < 0.001) return "None";
            return delta > 0 ? "Up" : "Down";
        }
    }

    public string StatusColor => Status switch
    {
        ItemStatus.Gone     => "Gone",
        ItemStatus.New      => "New",
        ItemStatus.Returned => "Returned",
        ItemStatus.Lingering=> "Lingering",
        _                   => "Stable",
    };
}
