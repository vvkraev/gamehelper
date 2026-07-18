# Архитектура батч-крафтинга: описание, проблемы, варианты развития

Документ описывает текущую систему пакетного крафта GameHelper на уровне, достаточном для
реализации с нуля (раздел 1), критически разбирает её слабые места (раздел 2) и предлагает
три варианта новой архитектуры с выбором победителя (раздел 3).

Исходники, на которых основан документ:

| Файл | Роль |
|---|---|
| `Services/CraftPipelineModels.cs` | Модель пайплайна: шаги, действия, переходы, конфиги действий |
| `Services/CraftPipelineRunner.cs` | Исполнитель одного предмета: диспетчер действий, `ExecuteStepAsync` |
| `Services/BatchPipelineRunner.cs` | Пакетный исполнитель: барьеры, вехи, инициализация стадий, стоимость |
| `Services/CraftConditionModels.cs` | Модель условий: `CraftConditionPlan` → `CraftAndGroup` → `CraftClause` |
| `Services/CraftConditionEvaluator.cs` | Валидация и вычисление условий по `ParsedItem` |
| `Services/ItemParser.cs` | Парсинг текста предмета из буфера обмена |
| `Services/AffixLibrary.cs` | База аффиксов (`affix_library.json`) — источник имён/тиров/строк стата |
| `Services/PipelineStepDetector.cs` | Определение начальной стадии предмета |
| `Pipelines/Новый рецепт.json` | Реальный пайплайн на 27 шагов (TLS Sapphire) — используется как сквозной пример |

---

## Раздел 1 — Детальное описание батч-крафтинга

### 1.1 Общая картина

Система крафтит предметы Path of Exile 2 автоматически, эмулируя мышь/клавиатуру (Win32 API)
и читая состояние предмета через буфер обмена (Ctrl+Alt+C над предметом → текст → `ItemParser`).

Три уровня:

```
CraftPipeline (JSON-рецепт: список шагов)
    │
    ├── CraftPipelineRunner  — исполняет пайплайн для ОДНОГО предмета:
    │                          цикл «выполнить шаг → перейти по OnSuccess/OnFailure»
    │
    └── BatchPipelineRunner  — исполняет пайплайн для НЕСКОЛЬКИХ предметов
                               (ячейки инвентаря) с барьерной синхронизацией по стадиям.
                               Для выполнения одного шага вызывает
                               CraftPipelineRunner.ExecuteStepAsync напрямую.
```

Ключевая идея батча: предметы идут по пайплайну «строем». Все активные предметы проходят
стадию N прежде, чем любой перейдёт к N+1. Это нужно, потому что часть шагов — «вехи»
(поездка в локацию, открытие стэша): их дорого выполнять по разу на предмет, и их выполняют
один раз на всю группу.

### 1.2 Модель данных пайплайна

```csharp
CraftPipeline
 ├─ Name, Description : string
 ├─ ItemClass         : string            // класс предмета (напр. "Time-Lost Sapphire Jewels")
 ├─ GuardCondition    : CraftConditionPlan?  // BATCH-1c: предохранитель перед каждым шагом
 ├─ EnableStepRecognition : bool          // BATCH-1d: предмет обязан распознаваться детектором
 └─ Steps : List<CraftPipelineStep>

CraftPipelineStep
 ├─ Name    : string
 ├─ Action  : PipelineAction              // тип действия (см. 1.3)
 ├─ <XxxConfig> : конфиг конкретного действия (OmenConfig, TravelConfig, DesecrateRevealConfig, …)
 ├─ CurrencyId    : string?               // для SimpleCurrency — имя орба из Currency-вкладки
 ├─ AbyssalBoneId : string?               // для SimpleAbyssalBone — id кости
 ├─ EntryCondition : CraftConditionPlan?  // проверяется ДО действия (у части действий)
 ├─ LoopUntil      : CraftConditionPlan?  // условие остановки итеративного действия
 ├─ MaxIterations  : int = 100            // предохранитель цикла
 ├─ OnSuccess : PipelineTransition        // по умолчанию Next
 └─ OnFailure : PipelineTransition        // по умолчанию Abort

PipelineTransition
 ├─ Target    : Next | Done | Abort | Step
 ├─ StepIndex : int      // только для Target=Step — АБСОЛЮТНЫЙ индекс шага
 └─ Message   : string   // показывается при Done/Abort
```

Результат выполнения шага — внутренний тип `StepOutcome(bool Succeeded, int Attempts, string? FinalItemText)`.
`Attempts` — сколько единиц валюты потрачено (для учёта стоимости), `FinalItemText` — текст
предмета после шага (для логов).

Сериализация — System.Text.Json c camelCase: в файлах `Pipelines/*.json` ключи `name`,
`action` (строкой: `"chaosCraft"`, `"simpleCurrency"`, …), `entryCondition`, `loopUntil`,
`onSuccess: { "target": "next" }` и т.д.

### 1.3 Типы действий — полный справочник

Диспетчер: `CraftPipelineRunner.ExecuteStepAsync` — `switch` по `step.Action`.

| Action | Что делает | Проверяет EntryCondition? | Использует LoopUntil? | Success / Failure |
|---|---|---|---|---|
| `CheckItem` | Только читает буфер, кликов нет | как fallback | **да — основное условие** | Success = условие выполнено; Failure = буфер пуст или условие не выполнено |
| `ChaosCraft` | Цикл Chaos Orb через `IChaosCraftService` | **нет** | да (условие остановки цикла) | Success = условие достигнуто до `MaxIterations` |
| `AugAnnulCraft` | Цикл Aug+Annul | **нет** | да | аналогично |
| `DivineCraft` | Цикл Divine Orb | **нет** | да | аналогично |
| `ExaltCraft` | Exalt-цикл с оменами (`ExaltationCraftService`) | **нет** | да | аналогично; после — активная вкладка стэша «неизвестна» |
| `SimpleCurrency` | ПКМ на орб (`CurrencyId` → ячейка Currency-вкладки), ЛКМ на предмет, один раз | да | нет | Success(1) всегда, если entry прошёл и области настроены; **результат клика не верифицируется** |
| `SimpleExalt/Annul/Chaos` | Устаревшие частные случаи SimpleCurrency | да | нет | аналогично |
| `OmenActivation` | Перекладывает омен из стэша в инвентарь и активирует (ПКМ) | да | нет | Success = омен активирован; Attempts = 0 (см. 1.13, 2.8) |
| `DeliriumLiquid` | ПКМ на масло делириума из Delirium-вкладки, ЛКМ на предмет | **нет** | нет | Success(1) всегда, если масло настроено |
| `SimpleAbyssalBone` | Цикл: ПКМ на кость из Abyss-вкладки, ЛКМ на предмет, до `LoopUntil` или `MaxIterations` | да | да | **всегда Success**(consumed); кость засчитана, только если текст предмета изменился |
| `ManualPause` | По модели — «сообщение + ждать Продолжить»; в текущем раннере — **no-op**, сразу `Success()` | нет | нет | Success |
| `TravelToLocation` | OCR «Waypoint» в области → клик → клик кнопки локации → ожидание загрузки → OCR-верификация `ExpectedLocation` | нет | нет | Failure если waypoint не найден / локация не подтвердилась |
| `WalkToPosition` | Esc (опц.) → последовательность зажатий WASD-клавиш (`Keys[] × DurationMs`) | нет | нет | Failure только при пустом конфиге |
| `OpenStash` | OCR-проверка «стэш уже открыт» → иначе OCR-поиск надписи STASH → клик → задержка | нет | нет | **Success почти всегда** (не найдено → «пропускаем» → Success) |
| `ClickTemplate` | Попиксельный поиск PNG-шаблона в области (порог 0.80, допуск 30) → клик по центру | нет | нет | Failure если шаблон не найден |
| `DesecratePick` | OCR трёх карточек reveal → сопоставление с десекрейт-пулом → выбор по `PickCondition` → клик (+Confirm) | нет | нет (условие — `PickCondition` в конфиге) | Success = нужный мод найден и кликнут; Failure = не найден (кликнут запасной) |
| `DesecrateReveal` | Атомарный цикл: Ctrl+ЛКМ предмет→слот reveal → клик Reveal → `DesecratePick` → Ctrl+ЛКМ слот→инвентарь | нет | нет | Возвращает исход внутреннего DesecratePick |
| `CtrlClickItem` | Ctrl+ЛКМ по центру `ItemArea` (перенос предмета) | нет | нет | Failure только если `ItemArea` не задана |
| `ClickRegion` | ЛКМ (или Ctrl+ЛКМ) по центру настраиваемой области | нет | нет | Failure только при пустом конфиге |

