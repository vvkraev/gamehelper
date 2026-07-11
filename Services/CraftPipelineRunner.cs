using System.IO;
using GameHelper.Native;

namespace GameHelper.Services;

public enum PipelineRunStatus
{
    Done,
    Aborted,
    Cancelled,
    Error,
}

public sealed class PipelineRunResult
{
    public PipelineRunStatus Status { get; init; }
    public string Message { get; init; } = "";
    public int TotalAttempts { get; init; }
    public string? FinalItemText { get; init; }
}

/// <summary>
/// Область экрана для каждого источника действий пайплайна.
/// Используется как единый объект конфигурации при вызове <see cref="CraftPipelineRunner.RunAsync"/>.
/// </summary>
public sealed record PipelineScreenConfig
{
    public static readonly PipelineScreenConfig Empty = new();

    public ScreenRect ItemArea { get; init; }

    // Орбы
    public ScreenRect ChaosOrbArea { get; init; }
    public ScreenRect DivineOrbArea { get; init; }
    public ScreenRect AugmentOrbArea { get; init; }
    public ScreenRect AnnulOrbArea { get; init; }
    public ScreenRect ExaltOrbArea { get; init; }

    // Инвентарь (для сервиса Exalt)
    public ScreenRect RitualInventoryRegion { get; init; }
    public ScreenRect CurrencyInventoryRegion { get; init; }

    // Ячейки стэша по типу омена (из AppSettings)
    public IReadOnlyList<ScreenRect> OmenSinistralStashCells { get; init; } = Array.Empty<ScreenRect>();
    public IReadOnlyList<ScreenRect> OmenDextralStashCells { get; init; } = Array.Empty<ScreenRect>();
    public IReadOnlyList<ScreenRect> OmenGreaterStashCells { get; init; } = Array.Empty<ScreenRect>();
    // Произвольные омены: имя → одна ячейка стэша (из _ritualItemRegions или _abyssItemRegions)
    public IReadOnlyDictionary<string, IReadOnlyList<ScreenRect>> OmenStashCellsByName { get; init; } =
        new Dictionary<string, IReadOnlyList<ScreenRect>>(StringComparer.OrdinalIgnoreCase);

    // Вкладка стэша для каждого омена по имени (Ritual или Abyss и т.д.)
    public IReadOnlyDictionary<string, ScreenRect> OmenStashTabByName { get; init; } =
        new Dictionary<string, ScreenRect>(StringComparer.OrdinalIgnoreCase);

    // Полный инвентарь (12×5) для размещения омена
    public IReadOnlyList<ScreenRect> FullInventoryCells { get; init; } = Array.Empty<ScreenRect>();
    public int InventoryGridColumns { get; init; } = 12;

    // Currency-вкладка и ячейки орбов (для SimpleCurrency)
    public IReadOnlyDictionary<string, ScreenRect> CurrencyItemRegions { get; init; } =
        new Dictionary<string, ScreenRect>(StringComparer.OrdinalIgnoreCase);

    // Abyss-вкладка и ячейки предметов (кости, некоторые омены)
    public ScreenRect AbyssInventoryRegion { get; init; }
    public IReadOnlyDictionary<string, ScreenRect> AbyssItemRegions { get; init; } =
        new Dictionary<string, ScreenRect>(StringComparer.OrdinalIgnoreCase);

    // Omen-регионы для ExaltCraft (опционально)
    public IReadOnlyList<ScreenRect> ExaltOmenSinistralCells { get; init; } = Array.Empty<ScreenRect>();
    public IReadOnlyList<ScreenRect> ExaltOmenDextralCells { get; init; } = Array.Empty<ScreenRect>();
    public IReadOnlyList<ScreenRect> ExaltOmenGreaterCells { get; init; } = Array.Empty<ScreenRect>();
    public ScreenRect ExaltOmenSinistralRegion { get; init; }
    public ScreenRect ExaltOmenDextralRegion { get; init; }
    public ScreenRect ExaltOmenGreaterRegion { get; init; }

    // Delirium
    public ScreenRect DeliriumInventoryRect { get; init; }
    public IReadOnlyDictionary<string, ScreenRect> DeliriumItemRegions { get; init; } = new Dictionary<string, ScreenRect>();

    /// <summary>Область экрана с названием текущей локации — для OCR-верификации после TravelToLocation.</summary>
    public ScreenRect LocationNameArea { get; init; }

    // Стэш (для OpenStash)
    public ScreenRect StashOcrSearchRect { get; init; }
    public string StashOcrText { get; init; } = "STASH";
    public ScreenRect StashIsOpenCheckRect { get; init; }
    public string StashIsOpenCheckText { get; init; } = "Stash";
    public int StashOpenDelayMs { get; init; } = 3000;
}

/// <summary>Результат выполнения одного шага пайплайна — внутренний тип для диспетчера.</summary>
internal sealed record StepOutcome(bool Succeeded, int Attempts = 0, string? FinalItemText = null)
{
    internal static StepOutcome Success(int attempts = 0, string? finalItem = null) => new(true, attempts, finalItem);
    internal static StepOutcome Failure(int attempts = 0, string? finalItem = null) => new(false, attempts, finalItem);
}

/// <summary>
/// Интерпретатор пайплайна крафта: последовательно выполняет шаги <see cref="CraftPipeline"/>,
/// применяет переходы (Next, Done, Abort, Step) и накапливает статистику.
/// </summary>
public sealed class CraftPipelineRunner
{
    /// <summary>Защита от бесконечных циклов: максимальное суммарное число выполнений шагов.</summary>
    public const int MaxGlobalStepExecutions = 10_000;

    private readonly IChaosCraftService? _chaos;
    private readonly IAugAnnulCraftService? _augAnnul;
    private readonly IDivineCraftService? _divine;
    private readonly IExaltationCraftService? _exalt;
    private readonly OmenActivationService? _omen;
    private readonly TravelToLocationService _travel = new();

