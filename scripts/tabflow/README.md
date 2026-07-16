# TabFlow — Руководство

Полный цикл заработка на планшетках: сбор данных → обучение модели → оценка.

Архитектура: `vault/ideas/waystone_tablet_market_cycle.md`

---

## Структура папок

```
C:\Users\VVK\GameHelper\               ← корень проекта
  trade_data\                          ← снапшоты (пишет GameHelper)
  scripts\
    tabflow\                           ← все Python-скрипты TabFlow
      train_evaluator.py
      models\
        ritual_tablet\
          model.txt
          vocab.json
          metadata.json
```

---

## Где запускать Python-скрипты

Все команды запускаются в **WSL-терминале** (Ubuntu).  
Корень проекта в WSL: `/mnt/c/Users/VVK/GameHelper/`

Перейти в папку скриптов TabFlow:

```bash
cd /mnt/c/Users/VVK/GameHelper/scripts/tabflow
```

> Из этой папки запускаются **все команды** в этом документе.  
> Скрипты сами находят `trade_data/` и `poe_ninja_prices.json` через относительный путь `../../`.

---

## Установка зависимостей

Один раз, в WSL-терминале:

```bash
sudo apt-get install -y python3-venv libgomp1
cd /mnt/c/Users/VVK/GameHelper/scripts/tabflow
python3 -m venv .venv
.venv/bin/pip install lightgbm numpy scikit-learn
```

Проверить что всё установлено:

```bash
.venv/bin/python3 -c "import lightgbm, numpy, sklearn; print('OK')"
```

---

## Шаг 1 — Сбор снапшотов (GameHelper, вкладка Наблюдение)

Запускается на **Windows**, не в терминале — это GUI приложение.

**Цель:** накопить ~300+ листингов планшеток с rich-модами.

### Порядок действий

1. Запустить `GameHelper.exe`
2. Перейти на вкладку **Наблюдение**
3. Нажать **▶ Слушать** — статус изменится на `Слушаю :7123`
4. Открыть браузер → `trade.pathofexile.com` → выбрать тип планшетки → настроить фильтры
5. Прокрутить страницу результатов до конца (Tampermonkey перехватывает запросы автоматически)
6. Вернуться в GameHelper — в статусе появится `Буфер: N пред.`
7. Нажать **Снимок** — данные записаны в `trade_data\`
8. Сменить фильтры на трейде → повторить с шага 4

### Стратегия cross-section (для каждого типа планшетки)

Цель: покрыть пространство модов диагональными срезами, не перебирая все комбинации.

| Запрос | Что фильтровать на трейде |
|--------|--------------------------|
| 1      | Prefix mod 1 + Prefix mod 2 + Suffix mod N + Suffix mod N-1 |
| 2      | Prefix mod 2 + Prefix mod 3 + Suffix mod N-1 + Suffix mod N-2 |
| 3      | Prefix mod 3 + Prefix mod 4 + Suffix mod N-2 + Suffix mod N-3 |
| ...    | сдвигаться по списку модов |
| +1     | **без фильтра по модам** — только тип планшетки |

Последний запрос без фильтра даёт флорные предметы (premium ≈ 0) — критически важен для модели.

Итого ~12–15 снапшотов на тип × ~100 листингов = ~1500 обучающих примеров.

### Типы планшеток (base_type в trade_data)

```
Ritual Tablet     Abyss Tablet      Breach Tablet    Expedition Tablet
Delirium Tablet   Irradiated Tablet  Overseer Tablet  Temple Tablet
```

### Что записывается в trade_data

Снапшоты сохраняются автоматически как `trade_data\YYYY-MM-DD_HH-MM-SS_{тип}.json`.

Каждый листинг содержит (начиная со снапшотов после 2026-07-16):
```json
{
  "base_type": "Ritual Tablet",
  "price_divine": 2.5,
  "price_currency": "divine",
  "mods_explicit": ["Tribute S1 — ...grant 50% increased Tribute"],
  "mods_explicit_rich": [
    {
      "text":     "Tribute S1 — ...grant 50% increased Tribute",
      "hash":     "explicit.stat_4052037485",
      "value":    50.0,
      "roll_min": 30.0,
      "roll_max": 70.0
    }
  ]
}
```

> Старые снапшоты без `mods_explicit_rich` автоматически пропускаются при обучении.  
> Поле `mods_explicit` (строки) остаётся — UI вкладки Наблюдение использует его.

---

## Шаг 2 — Обучение модели

**Терминал:** WSL  
**Папка:** `/mnt/c/Users/VVK/GameHelper/scripts/tabflow`

```bash
cd /mnt/c/Users/VVK/GameHelper/scripts/tabflow
.venv/bin/python3 train_evaluator.py
```

### Аргументы

| Аргумент | По умолчанию | Описание |
|----------|-------------|----------|
| `--data-dir PATH` | `../../trade_data` | Папка со снапшотами |
| `--models-dir PATH` | `./models` | Куда сохранять модели |
| `--min-samples N` | `30` | Минимум предметов для обучения типа |
| `--types TYPE ...` | все типы | Обучить только указанные типы |

Имена типов в `--types` — это `base_type` в нижнем регистре с пробелами → подчёркивания:  
`Ritual Tablet` → `ritual_tablet`, `Abyss Tablet` → `abyss_tablet`

### Примеры

```bash
# Обучить все найденные типы
.venv/bin/python3 train_evaluator.py

