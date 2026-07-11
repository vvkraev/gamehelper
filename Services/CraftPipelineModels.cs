namespace GameHelper.Services;

public enum PipelineAction
{
    /// <summary>Читает буфер (Ctrl+Alt+C) и проверяет <see cref="CraftPipelineStep.EntryCondition"/>. Без кликов.</summary>
    CheckItem,
    ChaosCraft,
    AugAnnulCraft,
    DivineCraft,
    ExaltCraft,
    /// <summary>Применяет один Exalted Orb к предмету. Устаревший — используйте <see cref="SimpleCurrency"/>.</summary>
    SimpleExalt,
    /// <summary>Применяет один Annulment Orb к предмету. Устаревший — используйте <see cref="SimpleCurrency"/>.</summary>
    SimpleAnnul,
    /// <summary>Применяет один Chaos Orb к предмету. Устаревший — используйте <see cref="SimpleCurrency"/>.</summary>
    SimpleChaos,
    /// <summary>ПКМ на орб из вкладки Currency стэша (выбирается в настройках шага), ЛКМ на предмет.</summary>
    SimpleCurrency,
    OmenActivation,
    /// <summary>ПКМ на масло делириума из стэша, затем ЛКМ на предмет.</summary>
    DeliriumLiquid,
    /// <summary>Показывает сообщение пользователю и ждёт нажатия «Продолжить».</summary>
    ManualPause,
    /// <summary>
    /// Переход между локациями: ищет «Waypoint» через OCR в заданной области → клик,
    /// задержка, клик по кнопке перехода (фиксированные координаты), задержка загрузки.
    /// </summary>
    TravelToLocation,
    /// <summary>ПКМ на Abyssal Bone из вкладки Abyss стэша, ЛКМ на предмет.</summary>
    SimpleAbyssalBone,
    /// <summary>
    /// Ходьба внутри текущей локации: Escape (опц.) → ПКМ на координаты → задержка прибытия.
    /// Веха (milestone): выполняется один раз для всего батча.
    /// </summary>
    WalkToPosition,
    /// <summary>
    /// Открыть стэш через OCR: ищет надпись (StashOcrText из настроек) в заданной области,
    /// кликает по ней и ждёт открытия. Веха (milestone): выполняется один раз для всего батча.
    /// </summary>
    OpenStash,
    /// <summary>
    /// Ищет PNG-шаблон в заданной области экрана методом попиксельного сравнения,
    /// кликает по центру совпадения. Используется для кнопок без текста (иконки, кастомный UI).
    /// </summary>
    ClickTemplate,
    /// <summary>
    /// OCR читает 3 открытых мода из интерфейса reveal, сопоставляет с пулом десекрейт,
    /// логирует результат. Success — если среди мода десекрейт совпал с желаемым паттерном;
    /// Failure — если нет (для повтора reveal). При Success кликает по найденному моду.
    /// </summary>
    DesecratePick,
    /// <summary>
    /// Ctrl+ЛКМ по центру области предмета (<c>screen.ItemArea</c>).
    /// Перемещает предмет в/из reveal-слота или другую область инвентаря.
    /// </summary>
    CtrlClickItem,
    /// <summary>
    /// ЛКМ (или Ctrl+ЛКМ) по центру настраиваемой области экрана.
    /// Используется для нажатия кнопок Reveal, Reroll и аналогичных статичных элементов UI.
    /// </summary>
    ClickRegion,
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

public sealed class DeliriumLiquidActionConfig
{
    /// <summary>Идентификатор масла делириума — ключ в <c>AppSettings.DeliriumItemRegions</c>.</summary>
    public string LiquidName { get; set; } = "";
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

/// <summary>Конфигурация перехода между локациями через Waypoint.</summary>
public sealed class TravelActionConfig
{
    /// <summary>Область экрана, в которой ищем надпись «Waypoint» через OCR.</summary>
    public ScreenRect WaypointSearchArea { get; set; }