    // Текущая активная вкладка стэша в рамках одного RunAsync; default = неизвестно.
    private ScreenRect _currentStashTab;

    /// <summary>
    /// Тест-хук: если установлен, вызывается вместо <c>ExecuteStepAsync</c>.
    /// Получает текущий шаг и токен отмены, возвращает исход.
    /// Позволяет тестировать логику переходов без реальных сервисов и экрана.
    /// </summary>
    internal Func<CraftPipelineStep, CancellationToken, Task<StepOutcome>>? _testStepExecutor;

    public CraftPipelineRunner(
        IChaosCraftService? chaos = null,
        IAugAnnulCraftService? augAnnul = null,
        IDivineCraftService? divine = null,
        IExaltationCraftService? exalt = null,
        OmenActivationService? omen = null)
    {
        _chaos = chaos;
        _augAnnul = augAnnul;
        _divine = divine;
        _exalt = exalt;
        _omen = omen;
    }

    /// <summary>
    /// Запускает пайплайн <paramref name="pipeline"/>, последовательно выполняя шаги
    /// и следуя переходам OnSuccess/OnFailure каждого шага.
    /// </summary>
    public async Task<PipelineRunResult> RunAsync(
        CraftPipeline pipeline,
        PipelineScreenConfig screen,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (pipeline.Steps.Count == 0)
            return new PipelineRunResult { Status = PipelineRunStatus.Done, Message = "Пайплайн пуст." };

        _currentStashTab = default;

        log?.Report($"[Конфиг] Инвентарь: {screen.FullInventoryCells.Count} ячеек (12 колонок → строки 0–{screen.FullInventoryCells.Count / screen.InventoryGridColumns - 1}, столбцы 0–{screen.InventoryGridColumns - 1})");
        log?.Report($"[Конфиг] Omen Sinistral стэш: {screen.OmenSinistralStashCells.Count} яч. | Dextral: {screen.OmenDextralStashCells.Count} яч. | Greater: {screen.OmenGreaterStashCells.Count} яч.");

        var stepIdx = 0;
        var totalAttempts = 0;
        string? finalItem = null;
        var executionCount = 0;

        try
        {

        while (true)
        {
            if (++executionCount > MaxGlobalStepExecutions)
                return new PipelineRunResult
                {
                    Status = PipelineRunStatus.Error,
                    Message = $"Превышен лимит выполнений шагов ({MaxGlobalStepExecutions}). Возможен бесконечный цикл.",
                    TotalAttempts = totalAttempts,
                    FinalItemText = finalItem,
                };

            if ((uint)stepIdx >= (uint)pipeline.Steps.Count)
                return new PipelineRunResult
                {
                    Status = PipelineRunStatus.Error,
                    Message = $"Шаг {stepIdx} вне диапазона [0, {pipeline.Steps.Count - 1}].",
                    TotalAttempts = totalAttempts,
                    FinalItemText = finalItem,
                };

            var step = pipeline.Steps[stepIdx];

            // Предохранитель (BATCH-1c / BATCH-1d): читаем предмет и проверяем гард
            if (_testStepExecutor is null && (
                    (pipeline.GuardCondition is { } gc && gc.OrAlternatives.Any(g => g.Clauses.Count > 0))
                    || pipeline.EnableStepRecognition))
            {
                ct.ThrowIfCancellationRequested();
                var (guardOk, guardReason) = await CheckPreStepGuardsAsync(pipeline, screen, log, ct).ConfigureAwait(false);
                if (!guardOk)
                    return new PipelineRunResult
                    {
                        Status = PipelineRunStatus.Aborted,
                        Message = guardReason,
                        TotalAttempts = totalAttempts,
                        FinalItemText = finalItem,
                    };
            }

            log?.Report($"[Шаг {stepIdx}] «{step.Name}»: выполнение {step.Action}…");

            StepOutcome outcome;
            try
            {
                ct.ThrowIfCancellationRequested();
                outcome = _testStepExecutor is not null
                    ? await _testStepExecutor(step, ct).ConfigureAwait(false)
                    : await ExecuteStepAsync(step, screen, log, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new PipelineRunResult
                {
                    Status = PipelineRunStatus.Cancelled,
                    Message = "Отменено пользователем.",
                    TotalAttempts = totalAttempts,
                    FinalItemText = finalItem,
                };
            }

            totalAttempts += outcome.Attempts;
            if (outcome.FinalItemText is not null)
                finalItem = outcome.FinalItemText;

            var transition = outcome.Succeeded ? step.OnSuccess : step.OnFailure;
            log?.Report($"[Шаг {stepIdx}] «{step.Name}»: {(outcome.Succeeded ? "успех" : "неудача")} → {transition.Target}");

            switch (transition.Target)
            {
                case TransitionTarget.Done:
                    return new PipelineRunResult
                    {
                        Status = PipelineRunStatus.Done,
                        Message = string.IsNullOrEmpty(transition.Message) ? "Готово." : transition.Message,
                        TotalAttempts = totalAttempts,
                        FinalItemText = finalItem,
                    };

                case TransitionTarget.Abort:
                    return new PipelineRunResult
                    {
                        Status = PipelineRunStatus.Aborted,
                        Message = string.IsNullOrEmpty(transition.Message) ? "Прервано." : transition.Message,
                        TotalAttempts = totalAttempts,
                        FinalItemText = finalItem,
                    };

                case TransitionTarget.Next:
                    stepIdx++;
                    if (stepIdx >= pipeline.Steps.Count)
                        return new PipelineRunResult
                        {
                            Status = PipelineRunStatus.Done,
                            Message = "Все шаги выполнены.",
                            TotalAttempts = totalAttempts,
                            FinalItemText = finalItem,
                        };
                    break;

                case TransitionTarget.Step:
                    stepIdx = transition.StepIndex;
                    break;
            }
        }

        } // try
        finally
        {
            Win32Input.ReleaseShift();
            Win32Input.ReleaseCtrlAlt();
        }
    }

    internal async Task<StepOutcome> ExecuteStepAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        return step.Action switch
        {
            PipelineAction.CheckItem => await ExecuteCheckItemAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.ChaosCraft => await ExecuteChaosCraftAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.AugAnnulCraft => await ExecuteAugAnnulAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.DivineCraft => await ExecuteDivineCraftAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.ExaltCraft => await ExecuteExaltCraftAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.SimpleCurrency => await ExecuteSimpleCurrencyAsync(step, screen, log, ct).ConfigureAwait(false),
            // Устаревшие — оставлены для совместимости сохранённых JSON-пайплайнов
            PipelineAction.SimpleExalt => await ExecuteSimpleExaltAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.SimpleAnnul => await ExecuteSimpleAnnulAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.SimpleChaos => await ExecuteSimpleChaosAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.OmenActivation => await ExecuteOmenActivationAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.DeliriumLiquid => await ExecuteDeliriumLiquidAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.TravelToLocation => await ExecuteTravelAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.SimpleAbyssalBone => await ExecuteSimpleAbyssalBoneAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.WalkToPosition => await ExecuteWalkToPositionAsync(step, log, ct).ConfigureAwait(false),
            PipelineAction.OpenStash => await ExecuteOpenStashAsync(screen, log, ct).ConfigureAwait(false),
            PipelineAction.ClickTemplate => await ExecuteClickTemplateAsync(step, log, ct).ConfigureAwait(false),
            PipelineAction.DesecratePick => await ExecuteDesecratePickAsync(step, log, ct).ConfigureAwait(false),
            PipelineAction.CtrlClickItem => await ExecuteCtrlClickItemAsync(screen, log, ct).ConfigureAwait(false),
            PipelineAction.ClickRegion   => await ExecuteClickRegionAsync(step, log, ct).ConfigureAwait(false),
            PipelineAction.ManualPause => StepOutcome.Success(),
            _ => StepOutcome.Failure(),
        };
    }