Важные наблюдения по таблице (они же — источники проблем в разделе 2):

1. **`EntryCondition` проверяется не везде.** Пре-флайт `CheckEntryConditionAsync`
   (чтение буфера → парсинг → `CraftConditionEvaluator.TryEvaluate`) вызывают только
   `SimpleCurrency`, устаревшие Simple-действия, `OmenActivation`, `SimpleAbyssalBone`.
   Итеративные крафт-действия (`ChaosCraft`, `DivineCraft`, `AugAnnulCraft`, `ExaltCraft`)
   и все «механические» действия (клики, ходьба, travel) — игнорируют `EntryCondition`.
   Для них `EntryCondition` — только вход для `PipelineStepDetector` (см. 1.9).

2. **`LoopUntil` — двойная семантика.** У итеративных действий это условие остановки цикла
   («крафть, пока не выполнится»). У `CheckItem` (после недавнего фикса) — это
   *проверяемое* условие: `var condition = step.LoopUntil ?? step.EntryCondition;`
   (EntryCondition оставлен как fallback для старых рецептов).

3. **Многие действия не умеют «падать».** `SimpleCurrency` возвращает Success(1), даже если
   клик по орбу ушёл в пустоту; `SimpleAbyssalBone` возвращает Success даже когда
   `LoopUntil` так и не выполнился за `MaxIterations`; `OpenStash` возвращает Success,
   когда надпись не найдена. Реальную верификацию берут на себя последующие `CheckItem`-шаги.

#### Детали ключевых действий

**ChaosCraft / DivineCraft / AugAnnulCraft / ExaltCraft.** Делегируют в соответствующий
сервис (`IChaosCraftService.RunAsync` и т.п.), передавая `plan = step.LoopUntil ?? пустой план`
и `step.MaxIterations` (одновременно как лимит попыток и лимит применений). Перед стартом
переключается вкладка стэша «Валюта» (`SwitchStashTabAsync` — с кэшем текущей вкладки
`_currentStashTab`, чтобы не кликать повторно). Сервис в цикле: применить орб → прочитать
буфер → `TryEvaluate(plan)` → стоп при выполнении. Возвращает `(Success, Attempts, FinalItem)`.
`ExaltCraft` и `OmenActivation` переключают вкладки самостоятельно, поэтому после них
кэш вкладки сбрасывается (`_currentStashTab = default`).

**SimpleCurrency.** `CurrencyId` — ключ словаря `screen.CurrencyItemRegions` (области ячеек
Currency-вкладки из настроек). Последовательность: entry-check → переключить вкладку →
ПКМ по случайной внутренней точке ячейки орба (задержки 150/300 мс) → ЛКМ по предмету →
прочитать буфер → `Success(1, itemText)`.

**SimpleAbyssalBone.** Единственное «простое» действие с внутренним циклом:

```
for i in 0..MaxIterations (default 1000, если MaxIterations<=0):
    prevText = itemText
    вкладка Abyss → ПКМ кость → ЛКМ предмет → itemText = прочитать буфер
    if itemText != prevText: consumed++          // кость реально потрачена
    if LoopUntil == null: break                   // одиночное применение
    if TryEvaluate(LoopUntil, parse(itemText)): break
return Success(consumed, itemText)
```

Так реализован, например, шаг «Добавляем десекрейт»: кость Preserved Cranium применяется,
пока `LoopUntil` («на предмете есть нераскрытый десекрейт-слот И нет Lightless») не выполнится.

**TravelToLocation.** Конфиг: `WaypointSearchArea` (OCR-поиск слова «waypoint»),
`LocationButtonArea` (кнопка нужной локации — фиксированные координаты), задержки
`AfterWaypointDelayMs` (10 с) и `LoadingDelayMs` (15 с), `ExpectedLocation` — ожидаемое
имя локации, верифицируется OCR-ом области `PipelineScreenConfig.LocationNameArea`
с повторами. `ExpectedLocation` также используется батчем для коррекции стадий (1.9).

**DesecratePick — сердце десекрейт-логики.** Вход: область `RevealArea`, делённая
по вертикали на `RevealSections` (по умолчанию 3) полос — по одной на карточку мода.

```
loop:
  для каждой секции: OCR → строка текста
  если все секции пустые → Failure (интерфейс reveal не открыт)
  пул десекрейта: DesecrateStatsScanner.GetDesecrateEntries(itemClass)   // из affix_library
  foundDesecrate = секции, чей текст матчится с пулом (FindDesecrateMatch)
  записать jsonl-лог (trade_data/YYYY-MM-DD_{item}_desecrate_log.jsonl)

  если PickCondition пуст:
      кликнуть первый десекрейт-мод (или ничего) → Success

  для каждого найденного десекрейт-мода:
      syntheticItem = предмет из ОДНОГО аффикса               // BuildSyntheticItemFromEntry:
                      (Name/Type/Tier/Effects из записи библиотеки)
      если TryEvaluate(PickCondition, syntheticItem):
          клик по секции → Confirm → Success

  ни один не подошёл:
      если UseReroll и реролл ещё не использован:
          клик Reroll → ждать AfterRerollDelayMs → continue loop   // ровно ОДИН реролл
      иначе:
          кликнуть запасной вариант (десекрейт-мод > первая непустая секция) → Failure
```

`PickCondition` — обычный `CraftConditionPlan`, но вычисляется над **синтетическим** предметом
из одного аффикса. Это позволяет использовать те же клозы (Single/WholeModifier), включая
проверку имени семейства. Confirm: при `AutoConfirm=true` — клик по `ConfirmButtonArea`;
иначе курсор наводится на Confirm и ждётся **клик пользователя** в этой области
(опрос кнопки мыши каждые 30 мс, таймаут 120 с) — полуавтоматический режим.

**DesecrateReveal** — обёртка «под ключ» для батча: Ctrl+ЛКМ предмет (→ слот reveal) →
клик кнопки Reveal → `DesecratePick` → Ctrl+ЛКМ слот reveal (→ предмет назад в инвентарь).
Возвращает исход внутреннего pick, поэтому `OnFailure` шага обычно ведёт на повтор
(в примере: `S->done F->next`, где next — checkItem, решающий, продолжать ли reveal-цикл).

### 1.4 Условия: структура, типы клозов, вычисление

Единственный язык условий во всей системе — `CraftConditionPlan`:

```
CraftConditionPlan
 ├─ ExpectedItemClass : string      // ОБЯЗАН совпасть с Item Class из буфера, иначе false
 ├─ ExpectedItemSubType, ExpectedItemIlvl, CraftOrbName   // для весовых расчётов (CRAFT-3), на вычисление не влияют
 ├─ Negate : bool                   // инвертировать итог OR-блока (ошибки парсера НЕ инвертируются)
 └─ OrAlternatives : List<CraftAndGroup>     // ИЛИ между группами
      └─ Clauses : List<CraftClause>         // И внутри группы
           ├─ Kind   : Single | Sum | Count | WholeModifier | AffixCount | HasDesecrate
           ├─ Negate : bool                  // инвертировать один клоз
           └─ данные по Kind (Single / Sum / Count / Whole / AffixCount / DesecrateSide)
```

**План выполнен**, если предмет валиден, класс совпал, и хотя бы одна OR-группа выполнена
полностью (все её клозы = true с учётом их `Negate`); затем применяется `plan.Negate`.

Типы клозов:

| Kind | Данные | Семантика |
|---|---|---|
| `Single` | `AffixType`, `SelectedAffixNames[]` (ИЛИ по именам), `AffixTier`, `Lines[]` (`StatTemplate` + `MinRoll(s)`) | На предмете есть аффикс одного из имён нужного типа/тира, у которого **все** строки `Lines` достигают порогов. Тир «лучше или равен» тоже засчитывается (`TryMatchBetterTierSameFamily`). Фрактурные включаются всегда (`includeFractured: true`) |
| `Sum` | `Parts[]` (тип + `StatTemplate`), `MinSum` | Сумма перекатов по всем частям ≥ MinSum; отсутствующая часть даёт вклад 0. Имя/тир не проверяются |
| `Count` | `MinMatchCount`, `Members[]` (каждый — целый модификатор с Lines), `IncludeFractured` | Не менее `MinMatchCount` членов набора найдено на предмете. Основной инструмент вида «≥2 из 6 нужных суффиксов». `IncludeFractured=false` по умолчанию — фрактурные НЕ считаются |
| `WholeModifier` | как Single, но запись библиотеки обязана быть многострочной | Все строки одного гибридного модификатора выполняются на одном аффиксе |
| `AffixCount` | `Scope` (All/Prefixes/Suffixes), `Min`, `Max` (0 = без верха) | Количество аффиксов области в диапазоне. Матчится по подстроке "Prefix"/"Suffix" в `AffixInfo.Type` — то есть Fractured/Crafted/Desecrated варианты учитываются |
| `HasDesecrate` | `DesecrateSide` (Any/Prefix/Suffix) | На предмете есть нераскрытый десекрейт-слот: аффикс `Veiled` с эффектом `Desecrated Prefix/Suffix` |

