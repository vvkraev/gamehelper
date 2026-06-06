# CLAUDE.md — GameHelper

Инструкции для AI-ассистента. Читай этот файл перед любой работой с проектом.

---

## Что это за проект

WPF-приложение (.NET 10, Windows) для автоматизации крафта предметов в **Path of Exile 2**.
Работает через Win32 API (эмуляция мыши/клавиатуры) и буфер обмена — никаких читов, только клики.

Подробно: [`docs/SRS.md`](docs/SRS.md) | Планы: [`docs/ROADMAP.md`](docs/ROADMAP.md) | Бэклог: [`BACKLOG.md`](BACKLOG.md)

---

## Ключевые файлы

| Файл / папка | Роль |
|---|---|
| `MainWindow.xaml.cs` | Точка входа UI — координирует все сервисы (~2000 строк, рефакторинг запланирован в ARCH-3) |
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

---

## Архитектурные решения — почему так, а не иначе

**Нет DI-контейнера (пока)** — сервисы создаются через `new()` прямо в `MainWindow`. Это известная проблема, запланирован рефакторинг (ARCH-2). Не добавляй DI без явной задачи на это.

**`SessionLogger` и `AffixLibrary` — статические классы** — намеренное решение для упрощения. Запланирована замена на инъектируемые сервисы (ARCH-5). Не рефакторь попутно.

**`MainWindow.xaml.cs` — монолит** — вся логика UI собрана в одном файле. Запланирован переход на MVVM (ARCH-3). Не добавляй новую логику в code-behind без крайней необходимости.

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

## Правила работы с кодом

**Стиль:**
- C# 12, `nullable enable`, `implicit usings` — следуй настройкам в `.csproj`
- Имена: `PascalCase` для типов/свойств, `_camelCase` для приватных полей
- Файл — один класс (или тесно связанные типы, как в `CraftConditionModels.cs`)
- Комментарии только там, где неочевидно — XML-summary для публичных методов сервисов

**Что нельзя:**
- Изменять `affix_library.json` руками — это данные от пользователя
- Добавлять сетевые запросы — приложение полностью локальное
- Трогать `Native/Win32Input.cs` без понимания — ошибка там = зависание ввода в ОС
- Убирать `await Task.Delay(...)` в циклах крафта — это не мёртвый код, а тайминги

**Перед изменением сервиса крафта:**
1. Прочитай соответствующий ASCII-флоу в `docs/` (например, `CHAOS_CRAFT_SERVICE_FLOW_ASCII.txt`)
2. Убедись, что `CancellationToken` проверяется после каждого шага
3. Убедись, что при отмене мышь и клавиши освобождены (нет зависшего Shift)

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

Текущее покрытие неполное (см. BACKLOG.md, раздел Тесты). При добавлении новой логики в `CraftConditionEvaluator` или `ItemParser` — добавляй тест.

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
