using GameHelper.Native;

namespace GameHelper.Services;

public enum BatchItemStatus { Active, Done, Failed }

public sealed class BatchItem
{
    public int CellIndex { get; init; }
    public BatchItemStatus Status { get; set; } = BatchItemStatus.Active;
    public int CurrentStage { get; set; }
    public string? LastMessage { get; set; }
    public int TotalAttempts { get; set; }
}

public sealed class BatchRunResult
{
    public IReadOnlyList<BatchItem> Items { get; init; } = Array.Empty<BatchItem>();
    public int DoneCount => Items.Count(i => i.Status == BatchItemStatus.Done);
    public int FailedCount => Items.Count(i => i.Status == BatchItemStatus.Failed);
}

/// <summary>
/// Выполняет пайплайн для нескольких предметов с барьерной синхронизацией по стадиям.
/// Все активные предметы проходят стадию N прежде чем любой переходит к N+1.
/// Предмет, ушедший назад (OnFailure → StepIndex &lt; N), ждёт на целевой стадии.
/// </summary>
public sealed class BatchPipelineRunner
{
    /// <summary>Максимальное суммарное число выполнений шагов. Защита от бесконечного цикла.</summary>
    public const int MaxTotalExecutions = 100_000;

    private readonly CraftPipelineRunner _runner;
    private readonly IChaosCraftService? _chaos;

    /// <summary>
    /// Тест-хук: если установлен, вызывается вместо <c>_runner.ExecuteStepAsync</c>.
    /// Получает текущий предмет, шаг и токен — позволяет тестировать барьерную логику без реального экрана.
    /// </summary>
    internal Func<BatchItem, CraftPipelineStep, CancellationToken, Task<StepOutcome>>? _testStepExecutor;

    public BatchPipelineRunner(CraftPipelineRunner runner, IChaosCraftService? chaos = null)
    {
        _runner = runner;
        _chaos = chaos;
    }