# Только ritual и abyss
.venv/bin/python3 train_evaluator.py --types ritual_tablet abyss_tablet

# При малой выборке (для проверки что всё работает)
.venv/bin/python3 train_evaluator.py --min-samples 10

# Явные пути (если запускаешь из другой папки)
python3 /mnt/c/Users/VVK/GameHelper/scripts/tabflow/train_evaluator.py \
  --data-dir /mnt/c/Users/VVK/GameHelper/trade_data \
  --models-dir /mnt/c/Users/VVK/GameHelper/scripts/tabflow/models
```

### Пример вывода

```
=== Tablet Evaluator Training ===
trade_data:  /mnt/c/Users/VVK/GameHelper/trade_data
models_dir:  ./models
Курсы валют загружены: 84 позиций

[1] Загрузка планшеток из trade_data/
Файлов в trade_data/: 47
  пропущено (нет rich-модов): 1240
  пропущено (цена): 3
  ritual_tablet: 183 предметов
  abyss_tablet: 97 предметов

[ritual_tablet]
  фичей (уникальных модов): 31
  фильтр цен [0.20d, 4.80d]: 183 → 165 предметов
  val RMSE(log): 0.1823  |  MAE(divine): 0.31d
  → сохранено: models/ritual_tablet/
    топ-3 фичи: [('explicit.stat_XXX', 142.3), ...]
```

### Файлы после обучения

```
models/ritual_tablet/
  model.txt       — LightGBM модель в native format (читается Python и C++)
  vocab.json      — {hash: feature_index} — нужен при inference
  metadata.json   — метрики, топ фич, размер выборки
```

---

## Шаг 3 — Оценка планшеток

**Терминал:** WSL  
**Папка:** `/mnt/c/Users/VVK/GameHelper/scripts/tabflow`

Снять снапшот через вкладку Наблюдение → запустить:

```bash
# Оценить последний снапшот с планшетками
.venv/bin/python3 predict.py

# Конкретный файл (полное имя или часть)
.venv/bin/python3 predict.py --snapshot rit_s19

# Последние 3 снапшота сразу
.venv/bin/python3 predict.py --latest 3

# Только предметы с маржой >= 0.5d, показать топ-30
.venv/bin/python3 predict.py --min-margin 0.5 --top 30
```

### Пример вывода

```
════════════════════════════════════════════════════════════════════════
  Ritual Tablet  —  2026-07-16_15-30-00_rit_s19.json  (95 предметов)
  Модель: MAE 0.31d  |  обучена на 165 предметах
