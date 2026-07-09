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
public sealed class PipelineScreenConfig
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

    // Полный инвентарь (12×5) для размещения омена
    public IReadOnlyList<ScreenRect> FullInventoryCells { get; init; } = Array.Empty<ScreenRect>();
    public int InventoryGridColumns { get; init; } = 12;

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

    private async Task<StepOutcome> ExecuteStepAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        return step.Action switch
        {
            PipelineAction.CheckItem => await ExecuteCheckItemAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.ChaosCraft => await ExecuteChaosCraftAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.AugAnnulCraft => await ExecuteAugAnnulAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.DivineCraft => await ExecuteDivineCraftAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.ExaltCraft => await ExecuteExaltCraftAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.SimpleExalt => await ExecuteSimpleExaltAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.SimpleAnnul => await ExecuteSimpleAnnulAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.OmenActivation => await ExecuteOmenActivationAsync(step, screen, log, ct).ConfigureAwait(false),
            PipelineAction.DeliriumLiquid => await ExecuteDeliriumLiquidAsync(step, screen, log, ct).ConfigureAwait(false),
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

    private async Task<StepOutcome> ExecuteSimpleAnnulAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_divine is null)
            return StepOutcome.Failure();

        if (!await CheckEntryConditionAsync(step, screen, log, ct).ConfigureAwait(false))
            return StepOutcome.Failure();

        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        var plan = new CraftConditionPlan { ExpectedItemClass = "" };
        var result = await _divine.RunAsync(
            screen.AnnulOrbArea, screen.ItemArea, plan, step.Name,
            1, 1, 0, log, ct).ConfigureAwait(false);
        return StepOutcome.Success(result.Attempts, result.FinalItem);
    }

    private async Task<StepOutcome> ExecuteSimpleExaltAsync(
        CraftPipelineStep step, PipelineScreenConfig screen, IProgress<string>? log, CancellationToken ct)
    {
        if (_divine is null)
            return StepOutcome.Failure();

        await SwitchStashTabAsync(screen.CurrencyInventoryRegion, log, ct, "Валюта").ConfigureAwait(false);
        var plan = new CraftConditionPlan { ExpectedItemClass = "" };
        var result = await _divine.RunAsync(
            screen.ExaltOrbArea, screen.ItemArea, plan, step.Name,
            1, 1, 0, log, ct).ConfigureAwait(false);
        return StepOutcome.Success(result.Attempts, result.FinalItem);
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

        var activated = await _omen.PlaceAndActivateOmensAsync(new[] { placement }, log, ct, screen.RitualInventoryRegion).ConfigureAwait(false);
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
