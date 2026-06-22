# Справочник кода — GameHelper

Актуальный обзор всех файлов проекта. Обновляется при добавлении новых сервисов.

---

## Точки входа и главное окно

| Файл | Роль |
|---|---|
| `App.xaml / App.xaml.cs` | Точка входа WPF — создаёт `MainWindow` |
| `MainWindow.xaml` | XAML-разметка всего UI (~все вкладки и панели) |
| `MainWindow.xaml.cs` | Координирует сервисы, обрабатывает кнопки, запускает/останавливает крафт (~2000 строк, рефакторинг запланирован в ARCH-3) |

---

## Настройки и хранилище

| Файл | Роль |
|---|---|
| `AppSettings.cs` | Все настройки приложения: области экрана, задержки, условие крафта, конфиги вкладок |
| `SettingsStore.cs` | Сериализация `AppSettings` ↔ `settings.json` |
| `ProjectPaths.cs` | Пути к корню проекта, папке логов, файлам данных |
| `ScreenRect.cs` | Базовый тип: прямоугольник экрана (X, Y, Width, Height) |

---

## Диалоговые окна

| Файл | Роль |
|---|---|
| `CraftConditionWindow.xaml / .cs` | Редактор условия остановки крафта (OR/AND/Clauses) |
| `CraftLogWindow.xaml / .cs` | Просмотр лога текущего крафта (автообновление) |
| `RegionPickerWindow.xaml / .cs` | Выделение области экрана мышью |
| `ItemParsingWindow.xaml / .cs` | Просмотр распарсенного предмета из буфера |
| `RecipeNameDialog.xaml / .cs` | Диалог ввода имени рецепта |
| `RepricingTabSettingsWindow.cs` | Настройка вкладок и шагов переоценки |

---

## Сервисы крафта

| Файл | Роль |
|---|---|
| `Services/ChaosCraftService.cs` | Цикл Chaos Orb крафта: орб → предмет → проверка условия |
| `Services/AugAnnulCraftService.cs` | Цикл Aug+Annul: аугментация, аннулирование, каскад |
| `Services/ExaltationCraftServiceFracturedSide.cs` | Exalt-крафт с fractured предметами + управление оменами |
| `Services/OmenActivationService.cs` | Рефил стаков оменов (Refresh + ПКМ, Greater refresh) |
| `Services/SharpenService.cs` | Заточка предметов по сетке инвентаря |
| `Services/ReforgeService.cs` | Перековка катализаторов: сканирование → пакет → станок → чтение результата |
| `Services/AutoReforgeService.cs` | Авто-вариант перековки с полным auto-scan |
| `Services/ChancingService.cs` | Шансинг таблетов: Single/Grid режимы + статистика ROI |

---

## Сервисы торговли и рынка

| Файл | Роль |
|---|---|
| `Services/RepricingService.cs` | Переоценка выставленных лотов: читает цену, снижает по ступеням, вводит новую |
| `Services/MarketRatioExaltedDivineAutomation.cs` | Авто-обмен по курсу Exalted/Divine через рыночный интерфейс |
| `Services/MarketRatioOrderBookDepthCapture.cs` | Снимок стакана заявок (до 5 строк Buy + 5 Sell) |
| `Services/MarketRatioPickerClickHelper.cs` | Клики по панели Market Ratio |
| `Services/MarketRatioReadoutParser.cs` | Разбор текста курса из OCR |
| `Services/OrderBookOcrParser.cs` | Основной OCR-парсер стакана (цена + количество) |
| `Services/OrderBookGridOcrRecognizer.cs` | Распознавание сетки ячеек стакана |
| `Services/OrderBookManualCellsRecognizer.cs` | Ручная разметка ячеек стакана |
| `Services/OrderBookOcrLogFormatter.cs` | Форматирование лога OCR-результата |
| `Services/OrderBookSnapshotCsvLog.cs` | Сохранение снимков стакана в CSV |
| `Services/ExchangeRateCsvLog.cs` | Лог курсов обмена в CSV |
| `Services/ExchangeRateInfoCollectionScan.cs` | Сессия сбора данных о курсе |
| `Services/GoldFeeLibraryScanRunner.cs` | Сканирование комиссий Gold за крафт |
| `Services/GoldFeeLibraryStore.cs` | Хранилище данных о комиссиях |
| `Services/CurrencyIWantGoldScanList.cs` | Список валют для обмена на Gold |
| `Services/CurrencyPairArbitrageCalculator.cs` | Расчёт арбитража между парами валют |
| `Services/TraderNpcNameOpenTradeAction.cs` | Открытие трейда у NPC по имени |

---

## Networth и цены

| Файл | Роль |
|---|---|
| `Services/NetworthService.cs` | Сканирует вкладки стэша, считает стоимость через poe.ninja |
| `Services/NetworthSnapshotStore.cs` | Сохраняет снэпшот Networth в JSON, загружает при старте |
| `Services/PoeNinjaPriceService.cs` | Загрузка и поиск цен из `poe_ninja_prices.json` |
| `poe_ninja_prices.json` | Кэш цен с poe.ninja (обновляется скриптом) |

---

## Условие остановки крафта