Вычисление (`TryEvaluate`) детерминированное, без побочных эффектов, и возвращает
`explanation` — многострочную человекочитаемую расшифровку («Выполнен вариант 2 (ИЛИ): …» /
«[1]: набор COUNT: выполнено 1 из 6 (нужно ≥ 2)…»). Эта строка — основной инструмент отладки.

Валидация (`TryValidate`) выполняется при сохранении условия из UI: проверяет, что каждое
имя+тир+строка существуют в `affix_library.json` (через `AffixCraftPatternBuilder` /
`CraftAffixCascadeHelper.FindStatIndexInEntry`). Удаление записи из библиотеки ломает рецепт.

**Как пишется условие.** В UI пользователь собирает OR-группы и клозы из выпадающих списков,
источник которых — библиотека аффиксов, отфильтрованная по `ExpectedItemClass`. В JSON
это большие вложенные объекты; типичное условие из реального рецепта:

```jsonc
// "есть ≥3 из 10 целевых суффиксов (включая фрактурные) И есть крафтед-префикс '+1 Suffix allowed'"
"entryCondition": {
  "expectedItemClass": "Time-Lost Sapphire Jewels",
  "orAlternatives": [ { "clauses": [
      { "kind": "count", "count": { "minMatchCount": 3, "includeFractured": true,
          "members": [ { "affixType": "Suffix Modifier",
                         "selectedAffixNames": ["of Potency"], "affixTier": 1,
                         "lines": [ { "statTemplate": "#% increased Critical Damage Bonus", "minRoll": 0 } ] },
                       /* … ещё 9 членов … */ ] } },
      { "kind": "single", "single": { "affixType": "Crafted Prefix Modifier", "affixTier": 1,
          "lines": [ { "statTemplate": "+1 Suffix Modifier allowed", "minRoll": 0 } ] } }
  ] } ]
}
```

### 1.5 EntryCondition vs LoopUntil vs GuardCondition — семантические различия

| Поле | Где живёт | Когда проверяется | Смысл |
|---|---|---|---|
| `EntryCondition` | шаг | до действия (только у части действий, см. 1.3) + `PipelineStepDetector` | «Предмет находится в состоянии, в котором этот шаг уместен». Не выполнено → исход шага = Failure → `OnFailure` |
| `LoopUntil` | шаг | внутри итеративного действия после каждой итерации; у `CheckItem` — как проверяемое условие | «Состояние, которого шаг должен ДОСТИЧЬ». Достигнуто → Success; исчерпан `MaxIterations` → Failure |
| `GuardCondition` | пайплайн | перед каждым шагом (**только в одиночном** `RunAsync`; батч его не проверяет — см. 2.9) | Инвариант всего рецепта («предмет всё ещё тот и не испорчен»); нарушение → Abort |
| `EnableStepRecognition` | пайплайн | перед каждым шагом (только одиночный запуск) | Предмет обязан распознаваться `PipelineStepDetector` хотя бы на одном шаге, иначе Abort |
| `PickCondition` | конфиг DesecratePick | над каждой OCR-карточкой reveal | «Какой из 3 предложенных модов кликнуть» |

Семантическая пара, на которой всё держится: **EntryCondition = предусловие, LoopUntil = постусловие.**
Но в текущем коде эта пара выдержана непоследовательно (см. раздел 2).

### 1.6 Переходы OnSuccess / OnFailure

После каждого шага берётся `outcome.Succeeded ? OnSuccess : OnFailure` и применяется:

- `Next` — stepIdx+1; выход за конец списка = Done («Все шаги выполнены»).
- `Done` — предмет готов (в батче: `BatchItemStatus.Done`), `Message` — в лог.
- `Abort` — предмет провален (`Failed`), `Message` — причина.
- `Step(N)` — безусловный goto на **абсолютный индекс** N (вперёд или назад).

Через `Step(N)` строятся все циклы верхнего уровня: «не получилось — вернись на шаг 3»,
«получилось — перепрыгни шаг 5». Защита от бесконечности — глобальные счётчики выполнений:
10 000 (одиночный), 100 000 (батч) + детект «за полный проход ни один предмет не сдвинулся».

### 1.7 Одиночный запуск (`CraftPipelineRunner.RunAsync`)

```
stepIdx = clamp(startStepIndex, 0, N-1)
loop:
    (гарды пайплайна: GuardCondition / EnableStepRecognition — 1 чтение буфера на обе проверки)
    outcome = ExecuteStepAsync(steps[stepIdx])
    transition = outcome.Succeeded ? OnSuccess : OnFailure
    Next → stepIdx++ (за конец = Done) | Done/Abort → выход | Step → stepIdx = N
finally: ReleaseShift(); ReleaseCtrlAlt()      // не оставить зажатых клавиш в ОС
```

Статусы результата: `Done | Aborted | Cancelled | Error` + `TotalAttempts` + `FinalItemText`.
`CancellationToken` проверяется перед каждым шагом и внутри действий; отмена = `Cancelled`.

### 1.8 Батч: барьеры, вехи, сшивка оменов

`BatchPipelineRunner.RunAsync(pipeline, screenTemplate, itemCells, log, ct)`:

1. Создаёт `BatchItem` на каждую ячейку (`CellIndex`, `Status=Active`, `CurrentStage=0`,
   счётчики попыток/стоимости).
2. **Инициализация стадий** (1.9): для каждой ячейки читает буфер; пустой буфер → `Failed`
   («ячейка пропущена»); иначе `PipelineStepDetector.Detect` → `CurrentStage`.
3. **Коррекция по локации**: OCR текущей локации; если она совпадает с `ExpectedLocation`
   какого-то `TravelToLocation`-шага, все предметы со стадией ≤ индекса этой вехи
   продвигаются на стадию «веха+1» (мы уже на месте — ехать не надо).
4. Основной цикл `RunCoreAsync`:

```
while есть Active-предметы:
    anyProgress = false
    for n in 0..N-1:                                  // проход по стадиям снизу вверх
        targetItems = Active-предметы на стадии n
        if targetItems пуст: continue
        if существует Active-предмет со стадией < n: continue     // ← БАРЬЕР
        anyProgress = true

        if IsMilestoneAction(steps[n]):               // ← ВЕХА
            ActiveBatchItemCount = targetItems.Count  // для оменов ×N
            outcome = ExecuteStepAsync(steps[n])      // ОДИН раз на группу
            для каждого targetItem: ApplyTransition(outcome)   // один исход — всем
            continue

        for item in targetItems:                      // обычная стадия — по предметам
            screen = screenTemplate with { ItemArea = itemCells[item.CellIndex] }
            probe = прочитать буфер над ячейкой; пусто → item.Failed, continue
            outcome = ExecuteStepAsync(steps[n], screen)
            item.TotalAttempts += outcome.Attempts
            if Succeeded: AccumulateCost(item, step, Attempts)
            ApplyTransition(item, ...)                // Next/Done/Abort/Step(N)

            // «атомарная сшивка»: OmenActivation кладёт омен в инвентарь,
            // и он должен быть потрачен СЛЕДУЮЩИМ шагом этого же предмета,
            // пока батч не переключился на соседний предмет
            if Succeeded && step.Action == OmenActivation && item.Active:
                выполнить steps[item.CurrentStage] для этого же предмета немедленно

    if !anyProgress: log("deadlock"); break
finally: ReleaseShift(); ReleaseCtrlAlt()
```

**Барьер.** Стадия n исполняется только когда нет активных предметов ниже n. Предмет,
откатившийся назад через `OnFailure → Step(k<n)`, опускает барьер: остальные ждут на своих
стадиях, пока отставший не догонит (или не станет Done/Failed). Цель — чтобы к вехе
(например, поездке в Well of Souls) вся группа подошла одновременно и поездка была одна.