    /// <summary>
    /// Запускает пакетный крафт.
    /// При наличии <see cref="IChaosCraftService"/> предварительно определяет начальные стадии через DetectStep.
    /// </summary>
    public async Task<BatchRunResult> RunAsync(
        CraftPipeline pipeline,
        PipelineScreenConfig screenTemplate,
        IReadOnlyList<ScreenRect> itemCells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (pipeline.Steps.Count == 0 || itemCells.Count == 0)
            return new BatchRunResult { Items = Array.Empty<BatchItem>() };

        var items = new List<BatchItem>(itemCells.Count);
        for (int i = 0; i < itemCells.Count; i++)
            items.Add(new BatchItem { CellIndex = i });

        if (_chaos is not null && _testStepExecutor is null)
            await InitializeStagesAsync(items, pipeline, screenTemplate, itemCells, log, ct).ConfigureAwait(false);

        return await RunCoreAsync(pipeline, screenTemplate, itemCells, items, log, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Основной алгоритм. Принимает готовый список предметов с заданными <see cref="BatchItem.CurrentStage"/>.
    /// Используется напрямую в тестах для инъекции начальных стадий.
    /// </summary>
    internal async Task<BatchRunResult> RunCoreAsync(
        CraftPipeline pipeline,
        PipelineScreenConfig screenTemplate,
        IReadOnlyList<ScreenRect> itemCells,
        List<BatchItem> items,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var executionCount = 0;

        try
        {
            while (items.Any(i => i.Status == BatchItemStatus.Active))
            {
                if (executionCount >= MaxTotalExecutions)
                {
                    log?.Report($"[Батч] Превышен лимит ({MaxTotalExecutions} шагов). Остановлено.");
                    break;
                }

                bool anyProgress = false;

                for (int n = 0; n < pipeline.Steps.Count; n++)
                {
                    var active = items.Where(i => i.Status == BatchItemStatus.Active).ToList();
                    var targetItems = active.Where(i => i.CurrentStage == n).ToList();

                    if (targetItems.Count == 0) continue;

                    // Барьер: ждём пока все активные предметы достигнут стадии n
                    if (active.Any(i => i.CurrentStage < n)) continue;

                    anyProgress = true;
                    log?.Report($"[Батч] Стадия {n} «{pipeline.Steps[n].Name}»: {targetItems.Count} предм.");

                    foreach (var item in targetItems)
                    {
                        executionCount++;
                        ct.ThrowIfCancellationRequested();

                        var step = pipeline.Steps[n];
                        var screen = screenTemplate with { ItemArea = itemCells[item.CellIndex] };

                        StepOutcome outcome;
                        if (_testStepExecutor is not null)
                            outcome = await _testStepExecutor(item, step, ct).ConfigureAwait(false);
                        else
                            outcome = await _runner.ExecuteStepAsync(step, screen, log, ct).ConfigureAwait(false);

                        item.TotalAttempts += outcome.Attempts;
                        ApplyTransition(item, step, outcome.Succeeded, n, pipeline.Steps.Count, log);
                    }
                }

                if (!anyProgress)
                {
                    log?.Report("[Батч] Нет прогресса — возможен deadlock. Остановлено.");
                    break;
                }
            }
        }
        finally
        {
            Win32Input.ReleaseShift();
            Win32Input.ReleaseCtrlAlt();
        }

        log?.Report($"[Батч] Итог: Done={items.Count(i => i.Status == BatchItemStatus.Done)}, Failed={items.Count(i => i.Status == BatchItemStatus.Failed)}");
        return new BatchRunResult { Items = items };
    }

    private static void ApplyTransition(
        BatchItem item, CraftPipelineStep step, bool succeeded,
        int stageN, int stepCount, IProgress<string>? log)
    {
        var transition = succeeded ? step.OnSuccess : step.OnFailure;
        switch (transition.Target)
        {
            case TransitionTarget.Done:
                item.Status = BatchItemStatus.Done;
                item.LastMessage = transition.Message;
                log?.Report($"[Батч] [{item.CellIndex}]: Done");
                break;

            case TransitionTarget.Abort:
                item.Status = BatchItemStatus.Failed;
                item.LastMessage = transition.Message;
                log?.Report($"[Батч] [{item.CellIndex}]: Failed — {transition.Message}");
                break;

            case TransitionTarget.Next:
                var next = stageN + 1;
                if (next >= stepCount)
                {
                    item.Status = BatchItemStatus.Done;
                    item.LastMessage = "Все шаги выполнены.";
                    log?.Report($"[Батч] [{item.CellIndex}]: Done (все шаги)");
                }
                else
                {
                    item.CurrentStage = next;
                }
                break;

            case TransitionTarget.Step:
                item.CurrentStage = transition.StepIndex;
                log?.Report($"[Батч] [{item.CellIndex}]: → стадия {transition.StepIndex}");
                break;
        }
    }

    private async Task InitializeStagesAsync(
        List<BatchItem> items, CraftPipeline pipeline,
        PipelineScreenConfig screenTemplate, IReadOnlyList<ScreenRect> itemCells,
        IProgress<string>? log, CancellationToken ct)
    {
        log?.Report("[Батч] Определяем начальные стадии…");
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var screen = screenTemplate with { ItemArea = itemCells[item.CellIndex] };
            var text = await _chaos!.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                log?.Report($"[Батч] [{item.CellIndex}]: буфер пуст — начинаем со стадии 0");
                continue;
            }
            var parsed = ItemParser.Parse(text);
            var detect = PipelineStepDetector.Detect(pipeline, parsed);
            item.CurrentStage = detect.StepIndex ?? 0;
            log?.Report($"[Батч] [{item.CellIndex}]: стадия {item.CurrentStage} «{pipeline.Steps[item.CurrentStage].Name}»");
        }
    }
}
