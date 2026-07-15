namespace TradeBot.Config;

public sealed class TradeBotSettings
{
    public ScreenRect? StashRegion { get; set; }
    public int StashCols { get; set; } = 12;
    public int StashRows { get; set; } = 12;
    public ScreenRect? MerchantRegion { get; set; }
    public int MaxItems { get; set; } = 3;
    public int DelayBetweenVisitsMs { get; set; } = 500;
    public int HideoutTimeoutMs { get; set; } = 30_000;
    public int ChromeDebugPort { get; set; } = 9222;
    public bool AutoBuy { get; set; } = false;
    public string GameProcessName { get; set; } = "PathOfExileSteam";
    public string OwnAccountName { get; set; } = "";

    // ── Инвентарь ────────────────────────────────────────────────────────────
    public ScreenRect? InventoryRegion { get; set; }      // 12×5 сетка инвентаря игрока
    public bool InventoryCheckEnabled { get; set; } = false;
    public int InventoryMinFreeSlots { get; set; } = 10;  // пауза если свободных ячеек меньше
    public bool AutoDumpToStash { get; set; } = false;    // сбросить в стэш когда полон
    public ScreenRect? PersonalStashRegion { get; set; }  // куда кликнуть чтобы открыть личный стэш
    public bool ScanInventoryOnStartup { get; set; } = false;
    public bool CloseOnInventoryFull { get; set; } = false; // убить игру и TradeBot при достижении лимита
}