**Вехи (`IsMilestoneAction`).** `TravelToLocation`, `WalkToPosition`, `OpenStash`,
`ClickTemplate`, а также `OmenActivation` с `UseActiveBatchCount=true`. Веха не зависит от
конкретного предмета: выполняется один раз, а её переход применяется ко всем предметам стадии.
Перед вехой раннеру сообщается `ActiveBatchItemCount` — омен Abyssal Echoes перекладывается
в количестве = числу активных предметов (по одному на каждый предстоящий reveal).

**Статусы предметов**: `Active | Done | Failed`. (`Pending/WaitingAt` из проектной модели
BATCH-2 в коде выражены неявно: «ждёт» = Active на стадии, которую барьер пока не пускает.)

### 1.8.1 Техническое ограничение: максимум 10 предметов в батче

> **Техническое ограничение, не архитектурное решение.**
>
> Батч не может содержать более **10 предметов** одновременно. Причина: координатная сетка
> (`_pipelineItemCells`, `PipelineItemCells` в `AppSettings`) — это список `ScreenRect`, который
> пользователь настраивает вручную через диалог `ItemGridDimensionsDialog`. Диалог ограничен
> сеткой максимум **10 ячеек** (константа в `ItemGridDimensionsDialog`). Каждый `BatchItem`
> адресует ячейку по `CellIndex`; за пределами списка — индексы не определены.
>
> Если в будущем сетка будет расширена или заменена динамической структурой (например, авто-
> обнаружение ячеек через OCR), ограничение можно снять без изменений в `BatchPipelineRunner`.
> До тех пор **добавление >10 предметов в UI должно быть заблокировано** предупреждением
> «Максимальный размер батча — 10 предметов (ограничение координатной сетки)».

### 1.9 Инициализация стадий — `PipelineStepDetector`

Алгоритм `Detect(pipeline, parsedItem)`:

```
1. Сканировать шаги С КОНЦА К НАЧАЛУ (i = N-1 … 0).
2. Пропускать шаги без EntryCondition.
3. Первый (наибольший i) шаг, чей EntryCondition выполнен на предмете → кандидат n.
4. Правило n-1: если шаг n-1 имеет Action ∈ {CheckItem, OmenActivation}
   (они «не меняют предмет»), его EntryCondition задан и тоже выполнен → вернуть n-1.
5. Иначе вернуть n. Если ничего не совпало → null.
```

В батче `null` превращается в `CurrentStage = 0` (`detect.StepIndex ?? 0`) — нераспознанный
предмет отправляется на первый шаг. Результат содержит `Explanation` — полный протокол
«какой шаг совпал/не совпал и почему».

Логика «с конца к началу» означает: **условия входа поздних шагов должны быть строго
«сильнее» ранних** — иначе предмет, доросший до шага 12, совпадёт и с условием шага 3, и
детектор обязан выбрать наибольший индекс. Автор рецепта вынужден проектировать цепочку
условий так, чтобы каждое следующее было надмножеством предыдущего, и держать это в голове
именно в обратном порядке (см. 2.1).

Дополнение к детектору — `AdjustStagesForCurrentLocationAsync` (описано в 1.8, п.3):
единственный механизм детекции для вех, и он покрывает только `TravelToLocation`
с непустым `ExpectedLocation`. Пройденность `WalkToPosition`/`OpenStash`/`ClickTemplate`
не детектируется никак.

### 1.10 Парсинг предмета — `ItemParser`

Вход — текст буфера обмена PoE2 (блоки, разделённые `--------`). Выход — `ParsedItem`:

- Заголовок: `ItemClass`, `Rarity`, `Name`, `Base`.
- Глобальные поля из любой секции: `Item Level`, `Requires` (→ `ItemSubType` по Str/Dex/Int),
  `Sockets`, `Stack Size`.
- Секция аффиксов — строки вида `{ Prefix Modifier "Name" (Tier: N) — Tags }` + строки
  эффектов под ней. Распознаются типы: `Prefix/Suffix Modifier`,
  `Fractured Prefix/Suffix Modifier` (→ тип нормализуется до Prefix/Suffix + `IsFractured=true`),
  `Crafted Prefix/Suffix Modifier`, `Desecrated Prefix/Suffix Modifier`.
- Нераскрытый десекрейт: аффикс `Veiled` с эффектом `Desecrated Prefix`/`Desecrated Suffix`
  → `IsUnrevealedDesecrate=true` (на этом стоит клоз `HasDesecrate`).
- Каждая строка эффекта разбирается в `AffixEffectLine`: `85(75-89)% increased X` →
  `StatText="% increased X"`, `RolledValue="85"`, `Range="75-89"`; несколько перекатов
  в строке — плейсхолдеры X/Y/Z. `StatText` — то, что матчится со `StatTemplate` условий.

### 1.11 Библиотека аффиксов — `AffixLibrary`

Статический класс над `affix_library.json` (данные пользователя, руками не менять).
Запись `AffixLibraryEntry`: `ItemClasses[1]`, `AffixType`, `AffixName`, `AffixTier`,
`AffixTierLevel` (минимальный ilvl), `AffixStats[]` (строки-шаблоны), `AffixRanges[]`.
При загрузке — миграция мультиклассовых записей (по записи на класс) и разлепление
конкатенированных гибридов poe2db (в памяти, файл не трогается).
`GetEntriesWithCrafted()` — объединение с `crafted_mods.json` (крафтед-моды масел и т.п.).
Библиотека — справочник для: валидации условий, каскада «имя+тир → строки стата»,
десекрейт-пула (`DesecrateStatsScanner`), построения синтетического предмета в DesecratePick.

### 1.12 Специфика десекрейта в пайплайне

Полный цикл на примере «Новый рецепт» (шаги 13–26):

```
[13] travelToLocation → Well of Souls            (веха; барьер собирает всю группу)
[14] openStash                                    (веха)
[15] chaosCraft: LoopUntil = НЕ(есть Notable-Lightless … ) — «выбить» лишний десекрейт/радиус
[16] deliriumLiquid (Ancient Melancholy): LoopUntil = появился Crafted-радиус Very Large
[17] checkItem: радиус добавлялся после десекрейта? S->next, F->step(19)
[18] simpleAbyssalBone (Preserved Cranium): LoopUntil = HasDesecrate(any) И нет Lightless
[19] checkItem: десекрейт не стёр радиус? F->step(16) (перекрафтить радиус)
[20] omenActivation Abyssal Echoes ×N (веха, UseActiveBatchCount) — по омену на предмет
[21] checkItem: S->done | F->abort                (страховка)
[22] walkToPosition — дойти до Bone Altar         (веха)
[23] clickTemplate — открыть UI reveal            (веха)
[24] desecrateReveal: предмет → слот → Reveal → OCR 3 модов → PickCondition →
     клик / реролл (омен Abyssal Echoes даёт переброс) → Confirm → предмет назад.
     S->done, F->next
[25] checkItem: условие финала? S->done, F->next
[26] walkToPosition назад → S->step(14)           (следующий круг: стэш → кость → reveal)
```

OCR: Windows.Media.Ocr через `WindowsOcrTextLocator` (нормализация текста, поиск подстроки),
пул для сверки — записи `Desecrated * Modifier` данного класса из библиотеки.
Каждый reveal логируется в `trade_data/YYYY-MM-DD_{item}_desecrate_log.jsonl`
(все 3 строки + опознанные десекрейт-моды).

### 1.13 Специфика оменов

`OmenActionConfig`: имя омена, индекс ячейки в стэше, координата (строка/столбец) целевой
ячейки инвентаря 12×N, флаг `UseActiveBatchCount`.

Выполнение: ячейки стэша выбираются по имени — три «легаси»-имени
(Sinistral/Dextral/Greater Exaltation) из выделенных списков, остальные (Sinistral Erasure,
Abyssal Echoes, …) из словаря `OmenStashCellsByName`; вкладка — из `OmenStashTabByName`
(Ritual/Abyss). `OmenActivationService.PlaceAndActivateOmensAsync` перекладывает `Quantity`
оменов в инвентарь и активирует ПКМ. Омен становится «заряженным» состоянием игры:
следующее применение соответствующей валюты его потребит.

Отсюда два архитектурных костыля:

- **Сшивка** в батче: после успешной активации омена следующий шаг того же предмета
  выполняется немедленно, вне общего порядка — иначе заряженный омен потратил бы соседний
  предмет.
- `UseActiveBatchCount`: Abyssal Echoes нужен по одному на каждый reveal, поэтому веха
  активирует сразу N штук.

