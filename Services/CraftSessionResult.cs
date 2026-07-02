namespace GameHelper.Services;

/// <summary>Итог крафт-сессии, возвращаемый <see cref="CraftOrchestrator.RunAsync"/>.</summary>
public sealed record CraftSessionResult
{
    public required ChaosCraftResult FinalResult { get; init; }
    public required int TotalAttempts { get; init; }

    /// <summary>
    /// Фактически потреблённых орбов (CRAFT-11, только для Chaos режима с overflow).
    /// -1 = не отслеживалось (Aug+Annul, Exalt, Fracturing).
    /// </summary>
    public int TotalActualOrbsConsumed { get; init; } = -1;

    /// <summary>Все ячейки удовлетворяли условию с самого начала — N не тратился.</summary>
    public bool AllCellsAlreadySatisfied { get; init; }

    /// <summary>Путь к wip-файлу лога крафта во время выполнения; null если лог не создавался.</summary>
    public string? ActiveCraftLogWipPath { get; init; }

    /// <summary>Сообщение ошибки предпроверки (для MessageBox в UI).</summary>
    public string? PrecheckErrorMessage { get; init; }

    /// <summary>Заголовок MessageBox ошибки предпроверки.</summary>
    public string? PrecheckErrorTitle { get; init; }
}
