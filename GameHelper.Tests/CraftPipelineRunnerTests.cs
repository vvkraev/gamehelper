using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Тесты логики переходов CraftPipelineRunner.
/// Реальные сервисы крафта не нужны: _testStepExecutor управляет исходом каждого шага.
/// </summary>
public sealed class CraftPipelineRunnerTests
{
    // ── Фикстуры ─────────────────────────────────────────────────────────────

    private static CraftPipelineRunner MakeRunner(
        Func<CraftPipelineStep, CancellationToken, Task<StepOutcome>>? stepExecutor = null)
    {
        var runner = new CraftPipelineRunner();
        runner._testStepExecutor = stepExecutor;
        return runner;
    }

    private static CraftPipeline Pipeline(params CraftPipelineStep[] steps) =>
        new() { Name = "Test", Steps = new List<CraftPipelineStep>(steps) };

    private static CraftPipelineStep Step(
        string name,
        TransitionTarget onSuccess = TransitionTarget.Done,
        TransitionTarget onFailure = TransitionTarget.Abort,
        int successStepIdx = 0,
        int failureStepIdx = 0,
        string successMsg = "",
        string failureMsg = "",
        PipelineAction action = PipelineAction.ManualPause) =>
        new()
        {
            Name = name,
            Action = action,
            OnSuccess = new PipelineTransition { Target = onSuccess, StepIndex = successStepIdx, Message = successMsg },
            OnFailure = new PipelineTransition { Target = onFailure, StepIndex = failureStepIdx, Message = failureMsg },
        };

    private static Task<PipelineRunResult> Run(CraftPipelineRunner runner, CraftPipeline pipeline, CancellationToken ct = default) =>
        runner.RunAsync(pipeline, PipelineScreenConfig.Empty, null, ct);

    // Исход шага: всегда успех
    private static Task<StepOutcome> AlwaysSuccess(CraftPipelineStep _, CancellationToken __) =>
        Task.FromResult(StepOutcome.Success());

    // Исход шага: всегда неудача
    private static Task<StepOutcome> AlwaysFailure(CraftPipelineStep _, CancellationToken __) =>
        Task.FromResult(StepOutcome.Failure());

    // ── Базовые случаи ────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyPipeline_ReturnsDone()
    {
        var runner = MakeRunner();
        var result = await Run(runner, Pipeline());
        Assert.Equal(PipelineRunStatus.Done, result.Status);
    }

    [Fact]
    public async Task SingleStep_SuccessWithDoneTransition_ReturnsDone()
    {
        var runner = MakeRunner(AlwaysSuccess);
        var result = await Run(runner, Pipeline(Step("A", onSuccess: TransitionTarget.Done)));
        Assert.Equal(PipelineRunStatus.Done, result.Status);
    }

    [Fact]
    public async Task SingleStep_FailureWithAbortTransition_ReturnsAborted()
    {
        var runner = MakeRunner(AlwaysFailure);
        var result = await Run(runner, Pipeline(Step("A", onFailure: TransitionTarget.Abort)));
        Assert.Equal(PipelineRunStatus.Aborted, result.Status);
    }