    /// <summary>Область кнопки перехода к нужной локации — клик по центру.</summary>
    public ScreenRect LocationButtonArea { get; set; }

    /// <summary>
    /// Текст, который OCR ищет в <see cref="WaypointSearchArea"/> (нормализованный, без учёта регистра).
    /// По умолчанию «waypoint». Изменить если в игре другая надпись.
    /// </summary>
    public string WaypointOcrText { get; set; } = "waypoint";

    /// <summary>Задержка после клика по Waypoint (мс): ждём открытия меню.</summary>
    public int AfterWaypointDelayMs { get; set; } = 10000;

    /// <summary>Задержка после клика по кнопке локации (мс): ждём загрузки карты.</summary>
    public int LoadingDelayMs { get; set; } = 15000;

    /// <summary>
    /// Ожидаемое название локации после перехода.
    /// Проверяется через OCR области <see cref="PipelineScreenConfig.LocationNameArea"/> с повторными попытками.
    /// Пустая строка — верификация не выполняется.
    /// </summary>
    public string ExpectedLocation { get; set; } = "";
}

/// <summary>
/// Один шаг WASD-движения: зажать одну или несколько клавиш <b>одновременно</b> на заданное время.
/// Несколько записей в <see cref="WalkToPositionConfig.KeyPresses"/> выполняются последовательно.
/// Для диагонального движения укажите несколько клавиш: ["W","D"].
/// </summary>
public sealed class WalkKeyPress
{
    /// <summary>Клавиши, зажимаемые одновременно, например ["W","D"] для движения по диагонали.</summary>
    public List<string> Keys { get; set; } = new() { "S" };

    /// <summary>Сколько миллисекунд держать все клавиши нажатыми.</summary>
    public int DurationMs { get; set; } = 2000;
}

/// <summary>Конфигурация шага reveal-десекрейт для <see cref="PipelineAction.DesecratePick"/>.</summary>
public sealed class DesecratePickConfig
{
    /// <summary>Область экрана где отображаются 3 открытых мода.</summary>
    public ScreenRect RevealArea { get; set; }

    /// <summary>Название предмета для логирования (например «Time-Lost Sapphire»).</summary>
    public string ItemName { get; set; } = "";

    /// <summary>Класс предмета для подбора пула десекрейт (например «Time-Lost Sapphire Jewels»).</summary>
    public string ItemClass { get; set; } = "";

    /// <summary>
    /// Если true — все 3 мода из десекрейт-пула (применён особый омен для оружия/бижутерии).
    /// Если false (по умолчанию) — 1 из десекрейт-пула, 2 обычных.
    /// </summary>
    public bool AllModsFromDesecratePool { get; set; } = false;

    /// <summary>
    /// Условие выбора мода — проверяется для каждой строки интерфейса reveal.
    /// Первая строка, для которой условие выполнено, кликается (Success).
    /// null = принять первый найденный десекрейт-мод без условия.
    /// </summary>
    public CraftConditionPlan? PickCondition { get; set; }

    /// <summary>Задержка после клика по выбранному моду (мс).</summary>
    public int ClickDelayMs { get; set; } = 600;

    /// <summary>Область кнопки Confirm в интерфейсе reveal. Нужна только при <see cref="AutoConfirm"/> = true.</summary>
    public ScreenRect ConfirmButtonArea { get; set; }

    /// <summary>Если true — после выбора мода кликает кнопку Confirm. По умолчанию false (ручное подтверждение).</summary>
    public bool AutoConfirm { get; set; } = false;
}

/// <summary>Конфигурация простого клика для <see cref="PipelineAction.ClickRegion"/>.</summary>
public sealed class ClickRegionConfig
{
    /// <summary>Область экрана; клик по центру.</summary>
    public ScreenRect Region { get; set; }

    /// <summary>Если true — зажимается Ctrl перед кликом (Ctrl+ЛКМ).</summary>
    public bool UseCtrl { get; set; } = false;

