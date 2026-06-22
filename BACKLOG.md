# Бэклог GameHelper

Список задач по улучшению архитектуры, документации и качества кода.
Статусы: `[ ]` — не начато, `[~]` — в процессе, `[x]` — готово.

---

## Документация

- [x] **DOC-1** Создать `README.md` — описание проекта, требования, установка, запуск, краткое руководство по режимам крафта
- [x] **DOC-2** Создать `CLAUDE.md` — инструкции для AI-ассистента: стиль кода, архитектурные решения, что нельзя менять
- [x] **DOC-3** Создать `CHANGELOG.md` — вести историю изменений по версиям
- [ ] **DOC-4** Создать `docs/architecture.md` — схема компонентов, описание слоёв, диаграмма зависимостей
- [ ] **DOC-5** Добавить XML-комментарии к публичным методам сервисов (`ChaosCraftService`, `AugAnnulCraftService`, `ExaltationCraftServiceFracturedSide`, `SharpenService`)

---

## Архитектура

- [x] **ARCH-1** Ввести интерфейсы для всех сервисов крафта
  - `ICraftService` с методом `Task<CraftResult> RunAsync(CancellationToken ct)`
  - Реализации: `ChaosCraftService`, `AugAnnulCraftService`, `ExaltationCraftServiceFracturedSide`, `SharpenService`
  - Это разблокирует тестирование и подмену реализаций

- [ ] **ARCH-2** Добавить DI-контейнер (`Microsoft.Extensions.DependencyInjection`)
  - Регистрация всех сервисов в `App.xaml.cs`
  - Убрать `new()` из `MainWindow` и других мест
  - Зависит от: ARCH-1

- [ ] **ARCH-3** Рефакторинг `MainWindow.xaml.cs` на MVVM
  - Создать `MainWindowViewModel : INotifyPropertyChanged`
  - Перенести состояние и команды из code-behind в ViewModel
  - Привязка данных через `Binding` вместо прямого обращения к контролам
  - Ожидаемый результат: сократить code-behind с ~2000 до ~200 строк
  - Зависит от: ARCH-1, ARCH-2

- [ ] **ARCH-4** Создать `CraftOrchestrator` — координирующий сервис
  - Инкапсулировать логику выбора режима крафта из `MainWindow`
  - Метод `RunAsync(CraftMode mode, CancellationToken ct)`
  - Зависит от: ARCH-1, ARCH-2

- [ ] **ARCH-5** Заменить статические классы на инъектируемые сервисы
  - `AffixLibrary` (static) → `IAffixLibrary` + DI
  - `SessionLogger` (static) → `ISessionLogger` + DI
  - `ProjectPaths` (static) → `IProjectPaths` + DI
  - Зависит от: ARCH-2

- [x] **ARCH-6** Создать `CraftResult` — единую модель результата крафта
  - Поля: `Success`, `Attempts`, `FinalItem`, `StopReason`
  - Использовать во всех сервисах вместо разрозненных возвращаемых значений

---

## Качество кода

- [x] **QA-1** Аудит и исправление пустых `catch` блоков
  - Найти все `catch` без логирования
  - Добавить логирование через `ISessionLogger`
  - Показывать пользователю критические ошибки через диалог

- [ ] **QA-2** Вынести магические числа и строки в конфигурацию
  - Создать класс `CraftDefaults` или расширить `AppSettings`
  - Примеры: `MouseActionDelayMs`, `ClipboardDelayMs`, дефолтные имена торговцев

- [ ] **QA-3** Единая стратегия отмены операций
  - Проверить корректное использование `CancellationToken` во всех сервисах
  - Убедиться, что отмена не оставляет ресурсы в неконсистентном состоянии

- [ ] **QA-4** Безопасность потоков в UI
  - Аудит прямых обращений к UI-элементам из async методов
  - Заменить хрупкие `Dispatcher.BeginInvoke` на `Dispatcher.InvokeAsync` с явным await

---

## Тесты — чистая логика (высокая польза)

