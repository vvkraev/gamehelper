using System.Globalization;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Сравнение текста предмета с буфера: подстрока без шаблона или шаблон с <c>n</c> — место для числа (целого или дробного).
/// </summary>
public static class AffixMatch
{
    /// <summary>
    /// Без <c>n</c> — успех при вхождении подстроки. С <c>n</c> — regex: до n + число + после n; число сравнивается с <paramref name="minRoll"/> (≥).
    /// </summary>
    public static bool TryMatch(string clipboard, string pattern, double minRoll, out string explanation)
    {
        explanation = "";
        pattern = pattern.Trim();
        if (pattern.Length == 0)
        {
            explanation = "Шаблон пуст.";
            return false;
        }

        var nIdx = pattern.IndexOf('n');
        if (nIdx < 0)
        {
            var ok = clipboard.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            explanation = ok
                ? "Подстрока найдена (режим без n)."
                : "Подстрока не найдена в буфере.";
            return ok;
        }

        var before = Regex.Escape(pattern[..nIdx]);
        var after = Regex.Escape(pattern[(nIdx + 1)..]);
        var rx = new Regex(
            before + @"(\d+(?:\.\d+)?)" + after,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var m = rx.Match(clipboard);
        if (!m.Success)
        {
            explanation = "Шаблон с числом на месте «n» не найден в буфере.";
            return false;
        }

        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rolled)
            && !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.CurrentCulture, out rolled))
        {
            explanation = "Не удалось разобрать число из совпадения.";
            return false;
        }

        var pass = rolled >= minRoll;
        var minStr = FormatMin(minRoll);
        explanation = pass
            ? $"Найдено значение {rolled}, требуется >= {minStr} — условие выполнено."
            : $"Найдено значение {rolled}, требуется >= {minStr} — продолжаем крафт.";
        return pass;
    }

    private static string FormatMin(double v) =>
        v == Math.Truncate(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);
}
