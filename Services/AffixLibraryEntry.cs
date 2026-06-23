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
    /// Для Tablet: "Ritual", "Breach", "Abyss" и т.д.
    /// Для брони: "Armour", "Evasion", "Energy Shield", "Armour/Evasion" и т.д. — определяется
    /// по crafting-тегам из mod_no (armour / evasion / energy_shield) на poe2db.tw.
    /// null = доступен на всех подтипах данного ItemClass.
    /// </summary>
    public string? AffixSubClass { get; set; }

    /// <summary>
    /// Вес тира при выборе орбом (шаг 3 алгоритма из AFFIX_TIER_SELECTION_MECHANICS.md).
    /// Источник: поле DropChance в ModsView JSON на poe2db.tw.
    /// null = неизвестен (записи из старой версии скрапера без весов).
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// Идентификатор семейства аффиксов из poe2db (ModFamilyList[0]).
    /// Все тиры одного семейства (Queen's T1, Princess' T2) имеют одинаковый FamilyId.
    /// null = неизвестен (записи из старой версии скрапера).
    /// </summary>
    public string? FamilyId { get; set; }
}
