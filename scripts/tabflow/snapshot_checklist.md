# Чеклист снятия снапшотов — TabFlow

## Как работать с этим файлом

1. GameHelper → вкладка **Наблюдение** → **▶ Слушать**
2. Для каждой строки:
   - Создать сессию с именем из колонки **Имя сессии** (поле «+ Создать сессию»)
   - Выбрать эту сессию в списке
   - Открыть `trade.pathofexile.com` → выставить фильтры из колонки **Фильтры на трейде**
   - Прокрутить страницу результатов до конца
   - Нажать **Снимок** в GameHelper
   - Поставить `[x]` в этом файле
3. После сессии — запустить **Проверку** (команда внизу)

> **Имя сессии** = slug в имени файла `trade_data/YYYY-MM-DD_HH-MM-SS_{имя}.json`  
> Имена придуманы так чтобы тип и фильтры были видны прямо из имени файла.

---

## Справка: названия модов

P01–P12 и S01–S10 одинаковы для всех типов планшеток.

| Код | Мод |
|-----|-----|
| P01 | Monsters have % increased Effectiveness |
| P02 | % increased Rarity of Items found in Map |
| P03 | % increased Pack Size in Map |
| P04 | Map has % increased Magic Monsters |
| P05 | Map has % increased number of Rare Monsters |
| P06 | Map has % increased Monster Rarity |
| P07 | % increased Gold found in Map |
| P08 | % increased Experience gain in Map |
| P09 | Map contains # additional Rare Chests |
| P10 | Map contains # additional Essences |
| P11 | % increased Quantity of Waystones found in Map |
| P12 | Map contains # additional Summoning Circles |
| S01 | % increased Quantity of Waystones found in Map |
| S02 | Map has % increased chance to contain Shrines |
| S03 | % reduced Pack Size in Map *(штраф)* |
| S04 | Map has % increased chance to contain Strongboxes |
| S05 | Map has % increased chance to contain Essences |
| S06 | Map has % increased chance to contain Azmeri Spirits |
| S07 | Map has % increased chance to contain Rogue Exiles |
| S08 | Map has % increased chance to contain a Summoning Circle |
| S09 | Map has # additional random Modifiers |
| S10 | Unique Monsters have # additional Rare Modifiers |

---

## Ritual Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | Monsters Sacrificed grant % increased Tribute |
| S12 | Rerolling Favours costs % reduced Tribute |
| S13 | Deferring Favours costs % reduced Tribute |
| S14 | Deferred Favours reappear % sooner |
| S15 | Altars allow rerolling Favours # additional times |
| S16 | Rerolled Favours have % chance to cost no Tribute |
| S17 | Revived Monsters have % increased chance to be Magic |
| S18 | Revived Monsters have % increased chance to be Rare |
| S19 | Ritual Favours have % increased chance to be Omens |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `rit_s19` | S19 |
| - [ ] | `rit_s19_s11` | S19 + S11 |
| - [ ] | `rit_s19_s15` | S19 + S15 |
| - [ ] | `rit_s19_p02` | S19 + P02 |
| - [ ] | `rit_s19_p05` | S19 + P05 |
| - [ ] | `rit_s11_s12` | S11 + S12 |
| - [ ] | `rit_s11_s13` | S11 + S13 |
| - [ ] | `rit_s15_s16` | S15 + S16 |
| - [ ] | `rit_p02_p05_s11` | P02 + P05 + S11 |
| - [ ] | `rit_p05_p06_s11` | P05 + P06 + S11 |
| - [ ] | `rit_p02_p06_s19` | P02 + P06 + S19 |
| - [ ] | `rit_floor` | *(без фильтров — только Ritual Tablet)* |

**Цель: ≥ 150 предметов с rich-модами**

---

## Abyss Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | Abysses spawn % increased Monsters |
| S12 | # additional Rare Monsters spawned from Abysses |
| S13 | Abyssal Monsters have % increased Effectiveness |
| S14 | Abysses have % increased chance to lead to Abyssal Depths |
| S15 | Map contains # additional Abysses |
| S16 | Abyss Pits are twice as likely to have Rewards |
| S17 | % chance to contain four additional Abysses |
| S18 | % increased chance for Abyssal Monsters to have Abyssal Gear |
| S19 | % increased chance for Desecrated Currency from Abysses |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `aby_s19` | S19 |
| - [ ] | `aby_s14` | S14 |
| - [ ] | `aby_s19_s14` | S19 + S14 |
| - [ ] | `aby_s19_s15` | S19 + S15 |
| - [ ] | `aby_s14_s15` | S14 + S15 |
| - [ ] | `aby_s16_s19` | S16 + S19 |
| - [ ] | `aby_p02_s19` | P02 + S19 |
| - [ ] | `aby_p05_s14` | P05 + S14 |
| - [ ] | `aby_p02_p05_s19` | P02 + P05 + S19 |
| - [ ] | `aby_p05_p06_s14` | P05 + P06 + S14 |
| - [ ] | `aby_p02_p06_s19` | P02 + P06 + S19 |
| - [ ] | `aby_floor` | *(без фильтров — только Abyss Tablet)* |

