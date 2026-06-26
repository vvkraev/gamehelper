# Формула расчёта Attacks per Second для копий (Spears)

## Контекст

Копьё используется для передвижения: атака-движение (Spear Throw) двигает персонажа вперёд быстрее обычного бега при высоком APS. Цель крафта — максимальный APS.

**Soaring Spear базовый APS: 1.70**

---

## Базовая формула

```
APS = 1.70 × (1 + Σ increased_attack_speed%)
```

Все бонусы к APS суммируются аддитивно, затем умножаются на базу.

---

## Механика Thrud's Might (ключевая)

**Thrud's Might** — Augment Rune (socket-bound). При вставке НАВСЕГДА фузируется:
- Занимает 1 Augment Socket — **невозможно убрать**
- Добавляет в пул предмета **Destruction-модификаторы** (prefix, fire/lightning)
- Один из Destruction-модов: **"Thrud's" T1** — `X% increased Explicit Speed Modifier magnitudes` (X: 25–30%)

**"Thrud's" T1** — это Prefix, который усиляет значения Explicit Speed-модов:

```
effective_mod = mod_roll × (1 + thruds_roll / 100)
```

Пример: "of Celebration" 28% + Thrud's 30% → 28 × 1.30 = 36.4%

**Thrud's T1 усиляет только:**
- Explicit suffix-моды скорости: "of Celebration", "of Infamy" и т.д.

**Thrud's T1 НЕ усиляет:**
- Crafted-моды (Verisium)
- Soul Core of Quipolatl
- Vaal Enchantment
- Rune-эффекты

---

## Источники increased Attack Speed

| Источник | Тип | Значение | Усиляется Thrud's? |
|---|---|---|---|
| "of Celebration" T1 (explicit suffix) | Explicit | 26–28% | **Да** |
| "of Infamy" T2 (explicit suffix) | Explicit | 23–25% | **Да** |
| Verisium (Celestial Alloy) | Crafted Prefix | 5–8% | Нет |
| Soul Core of Quipolatl | Augment Socket | 5% × (1 + stars%) | Нет |
| Vaal Enchantment "Attack Speed" | Enchantment | 12–16% | Нет |
| Thrud's Might (rune) | Rune Socket | 0% AS (разблокирует Destruction-пул) | — |

---

## Soul Core + "of the Stars"

**"of the Stars"** (Sovereign Alloy crafted suffix): `X% increased effect of Socketed Augment Items` (X: 20–30%)

Soul Core contribution: `5% × (1 + stars_roll%) → floor → displayed%`

**Важное наблюдение:** при любом roll "of the Stars" от 20% до 30%:
```
5 × 1.20 = 6.00 → floor → 6%
5 × 1.28 = 6.40 → floor → 6%
5 × 1.30 = 6.50 → floor → 6%
```
Значение "of the Stars" **не влияет на APS** — Soul Core всегда даёт 6% при любом roll 20–30%.

---

## Поведение floor()

PoE2 применяет floor() к каждому умноженному значению **по отдельности** перед суммированием.

```
"of Celebration" 28% × 1.30 = 36.4 → floor → 36%  (НЕ 36.4%)
Soul Core 5% × 1.28 = 6.4    → floor → 6%           (НЕ 6.4%)
```

Верифицировано: Skull Edge показывает APS 2.82 только при floor-per-mod:
- Без floor: 36.4 + 8 + 6.4 + 16 = 66.8% → 1.70 × 1.668 = 2.84 ✗
- С floor:   36   + 8 + 6   + 16 = 66%   → 1.70 × 1.66  = 2.82 ✓

---

## Верифицированные примеры из трейда

### T2 путь: ~150 div (Brimstone Edge)

```
Thrud's T1 29%, "of Infamy" T2 25%, Verisium 8%, 1× Soul Core

"of Infamy": 25% × 1.29 = 32.25% → 32%
Verisium: 8%
Soul Core: 5-6%
Total: ~46%
APS ≈ 2.45
```

### T1 без фрактуры суффикса: ~600 div (Soul Edge)

```
Thrud's T1 30% (Desecrated), "of Celebration" T1 28%, Verisium 8%,
2× Soul Core (Stars 21%), Corrupted

"of Celebration": 28% × 1.30 = 36.4% → 36%
Verisium: 8%
2× Soul Core (Stars 21%): 5×1.21=6.05→6%, ×2 = 12%
Total: 56%  →  APS = 1.70 × 1.56 = 2.65
(в игре показывает 2.66: возможна более высокая внутренняя точность)
```

### T1 с фрактурой суффикса: ~900 div (Mind Edge)

