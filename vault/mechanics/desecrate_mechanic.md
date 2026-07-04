# Desecrate Mechanic

## Инструменты

- **Preserved Cranium** — активирует Desecrate, показывает 3 суффикса на выбор
- **Omen of Abyssal Echoes** — реролл этих трёх вариантов (1 попытка)
- **Omen of Light** — убрать уже добавленный desecrated мод (если не тот)

## Десекрейт-пул для Time-Lost Sapphire

Десекрейт показывает **только моды семейства Lightless** (`familyId: AbyssRadiusJewelMod`).
Пул = 12 вариантов (6 prefix + 6 suffix) — см. полный список в `docs/CRAFTING_MATERIALS.md`.

**Цель крафта:** "+1% increased maximum Mana" — это Desecrated **PREFIX**.
При его наличии все 3 суффикса на предмете могут быть натуральными (Chaos Orb роллы).

## Математика (для Time-Lost Sapphire — получить +1% max Mana)

Пул Desecrate = 12 модов (6 prefix + 6 suffix).
При наличии на предмете свободного prefix слота: вероятность "+1% max Mana" = 1/6 ≈ 16.7% за вариант.

```
P("+1% max Mana" в 3 вариантах из prefix-пула 6) = 1 - C(5,3)/C(6,3) = 1 - 10/20 = 50%
P(с 2 попытками через Abyssal Echo) = 1 - (0.5)² = 75%
E(циклов без Echo) ≈ 2, с Echo ≈ 1.33
```

⚠️ Математика выше требует верификации на практике — вес каждого мода в пуле не задокументирован (weight=null).

## Математика для CHS/CH через Chaos Orb (старый расчёт)

Если цель — получить CHS (of Annihilating) или CH (of Annihilation) через **Chaos Orb** (не десекрейт):

Пул суффиксов TLS = 34 натуральных суффикса.
В момент применения на предмете: CS(frac) + CD = 2 суффикса заняты → пул = 32 из которых 34 тотал − 2 = 32 открытых.

Цели: CHS (of Annihilating) + CH (of Annihilation) = **2 из 32**

1 цикл (Cranium + Abyssal Echo) ≈ 7.12d → **итого ~22.15d** (данные 2026-06-26)

## Как это выглядит в игре

1. Применить Preserved Cranium → появляются 3 суффикса
2. Выбрать нужный (или применить Abyssal Echo если ни один не подходит)
3. Если выбранный не тот — Omen of Light → убрать → повторить

## Факт (батч 2026-06-26)

14 циклов на 4 базы (3.5 avg ≈ ожидание 3.11). Стоимость ~22d.

Результаты:
- База 1: CHS 7/7 (max) ← повезло
- База 2: CHS for Spells 4%
- База 3: CHS 4%
- База 4: CHS for Spells 3%

## Ссылки

- [[sapphire_v1_desecrate]] — использование в пайплайне
- [[time_lost_sapphire]] — пул аффиксов
