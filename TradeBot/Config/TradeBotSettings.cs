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

    // ── Детекция зависания игры ───────────────────────────────────────────────
    public ScreenRect? FreezeDetectRegion { get; set; }
    public int FreezeDetectWaitMs { get; set; } = 4000;
    public string GameExePath { get; set; } = "";

    // ── Верификация локации после /hideout ────────────────────────────────────
    public ScreenRect? LocationRegion { get; set; }
    public string ExpectedHideoutText { get; set; } = "Hideout";
    public int LocationVerifyDelayMs { get; set; } = 3000;

    // ── Автовход после перезапуска игры ──────────────────────────────────────
    public GameHelper.Services.GameLoginSettings LoginSettings { get; set; } = new();

    // ── Витрина: пауза покупок при заполнении ─────────────────────────────────
    /// <summary>Порог заполненности витрины [0..1] для паузы покупок. 0 = отключено.</summary>
    public double PauseFillRateThreshold { get; set; } = 0.75;
}