```
Thrud's T1 29% (regular), "of Celebration" T1 28% (FRACTURED), Verisium 8%,
2× Soul Core (Stars 24%), Corrupted, 3 сокета

"of Celebration": 28% × 1.29 = 36.12%
Verisium: 8%
2× Soul Core (Stars 24%): 5 × 1.24 × 2 = 12.4%
Total: 56.52%
APS = 1.70 × 1.5652 = 2.661 → 2.66 ✓
```

### T1 + Vaal Enchantment: ~1 mirror (Skull Edge)

```
Thrud's T1 30%, "of Celebration" T1 28% (FRACTURED), Verisium 8%,
1× Soul Core (Stars 28%), Vaal Enchantment +16%, Corrupted, 2 сокета

"of Celebration": 28% × 1.30 = 36.4 → floor → 36%
Verisium: 8%
1× Soul Core (Stars 28%): 5 × 1.28 = 6.4 → floor → 6%
Vaal Enchantment: 16% (НЕ усиляется Thrud's)
Total: 66%
APS = 1.70 × 1.66 = 2.822 → 2.82 ✓
```

---

## Таблица APS по конфигурациям (Thrud's 30%, Cel 28%, Verisium 8%)

| Сокеты | Soul Cores | Enchantment | Total | APS |
|---|---|---|---|---|
| 1 (стандарт) | 0 | — | 44% | 2.45 |
| 2 (exceptional) | 1 | — | 50% | 2.55 |
| 3 (exceptional + corrupt) | 2 | — | 56% | 2.65 |
| 2 (exceptional) | 1 | +16% | **66%** | **2.82** |
| 3 (exceptional + corrupt) | 2 | +16% | 72% | 2.92 _(теор.)_ |

Enchantment (+16%) >> +1 socket (+6%). Vaal Orb на exceptional базе:
лучший исход для APS = **Enchantment**, а не +1 socket.

---

## Vaal Enchantment: как получить

Vaal Orb на оружие (4 исхода по ~25%):
1. Без изменений
2. Перекатить до 3 explicit модов ← ОПАСНО для non-fractured модов
3. **Добавить Vaal Enchantment** ← цель
4. +1 socket

**Orb of Sacrifice** (Atziri drop, возможно новый): убирает random explicit mod → улучшает Enchantment.
- Skull Edge (1 mirror) имеет пустой суффикс-слот → свидетельство применения Orb of Sacrifice.
- Потенциально позволяет иметь 3 сокета + Enchantment → теоретический потолок APS 2.92.

---

## Три пути крафта

### Путь A: Фрактура Thrud's T1 → полуфабрикат

```
Exceptional база (24 div) + Thrud's Might →
Chaos Orb до Thrud's T1 →
Divine ≥28% →
Desecrate trick + Fracturing Orb (1/3 шанс) →
Продажа как база ~55 div  (маржа ~5 div)
```
E[стоимость] ≈ 50 div. Высокая ликвидность, быстрый оборот.

### Путь B: Фрактура "of Celebration" T1

```
Exceptional база + Thrud's Might →
Chaos Orb до "of Celebration" T1 (~77 div) →
Desecrate trick + Fracturing Orb (1/2-1/3 шанс) →
Thrud's T1 через Chaos ИЛИ Ancient RIB →
Verisium + Stars + Corrupt
```
E[себестоимость] ≈ 250 div. Результат: Mind Edge тип ~900 div.

### Путь C: Desecrated Thrud's T1

```
Exceptional база + Thrud's Might →
Chaos Orb до "of Celebration" T1 (~77 div) →
Ancient RIB + Omen Sinistral Necromancy → Desecrated Thrud's T1 (max roll 30%)
(Ancient RIB ~1.41 div/попытка, несколько попыток) →
"of Amanamu" Desecrated + Fracturing Orb (1/2 шанс) →
Verisium + Stars + Corrupt
```
E[себестоимость] ≈ 180 div. Результат: Soul Edge тип ~600 div.

---

## Анализ рентабельности

| Продукт | Себестоимость | Цена | Прибыль | Ликвидность |
|---|---|---|---|---|
| База (Путь A) | ~50 div | ~55 div | ~5 div | Высокая, быстро |
| Soul Edge (Путь C) | ~180 div | ~600 div | ~420 div | 1–3 шт/лигу |
| Mind Edge (Путь B) | ~250 div | ~900 div | ~650 div | 1–2 шт/лигу |
| Skull Edge | ~500+ div | ~1 mirror | — | <1/лигу |

**Вывод:** Математика показывает высокий потенциал прибыли, но:
- Рынок тонкий: единицы покупателей за 600+ div в лиге
- Капитал заморожен на недели, риск конца лиги
- Каскадный RNG: каждый шаг с высокой дисперсией

Оптимальная стратегия: база-бизнес (Путь A) как основа, редкие попытки полного пайплайна на исключительных базах.
