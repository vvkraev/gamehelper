# Глубокий анализ системы TabFlow

**Дата анализа:** 2026-07-17  
**Версия:** Post-SmartRepricingService  
**Статус:** Функциональная, готовая к производству с рекомендациями

---

## 1. Полный цикл TabFlow

TabFlow реализует замкнутый цикл монетизации планшеток (Tablets) в Path of Exile 2:

```
┌─────────────────────────────────────────────────────────────────────┐
│ ФАЗА 1: СБОР И ОБУЧЕНИЕ (Шаги 1–2)                                  │
├─────────────────────────────────────────────────────────────────────┤
│ Сканирование трейда           → trade_data/*.json (снапшоты)       │
│   (вкладка Наблюдение,           - базовые типы (base_type)        │
│    cross-section стратегия)      - мод-хэши (explicit.stat_*)      │
│                                  - price_divine + currency         │
│                                                                     │
│ Обучение LightGBM модели      → scripts/tabflow/models/{type}/    │
│   (train_evaluator.py)           - model.txt (LightGBM native)     │
│                                  - vocab.json (hash → feature_idx) │
│                                  - metadata.json (метрики, MAE)    │
└─────────────────────────────────────────────────────────────────────┘
            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ ФАЗА 2: ОЦЕНКА И ЛИСТИНГ (Шаги 3–4)                                 │
├─────────────────────────────────────────────────────────────────────┤
│ Оценка планшетки из буфера   ← evaluate_clipboard.py (Python)      │
│   (Ctrl+Alt+C → Python)         или GameHelper WPF (C#)             │
│   ↓                                                                 │
│ Модель предсказывает цену (divine, дробное число)                  │
│   ↓                                                                 │
│ Ceiling: 1.7d → 2d, затем выставление в магазин                   │
│   (TabletListingService в GameHelper)                               │
│   ↓                                                                 │
│ Запись в listings_index.json + listings.jsonl                      │
│   - Timestamp, Col, Row, мод-шаблоны, цена листинга                │
└─────────────────────────────────────────────────────────────────────┘
            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ ФАЗА 3: МОНИТОРИНГ И ДООБУЧЕНИЕ (Шаги 5–6)                          │
├─────────────────────────────────────────────────────────────────────┤
│ match_sales.py (Python):                                            │
│   - Ищет продажи в sales_history.json                              │
│   - Сопоставляет по base_type + нормализованные моды              │
│   - Вычисляет minutesToSale                                         │
│                                                                     │
│ ПРОМАЗ (miss):                                                      │
│   Продажа < 10 минут + нет переоценок →                            │
│   floor_price = 2× цена продажи                                    │
│   Запись в miss_overrides.json (status: pending)                   │
│   + action_items.md (задача дообучения)                            │
│                                                                     │
│ evaluate_clipboard.py --check-misses:                               │
│   Пересчитывает оценку всех miss-записей                           │
│   Если модель ≥ floor → можно snять override                       │
│   (status: resolved)                                                │
│                                                                     │
│ train_evaluator.py --types {type}:                                 │
│   Добавляет новые снапшоты с неправильными комбинациями            │
│   Дообучает модель, сравнивает MAE                                 │
└─────────────────────────────────────────────────────────────────────┘
            ↓
┌─────────────────────────────────────────────────────────────────────┐
│ ФАЗА 4: АДАПТИВНАЯ ПЕРЕОЦЕНКА (Шаг 4 расширенный)                   │
├─────────────────────────────────────────────────────────────────────┤
│ reprice.py (Python):                                                │
│   Анализирует velocity (minutesToSale по комбо модов за 14 дней)    │
│   Решает: ждать 24ч (если быстрые продажи) или 12ч (нет данных)   │
│   Вычисляет новую цену:                                             │
│     - divine > 5d → −1d                                            │
│     - divine ≤ 5d → конвертирует в chaos, −3c                     │
│     - уже chaos → −3c (мин 3c)                                    │
│   Выпускает reprice_plan.json                                       │
│                                                                     │
│ SmartRepricingService (C#):                                         │
│   Выполняет план из reprice_plan.json                               │
│   - Режим SamePrice: ПКМ → Ctrl+A → цена → Enter                  │
│   - Режим CurrencySwitch: Ctrl+ЛКМ → полный диалог → chaos        │
│   Логирует переоценки в listings_index.json                         │
│                                                                     │
│ RepricingService (C#, legacy):                                      │
│   Асинхронная переоценка через per-tab конфиги                      │
│   Используется как fallback/альтернатива                            │
└─────────────────────────────────────────────────────────────────────┘
            ↓
        [PROFIT] ✓
        Проданы после нескольких переоценок — цена найдена оптимальная
```

**Ключевые участники по фазам:**

