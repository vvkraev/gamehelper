using GameHelper.Services;

namespace GameHelper;

/// <summary>Настройки UI, сохраняемые в <c>settings.json</c> в корне проекта.</summary>
public sealed class AppSettings
{
    public ScreenRect OrbRect { get; set; }
    public ScreenRect ExaltRect { get; set; }
    public ScreenRect AugRect { get; set; }
    public ScreenRect AnnulRect { get; set; }
    public ScreenRect SharpenRect { get; set; }

    public ScreenRect OmenSinistralRect { get; set; }
    public List<ScreenRect>? OmenSinistralCells { get; set; }

    public ScreenRect OmenDextralRect { get; set; }
    public List<ScreenRect>? OmenDextralCells { get; set; }

    public ScreenRect OmenGreaterRect { get; set; }
    public List<ScreenRect>? OmenGreaterCells { get; set; }

    public ScreenRect ItemRect { get; set; }

    /// <summary>Ячейки сетки инвентаря (после «Задать область предмета»). Если null или пусто — используется только <see cref="ItemRect"/>.</summary>
    public List<ScreenRect>? ItemCells { get; set; }

    /// <summary>Синхронизируемый шаблон (после выбора из библиотеки); устаревшее ручное поле может подставляться при миграции.</summary>
    public string AffixPattern { get; set; } = "";

    /// <summary>Условие остановки крафта (класс предмета, ИЛИ вариантов, внутри варианта — И).</summary>
    public CraftConditionPlan? CraftCondition { get; set; }

    /// <summary>Выбор крафта из <c>affix_library.json</c> (миграция в <see cref="CraftCondition"/>).</summary>
    public string CraftItemClass { get; set; } = "";
    public string CraftAffixType { get; set; } = "";
    public string CraftAffixStat { get; set; } = "";
    public int CraftAffixTier { get; set; }

    /// <summary>Минимальный порог для сравнения (текст: целое или дробное в зависимости от диапазона тира).</summary>
    public string MinRollInput { get; set; } = "0";

    /// <summary>Устаревшее целое из старых settings.json.</summary>
    public int MinRoll { get; set; }

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public int MaxOps { get; set; } = 20;

    /// <summary>Выбранный режим крафта в UI: "Хаос" или "Ауг+Аннул".</summary>
    public string CraftMode { get; set; } = "Хаос";

    public bool TraceInput { get; set; }
    public bool StepConfirm { get; set; }
}
