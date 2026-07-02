namespace GameHelper.Services;

public enum ChaosCraftResult
{
    AffixFound,
    EmptyCell,
    MaxAttemptsReached,
    Cancelled,
    Error,

    /// <summary>Один или несколько аффиксов из условия отсутствуют на предмете — ячейку пропускаем без трат орба.</summary>
    AffixesMissing,
}