| Фаза | Компонент | Язык | Основная задача |
|------|-----------|------|-----------------|
| 1 | GameHelper Observation Tab | C# WPF | Сбор снапшотов с трейда |
| 1 | train_evaluator.py | Python | Обучение LightGBM моделей |
| 2 | evaluate_clipboard.py | Python | Оценка предмета из буфера |
| 2 | TabletListingService | C# | Автоматический листинг в магазин |
| 2 | TabletListingsIndex | C# | Персистентный индекс листингов |
| 3 | match_sales.py | Python | Сопоставление продаж, детекция промазов |
| 4 | reprice.py | Python | Генерация плана переоценки |
| 4 | SmartRepricingService | C# | Выполнение плана переоценки |

---

## 2. Целостность идеи

### 2.1 Сквозная логика

Система **хорошо связана**: каждый этап выходит данные для следующего.

**Сильные стороны:**

- **Замкнутый цикл обратной связи**: Продажи → miss_overrides → дообучение → лучшие предсказания
- **Персистентность**: Все листинги и переоценки логируются в JSON (listings_index.json, reprice_plan.json)
- **Разделение ответственности**: Python — ML/аналитика, C# — UI/клики мыши
- **Стандартизация форматов**: Нормализованные моды, явные шаблоны, JSON везде

### 2.2 Разрывы и неясности

#### **Разрыв 1: Связь между predict.py и evaluate_clipboard.py**

**Проблема:** Есть два независимых скрипта для оценки:
- `predict.py` — берёт снапшоты из trade_data/, выводит таблицу по листингам
- `evaluate_clipboard.py` — оценивает предмет из буфера, вызывается из C#

**Следствие:** Логика предсказания продублирована, но не идентична:
- `predict.py` использует `extract_features(rich_mods, vocab)` прямо из снапшота
- `evaluate_clipboard.py` парсит текст предмета в буфере, тоже строит вектор
- Оба используют одну модель (model.txt), но код разный

**Риск:** Если модель предсказывает для снапшота 2.5d, а для буфера 2.3d → путаница в UI.

**Рекомендация:** Факторизовать `extract_features()` в общий модуль, использовать оба.

---

#### **Разрыв 2: Гэп между miss_overrides и переоценкой**

**Проблема:** 
- `match_sales.py` пишет miss_overrides.json с floor_price
- `evaluate_clipboard.py` читает эти оверрайды и поднимает цену (max(price, floor))
- **НО:** reprice.py не знает о miss_overrides, вычисляет переоценку только по velocity

**Следствие:** Если листинг был замарширован (missed), а потом переоценивается:
- `evaluate_clipboard.py` скажет floor 4d
- `reprice.py` вычислит −1d от текущей цены, может спустить ниже floor

**Сценарий:** Листинг 2d (промаз) → floor 4d → но `reprice.py` спустит до 1d, что противоречит override.

**Рекомендация:** `reprice.py` должен учитывать `miss_overrides.json` и не опускаться ниже floor.

---

#### **Разрыв 3: Нормализация модов везде разная**

**Проблема:** Модули нормализации модов разбросаны:
- `train_evaluator.py`: `parse_mods_from_text()` синтезирует rich-моды из текста
- `match_sales.py`: `mod_template()` убирает диапазоны и числа
- `predict.py`: `extract_features()` нормализует roll по min/max
- `reprice.py`: свой `mod_template()`

**Следствие:** Если где-то ошибка в регексе, может быть несоответствие между обучением и inference.

**Пример:** `(30-20)` — диапазон в обратном порядке (минимум > максимум). При нормализации:
```python
_RANGE = re.compile(r'\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)')
_RANGE.sub('', "(30-20)")  # → пусто
```
Это работает, но семантически странно. Может быть баг в trade-данных.

**Рекомендация:** Создать общий модуль `tabflow/mod_utils.py` с единой функцией нормализации.

---

#### **Разрыв 4: Отсутствие интеграции C# ↔ Python для переоценки**

