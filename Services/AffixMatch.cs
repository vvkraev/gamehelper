using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Сравнение текста предмета с буфера: либо подстрока без шаблона, либо шаблон с одной буквой <c>n</c> — место для одной группы цифр (regex <c>(\d+)</c>).
/// </summary>
public static class AffixMatch
{
    /// <summary>
    /// Если в <paramref name="pattern"/> нет символа <c>n</c> — успех, когда <paramref name="clipboard"/> содержит шаблон без учёта регистра.
    /// Если <c>n</c> есть — строится regex: текст до <c>n</c> + одно или более цифр + текст после <c>n</c>; успех, если совпадение есть и число <c>&gt;= minRoll</c>.
    /// </summary>
    public static bool TryMatch(string clipboard, string pattern, int minRoll, out string explanation)
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
        var rx = new Regex(before + @"(\d+)" + after, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var m = rx.Match(clipboard);
        if (!m.Success)
        {
            explanation = "Шаблон с цифрами на месте «n» не найден в буфере.";
            return false;
        }

        if (!int.TryParse(m.Groups[1].Value, out var rolled))
        {
            explanation = "Не удалось разобрать число из совпадения.";
            return false;
        }

        var pass = rolled >= minRoll;
        explanation = pass
            ? $"Найдено значение {rolled}, требуется >= {minRoll} — условие выполнено."
            : $"Найдено значение {rolled}, требуется >= {minRoll} — продолжаем крафт.";
        return pass;
    }
}