════════════════════════════════════════════════════════════════════════
    #  листинг    предск     маржа  моды
  ───  ────────  ────────  ────────  ──────────────────────────────
    1     0.80d     1.21d    +0.41d  Omens▲  Tribute·  ItemRarity▲
    2     1.50d     1.83d    +0.33d  Omens·  RareMon▲  PackSize
    3     2.00d     2.28d    +0.28d  Omens▲  Tribute▲  MonRarity·
  ...
  Итого подходящих: 12 из 95
  Медианная маржа:  0.18d
  Лучшая маржа:     0.41d
```

**Индикаторы ролла:** `▲` = топ 25%  ·  `·` = средний  ·  `▽` = нижние 25%  
Моды отсортированы по важности (самые влиятельные по модели — первые).

### Аргументы

| Аргумент | По умолчанию | Описание |
|----------|-------------|----------|
| `--snapshot NAME` | авто | Имя файла в trade_data/ (полное или частичное) |
| `--latest N` | `1` | Сколько последних снапшотов взять |
| `--top N` | `20` | Показать топ-N предметов |
| `--min-margin X` | `0.0` | Минимальная маржа в divine |
| `--data-dir PATH` | `../../trade_data` | Папка со снапшотами |

---

## Шаг 5 — Переобучение

**Когда:** после патча GGG, или когда реальные продажи систематически расходятся с предсказанием.

**Терминал:** WSL  
**Папка:** `/mnt/c/Users/VVK/GameHelper/scripts/tabflow`

```bash
.venv/bin/python3 train_evaluator.py --types ritual_tablet
```

Старая модель перезаписывается. Метрики сравни с предыдущими в `metadata.json`.

---

## Диагностика

Все команды ниже — WSL-терминал, папка `/mnt/c/Users/VVK/GameHelper/scripts/tabflow`.

### Проверить что новые снапшоты содержат rich-моды

```bash
.venv/bin/python3 -c "
import json, glob
files = sorted(glob.glob('../../trade_data/*.json'))[-5:]
for f in files:
    d = json.load(open(f))
    tablets = [x for x in d.get('listings', []) if 'tablet' in x.get('base_type','').lower()]
    rich    = [x for x in tablets if x.get('mods_explicit_rich')]
    print(f.split('/')[-1], f'  tablets={len(tablets)}  rich={len(rich)}')
"
```

Ожидаемый вывод для нового снапшота:
```
2026-07-16_15-30-00_ritual_tablet.json  tablets=95  rich=95
```

Если `rich=0` — снапшот снят старой версией GameHelper (до 2026-07-16). Пересними.

### Сколько планшеток накоплено по типам

```bash
.venv/bin/python3 -c "
import json, glob
from collections import defaultdict
counts = defaultdict(int)
seen = set()
for f in glob.glob('../../trade_data/*.json'):
    for x in json.load(open(f)).get('listings', []):
        bt = x.get('base_type','')
        if 'tablet' in bt.lower() and x.get('mods_explicit_rich') and x['id'] not in seen:
            seen.add(x['id'])
            counts[bt] += 1
for bt, n in sorted(counts.items()):
    status = '✓' if n >= 30 else f'нужно ещё {30-n}'
    print(f'  {n:4d}  {bt}  {status}')
"
```

### Посмотреть топ-фичи обученной модели

```bash
python3 -c "
import json
m = json.load(open('models/ritual_tablet/metadata.json'))
print(f'MAE: {m[\"mae_divine\"]}d  |  выборка: {m[\"n_train\"]} train + {m[\"n_val\"]} val')
print()
for h, imp in m['top_features'][:10]:
    print(f'  {imp:8.1f}  {h}')
"
```

### Посмотреть словарь фич (какой hash соответствует какому моду)

```bash
python3 -c "
import json
vocab = json.load(open('models/ritual_tablet/vocab.json'))
print(f'Всего фич: {len(vocab)}')
for h, i in list(vocab.items())[:5]:
    print(f'  [{i:2d}] {h}')
"
```
