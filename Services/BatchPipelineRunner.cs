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
    public decimal TotalCostDiv { get; set; }
    public List<BatchCostRecord> CostRecords { get; } = [];

    /// <summary>
    /// Последний известный текст предмета в ячейке. Используется как кэш вместо probe Ctrl+Alt+C.
    /// Null = кэш недействителен, нужно читать буфер.
    /// Инвалидируется после state-changing шагов, не возвращающих FinalItemText.
    /// </summary>
    internal string? LastKnownText { get; set; }
}

public sealed class BatchCostRecord
{
    public string StepName { get; init; } = "";
    public string CurrencyName { get; init; } = "";
    public int Attempts { get; init; }
    public decimal CostDiv { get; init; }
}

public sealed class BatchRunResult
{
    public IReadOnlyList<BatchItem> Items { get; init; } = Array.Empty<BatchItem>();
    public int DoneCount => Items.Count(i => i.Status == BatchItemStatus.Done);
    public int FailedCount => Items.Count(i => i.Status == BatchItemStatus.Failed);
    public string BatchId { get; init; } = "";
    public string PipelineName { get; init; } = "";
    public decimal TotalCostDiv => Items.Sum(i => i.TotalCostDiv);
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
            return new BatchRunResult { Items = Array.Empty<BatchItem>(), BatchId = "", PipelineName = pipeline.Name };

        var batchId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_batch";

        var items = new List<BatchItem>(itemCells.Count);
        for (int i = 0; i < itemCells.Count; i++)
            items.Add(new BatchItem { CellIndex = i });

        if (_chaos is not null && _testStepExecutor is null)
            await InitializeStagesAsync(items, pipeline, screenTemplate, itemCells, log, ct).ConfigureAwait(false);

        if (_testStepExecutor is null)
            await AdjustStagesForCurrentLocationAsync(items, pipeline, screenTemplate.LocationNameArea, log, ct).ConfigureAwait(false);