Используемые в рецептах омены: `Omen of Sinistral Erasure` (аннул/хаос бьёт только по
префиксам… в рецепте — защита `+1 Suffix` крафта), `Omen of Dextral Exaltation` (экзальт
только в суффикс), `Omen of Abyssal Echoes` (переброс модов reveal).

### 1.14 Учёт стоимости

`AccumulateCost(item, step, attempts)` вызывается **только при успехе не-вехового шага**:
`ResolveStepCurrencyName` отображает действие в имя валюты (SimpleCurrency → `CurrencyId`,
ChaosCraft → "Chaos Orb", OmenActivation → имя омена, DeliriumLiquid/Bone → display-имя
через реестры), цена — `PoeNinjaPriceService.GetPrice(name).DivineValue`, стоимость =
цена × attempts. Пишется в `item.TotalCostDiv` и `CostRecords[]` (шаг, валюта, попытки, дивы).
Итог батча: `BatchRunResult.TotalCostDiv` и строка лога «Итог: Done=…, Failed=…, Расход≈…d».

Известные дыры учёта (важно для реализации с нуля — НЕ повторять): вехи не учитываются
вовсе (ветка milestone не вызывает `AccumulateCost`), а `OmenActivation` возвращает
`Success()` с `Attempts=0` → стоимость оменов всегда 0 (см. 2.8).

### 1.15 Логирование

- `IProgress<string> log` — построчный отчёт в UI («[Батч] Стадия 3 …», «[CheckItem] не
  выполнено: …», расшифровки условий).
- `SessionLogger` — файл сессии; `WriteFileOnly` для каждого исхода шага батча,
  `InfoClipboard` — полный текст предмета после шага.
- jsonl-логи десекрейта (1.12).

---

## Раздел 2 — Проблемы текущей реализации

### 2.1 Условия пишутся «в обратном порядке» и требуют симуляции в голове

`PipelineStepDetector` сканирует шаги с конца, поэтому корректность детекции зависит от
инварианта, который нигде не проверяется и существует только в голове автора:
*EntryCondition каждого следующего шага должен быть «сильнее» (уже) всех предыдущих,
и никакое промежуточное состояние предмета не должно случайно удовлетворять условию
более позднего шага.*

Практически это значит: добавляя шаг 8 «Попытка удаления неподходящего третьего суффикса»,
автор обязан вписать в его entry не только «что нужно шагу 8», а всю накопленную историю:
`НЕ Count>=3 И есть крафтед +1 Suffix И суффиксов ровно 3 И …` — потому что иначе предмет
после шага 3 тоже совпадёт с шагом 8, или наоборот. Условия разбухают (в реальном рецепте
entry шага 19 — три клоза с Count из 10 членов), дублируются между шагами с ручным
копированием, и любое изменение стратегии требует переревизии всех условий ниже по списку.

### 2.2 «Что будет, если начнём не сначала» — детекция ненадёжна

Слабые места `Detect` + `InitializeStagesAsync`:

1. **Шаги без EntryCondition невидимы.** В «Новом рецепте» их 12 из 27 (шаги 1, 2, 6, 13,
   14, 20, 22–26). Предмет, чьё истинное место — такой шаг, будет приписан к другому шагу
   (последнему совпавшему с условием) или к шагу 0.
2. **Fallback `?? 0` опасен.** Нераспознанный предмет отправляется на шаг 0 — а шаг 0
   это `chaosCraft` без entry-проверки на исполнении: готовый дорогой предмет, который
   детектор не понял (например, из-за нового мода, отсутствующего в условиях), начнут
   заливать хаосами. Правильная реакция на «не распознан» — Failed/вопрос пользователю,
   а не «начать сначала».
3. **Правило n-1 — эвристика с ложными срабатываниями.** «CheckItem и OmenActivation не
   меняют предмет» — верно, но выбор «предпочесть более ранний» произволен: если entry
   у n-1 и n совпали, а между ними семантическая разница (омен уже активирован или нет —
   состояние ИГРЫ, не предмета!), детектор не может это узнать. Состояние «омен заряжен»
   в принципе не наблюдаемо через буфер — при рестарте посреди сшивки предмет будет
   определён на стадию омена и омен активируют второй раз (потеря 20+ дивов на Abyssal).
4. **Вехи детектируются частично.** Только `TravelToLocation` с `ExpectedLocation`
   корректируется по OCR локации. «Уже дошли до алтаря» (`WalkToPosition`), «UI reveal уже
   открыт» (`ClickTemplate`) — не детектируются; при рестарте батч повторит ходьбу из
   произвольной точки локации, что почти гарантированно уводит персонажа не туда
   (WalkToPosition — слепые зажатия клавиш от известной стартовой позиции).
5. **Коррекция по локации грубая.** Она продвигает на «веха+1» ВСЕ предметы со стадией ≤
   вехи — включая те, что по модам ещё не готовы к десекрейт-фазе (детектор мог верно
   поставить предмет на шаг 5, но раз мы в Well of Souls — его насильно перенесут на 14).

### 2.3 Тройная роль EntryCondition и свежепочиненный баг checkItem

`EntryCondition` одновременно: (а) пре-флайт перед действием, (б) вход детектора стадий,
(в) исторически — проверяемое условие `CheckItem`. Роли конфликтуют:

- Для (б) условие должно описывать «состояние, при котором предмет стоит на этом шаге»
  — то есть быть максимально ПОЛНЫМ (со всей историей, см. 2.1).
- Для (а) достаточно минимальной проверки безопасности («суффиксов ровно 3»).
- Для (в) это вообще другое: не «когда шаг уместен», а «что шаг проверяет».

Баг, который только что исправили, — прямое следствие (в): у `CheckItem` шаблон
«entry = что проверяем» конфликтовал с детектором, который читал тот же entry как «где
находится предмет». Фикс — `CheckItem` теперь берёт условие из `LoopUntil`
(`step.LoopUntil ?? step.EntryCondition`), но это добавило третью семантику уже полю
`LoopUntil`: «повторять до» у итеративных действий, «однократно проверить» у CheckItem.
Оба поля стали перегруженными, и правильное заполнение зависит от типа действия —
таблица из 1.3 обязана быть перед глазами.

### 2.4 Непоследовательная семантика действий

- `ChaosCraft`/`DivineCraft`/`AugAnnulCraft`/`ExaltCraft` **не проверяют** EntryCondition
  при выполнении — он там «только для детектора». `SimpleCurrency`/`OmenActivation`/`Bone`
  — проверяют. `DeliriumLiquid` — не проверяет (хотя по смыслу однотипен с SimpleCurrency).
  Автор рецепта не может предсказать поведение, не зная исходников.
- Failure означает разное: у `CheckItem` — «условие не выполнено» (нормальная развилка),
  у `SimpleCurrency` — «не настроены области» (ошибка конфигурации), у `DesecratePick` —
  «мод не выпал» (вероятностный исход), у `Travel` — «OCR не нашёл» (сбой среды).
  Один канал `OnFailure` на четыре принципиально разных класса событий; отличить
  «предмет не готов» от «сломался экран» невозможно ни в переходах, ни в статистике.
- Действия, которые «не умеют падать» (`SimpleCurrency` → Success даже при промахе клика,
  `SimpleAbyssalBone` → Success при недостигнутом LoopUntil, `OpenStash` → Success при
  ненайденной надписи), маскируют сбои; их приходится страховать checkItem-шагами вручную.
- `ManualPause` в раннере — no-op (сразу Success): модель обещает «показать сообщение и
  ждать», реализация — нет.

### 2.5 Хрупкие ссылки Step(N)

Переходы хранят абсолютные индексы. Вставка/удаление шага в середине молча ломает все
`Step(N)` ниже (и `AdjustStages`, и детектор — всё индексное). В реальном рецепте имена
шагов «Шаг 19»…«Шаг 26» уже не совпадают со своими фактическими индексами (шаг с именем
«Шаг 19» стоит на позиции 22) — прямое свидетельство, что рецепт редактировался и
нумерацию пришлось чинить руками. Валидатора корректности графа переходов нет
(есть только защита от выхода за диапазон в рантайме).

### 2.6 Спец-хаки, живущие вне модели

- **Сшивка OmenActivation** — жёстко зашитый в `BatchPipelineRunner` частный случай
  («выполни следующий шаг этого предмета немедленно»). Понятие «пара шагов атомарна»
  в модели отсутствует — оно закодировано условием `step.Action == OmenActivation`.
  Появится другой «заряжающий» шаг — придётся дописывать runner.