**Цель: ≥ 150 предметов с rich-модами**

---

## Breach Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | % increased Quantity of Hiveblood found |
| S12 | % increased Quantity of Wombgifts found |
| S13 | Wombgifts have % chance to drop one Level higher |
| S14 | Unstable Breaches have % increased chance to contain Vrana |
| S15 | Unstable Breaches spawn additional Rare Monsters when Closed |
| S16 | % increased Effectiveness of Rare Breach Monsters |
| S17 | Breaches have % increased Pack Size |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `bre_s11` | S11 |
| - [ ] | `bre_s12` | S12 |
| - [ ] | `bre_s14` | S14 |
| - [ ] | `bre_s11_s12` | S11 + S12 |
| - [ ] | `bre_s11_s14` | S11 + S14 |
| - [ ] | `bre_s15_s17` | S15 + S17 |
| - [ ] | `bre_p02_s11` | P02 + S11 |
| - [ ] | `bre_p05_s14` | P05 + S14 |
| - [ ] | `bre_p02_p05_s11` | P02 + P05 + S11 |
| - [ ] | `bre_p05_p06_s12` | P05 + P06 + S12 |
| - [ ] | `bre_p02_p06_s14` | P02 + P06 + S14 |
| - [ ] | `bre_floor` | *(без фильтров — только Breach Tablet)* |

**Цель: ≥ 120 предметов с rich-модами**

---

## Expedition Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | % increased Expedition Artifacts dropped |
| S12 | % increased Explosive Placement Range |
| S13 | Expeditions have +# Remnants |
| S14 | % increased Explosive Radius |
| S15 | % increased Quantity of Logbooks dropped |
| S16 | % increased number of Rare Expedition Monsters |
| S17 | % increased Effect of Expedition Remnants |
| S18 | % increased number of Runic Monster Markers |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `exp_s13` | S13 |
| - [ ] | `exp_s15` | S15 |
| - [ ] | `exp_s13_s15` | S13 + S15 |
| - [ ] | `exp_s13_s17` | S13 + S17 |
| - [ ] | `exp_s11_s13` | S11 + S13 |
| - [ ] | `exp_s15_s18` | S15 + S18 |
| - [ ] | `exp_p02_s13` | P02 + S13 |
| - [ ] | `exp_p05_s15` | P05 + S15 |
| - [ ] | `exp_p02_p05_s13` | P02 + P05 + S13 |
| - [ ] | `exp_p05_p06_s15` | P05 + P06 + S15 |
| - [ ] | `exp_p02_p06_s13` | P02 + P06 + S13 |
| - [ ] | `exp_floor` | *(без фильтров — только Expedition Tablet)* |

**Цель: ≥ 120 предметов с rich-модами**

---

## Delirium Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | % increased Stack size of Simulacrum Splinters |
| S12 | Delirium Fog lasts # additional seconds |
| S13 | Delirium Fog dissipates % slower |
| S14 | Delirium Fog applies % increased Deliriousness |
| S15 | Delirium Monsters have % increased Pack Size |
| S16 | Fog spawns % increased Fracturing Mirrors |
| S17 | Slaying Rare Monsters pauses the Mirror Timer |
| S18 | more likely to spawn Unique Bosses |
| S19 | Fog spawns % increased MirrorShards |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `del_s11` | S11 |
| - [ ] | `del_s19` | S19 |
| - [ ] | `del_s11_s19` | S11 + S19 |
| - [ ] | `del_s11_s14` | S11 + S14 |
| - [ ] | `del_s13_s16` | S13 + S16 |
| - [ ] | `del_s17_s19` | S17 + S19 |
| - [ ] | `del_p02_s11` | P02 + S11 |
| - [ ] | `del_p05_s19` | P05 + S19 |
| - [ ] | `del_p02_p05_s11` | P02 + P05 + S11 |
| - [ ] | `del_p05_p06_s19` | P05 + P06 + S19 |
| - [ ] | `del_p02_p06_s11` | P02 + P06 + S11 |
| - [ ] | `del_floor` | *(без фильтров — только Delirium Tablet)* |

**Цель: ≥ 120 предметов с rich-модами**

---

