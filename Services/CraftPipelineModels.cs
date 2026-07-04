namespace GameHelper.Services;

public enum PipelineAction
{
    /// <summary>Читает буфер (Ctrl+Alt+C) и проверяет <see cref="CraftPipelineStep.EntryCondition"/>. Без кликов.</summary>
    CheckItem,
    ChaosCraft,
    AugAnnulCraft,
    DivineCraft,
    ExaltCraft,
    OmenActivation,
    /// <summary>Показывает сообщение пользователю и ждёт нажатия «Продолжить».</summary>
    ManualPause,
}

public enum TransitionTarget
{
    /// <summary>Перейти к следующему шагу по порядку.</summary>
    Next,
    /// <summary>Пайплайн завершён успешно.</summary>
    Done,
    /// <summary>Пайплайн прерван (ошибка или условие не выполнено).</summary>
    Abort,
    /// <summary>Перейти к шагу с индексом <see cref="PipelineTransition.StepIndex"/>.</summary>
    Step,
}

public sealed class PipelineTransition
{
    public TransitionTarget Target { get; set; } = TransitionTarget.Next;

    /// <summary>Индекс шага для перехода, когда <see cref="Target"/> == <see cref="TransitionTarget.Step"/>.</summary>
    public int StepIndex { get; set; }

    /// <summary>Сообщение, показываемое пользователю при <see cref="TransitionTarget.Done"/> или <see cref="TransitionTarget.Abort"/>.</summary>
    public string Message { get; set; } = "";
}

public sealed class OmenActionConfig
{
    public string OmenName { get; set; } = OmenActivationService.OmenSinistralExaltationName;
    /// <summary>Индекс ячейки омена в списке ячеек стэша для данного типа омена (0-based).</summary>
    public int StashCellIndex { get; set; }
    /// <summary>Строка ячейки назначения в инвентаре (0-based).</summary>
    public int InventoryRow { get; set; }
    /// <summary>Столбец ячейки назначения в инвентаре (0-based).</summary>
    public int InventoryCol { get; set; }
}

public sealed class CraftPipelineStep
{
    public string Name { get; set; } = "";
    public PipelineAction Action { get; set; }

    /// <summary>Параметры омена — используется только при <see cref="PipelineAction.OmenActivation"/>.</summary>
    public OmenActionConfig? OmenConfig { get; set; }

    /// <summary>Проверяется до выполнения действия. Если false → переход по <see cref="OnFailure"/>.</summary>
    public CraftConditionPlan? EntryCondition { get; set; }

    /// <summary>Действие повторяется, пока это условие не выполнено или не исчерпан <see cref="MaxIterations"/>.</summary>
    public CraftConditionPlan? LoopUntil { get; set; }

    /// <summary>Максимальное число итераций цикла. Защита от бесконечного повтора.</summary>
    public int MaxIterations { get; set; } = 100;

    public PipelineTransition OnSuccess { get; set; } = new() { Target = TransitionTarget.Next };
    public PipelineTransition OnFailure { get; set; } = new() { Target = TransitionTarget.Abort };
}

public sealed class CraftPipeline
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public List<CraftPipelineStep> Steps { get; set; } = new();
}
