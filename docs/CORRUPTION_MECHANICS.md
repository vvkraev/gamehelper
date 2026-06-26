# Механика Corruption (Vaal Orb) в PoE2

Источник: https://www.poe2wiki.net/wiki/Corrupted (25 June 2026)

## Общие правила

- Corrupted-предметы **нельзя изменять** обычными методами крафта
- Socketable-предметы (руны, soul cores, talismans) можно вставлять и заменять даже в corrupted-предмете
- Corrupted skill gems можно поднять в уровне через Uncut Gem
- Corrupted-тег нельзя снять — необратимо

## Источники corruption

- **Vaal Orb** — применяется к предмету вручную, даёт один unpredictable outcome
- **Corruption Altar** (Paquate's Mechanism, Act 3, Jiquani's Sanctum) — применяет эффект Vaal Orb один раз за персонажа
- Corrupted Strongboxes — содержимое уже corrupted
- Trial of Chaos — награды обычно corrupted

---

## Пулы исходов

Вероятности **равные** между исходами (≈25% каждый при 4 исходах).
Эффект внутри исхода (какой именно мод, какой enchantment) может использовать weight-систему.

### Не-уникальное снаряжение (оружие, броня)

| # | Исход |
|---|---|
| 1 | **Без изменений** |
| 2 | **Перекрутить до трёх модификаторов** в новые |
| 3 | **Добавить Vaal enchantment** (из пула для класса предмета и ilvl) |
| 4 | **Добавить сокет** (оружие включая sceptres, броня) |

> **Для ювелирных украшений:** исход 4 = «Без изменений» (не убирается Omen of Corruption).

**Ограничения добавления сокета:**
- Предмет не может иметь сокетов больше, чем физическое место на нём
- Если в предмете стоит Cadigan's Epiphany — augment socket нельзя получить через corruption

### Уникальное снаряжение

| # | Исход |
|---|---|
| 1 | **Без изменений** |
| 2 | **Мультипликатор величин модификаторов** × 0.78–1.22 (шаг 0.01) перед округлением |
| 3 | **Добавить Vaal enchantment** |
| 4 | **Добавить сокет** (оружие/броня; ювелирка = без изменений) |

> Мультипликатор применяется до эффектов качества. Округление: ближайшее целое (`trunc(value + 0.5)`), что может смещать результаты для отрицательных статов.

---

## Omen of Corruption

Убирает исход «Без изменений», повышая шансы остальных трёх исходов до ≈33% каждый.

**Не убирает:**
- «Без изменений» для ювелирных украшений (исход 4 замещён на него)
- «Псевдо»-без изменений: например, quality change = 0

---

## Дополнительные инструменты из Atziri's Temple

| Инструмент | Источник | Эффект |
|---|---|---|
| **Masterwork Forge** (Infusers) | Golden Forge | Поднимает quality выше 20%, может сам corrupted-ить предмет |
| **Architect's Orb** | Locus of Corruption | **Гарантирует enchantment**; 50% — добавляет второй, 50% — уничтожает предмет |
| **Gem Corrupter** (Crystallised Corruption) | Thaumaturge's Cathedral | Модифицирует corrupted gem или уничтожает; 50/50 |
| **Morphology Mechanism** (Vaal Cultivation Orb) | Apex of Oblation | Заменяет моды на Corrupted Vaal Unique |
| **Vaal Siphoner** | Atziri drop | Понижает tier случайного explicit mod на 1 на corrupted rare ювелирке |
| **Orb of Sacrifice** | Atziri drop | Убирает random explicit mod, улучшая corruption enchantment |

---

## Исходы для других типов предметов

### Гемы
- Без изменений
- ±1 gem level (сохраняется при levelup через Uncut Gem; не уходит ниже 1)
- Добавить или убрать сокет
- ±10% quality, максимум 23%

### Флаконы и обаяния (Flasks & Charms)
- ±10% quality, максимум 23%

### Не-уникальные jewels
- Без изменений
- Перекрутить до трёх explicit модификаторов
- Добавить Vaal enchantment
- Добавить или убрать explicit modifier (игнорирует лимит 2P/2S)

### Waystones
- Без изменений
- ±1 Waystone Tier (рерол модов) — единственный способ получить Tier 16
- Lock prefixes → rforge suffixes (или наоборот)
- Lock all → добавить 0–4 random модов (итого до 8)

---

## Применение к крафту копья Soaring Spear

```
Exceptional база (2 сокета)  →  Vaal Orb  →  +1 socket (исход 4, ≈25%)
                                            →  Итого 3 сокета
```

### Содержимое сокетов и вклад в APS

**Soul Core of Quipolatl** (+5% increased Attack Speed, Martial Weapon) — Augment Socket.  
**Destruction Rune** ("Can roll Destruction modifiers") — Rune Socket, 0% AS.

| Сокеты | Состав | AS от сокетов | APS (Thrud's 29%, Cel 28%, Verisium 8%) |
|---|---|---|---|
| 1 (стандарт) | 1 Destruction Rune | 0% | 1.70×1.4412 = 2.450 |
| 2 (exceptional) | 1 Destruction Rune + 1 Soul Core | +5% | 1.70×1.4912 = 2.535 |
| 3 (exceptional + corruption) | 1 Destruction Rune + 2 Soul Core | +10% | 1.70×1.5412 = 2.620 ✓ |

**Полный расчёт для финального предмета (3 сокета, Thrud's 29%, Cel 28%):**
```
of Celebration: 28% × 1.29 = 36.12%   ← усилено Thrud's
Verisium:        8%
Soul Core × 2:  10%  (два Augment Socket)
Total:          54.12%
APS = 1.70 × 1.5412 = 2.620 APS  ✓
```

Вклад corruption (+1 сокет → 2-й Soul Core): **+0.085 APS**.

Corruption на сокет: **~25% без Omen** / **~33% с Omen of Corruption**.

**Цена вопроса при Omen of Corruption:** убирает «Без изменений», оставляя три исхода:
- Перекрут модов (~33%) — может убить Thrud's или of Celebration!
- Vaal enchantment (~33%) — полезно, но не цель
- +1 socket (~33%) — цель

> Использовать Omen of Corruption стоит только если риск потери модов приемлем для ожидаемой прибыли.
