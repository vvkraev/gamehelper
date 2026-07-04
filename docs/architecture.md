# Architecture — GameHelper

WPF-приложение (.NET 10, Windows) для автоматизации крафта предметов в Path of Exile 2.
Работает через Win32 API: эмуляция мыши/клавиатуры + буфер обмена. Сетевых запросов нет.

---

## Слои

```
┌─────────────────────────────────────────────────────────────┐
│  UI Layer                                                   │
│  MainWindow.xaml/.cs · диалоги · XAML-привязки             │
├─────────────────────────────────────────────────────────────┤
│  ViewModel Layer                                            │
│  MainWindowViewModel · ViewModelBase · RelayCommand         │
├─────────────────────────────────────────────────────────────┤
│  Orchestration                                              │
│  CraftOrchestrator · CraftSessionContext · CraftSessionResult│
├────────────────────────────────┬────────────────────────────┤
│  Craft Services                │  Support Services          │
│  ChaosCraftService             │  SessionLogger             │
│  AugAnnulCraftService          │  AffixLibrary              │
│  ExaltationCraftService…       │  ItemParser                │
│  FracturingOrbService          │  CraftConditionEvaluator   │
│  SharpenService                │  PoeNinjaPriceService      │
│  ReforgeService                │  TradeHistoryService       │
│  AutoReforgeService            │  RepricingService          │
│  ChancingService               │  NetworthService           │
│  OmenActivationService         │  ReferenceStatsService     │
├────────────────────────────────┴────────────────────────────┤
│  Infrastructure                                             │
│  App.xaml.cs (DI) · AppSettings · SettingsStore            │
│  ProjectPaths · AffixLibrary (static + адаптеры)           │
├─────────────────────────────────────────────────────────────┤
│  Native Layer                                               │
│  Win32Input · GlobalHotkey · ProcessForeground              │
└─────────────────────────────────────────────────────────────┘
```

---

## DI-контейнер

Собирается в `App.xaml.cs → BuildServiceProvider()` через `Microsoft.Extensions.DependencyInjection`.
Все зависимости — синглтоны. `MainWindow` получается из контейнера и открывается в `Application_Startup`.

```
ISessionLogger      ← SessionLoggerAdapter   → static SessionLogger
IProjectPaths       ← ProjectPathsAdapter    → static ProjectPaths
IAffixLibrary       ← AffixLibraryAdapter    → static AffixLibrary

OmenActivationService
ChaosCraftService
AugAnnulCraftService
ExaltationCraftServiceFracturedSide
SharpenService
FracturingOrbService
ReforgeService
AutoReforgeService
RepricingService
ChancingService
CraftOrchestrator   ← внедрение 5 craft-сервисов выше
MainWindowViewModel
MainWindow          ← конструктор принимает все вышеперечисленные
```

Статические адаптеры позволяют существующим call-site'ам (169 мест) не менять код,
пока новый код работает через интерфейсы.

---

## Главный цикл крафта

```
MainWindow.StartBtn_Click
  │
  ├─ собирает CraftSessionContext (Mode, ItemCells, Plan, ...)
  │
  └─ CraftOrchestrator.RunAsync(context, progress, ct)
       │
       ├─ ApplySettings() — копирует задержки из AppSettings в сервисы
       │
       ├─ for each ItemCell:
       │    ├─ CraftService.PrecheckAsync()  ← Ctrl+Alt+C, парсинг, проверка условия
       │    │    CraftPrecheckOutcome:
       │    │      Ready          → RunAsync
       │    │      AlreadySatisfied → пропустить ячейку
       │    │      EmptyCell      → пропустить ячейку
       │    │      NonMagicCell   → пропустить ячейку
       │    │      Failed         → abort сессии
       │    │
       │    └─ CraftService.RunAsync()       ← основной цикл орбов
       │         возвращает CraftResult { Success, Attempts, StopReason }
       │
       └─ возвращает CraftSessionResult в MainWindow
```

Dispatch по `CraftMode` enum (`Chaos | AugAnnul | Exaltation | FracturingOrb`).

---

## Компоненты

### UI

| Файл | Роль |
|---|---|
| `MainWindow.xaml.cs` | Точка входа UI — координирует сервисы, ~5700 строк |
| `MainWindowViewModel.cs` | 28 bindable-свойств: running-state, Can*, статус-строки, output TextBlocks |
| `CraftConditionWindow.xaml.cs` | Редактор условия остановки + Monte Carlo расчёт вероятности |
| `ItemParsingWindow.xaml.cs` | Ручной парсинг предмета из буфера и слияние в affix_library |
| `RegionPickerWindow.xaml.cs` | Захват области экрана через drag-and-drop поверх игры |
| `CraftLogWindow.xaml.cs` | Просмотр активного WIP-лога крафта |

### Сервисы крафта

| Файл | Что делает |
|---|---|
| `ChaosCraftService` | Shift+ПКМ орб → ЛКМ предмет → Ctrl+Alt+C → проверка |
| `AugAnnulCraftService` | Aug если нет нужного типа аффикса, Annul если есть но условие не выполнено |
| `ExaltationCraftServiceFracturedSide` | Exalt → проверка; при неудаче Annul через Sinistral/Dextral/Greater омены |
| `FracturingOrbService` | Fracturing Orb с проверкой IsFractured аффикса |
| `SharpenService` | ПКМ по области заточки → Shift+ЛКМ по сетке ячеек N раз |
| `OmenActivationService` | ПКМ по омену перед Exalt (Sinistral/Dextral/Greater) |
| `ReforgeService` | Перековка на верстаке (Reforging Bench) |
| `AutoReforgeService` | Автоматическая перековка со стаком предметов из сташа |
| `ChancingService` | Orb of Chance с подсчётом Unique/Normal/Magic/Rare |