| Файл | Роль |
|---|---|
| `Services/CraftConditionModels.cs` | Модели: `CraftConditionPlan`, `CraftAndGroup`, `CraftClause` |
| `Services/CraftConditionEvaluator.cs` | Проверка условия по raw-тексту из буфера |
| `Services/ParsedItemCraftEvaluator.cs` | Проверка условия по `ParsedItem` |
| `Services/CraftConditionPlanNormalizer.cs` | Нормализация плана перед сохранением/сравнением |
| `Services/CraftConditionMigration.cs` | Миграция старого формата условия |
| `Services/AffixCraftPatternBuilder.cs` | Построение паттернов для поиска аффиксов |
| `Services/CraftAffixCascadeHelper.cs` | Каскадный подбор аффиксов при Aug+Annul |

---

## Парсинг предметов и библиотека аффиксов

| Файл | Роль |
|---|---|
| `Services/ItemParser.cs` | Парсинг текста предмета из буфера → `ParsedItem` |
| `Services/AffixLibrary.cs` | Загрузка `affix_library.json`, поиск по шаблону |
| `Services/AffixLibraryEntry.cs` | Модель одной записи библиотеки аффиксов |
| `affix_library.json` | База аффиксов PoE2 — не редактировать вручную |

---

## Статистика

| Файл | Роль |
|---|---|
| `Services/AffixStatsScanner.cs` | Читает лог-файлы крафта, строит статистику аффиксов |
| `Services/AffixStatsData.cs` | Модели данных статистики (частоты, накопление) |
| `Services/ReferenceStatsService.cs` | Загрузка/сохранение статистики из `docs/stats/` |
| `Services/CatalystReforgeStatsScanner.cs` | Статистика выходов перековки катализаторов |
| `affix_stats.json` | Накопленная статистика аффиксов |
| `catalyst_reforge_stats.json` | Накопленная статистика катализаторов |
| `docs/stats/` | Статистика шансинга и других сессий |

---

## OCR и захват экрана

| Файл | Роль |
|---|---|
| `Services/WindowsOcrTextLocator.cs` | Поиск текста на экране через Windows OCR API |
| `Services/ScreenCaptureHelper.cs` | Захват области экрана в Bitmap |

---

## Реестр стакуемых предметов

| Файл | Роль |
|---|---|
| `Services/StackableItemRegistry.cs` | Реестр всех стакуемых предметов (орбы, рунные камни, катализаторы, Delirium) |
| `stackable_item_types.json` | Источник данных реестра |

---

## Логирование

| Файл | Роль |
|---|---|
| `Services/SessionLogger.cs` | Статический синглтон — лог текущей сессии в файл |
| `Services/CraftRunFileLog.cs` | Лог одного крафт-запуска (`.tmp` → `.txt`) |

---

## Сервисы интерфейса

| Файл | Роль |
|---|---|
| `Services/WindowGeometryStore.cs` | Сохранение/восстановление позиций окон |
| `Services/CraftServiceInterfaces.cs` | Общие интерфейсы для крафт-сервисов |
| `Services/CraftResult.cs` | `record CraftResult` — единый результат всех крафтов |
| `Services/GameInputLock.cs` | Блокировка пользовательского ввода во время крафта |
| `ReforgeState.cs` | Состояние перековки между сессиями |

---

## Native (Win32)

| Файл | Роль |
|---|---|
| `Native/Win32Input.cs` | Клики, нажатия клавиш, Ctrl+Alt+C, ShiftDown/Up через WinAPI |
| `Native/GlobalHotkey.cs` | Регистрация глобальных горячих клавиш |
| `Native/ProcessForeground.cs` | Перевод PoE2 на передний план |

---

## Документация (`docs/`)

| Файл | Когда читать |
|---|---|
| `SRS.md` | Требования — что должно работать именно так |
| `ROADMAP.md` | Текущий фокус и планы |
| `GAME_MECHANICS.md` | Механики PoE2 (аффиксы, валюты, крафт) |
| `ITEM_PARSING.md` | Формат текста предмета из буфера обмена |
| `CRAFTING_STRATEGIES.md` | Зачем нужен каждый режим крафта |
| `MARKET_RATIO_ORDER_BOOK.md` | Биржевой стакан и авто-обмен |
| `CRAFT_GOLD_COST_SCREEN_OCR.md` | OCR стоимости крафта в Gold |
| `CRAFT_CONDITION_AFFIX_MATCH_ASCII.txt` | Логика условия остановки, сопоставление аффиксов |
| `CHAOS_CRAFT_SERVICE_FLOW_ASCII.txt` | Поток Chaos Orb крафта |
| `AUG_ANNUL_DECISION_FLOW_ASCII.txt` | Поток Aug+Annul крафта |
| `EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt` | Поток Exalt (fractured side) |
| `EXALTATION_CRAFT_SERVICE_NONFRACTURED_SIDE_FLOW_ASCII.txt` | Поток Exalt (обычная сторона) |
| `REFORGE_SERVICE_FLOW_ASCII.txt` | Поток перековки катализаторов |
| `CHANCING_SERVICE_FLOW_ASCII.txt` | Поток шансинга таблетов |
| `REPRICING_SERVICE_FLOW_ASCII.txt` | Поток переоценки лотов |
| `NETWORTH_SERVICE_FLOW_ASCII.txt` | Поток сканирования Networth |