    [Fact]
    public async Task SingleStep_SuccessWithAbortTransition_ReturnsAborted()
    {
        var runner = MakeRunner(AlwaysSuccess);
        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Abort, failureMsg: "msg", successMsg: "Сюда")));
        Assert.Equal(PipelineRunStatus.Aborted, result.Status);
        Assert.Equal("Сюда", result.Message);
    }

    // ── Переход Next ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoSteps_NextThenDone_BothExecuted()
    {
        var executed = new List<string>();
        var runner = MakeRunner((step, _) =>
        {
            executed.Add(step.Name);
            return Task.FromResult(StepOutcome.Success());
        });

        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Next),
            Step("B", onSuccess: TransitionTarget.Done)));

        Assert.Equal(PipelineRunStatus.Done, result.Status);
        Assert.Equal(new[] { "A", "B" }, executed);
    }

    [Fact]
    public async Task NextAfterLastStep_ReturnsDone()
    {
        var runner = MakeRunner(AlwaysSuccess);
        // Единственный шаг с Next — исчерпание списка → Done
        var result = await Run(runner, Pipeline(Step("Only", onSuccess: TransitionTarget.Next)));
        Assert.Equal(PipelineRunStatus.Done, result.Status);
    }

    // ── Переход Step (прыжок) ─────────────────────────────────────────────────

    [Fact]
    public async Task StepTransition_JumpsToTargetIndex_SkipsIntermediateStep()
    {
        var executed = new List<string>();
        var runner = MakeRunner((step, _) =>
        {
            executed.Add(step.Name);
            return Task.FromResult(StepOutcome.Success());
        });

        // step0 → Step(2), step1 пропускается, step2 → Done
        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Step, successStepIdx: 2),
            Step("B", onSuccess: TransitionTarget.Done), // должен быть пропущен
            Step("C", onSuccess: TransitionTarget.Done)));

        Assert.Equal(PipelineRunStatus.Done, result.Status);
        Assert.Equal(new[] { "A", "C" }, executed);
    }

    [Fact]
    public async Task StepTransition_IndexOutOfRange_ReturnsError()
    {
        var runner = MakeRunner(AlwaysSuccess);
        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Step, successStepIdx: 99)));
        Assert.Equal(PipelineRunStatus.Error, result.Status);
    }

    [Fact]
    public async Task FailureStep_NegativeIndex_ReturnsError()
    {
        var runner = MakeRunner(AlwaysFailure);
        var result = await Run(runner, Pipeline(
            Step("A", onFailure: TransitionTarget.Step, failureStepIdx: -1)));
        Assert.Equal(PipelineRunStatus.Error, result.Status);
    }

    // ── Сообщения переходов ───────────────────────────────────────────────────

    [Fact]
    public async Task CustomDoneMessage_PreservedInResult()
    {
        var runner = MakeRunner(AlwaysSuccess);
        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Done, successMsg: "Готово: предмет найден")));
        Assert.Equal("Готово: предмет найден", result.Message);
    }

    [Fact]
    public async Task CustomAbortMessage_PreservedInResult()
    {
        var runner = MakeRunner(AlwaysFailure);
        var result = await Run(runner, Pipeline(
            Step("A", onFailure: TransitionTarget.Abort, failureMsg: "Кончились орбы")));
        Assert.Equal("Кончились орбы", result.Message);
    }

    // ── Накопление статистики ─────────────────────────────────────────────────

    [Fact]
    public async Task TotalAttempts_AccumulatedAcrossSteps()
    {
        var runner = MakeRunner((step, _) =>
        {
            var attempts = step.Name == "A" ? 3 : 7;
            return Task.FromResult(StepOutcome.Success(attempts));
        });

        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Next),
            Step("B", onSuccess: TransitionTarget.Done)));

        Assert.Equal(10, result.TotalAttempts);
    }

    [Fact]
    public async Task FinalItemText_PreservedFromLastSettingStep()
    {
        var runner = MakeRunner((step, _) =>
        {
            var item = step.Name == "B" ? "Item: Sword" : null;
            return Task.FromResult(StepOutcome.Success(0, item));
        });

        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Next),
            Step("B", onSuccess: TransitionTarget.Done)));

        Assert.Equal("Item: Sword", result.FinalItemText);
    }

    [Fact]
    public async Task FinalItemText_OverwrittenByLaterStep()
    {
        var runner = MakeRunner((step, _) =>
        {
            var item = step.Name switch
            {
                "A" => "Item from A",
                "B" => "Item from B",
                _ => null,
            };
            return Task.FromResult(StepOutcome.Success(0, item));
        });

        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Next),
            Step("B", onSuccess: TransitionTarget.Done)));

        Assert.Equal("Item from B", result.FinalItemText);
    }

    // ── Отмена ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelledToken_BeforeFirstStep_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var runner = MakeRunner(AlwaysSuccess);
        var result = await Run(runner, Pipeline(Step("A")), cts.Token);

        Assert.Equal(PipelineRunStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task CancelledDuringStep_ReturnsCancelled()
    {
        var runner = MakeRunner((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(StepOutcome.Success());
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Run(runner, Pipeline(Step("A", onSuccess: TransitionTarget.Done)), cts.Token);
        Assert.Equal(PipelineRunStatus.Cancelled, result.Status);
    }

    // ── Защита от бесконечных циклов ─────────────────────────────────────────

    [Fact]
    public async Task InfiniteLoop_ExceedsMaxGlobalExecutions_ReturnsError()
    {
        // step0 → Step(0) всегда (бесконечный цикл)
        var runner = MakeRunner(AlwaysSuccess);
        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Step, successStepIdx: 0)));

        Assert.Equal(PipelineRunStatus.Error, result.Status);
        Assert.Contains(CraftPipelineRunner.MaxGlobalStepExecutions.ToString(), result.Message);
    }

    // ── ManualPause без тест-хука ─────────────────────────────────────────────

    [Fact]
    public async Task ManualPause_WithoutTestHook_ReturnsSuccessAndFollowsOnSuccess()
    {
        // ManualPause не требует сервисов — проверяем реальный диспетч
        var runner = new CraftPipelineRunner(); // без _testStepExecutor
        var result = await runner.RunAsync(
            Pipeline(new CraftPipelineStep
            {
                Name = "Pause",
                Action = PipelineAction.ManualPause,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Done, Message = "Продолжено" },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Abort },
            }),
            PipelineScreenConfig.Empty, null, default);

        Assert.Equal(PipelineRunStatus.Done, result.Status);
        Assert.Equal("Продолжено", result.Message);
    }

    // ── Сложный сценарий: петля с выходом ─────────────────────────────────────

    [Fact]
    public async Task LoopWithEventualSuccess_RunsMultipleTimes_ThenDone()
    {
        // step0: неудача × 3, потом успех → Done
        var callCount = 0;
        var runner = MakeRunner((_, _) =>
        {
            var succeeded = ++callCount > 3;
            return Task.FromResult(succeeded ? StepOutcome.Success() : StepOutcome.Failure());
        });

        // Неудача → Step(0) (повтор), Успех → Done
        var result = await Run(runner, Pipeline(
            new CraftPipelineStep
            {
                Name = "Try",
                Action = PipelineAction.ManualPause,
                OnSuccess = new PipelineTransition { Target = TransitionTarget.Done },
                OnFailure = new PipelineTransition { Target = TransitionTarget.Step, StepIndex = 0 },
            }));

        Assert.Equal(PipelineRunStatus.Done, result.Status);
        Assert.Equal(4, callCount); // 3 неудачи + 1 успех
    }

    // ── Несколько шагов с разными результатами ────────────────────────────────

    [Fact]
    public async Task ThreeStepPipeline_MiddleStepFails_AbortsEarly()
    {
        var executed = new List<string>();
        var runner = MakeRunner((step, _) =>
        {
            executed.Add(step.Name);
            var success = step.Name != "B"; // B always fails
            return Task.FromResult(success ? StepOutcome.Success() : StepOutcome.Failure());
        });

        var result = await Run(runner, Pipeline(
            Step("A", onSuccess: TransitionTarget.Next),
            Step("B", onFailure: TransitionTarget.Abort),
            Step("C", onSuccess: TransitionTarget.Done)));

        Assert.Equal(PipelineRunStatus.Aborted, result.Status);
        Assert.Equal(new[] { "A", "B" }, executed); // C не выполнялся
    }
}
