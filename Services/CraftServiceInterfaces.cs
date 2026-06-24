namespace GameHelper.Services;

/// <summary>Общие настройки для всех сервисов орб-крафта.</summary>
public interface ICraftServiceSettings
{
    int MouseActionDelayMs { get; set; }
    int ClipboardDelayMs { get; set; }
    int HoverSettleBeforeClipboardMs { get; set; }
    bool TraceInputToLog { get; set; }
    Func<string, Task>? StepConfirmAsync { get; set; }
    Task ClearClipboardAsync();
}

/// <summary>
/// Сервис крафта, поддерживающий предварительную проверку предмета перед запуском цикла.
/// PrecheckAsync имеет одинаковую сигнатуру у всех трёх основных сервисов крафта.
/// </summary>
public interface IPrecheckableCraftService : ICraftServiceSettings
{
    Task<CraftPrecheckResult> PrecheckAsync(
        ScreenRect itemArea,
        CraftConditionPlan plan,
        IProgress<string>? log,
        CancellationToken ct);
}

/// <summary>Сервис Chaos Orb крафта.</summary>
public interface IChaosCraftService : IPrecheckableCraftService
{
    Task<string> ReadItemClipboardTextAsync(
        ScreenRect itemArea,
        IProgress<string>? log,
        CancellationToken cancellationToken);

    Task<CraftResult> RunAsync(
        ScreenRect orbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        int segmentMaxOperations,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken cancellationToken,
        CraftRunFileLog? craftLog = null);
}

/// <summary>Сервис Aug+Annul крафта.</summary>
public interface IAugAnnulCraftService : IPrecheckableCraftService
{
    Task<CraftResult> RunAsync(
        ScreenRect augOrbArea,
        ScreenRect annulOrbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        ParsedItem initialParsedItem,
        string? initialClipboardText,
        int segmentMaxOperations,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog = null);
}

/// <summary>Сервис Exaltation крафта (Fractured side, с омнами).</summary>
public interface IExaltationCraftService : IPrecheckableCraftService
{
    bool SchemaTraceToLog { get; set; }

    Task<CraftResult> RunAsync(
        ScreenRect exaltArea,
        ScreenRect annulArea,
        ScreenRect ritualInventoryRegion,
        ScreenRect currencyInventoryRegion,
        ScreenRect omenSinistralStashRegion,
        ScreenRect omenDextralStashRegion,
        ScreenRect omenGreaterStashRegion,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        ParsedItem initialParsedItem,
        string? initialClipboardText,
        int remainingAttempts,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog);
}

/// <summary>
/// Сервис Fracturing Orb крафта: применяет орб к одному предмету и проверяет,
/// нужный ли аффикс зафиксировался (IsFractured = true).
/// </summary>
public interface IFracturingOrbService : IPrecheckableCraftService
{
    Task<CraftResult> RunAsync(
        ScreenRect orbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog = null);
}

/// <summary>Сервис заточки предметов (не использует план крафта).</summary>
public interface ISharpenService
{
    int MouseActionDelayMs { get; set; }
    bool TraceInputToLog { get; set; }

    Task RunAsync(
        ScreenRect sharpenArea,
        IReadOnlyList<ScreenRect> itemCells,
        int clicksPerCell,
        IProgress<string>? log,
        CancellationToken ct);
}
