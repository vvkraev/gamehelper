namespace GameHelper.Services;

/// <summary>Единая модель результата крафта, возвращаемая всеми сервисами RunAsync.</summary>
public sealed record CraftResult(
    ChaosCraftResult StopReason,
    int Attempts,
    string? FinalItem = null)
{
    public bool Success => StopReason == ChaosCraftResult.AffixFound;

    public static CraftResult Found(int attempts, string? finalItem = null) =>
        new(ChaosCraftResult.AffixFound, attempts, finalItem);

    public static CraftResult Empty(int attempts) =>
        new(ChaosCraftResult.EmptyCell, attempts);

    public static CraftResult LimitReached(int attempts) =>
        new(ChaosCraftResult.MaxAttemptsReached, attempts);

    public static CraftResult Stopped(int attempts) =>
        new(ChaosCraftResult.Cancelled, attempts);

    public static CraftResult Failed(int attempts = 0) =>
        new(ChaosCraftResult.Error, attempts);
}
