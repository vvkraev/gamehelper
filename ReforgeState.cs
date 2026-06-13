namespace GameHelper;

/// <summary>Мутабельный контейнер настроек перековки, которым совместно владеют MainWindow и ReforgeWindow.</summary>
public sealed class ReforgeState
{
    public ScreenRect Slot1Rect   { get; set; }
    public ScreenRect Slot2Rect   { get; set; }
    public ScreenRect Slot3Rect   { get; set; }
    public ScreenRect ConfirmRect { get; set; }
    public ScreenRect ResultRect  { get; set; }
    public int PostAnimationDelayMs { get; set; } = 800;
    public int MaxOps               { get; set; } = 0; // 0 = без ограничений

    /// <summary>Сетка инвентаря — используется и для сканирования реестра, и для перековки.</summary>
    public List<ScreenRect> ItemCells { get; set; } = new();

    /// <summary>Id выбранных катализаторов (из StackableItemRegistry).</summary>
    public List<string> SelectedCatalystIds { get; set; } = new();

    public static ReforgeState FromSettings(AppSettings s) => new()
    {
        Slot1Rect             = s.ReforgeSlot1Rect,
        Slot2Rect             = s.ReforgeSlot2Rect,
        Slot3Rect             = s.ReforgeSlot3Rect,
        ConfirmRect           = s.ReforgeConfirmRect,
        ResultRect            = s.ReforgeResultRect,
        PostAnimationDelayMs  = s.ReforgePostAnimationDelayMs,
        MaxOps                = s.ReforgeMaxOps,
        SelectedCatalystIds   = s.ReforgeSelectedCatalystIds?.ToList() ?? new(),
        ItemCells             = s.ReforgeItemCells?.ToList() ?? new(),
    };

    public void ApplyToSettings(AppSettings s)
    {
        s.ReforgeSlot1Rect             = Slot1Rect;
        s.ReforgeSlot2Rect             = Slot2Rect;
        s.ReforgeSlot3Rect             = Slot3Rect;
        s.ReforgeConfirmRect           = ConfirmRect;
        s.ReforgeResultRect            = ResultRect;
        s.ReforgePostAnimationDelayMs  = PostAnimationDelayMs;
        s.ReforgeMaxOps                = MaxOps;
        s.ReforgeSelectedCatalystIds   = SelectedCatalystIds.Count > 0 ? SelectedCatalystIds.ToList() : null;
        s.ReforgeItemCells             = ItemCells.Count > 0 ? ItemCells.ToList() : null;
    }
}
