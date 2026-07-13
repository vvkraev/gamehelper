using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class BatchPipelineRunnerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CraftPipeline BuildPipeline(params CraftPipelineStep[] steps)
    {
        var p = new CraftPipeline { Name = "Test", ItemClass = "Jewels" };
        p.Steps.AddRange(steps);
        return p;
    }

    private static CraftPipelineStep Step(string name,
        TransitionTarget onSuccess = TransitionTarget.Done,
        TransitionTarget onFailure = TransitionTarget.Abort,
        int onSuccessStep = 0, int onFailureStep = 0)
    {
        return new CraftPipelineStep
        {
            Name = name,
            Action = PipelineAction.CheckItem,
            OnSuccess = new PipelineTransition { Target = onSuccess, StepIndex = onSuccessStep },
            OnFailure = new PipelineTransition { Target = onFailure, StepIndex = onFailureStep },
        };
    }

    private static BatchPipelineRunner BuildRunner(
        Func<BatchItem, CraftPipelineStep, CancellationToken, Task<StepOutcome>> executor)
    {
        var runner = new BatchPipelineRunner(new CraftPipelineRunner());
        runner._testStepExecutor = executor;
        return runner;
    }

    private static IReadOnlyList<ScreenRect> Cells(int count) =>
        Enumerable.Repeat(default(ScreenRect), count).ToArray();

    private Task<BatchRunResult> RunCore(
        BatchPipelineRunner runner, CraftPipeline pipeline, List<BatchItem> items) =>
        runner.RunCoreAsync(pipeline, PipelineScreenConfig.Empty, Cells(items.Count), items, null, CancellationToken.None);

    // ── Базовые сценарии ──────────────────────────────────────────────────────

    [Fact]
    public async Task SingleItem_SingleStep_Done()
    {
        var pipeline = BuildPipeline(Step("A", onSuccess: TransitionTarget.Done));
        var runner = BuildRunner((_, _, _) => Task.FromResult(StepOutcome.Success()));

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(1), null, CancellationToken.None);

        Assert.Equal(1, result.DoneCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task SingleItem_Fails_StatusFailed()
    {
        var pipeline = BuildPipeline(Step("A", onSuccess: TransitionTarget.Done, onFailure: TransitionTarget.Abort));
        var runner = BuildRunner((_, _, _) => Task.FromResult(StepOutcome.Failure()));

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(1), null, CancellationToken.None);

        Assert.Equal(0, result.DoneCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public async Task ThreeItems_TwoSteps_AllDone()
    {
        var pipeline = BuildPipeline(
            Step("A", onSuccess: TransitionTarget.Next),
            Step("B", onSuccess: TransitionTarget.Done));
        var runner = BuildRunner((_, _, _) => Task.FromResult(StepOutcome.Success()));

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(3), null, CancellationToken.None);

        Assert.Equal(3, result.DoneCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task OneItemFails_OtherContinuesToDone()
    {
        var pipeline = BuildPipeline(Step("A", onSuccess: TransitionTarget.Done, onFailure: TransitionTarget.Abort));
        var runner = BuildRunner((item, _, _) =>
            Task.FromResult(item.CellIndex == 0 ? StepOutcome.Failure() : StepOutcome.Success()));

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(2), null, CancellationToken.None);

        Assert.Equal(1, result.DoneCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public async Task EmptyPipeline_ReturnsEmptyResult()
    {
        var pipeline = BuildPipeline();
        var runner = BuildRunner((_, _, _) => Task.FromResult(StepOutcome.Success()));

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(2), null, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ── Барьерная синхронизация ───────────────────────────────────────────────

    [Fact]
    public async Task Barrier_Stage1WaitsForSlowerItem()
    {
        // item[0] стартует на стадии 1, item[1] — на стадии 0
        // ожидание: item[1] должен пройти стадию 0 ДО того как item[0] пройдёт стадию 1
        var pipeline = BuildPipeline(
            Step("Stage0", onSuccess: TransitionTarget.Next),
            Step("Stage1", onSuccess: TransitionTarget.Done));

        var callOrder = new List<(int cell, string step)>();
        var runner = BuildRunner((item, step, _) =>
        {
            callOrder.Add((item.CellIndex, step.Name));
            return Task.FromResult(StepOutcome.Success());
        });

        var items = new List<BatchItem>
        {
            new() { CellIndex = 0, CurrentStage = 1 },
            new() { CellIndex = 1, CurrentStage = 0 },
        };
        await RunCore(runner, pipeline, items);

        Assert.Equal(2, items.Count(i => i.Status == BatchItemStatus.Done));

        var idx1At0 = callOrder.FindIndex(x => x.cell == 1 && x.step == "Stage0");
        var idx0At1 = callOrder.FindIndex(x => x.cell == 0 && x.step == "Stage1");
        Assert.True(idx1At0 < idx0At1, "item[1]@Stage0 должен быть раньше item[0]@Stage1");
    }

    [Fact]
    public async Task Barrier_AllItemsProcessedTogether_AtSameStage()
    {
        // Оба предмета стартуют на стадии 0. На стадии 1 должны быть обработаны вместе.
        var pipeline = BuildPipeline(
            Step("Stage0", onSuccess: TransitionTarget.Next),
            Step("Stage1", onSuccess: TransitionTarget.Done));

        var stage1Calls = new List<int>();
        var runner = BuildRunner((item, step, _) =>
        {
            if (step.Name == "Stage1") stage1Calls.Add(item.CellIndex);
            return Task.FromResult(StepOutcome.Success());
        });

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(3), null, CancellationToken.None);

        Assert.Equal(3, result.DoneCount);
        // Все три предмета прошли Stage1
        Assert.Equal(3, stage1Calls.Count);
        Assert.Contains(0, stage1Calls);
        Assert.Contains(1, stage1Calls);
        Assert.Contains(2, stage1Calls);
    }

    // ── Обратные переходы ─────────────────────────────────────────────────────

    [Fact]
    public async Task BackwardTransition_ItemRetries_ThenDone()
    {
        // Пайплайн: 0→Next→1(onFailure=Step(0))→Done
        var pipeline = BuildPipeline(
            Step("Prep", onSuccess: TransitionTarget.Next),
            new CraftPipelineStep
            {
                Name = "Exalt",
                Action = PipelineAction.CheckItem,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Done },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 0 },
            });

        var exaltAttempts = 0;
        var runner = BuildRunner((item, step, _) =>
        {
            if (step.Name != "Exalt") return Task.FromResult(StepOutcome.Success());
            exaltAttempts++;
            return Task.FromResult(exaltAttempts >= 2 ? StepOutcome.Success() : StepOutcome.Failure());
        });

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(1), null, CancellationToken.None);

        Assert.Equal(1, result.DoneCount);
        Assert.Equal(2, exaltAttempts);
    }

    [Fact]
    public async Task BackwardTransition_TwoItems_BothEventuallyDone()
    {
        // Пайплайн: 0(Prep)→1(Exalt)→2(CheckSuffix): ok=Step(4)/fail=Step(3)→3(AugAnnul)→Step(1)→4(Finish)→Done
        // item[0]: CheckSuffix провал → AugAnnul → снова Exalt → CheckSuffix успех → Finish
        // item[1]: CheckSuffix успех с первого раза → Finish
        // Барьер задерживает item[1] на Finish (ст.4) пока item[0] не догонит через AugAnnul
        var pipeline = BuildPipeline(
            Step("Prep",   onSuccess: TransitionTarget.Next),
            Step("Exalt",  onSuccess: TransitionTarget.Next),
            new CraftPipelineStep
            {
                Name = "CheckSuffix",
                Action = PipelineAction.CheckItem,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 4 },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 3 },
            },
            new CraftPipelineStep
            {
                Name = "AugAnnul",
                Action = PipelineAction.CheckItem,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 1 },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Abort },
            },
            Step("Finish", onSuccess: TransitionTarget.Done));

        var checkCalls = new Dictionary<int, int> { [0] = 0, [1] = 0 };
        var runner = BuildRunner((item, step, _) =>
        {
            if (step.Name == "CheckSuffix")
            {
                checkCalls[item.CellIndex]++;
                bool ok = item.CellIndex != 0 || checkCalls[0] > 1;
                return Task.FromResult(ok ? StepOutcome.Success() : StepOutcome.Failure());
            }
            return Task.FromResult(StepOutcome.Success());
        });

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(2), null, CancellationToken.None);

        Assert.Equal(2, result.DoneCount);
        Assert.Equal(2, checkCalls[0]); // item[0]: провал + успех
        Assert.Equal(1, checkCalls[1]); // item[1]: только успех
    }

    // ── Атрибуты предметов ────────────────────────────────────────────────────

    [Fact]
    public async Task TotalAttempts_AccumulatedCorrectly()
    {
        var pipeline = BuildPipeline(Step("A", onSuccess: TransitionTarget.Done));
        var runner = BuildRunner((_, _, _) => Task.FromResult(new StepOutcome(true, Attempts: 5)));

        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(2), null, CancellationToken.None);

        Assert.All(result.Items, item => Assert.Equal(5, item.TotalAttempts));
    }

    // ── OmenActivation: батчевая сшивка ──────────────────────────────────────

    private static CraftPipelineStep OmenStep(string name,
        TransitionTarget onSuccess = TransitionTarget.Next,
        TransitionTarget onFailure = TransitionTarget.Abort)
    {
        return new CraftPipelineStep
        {
            Name  = name,
            Action = PipelineAction.OmenActivation,
            OnSuccess = new PipelineTransition { Target = onSuccess },
            OnFailure = new PipelineTransition { Target = onFailure },
        };
    }

    /// <summary>
    /// OmenActivation — это веха: запускается один раз для всего батча (qty=N через клавиатуру),
    /// затем следующий шаг выполняется per-item (fused phase).
    /// Порядок: Omen[0] (один раз для item[0]), затем Exalt[0..2].
    /// </summary>
    [Fact]
    public async Task OmenActivation_BatchFused_AllItemsGetNextStep_WhenOmenSucceeds()
    {
        var log = new List<string>();
        var pipeline = BuildPipeline(
            OmenStep("Omen", onSuccess: TransitionTarget.Next),
            Step("Exalt", onSuccess: TransitionTarget.Done));

        var runner = BuildRunner((item, step, _) =>
        {
            log.Add($"{step.Name}[{item.CellIndex}]");
            return Task.FromResult(StepOutcome.Success());
        });

        var items = new List<BatchItem>
        {
            new() { CellIndex = 0 },
            new() { CellIndex = 1 },
            new() { CellIndex = 2 },
        };
        await RunCore(runner, pipeline, items);

        // Веха: Omen вызывается один раз (с item[0] как референс); Exalt — для всех
        Assert.Equal(["Omen[0]", "Exalt[0]", "Exalt[1]", "Exalt[2]"], log);
        Assert.All(items, i => Assert.Equal(BatchItemStatus.Done, i.Status));
    }

    /// <summary>
    /// OmenActivation провалилась (веха) → ни один предмет не получает fused-шаг;
    /// все предметы переходят в Failed по OnFailure=Abort.
    /// </summary>
    [Fact]
    public async Task OmenActivation_BatchFused_NoItemsGetNextStep_WhenOmenFails()
    {
        var log = new List<string>();
        var pipeline = BuildPipeline(
            OmenStep("Omen", onSuccess: TransitionTarget.Next, onFailure: TransitionTarget.Abort),
            Step("Exalt", onSuccess: TransitionTarget.Done));

        // Веха проваливается (всегда — для всего батча)
        var runner = BuildRunner((item, step, _) =>
        {
            log.Add($"{step.Name}[{item.CellIndex}]");
            if (step.Action == PipelineAction.OmenActivation)
                return Task.FromResult(StepOutcome.Failure());
            return Task.FromResult(StepOutcome.Success());
        });

        var items = new List<BatchItem>
        {
            new() { CellIndex = 0 },
            new() { CellIndex = 1 },
        };
        await RunCore(runner, pipeline, items);

        // Omen[0] — один вызов вехи; Exalt не запускается ни для кого
        Assert.Equal(["Omen[0]"], log);
        Assert.All(items, i => Assert.Equal(BatchItemStatus.Failed, i.Status));
    }

    // ── Отмена ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_ReturnsResultWithoutThrowing()
    {
        // RunCoreAsync перехватывает OperationCanceledException и завершается нормально
        // (чтобы аккумулировать накопленные расходы перед выходом).
        var pipeline = BuildPipeline(Step("A", onSuccess: TransitionTarget.Done));
        var cts = new CancellationTokenSource();

        var runner = BuildRunner((_, _, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(StepOutcome.Success());
        });

        // Не бросает исключение; возвращает результат с незавершёнными предметами
        var result = await runner.RunAsync(pipeline, PipelineScreenConfig.Empty, Cells(1), null, cts.Token);
        Assert.Equal(0, result.DoneCount);
    }
}
