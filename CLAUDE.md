# CLAUDE.md — GameHelper

Инструкции для AI-ассистента. Читай этот файл перед любой работой с проектом.

---

## Что это за проект

WPF-приложение (.NET 10, Windows) для автоматизации крафта предметов и торговли таблетками в **Path of Exile 2**.
Работает через Win32 API (эмуляция мыши/клавиатуры) и буфер обмена — никаких читов, только клики.

**Два основных модуля:**
- **Крафт** — циклы Chaos/Aug+Annul/Exalt/Divine, визард условий остановки, пакетный крафт по стадиям
- **TabFlow** — цикл торговли таблетками: Buy (TradeBot) → Fill→Craft→Scan→List → Reprice/ReforgeQueue → Reforge→Stash

Подробно: [`docs/SRS.md`](docs/SRS.md) | Планы: [`docs/ROADMAP.md`](docs/ROADMAP.md) | Бэклог: [`BACKLOG.md`](BACKLOG.md) | TabFlow: [`vault/tabflow/roadmap.md`](vault/tabflow/roadmap.md)

---

## Ключевые файлы

| Файл / папка | Роль |
|---|---|
| `MainWindow.xaml.cs` | Точка входа UI — координирует все сервисы (~8800 строк; MVVM ✓; ~40% занимает TabFlow) |
| `AppSettings.cs` | Все настройки приложения — области экрана, задержки, условие крафта |
| `SettingsStore.cs` | Сериализация `AppSettings` ↔ `settings.json` |
| `Services/ChaosCraftService.cs` | Основной цикл Chaos Orb крафта |
| `Services/AugAnnulCraftService.cs` | Цикл Aug+Annul крафта |
| `Services/ExaltationCraftServiceFracturedSide.cs` | Exalt крафт с управлением омнами |
| `Services/SharpenService.cs` | Заточка предметов по сетке |
| `Services/OmenActivationService.cs` | Активация омена перед экзальтом |
| `Services/CraftConditionModels.cs` | Модели условия остановки (CraftConditionPlan, CraftAndGroup, CraftClause) |
| `Services/CraftConditionEvaluator.cs` | Проверка условия по тексту из буфера |
| `Services/ParsedItemCraftEvaluator.cs` | Проверка условия по распарсенному предмету |
| `Services/ItemParser.cs` | Парсинг текста предмета из буфера обмена |
| `Services/AffixLibrary.cs` | Загрузка и поиск по `affix_library.json` |
| `Services/SessionLogger.cs` | Логирование сессии (статический синглтон) |
| `Native/Win32Input.cs` | Низкоуровневые клики и клавиши через WinAPI |
| `affix_library.json` | База данных аффиксов PoE2 — не генерировать, не перезаписывать |
| **TabFlow** | |
| `Services/FragmentStashFillService.cs` | Забирает таблетки из Fragment Stash (Fragment → Tablets → тип → страницы) |
| `Services/GridOccupancyDetector.cs` | Скриншот сетки → яркость ячеек → список занятых (исключает пустые) |
| `Services/TabletInventoryScanService.cs` | Ctrl+C по ячейкам инвентаря → `evaluate_clipboard.py` → цены |
| `Services/TabletListingsIndex.cs` | Управление `listings_index.json` — общее состояние GameHelper и TradeBot |
| `Services/TabletReforgeQueue.cs` | Очередь рефорджа (статический список; **не потокобезопасен**) |
| `Services/TabletReforgeService.cs` | Применяет орб рефорджа к таблетке в инвентаре |
| `Services/TabletListingService.cs` | Выставляет таблетки через торговый UI (Ange) |
| `Services/SoldDetector.cs` | GGG API `sales_history` → помечает проданные записи в listings_index |
| `Services/SmartRepricingService.cs` | Переоценка листингов: флор из `floor_prices.json` → новая цена |
| `scripts/tabflow/evaluate_clipboard.py` | Оценка таблетки (буфер → predict.py → цена div) |
| `scripts/tabflow/floor_fetcher.py` | Получение флор-цен по 7+1 типам таблеток (GGG Trade API) |
| `scripts/tabflow/reprice.py` | Пакетная переоценка листингов (Python) |

---

## Архитектурные решения — почему так, а не иначе

**DI-контейнер (ARCH-2 ✓)** — `Microsoft.Extensions.DependencyInjection`, регистрация в `App.xaml.cs`. Не добавляй новые DI-регистрации без явной задачи.

**`SessionLogger`, `AffixLibrary` — адаптеры добавлены (ARCH-5 ✓)** — существуют `SessionLoggerAdapter`, `AffixLibraryAdapter`. Статические вызовы в legacy-коде сохранены намеренно — не мигрируй попутно.