- **Правило n-1** детектора — хардкод списка «безобидных» действий.
- **Milestone-переход применяется всем поровну** — один `StepOutcome` на группу; если веха
  наполовину сработала (двое доехали, интерфейс у третьего закрылся) — модель этого не
  выражает.
- `_pipelineItemClass` устанавливается только в `RunAsync`; батч зовёт `ExecuteStepAsync`
  напрямую, и авто-подстановка класса в DesecratePick в батче не работает (спасает только
  явно заполненный `ItemClass` в конфиге шага).

### 2.7 Сложность отладки

- Состояние предмета существует только как текст буфера в момент чтения; между шагами
  «истина» нигде не фиксируется. Разбирая инцидент, приходится реконструировать историю по
  `SessionLogger`-строкам и `InfoClipboard`-дампам.
- Расшифровки условий (`explanation`) хороши, но условие из 10-членного Count в entry —
  это простыня, в которой причина «почему предмет ушёл в step(5)» тонет.
- Нет режима «dry run»: невозможно прогнать предмет (или текстовый снапшот) через пайплайн
  и получить трассу «шаг → условия → переход» без реальных кликов. Детектор возвращает
  Explanation, но полного симулятора переходов нет.
- Батч-лог не пишет ключевого: на какой стадии КАЖДЫЙ предмет и почему барьер держит группу
  («Нет прогресса — возможен deadlock» без указания, кто кого ждёт).

### 2.8 Ошибки учёта стоимости

- `OmenActivation` возвращает `Attempts=0` → стоимость оменов = 0. Abyssal Echoes по 22d
  за штуку в статистику не попадает — P&L батча систематически занижен.
- Ветка вехи вообще не вызывает `AccumulateCost` (та же проблема для оменов ×N).
- `AccumulateCost` только при `Succeeded` — валюта, потраченная в неудачной попытке
  (chaosCraft, упёршийся в MaxIterations, реально сжёг все орбы), не учитывается.

### 2.9 Прочие находки

- **GuardCondition и EnableStepRecognition не работают в батче**: обе проверки живут в
  `CraftPipelineRunner.RunAsync`, а батч вызывает `ExecuteStepAsync` напрямую.
  Предохранители, спроектированные для защиты дорогих предметов, в главном сценарии
  использования выключены.
- Барьер + откат назад = живой лок для медленных стратегий: один предмет, зациклившийся
  между шагами 3↔8, останавливает всю группу перед вехой на неограниченное время
  (лимит только на суммарные выполнения).
- Probe «пустой буфер → Failed» не отличает «ячейка пуста» от «клипборд не успел» —
  один таймаут превращает живой предмет в Failed без повторной попытки.
- `MaxIterations` имеет разный смысл: лимит цикла (chaosCraft), лимит костей (bone),
  игнорируется (simpleCurrency, checkItem) — а default 100 у всех.

---

## Раздел 3 — Три варианта архитектуры

Ниже три подхода. Общая для всех терминология: **детект** — определение места предмета при
старте; **шаг** — единица действия; **состояние** — наблюдаемые через буфер свойства
предмета (класс, аффиксы, десекрейт-слот, счётчики префиксов/суффиксов).

Важное ограничение всех вариантов, которое надо признать честно: часть состояния
**не наблюдаема через предмет** (заряженный омен, открытый UI, позиция персонажа).
Ни один детект не восстановит это из буфера. Правильный ответ — минимизировать зону
ненаблюдаемости (атомарные пары «омен+трата» как один шаг) и детектировать среду отдельно
(OCR локации/стэша), а не притворяться, что предмет знает всё.

---

### Вариант A — Явный граф состояний (State Machine)

**Суть.** Рецепт описывается не списком шагов, а словарём именованных состояний предмета.
Каждое состояние имеет предикат (условие на предмете) и переходы: `state + действие →
ожидаемые исходы → следующее состояние`.

**Конфигурация:**

```yaml
itemClass: Time-Lost Sapphire Jewels
states:
  TwoSuffixes:
    match: { count: { min: 2, of: [of Potency, of Annihilating, of Unmaking, of Lengthening, of Osmosis, of Mind], includeFractured: true } }
    do: { action: chaosCraft, until: ThreeSuffixes, maxIter: 200 }
    onMaxIter: fail

  ThreeSuffixes:
    match: { count: { min: 3, of: [...10 имён...], includeFractured: true },
             not: { crafted: "+1 Suffix Modifier allowed" } }
    do: { action: simpleCurrency, currency: Exalted Orb }   # донорный префикс
    outcomes:
      - when: { affixCount: { scope: prefixes, min: 2 } } → ReadyForRadius
      - otherwise → ThreeSuffixes                            # повторить

  HasDesecrateSlot:
    match: { hasDesecrate: prefix, crafted: "Upgrades Radius to Very Large" }
    do: { action: desecrateReveal, pick: { name: Lightless, line: "Notable Passive Skills in Radius", min: 2 } }
    outcomes:
      - success → Done
      - failure → NeedsRadiusRecheck
```

**Детект стартовой стадии:** тривиален и надёжен — вычислить `match` каждого состояния,
предмет находится в том, чей предикат истинен. Требование: предикаты состояний **взаимно
исключающие и полные** (это можно проверять статически: для конечного словаря состояний —
попарная выводимая несовместимость, плюс тест-набор снапшотов предметов).

**Условия:** каждое состояние описывается «положительно» — что есть на предмете сейчас.
Нет обратного порядка: автор перечисляет фотографии состояний, а не дифференциальную
историю. Но платой становится комбинаторика: реальный рецепт содержит скрытые оси
(радиус есть/нет × десекрейт есть/нет × 3-й суффикс хороший/плохой × крафтед +1 есть/нет)
— честный граф это 10–15 состояний с предикатами, которые всё равно повторяют друг друга.

**Старт с середины:** идеален по определению — «середины» нет, есть текущее состояние.

**Плюсы:** максимально надёжный детект; статическая проверка полноты/непересечения;
понятная визуализация (граф); отладка = «в каком состоянии предмет» одним вычислением.
**Минусы:** полное переписывание моделей, UI и рецептов; комбинаторный рост состояний;
ненаблюдаемые состояния (омен заряжен) в модель не ложатся — нужны псевдо-состояния
среды; вехи/батч-барьер требуют отдельного слоя поверх (веха — свойство пути, не
состояния предмета).

**Сравнение с текущим:** решает 2.1, 2.2, 2.3 радикально, но нарушает критерий
реалистичной миграции: ни один существующий рецепт и почти ни один класс не переиспользуются.

---

### Вариант B — Декларативный пайплайн с предусловиями (requires / ensures)

**Суть.** Оставить линейный список шагов и типы действий как есть, но у каждого шага
формализовать два условия с жёсткой, единой для всех действий семантикой:

- `requires` — что должно быть истинно на предмете, чтобы шаг имело смысл выполнять
  (замена EntryCondition, но проверяется **одинаково для всех** типов действий);
- `ensures` — что станет истинно после успешного шага (замена LoopUntil; для
  одноразовых действий — это пост-проверка результата, для итеративных — условие цикла,
  для checkItem — проверяемое условие; семантика едина: «шаг успешен ⇔ ensures выполнен»).

Ключевое правило исполнения:

```
перед шагом:
    if ensures уже истинен → SKIP (шаг idempotent-пропущен, переход OnSuccess)
    if requires ложен      → Failure → OnFailure
выполнить действие (для итеративных — до ensures или MaxIterations)
после:  Success ⇔ ensures истинен
```

**Конфигурация шага (JSON, эволюция текущего формата):**

```jsonc
{
  "name": "Добавление третьего суффикса",
  "action": "simpleCurrency", "currencyId": "Exalted Orb",
  "requires": { /* CraftConditionPlan: суффиксов == 2, есть крафтед +1 */ },
  "ensures":  { /* суффиксов == 3 */ },
  "onSuccess": { "target": "next" },
  "onFailure": { "target": "step", "stepRef": "annul-third-suffix" }   // ссылки по ИМЕНИ, не индексу
}
```

Для вех `ensures` описывает **среду**, а не предмет, отдельным блоком:

```jsonc
{ "name": "well-of-souls", "action": "travelToLocation",
  "ensuresEnv": { "location": "the well of soul" } }        // проверяется OCR-ом
{ "name": "open-reveal-ui", "action": "clickTemplate",
  "ensuresEnv": { "templateVisible": "reveal_header.png" } } // «UI открыт» = шаблон виден
```

