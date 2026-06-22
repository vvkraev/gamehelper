using System.Linq;

namespace GameHelper.Services;

/// <summary>
/// Условие остановки крафта: список вариантов, соединённых ИЛИ; внутри варианта все клозы должны выполняться (И).
/// Класс предмета один на всё условие и должен совпадать с классом в буфере.
/// </summary>
public sealed class CraftConditionPlan
{
    public string ExpectedItemClass { get; set; } = "";

    /// <summary>Варианты ИЛИ: достаточно полностью выполнить один <see cref="CraftAndGroup"/>.</summary>
    public List<CraftAndGroup> OrAlternatives { get; set; } = new();
}

/// <summary>Все клозы в группе связаны логическим И.</summary>
public sealed class CraftAndGroup
{
    public List<CraftClause> Clauses { get; set; } = new();
}

public enum CraftClauseKind
{
    Single,
    Sum,

    /// <summary>Сколько строк из набора выполняется на предмете одновременно ≥ <see cref="CraftCountAffixData.MinMatchCount"/>.</summary>
    Count,

    /// <summary>
    /// Один модификатор из библиотеки (имя + тир + тип): все строки <see cref="CraftWholeModifierAffixData.Lines"/> должны
    /// выполняться на одном аффиксе на предмете (логическое И по строкам).
    /// </summary>
    WholeModifier,
}

/// <summary>Одиночный аффикс, сумма по частям или набор с COUNT.</summary>
public sealed class CraftClause
{
    public CraftClauseKind Kind { get; set; }

    public CraftSingleAffixData? Single { get; set; }

    public CraftSumAffixData? Sum { get; set; }

    public CraftCountAffixData? Count { get; set; }

    public CraftWholeModifierAffixData? Whole { get; set; }
}

public sealed class CraftSingleAffixData
{
    public string AffixType { get; set; } = "";
    public string AffixName { get; set; } = "";

    /// <summary>Имена из библиотеки для выбранного семейства; при пустом списке используется <see cref="AffixName"/>.</summary>
    public List<string> SelectedAffixNames { get; set; } = new();

    public int AffixTier { get; set; }

    /// <summary>Строки стата семейства с порогами (новый формат, как в WholeModifier).</summary>
    public List<CraftWholeModifierLine> Lines { get; set; } = new();

    /// <summary>Устаревшее поле — оставлено для совместимости при загрузке старых рецептов.</summary>
    public string StatTemplate { get; set; } = "";

    /// <summary>Имена для сопоставления с предметом (мультивыбор ИЛИ одно устаревшее поле).</summary>
    public IReadOnlyList<string> EffectiveAffixNames()
    {
        if (SelectedAffixNames.Count > 0)
            return SelectedAffixNames;
        if (!string.IsNullOrWhiteSpace(AffixName))
            return new[] { AffixName.Trim() };
        return Array.Empty<string>();
    }

    /// <summary>Если <see cref="Lines"/> пуст, но задан устаревший <see cref="StatTemplate"/> — создаёт одну строку для совместимости.</summary>
    public void EnsureLinesFromLegacy()
    {
        if (Lines.Count == 0 && !string.IsNullOrEmpty(StatTemplate))
        {
            var line = new CraftWholeModifierLine { StatTemplate = StatTemplate, MinRoll = MinRoll };
            if (MinRolls.Count > 0)
                line.MinRolls = MinRolls.ToList();
            else
                line.EnsureMinRollsSize(1);
            Lines.Add(line);
        }
    }

    /// <summary>Устаревшее поле MinRoll — сохранено для чтения старых файлов.</summary>
    public double MinRoll { get; set; }

    /// <summary>Устаревшее поле MinRolls — сохранено для чтения старых файлов.</summary>
    public List<double> MinRolls { get; set; } = new();

    /// <summary>Минимумы для каждого слота; при пустом <see cref="MinRolls"/> используется <see cref="MinRoll"/> для всех слотов.</summary>
    public IReadOnlyList<double> GetEffectiveMinRolls(int slotCount)
    {
        if (slotCount < 1)
            slotCount = 1;
        if (MinRolls.Count == slotCount)
            return MinRolls;
        if (MinRolls.Count > 0)
        {
            var copy = MinRolls.Take(slotCount).ToList();
            while (copy.Count < slotCount)
                copy.Add(0);
            return copy;
        }

        return Enumerable.Repeat(MinRoll, slotCount).ToList();
    }