**Проблема:**
- `SmartRepricingService` и `RepricingService` (C#) — это **закрытые циклы** переоценки
- Они **не используют** reprice.py и не логируют результаты обратно
- reprice.py генерирует план, но SmartRepricingService должен его выполнить — но это требует ручного запуска

**Следствие:** Цепочка неясна:
  1. reprice.py генерирует plan
  2. SmartRepricingService должен загрузить и выполнить
  3. Но нет явного глюэ-кода, который вызывает SmartRepricingService после reprice.py

**Рекомендация:** Добавить `Repricer` UI-команду в GameHelper, которая:
1. Запустит `reprice.py --dry-run`
2. Покажет план
3. По подтверждению пользователя запустит `SmartRepricingService.ExecutePlanAsync()`

---

### 2.3 Архитектурная целостность: оценка

| Аспект | Статус | Оценка |
|--------|--------|--------|
| Циклическая обратная связь | ✓ Реализована | 4/5 |
| Единство данных (JSON формат) | ✓ Хорошо | 5/5 |
| Единство алгоритмов | ⚠ Разбросано | 2/5 |
| Связанность фаз | ⚠ Ручная | 2/5 |
| Обработка ошибок | ✗ Минимальна | 1/5 |

**Общая оценка целостности: 3/5** — логика работает, но архитектура требует компактизации.

---

## 3. Анализ принятых решений

### 3.1 Хорошие решения

#### ✓ **Двухуровневая валидация моделей** (`train_evaluator.py`)
```python
X_train, X_val, y_train, y_val = train_test_split(X, y, test_size=0.2, random_state=42)
```
- Train/val split 80/20 стандартный, воспроизводимый (random_state=42)
- Вычисляется MAE на обоих подмножествах для контроля переобучения
- Выявляется переобучение по `overfit_ratio = mae_val / mae_train`

**Почему хорошо:** Защита от overfitting, которая критична для ML, работающей на реальных торговых данных.

---

#### ✓ **LightGBM + логарифмическое масштабирование**
```python
y = np.log1p(np.array([it["price_d"] for it in items], dtype=np.float32))
# ...
mae_val = float(np.mean(np.abs(np.expm1(y_pred_val) - np.expm1(y_val))))
```
- log1p(price) — логнормальное распределение цен (1 divine и 100 divine имеют разный "вес")
- LightGBM хорошо работает с такими данными
- RMSE на логшкале + MAE на обычной шкале — двойной контроль

**Почему хорошо:** Экономически верно — разница между 1d и 2d важнее, чем между 100d и 101d.

---

#### ✓ **Нормализация роллов по min/max**
```python
if rmax > rmin:
    vec[idx] = (value - rmin) / (rmax - rmin)  # 0..1 диапазон
```
- Мод "50% increased" с диапазоном 30-70 → нормированный ролл 0.5 (средний)
- Мод "30% increased" с диапазоном 20-40 → нормированный ролл 0.5 (тоже средний!)
- Это позволяет моделировать "относительную силу" мода, не абсолютное значение

**Почему хорошо:** ML получает семантически правильные фичи, а не сырые проценты.

---

#### ✓ **Стратегия cross-section для сбора данных** (README, шаг 1)
```
Запрос 1: Prefix1 + Prefix2 + SuffixN + SuffixN-1
Запрос 2: Prefix2 + Prefix3 + SuffixN-1 + SuffixN-2
...
Запрос +1: БЕЗ фильтра по модам (флорные предметы, base только)
```
- Не перебираются все комбинации (экспоненциально)
- Вместо этого — диагональные срезы пространства модов
- Последний запрос без фильтра критичен для модели (моды с нулевой ценой)

**Почему хорошо:** Эффективно собирает данные, избегает спама трейда. Последний запрос даёт baseline.

---

#### ✓ **Мониторинг промазов через minutesToSale**
```python
"miss": minutes < MISS_THRESHOLD_MIN and len(entry.get("repricings")) == 0
```
- Продажа < 10 минут БЕЗ переоценок = недооценка
- Запускает автоматическое дообучение
- `evaluate_clipboard.py --check-misses` позволяет пересчитать

**Почему хорошо:** Система учится на своих ошибках. Feedback loop работает.

---

### 3.2 Сомнительные решения

#### ⚠ **Потолок в 30 минут на обучение — слишком низко**
```python
callbacks = [lgb.early_stopping(30, verbose=False), ...]
```
- `early_stopping(30)` остановит обучение если валидация не улучшилась 30 итераций
- Для меньшей выборки (30-50 примеров) это может быть слишком агрессивно
- Модель не сходится полностью

**Риск:** На малых данных (Abyss Tablet: 15 примеров) модель недообучена.

**Рекомендация:** Сделать `early_stopping` адаптивным: `min(30, max(5, n_samples // 10))`.

---

#### ⚠ **Ceiling для листинга может быть слишком высоким**
```python
int listingPrice = Math.Max(MinListingPrice, (int)Math.Ceiling(estimatedPrice));
```
- Модель предсказала 1.3d → листинг 2d
- Потребитель видит цену 2d, а не 1.3d (markdown)
- Если есть конкуренция, товар может залежаться

**Альтернатива:** `(int)Math.Round(estimatedPrice)` или `(int)Math.Floor(estimatedPrice)` для более агрессивной продажи.

**Рекомендация:** Сделать configurable, попробовать оба режима A/B.

---

#### ⚠ **Валюта Chaos используется только при ≤ 5 divine**
```python
if current_price > CHAOS_THRESHOLD_D:
    return current_price - DIVINE_STEP, "divine", ...
else:
    in_chaos = round(current_price * cpd)  # cpd ≈ 8
    new_chaos = max(CHAOS_MIN, in_chaos - CHAOS_STEP)
```
- Логика жёсткая: 5.01d → −1d (остаётся в divine), 4.99d → convert → chaos
- Нет градиента, скачок в цене

**Риск:** Можно потерять продажу на границе из-за скачка формата.

**Рекомендация:** Плавный переход или конфигурация порога.

---

#### ⚠ **match_sales.py может не найти продажу если моды немного отличаются**
```python
if mod_set(sale_mods(s)) != target_mods:
    continue
```
- Точное совпадение нормализованных модов
- Если листинг: "5(5-7)% increased pack", продажа: "5% increased pack" → не совпадёт
- Зависит от того, как PoE2 API возвращает моды

**Риск:** Ложные промахи, завышены miss_price.

**Рекомендация:** Нечёткое сравнение модов с Levenshtein расстоянием или покрытием (>90%).

---

### 3.3 Архитектурные решения, требующие обсуждения

#### ⚠ **Отдельные Python-скрипты vs интегрированный Python в GameHelper**

**Текущий подход:**
- Python-скрипты запускаются из WSL вручную
- Результаты (.json файлы) читаются GameHelper
- Нет изолированного окружения

**Альтернатива:**
- Встроить Python в GameHelper как subprocess
- Кнопка "Обучить модель" в UI запускает train_evaluator.py автоматически
- Кнопка "Репрайс" запускает reprice.py, потом SmartRepricingService

**Текущая оценка:** 3/5 (функционирует, но не удобно для пользователя)

---

#### ⚠ **Кэширование курсов валют в poe_ninja_prices.json**

- Обновляется вручную (`poe_ninja_prices.json`)
- Если курс divine/chaos изменился, нужно пересчитать историю
- `train_evaluator.py` и `predict.py` читают последний курс, но обучение на старых курсах

**Риск:** Инфляция валют создаст рассинхронизацию.

**Рекомендация:** API-интеграция с poe.ninja или периодический fetch.

---

## 4. Узкие места и риски

### 4.1 Хрупкость при изменении trade API

**Проблема:** Система зависит от формата trade_data снапшотов.
- Если PoE2 изменит структуру API → все снапшоты "сломаются"
- `mods_explicit_rich` может стать `mods_explicit_enhanced` и т.д.

**Текущая защита:** Fallback в `parse_mods_from_text()`, парсит `mods_explicit` (строки).

**Риск:** Уровень 8/10 — PoE2 — активно развивающаяся игра, API нестабилен.

**Рекомендация:** 
- Версионирование snapshots по API version
- Unit-тесты на парсинг с мок-данными
- Миграции (скрипты обновления структуры) когда API меняется

---

### 4.2 Холодный старт модели

**Проблема:** `--min-samples 30` требуется 30 предметов для обучения.
- Новый тип планшетки = нужно снять 12-15 снапшотов × 100 листингов = ~1500 предметов
- Это ~1-2 часа ручной работы на трейде

**Риск:** 4/10 (одноразовая проблема, потом система работает)

**Рекомендация:**
- Начать с `--min-samples 15` для быстрого старта
- Дообучение по мере накопления данных
- Документация: скрипт для параллельной загрузки снапшотов (если Tampermonkey может)

---

### 4.3 Отсутствие обработки краевых случаев

| Случай | Текущее поведение | Риск |
|--------|-------------------|------|
| Листинг не продан > 14 дней | Остаётся в индексе навсегда | 5/10 (выбросы в data) |
| Две идентичные комбинации модов → разные цены | Модель берёт среднее | 3/10 (потенциал опт) |
| OCR не найдёт "Divine Orb" в дропдауне | Кидает InvalidOperationException | 7/10 (критичная ошибка) |
| sales_history.json невалидный JSON | Скрипт кидает исключение | 6/10 (точка отказа) |

**Рекомендация:** Try-catch везде, graceful degradation.

---

### 4.4 Нет версионирования моделей

**Проблема:**
- Обучение перезаписывает `model.txt`, `vocab.json`
- Если новая модель хуже (MAE выше) → нет способа откатиться

**Риск:** 5/10 (лечится ручным откатом, но неприятно)

**Рекомендация:**
```
models/ritual_tablet/
  v1_2026-07-16_165_items_MAE0.31/
    model.txt
    vocab.json
    metadata.json
  v2_2026-07-17_185_items_MAE0.29/  ← текущая
    model.txt
    vocab.json
    metadata.json
```

---

### 4.5 Взрывной рост индекса

**Проблема:**
- `listings_index.json` содержит полный текст каждого листинга (`itemText`)
- Если листить 1000 предметов в день, файл вырастет на ~50 MB в день
- На месячной работе > 1 GB

**Риск:** 6/10 (I/O становится медленной, трудно работать вручную)

**Рекомендация:**
- Архивирование: перемещать старые (проданные) записи в `listings_index.archive.json`
- Или расщепить на `listings_index_active.json` (только sold=false) и `listings_history.json`

---

### 4.6 Отсутствие timeout на Win32 операциях

**Проблема:** В `SmartRepricingService` и `TabletListingService`:
```csharp
await Task.Delay(WithJitter(DialogSettleMs), ct).ConfigureAwait(false);
Win32Input.ClickLeft();
```
- Если игра зависла или упала → операция зависнет
- Нет глобального timeout на выполнение плана

**Риск:** 4/10 (приложение WPF может зависнуть)

**Рекомендация:**
```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromMinutes(30));  // max 30 минут на план
```

---

## 5. Конкретные предложения по улучшению

### ПРИОРИТЕТ 1: Критичные (HIGH)

#### **P1.1 — Убрать дублирование логики предсказания**

**Статус:** Высокий приоритет (слияние predict.py и evaluate_clipboard.py)

**Почему:** Две реализации = две потенциальные ошибки, два источника истины.

**Файлы:** 
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/predict.py` (150+ строк)
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/evaluate_clipboard.py` (420+ строк)

**План действий:**

1. Создать `scripts/tabflow/predictor.py` с классом `TabletPredictor`:
```python
class TabletPredictor:
    def __init__(self, models_dir: Path):
        self.models_cache = {}  # slug → (model, vocab, meta)
    
    def predict_from_rich_mods(self, base_type: str, rich_mods: list[dict]) -> PredictionResult:
        """Для trade_data снапшотов"""
        ...
    
    def predict_from_item_text(self, item_text: str) -> PredictionResult:
        """Для буфера обмена"""
        base_type, mods = parse_item(item_text, self.models_dir)
        ...
```

2. Оба скрипта импортируют `TabletPredictor` и используют единый path.

3. Добавить unit-тесты на предсказания с мок-данными.

**Ожидаемый результат:** −200 строк кода, одна источник истины, легче мейнтейнить.

---

#### **P1.2 — Интегрировать reprice.py в SmartRepricingService**

**Статус:** Высокий приоритет (связь Python ↔ C#)

**Почему:** Сейчас план генерируется отдельно, выполняется отдельно. Нет гарантии что они синхронизированы.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/reprice.py`
- `/mnt/c/Users/VVK/GameHelper/Services/SmartRepricingService.cs`

**План действий:**

1. Добавить в `SmartRepricingService` статический метод:
```csharp
public static async Task<(bool success, List<RepricePlanItem> plan)> 
    GeneratePlanAsync(string pythonExe, string repriceScript, string indexPath)
{
    // Запустить subprocess: python reprice.py
    // Прочитать reprice_plan.json
    // Вернуть распарсенный план
}
```

2. В GameHelper UI добавить кнопку "Умная переоценка":
```csharp
private async void OnSmartRepriceClick(object sender, RoutedEventArgs e)
{
    var (ok, plan) = await SmartRepricingService.GeneratePlanAsync(...);
    if (ok && plan.Count > 0)
    {
        MessageBox.Show($"Найдено {plan.Count} позиций для переоценки. Выполнить?");
        // ExecutePlanAsync(plan, ...)
    }
}
```

3. Логирование результата обратно (сколько успешно переоценено).

**Ожидаемый результат:** Полный цикл в UI, без ручного запуска скриптов.

---

#### **P1.3 — Учитывать miss_overrides в reprice.py**

**Статус:** Высокий приоритет (логическая целостность)

**Почему:** Промазы должны запомниться, новая переоценка не должна спустить цену ниже floor.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/reprice.py`
- `/mnt/c/Users/VVK/GameHelper/vault/tabflow/miss_overrides.json`

**План действий:**

1. В `reprice.py`, в функции `make_plan()` добавить:
```python
def load_miss_overrides() -> dict[frozenset, float]:
    """Возвращает {mod_key → floor_price} из miss_overrides.json"""
    if not OVERRIDES_PATH.exists():
        return {}
    overrides = json.loads(OVERRIDES_PATH.read_text())
    return {
        frozenset(o["mod_templates"]): o["floor_price"]
        for o in overrides if o.get("status") == "pending"
    }

# В цикле по `unsold`:
miss_floors = load_miss_overrides()
floor = miss_floors.get(mod_k)
if floor and new_price < floor:
    print(f"  ⛔ [{col},{row}] новая цена {new_price} < floor {floor} — пропуск")
    continue  # Не добавляем в план
```

2. Unit-тест: создать override для мод-комбо, проверить что план не включает её.

**Ожидаемый результат:** Консистентная логика: промазы не переоцениваются вниз.

---

### ПРИОРИТЕТ 2: Важные (MEDIUM)

#### **P2.1 — Создать общий модуль для нормализации модов**

**Статус:** Средний приоритет (код-организация)

**Почему:** Регексы разбросаны по 4 файлам, риск рассинхронизации.

**Файлы:**
- `train_evaluator.py`
- `predict.py`
- `match_sales.py`
- `reprice.py`
- `evaluate_clipboard.py`

**План действий:**

1. Создать `scripts/tabflow/mod_utils.py`:
```python
# Единые регексы
_POE_TAG_PATTERN = re.compile(r'\[([^\|]+\|)?([^\]]+)\]')
_RANGE_PATTERN  = re.compile(r'\(\d+(?:\.\d+)?-\d+(?:\.\d+)?\)')
_NUMBER_PATTERN = re.compile(r'\d+(?:\.\d+)?')

def strip_poe_tags(text: str) -> str:
    """Убрать [Tag|Display] → Display"""
    return _POE_TAG_PATTERN.sub(lambda m: m.group(2), text)

def normalize_mod(mod: str) -> str:
    """Полная нормализация: strip_poe_tags → lowercase → trim"""
    return strip_poe_tags(mod).strip().lower()

def mod_template(mod: str, preserve_ranges: bool = False) -> str:
    """Шаблон для сравнения: числа → #, опционально сохранить диапазоны"""
    s = strip_poe_tags(mod)
    if not preserve_ranges:
        s = _RANGE_PATTERN.sub('', s)
    s = _NUMBER_PATTERN.sub('#', s)
    return s.strip().lower()

def mod_set(mods: list[str]) -> frozenset[str]:
    """Нормализованное множество модов"""
    return frozenset(mod_template(m) for m in mods if m.strip())
```

2. Во всех файлах заменить локальные функции на импорт:
```python
from mod_utils import normalize_mod, mod_template, mod_set
```

3. Unit-тесты в `test_mod_utils.py` с примерами из реальных листингов.

**Ожидаемый результат:** DRY-принцип соблюдён, тестирование проще.

---

#### **P2.2 — Адаптивный early_stopping в train_evaluator.py**

**Статус:** Средний приоритет (качество моделей на малых данных)

**Почему:** Моделей на <50 примеров недообучаются с early_stopping=30.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/train_evaluator.py` (строка 292)

**План действий:**

1. Изменить в функции `train_model()`:
```python
def train_model(items: list[dict], vocab: dict[str, int], tablet_type: str) -> dict:
    X = np.array([extract_features(it, vocab) for it in items])
    y = np.log1p(...)
    
    X_train, X_val, y_train, y_val = train_test_split(...)
    
    # Адаптивный early_stopping: мало данных = более терпелив
    early_stop_rounds = max(5, min(30, len(X_train) // 5))
    
    callbacks = [lgb.early_stopping(early_stop_rounds, verbose=False), ...]
```

2. Логировать выбранный early_stop_rounds:
```python
print(f"  early_stop_rounds={early_stop_rounds} (adaptive for n_samples={len(X_train)})")
```

3. Тест: обучить на малой выборке (10 примеров), проверить что `num_boost_round` > 50.

**Ожидаемый результат:** Модели на малых данных более полные, MAE может чуть упасть.

---

#### **P2.3 — Версионирование моделей**

**Статус:** Средний приоритет (безопасность, трейсировка)

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/train_evaluator.py` (строка 320, функция `save_model`)

**План действий:**

1. Изменить структуру папок:
```
models/ritual_tablet/
  _current → latest/  # симлинк на последнюю версию
  v001_2026-07-16_165items_MAE0.31/
    model.txt
    vocab.json
    metadata.json
  v002_2026-07-17_185items_MAE0.29/
    model.txt
    vocab.json
    metadata.json
```

2. В `save_model()`:
```python
def save_model(tablet_type: str, model, vocab, metrics, models_dir, text_to_hash=None):
    # Поколение = количество существующих моделей
    versions = sorted([d for d in (models_dir / tablet_type).glob("v*")])
    gen = len(versions) + 1
    
    version_name = f"v{gen:03d}_{datetime.now():%Y-%m-%d}_{metrics['n_train']}items_MAE{metrics['mae_d']:.2f}"
    out = models_dir / tablet_type / version_name
    out.mkdir(parents=True, exist_ok=True)
    
    model.save_model(str(out / "model.txt"))
    # ... сохранить vocab и metadata ...
    
    # Обновить симлинк _current
    current_link = models_dir / tablet_type / "_current"
    if current_link.exists():
        current_link.unlink()
    current_link.symlink_to(version_name)
```

3. В `predict.py` и `evaluate_clipboard.py` изменить:
```python
model_dir = models_dir / slug / "_current"  # вместо просто models_dir / slug
```

4. CLI для отката: `train_evaluator.py --rollback v001`

**Ожидаемый результат:** История моделей сохранена, откат на 1 клик.

---

#### **P2.4 — Нечёткое сопоставление продаж в match_sales.py**

**Статус:** Средний приоритет (ложные промахи)

**Почему:** Точное совпадение mod_set() может упустить продажи из-за minor форматирования.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/scripts/match_sales.py` (строка 141)

**План действий:**

1. Добавить функцию Jaccard-подобия:
```python
def mod_set_similarity(set1: frozenset[str], set2: frozenset[str]) -> float:
    """Jaccard similarity: |A ∩ B| / |A ∪ B|"""
    if not set1 and not set2:
        return 1.0
    intersection = len(set1 & set2)
    union = len(set1 | set2)
    return intersection / union if union > 0 else 0.0

# В match():
for s in candidates:
    similarity = mod_set_similarity(target_mods, mod_set(sale_mods(s)))
    if similarity < 0.85:  # требуем 85% сходства
        continue
    # ... остальная логика ...
```

2. Логировать низкое сходство (debug):
```python
if 0.5 < similarity < 0.85:
    print(f"  [DEBUG] низкое сходство {similarity:.1%}: {entry.get('id')} vs {s['itemId']}")
```

3. Unit-тест с примерами, где есть опечатка в моде.

**Ожидаемый результат:** Меньше ложных промахов, больше правильных сопоставлений.

---

### ПРИОРИТЕТ 3: Оптимизация (LOW)

#### **P3.1 — Конфигурируемый режим листинга (ceiling vs round)**

**Статус:** Низкий приоритет (A/B тестирование)

**Почему:** Ceiling может быть слишком консервативным для быстрого оборота.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/Services/TabletListingService.cs` (строка 130)

**План действий:**

1. Добавить property:
```csharp
public enum ListingRoundMode { Ceiling, Round, Floor }
public ListingRoundMode RoundMode { get; set; } = ListingRoundMode.Ceiling;

// В ListAsync():
int listingPrice = RoundMode switch
{
    ListingRoundMode.Ceiling => (int)Math.Ceiling(estimatedPrice),
    ListingRoundMode.Round    => (int)Math.Round(estimatedPrice),
    ListingRoundMode.Floor    => (int)Math.Floor(Math.Max(MinListingPrice, estimatedPrice)),
    _ => (int)Math.Ceiling(estimatedPrice)
};
```

2. В конфиге GameHelper позволить выбор режима.

3. Логировать выбор в listings.jsonl для A/B анализа.

**Ожидаемый результат:** Возможность экспериментировать с ценообразованием без изменения кода.

---

#### **P3.2 — Кэширование моделей в памяти**

**Статус:** Низкий приоритет (performance, если evaluate_clipboard запускается часто)

**Почему:** `predict.py` сейчас не кэширует модели между вызовами.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/scripts/tabflow/predict.py` (строка 302)

**План действий:**

1. Если evaluate_clipboard.py запускается из WPF:
   - Вместо subprocess запускать Python-сервер (Flask/FastAPI)
   - Модели загружаются один раз в памяти
   - HTTP запросы для оценок

2. Или: глобальное кэширование в Python:
```python
_model_cache: dict[str, tuple] = {}

def load_model(slug: str):
    if slug in _model_cache:
        return _model_cache[slug]
    # ... загрузить с диска ...
    _model_cache[slug] = (model, vocab, meta)
    return _model_cache[slug]
```

**Ожидаемый результат:** Оценка предмета из буфера < 100 ms вместо ~500 ms.

---

#### **P3.3 — Архивирование старых записей в listings_index**

**Статус:** Низкий приоритет (масштабируемость при длительной работе)

**Почему:** Индекс растёт линейно, файл становится большим.

**Файлы:**
- `/mnt/c/Users/VVK/GameHelper/Services/TabletListingsIndex.cs` (строка 89)

**План действий:**

1. Добавить метод архивирования:
```csharp
public static void ArchiveOldEntries(int daysToKeep = 30)
{
    lock (_lock)
    {
        var entries = Load();
        var cutoff = DateTime.Now.AddDays(-daysToKeep);
        var archived = entries.Where(e => e.Sold && DateTime.Parse(e.SaleTime) < cutoff).ToList();
        var active = entries.Except(archived).ToList();
        
        if (archived.Count > 0)
        {
            // Сохранить archived в listings_index.archive.json
            var archivePath = IndexPath.Replace("listings_index.json", "listings_index.archive.json");
            var existing = File.Exists(archivePath) ? JsonSerializer.Deserialize<List<...>>(File.ReadAllText(archivePath)) : new();
            existing.AddRange(archived);
            File.WriteAllText(archivePath, JsonSerializer.Serialize(existing, JsonOpts));
            
            // Сохранить только active в основной файл
            Save(active);
        }
    }
}
```

2. Вызывать `ArchiveOldEntries()` один раз в день (scheduler).

**Ожидаемый результат:** listings_index.json < 10 MB, быстрый парс и сохранение.

---

## 6. Таблица рекомендаций с приоритетами

| ID | Название | Приоритет | Сложность | Ожидаемый эффект | Файлы |
|---|---|---|---|---|---|
| P1.1 | Единый predictor для predict.py и evaluate_clipboard.py | HIGH | HIGH | −200 LOC, 1 источник истины | `predictor.py` |
| P1.2 | Интегрировать reprice.py в SmartRepricingService | HIGH | MEDIUM | UI без ручных скриптов | `SmartRepricingService.cs`, `reprice.py` |
| P1.3 | Учитывать miss_overrides в reprice.py | HIGH | LOW | Консистентность логики | `reprice.py`, `miss_overrides.json` |
| P2.1 | Общий модуль mod_utils.py | MEDIUM | LOW | DRY, тестируемо | `mod_utils.py` (новый) |
| P2.2 | Адаптивный early_stopping | MEDIUM | LOW | Лучшее качество на малых данных | `train_evaluator.py` |
| P2.3 | Версионирование моделей | MEDIUM | MEDIUM | Безопасный откат | `train_evaluator.py`, структура папок |
| P2.4 | Нечёткое сопоставление продаж | MEDIUM | MEDIUM | Меньше ложных промахов | `match_sales.py` |
| P3.1 | Конфигурируемый режим листинга | LOW | LOW | A/B тестирование ценообразования | `TabletListingService.cs` |
| P3.2 | Кэширование моделей в памяти | LOW | MEDIUM | Оценка < 100 ms | `predict.py`, `evaluate_clipboard.py` |
| P3.3 | Архивирование старых записей | LOW | MEDIUM | Масштабируемость | `TabletListingsIndex.cs` |

---

## 7. Итоговая оценка системы

### Компоненты

| Компонент | Статус | Оценка | Комментарий |
|-----------|--------|--------|------------|
| **Python ML** (обучение) | ✓ Solid | 4/5 | Хороший выбор LightGBM, нормализация роллов правильная |
| **Python Inference** (предсказание) | ⚠ Дублировано | 2.5/5 | predict.py и evaluate_clipboard.py делают одно |
| **C# UI (листинг)** | ✓ Надёжная | 4/5 | Шорткаты, fallback, логирование работают |
| **C# UI (переоценка)** | ✓ Надёжная | 4/5 | Два режима (same price / currency switch) хорошо обработаны |
| **Python Feedback** (match_sales) | ⚠ Хрупкая | 3/5 | Точное совпадение модов может упустить |
| **Python Repricing** (reprice.py) | ⚠ Отдельная | 3/5 | Не знает о miss_overrides, нет интеграции |
| **Интеграция Python-C#** | ✗ Слабая | 2/5 | Ручные запуски, нет UI для координации |
| **Обработка ошибок** | ✗ Минимальна | 1/5 | Много unchecked exceptions |
| **Тестирование** | ✗ Отсутствует | 0/5 | Нет unit-тестов, только manual QA |

### Взвешенная оценка

```
ML Quality:              4.2/5  (60% вес)  = 2.52
Integration:             2.5/5  (30% вес)  = 0.75
Reliability:             2.5/5  (10% вес)  = 0.25
                                           ────────
ИТОГО:                                     3.52/5
```

**Вывод:** Система **функциональна и готова к производству**, но имеет **технический долг** в области интеграции, дублирования кода и обработки ошибок. **Quick wins (P1.1, P1.3, P2.1) дадут 30-40% улучшения архитектуры без больших затрат.**

---

## 8. Roadmap развития

### На месяц (Sprint 1)

- [x] P1.3: Учитывать miss_overrides в reprice.py
- [x] P2.1: Создать mod_utils.py
- [x] P2.2: Адаптивный early_stopping
- [ ] Добавить 5-10 unit-тестов для парсинга и нормализации

### На квартал (Sprint 2–3)

- [x] P1.1: Единый predictor — создан predictor.py (TabletPredictor + PredictionResult)
- [x] P1.2: Интегрировать reprice в UI — GeneratePlanAsync в SmartRepricingService, кнопка упрощена
- [x] P2.3: Версионирование моделей — _current.txt + v001_..., --rollback, --list-versions
- [x] P2.4: Нечёткое сопоставление продаж

### На полугодие (Sprint 4+)

- [x] P3.1: Конфигурируемое ценообразование — ListingRoundMode enum в TabletListingService.cs
- [ ] P3.2: Кэширование в памяти / HTTP сервер для Python
- [x] P3.3: Архивирование — ArchiveOldEntries(keepDays) в TabletListingsIndex.cs
- [ ] API-интеграция с poe.ninja для курсов валют
- [ ] Dashboard с метриками (успешно проданных, MAE тренды, velocity по типам)

---

## Заключение

**TabFlow — хорошо продуманная система** с правильным выбором технологий (LightGBM, log-масштабирование, нормализация роллов). **Основная работа выполнена.** 

**Оставшаяся работа — архитектурная чистота:**
- убрать дублирование кода (P1.1)
- связать Python и C# (P1.2)  
- сделать алгоритмы консистентными (P2.1, P1.3)

После этих улучшений система будет готова к **масштабированию на несколько типов табличек одновременно и автоматизации полного цикла.**

