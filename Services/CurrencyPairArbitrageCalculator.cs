using System.Globalization;

namespace GameHelper.Services;

/// <summary>
/// Чистые функции оценки спреда по данным стакана Market Ratio (без UI и файлов).
/// Для разных пар валют вызываются с уже извлечёнными числами из OCR/парсера.
/// </summary>
public static class CurrencyPairArbitrageCalculator
{
    /// <summary>
    /// По строкам курса вида «1 : R» (левая и правая части после разбора).
    /// В контексте PoE2 Market Ratio: Available Trades — лучшее предложение «с рынка» (ask),
    /// Competing Trades — лучшее конкурирующее (bid). Правая часть <paramref name="bestAvailableRatioRight"/>
    /// и <paramref name="bestCompetingRatioRight"/> должны быть в одних и тех же единицах (как на экране).
    /// </summary>
    /// <returns>
    /// <paramref name="bestAvailableRatioRight"/> − <paramref name="bestCompetingRatioRight"/>, или null, если нет обоих значений.
    /// Интерпретация спреда зависит от направления сделки; типично положительное значение — зазор между лучшим ask и лучшим bid в «правых» единицах на единицу левой.
    /// </returns>
    public static double? TryComputeAskMinusBidSpread(
        double? bestAvailableRatioRight,
        double? bestCompetingRatioRight)
    {
        if (bestAvailableRatioRight is not { } ask || bestCompetingRatioRight is not { } bid)
            return null;
        return ask - bid;
    }

    /// <summary>Удобная обёртка для вывода в лог/CSV (инвариантная культура).</summary>
    public static string FormatSpreadInvariant(double? spread) =>
        spread is { } v ? v.ToString("G9", CultureInfo.InvariantCulture) : "";
}