    /// <summary>Подгоняет список порогов под число перекатов (при смене стата в UI).</summary>
    public void EnsureMinRollsSize(int slotCount)
    {
        if (slotCount < 1)
            slotCount = 1;
        if (MinRolls.Count == slotCount)
            return;
        if (MinRolls.Count == 0 && MinRoll != 0)
        {
            MinRolls = Enumerable.Repeat(MinRoll, slotCount).ToList();
            return;
        }

        while (MinRolls.Count < slotCount)
            MinRolls.Add(0);
        while (MinRolls.Count > slotCount)
            MinRolls.RemoveAt(MinRolls.Count - 1);
    }

}

/// <summary>Целый модификатор из affix_library: имя и тир задают запись; пороги по каждой строке стата.</summary>
public sealed class CraftWholeModifierAffixData
{
    public string AffixType { get; set; } = "";
    public string AffixName { get; set; } = "";

    /// <summary>Несколько имён целого модификатора (ИЛИ): одинаковый набор строк стата у всех в библиотеке.</summary>
    public List<string> SelectedAffixNames { get; set; } = new();

    public int AffixTier { get; set; }

    public List<CraftWholeModifierLine> Lines { get; set; } = new();

    public IReadOnlyList<string> EffectiveWholeAffixNames()
    {
        if (SelectedAffixNames.Count > 0)
            return SelectedAffixNames;
        if (!string.IsNullOrWhiteSpace(AffixName))
            return new[] { AffixName.Trim() };
        return Array.Empty<string>();
    }
}

/// <summary>Одна строка эффекта внутри целого модификатора (как <see cref="CraftSingleAffixData"/>, но без типа/имени).</summary>
public sealed class CraftWholeModifierLine
{
    public string StatTemplate { get; set; } = "";

    public double MinRoll { get; set; }

    public List<double> MinRolls { get; set; } = new();

    public IReadOnlyList<double> GetEffectiveMinRolls(int slotCount)
    {
        if (slotCount < 1)
            slotCount = 1;
        if (MinRolls.Count == slotCount)
            return MinRolls;
        if (MinRolls.Count > 0)
        {
            var copy = MinRolls.Take(slotCount).ToList();
            while (copy.Count < slotCount)
                copy.Add(0);
            return copy;
        }

        return Enumerable.Repeat(MinRoll, slotCount).ToList();
    }

    public void EnsureMinRollsSize(int slotCount)
    {
        if (slotCount < 1)
            slotCount = 1;
        if (MinRolls.Count == slotCount)
            return;
        if (MinRolls.Count == 0 && MinRoll != 0)
        {
            MinRolls = Enumerable.Repeat(MinRoll, slotCount).ToList();
            return;
        }

        while (MinRolls.Count < slotCount)
            MinRolls.Add(0);
        while (MinRolls.Count > slotCount)
            MinRolls.RemoveAt(MinRolls.Count - 1);
    }
}

/// <summary>Сумма перекатов по перечисленным аффиксам (нет на предмете — вклад 0) ≥ <see cref="MinSum"/>.</summary>
public sealed class CraftSumAffixData
{
    public List<CraftAffixRef> Parts { get; set; } = new();
    public double MinSum { get; set; }
}

/// <summary>
/// Набор аффиксов. Успех клоза: не менее <see cref="MinMatchCount"/> членов из <see cref="Members"/>
/// найдено на предмете (каждый член — целый аффикс из библиотеки со всеми строками стата).
/// </summary>
public sealed class CraftCountAffixData
{
    /// <summary>Минимум выполненных членов набора (от 1 до числа членов).</summary>
    public int MinMatchCount { get; set; } = 1;

    public List<CraftWholeModifierAffixData> Members { get; set; } = new();
}

public sealed class CraftAffixRef
{
    public string AffixType { get; set; } = "";
    public string AffixName { get; set; } = "";
    public int AffixTier { get; set; }
    public string StatTemplate { get; set; } = "";
}