    /// <summary>Задержка после клика (мс).</summary>
    public int ClickDelayMs { get; set; } = 500;
}

/// <summary>Конфигурация поиска кнопки по PNG-шаблону для <see cref="PipelineAction.ClickTemplate"/>.</summary>
public sealed class ClickTemplateConfig
{
    /// <summary>Путь к PNG-шаблону (относительный к Templates/ или абсолютный).</summary>
    public string TemplatePath { get; set; } = "";

    /// <summary>Область экрана для поиска шаблона.</summary>
    public ScreenRect SearchRect { get; set; }

    /// <summary>Минимальная доля совпадающих пикселей [0..1]. По умолчанию 0.80.</summary>
    public double MatchThreshold { get; set; } = 0.80;

    /// <summary>Допуск яркости пикселя [0..255]. По умолчанию 30.</summary>
    public int ColorTolerance { get; set; } = 30;

    /// <summary>Задержка после клика, мс.</summary>
    public int ClickDelayMs { get; set; } = 500;
}

/// <summary>Конфигурация ходьбы внутри локации для <see cref="PipelineAction.WalkToPosition"/>.</summary>
public sealed class WalkToPositionConfig
{
    /// <summary>Список нажатий клавиш. Выполняются последовательно.</summary>
    public List<WalkKeyPress> KeyPresses { get; set; } = new();

    /// <summary>Нажать Escape перед движением — закрыть открытый стэш/инвентарь.</summary>
    public bool PressEscapeFirst { get; set; } = true;
}

public sealed class CraftPipelineStep
{
    public string Name { get; set; } = "";
    public PipelineAction Action { get; set; }

    /// <summary>Параметры омена — используется только при <see cref="PipelineAction.OmenActivation"/>.</summary>
    public OmenActionConfig? OmenConfig { get; set; }

    /// <summary>Параметры масла делириума — используется только при <see cref="PipelineAction.DeliriumLiquid"/>.</summary>
    public DeliriumLiquidActionConfig? DeliriumLiquidConfig { get; set; }

    /// <summary>Параметры перехода между локациями — используется только при <see cref="PipelineAction.TravelToLocation"/>.</summary>
    public TravelActionConfig? TravelConfig { get; set; }

    /// <summary>Параметры ходьбы внутри локации — используется только при <see cref="PipelineAction.WalkToPosition"/>.</summary>
    public WalkToPositionConfig? WalkConfig { get; set; }

    /// <summary>Параметры поиска по шаблону — используется только при <see cref="PipelineAction.ClickTemplate"/>.</summary>
    public ClickTemplateConfig? TemplateConfig { get; set; }

    /// <summary>Параметры reveal-десекрейт — используется только при <see cref="PipelineAction.DesecratePick"/>.</summary>
    public DesecratePickConfig? DesecratePickConfig { get; set; }

    /// <summary>Параметры клика по области — используется только при <see cref="PipelineAction.ClickRegion"/>.</summary>
    public ClickRegionConfig? ClickRegionConfig { get; set; }

    /// <summary>Id кости (из AbyssKnownItems) — используется только при <see cref="PipelineAction.SimpleAbyssalBone"/>.</summary>
    public string? AbyssalBoneId { get; set; }

    /// <summary>Имя орба из Currency stash — используется только при <see cref="PipelineAction.SimpleCurrency"/>.</summary>
    public string? CurrencyId { get; set; }

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

    /// <summary>
    /// BATCH-1c: Условие, проверяемое перед каждым шагом пайплайна.
    /// Если условие не выполнено — крафт прерывается.
    /// null = предохранитель не задан.
    /// </summary>
    public CraftConditionPlan? GuardCondition { get; set; }

    /// <summary>
    /// BATCH-1d: Когда true — перед каждым шагом проверяется, что предмет
    /// распознаётся хотя бы одним entryCondition пайплайна.
    /// Если не распознан — крафт прерывается.
    /// </summary>
    public bool EnableStepRecognition { get; set; } = false;

    public List<CraftPipelineStep> Steps { get; set; } = new();
}
