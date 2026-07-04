# Глоссарий крафтовых материалов PoE2

Все расходники, которые можно использовать при крафте в Path of Exile 2.
Для каждого — назначение, ограничения, использование в приложении и ссылка на сырой буфер из игры.

Сырые тексты Ctrl+Alt+C: [`docs/item_clips/`](item_clips/)

---

## Содержание

1. [Основные орбы](#1-основные-орбы)
2. [Орбы качества](#2-орбы-качества)
3. [Орбы сокетов](#3-орбы-сокетов)
4. [Омены](#4-омены)
5. [Десекрейт — раскрытие скрытых модов](#5-десекрейт--раскрытие-скрытых-модов)
6. [Liquid Currency — добавление модов](#6-liquid-currency--добавление-модов)
7. [Катализаторы](#7-катализаторы)
8. [Руны](#8-руны)
9. [Soul Cores](#9-soul-cores)
10. [Прочее](#10-прочее)

---

## 1. Основные орбы

### Chaos Orb

| | |
|---|---|
| **Действие** | Заменяет один случайный аффикс на любой другой случайный аффикс из пула |
| **Применимо к** | Magic, Rare |
| **Ограничения** | Нельзя применить к Normal. Нельзя применить если 0 аффиксов |
| **В приложении** | `ChaosCraftService` — основной цикл крафта |
| **Clipboard** | [chaos_orb.txt](item_clips/chaos_orb.txt) |

### Perfect Chaos Orb

| | |
|---|---|
| **Действие** | Как Chaos Orb, но перекатывает выбранный аффикс **в максимальном значении тира** |
| **Применимо к** | Magic, Rare |
| **В приложении** | `AppSettings.ChaosOrbName` по умолчанию = `"Chaos Orb"` — можно переключить на Perfect |
| **Стоимость** | ~3 div (poe.ninja 2026-06-15) |
| **Clipboard** | [perfect_chaos_orb.txt](item_clips/perfect_chaos_orb.txt) |

### Orb of Augmentation

| | |
|---|---|
| **Действие** | Добавляет один случайный аффикс к Magic-предмету (если есть свободный слот) |
| **Применимо к** | Только Magic, если аффиксов < 2 |
| **Ограничения** | Normal, Rare — нельзя |
| **В приложении** | `AugAnnulCraftService` — цикл Aug+Annul |
| **Clipboard** | [orb_of_augmentation.txt](item_clips/orb_of_augmentation.txt) |

### Perfect Orb of Augmentation

| | |
|---|---|
| **Действие** | Как Augmentation, но добавляет аффикс в максимальном значении тира |
| **Применимо к** | Только Magic, если аффиксов < 2 |
| **В приложении** | `AppSettings.AugOrbName` по умолчанию = `"Perfect Orb of Augmentation"` |
| **Стоимость** | ~0.001 div (poe.ninja 2026-06-15) |
| **Clipboard** | [perfect_orb_of_augmentation.txt](item_clips/perfect_orb_of_augmentation.txt) |

### Orb of Annulment

| | |
|---|---|
| **Действие** | Удаляет один случайный аффикс |
| **Применимо к** | Magic (если аффиксов ≥ 1), Rare (если аффиксов ≥ 1) |
| **Ограничения** | Normal — нельзя |
| **В приложении** | `AugAnnulCraftService` — цикл Aug+Annul; omens используют для направленного удаления |
| **Стоимость** | ~0.49 div (poe.ninja 2026-06-15) |
| **Clipboard** | [orb_of_annulment.txt](item_clips/orb_of_annulment.txt) |

### Exalted Orb

| | |
|---|---|
| **Действие** | Добавляет один случайный аффикс к Rare-предмету (если есть свободный слот) |
| **Применимо к** | Rare, если аффиксов < 6 (< 3 префиксов и < 3 суффиксов соответственно) |
| **Ограничения** | Normal, Magic — нельзя |
| **В приложении** | `ExaltationCraftServiceFracturedSide` — крафт с управлением омнами |
| **Стоимость** | ~0.006 div (poe.ninja 2026-06-15) |
| **Clipboard** | [exalted_orb.txt](item_clips/exalted_orb.txt) |

### Divine Orb

| | |
|---|---|
| **Действие** | Перекатывает числовые значения всех аффиксов внутри их тира |
| **Применимо к** | Magic, Rare (имеющие аффиксы со значениями) |
| **Ограничения** | Не меняет сам аффикс, не меняет тир — только value в пределах min-max тира |
| **В приложении** | Не используется в автоматических циклах; применяется вручную |
| **Стоимость** | 1 div = базовая валюта |
| **Clipboard** | [divine_orb.txt](item_clips/divine_orb.txt) |

### Fracturing Orb

| | |
|---|---|
| **Действие** | Фиксирует один случайный аффикс (помечает как Fractured — нельзя изменить орбами) |
| **Применимо к** | Rare, если ≥ 1 аффикс |
| **Ограничения** | Можно использовать только один раз на предмете. После — предмет становится Fractured |
| **В приложении** | `ExaltationCraftServiceFracturedSide` — крафт с фиксированным суффиксом/префиксом |
| **Стоимость** | ~6.76 div (poe.ninja 2026-06-15) |
| **Clipboard** | [fracturing_orb.txt](item_clips/fracturing_orb.txt) |

### Orb of Transmutation

| | |
|---|---|
| **Действие** | Превращает Normal-предмет в Magic с одним случайным аффиксом |
| **Применимо к** | Только Normal |
| **В приложении** | Не используется напрямую в циклах |
| **Clipboard** | [orb_of_transmutation.txt](item_clips/orb_of_transmutation.txt) |

### Orb of Alchemy

| | |
|---|---|
| **Действие** | Превращает Normal-предмет в Rare с 4–6 случайными аффиксами |
| **Применимо к** | Только Normal |
| **В приложении** | Не используется напрямую в циклах |
| **Clipboard** | [orb_of_alchemy.txt](item_clips/orb_of_alchemy.txt) |

### Regal Orb

| | |
|---|---|
| **Действие** | Превращает Magic-предмет в Rare, добавляя один случайный аффикс |
| **Применимо к** | Только Magic |
| **В приложении** | Не используется напрямую в циклах |
| **Clipboard** | [regal_orb.txt](item_clips/regal_orb.txt) |

### Orb of Chance

| | |
|---|---|
| **Действие** | Пытается создать Unique из Normal-предмета (если есть Unique-версия базы) |
| **Применимо к** | Normal |
| **В приложении** | Chancing-режим (`ChancingUseOmen` + `ChancingOmenRect`) |
| **Clipboard** | [orb_of_chance.txt](item_clips/orb_of_chance.txt) |

### Vaal Orb

| | |
|---|---|
| **Действие** | Corrupts предмет — случайный эффект (добавляет мод, изменяет implicit, уничтожает) |
| **Применимо к** | Любой предмет без Corrupted/Sanctified |
| **Ограничения** | Необратимо. После Corrupted большинство орбов неприменимо |
| **В приложении** | Не используется в автоматических циклах |
| **Clipboard** | [vaal_orb.txt](item_clips/vaal_orb.txt) |

### Crystallised Corruption

| | |
|---|---|
| **Действие** | Добавляет на Rare-предмет один Desecrated (скрытый) мод без Corrupting предмета |
| **Применимо к** | Rare |
| **Ограничения** | Требует свободного суффиксного слота (нельзя добавить при 3 суффиксах); добавляет только суффиксы |
| **В приложении** | Упомянут в vault; поддержка Desecrated-модов в `CRAFT-7` |
| **Стоимость** | ~0.74 div (poe.ninja 2026-06-15) |
| **Clipboard** | [crystallised_corruption.txt](item_clips/crystallised_corruption.txt) |

---

## 2. Орбы качества

### Armourer's Scrap

| | |
|---|---|
| **Действие** | Увеличивает качество брони (Quality) — улучшает базовые дефенсивные характеристики |
| **Применимо к** | Броня (Normal/Magic: +5%, Rare/Unique: +1%) |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [armourers_scrap.txt](item_clips/armourers_scrap.txt) |

### Blacksmith's Whetstone

| | |
|---|---|
| **Действие** | Увеличивает качество оружия |
| **Применимо к** | Оружие (Normal/Magic: +5%, Rare/Unique: +1%) |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [blacksmiths_whetstone.txt](item_clips/blacksmiths_whetstone.txt) |

### Glassblower's Bauble

| | |
|---|---|
| **Действие** | Увеличивает качество флакона |
| **Применимо к** | Флаконы |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [glassblowers_bauble.txt](item_clips/glassblowers_bauble.txt) |

### Arcanist's Etcher

| | |
|---|---|
| **Действие** | Увеличивает качество жезлов/посохов/другого магического снаряжения |
| **Применимо к** | Жезлы, посохи (Normal/Magic: +5%, Rare/Unique: +1%) |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [arcanists_etcher.txt](item_clips/arcanists_etcher.txt) |

---

## 3. Орбы сокетов

### Artificer's Orb

| | |
|---|---|
| **Действие** | Добавляет один сокет к предмету |
| **Применимо к** | Оружие, броня с пустым местом для сокетов |
| **Ограничения** | Максимум сокетов зависит от типа предмета |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [artificers_orb.txt](item_clips/artificers_orb.txt) |

### Orb of Extraction

| | |
|---|---|
| **Действие** | Извлекает руну из сокета без её уничтожения |
| **Применимо к** | Предмет с вставленной руной |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [orb_of_extraction.txt](item_clips/orb_of_extraction.txt) |

---

## 4. Омены

Омены — одноразовые предметы, которые активируются перед применением определённого орба и изменяют его поведение.

### Omen of Sinistral Annulment

| | |
|---|---|
| **Действие** | Следующий Orb of Annulment удалит **префикс** (левый аффикс) |
| **Применимо к** | Paired с Orb of Annulment |
| **В приложении** | `OmenActivationService`, `AppSettings.OmenSinistralRect` |
| **Clipboard** | [omen_sinistral_annulment.txt](item_clips/omen_sinistral_annulment.txt) |

### Omen of Dextral Annulment

| | |
|---|---|
| **Действие** | Следующий Orb of Annulment удалит **суффикс** (правый аффикс) |
| **Применимо к** | Paired с Orb of Annulment |
| **В приложении** | `OmenActivationService`, `AppSettings.OmenDextralRect` |
| **Clipboard** | [omen_dextral_annulment.txt](item_clips/omen_dextral_annulment.txt) |

### Omen of Sinistral Exaltation

| | |
|---|---|
| **Действие** | Следующий Exalted Orb добавит аффикс в **префиксный** слот |
| **Применимо к** | Paired с Exalted Orb |
| **В приложении** | `OmenActivationService`, `AppSettings.OmenSinistralRect` |
| **Clipboard** | [omen_sinistral_exaltation.txt](item_clips/omen_sinistral_exaltation.txt) |

### Omen of Dextral Exaltation

| | |
|---|---|
| **Действие** | Следующий Exalted Orb добавит аффикс в **суффиксный** слот |
| **Применимо к** | Paired с Exalted Orb |
| **В приложении** | `OmenActivationService`, `AppSettings.OmenDextralRect` |
| **Clipboard** | [omen_dextral_exaltation.txt](item_clips/omen_dextral_exaltation.txt) |

### Omen of Greater Exaltation

| | |
|---|---|
| **Действие** | Следующий Exalted Orb добавит аффикс **более высокого тира** |
| **Применимо к** | Paired с Exalted Orb |
| **В приложении** | `AppSettings.OmenGreaterRect` |
| **Clipboard** | [omen_greater_exaltation.txt](item_clips/omen_greater_exaltation.txt) |

### Omen of the Ancients

| | |
|---|---|
| **Действие** | Следующий Orb of Chance гарантированно создаёт Unique (и допускает cross-base результат) |
| **Применимо к** | Paired с Orb of Chance |
| **В приложении** | `AppSettings.ChancingOmenRect`, `ChancingUseOmen` |
| **Clipboard** | [omen_of_the_ancients.txt](item_clips/omen_of_the_ancients.txt) |

### Omen of Abyssal Echoes

| | |
|---|---|
| **Действие** | Реролл трёх вариантов Desecrate-раскрытия (появляется 3 новых варианта) |
| **Применимо к** | После применения Preserved Cranium, если варианты не подходят |
| **В приложении** | Десекрейт-логика (vault/mechanics/desecrate_mechanic.md) |
| **Стоимость** | Проверять poe.ninja |
| **Clipboard** | [omen_abyssal_echoes.txt](item_clips/omen_abyssal_echoes.txt) |

### Omen of Light

| | |
|---|---|
| **Действие** | Удаляет уже добавленный Desecrated-мод с предмета |
| **Применимо к** | Предмет с Desecrated-модом |
| **Ограничения** | Не работает на обычных аффиксах |
| **В приложении** | Упомянут в vault/mechanics/desecrate_mechanic.md |
| **Clipboard** | [omen_of_light.txt](item_clips/omen_of_light.txt) |

### Omen of Whittling

| | |
|---|---|
| **Действие** | Следующий Chaos Orb удалит аффикс с **наименьшим ilvl-требованием** и добавит новый (не путать с тиром — это именно ilvl требование мода) |
| **Применимо к** | Paired с Chaos Orb |
| **Особенности** | Hover над предметом после активации показывает, какой мод будет удалён. Unrevealed Desecrated-мод считается ilvl 1 и всегда будет удалён первым |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [omen_of_whittling.txt](item_clips/omen_of_whittling.txt) |

---

## 5. Десекрейт — раскрытие скрытых модов

Механика Desecrate позволяет раскрыть (reveal) один из трёх скрытых Desecrated-модов.
Подробно: [`vault/mechanics/desecrate_mechanic.md`](../vault/mechanics/desecrate_mechanic.md)

### Preserved Cranium

| | |
|---|---|
| **Действие** | Применяется на Rare-предмет → активирует Desecrate: показывает 3 варианта скрытых модов на выбор, один нужно выбрать |
| **Применимо к** | Rare с хотя бы одним свободным слотом (prefix или suffix в зависимости от пула предмета) |
| **Ограничения** | Пул модов зависит от типа предмета; вес модов в пуле равен null (пул целиком в affix_library.json) |
| **В приложении** | `CRAFT-7` (поддержка Desecrated-модов); десекрейт-сессии в vault |
| **Стоимость** | Проверять poe.ninja |
| **Clipboard** | [preserved_cranium.txt](item_clips/preserved_cranium.txt) |

### Десекрейт-пул для Time-Lost Sapphire Jewel

Все десекрейт-моды для TLS — семейство **Lightless** (`familyId: AbyssRadiusJewelMod`), weight=null.
Пул содержит 12 модов: 6 prefix + 6 suffix. Найти в `affix_library.json` фильтром по `affixType` = `"Desecrated Prefix/Suffix Modifier"`.

**6 Desecrated Prefix Modifier:**
| Мод | Стат |
|---|---|
| Lightless | Notable Passive Skills in Radius also grant 1% of Damage is taken from Mana before Life |
| Lightless | Notable Passive Skills in Radius also grant 1% of Damage taken Recouped as Life |
| Lightless | Notable Passive Skills in Radius also grant 1% of Damage taken Recouped as Mana |
| Lightless | Notable Passive Skills in Radius also grant (2–3)% increased Global Armour, Evasion and Energy Shield |
| Lightless | Notable Passive Skills in Radius also grant 1% increased maximum Life |
| Lightless | Notable Passive Skills in Radius also grant **1% increased maximum Mana** ← часто цель крафта |

**6 Desecrated Suffix Modifier:**
| Мод | Стат |
|---|---|
| Lightless | Notable Passive Skills in Radius also grant Charms gain 0.1 charges per Second |
| Lightless | Notable Passive Skills in Radius also grant Life Flasks gain 0.1 charges per Second |
| Lightless | Notable Passive Skills in Radius also grant (2–3)% increased Mana Cost Efficiency |
| Lightless | Notable Passive Skills in Radius also grant Mana Flasks gain 0.1 charges per Second |
| Lightless | Notable Passive Skills in Radius also grant Hits have (3–5)% reduced Critical Hit Chance against you |
| Lightless | Notable Passive Skills in Radius also grant Regenerate (0.03–0.07)% of maximum Life per second |

**Вывод:** Стат "+1% max Mana" для TLS — это всегда Desecrated PREFIX. При его наличии на предмете все 3 натуральных суффикса могут быть от Chaos Orb.

### Abyssal Bone

| | |
|---|---|
| **Действие** | Модифицирует следующее Desecrate-раскрытие: выбранный мод будет более высокого тира |
| **Применимо к** | Используется непосредственно перед Preserved Cranium |
| **Ограничения** | Применяется к десекрейтед-типу модов. Открытый вопрос: точная механика не верифицирована (CRAFT-10) |
| **В приложении** | `vault/mechanics/abyssal_collarbone_tier_theory.md` |
| **Clipboard** | [abyssal_bone.txt](item_clips/abyssal_bone.txt) |

### Ancient Collarbone

| | |
|---|---|
| **Действие** | Модифицирует следующее Desecrate-раскрытие: все три варианта будут ilvl ≥ 40 |
| **Применимо к** | Используется непосредственно перед Preserved Cranium |
| **Ограничения** | Гипотеза о совместном эффекте с Abyssal Bone опровергнута (см. abyssal_collarbone_tier_theory.md) |
| **В приложении** | `vault/mechanics/abyssal_collarbone_tier_theory.md` |
| **Clipboard** | [ancient_collarbone.txt](item_clips/ancient_collarbone.txt) |

---

## 6. Liquid Currency — добавление модов

Liquid Currency — группа предметов, каждый из которых **добавляет один конкретный мод** в случайный свободный слот.
Подробно: [`vault/mechanics/lc_mechanic.md`](../vault/mechanics/lc_mechanic.md)

### Potent Liquid Contempt

| | |
|---|---|
| **Действие** | Добавляет мод типа "+1 к суффиксам" в случайный свободный слот (PREFIX или SUFFIX) |
| **Применимо к** | Rare с хотя бы одним свободным слотом |
| **Вероятность PREFIX** | 50% при 1 свободном prefix + 1 свободном suffix |
| **В приложении** | Пайплайн sapphire_v1_desecrate (шаг добавления VL-мода) |
| **Стоимость** | ~2.15 div (poe.ninja 2026-06-26) |
| **Clipboard** | [potent_liquid_contempt.txt](item_clips/potent_liquid_contempt.txt) |

### Ancient Liquid Contempt

| | |
|---|---|
| **Действие** | Аналог Potent, но добавляет мод с другими параметрами (механика отличается) |
| **Применимо к** | Rare с хотя бы одним свободным слотом |
| **В приложении** | Рассматривается в vault/strategies/sapphire_v2_lc_twophase.md |
| **Clipboard** | [ancient_liquid_contempt.txt](item_clips/ancient_liquid_contempt.txt) |

---

## 7. Катализаторы

Катализаторы добавляют **качество** к аксессуарам (кольца, амулеты, пояса) и **джевелам**, усиливая определённую группу аффиксов.
1 катализатор = 1% quality. Для q20 нужно 20 штук.

| Катализатор | Усиливает | Конкретные статы (примеры) |
|---|---|---|
| Adaptive Catalyst | адаптируется | несколько типов |
| Carapace Catalyst | защитные | Armour, Evasion, ES |
| Chayula's Catalyst | хаос | хаос-сопротивление, хаос-урон |
| Esh's Catalyst | молния | молниевое сопротивление, молниевый урон |
| Flesh Catalyst | физические | физический урон, жизнь (физ.) |
| Necrotic Catalyst | некромантия | minion-моды |
| Neural Catalyst | атрибуты | Strength, Dexterity, Intelligence |
| Reaver Catalyst | атака | Attack Speed, Attack Damage |
| Sibilant Catalyst | холод | холодовое сопротивление, холодный урон |
| Skittering Catalyst | скорость | Move Speed |
| Tul's Catalyst | заморозка | Freeze/Chill моды |
| Uul-Netol's Catalyst | жизнь | Max Life, Life Regen |
| Xoph's Catalyst | огонь | огневое сопротивление, огненный урон |

### Refined Sibilant Catalyst — подтверждённые данные (TLS)

Усиляет **только статы, связанные с заклинаниями**:
- ✓ Critical Hit Chance for Spells (of Annihilating) — например 10% → 12% при q20
- ✓ Critical Spell Damage Bonus (of Unmaking)
- ✗ Critical Hit Chance (of Annihilation) — общий крит, НЕ усиляется
- ✗ Critical Damage Bonus (of Potency) — общий крит урон, НЕ усиляется

Цена (2026-07-02): ~4.66d/шт → q20 ≈ 93d. Проверять актуальную цену в `poe_ninja_prices.json`.

**Refined-версии**: Refined Adaptive Catalyst, Refined Carapace Catalyst, Refined Chayula's Catalyst, Refined Esh's Catalyst, Refined Flesh Catalyst, Refined Necrotic Catalyst, Refined Neural Catalyst, Refined Reaver Catalyst, Refined Sibilant Catalyst, Refined Skittering Catalyst, Refined Tul's Catalyst, Refined Uul-Netol's Catalyst, Refined Xoph's Catalyst.
Refined-катализаторы более эффективны (буcтят только конкретную специализацию мода) и дороже обычных; в реестре имеют тип `RefinedCatalyst`.

**Правило перековки:** При перековке обычных катализаторов на Reforging Bench (3→1) выход — обычный катализатор случайного типа. При перековке Refined-катализаторов выход — тоже Refined-катализатор (отдельный пул).

### Каскадная перековка и авто-порог

Каскадная перековка: после основной сессии рефорджа дешёвые выходы автоматически перековываются снова без затрат золота (клик прямо на станке).

**Авто-порог рентабельности:**

```
E[output] = Σ_Y  p(Y) × price(Y)        # ожидаемая стоимость одного выхода
threshold = E[output] / 3               # порог: 3 входа → 1 выход
```

Если `price(catalyst) ≤ threshold` — перековать выгоднее, чем продать.

При наличии истории рефорджей в логах (`session_*.txt`, строки вида `[N/M] → X Catalyst`) — `p(Y)` берётся из эмпирической частоты. Иначе — равномерное распределение по ценам всех зарегистрированных катализаторов пула.

Обычные и Refined катализаторы имеют **раздельные пороги** (раздельные пулы выходов).

В приложении: `AppSettings.ReforgeSelectedCatalystIds`, `BreachCatalystRegions`, `StackableItemKind.RefinedCatalyst`.

Clipboard-файлы для катализаторов — добавить по мере необходимости в `item_clips/`.

---

## 8. Руны

Руны вставляются в сокеты снаряжения и дают постоянные бонусы (пока вставлены).
Извлекаются через Orb of Extraction без уничтожения.

| Тип руны | Эффект |
|---|---|
| Body Rune | +# к максимальному Life |
| Storm Rune | +# к молниевому сопротивлению |
| Iron Rune | +# к Physical Damage Reduction |
| Charging Rune | +# к Mana |
| Desert Rune | +# к огневому сопротивлению |
| Glacial Rune | +# к ледяному сопротивлению |
| Mind Rune | +# к ES |
| Resolve Rune | +# к максимальному Life (другой вариант) |
| Robust Rune | +# к Armour |
| Stone Rune | +# к Armour (другой вариант) |
| Vision Rune | +# к точности |
| Ward Rune | +# к Ward |
| Inspiration Rune | +# к урону заклинаниями |
| Rebirth Rune | +# к Life Regeneration |
| Adept Rune | +# к урону |

У каждого типа три уровня: Lesser / (базовый) / Greater / Perfect.

В приложении: `AppSettings.SocketableSubTabRunesRect`.

---

## 9. Soul Cores

Soul Cores вставляются в сокеты и дают уникальные бонусы, специфичные для каждого Core.
Именованы по именам монстров (Atmohua, Cholotl, Citaqualotl и т.д.).

В приложении: `AppSettings.SocketableSubTabSoulCoresRect`.

Clipboard-файлы для Soul Cores — добавить по мере необходимости в `item_clips/`.

---

## 10. Прочее

### Gemcutter's Prism

| | |
|---|---|
| **Действие** | Добавляет качество к самоцвету (gem) |
| **Применимо к** | Gems |
| **В приложении** | Не автоматизировано |
| **Clipboard** | [gemcutters_prism.txt](item_clips/gemcutters_prism.txt) |

---

## Связанные документы

- [`docs/GAME_MECHANICS.md`](GAME_MECHANICS.md) — общие механики PoE2
- [`docs/CRAFTING_STRATEGIES.md`](CRAFTING_STRATEGIES.md) — стратегии крафта
- [`vault/mechanics/`](../vault/mechanics/) — детальный анализ конкретных механик
- [`poe_ninja_prices.json`](../poe_ninja_prices.json) — актуальные цены
