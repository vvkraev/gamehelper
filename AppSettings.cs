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

    public ScreenRect CurrencyInventoryRect { get; set; }
    public ScreenRect RitualInventoryRect { get; set; }

    public ScreenRect OmenSinistralRect { get; set; }
    public List<ScreenRect>? OmenSinistralCells { get; set; }

    public ScreenRect OmenDextralRect { get; set; }
    public List<ScreenRect>? OmenDextralCells { get; set; }

    public ScreenRect OmenGreaterRect { get; set; }
    public List<ScreenRect>? OmenGreaterCells { get; set; }

    /// <summary>Область в инвентаре для кликов по Stash у соответствующего омена (прямоугольник).</summary>
    public ScreenRect OmenSinistralStashRect { get; set; }
    public ScreenRect OmenDextralStashRect { get; set; }
    public ScreenRect OmenGreaterStashRect { get; set; }

    public ScreenRect ItemRect { get; set; }

    /// <summary>Область экрана для OCR подписи имени NPC (чёрная плашка над торговцем).</summary>
    public ScreenRect TraderNameOcrSearchRect { get; set; }

    /// <summary>Текст имени для поиска (например ANGE); сравнение без регистра и без пробелов.</summary>
    public string TraderNpcNameForOcr { get; set; } = "ANGE";

    /// <summary>Market Ratio: клик ЛКМ по зоне «I HAVE» (продаём, здесь — Exalted Orb).</summary>
    public ScreenRect MarketRatioIHaveClickRect { get; set; }

    /// <summary>Market Ratio: клик ЛКМ по зоне «I WANT» (покупаем, здесь — Divine Orb).</summary>
    public ScreenRect MarketRatioIWantClickRect { get; set; }

    /// <summary>Область экрана со списком валют при открытом выборе (для OCR названия и ЛКМ).</summary>
    public ScreenRect MarketRatioCurrencyPickerListRect { get; set; }

    /// <summary>Market Ratio: область текста курса (например «1 : 1») между I WANT и I HAVE.</summary>
    public ScreenRect MarketRatioRateReadoutRect { get; set; }

    /// <summary>Market Ratio: область комиссии в золоте под курсом.</summary>
    public ScreenRect MarketRatioGoldFeeReadoutRect { get; set; }

    /// <summary>Наведение мыши на панель Market Ratio перед Alt (открытие стакана).</summary>
    public ScreenRect MarketRatioDepthHoverRect { get; set; }

    /// <summary>OCR всего всплывающего окна стакана (Available / Competing Trades).</summary>
    public ScreenRect MarketRatioOrderBookOcrRect { get; set; }

    /// <summary>Ручная сетка 6×2 для сценария «есть обе секции»: блок Available (12 ячеек, row-major: ratio,stock × 6).</summary>
    public List<ScreenRect>? MarketRatioOrderBookBothAvailableCells { get; set; }

    /// <summary>Ручная сетка 6×2 для сценария «есть обе секции»: блок Competing (12 ячеек, row-major: ratio,stock × 6).</summary>
    public List<ScreenRect>? MarketRatioOrderBookBothCompetingCells { get; set; }

    /// <summary>Ручная сетка 6×2 для сценария «только Available» (12 ячеек, row-major).</summary>
    public List<ScreenRect>? MarketRatioOrderBookAvailableOnlyCells { get; set; }

    /// <summary>Ручная сетка 6×2 для сценария «только Competing» (12 ячеек, row-major).</summary>
    public List<ScreenRect>? MarketRatioOrderBookCompetingOnlyCells { get; set; }

    /// <summary>Сдвиг курсора вправо после Alt, пиксели (по умолчанию 200).</summary>
    public int MarketRatioDepthHoverOffsetXPx { get; set; } = 200;

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

    /// <summary>Префикс [ExaltSchema] в логе: привязка к ASCII-флоу экзальт-крафта (Fractured side).</summary>
    public bool TraceExaltationSchema { get; set; }

    /// <summary>Virtual key code для горячей клавиши «Трей» (0 = не задано).</summary>
    public int TrayToggleVirtualKey { get; set; }
    /// <summary>Модификаторы: Alt=1, Ctrl=2, Shift=4 (комбинируются через |).</summary>
    public int TrayToggleModifiers { get; set; }

    /// <summary>Virtual key code для горячей клавиши «Открыть лог крафта» (0 = не задано).</summary>
    public int OpenLogVirtualKey { get; set; }
    /// <summary>Модификаторы для «Открыть лог крафта»: Alt=1, Ctrl=2, Shift=4.</summary>
    public int OpenLogModifiers { get; set; }

    /// <summary>Virtual key code для горячей клавиши «Старт/Стоп крафта» (0 = не задано).</summary>
    public int CraftStartStopVirtualKey { get; set; }
    /// <summary>Модификаторы для «Старт/Стоп крафта»: Alt=1, Ctrl=2, Shift=4.</summary>
    public int CraftStartStopModifiers { get; set; }

    // ── Reforge (Перековка) ──────────────────────────────────────────────────
    /// <summary>Слот 1 входа в станке перековки.</summary>
    public ScreenRect ReforgeSlot1Rect { get; set; }
    /// <summary>Слот 2 входа в станке перековки.</summary>
    public ScreenRect ReforgeSlot2Rect { get; set; }
    /// <summary>Слот 3 входа в станке перековки.</summary>
    public ScreenRect ReforgeSlot3Rect { get; set; }
    /// <summary>Область результата перековки (откуда забирать Ctrl+ЛКМ).</summary>
    public ScreenRect ReforgeResultRect { get; set; }
    /// <summary>Кнопка «Reforge» / подтверждения на станке.</summary>
    public ScreenRect ReforgeConfirmRect { get; set; }
    /// <summary>Ожидание после нажатия кнопки Reforge (анимация станка), мс.</summary>
    public int ReforgePostAnimationDelayMs { get; set; } = 800;
    /// <summary>Максимум операций перековки за сессию (0 = без ограничений).</summary>
    public int ReforgeMaxOps { get; set; } = 0;
    /// <summary>Id катализаторов, выбранных для перековки.</summary>
    public List<string>? ReforgeSelectedCatalystIds { get; set; }
    /// <summary>Ячейки сетки инвентаря для перековки (независимы от ItemCells крафта).</summary>
    public List<ScreenRect>? ReforgeItemCells { get; set; }
}
