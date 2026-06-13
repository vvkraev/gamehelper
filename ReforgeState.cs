namespace GameHelper;

/// <summary>Мутабельный контейнер настроек перековки, которым совместно владеют MainWindow и ReforgeWindow.</summary>
public sealed class ReforgeState
{
    public ScreenRect CatalystInventoryRect { get; set; }
    public ScreenRect Slot1Rect            { get; set; }
    public ScreenRect Slot2Rect            { get; set; }
    public ScreenRect Slot3Rect            { get; set; }
    public ScreenRect ConfirmRect          { get; set; }
    public ScreenRect ResultRect           { get; set; }
    public int PostAnimationDelayMs        { get; set; } = 800;

    /// <summary>Ячейки сетки предмета (из ItemRect/ItemCells) — для сканирования реестра.</summary>
    public List<ScreenRect> ItemCells      { get; set; } = new();

    public static ReforgeState FromSettings(AppSettings s) => new()
    {
        CatalystInventoryRect = s.ReforgeCatalystInventoryRect,
        Slot1Rect             = s.ReforgeSlot1Rect,
        Slot2Rect             = s.ReforgeSlot2Rect,
        Slot3Rect             = s.ReforgeSlot3Rect,
        ConfirmRect           = s.ReforgeConfirmRect,
        ResultRect            = s.ReforgeResultRect,
        PostAnimationDelayMs  = s.ReforgePostAnimationDelayMs,
    };

    public void ApplyToSettings(AppSettings s)
    {
        s.ReforgeCatalystInventoryRect = CatalystInventoryRect;
        s.ReforgeSlot1Rect             = Slot1Rect;
        s.ReforgeSlot2Rect             = Slot2Rect;
        s.ReforgeSlot3Rect             = Slot3Rect;
        s.ReforgeConfirmRect           = ConfirmRect;
        s.ReforgeResultRect            = ResultRect;
        s.ReforgePostAnimationDelayMs  = PostAnimationDelayMs;
    }
}