**`MainWindow.xaml.cs` (~8800 строк)** — MVVM внедрён (ARCH-3 ✓), но code-behind остаётся большим: ~40% — TabFlow (циклы, сервисные вызовы). Не добавляй новую логику в code-behind без крайней необходимости.

**Задержки (`MouseActionDelayMs`, `ClipboardDelayMs`)** — критичны для стабильной работы в игре. Не убирай, не сокращай без тестирования. Значения подобраны под реальные тайминги PoE2.

**`DelayJitterFraction = 0.30`** в сервисах крафта — намеренный джиттер ±30% к задержкам для имитации человеческого поведения. Не убирать.

**`CancellationToken` во всех сервисах** — обязателен. При добавлении нового сервиса крафта передавай и проверяй токен в каждом цикле.

---

## Модель условия остановки крафта

Это центральная концепция — понимай её правильно:

```
CraftConditionPlan
  └─ ExpectedItemClass       (класс предмета — должен совпадать)
  └─ OrAlternatives[]        (варианты — ИЛИ между ними)
       └─ CraftAndGroup
            └─ Clauses[]     (клозы — И между ними)
                 └─ CraftClause (Kind: Single / Sum / Count / WholeModifier)
```

Крафт останавливается, если **хотя бы один** `CraftAndGroup` выполнен полностью (все его `Clauses` = true).

Подробно: [`docs/CRAFT_CONDITION_AFFIX_MATCH_ASCII.txt`](docs/CRAFT_CONDITION_AFFIX_MATCH_ASCII.txt)

---

## Пакетный крафт (BATCH-2) — архитектурное решение

**Модель: барьерная синхронизация по стадиям.** Решение принято 2026-07-09.

Предметы проходят пайплайн группами (stage-by-stage) с барьером на каждой стадии:
- Все активные предметы проходят стадию N прежде чем любой из них переходит к N+1
- Предмет, ушедший назад (OnFailure → StepIndex < N), ждёт на целевой стадии пока остальные активные предметы не придут туда же или не станут Done/Failed
- «Вехи» (Desecrate, смена локации) — жёсткий барьер: все предметы должны быть готовы к этой стадии прежде чем делать переход; цель — одна поездка на весь батч

**Алгоритм `BatchPipelineRunner`:**
```
while есть активные предметы (не Done/Failed):
  for n in 0..N:
    targetItems = items где DetectStep(item) == n
    if empty: continue
    if любой активный предмет на стадии < n: skip  ← барьер
    setupOnce(n)   // открыть вкладку стеша / поехать в локацию
    for item in targetItems:
      result = runStep(item, n)
      updateItemStage(item, result)  // с учётом OnSuccess/OnFailure переходов
```

**Статусы `BatchItem`:** `Pending` | `WaitingAt(stageIndex)` | `Done` | `Failed`

**Реализация:** `Services/BatchPipelineRunner.cs` (BATCH-2b)

---

## TabFlow — торговля таблетками

Подробная документация: [`vault/tabflow/roadmap.md`](vault/tabflow/roadmap.md)

**Четыре workflow** (`RunTabFlowLoopAsync` / `RunTabFlowIterationAsync` в `MainWindow.xaml.cs`):

| # | Workflow | Ключевые сервисы |
|---|---|---|
| 1 | **Buy** (TradeBot) | Отдельный процесс `TradeBot.exe`; читает `listings_index.json` |
| 2 | **Fill→Craft→Scan→List** | `FragmentStashFillService`, `TabletOrbApplicationService`, `TabletInventoryScanService`, `TabletListingService` |
| 3 | **Reprice→ReforgeQueue** | `SmartRepricingService`, `TabletReforgeQueue` |
| 4 | **Reforge→Stash** | `TabletReforgeService`, `AutoReforgeService` |

**Общее состояние между процессами:** `listings_index.json` — `TabletListingsIndex` управляет сериализацией; читается обоими процессами.

**Оценка таблеток:** `TabletInventoryScanService` вызывает `wsl.exe evaluate_clipboard.py` по одному на каждую ячейку (~120 вызовов/итерацию). Батчинг — задача TABFLOW-2.

**Монополия на Win32 Input:** named mutex `PoE2Bot_InputMutex` — **запланировано, не реализовано** (TABFLOW-4). Сейчас процессы не пересекаются по времени вручную. Не реализуй mutex без явной задачи.

**`TabletReforgeQueue` — не потокобезопасен.** Статический список; обращения из UI-потока и фонового Task без lock. Не добавляй новые потоки без lock.

**Python↔C# контракт:** `evaluate_clipboard.py` → stdout → regex `~(\d+\.?\d*)d`. При изменении формата вывода скрипта — обновить regex в `TabletInventoryScanService`.

---

## Git-ветки