Тестируют код без зависимости от игры или Win32. Фикстуры — реальный текст предметов из буфера обмена.

- [ ] **TEST-1** Unit-тесты для `ItemParser`
  - Покрыть парсинг редких предметов, магических, нормальных
  - Покрыть edge cases: пустой буфер, нераспознанный формат

- [ ] **TEST-4** Unit-тесты для `CraftConditionEvaluator`
  - Расширить существующие тесты в `CraftConditionCountEvaluatorTests.cs`
  - Покрыть все типы условий остановки

---

## Тесты — сервисы крафта (сомнительная польза)

Реальное поведение этих сервисов проверяется только в игре — только пользователь может подтвердить корректность.
Моки Win32 дают ложное чувство покрытия и не ловят реальные баги (тайминги, координаты, поведение игры).

- [ ] **TEST-2** Unit-тесты для `ChaosCraftService`
  - Мокировать `Win32Input` и `ISessionLogger`
  - Проверить: старт/стоп, достижение лимита попыток, успешное условие
  - Зависит от: ARCH-1

- [ ] **TEST-3** Unit-тесты для `AugAnnulCraftService` и `ExaltationCraftServiceFracturedSide`
  - Аналогично TEST-2
  - Зависит от: ARCH-1

- [ ] **TEST-5** Интеграционные тесты
  - Полный цикл крафта с реальным `ItemParser` и `CraftConditionEvaluator`
  - Без обращения к Win32 (мокировать `IWin32Input`)
  - Зависит от: ARCH-1, TEST-1, TEST-2

---

## Инфраструктура

- [ ] **INF-1** Настроить CI/CD (GitHub Actions или аналог)
  - Сборка проекта на push
  - Запуск тестов автоматически
  - Публикация артефактов (exe/zip)

- [ ] **INF-2** Добавить `.editorconfig` — единый стиль кода для всей команды

- [ ] **INF-3** Добавить анализатор кода (Roslyn Analyzers или SonarAnalyzer)
  - Подключить как NuGet-пакет
  - Настроить правила в `.editorconfig`

---

## Крафт и статистика аффиксов

- [ ] **CRAFT-1** Добавить выбор подтипа предмета в UI крафта
  - После выбора класса (Body Armours, Gloves, Helmet, Boots) показывать dropdown с подтипом базы
  - Подтипы: Armour (Str), Evasion (Dex), Energy Shield (Int), Armour/Evasion, Armour/Energy Shield, Evasion/Energy Shield
  - Подтип влияет на пул аффиксов библиотеки — исключает аффиксы чужих подтипов
  - Нужно для корректного расчёта вероятности и чистоты статистики весов
  - Связано с: `AffixSubClass`, `AffixStatsData`, `feat/ilvl-orb-tier-weights`

- [x] **CRAFT-2** Добавить `ItemSubType` в сбор статистики крафта
  - Вычислять подтип из `ParsedItem.Requirements` (Str/Dex/Int → Armour/Evasion/Energy Shield)
  - Изменить ключ `AffixStatsData.PerClass` на `"ItemClass|SubType"` (версия 4)
  - Без разбивки по подтипу статистика Body Armours смешивает несовместимые пулы аффиксов

---

## Очерёдность реализации (рекомендуемый порядок)

```
1. DOC-1 → README.md           (быстро, высокая польза)
2. DOC-2 → CLAUDE.md           (быстро, нужен для AI-работы)
3. ARCH-1 → интерфейсы         (низкая сложность, разблокирует всё остальное)
4. ARCH-6 → CraftResult        (низкая сложность)
5. QA-1  → fix catch блоки     (низкая сложность, повышает стабильность)
6. ARCH-2 → DI-контейнер       (средняя сложность)
7. ARCH-5 → убрать static      (средняя, зависит от ARCH-2)
8. TEST-1/4 → тесты парсера и условия (можно параллельно с ARCH-*)
9. ARCH-4 → Orchestrator       (средняя, зависит от ARCH-1/2)
10. ARCH-3 → MVVM              (высокая сложность, последний крупный шаг)
```
