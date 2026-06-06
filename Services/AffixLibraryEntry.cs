namespace GameHelper.Services;

/// <summary>
/// Запись библиотеки аффиксов (файл <c>affix_library.json</c> в корне проекта).
/// </summary>
public sealed class AffixLibraryEntry
{
    /// <summary>Классы предметов, на которых встречался аффикс.</summary>
    public List<string> ItemClasses { get; set; } = new();

    /// <summary>Тип из игры: Prefix Modifier, Suffix Modifier, Desecrated Suffix Modifier и т.д.</summary>
    public string AffixType { get; set; } = "";

    public string AffixName { get; set; } = "";
    public int AffixTier { get; set; }

    /// <summary>Минимальный уровень предмета (ilvl), с которым аффикс наблюдался при автообучении.</summary>
    public int? AffixTierLevel { get; set; }

    /// <summary>Тексты статов (по строкам эффекта).</summary>
    public List<string> AffixStats { get; set; } = new();

    /// <summary>Диапазоны тира для каждого стата (тот же порядок, что у <see cref="AffixStats"/>).</summary>
    public List<string?> AffixRanges { get; set; } = new();

    /// <summary>
    /// Подкласс для предметов, где один Item Class объединяет несколько механик.
    /// Например, все планшеты имеют класс "Tablet", но подклассы: "Ritual", "Breach", "Abyss" и т.д.
    /// </summary>
    public string? AffixSubClass { get; set; }
}