**Детект стартовой стадии:** сканирование **с начала**:

```
for i in 0..N-1:
    if шаг[i].ensures задан и истинен → continue          // шаг уже сделан
    if шаг[i].requires истинен        → вернуть i          // первый несделанный выполнимый шаг
    → предмет в неописанном состоянии → Failed + отчёт (НЕ «шаг 0»)
```

Это прямой порядок мышления: «идём по рецепту сверху вниз, пропускаем сделанное,
останавливаемся на первом несделанном». Никакой «последний совпавший с конца» и никакого
правила n-1: CheckItem-шаги с выполненным `ensures` просто пропускаются. Вехи детектятся
своим `ensuresEnv` (OCR локации — уже есть; видимость шаблона — уже есть TemplateMatcher;
«стэш открыт» — уже есть OCR-проверка в OpenStash) — механизмы существуют, они лишь
становятся декларативными.

**Проблема «начать с середины»** решается тем же правилом SKIP в рантайме: даже если
детект ошибся на шаг раньше, шаги с истинным `ensures` промотаются без действий.
Двойная защита: детект + идемпотентный пропуск.

**Условия — проще писать:** `requires` описывает только вход ЭТОГО шага (без накопленной
истории — историю гарантирует порядок списка и пропуск сделанного), `ensures` — только его
результат. Дифференциальные условия короче «фотографий состояния» варианта A. Дублирование
между entry соседних шагов исчезает: то, что раньше копировалось в entry шагов 9, 10, 11,
теперь — `ensures` шага 7, который просто «уже истинен».

**Отладка:** каждая остановка объяснима тройкой «шаг X: ensures=false (расшифровка),
requires=true (расшифровка) → выполняем». Плюс дешёвый dry-run: детект + проверка
requires/ensures по всем шагам без кликов — уже даёт полную карту «где предмет и что будет».

**Миграция — реалистичная:**

1. `LoopUntil` → `ensures` у итеративных действий и checkItem — переименование
   (недавний фикс checkItem уже сделал первый шаг в эту сторону).
2. `EntryCondition` → `requires` — переименование + включение проверки для всех действий.
3. Детектор — переписать (~70 строк) на прямой скан.
4. SKIP-правило — одна проверка в начале `ExecuteStepAsync`/батч-цикла.
5. `stepRef` по имени — слой разрешения имён при загрузке (обратная совместимость с
   `stepIndex` сохраняется).
6. Старые рецепты работают через fallback (нет `ensures` → поведение как раньше),
   мигрируются по одному.

**Плюсы:** прямой порядок; надёжный старт (детект + SKIP); единая семантика полей для всех
действий (закрывает 2.3, 2.4); вехи детектируются через ensuresEnv (закрывает 2.2 п.4);
переиспользуются CraftConditionPlan, evaluator, все действия, UI условий; ссылки по имени
закрывают 2.5. **Минусы:** для шагов вида «просто клик» придумать честный `ensures`
не всегда возможно (допустимо `ensures: null` → шаг всегда выполняется — как сейчас);
несделанный/невыполнимый шаг посреди списка требует аккуратной обработки циклов
(`Step(name)`-переходы назад остаются, барьер батча не меняется); дисциплина «ensures
шага n ⊆ requires шага n+1» желательна, но не навязана (можно добавить линтер).

---

### Вариант C — Линейный пайплайн + IdempotentGuard

**Суть.** Минимальное вмешательство: текущая модель целиком сохраняется, к шагу добавляется
одно поле `guard` — условие «шаг уже сделан». Исполнение всегда начинается с шага 0;
шаги с истинным guard пропускаются (переход OnSuccess без действия). Детектор стадий
удаляется вовсе.

```jsonc
{ "name": "Добавление третьего суффикса", "action": "simpleCurrency", "currencyId": "Exalted Orb",
  "guard": { /* суффиксов >= 3 */ },        // уже сделан → пропустить
  "entryCondition": { ... },                 // как раньше
  "onSuccess": {...}, "onFailure": {...} }
```

**Детект:** не нужен — «промотка» от нуля. Для вех guard = те же OCR-проверки
(локация/стэш/шаблон), выполняемые в момент достижения шага.

**Условия:** entry остаётся со всеми старыми проблемами (2.1 частично сохраняется:
entry по-прежнему двусмысленный, checkItem-семантика остаётся особой). Guard — это
фактически дубль LoopUntil у большинства шагов: у chaosCraft guard ≡ LoopUntil, писать
придётся оба или вводить `"guard": "sameAsLoopUntil"`.

**Старт с середины:** работает, но промотка не бесплатна: каждый guard = чтение буфера
(в батче × число предметов × число шагов до фактической позиции) — десятки Ctrl+Alt+C
перед первым полезным действием. Для вех — промотка через travel-шаги требует OCR на каждый.
Хуже: пайплайны с циклами назад (`Step(0)` из шага 5) при рестарте промотаются на позицию,
которая зависит от того, какие guard истинны, а не от того, в какой фазе цикла предмет был —
для немонотонных рецептов (аннул временно ПОРТИТ условие ранних шагов) промотка может
остановиться слишком рано и повторить разрушительное действие.

**Плюсы:** самая дешёвая миграция (одно поле + пропуск), детектор и правило n-1 удаляются,
поведение легко объяснимо. **Минусы:** дублирование guard/LoopUntil; не чинит семантический
хаос entry/loopUntil (2.3, 2.4); немонотонные рецепты промотка обслуживает неверно;
стоимость промотки в батче; условия по-прежнему пишутся с оглядкой на всю историю.

---

### Сравнение

| Критерий | Текущая | A (State Machine) | B (requires/ensures) | C (guard) |
|---|---|---|---|---|
| Писать условия без «обратного порядка» | ✗ | ✓✓ (но комбинаторика состояний) | ✓✓ (диффы: вход/выход шага) | ✗ (entry как раньше) |
| Надёжный старт из любого состояния | ✗ (детект с конца, `?? 0`, n-1) | ✓✓ | ✓ детект с начала + SKIP | ~ (промотка; немонотонность ломает) |
| Детект вех (локация/UI/стэш) | частично (только Travel) | нужен отдельный слой | ✓ ensuresEnv, механизмы уже есть | ✓ guard-OCR, но дорого |
| Единая семантика полей для всех действий | ✗ | ✓ | ✓✓ | ✗ |
| Стоимость миграции | — | очень высокая (модель+UI+рецепты) | средняя (переименование+детектор+SKIP) | низкая |
| Отладка | простыни entry | «в каком состоянии» одним вычислением | тройка ensures/requires/действие + dry-run | «какой guard пропустил» |
| Хрупкие Step(N) | ✗ | n/a (граф) | ✓ ссылки по имени | ✗ (не трогает) |
| Циклы/откаты назад | Step(N) | естественны | Step(name), без изменений | конфликтуют с промоткой |
| Батч-барьер/вехи | хардкод | переписывать | без изменений | без изменений |

### Победитель: Вариант B — декларативный пайплайн requires/ensures

Обоснование по четырём заявленным критериям:

1. **Конфигурировать без «обратного порядка»** — да: автор описывает каждый шаг локально
   («нужно X, получится Y»), порядок гарантирует список, а не нарастающие копипаст-условия.
   Вариант A тоже решает это, но заменяет проблему на комбинаторику состояний;
   C не решает вовсе.
2. **Надёжный старт с любого состояния** — детект прямым сканом «первый шаг, где ensures
   ложен, а requires истинен» + рантайм-SKIP как вторая линия обороны + `ensuresEnv` для
   вех + честный отказ (Failed с отчётом) вместо `?? 0` для нераспознанных предметов.
   Единственный класс, который не решает никто, — ненаблюдаемое состояние игры (заряженный
   омен); в B он минимизируется штатным средством: пара «омен + трата» объявляется одним
   атомарным шагом (сшивка перестаёт быть хардкодом раннера и становится свойством модели).
3. **Реалистичная миграция** — `LoopUntil` уже фактически стал `ensures` для checkItem
   после последнего фикса; `EntryCondition` переименовывается в `requires` с включением
   проверки везде; `CraftConditionPlan`, evaluator, `ItemParser`, все 20 действий, UI
   редактора условий, батч-барьер и вехи переиспользуются как есть. Старые рецепты
   продолжают работать через fallback.
4. **Понятность при отладке** — каждое решение раннера объяснимо тремя строками
   (ensures? requires? исход действия?), каждая — с готовой расшифровкой из
   `CraftConditionEvaluator`. Появляется дешёвый dry-run без кликов.