    private async Task<StepOutcome> ExecuteCheckItemAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_chaos is null)
            return StepOutcome.Failure();

        var text = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return StepOutcome.Failure();

        if (step.EntryCondition is null)
            return StepOutcome.Success(0, text);

        var parsed = ItemParser.Parse(text);
        var matched = CraftConditionEvaluator.TryEvaluate(step.EntryCondition, parsed, out var detail);
        log?.Report($"[CheckItem] {(matched ? "выполнено" : "не выполнено")}: {detail}");
        return matched ? StepOutcome.Success(0, text) : StepOutcome.Failure(0, text);
    }

    private async Task<StepOutcome> ExecuteChaosCraftAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_chaos is null)
            return StepOutcome.Failure();

        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        var plan = step.LoopUntil ?? new CraftConditionPlan { ExpectedItemClass = "" };
        var result = await _chaos.RunAsync(
            screen.ChaosOrbArea, screen.ItemArea, plan, step.Name,
            step.MaxIterations, step.MaxIterations, 0, log, ct).ConfigureAwait(false);
        return new StepOutcome(result.Success, result.Attempts, result.FinalItem);
    }

    private async Task<StepOutcome> ExecuteDivineCraftAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_divine is null)
            return StepOutcome.Failure();

        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        var plan = step.LoopUntil ?? new CraftConditionPlan { ExpectedItemClass = "" };
        var result = await _divine.RunAsync(
            screen.DivineOrbArea, screen.ItemArea, plan, step.Name,
            step.MaxIterations, step.MaxIterations, 0, log, ct).ConfigureAwait(false);
        return new StepOutcome(result.Success, result.Attempts, result.FinalItem);
    }

    private async Task<StepOutcome> ExecuteAugAnnulAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_augAnnul is null || _chaos is null)
            return StepOutcome.Failure();

        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        var initialText = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
        var initialParsed = ItemParser.Parse(initialText);
        if (initialParsed is null)
            return StepOutcome.Failure();

        var plan = step.LoopUntil ?? new CraftConditionPlan { ExpectedItemClass = "" };
        var result = await _augAnnul.RunAsync(
            screen.AugmentOrbArea, screen.AnnulOrbArea, screen.ItemArea,
            plan, step.Name, initialParsed, initialText,
            step.MaxIterations, step.MaxIterations, 0, log, ct).ConfigureAwait(false);
        return new StepOutcome(result.Success, result.Attempts, result.FinalItem);
    }

    private async Task<StepOutcome> ExecuteExaltCraftAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_exalt is null || _chaos is null)
            return StepOutcome.Failure();

        var initialText = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
        var initialParsed = ItemParser.Parse(initialText);
        if (initialParsed is null)
            return StepOutcome.Failure();

        var plan = step.LoopUntil ?? new CraftConditionPlan { ExpectedItemClass = "" };
        var result = await _exalt.RunAsync(
            screen.ExaltOrbArea, screen.AnnulOrbArea,
            screen.RitualInventoryRegion, screen.CurrencyInventoryRegion,
            screen.ExaltOmenSinistralRegion, screen.ExaltOmenDextralRegion, screen.ExaltOmenGreaterRegion,
            screen.ExaltOmenSinistralCells, screen.ExaltOmenDextralCells, screen.ExaltOmenGreaterCells,
            screen.ItemArea, plan, step.Name, initialParsed, initialText,
            step.MaxIterations, step.MaxIterations, 0, log, ct, null).ConfigureAwait(false);
        // ExaltCraft переключает вкладки самостоятельно — текущая вкладка неизвестна после завершения
        _currentStashTab = default;
        return new StepOutcome(result.Success, result.Attempts, result.FinalItem);
    }

    private async Task<StepOutcome> ExecuteSimpleCurrencyAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        var currencyId = step.CurrencyId;
        if (string.IsNullOrWhiteSpace(currencyId))
        {
            log?.Report("[Currency] Не выбран орб. Настройте шаг.");
            return StepOutcome.Failure();
        }

        if (!screen.CurrencyItemRegions.TryGetValue(currencyId, out var orbRect) || orbRect == default)
        {
            log?.Report($"[Currency] Орб «{currencyId}» не настроен в «Настройки областей → Currency».");
            return StepOutcome.Failure();
        }

        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();

        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        await ApplyCurrencyToItemAsync(orbRect, screen.ItemArea, currencyId, log, ct).ConfigureAwait(false);
        var itemText = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
        return StepOutcome.Success(1, itemText);
    }

    // Устаревшие Simple-действия — редиректят на единую логику ApplyCurrencyToItemAsync
    private async Task<StepOutcome> ExecuteSimpleAnnulAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();
        if (screen.AnnulOrbArea == default) { log?.Report("[Currency] Область Annulment Orb не настроена."); return StepOutcome.Failure(); }
        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        await ApplyCurrencyToItemAsync(screen.AnnulOrbArea, screen.ItemArea, "Orb of Annulment", log, ct).ConfigureAwait(false);
        return StepOutcome.Success(1);
    }

    private async Task<StepOutcome> ExecuteSimpleChaosAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();
        if (screen.ChaosOrbArea == default) { log?.Report("[Currency] Область Chaos Orb не настроена."); return StepOutcome.Failure(); }
        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        await ApplyCurrencyToItemAsync(screen.ChaosOrbArea, screen.ItemArea, "Chaos Orb", log, ct).ConfigureAwait(false);
        return StepOutcome.Success(1);
    }

    private async Task<StepOutcome> ExecuteSimpleExaltAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();
        if (screen.ExaltOrbArea == default) { log?.Report("[Currency] Область Exalted Orb не настроена."); return StepOutcome.Failure(); }
        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        await ApplyCurrencyToItemAsync(screen.ExaltOrbArea, screen.ItemArea, "Exalted Orb", log, ct).ConfigureAwait(false);
        return StepOutcome.Success(1);
    }

    private async Task ApplyCurrencyToItemAsync(
        ScreenRect orbRect, ScreenRect itemRect, string displayName, IProgress<string>? log, CancellationToken ct)
    {
        var (ox, oy) = orbRect.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        log?.Report($"[Currency] ПКМ «{displayName}» ({ox},{oy})…");
        Win32Input.MoveTo(ox, oy);
        await Task.Delay(150, ct).ConfigureAwait(false);
        Win32Input.ClickRight();
        await Task.Delay(300, ct).ConfigureAwait(false);

        var (ix, iy) = itemRect.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        log?.Report($"[Currency] ЛКМ предмет ({ix},{iy})…");
        Win32Input.MoveTo(ix, iy);
        await Task.Delay(150, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(300, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет <see cref="CraftPipelineStep.EntryCondition"/> перед выполнением шага.
    /// Возвращает false (и логирует причину) если условие не выполнено.
    /// </summary>
    private async Task<bool> CheckEntryConditionAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (step.EntryCondition is null)
            return true;
        if (_chaos is null)
            return false;
        var text = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            log?.Report($"[entryCondition] буфер пуст — условие не проверить.");
            return false;
        }
        var parsed = ItemParser.Parse(text);
        var matched = CraftConditionEvaluator.TryEvaluate(step.EntryCondition, parsed, out var detail);
        log?.Report($"[entryCondition] {(matched ? "выполнено" : "не выполнено")}: {detail}");
        return matched;
    }

    private async Task<StepOutcome> ExecuteOmenActivationAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_omen is null || step.OmenConfig is null)
            return StepOutcome.Failure();

        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();

        var cfg = step.OmenConfig;
        var stashCells = cfg.OmenName switch
        {
            OmenActivationService.OmenSinistralExaltationName => screen.OmenSinistralStashCells,
            OmenActivationService.OmenDextralExaltationName   => screen.OmenDextralStashCells,
            OmenActivationService.OmenGreaterExaltationName   => screen.OmenGreaterStashCells,
            _ when screen.OmenStashCellsByName.TryGetValue(cfg.OmenName, out var cells) => cells,
            _ => (IReadOnlyList<ScreenRect>)Array.Empty<ScreenRect>(),
        };

        if (cfg.StashCellIndex < 0 || cfg.StashCellIndex >= stashCells.Count)
        {
            log?.Report($"Омен «{cfg.OmenName}»: ячейка стэша [{cfg.StashCellIndex}] вне диапазона (всего {stashCells.Count}). Настройте область омена в «Настройки областей».");
            return StepOutcome.Failure();
        }

        log?.Report($"[Омен] «{cfg.OmenName}»: стэш ячеек={stashCells.Count}, инвентарь ячеек={screen.FullInventoryCells.Count}, цель=[строка {cfg.InventoryRow}, столбец {cfg.InventoryCol}]");

        var invIdx = cfg.InventoryRow * screen.InventoryGridColumns + cfg.InventoryCol;
        if (invIdx < 0 || invIdx >= screen.FullInventoryCells.Count)
        {
            var maxRow = screen.FullInventoryCells.Count / screen.InventoryGridColumns - 1;
            var maxCol = screen.InventoryGridColumns - 1;
            log?.Report($"[Омен] ОШИБКА: строка {cfg.InventoryRow} или столбец {cfg.InventoryCol} вне диапазона. Допустимо: строки 0–{maxRow}, столбцы 0–{maxCol}.");
            return StepOutcome.Failure();
        }

        var placement = new OmenPlacement
        {
            StashCell     = stashCells[cfg.StashCellIndex],
            InventoryCell = screen.FullInventoryCells[invIdx],
            OmenName      = cfg.OmenName,
        };

        var tabRegion = screen.OmenStashTabByName.TryGetValue(cfg.OmenName, out var t) ? t : screen.RitualInventoryRegion;
        var activated = await _omen.PlaceAndActivateOmensAsync(new[] { placement }, log, ct, tabRegion).ConfigureAwait(false);
        // OmenActivation переключает вкладки самостоятельно — текущая вкладка неизвестна после завершения
        _currentStashTab = default;
        return activated ? StepOutcome.Success() : StepOutcome.Failure();
    }

    private async Task<StepOutcome> ExecuteDeliriumLiquidAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        var cfg = step.DeliriumLiquidConfig;
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.LiquidName))
        {
            log?.Report("[Delirium] Не задано название масла. Настройте шаг.");
            return StepOutcome.Failure();
        }

        if (!screen.DeliriumItemRegions.TryGetValue(cfg.LiquidName, out var liquidRect) || liquidRect == default)
        {
            log?.Report($"[Delirium] Масло «{cfg.LiquidName}» не настроено (нет в DeliriumItemRegions). Настройте область в «Настройки областей».");
            return StepOutcome.Failure();
        }

        await SwitchStashTabAsync(screen.DeliriumInventoryRect, log, ct, "Делириум").ConfigureAwait(false);

        // ПКМ на ячейке масла
        var (lx, ly) = liquidRect.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        log?.Report($"[Delirium] ПКМ масло «{cfg.LiquidName}» ({lx},{ly})…");
        Win32Input.MoveTo(lx, ly);
        await Task.Delay(150, ct).ConfigureAwait(false);
        Win32Input.ClickRight();
        await Task.Delay(300, ct).ConfigureAwait(false);

        // ЛКМ на предмет
        var (ix, iy) = screen.ItemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        log?.Report($"[Delirium] ЛКМ предмет ({ix},{iy})…");
        Win32Input.MoveTo(ix, iy);
        await Task.Delay(150, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(300, ct).ConfigureAwait(false);

        return StepOutcome.Success(1);
    }

    private async Task<StepOutcome> ExecuteSimpleAbyssalBoneAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        var boneId = step.AbyssalBoneId;
        if (string.IsNullOrWhiteSpace(boneId))
        {
            log?.Report("[Bone] Не выбрана кость. Настройте шаг.");
            return StepOutcome.Failure();
        }

        if (!screen.AbyssItemRegions.TryGetValue(boneId, out var boneRect) || boneRect == default)
        {
            log?.Report($"[Bone] Кость «{boneId}» не настроена (нет в AbyssItemRegions). Настройте область в «Настройки областей → Abyss».");
            return StepOutcome.Failure();
        }

        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();

        var maxIter = step.MaxIterations > 0 ? step.MaxIterations : 1000;
        var consumed = 0; // кости реально потраченные (предмет изменился)
        var itemText = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);

        for (var i = 0; i < maxIter; i++)
        {
            ct.ThrowIfCancellationRequested();
            var prevText = itemText;

            await SwitchStashTabAsync(screen.AbyssInventoryRegion, log, ct, "Abyss").ConfigureAwait(false);

            var (bx, by) = boneRect.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
            log?.Report($"[Bone] ПКМ кость «{boneId}» ({bx},{by})…");
            Win32Input.MoveTo(bx, by);
            await Task.Delay(150, ct).ConfigureAwait(false);
            Win32Input.ClickRight();
            await Task.Delay(300, ct).ConfigureAwait(false);

            var (ix, iy) = screen.ItemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
            log?.Report($"[Bone] ЛКМ предмет ({ix},{iy})…");
            Win32Input.MoveTo(ix, iy);
            await Task.Delay(150, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(300, ct).ConfigureAwait(false);

            itemText = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);

            // Кость считается потраченной только если предмет реально изменился.
            if (itemText != prevText)
                consumed++;

            if (step.LoopUntil is null)
                break;

            var parsed = ItemParser.Parse(itemText);
            var met = CraftConditionEvaluator.TryEvaluate(step.LoopUntil, parsed, out var detail);
            log?.Report($"[Bone] loopUntil: {(met ? "выполнено" : "не выполнено")}: {detail}");
            if (met)
                break;
        }

        return StepOutcome.Success(consumed, itemText);
    }

    private async Task<StepOutcome> ExecuteTravelAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        var cfg = step.TravelConfig;
        if (cfg is null)
        {
            log?.Report("[Travel] Конфигурация перехода не задана. Настройте шаг.");
            return StepOutcome.Failure();
        }

        var ok = await _travel.TravelAsync(cfg, log, ct, screen.LocationNameArea).ConfigureAwait(false);
        return ok ? StepOutcome.Success(1) : StepOutcome.Failure();
    }

    private async Task<StepOutcome> ExecuteOpenStashAsync(
        PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        // Сначала проверяем — возможно стэш уже открыт
        var checkRect = screen.StashIsOpenCheckRect;
        if (checkRect.Width > 0 && checkRect.Height > 0)
        {
            var checkText   = string.IsNullOrWhiteSpace(screen.StashIsOpenCheckText) ? "Stash" : screen.StashIsOpenCheckText;
            var checkTarget = WindowsOcrTextLocator.NormalizeForMatch(checkText);
            var checkMatch  = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(checkRect, checkTarget, null, ct)
                                  .ConfigureAwait(false);
            if (checkMatch is not null)
            {
                log?.Report($"[OpenStash] Стэш уже открыт (найдено «{checkMatch.Value.MatchedLineText}» в области проверки) — пропускаем клик.");
                return StepOutcome.Success();
            }
        }

        // Стэш не открыт — ищем иконку и кликаем
        var searchRect = screen.StashOcrSearchRect;
        if (searchRect.Width <= 0 || searchRect.Height <= 0)
        {
            log?.Report("[OpenStash] Область OCR стэша не задана в настройках — пропускаем.");
            return StepOutcome.Success();
        }

        var ocrText = string.IsNullOrWhiteSpace(screen.StashOcrText) ? "STASH" : screen.StashOcrText;
        var target  = WindowsOcrTextLocator.NormalizeForMatch(ocrText);
        // exactMatch=true: "STASH" не совпадёт с "GUILDSTASH" (Guild Stash без пробела после нормализации)
        var match   = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(searchRect, target, log, ct, exactMatch: true)
                          .ConfigureAwait(false);

        if (match is null)
        {
            log?.Report($"[OpenStash] OCR: «{ocrText}» не найден в области поиска — пропускаем.");
            return StepOutcome.Success();
        }

        var (cx, cy) = match.Value.BoundsOnScreen.GetInteriorPoint(inset: 1);
        log?.Report($"[OpenStash] Найдено «{match.Value.MatchedLineText}» → клик ({cx},{cy})");
        Win32Input.MoveTo(cx, cy);
        await Task.Delay(100, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        var delay = screen.StashOpenDelayMs > 0 ? screen.StashOpenDelayMs : 2000;
        await Task.Delay(delay, ct).ConfigureAwait(false);
        return StepOutcome.Success();
    }

    private async Task<StepOutcome> ExecuteClickTemplateAsync(
        CraftPipelineStep step, IProgress<string>? log, CancellationToken ct)
    {
        var cfg = step.TemplateConfig;
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.TemplatePath))
        {
            log?.Report("[ClickTemplate] Конфигурация шаблона не задана.");
            return StepOutcome.Failure();
        }

        var path = Path.IsPathRooted(cfg.TemplatePath)
            ? cfg.TemplatePath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cfg.TemplatePath);

        if (!File.Exists(path))
        {
            log?.Report($"[ClickTemplate] Файл шаблона не найден: {path}");
            return StepOutcome.Failure();
        }

        var searchRect = cfg.SearchRect;
        if (searchRect.Width <= 0 || searchRect.Height <= 0)
        {
            log?.Report("[ClickTemplate] Область поиска не задана.");
            return StepOutcome.Failure();
        }

        log?.Report($"[ClickTemplate] Ищем «{Path.GetFileName(path)}» в {searchRect.Width}×{searchRect.Height}, порог={cfg.MatchThreshold:P0}, допуск={cfg.ColorTolerance}");
        var result = await TemplateMatcher.FindAsync(path, searchRect, cfg.MatchThreshold, cfg.ColorTolerance, ct).ConfigureAwait(false);

        if (result is null)
        {
            log?.Report($"[ClickTemplate] Шаблон не найден (счёт ниже порога {cfg.MatchThreshold:P0}).");
            return StepOutcome.Failure();
        }

        log?.Report($"[ClickTemplate] Найдено: ({result.ScreenX},{result.ScreenY}), счёт={result.Score:P1} → клик");
        Win32Input.MoveTo(result.ScreenX, result.ScreenY);
        await Task.Delay(100, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        if (cfg.ClickDelayMs > 0)
            await Task.Delay(cfg.ClickDelayMs, ct).ConfigureAwait(false);
        return StepOutcome.Success();
    }

    private async Task<StepOutcome> ExecuteDesecratePickAsync(
        CraftPipelineStep step, IProgress<string>? log, CancellationToken ct)
    {
        var cfg = step.DesecratePickConfig;
        if (cfg is null)
        {
            log?.Report("[DesecratePick] Конфигурация не задана.");
            return StepOutcome.Failure();
        }

        var area = cfg.RevealArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            log?.Report("[DesecratePick] Область reveal не задана.");
            return StepOutcome.Failure();
        }

        log?.Report($"[DesecratePick] OCR области {area.Width}×{area.Height}…");
        var lines = await WindowsOcrTextLocator.ReadAllLinesAsync(area, log, ct).ConfigureAwait(false);

        if (lines.Count == 0)
        {
            log?.Report("[DesecratePick] OCR не вернул строк — интерфейс reveal не открыт?");
            return StepOutcome.Failure();
        }

        log?.Report($"[DesecratePick] Распознано строк: {lines.Count}");
        foreach (var line in lines)
            log?.Report($"  • {line.Text}");

        var libraryClass = string.IsNullOrWhiteSpace(cfg.ItemClass) ? cfg.ItemName : cfg.ItemClass;
        var desecrateEntries = DesecrateStatsScanner.GetDesecrateEntries(libraryClass);

        // Находим строки OCR, соответствующие десекрейт-моду, с сохранением позиций
        var foundDesecrate = new List<OcrTextLine>();
        foreach (var line in lines)
        {
            if (DesecrateStatsScanner.FindDesecrateMatch(line.Text, desecrateEntries) is not null)
                foundDesecrate.Add(line);
        }

        var foundTexts = foundDesecrate.Select(l => l.Text).ToList();
        if (foundDesecrate.Count == 0 && desecrateEntries.Count > 0)
            log?.Report($"[DesecratePick] Десекрейт-мод не опознан среди {lines.Count} строк. Все: {string.Join(" | ", lines.Select(l => l.Text))}");
        else
            foreach (var m in foundTexts) log?.Report($"[DesecratePick] Десекрейт-мод: «{m}»");

        await WriteDesecrateLogEntryAsync(cfg, lines.Select(l => l.Text).ToList(), foundTexts).ConfigureAwait(false);

        // Условие не задано — принимаем первый найденный десекрейт-мод
        if (cfg.PickCondition is null || !HasClauses(cfg.PickCondition))
        {
            if (foundDesecrate.Count > 0)
            {
                await ClickOcrLineAsync(foundDesecrate[0], cfg.ClickDelayMs, log, ct).ConfigureAwait(false);
                log?.Report("[DesecratePick] Условие не задано → принят первый десекрейт-мод → Success");
                await HandleConfirmAsync(cfg, log, ct).ConfigureAwait(false);
            }
            else
            {
                log?.Report("[DesecratePick] Условие не задано и десекрейт-мод не опознан → Success (без клика)");
            }
            return StepOutcome.Success();
        }

        // Проверяем каждую OCR-строку как отдельный мод через CraftConditionPlan
        var expectedClass = string.IsNullOrWhiteSpace(cfg.ItemClass) ? cfg.ItemName : cfg.ItemClass;
        foreach (var ocrLine in lines)
        {
            var syntheticItem = BuildSyntheticItem(ocrLine.Text, expectedClass);
            if (CraftConditionEvaluator.TryEvaluate(cfg.PickCondition, syntheticItem, out var detail))
            {
                log?.Report($"[DesecratePick] Условие выполнено для «{ocrLine.Text}»: {detail} → клик → Success");
                await ClickOcrLineAsync(ocrLine, cfg.ClickDelayMs, log, ct).ConfigureAwait(false);
                await HandleConfirmAsync(cfg, log, ct).ConfigureAwait(false);
                return StepOutcome.Success();
            }
        }

        log?.Report("[DesecratePick] Ни одна из строк не удовлетворила условию → Failure");
        return StepOutcome.Failure();
    }

    /// <summary>Строит синтетический ParsedItem из одной строки OCR-текста для проверки условия.</summary>
    private static ParsedItem BuildSyntheticItem(string ocrLine, string itemClass)
    {
        var effectLine = ItemParser.ParseRawStatLine(ocrLine);
        var affix = new AffixInfo
        {
            Type    = "Suffix Modifier",
            Effects = new List<string> { ocrLine },
            EffectDetails = new List<AffixEffectLine> { effectLine },
        };
        return new ParsedItem
        {
            IsValid   = true,
            ItemClass = itemClass,
            Rarity    = "Rare",
            Affixes   = new List<AffixInfo> { affix },
        };
    }

    private static bool HasClauses(CraftConditionPlan? plan) =>
        plan?.OrAlternatives?.Any(g => g.Clauses?.Count > 0) == true;

    private static async Task HandleConfirmAsync(DesecratePickConfig cfg, IProgress<string>? log, CancellationToken ct)
    {
        var area = cfg.ConfirmButtonArea;
        if (area.Width <= 0 || area.Height <= 0)
            return; // область не задана — ни клик, ни ожидание невозможны

        if (cfg.AutoConfirm)
        {
            var (cx, cy) = area.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
            log?.Report($"[DesecratePick] Confirm ({cx},{cy})…");
            Win32Input.MoveTo(cx, cy);
            await Task.Delay(150, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(400, ct).ConfigureAwait(false);
        }
        else
        {
            log?.Report("[DesecratePick] Ожидание нажатия Confirm пользователем (наблюдаем за областью кнопки)…");
            await WaitForAreaChangeAsync(area, log, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ждёт пока пиксели в области заметно изменятся (кнопка исчезла / UI закрылся).
    /// Сравнивает текущий снимок с опорным через среднее отклонение яркости сэмпла пикселей.
    /// </summary>
    private static async Task WaitForAreaChangeAsync(
        ScreenRect area, IProgress<string>? log, CancellationToken ct,
        int timeoutMs = 120_000, int pollMs = 300, double changeThreshold = 20.0)
    {
        using var refBmp  = ScreenCaptureHelper.CaptureRegion(area);
        var refSample = SamplePixels(refBmp);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(pollMs, ct).ConfigureAwait(false);

            using var cur = ScreenCaptureHelper.CaptureRegion(area);
            var curSample = SamplePixels(cur);
            var diff = PixelSampleDiff(refSample, curSample);

            if (diff >= changeThreshold)
            {
                log?.Report($"[DesecratePick] Confirm обнаружен (diff={diff:F1}) → продолжаем");
                return;
            }
        }
        log?.Report($"[DesecratePick] Таймаут {timeoutMs / 1000}с — продолжаем без подтверждения");
    }

    // Сэмплирует равномерную сетку пикселей из Bitmap (до 400 точек).
    private static float[] SamplePixels(System.Drawing.Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        const int maxSamples = 400;
        var stepX = Math.Max(1, w / 20);
        var stepY = Math.Max(1, h / 20);
        var samples = new List<float>(maxSamples * 3);
        for (var y = 0; y < h && samples.Count < maxSamples * 3; y += stepY)
        for (var x = 0; x < w && samples.Count < maxSamples * 3; x += stepX)
        {
            var c = bmp.GetPixel(x, y);
            samples.Add(c.R);
            samples.Add(c.G);
            samples.Add(c.B);
        }
        return samples.ToArray();
    }

    private static double PixelSampleDiff(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var len = Math.Min(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < len; i++)
            sum += Math.Abs(a[i] - b[i]);
        return sum / len;
    }

    private static async Task ClickOcrLineAsync(OcrTextLine line, int delayMs, IProgress<string>? log, CancellationToken ct)
    {
        var (cx, cy) = line.Center;
        log?.Report($"[DesecratePick] Клик по «{line.Text}» @ ({cx},{cy})");
        Win32Input.MoveTo(cx, cy);
        await Task.Delay(100, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        if (delayMs > 0)
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
    }

    private async Task<StepOutcome> ExecuteCtrlClickItemAsync(
        PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        var area = screen.ItemArea;
        if (area.Width <= 0 || area.Height <= 0)
        {
            log?.Report("[CtrlClickItem] ItemArea не задана — невозможно переместить предмет.");
            return StepOutcome.Failure();
        }

        var (cx, cy) = area.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        log?.Report($"[CtrlClickItem] Ctrl+ЛКМ предмет ({cx},{cy})…");
        Win32Input.KeyDown(0x11); // VK_CONTROL
        try
        {
            await Task.Delay(80, ct).ConfigureAwait(false);
            Win32Input.MoveTo(cx, cy);
            await Task.Delay(80, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(400, ct).ConfigureAwait(false);
        }
        finally
        {
            Win32Input.KeyUp(0x11);
        }

        return StepOutcome.Success();
    }

    private static async Task<StepOutcome> ExecuteClickRegionAsync(
        CraftPipelineStep step, IProgress<string>? log, CancellationToken ct)
    {
        var cfg = step.ClickRegionConfig;
        if (cfg is null)
        {
            log?.Report("[ClickRegion] Конфигурация не задана.");
            return StepOutcome.Failure();
        }

        var region = cfg.Region;
        if (region.Width <= 0 || region.Height <= 0)
        {
            log?.Report("[ClickRegion] Область не задана.");
            return StepOutcome.Failure();
        }

        var (cx, cy) = region.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        if (cfg.UseCtrl)
        {
            log?.Report($"[ClickRegion] Ctrl+ЛКМ ({cx},{cy})…");
            Win32Input.KeyDown(0x11);
            try
            {
                await Task.Delay(80, ct).ConfigureAwait(false);
                Win32Input.MoveTo(cx, cy);
                await Task.Delay(80, ct).ConfigureAwait(false);
                Win32Input.ClickLeft();
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            finally
            {
                Win32Input.KeyUp(0x11);
            }
        }
        else
        {
            log?.Report($"[ClickRegion] ЛКМ ({cx},{cy})…");
            Win32Input.MoveTo(cx, cy);
            await Task.Delay(80, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
        }

        if (cfg.ClickDelayMs > 0)
            await Task.Delay(cfg.ClickDelayMs, ct).ConfigureAwait(false);

        return StepOutcome.Success();
    }

    private static async Task WriteDesecrateLogEntryAsync(
        DesecratePickConfig cfg,
        List<string> allLines,
        List<string> desecrateMods)
    {
        try
        {
            var tradeDir = Path.Combine(ProjectPaths.GetProjectRoot(), "trade_data");
            Directory.CreateDirectory(tradeDir);
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var safeName = string.IsNullOrWhiteSpace(cfg.ItemName)
                ? "item"
                : string.Concat(cfg.ItemName.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(tradeDir, $"{date}_{safeName}_desecrate_log.jsonl");

            var entry = new
            {
                timestamp  = DateTime.Now.ToString("o"),
                item       = cfg.ItemName,
                item_class = cfg.ItemClass,
                all_mods_desecrate = cfg.AllModsFromDesecratePool,
                mods       = allLines.ToArray(),
                desecrate_mods = desecrateMods.ToArray(),
            };
            var json = System.Text.Json.JsonSerializer.Serialize(entry);
            await File.AppendAllTextAsync(file, json + "\n").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"[DesecratePick] Ошибка записи лога: {ex.Message}");
        }
    }

    private async Task<StepOutcome> ExecuteWalkToPositionAsync(
        CraftPipelineStep step, IProgress<string>? log, CancellationToken ct)
    {
        var cfg = step.WalkConfig;
        if (cfg is null || cfg.KeyPresses.Count == 0)
        {
            log?.Report("[Walk] Конфигурация движения не задана. Добавьте хотя бы одно нажатие клавиши.");
            return StepOutcome.Failure();
        }

        ct.ThrowIfCancellationRequested();

        if (cfg.PressEscapeFirst)
        {
            log?.Report("[Walk] Закрываем интерфейс (Escape)…");
            Win32Input.PressKey(0x1B); // VK_ESCAPE
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        foreach (var kp in cfg.KeyPresses)
        {
            ct.ThrowIfCancellationRequested();
            var vks = kp.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => (byte)k.ToUpperInvariant()[0])
                .Distinct()
                .ToArray();
            if (vks.Length == 0 || kp.DurationMs <= 0)
                continue;

            var label = string.Join("+", kp.Keys.Select(k => k.ToUpperInvariant()));
            log?.Report($"[Walk] Держим «{label}» {kp.DurationMs} мс…");
            foreach (var vk in vks) Win32Input.KeyDown(vk);
            try
            {
                await Task.Delay(kp.DurationMs, ct).ConfigureAwait(false);
            }
            finally
            {
                foreach (var vk in vks) Win32Input.KeyUp(vk); // все клавиши гарантированно отпускаем
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return StepOutcome.Success();
    }

    /// <summary>
    /// BATCH-1c / BATCH-1d: Предварительная проверка предмета перед шагом.
    /// Один Ctrl+Alt+C — результаты обеих проверок из одного чтения.
    /// </summary>
    private async Task<(bool ok, string reason)> CheckPreStepGuardsAsync(
        CraftPipeline pipeline, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_chaos is null)
            return (true, "");

        var text = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            const string msg = "[Предохранитель] Буфер пуст — не удалось прочитать предмет.";
            log?.Report(msg);
            return (false, msg);
        }

        var item = ItemParser.Parse(text);

        // BATCH-1c: GuardCondition
        if (pipeline.GuardCondition is { } guard && guard.OrAlternatives.Any(g => g.Clauses.Count > 0))
        {
            if (!CraftConditionEvaluator.TryEvaluate(guard, item, out var detail))
            {
                var msg = $"[Предохранитель] GuardCondition не выполнен: {detail}";
                log?.Report(msg);
                return (false, msg);
            }
            log?.Report($"[Предохранитель] GuardCondition OK: {detail}");
        }

        // BATCH-1d: recognizability — предмет должен совпадать хотя бы с одним entryCondition
        if (pipeline.EnableStepRecognition)
        {
            var detect = PipelineStepDetector.Detect(pipeline, item);
            if (detect.StepIndex is null)
            {
                const string msg = "[Предохранитель] Предмет не распознан ни на одном шаге пайплайна — крафт прерван.";
                log?.Report(msg);
                return (false, msg);
            }
            log?.Report($"[Предохранитель] Предмет распознан: шаг {detect.StepIndex} «{pipeline.Steps[detect.StepIndex.Value].Name}»");
        }

        return (true, "");
    }

    private async Task SwitchStashTabAsync(ScreenRect tabRegion, IProgress<string>? log, CancellationToken ct, string label)
    {
        if (tabRegion == default) return;
        if (tabRegion == _currentStashTab) return;
        var (tx, ty) = tabRegion.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        log?.Report($"[Пайплайн] Переключаемся на вкладку «{label}» ({tx},{ty})…");
        Win32Input.MoveTo(tx, ty);
        await Task.Delay(300, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(1600, ct).ConfigureAwait(false);
        _currentStashTab = tabRegion;
    }
}
