# Sleek Jacket — Oblivion Shell (Banshee's fractured)

**База:** Sleek Jacket (Body Armour, Evasion/ES hybrid)
**ilvl:** 82
**Качество:** +30%
**Дата начала:** 2026-06-28
**Статус:** В процессе

## Текущее состояние (2026-06-28)

```
Item Class: Body Armours
Rarity: Rare
Oblivion Shell / Sleek Jacket
Quality: +30% | Evasion: 576 | ES: 169 | ilvl: 82

{ Fractured Prefix T1 "Banshee's" }
  +158(142-161) Evasion Rating
  +43(43-48) max Energy Shield
{ Suffix T1 "of Tzteosh" }
  +44(41-45)% Fire Resistance
{ Crafted Suffix T3 "of the Polar Bear" }
  +35(31-35)% Cold Resistance   ← placeholder
{ Prefix Desecrated "Veiled" }
  ??? — НЕ РАСКРЫТ
```

**Слоты:** 2/3 prefix, 2/3 suffix → 1 открытый prefix, 1 открытый suffix

---

## Пайплайн и расходы

| # | Шаг | Стоимость | Результат |
|---|---|---|---|
| 1 | Купил белую базу 30% quality | **10d** | Normal Sleek Jacket ilvl 82 |
| 2 | Aug + Annul руками, 30 циклов | **~14.6d** | Magic: 2×T1 аффикс |
| 3 | Essence Cold Resistance | **TBD** | Rare + Cold Res (crafted T3) |
| 4 | Ancient Rib → Desecrated Prefix | **TBD** | добавлен "Veiled" desecrated prefix |
| 5 | Divine Orb × ? | **10d** | улучшение роллов |
| 6 | Fracturing Orb × 1 (попал с первого) | **~6.76d** | зафрактурен Banshee's T1 ✓ |
| **ИТОГО известно** | | **~41.4d + TBD** | |

> Цены: Annul 0.486d, Aug ~0.001d (negligible), Fracturing Orb 6.76d (poe.ninja 2026-06-15)

### Что неизвестно по расходам
- [ ] Essence Cold Resistance: какая именно? (Shrieking/Screaming/Deafening?) Цена?
- [ ] Ancient Rib: цена? (нет в базе цен poe.ninja)
- [ ] Divine: сколько штук (10d суммарно — это ~10 divines по 1d? или иначе)?

---

## Разбор текущих модов

| Слот | Тип | Мод | Ролл | Примечание |
|---|---|---|---|---|
| PREFIX | **fractured** T1 | Banshee's — Evasion + ES | 158/161, 43/48 | защищён, sub-max |
| PREFIX | **desecrate** | "Veiled" — ??? | не раскрыт | ⚠️ нужно раскрыть |
| SUFFIX | explicit T1 | of Tzteosh — Fire Res | 44/45 | почти max |
| SUFFIX | crafted T3 | of the Polar Bear — Cold Res | 35/35 | placeholder |

---

## Открытые вопросы

- [ ] **Veiled Desecrated Prefix**: что будет после раскрытия? Как раскрывается?
- [ ] **Цель крафта**: что нужно в итоге? (Life? ES? второй резист? specific мод для билда?)
- [ ] **Banshee's роллы**: sub-max (158/161 Evas, 43/48 ES) — нужен Divine на них?
- [ ] **Cold Res crafted T3**: убирать через Annul и делать T1? Или оставить как есть?

---

## План крафта

TBD — после раскрытия Veiled мода и прояснения цели

## Рыночный анализ (2026-06-28)

Источник: `trade_data/2026-06-28_sleek_jacket_market.json`  
10 рекомендованных цен по запросу с Banshee's+Incorporeal+Spirit (все топ-левел варианты)

### Ценовые уровни

| Цена | Пример | Что отличает |
|---|---|---|
| 565–700d | Plague Veil, Rage Curtain, Onslaught Cloak | corrupted / нет фрактуры / P2 вместо P1 |
| **850–880d** | **Rage Keep, Corpse Carapace** | **Banshee's P1 fractured + все 6 T1 модов + чистый** |
| 1000–1188d | Havoc Hide, Mind Veil | of Warping fractured / Spirit frac + corrupted |
| 1188–1688d | Demon Guardian, Rune Mantle | Spirit (Queen's P1) fractured + чистый |
| 1950d | Doom Skin | 4 сокетов (несмотря на P2+S2 моды) |

### 6 обязательных модов (присутствуют во ВСЕХ рекомендованных ценах)

| Мод | Роль |
|---|---|
| Banshee's P1 — +142-161 Evasion +43-48 ES | PREFIX (fractured/explicit/desecrated) |
| Incorporeal P1 — 101-110% incr Evasion+ES | PREFIX (explicit/desecrated) |
| Queen's P1 — +57-61 Spirit | PREFIX (fractured/explicit/desecrated) |
| of Warping S1 — Deflection 26% Evasion | SUFFIX |
| T1 Resistance × 1 | SUFFIX |
| T1 Resistance × 2 (или S2 = дисконт) | SUFFIX |

### Два прямых аналога Oblivion Shell

**Corpse Carapace** (880d) и **Rage Keep** (850d):
- Banshee's P1 **fractured** — идентично нашему ✓
- Incorporeal P1 — **desecrated** (вероятно, это и есть наш Veiled!)
- Queen's P1 (Spirit) — explicit
- 2× T1 Resistance + of Warping S1 — explicit

→ Это целевая конфигурация для Oblivion Shell

### Оценка позиции

| | |
|---|---|
| Вложено (известно) | ~41.4d |
| + TBD (Essence + Ancient Rib) | +5–15d |
| **Итого оценка** | **~47–56d** |
| Цель при Veiled=Incorporeal + полный докрафт | **850–900d** |
| Потенциальная прибыль | **~800–850d** |

### Критические зависимости

1. **Veiled = Incorporeal P1** → прямой путь к 850-900d (Corpse Carapace template)
2. **Veiled = Spirit (Queen's P1)** → другой архетип, 1200-1688d, но маловероятно
3. **Veiled = что-то ещё** → сильно хуже; нужно смотреть пул десекрейт-модов броней

### Что нужно добавить (если Veiled=Incorporeal)

**Текущие слоты:**
```
PREFIX 1 (frac):  Banshee's T1 ✓
PREFIX 2 (desc):  Veiled = Incorporeal P1? (не раскрыт)
PREFIX 3 (open):  → нужен Queen's P1 (Spirit)
SUFFIX 1 (T1):    Fire Res (of Tzteosh) ✓
SUFFIX 2 (craft): Cold Res T3 (placeholder) → убрать
SUFFIX 3 (open):  → нужен of Warping S1
```

После раскрытия Veiled нужно добавить:
- Queen's P1 (Spirit) в open PREFIX — через Sinistral Exalt Omen?
- of Warping S1 в open SUFFIX — через Dextral Exalt?
- 2nd T1 Res в suffix (после убирания Cold Res T3)