Рекомендуемый порядок внедрения (инкрементально, каждая фаза самостоятельно полезна):

1. **Фаза 1 — семантика.** Ввести `requires`/`ensures` как алиасы entry/loopUntil при
   загрузке; включить проверку `requires` для ВСЕХ действий; ввести правило
   «Success ⇔ ensures» там, где ensures задан. Ввести `ItemStateCache` (см. B.2) —
   без него единая проверка requires/ensures для всех действий умножит число чтений
   буфера. Починить попутно: Attempts у OmenActivation,
   AccumulateCost для вех и неуспешных попыток, гарды пайплайна в батче (2.8, 2.9).
2. **Фаза 2 — детект.** Новый прямой детектор + SKIP-правило; `?? 0` → Failed с отчётом;
   удалить правило n-1; `ensuresEnv` для Travel/OpenStash/ClickTemplate/WalkToPosition
   (для Walk — честный `ensuresEnv: null` = «не детектируется, требует ручного подтверждения
   при старте с середины»).
3. **Фаза 3 — качество жизни.** `stepRef` по имени, линтер рецепта (недостижимые шаги,
   ссылки в никуда, ensures∩requires-несостыковки), dry-run режим, атомарные группы шагов
   вместо хардкода сшивки оменов.

---

### B.1 — Два условия шага: requires и ensures — детальная механика

Да, у каждого шага **две проверки состояния**: на входе (`requires`) и на выходе
(`ensures`). Но «две проверки» ≠ «два чтения буфера»: проверки вычисляются над
кэшированным состоянием предмета, и чтений на шаг в типичном случае 0 или 1.

**Что читать и когда (clipboard)**

У каждого шага есть максимум одно чтение буфера обмена (Ctrl+Alt+C) на каждую фазу.
Читать надо, когда:

- `requires` требует проверки — читаем один раз, получаем `cachedItemText + cachedParsedItem`;
- `ensures` требует проверки ДО действия (SKIP-правило) — используем то же кэшированное
  состояние, повторного чтения нет;
- после действия, которое **меняет предмет**, — читаем снова и обновляем кэш.

**Алгоритм выполнения шага:**

```
// Шаг начинается с имеющегося cachedParsedItem (может быть null, если кэш пуст)

1. PRE-CHECK (если requires задан И кэш актуален):
   - если кэш пуст или устарел → прочитать буфер → обновить кэш
   - вычислить requires(cachedParsedItem)
   - если false → Failure → OnFailure (орб не тратится)

2. SKIP-CHECK (если ensures задан):
   - использовать тот же cachedParsedItem (уже прочитан на шаге 1, или читать если ещё нет)
   - вычислить ensures(cachedParsedItem)
   - если true → SUCCESS без действия → OnSuccess (idempotent skip)
   - если false → переходим к действию

3. EXECUTE ACTION:
   - выполнить действие (клик, цикл, перемещение и т.д.)
   - если действие МЕНЯЕТ предмет → пометить кэш как устаревший (или обновить сразу)
   - если действие НЕ МЕНЯЕТ предмет → кэш остаётся актуальным

4. POST-CHECK (если ensures задан):
   - если кэш устарел → прочитать буфер → обновить кэш
   - вычислить ensures(cachedParsedItem)
   - если true  → Success → OnSuccess
   - если false → Failure → OnFailure (действие не достигло цели)

   если ensures не задан → Success всегда (как сейчас)
```

**Ключевые правила:**

- За один шаг — максимум 2 чтения буфера: до и после действия. На практике часто 0 или 1.
- Если предыдущий шаг уже закончился чтением (обновил кэш), шаг 1 не читает повторно —
  кэш актуален.
- Итеративные действия (ChaosCraft, DivineCraft и т.д.) читают буфер ВНУТРИ своего цикла —
  post-check использует их финальное чтение, повторного чтения нет.

**Какие действия МЕНЯЮТ предмет** (кэш инвалидируется):

- `ChaosCraft`, `DivineCraft`, `AugAnnulCraft`, `ExaltCraft`
- `SimpleCurrency`, `SimpleExalt`, `SimpleAnnul`, `SimpleChaos`, `DeliriumLiquid`,
  `SimpleAbyssalBone`
- `DesecrateReveal` (предмет уходит в слот и возвращается изменённым)

**Какие действия НЕ МЕНЯЮТ предмет** (кэш остаётся валидным):

- `CheckItem` (только читает)
- `OmenActivation` (перемещает омен, предмет не трогает)
- `TravelToLocation`, `WalkToPosition`, `OpenStash`, `ClickTemplate`, `ClickRegion`,
  `CtrlClickItem` (механические)
- `ManualPause`

**Пример потока для 3 подряд идущих шагов (из реального рецепта):**

```
Шаг N:   SimpleCurrency (ALC) → меняет предмет → POST-CHECK читает → кэш обновлён
Шаг N+1: CheckItem → requires берёт кэш из N (не читает!) → ensures = проверяемое условие
         → Success/Failure
Шаг N+2: SimpleCurrency (Annul) → requires берёт кэш из N+1 → ...
```

В текущей реализации шаги N+1 и N+2 каждый читают буфер заново — это паразитные
копирования.

---

### B.2 — ItemStateCache: устранение паразитных чтений буфера

**Проблема.** В текущей реализации буфер читается слишком часто:

1. `BatchPipelineRunner` делает «probe read» перед каждым шагом (строки 164–174
   `BatchPipelineRunner.cs`) — убеждается, что ячейка не пуста;
2. каждый `ExecuteStepAsync` читает буфер самостоятельно для `CheckEntryConditionAsync`;
3. `SimpleCurrency` читает буфер ПОСЛЕ применения (возвращает `FinalItemText`);
4. следующий шаг снова читает тот же буфер.

Для пайплайна из 27 шагов, 5 предметов — это сотни лишних Ctrl+Alt+C.

**Решение: `ItemStateCache` — объект уровня шага/предмета:**

```csharp
internal sealed class ItemStateCache
{
    public string?      Text        { get; private set; }
    public ParsedItem?  Parsed      { get; private set; }
    public bool         IsStale     { get; private set; } = true;

    // Вызывается, когда действие изменило предмет
    public void Invalidate() => IsStale = true;

    // Вызывается с результатом чтения буфера
    public void Update(string text)
    {
        Text    = text;
        Parsed  = ItemParser.Parse(text);
        IsStale = false;
    }

    // Обеспечить актуальность: прочитать, если устарел
    public async Task EnsureFreshAsync(ScreenRect itemArea, IClipboardReader reader, CancellationToken ct)
    {
        if (!IsStale) return;
        var text = await reader.ReadItemClipboardTextAsync(itemArea, ct);
        Update(text);
    }
}
```

**Жизненный цикл кэша:**

- создаётся один раз на предмет в начале батч-рана (или на сессию в одиночном режиме);
- `IsStale = true` изначально и после любого state-changing action;
- `EnsureFreshAsync` вызывается в начале PRE-CHECK и POST-CHECK — и только тогда;
- probe read в `BatchPipelineRunner` заменяется на `cache.EnsureFreshAsync()` +
  проверка `string.IsNullOrWhiteSpace(cache.Text)`.

**Передача кэша через систему:**

`ItemStateCache` передаётся в `ExecuteStepAsync` как параметр контекста:

```csharp
// Было:
StepOutcome ExecuteStepAsync(step, screen, log, ct)

// Станет:
StepOutcome ExecuteStepAsync(step, screen, cache, log, ct)
```

Батч передаёт кэш предмета, одиночный режим — один кэш на всё время.

**Что меняется в действиях:**

- итеративные сервисы (ChaosCraft и т.д.) в конце своего цикла сохраняют последнее
  прочитанное состояние в `cache.Update(finalText)` — POST-CHECK использует это без
  повторного чтения;
- `SimpleCurrency`: применил → `cache.Invalidate()` (или `cache.Update()`, если читает
  после);
- `CheckItem`: только `cache.EnsureFreshAsync()` + вычислить ensures → не трогает предмет
  → `IsStale` остаётся `false`.

**Выигрыш на реальном пайплайне (27 шагов, 5 предметов):**

- текущее состояние: ~3–5 чтений буфера на шаг = ~100–200+ лишних Ctrl+Alt+C за сессию;
- после: 0–1 чтение на шаг (probe read или post-check), максимум 2;
- критично для шагов-гирлянд типа checkItem → checkItem → OmenActivation → checkItem:
  здесь сейчас 4 чтения, станет 1.
