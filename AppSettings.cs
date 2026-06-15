using System.Text.Json.Serialization;
using GameHelper.Services;

namespace GameHelper;

/// <summary>Настройки UI, сохраняемые в <c>settings.json</c> в корне проекта.</summary>
public sealed class AppSettings
{
    // Устаревшие поля — читаются из старых settings.json для миграции в CurrencyItemRegions, не пишутся.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect OrbRect { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect ExaltRect { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect AugRect { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect AnnulRect { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect SharpenRect { get; set; }

    public ScreenRect CurrencyInventoryRect { get; set; }
    public ScreenRect RitualInventoryRect { get; set; }

    public ScreenRect OmenSinistralRect { get; set; }
    public List<ScreenRect>? OmenSinistralCells { get; set; }

    public ScreenRect OmenDextralRect { get; set; }
    public List<ScreenRect>? OmenDextralCells { get; set; }

    public ScreenRect OmenGreaterRect { get; set; }
    public List<ScreenRect>? OmenGreaterCells { get; set; }

    /// <summary>Области предметов во вкладке Ritual Stash для Networth. Ключ — имя предмета точно как на poe.ninja.</summary>
    public Dictionary<string, ScreenRect>? RitualItemRegions { get; set; }

    // Устаревшие поля — читаются из старых settings.json для миграции в RitualItemRegions, не пишутся.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect OmenSinistralStashRect { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ScreenRect OmenDextralStashRect { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
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
    /// <summary>Включить каскадный рефордж: дешёвые выходы перековываются повторно прямо на станке.</summary>
    public bool ReforgeCascadeEnabled { get; set; } = false;
    /// <summary>Порог отсечки каскадного рефорджа: катализаторы дороже этой цены (ex) не перековываются повторно.</summary>
    public decimal ReforgeCascadeThresholdEx { get; set; } = 2.0m;
    /// <summary>Минимальное количество катализаторов в стэше для участия в перековке. Типы с меньшим остатком пропускаются.</summary>
    public int ReforgeCascadeMinStashCount { get; set; } = 30;
    /// <summary>Id катализаторов, выбранных для перековки.</summary>
    public List<string>? ReforgeSelectedCatalystIds { get; set; }
    /// <summary>Ячейки сетки инвентаря для перековки (независимы от ItemCells крафта).</summary>
    public List<ScreenRect>? ReforgeItemCells { get; set; }

    /// <summary>Virtual key code для горячей клавиши «Старт/Стоп перековки» (0 = не задано).</summary>
    public int ReforgeStartStopVirtualKey { get; set; }
    /// <summary>Модификаторы для «Старт/Стоп перековки»: Alt=1, Ctrl=2, Shift=4.</summary>
    public int ReforgeStartStopModifiers { get; set; }

    /// <summary>Virtual key code для горячей клавиши «Авто Старт/Стоп перековки» (0 = не задано).</summary>
    public int AutoReforgeStartStopVirtualKey { get; set; }
    /// <summary>Модификаторы для «Авто Старт/Стоп перековки»: Alt=1, Ctrl=2, Shift=4.</summary>
    public int AutoReforgeStartStopModifiers { get; set; }

    /// <summary>Virtual key code для горячей клавиши «Networth Старт/Стоп» (0 = не задано).</summary>
    public int NetworthStartStopVirtualKey { get; set; }
    /// <summary>Модификаторы для «Networth Старт/Стоп»: Alt=1, Ctrl=2, Shift=4.</summary>
    public int NetworthStartStopModifiers { get; set; }

    /// <summary>Области предметов в Currency Stash вкладке для Networth. Ключ — имя предмета точно как на poe.ninja.</summary>
    public Dictionary<string, ScreenRect>? CurrencyItemRegions { get; set; }

    /// <summary>Вкладка Breach Stash целиком (для навигации / скролла).</summary>
    public ScreenRect BreachInventoryRect { get; set; }
    /// <summary>Области катализаторов во вкладке Breach Stash. Ключ — Id из StackableItemRegistry.</summary>
    public Dictionary<string, ScreenRect>? BreachCatalystRegions { get; set; }

    /// <summary>Вкладка Delirium Stash целиком (для навигации / скролла).</summary>
    public ScreenRect DeliriumInventoryRect { get; set; }
    /// <summary>Области предметов делирия во вкладке Delirium Stash. Ключ — Id из StackableItemRegistry.</summary>
    public Dictionary<string, ScreenRect>? DeliriumItemRegions { get; set; }

    /// <summary>Полный инвентарь персонажа (12×5 = 60 ячеек). Используется в авто-режиме: заполнение и сброс катализаторов.</summary>
    public List<ScreenRect>? FullInventoryCells { get; set; }

    // ── Цены в золоте ───────────────────────────────────────────────────────
    /// <summary>Цена одного катализатора в золоте. Ключ — Id из StackableItemRegistry.</summary>
    public Dictionary<string, int>? CatalystGoldPrices { get; set; }

    // ── poe.ninja цены ──────────────────────────────────────────────────────
    /// <summary>Название лиги для запроса цен с poe.ninja (например «Runes of Aldur» или «Standard»).</summary>
    public string PoeNinjaLeague { get; set; } = "Runes of Aldur";

    // ── Навигация (авто-перековка) ───────────────────────────────────────────
    /// <summary>Область экрана для OCR-поиска метки STASH.</summary>
    public ScreenRect StashOcrSearchRect { get; set; }
    /// <summary>Текст для поиска метки STASH (сравнение без регистра/пробелов).</summary>
    public string StashOcrText { get; set; } = "STASH";
    /// <summary>Область экрана для OCR-поиска метки Reforging Bench.</summary>
    public ScreenRect ReforgingBenchOcrSearchRect { get; set; }
    /// <summary>Текст для поиска метки Reforging Bench (только «Reforging» — «Bench» OCR путает с кириллицей).</summary>
    public string ReforgingBenchOcrText { get; set; } = "Reforging";
    /// <summary>Задержка после клика по STASH (персонаж идёт к стэшу), мс.</summary>
    public int StashOpenDelayMs { get; set; } = 3000;
    /// <summary>Задержка после клика по Reforging Bench (персонаж идёт к станку), мс.</summary>
    public int ReforgingBenchOpenDelayMs { get; set; } = 3000;
    /// <summary>Сколько катализаторов перекладывает один Ctrl+ЛКМ из Breach-вкладки стэша.</summary>
    public int AutoReforgeStashItemsPerClick { get; set; } = 10;
    /// <summary>Задержка после каждого Ctrl+ЛКМ/ПКМ при переносе предмета (игра обрабатывает перенос), мс.</summary>
    public int AutoReforgeItemTransferDelayMs { get; set; } = 400;

    // ── Переоценка (async trade) ─────────────────────────────────────────────
    /// <summary>Сетка ячеек выставленных товаров для переоценки.</summary>
    public List<ScreenRect>? RepricingCells { get; set; }
    /// <summary>Задержка после ПКМ (открытие поля ввода цены), мс.</summary>
    public int RepricingPostClickDelayMs { get; set; } = 300;
    /// <summary>Пауза наведения перед Ctrl+Alt+C, мс.</summary>
    public int RepricingHoverSettleMs { get; set; } = 120;
    /// <summary>Virtual key code для горячей клавиши «Старт/Стоп переоценки» (0 = не задано).</summary>
    public int RepricingStartStopVirtualKey { get; set; }
    /// <summary>Модификаторы для «Старт/Стоп переоценки»: Alt=1, Ctrl=2, Shift=4.</summary>
    public int RepricingStartStopModifiers { get; set; }

}