        return await RunCoreAsync(pipeline, screenTemplate, itemCells, items, log, ct, batchId).ConfigureAwait(false);
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
        CancellationToken ct,
        string? batchId = null)
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
                    var step = pipeline.Steps[n];

                    // Веха: действие выполняется один раз для всей группы предметов,
                    // переход применяется ко всем (барьер уже гарантирует, что все дошли).
                    if (IsMilestoneAction(step))
                    {
                        executionCount++;
                        ct.ThrowIfCancellationRequested();
                        log?.Report($"[Батч] Веха: стадия {n} «{step.Name}» — 1 раз для {targetItems.Count} предм.");
                        StepOutcome milestoneOutcome;
                        if (_testStepExecutor is not null)
                            milestoneOutcome = await _testStepExecutor(targetItems[0], step, ct).ConfigureAwait(false);
                        else
                        {
                            // Для шагов с entryCondition (напр. OmenActivation) передаём область
                            // первого предмета и его кэш, чтобы не смещаться на ячейку 0.
                            var milestoneScreen = screenTemplate with { ItemArea = itemCells[targetItems[0].CellIndex] };
                            _runner.ActiveBatchItemCount = targetItems.Count;
                            milestoneOutcome = await _runner.ExecuteStepAsync(step, milestoneScreen, log, ct,
                                cachedText: targetItems[0].LastKnownText).ConfigureAwait(false);
                            _runner.ActiveBatchItemCount = 1;
                        }
                        foreach (var mi in targetItems)
                        {
                            mi.TotalAttempts++;
                            ApplyTransition(mi, step, milestoneOutcome.Succeeded, n, pipeline.Steps.Count, log);
                        }

                        // OmenActivation: размещаем N оменов за один раз (qty=N через клавиатуру),
                        // затем сразу применяем следующий шаг per-item — каждый омен расходуется
                        // на одно применение орба к конкретному предмету.
                        // Fused-шаг определяется реальным переходом OnSuccess, а не всегда n+1:
                        // пайплайны часто переходят через Step(idx), минуя n+1.
                        if (step.Action == PipelineAction.OmenActivation && milestoneOutcome.Succeeded)
                        {
                            var fusedStepIndex = step.OnSuccess.Target switch
                            {
                                TransitionTarget.Next => n + 1,
                                TransitionTarget.Step => step.OnSuccess.StepIndex,
                                _ => -1,
                            };
                            if (fusedStepIndex >= 0 && fusedStepIndex < pipeline.Steps.Count)
                            {
                                var fusedStep = pipeline.Steps[fusedStepIndex];
                                var fusedItems = targetItems.Where(i => i.Status == BatchItemStatus.Active).ToList();
                                log?.Report($"[Батч] Омен-сшивка × {fusedItems.Count}: стадия {fusedStepIndex} «{fusedStep.Name}»");
                                foreach (var item in fusedItems)
                                {
                                    executionCount++;
                                    ct.ThrowIfCancellationRequested();
                                    var screen = screenTemplate with { ItemArea = itemCells[item.CellIndex] };
                                    StepOutcome fusedOutcome;
                                    if (_testStepExecutor is not null)
                                        fusedOutcome = await _testStepExecutor(item, fusedStep, ct).ConfigureAwait(false);
                                    else
                                        fusedOutcome = await _runner.ExecuteStepAsync(fusedStep, screen, log, ct, cachedText: item.LastKnownText).ConfigureAwait(false);
                                    item.TotalAttempts += fusedOutcome.Attempts;
                                    if (fusedOutcome.Succeeded)
                                        AccumulateCost(item, fusedStep, fusedOutcome.Attempts);
                                    LogItemState(item.CellIndex, fusedStep.Name, fusedOutcome);
                                    UpdateItemTextCache(item, fusedStep);
                                    if (!string.IsNullOrWhiteSpace(fusedOutcome.FinalItemText))
                                        item.LastKnownText = fusedOutcome.FinalItemText;
                                    ApplyTransition(item, fusedStep, fusedOutcome.Succeeded, fusedStepIndex, pipeline.Steps.Count, log);
                                }
                            }
                        }

                        continue;
                    }

                    log?.Report($"[Батч] Стадия {n} «{step.Name}»: {targetItems.Count} предм.");

                    // Для ChaosCraft/DivineCraft: держим Shift+орб между предметами одной стадии.
                    // Первый предмет кликает орб; остальные просто ЛКМ по предмету — экономим N-1 кликов орба.
                    var batchOrbSelected = false;

                    try
                    {
                    foreach (var item in targetItems)
                    {
                        executionCount++;
                        ct.ThrowIfCancellationRequested();

                        var screen = screenTemplate with { ItemArea = itemCells[item.CellIndex] };

                        // Probe: используем межшаговый кэш если он актуален, иначе Ctrl+Alt+C.
                        // Кэш обновляется из FinalItemText каждого шага; инвалидируется после
                        // state-changing действий, не возвращающих текст предмета.
                        string? probeText = item.LastKnownText;
                        if (_chaos is not null && _testStepExecutor is null)
                        {
                            if (string.IsNullOrWhiteSpace(probeText))
                            {
                                probeText = await _chaos.ReadItemClipboardTextAsync(screen.ItemArea, log, ct).ConfigureAwait(false);
                                if (string.IsNullOrWhiteSpace(probeText))
                                {
                                    item.Status = BatchItemStatus.Failed;
                                    item.LastMessage = "Пустой буфер — ячейка пропущена";
                                    log?.Report($"[Батч] [{item.CellIndex}]: буфер пуст перед шагом «{step.Name}» — ячейка помечена как необрабатываемая.");
                                    continue;
                                }
                            }
                            else
                            {
                                log?.Report($"[Батч] [{item.CellIndex}]: используем кэш предмета (без Ctrl+Alt+C перед «{step.Name}»)");
                            }
                        }

                        bool isLastItem = item == targetItems[^1];
                        bool keepOrb = IsIterativeCurrencyStep(step) && !isLastItem;

                        StepOutcome outcome;
                        try
                        {
                            if (_testStepExecutor is not null)
                                outcome = await _testStepExecutor(item, step, ct).ConfigureAwait(false);
                            else
                                outcome = await _runner.ExecuteStepAsync(step, screen, log, ct,
                                    cachedText: probeText,
                                    orbAlreadySelected: batchOrbSelected,
                                    keepOrbSelected: keepOrb).ConfigureAwait(false);
                            batchOrbSelected = keepOrb;
                        }
                        catch
                        {
                            batchOrbSelected = false; // исключение: RunAsync уже отпустил Shift
                            throw;
                        }

                        item.TotalAttempts += outcome.Attempts;
                        if (outcome.Succeeded)
                            AccumulateCost(item, step, outcome.Attempts);
                        LogItemState(item.CellIndex, step.Name, outcome);
                        UpdateItemTextCache(item, step);
                        if (!string.IsNullOrWhiteSpace(outcome.FinalItemText))
                            item.LastKnownText = outcome.FinalItemText;
                        ApplyTransition(item, step, outcome.Succeeded, n, pipeline.Steps.Count, log);

                    }
                    } // foreach targetItems
                    finally
                    {
                        // Если орб остался выбранным (исключение посреди батча или последний предмет не дошёл до RunAsync),
                        // освобождаем Shift здесь. В штатном случае это no-op (RunAsync уже отпустил Shift).
                        if (batchOrbSelected)
                        {
                            Win32Input.ReleaseShift();
                            batchOrbSelected = false;
                        }
                    }
                }

                if (!anyProgress)
                {
                    log?.Report("[Батч] Нет прогресса — возможен deadlock. Остановлено.");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            log?.Report("[Батч] Остановлено пользователем — считаем накопленные расходы.");
        }
        finally
        {
            Win32Input.ReleaseShift();
            Win32Input.ReleaseCtrlAlt();
        }

        var doneCount   = items.Count(i => i.Status == BatchItemStatus.Done);
        var failedCount = items.Count(i => i.Status == BatchItemStatus.Failed);
        var totalCost   = items.Sum(i => i.TotalCostDiv);
        log?.Report($"[Батч] Итог: Done={doneCount}, Failed={failedCount}, Расход≈{totalCost:F2}d");
        return new BatchRunResult { Items = items, BatchId = batchId ?? "", PipelineName = pipeline.Name };
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

    private static void LogItemState(int cellIndex, string stepName, StepOutcome outcome)
    {
        var result = outcome.Succeeded ? "OK" : "FAIL";
        SessionLogger.WriteFileOnly($"[Батч] [{cellIndex}] «{stepName}» → {result}");
        if (!string.IsNullOrWhiteSpace(outcome.FinalItemText))
            SessionLogger.InfoClipboard($"Батч [{cellIndex}] после «{stepName}»", outcome.FinalItemText);
    }

    private static void AccumulateCost(BatchItem item, CraftPipelineStep step, int attempts)
    {
        var currencyName = ResolveStepCurrencyName(step);
        if (string.IsNullOrEmpty(currencyName)) return;

        var pricePerUnit = PoeNinjaPriceService.GetPrice(currencyName)?.DivineValue ?? 0m;
        var cost = pricePerUnit * attempts;
        item.TotalCostDiv += cost;
        item.CostRecords.Add(new BatchCostRecord
        {
            StepName     = step.Name,
            CurrencyName = currencyName,
            Attempts     = attempts,
            CostDiv      = cost,
        });
    }

    private static string? ResolveStepCurrencyName(CraftPipelineStep step) => step.Action switch
    {
        PipelineAction.SimpleCurrency  => step.CurrencyId,
        PipelineAction.SimpleChaos     => "Chaos Orb",
        PipelineAction.SimpleAnnul     => "Orb of Annulment",
        PipelineAction.SimpleExalt     => "Exalted Orb",
        PipelineAction.ChaosCraft      => "Chaos Orb",
        PipelineAction.AugAnnulCraft   => "Orb of Annulment",
        PipelineAction.ExaltCraft      => "Exalted Orb",
        PipelineAction.DivineCraft     => "Divine Orb",
        PipelineAction.OmenActivation  => step.OmenConfig?.OmenName,
        PipelineAction.DeliriumLiquid  => ResolveDeliriumName(step.DeliriumLiquidConfig?.LiquidName),
        PipelineAction.SimpleAbyssalBone => ResolveBoneName(step.AbyssalBoneId),
        _                              => null,
    };

    private static string? ResolveDeliriumName(string? liquidId)
    {
        if (string.IsNullOrEmpty(liquidId)) return null;
        var item = Services.StackableItemRegistry.Items
            .FirstOrDefault(i => i.Id == liquidId);
        return item?.DisplayName;
    }

    private static string? ResolveBoneName(string? boneId)
    {
        if (string.IsNullOrEmpty(boneId)) return null;
        // AbyssKnownItems живёт в MainWindow — используем нормализацию по Id
        // Id вида "ancient_jawbone" → "Ancient Jawbone"
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(boneId.Replace('_', ' '));
    }

    /// <summary>
    /// Инвалидирует <see cref="BatchItem.LastKnownText"/> если действие меняет предмет
    /// но не возвращает FinalItemText в StepOutcome.
    /// Вызывать ДО присвоения outcome.FinalItemText — тогда FinalItemText перезапишет null.
    /// </summary>
    private static void UpdateItemTextCache(BatchItem item, CraftPipelineStep step)
    {
        // Эти действия меняют предмет, но возвращают Success без FinalItemText.
        // Без явной инвалидации кэш будет содержать устаревший текст.
        if (step.Action is PipelineAction.DeliriumLiquid
            or PipelineAction.SimpleAnnul
            or PipelineAction.SimpleChaos
            or PipelineAction.SimpleExalt
            or PipelineAction.SimpleCurrency
            or PipelineAction.SimpleAbyssalBone
            or PipelineAction.CtrlClickItem
            or PipelineAction.DesecrateReveal
            or PipelineAction.DesecratePick
            or PipelineAction.ManualPause)
        {
            item.LastKnownText = null;
        }
    }

    /// <summary>
    /// Веха: выполняется один раз для всей группы предметов, достигших этой стадии.
    /// Не требует итерации по предметам — не зависит от конкретного предмета.
    /// </summary>
    private static bool IsMilestoneAction(CraftPipelineStep step) =>
        step.Action == PipelineAction.TravelToLocation
        || step.Action == PipelineAction.WalkToPosition
        || step.Action == PipelineAction.OpenStash
        || step.Action == PipelineAction.ClickTemplate
        || step.Action == PipelineAction.OmenActivation;

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
                item.Status = BatchItemStatus.Failed;
                item.LastMessage = "Пустой буфер — ячейка пропущена";
                log?.Report($"[Батч] [{item.CellIndex}]: буфер пуст — ячейка помечена как необрабатываемая.");
                continue;
            }
            var parsed = ItemParser.Parse(text);
            var detect = PipelineStepDetector.Detect(pipeline, parsed);
            item.CurrentStage = detect.StepIndex ?? 0;
            log?.Report($"[Батч] [{item.CellIndex}]: стадия {item.CurrentStage} «{pipeline.Steps[item.CurrentStage].Name}»");
        }
    }

    /// <summary>
    /// OCR-детектирует текущую локацию и продвигает активные предметы через уже пройденные вехи TravelToLocation.
    /// Полезно при старте батча в середине пайплайна: если мы уже в Well of Souls, предметы
    /// на стадиях до этой вехи продвигаются на стадию после неё.
    /// </summary>
    private static async Task AdjustStagesForCurrentLocationAsync(
        List<BatchItem> items, CraftPipeline pipeline,
        ScreenRect locationNameArea, IProgress<string>? log, CancellationToken ct)
    {
        if (locationNameArea.Width <= 0 || locationNameArea.Height <= 0) return;

        // Собираем TravelToLocation-шаги с заданной локацией
        var travelSteps = pipeline.Steps
            .Select((s, i) => (step: s, index: i))
            .Where(t => t.step.Action == PipelineAction.TravelToLocation
                        && !string.IsNullOrEmpty(t.step.TravelConfig?.ExpectedLocation))
            .ToList();

        if (travelSteps.Count == 0) return;

        ct.ThrowIfCancellationRequested();
        var detected = await LocationDetector.DetectAsync(locationNameArea, ct).ConfigureAwait(false);
        log?.Report($"[Батч] Текущая локация OCR: «{detected}»");

        // Ищем совпадение с одним из TravelToLocation-шагов
        var matchedTravel = travelSteps.FirstOrDefault(
            t => LocationDetector.LocationMatchesExpected(detected, t.step.TravelConfig!.ExpectedLocation));

        if (matchedTravel.step is null)
        {
            log?.Report("[Батч] Локация не совпадает ни с одной вехой — стадии не корректируются.");
            return;
        }

        var milestoneIndex = matchedTravel.index;
        log?.Report($"[Батч] Локация совпадает с вехой {milestoneIndex} «{matchedTravel.step.Name}». Предметы до этой вехи продвигаются на стадию {milestoneIndex + 1}.");

        foreach (var item in items.Where(i => i.Status == BatchItemStatus.Active && i.CurrentStage <= milestoneIndex))
        {
            log?.Report($"[Батч] [{item.CellIndex}]: стадия {item.CurrentStage} → {milestoneIndex + 1} (уже в локации)");
            item.CurrentStage = milestoneIndex + 1;
        }
    }

    /// <summary>
    /// Шаги с итеративной орб-механикой (Shift+RMB на орбе, затем ЛКМ по предметам).
    /// Для них батч удерживает Shift между предметами одной стадии.
    /// </summary>
    private static bool IsIterativeCurrencyStep(CraftPipelineStep step) =>
        step.Action is PipelineAction.ChaosCraft or PipelineAction.DivineCraft;
}