## Overseer Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | Map contains # additional Strongboxes |
| S12 | Map contains # additional Shrines |
| S13 | Map contains # additional Azmeri Spirits |
| S14 | % increased Quantity of Waystones dropped by Map Bosses |
| S15 | Map Bosses grant % increased Experience |
| S16 | % increased Rarity of Items dropped by Map Bosses |
| S17 | % increased Quantity of Items dropped by Map Bosses |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `ovs_s11` | S11 |
| - [ ] | `ovs_s12` | S12 |
| - [ ] | `ovs_s14` | S14 |
| - [ ] | `ovs_s11_s12` | S11 + S12 |
| - [ ] | `ovs_s14_s16` | S14 + S16 |
| - [ ] | `ovs_s15_s17` | S15 + S17 |
| - [ ] | `ovs_p02_s14` | P02 + S14 |
| - [ ] | `ovs_p05_s11` | P05 + S11 |
| - [ ] | `ovs_p02_p05_s11` | P02 + P05 + S11 |
| - [ ] | `ovs_p05_p06_s14` | P05 + P06 + S14 |
| - [ ] | `ovs_p02_p06_s12` | P02 + P06 + S12 |
| - [ ] | `ovs_floor` | *(без фильтров — только Overseer Tablet)* |

**Цель: ≥ 120 предметов с rich-модами**

---

## Temple Tablet

Уникальные суффиксы:

| Код | Мод |
|-----|-----|
| S11 | % increased Pack Size for Monsters around Vaal Beacons |
| S12 | # extra packs around Vaal Beacons |
| S13 | % chance for extra pack around Vaal Beacons |
| S14 | % increased chance Vaal Beacons summon additional Monsters |
| S15 | % chance to gain additional Crystal from Vaal Beacons |
| S16 | % chance to add a Vaal Beacon Unique Monster |
| S17 | % increased chance Vaal Beacon Chests are Rare |

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `tem_s15` | S15 |
| - [ ] | `tem_s16` | S16 |
| - [ ] | `tem_s15_s16` | S15 + S16 |
| - [ ] | `tem_s12_s14` | S12 + S14 |
| - [ ] | `tem_s16_s17` | S16 + S17 |
| - [ ] | `tem_s11_s16` | S11 + S16 |
| - [ ] | `tem_p02_s16` | P02 + S16 |
| - [ ] | `tem_p05_s15` | P05 + S15 |
| - [ ] | `tem_p02_p05_s16` | P02 + P05 + S16 |
| - [ ] | `tem_p05_p06_s15` | P05 + P06 + S15 |
| - [ ] | `tem_p02_p06_s16` | P02 + P06 + S16 |
| - [ ] | `tem_floor` | *(без фильтров — только Temple Tablet)* |

**Цель: ≥ 120 предметов с rich-модами**

---

## Irradiated Tablet

> Нет уникальных суффиксов — ценность только в комбинации общих префиксов.

| Готово | Имя сессии | Фильтры на трейде |
|--------|-----------|-------------------|
| - [ ] | `irr_p02_p05` | P02 + P05 |
| - [ ] | `irr_p02_p06` | P02 + P06 |
| - [ ] | `irr_p05_p06` | P05 + P06 |
| - [ ] | `irr_p02_p03` | P02 + P03 |
| - [ ] | `irr_p03_p05` | P03 + P05 |
| - [ ] | `irr_p01_p02` | P01 + P02 |
| - [ ] | `irr_p01_p05` | P01 + P05 |
| - [ ] | `irr_p02_p05_s10` | P02 + P05 + S10 |
| - [ ] | `irr_p02_p06_s09` | P02 + P06 + S09 |
| - [ ] | `irr_p05_p06_s01` | P05 + P06 + S01 |
| - [ ] | `irr_floor` | *(без фильтров — только Irradiated Tablet)* |

**Цель: ≥ 100 предметов с rich-модами**

---

## Проверка прогресса

WSL → `cd /mnt/c/Users/VVK/GameHelper/scripts/tabflow`

```bash
python3 -c "
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
targets = {'Ritual Tablet':150,'Abyss Tablet':150,'Breach Tablet':120,
           'Expedition Tablet':120,'Delirium Tablet':120,'Overseer Tablet':120,
           'Temple Tablet':120,'Irradiated Tablet':100}
for bt, target in targets.items():
    n = counts.get(bt, 0)
    bar = '█' * (n * 20 // max(target,1))
    status = '✓ готово' if n >= target else f'ещё {target - n}'
    print(f'{bt:20s}  {n:4d}/{target}  {bar:<20}  {status}')
"
```

---

## Запуск обучения (после выполнения всех целей)

WSL → `cd /mnt/c/Users/VVK/GameHelper/scripts/tabflow`

```bash
python3 train_evaluator.py
```
