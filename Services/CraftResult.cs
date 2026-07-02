namespace GameHelper.Services;

/// <summary>Единая модель результата крафта, возвращаемая всеми сервисами RunAsync.</summary>
public sealed record CraftResult(
    ChaosCraftResult StopReason,
    int Attempts,
    string? FinalItem = null,
    /// <summary>
    /// Фактически потреблённых орбов (с учётом overflow-возврата).
    /// -1 = режим не поддерживает точный учёт (Aug+Annul, Exalt и др.).
    /// </summary>
    int ActualOrbsConsumed = -1)
{
    public bool Success => StopReason == ChaosCraftResult.AffixFound;

    public static CraftResult Found(int attempts, string? finalItem = null, int actualOrbs = -1) =>
        new(ChaosCraftResult.AffixFound, attempts, finalItem, actualOrbs);

    public static CraftResult Empty(int attempts) =>
        new(ChaosCraftResult.EmptyCell, attempts);

    public static CraftResult LimitReached(int attempts, int actualOrbs = -1) =>
        new(ChaosCraftResult.MaxAttemptsReached, attempts, null, actualOrbs);

    public static CraftResult Stopped(int attempts, int actualOrbs = -1) =>
        new(ChaosCraftResult.Cancelled, attempts, null, actualOrbs);

    public static CraftResult Failed(int attempts = 0) =>
        new(ChaosCraftResult.Error, attempts);

    public static CraftResult NoAffixes(int attempts = 0) =>
        new(ChaosCraftResult.AffixesMissing, attempts);
}
