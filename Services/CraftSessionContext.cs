namespace GameHelper.Services;

/// <summary>
/// Всё, что нужно <see cref="CraftOrchestrator"/> для одного запуска крафт-сессии.
/// Строится в MainWindow из UI-состояния и передаётся в <see cref="CraftOrchestrator.RunAsync"/>.
/// </summary>
public sealed record CraftSessionContext
{
    public required CraftMode Mode { get; init; }
    public required List<ScreenRect> ItemCells { get; init; }
    public required CraftConditionPlan Plan { get; init; }
    public required string ConditionSummary { get; init; }
    public required int MaxOps { get; init; }
    public int MouseActionDelayMs { get; init; }
    public int ClipboardDelayMs { get; init; }
    public bool TraceInputToLog { get; init; }
    public bool SchemaTraceToLog { get; init; }
    public Func<string, Task>? StepConfirmAsync { get; init; }

    // Currency rects
    public ScreenRect OrbRect { get; init; }      // Chaos / FracturingOrb
    public ScreenRect ExaltRect { get; init; }    // Exaltation
    public ScreenRect AugRect { get; init; }      // AugAnnul
    public ScreenRect AnnulRect { get; init; }    // AugAnnul + Exaltation

    // Exaltation omen-refill rects
    public ScreenRect RitualInventoryRect { get; init; }
    public ScreenRect CurrencyInventoryRect { get; init; }
    public ScreenRect OmenSinistralRect { get; init; }
    public ScreenRect OmenDextralRect { get; init; }
    public ScreenRect OmenGreaterRect { get; init; }
    public List<ScreenRect> OmenSinistralCells { get; init; } = [];
    public List<ScreenRect> OmenDextralCells { get; init; } = [];
    public List<ScreenRect> OmenGreaterCells { get; init; } = [];

    // Display names for the craft log file
    public string OrbDisplayName { get; init; } = "Chaos Orb";
    public string AugOrbDisplayName { get; init; } = "Perfect Orb of Augmentation";
}