**Крупные задачи — в feature-ветке, не в `main`.**

Критерий «крупная задача»: затрагивает несколько файлов и занимает более одной сессии, или относится к именованной задаче из BACKLOG (BATCH-*, CRAFT-*, ARCH-*, DESECRATE-* и т.д.).

Порядок работы:
1. `git checkout -b feat/<task-id>` перед первым коммитом (например `feat/batch-2`)
2. Коммиты в ветку в ходе работы
3. Когда задача завершена: `dotnet test` зелёный → `git push -u origin feat/<task-id>` → PR в `main` (через `gh pr create`)
4. Мелкие правки (одиночный баг, фикс опечатки, правка документации) — можно прямо в `main`

Уточнение: BATCH-1 уже влит в `main` напрямую (до введения правила) — это нормально.

---

## Правила работы с кодом

**Стиль:**
- C# 12, `nullable enable`, `implicit usings` — следуй настройкам в `.csproj`
- Имена: `PascalCase` для типов/свойств, `_camelCase` для приватных полей
- Файл — один класс (или тесно связанные типы, как в `CraftConditionModels.cs`)
- Комментарии только там, где неочевидно — XML-summary для публичных методов сервисов

**После изменения `affix_library.json`:**
Обязательно провести валидацию всех сохранённых рецептов в `Recipes/*.json` и `settings.json`:
1. Найти `"statTemplate"` значения вида `"+ to X"` (без `#`) — это устаревший формат, заменить на `"+# to X"`.
2. Проверить, что каждый шаблон из рецепта находится в библиотеке через `FindStatIndexInEntry` — удалённые записи ломают рецепты.
3. Для замен использовать PowerShell с явным конструированием строки поиска через `[char]0x005C + 'u002B'` (файлы хранят `+` как литерал, не символ `+`).

**Что нельзя:**
- Изменять `affix_library.json` руками — это данные от пользователя
- Добавлять сетевые запросы — приложение полностью локальное
- Трогать `Native/Win32Input.cs` без понимания — ошибка там = зависание ввода в ОС
- Убирать `await Task.Delay(...)` в циклах крафта — это не мёртвый код, а тайминги

**Перед изменением сервиса крафта:**
1. Прочитай соответствующий ASCII-флоу в `docs/` (например, `CHAOS_CRAFT_SERVICE_FLOW_ASCII.txt`)
2. Убедись, что `CancellationToken` проверяется после каждого шага
3. Убедись, что при отмене мышь и клавиши освобождены (нет зависшего Shift)

**Обязательно при создании любого нового сервиса с мышью/клавиатурой (нарушение = неуправляемый ПК):**
1. **ESC-прерывание** — ПЕРВОЕ: глобальный хук ESC → `CancellationTokenSource.Cancel()`. Без него при зависании ОС не поддаётся управлению.
2. **Сворачивание окна** — при старте `MinimizeToTrayOnStart()`, в `finally` → `Dispatcher.Invoke(RestoreFromTray)`. Без этого окно перекрывает игру.

---

## Как добавить новый режим крафта

1. Создать `Services/NewModeCraftService.cs` по образцу `ChaosCraftService.cs`
2. Добавить нужные поля в `AppSettings.cs`
3. Добавить обработчик в `MainWindow.xaml.cs` (кнопки, запуск, остановка)
4. Добавить XAML-контролы в `MainWindow.xaml`
5. Настройки сохранятся автоматически через `SettingsStore`

---

## Тесты

Тесты находятся в `GameHelper.Tests/` — отдельный проект в той же папке.
Запуск: `dotnet test GameHelper.Tests`

Текущее покрытие неполное (см. BACKLOG.md, раздел Тесты). При добавлении новой логики в `CraftConditionEvaluator` или `ItemParser` — добавляй тест. При добавлении чистой логики в TabFlow-сервисах (без Win32) — аналогично.

---

## Документация в `docs/`

| Файл | Когда читать |
|---|---|
| `SRS.md` | Требования к поведению — что должно работать именно так |
| `ROADMAP.md` | Текущий фокус и планы |
| `CODEBASE_GUIDE.md` | Обзор всех файлов |
| `GAME_MECHANICS.md` | Механики PoE2 — если непонятен контекст крафта |
| `ITEM_PARSING.md` | Формат текста предмета из буфера |
| `CRAFTING_STRATEGIES.md` | Зачем нужен каждый режим крафта |
| `*_FLOW_ASCII.txt` | ASCII-диаграммы потоков каждого сервиса |
| `vault/tabflow/roadmap.md` | Архитектура и фазы TabFlow — текущее состояние, целевая функция |
| `vault/tabflow/tablet_types.md` | 7+1 типов таблеток (API-имена, индексы) |