### Поддерживающие сервисы

| Файл | Что делает |
|---|---|
| `ItemParser` | Парсит текст предмета из буфера → `ParsedItem` (аффиксы, редкость, класс, iLvl) |
| `CraftConditionEvaluator` | Проверяет текст буфера против `CraftConditionPlan` (текстовая проверка) |
| `ParsedItemCraftEvaluator` | Проверяет `ParsedItem` против `CraftConditionPlan` (структурная проверка) |
| `AffixLibrary` | Загрузка и поиск по `affix_library.json`; `FindStatIndexInEntry`, `MergeFromParsedItem` |
| `SessionLogger` | Логирование сессии в UI и файл; статический синглтон + `ISessionLogger` адаптер |
| `PoeNinjaPriceService` | Загрузка цен орбов/катализаторов/оменов из `poe_ninja_prices.json` |
| `TradeHistoryService` | Работа с GGG trade history API; хранение в `sales_history.json` |
| `RepricingService` | OCR имени торговца → авто-переоценка листингов |
| `ReferenceStatsService` | Агрегат статистики крафта из `affix_stats.json` и `catalyst_reforge_stats.json` |
| `RecipeStore` | Загрузка/сохранение рецептов крафта из `Recipes/*.json` |
| `CraftOrchestrator` | Итерация по ячейкам + dispatch на нужный сервис |

### Модель условия остановки

```
CraftConditionPlan
  └─ ExpectedItemClass       (должен совпасть с классом предмета)
  └─ OrAlternatives[]        (ИЛИ между группами)
       └─ CraftAndGroup
            └─ Clauses[]     (И между клозами)
                 └─ CraftClause
                      Kind: Single | Sum | Count | WholeModifier
```

Два эвалюатора для одной модели:
- `CraftConditionEvaluator` — по сырому тексту буфера (быстро, без полного парсинга)
- `ParsedItemCraftEvaluator` — по `ParsedItem` (точно, используется в финальной проверке)

### Инфраструктура

| Файл | Что делает |
|---|---|
| `AppSettings` | Все настройки приложения — области экрана, задержки, условие крафта |
| `SettingsStore` | Сериализация `AppSettings` ↔ `settings.json` |
| `ProjectPaths` | Пути к корню проекта, папке логов |
| `ScreenRect` | Прямоугольник экрана; `GetRandomInteriorPoint()` для имитации человеческих кликов |
| `ViewModelBase` | `INotifyPropertyChanged` + `SetProperty<T>` с `CallerMemberName` |
| `RelayCommand` | `ICommand` с `CommandManager.RequerySuggested` |

### Native

| Файл | Что делает |
|---|---|
| `Win32Input` | `MoveTo`, `ClickLeft`, `ClickRight`, `ShiftDown/Up`, `Ctrl+Alt+C` через WinAPI |
| `GlobalHotkey` | Регистрация/снятие глобальных хоткеев (`RegisterHotKey`) |
| `ProcessForeground` | Вывод окна PoE2 на передний план перед вводом |
| `WindowTopHelper` | Управление Topmost для окна приложения |

---

## Данные

| Файл | Назначение |
|---|---|
| `affix_library.json` | 6722 записей аффиксов PoE2 — источник истины, не генерировать |
| `rune_affix_overrides.json` | Аффиксы рун с реальными весами (вместо weight=1 из poe2db) |
| `affix_stats.json` | Накопленная статистика крафта (наблюдаемые частоты аффиксов) |
| `catalyst_reforge_stats.json` | Статистика перековки с катализаторами |
| `poe_ninja_prices.json` | Кэш цен poe.ninja (орбы, омены, катализаторы) |
| `sales_history.json` | История продаж через GGG Merchant History API |
| `craft_ledger.json` | Ручной журнал шагов крафта с расходами |
| `settings.json` | Пользовательские настройки (сериализованный `AppSettings`) |
| `Recipes/*.json` | Сохранённые рецепты `CraftConditionPlan` |

---

## Тайминги и джиттер

Все задержки критичны для стабильной работы в игре:

- `MouseActionDelayMs` (по умолчанию 80 мс) — пауза после каждого движения мыши и клика
- `ClipboardDelayMs` (по умолчанию 220 мс) — ожидание после Ctrl+Alt+C перед чтением буфера
- `HoverSettleBeforeClipboardMs` (120 мс) — пауза при смене ячейки перед копированием
- `DelayJitterFraction = 0.30` — джиттер ±30% к каждой задержке для имитации человеческого ввода

`CancellationToken` проверяется после каждого `await Task.Delay(...)` во всех сервисах.
При отмене Shift всегда освобождается в `finally`-блоке.

---

## Связанные документы

| Файл | Содержимое |
|---|---|
| `docs/SRS.md` | Требования к поведению |
| `docs/GAME_MECHANICS.md` | Механики PoE2 (орбы, омены, руны, Desecrate) |
| `docs/ITEM_PARSING.md` | Формат текста предмета из буфера |
| `docs/CRAFTING_STRATEGIES.md` | Зачем нужен каждый режим крафта |
| `docs/CRAFT_CONDITION_AFFIX_MATCH_ASCII.txt` | Модель условия остановки (ASCII) |
| `docs/*_FLOW_ASCII.txt` | ASCII-диаграммы потоков каждого сервиса |
