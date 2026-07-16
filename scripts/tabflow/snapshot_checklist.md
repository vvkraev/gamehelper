# Чеклист снятия снапшотов — TabFlow

## Как работать

1. GameHelper → вкладка **Наблюдение** → **▶ Слушать**
2. Для каждой строки:
   - В поле «+ Создать сессию» введи имя из колонки **Сессия**
   - Выставь на трейд-сайте все указанные фильтры (пустая ячейка = без фильтра)
   - Долистай результаты до конца → **Снимок** → `[x]`
3. Строка `floor` — только тип таблички, без модовых фильтров

> **Обязательный фильтр для каждого снапшота:** `Uses Remaining = 10`

**Если результатов 0:** убирай фильтры по одному (сначала префиксы, потом один суффикс) пока не появятся листинги. Пропускай снап только если 0 результатов даже с одним суффиксом.

---

## Ritual Tablet
*(17 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `rit_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | Ritual Favours have % increased chance to be Omens | Revived Monsters have % increased chance to be Rare |
| - [ ] | `rit_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | Revived Monsters have % increased chance to be Rare | Revived Monsters have % increased chance to be Magic |
| - [ ] | `rit_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | Revived Monsters have % increased chance to be Magic | Rerolled Favours have % chance to cost no Tribute |
| - [ ] | `rit_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | Rerolled Favours have % chance to cost no Tribute | Altars allow rerolling Favours # additional times |
| - [ ] | `rit_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | Altars allow rerolling Favours # additional times | Deferred Favours reappear % sooner |
| - [ ] | `rit_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | Deferred Favours reappear % sooner | Deferring Favours costs % reduced Tribute |
| - [ ] | `rit_d07` | % increased Gold found in Map | % increased Experience gain in Map | Deferring Favours costs % reduced Tribute | Rerolling Favours costs % reduced Tribute |
| - [ ] | `rit_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | Rerolling Favours costs % reduced Tribute | Monsters Sacrificed grant % increased Tribute |
| - [ ] | `rit_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | Monsters Sacrificed grant % increased Tribute | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `rit_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `rit_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `rit_d12` | | | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `rit_d13` | | | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `rit_d14` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `rit_d15` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `rit_d16` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `rit_d17` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `rit_floor` | | | | |

---

## Abyss Tablet
*(17 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `aby_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | % increased chance for Desecrated Currency from Abysses | % increased chance for Abyssal Monsters to have Abyssal Gear |
| - [ ] | `aby_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | % increased chance for Abyssal Monsters to have Abyssal Gear | % chance to contain four additional Abysses |
| - [ ] | `aby_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | % chance to contain four additional Abysses | Abyss Pits are twice as likely to have Rewards |
| - [ ] | `aby_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | Abyss Pits are twice as likely to have Rewards | Map contains # additional Abysses |
| - [ ] | `aby_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | Map contains # additional Abysses | Abysses have % increased chance to lead to Abyssal Depths |
| - [ ] | `aby_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | Abysses have % increased chance to lead to Abyssal Depths | Abyssal Monsters have % increased Effectiveness |
| - [ ] | `aby_d07` | % increased Gold found in Map | % increased Experience gain in Map | Abyssal Monsters have % increased Effectiveness | # additional Rare Monsters spawned from Abysses |
| - [ ] | `aby_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | # additional Rare Monsters spawned from Abysses | Abysses spawn % increased Monsters |
| - [ ] | `aby_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | Abysses spawn % increased Monsters | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `aby_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `aby_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `aby_d12` | | | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `aby_d13` | | | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `aby_d14` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `aby_d15` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `aby_d16` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `aby_d17` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `aby_floor` | | | | |

---

## Breach Tablet
*(15 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `bre_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | Breaches have % increased Pack Size | % increased Effectiveness of Rare Breach Monsters |
| - [ ] | `bre_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | % increased Effectiveness of Rare Breach Monsters | Unstable Breaches spawn additional Rare Monsters when Closed |
| - [ ] | `bre_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | Unstable Breaches spawn additional Rare Monsters when Closed | Unstable Breaches have % increased chance to contain Vrana |
| - [ ] | `bre_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | Unstable Breaches have % increased chance to contain Vrana | Wombgifts have % chance to drop one Level higher |
| - [ ] | `bre_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | Wombgifts have % chance to drop one Level higher | % increased Quantity of Wombgifts found |
| - [ ] | `bre_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | % increased Quantity of Wombgifts found | % increased Quantity of Hiveblood found |
| - [ ] | `bre_d07` | % increased Gold found in Map | % increased Experience gain in Map | % increased Quantity of Hiveblood found | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `bre_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `bre_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `bre_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `bre_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `bre_d12` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `bre_d13` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `bre_d14` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `bre_d15` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `bre_floor` | | | | |

---

## Expedition Tablet
*(16 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `exp_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | % increased number of Runic Monster Markers | % increased Effect of Expedition Remnants |
| - [ ] | `exp_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | % increased Effect of Expedition Remnants | % increased number of Rare Expedition Monsters |
| - [ ] | `exp_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | % increased number of Rare Expedition Monsters | % increased Quantity of Logbooks dropped |
| - [ ] | `exp_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | % increased Quantity of Logbooks dropped | % increased Explosive Radius |
| - [ ] | `exp_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | % increased Explosive Radius | Expeditions have +# Remnants |
| - [ ] | `exp_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | Expeditions have +# Remnants | % increased Explosive Placement Range |
| - [ ] | `exp_d07` | % increased Gold found in Map | % increased Experience gain in Map | % increased Explosive Placement Range | % increased Expedition Artifacts dropped |
| - [ ] | `exp_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | % increased Expedition Artifacts dropped | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `exp_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `exp_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `exp_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `exp_d12` | | | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `exp_d13` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `exp_d14` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `exp_d15` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `exp_d16` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `exp_floor` | | | | |

---

## Delirium Tablet
*(17 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `del_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | Fog spawns % increased MirrorShards | more likely to spawn Unique Bosses |
| - [ ] | `del_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | more likely to spawn Unique Bosses | Slaying Rare Monsters pauses the Mirror Timer |
| - [ ] | `del_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | Slaying Rare Monsters pauses the Mirror Timer | Fog spawns % increased Fracturing Mirrors |
| - [ ] | `del_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | Fog spawns % increased Fracturing Mirrors | Delirium Monsters have % increased Pack Size |
| - [ ] | `del_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | Delirium Monsters have % increased Pack Size | Delirium Fog applies % increased Deliriousness |
| - [ ] | `del_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | Delirium Fog applies % increased Deliriousness | Delirium Fog dissipates % slower |
| - [ ] | `del_d07` | % increased Gold found in Map | % increased Experience gain in Map | Delirium Fog dissipates % slower | Delirium Fog lasts # additional seconds |
| - [ ] | `del_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | Delirium Fog lasts # additional seconds | % increased Stack size of Simulacrum Splinters |
| - [ ] | `del_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | % increased Stack size of Simulacrum Splinters | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `del_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `del_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `del_d12` | | | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `del_d13` | | | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `del_d14` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `del_d15` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `del_d16` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `del_d17` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `del_floor` | | | | |

---

## Overseer Tablet
*(15 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `ovs_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | % increased Quantity of Items dropped by Map Bosses | % increased Rarity of Items dropped by Map Bosses |
| - [ ] | `ovs_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | % increased Rarity of Items dropped by Map Bosses | Map Bosses grant % increased Experience |
| - [ ] | `ovs_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | Map Bosses grant % increased Experience | % increased Quantity of Waystones dropped by Map Bosses |
| - [ ] | `ovs_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | % increased Quantity of Waystones dropped by Map Bosses | Map contains # additional Azmeri Spirits |
| - [ ] | `ovs_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | Map contains # additional Azmeri Spirits | Map contains # additional Shrines |
| - [ ] | `ovs_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | Map contains # additional Shrines | Map contains # additional Strongboxes |
| - [ ] | `ovs_d07` | % increased Gold found in Map | % increased Experience gain in Map | Map contains # additional Strongboxes | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `ovs_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `ovs_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `ovs_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `ovs_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `ovs_d12` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `ovs_d13` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `ovs_d14` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `ovs_d15` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `ovs_floor` | | | | |

---

## Temple Tablet
*(15 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `tem_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | % increased chance Vaal Beacon Chests are Rare | % chance to add a Vaal Beacon Unique Monster |
| - [ ] | `tem_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | % chance to add a Vaal Beacon Unique Monster | % chance to gain additional Crystal from Vaal Beacons |
| - [ ] | `tem_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | % chance to gain additional Crystal from Vaal Beacons | % increased chance Vaal Beacons summon additional Monsters |
| - [ ] | `tem_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | % increased chance Vaal Beacons summon additional Monsters | % chance for extra pack around Vaal Beacons |
| - [ ] | `tem_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | % chance for extra pack around Vaal Beacons | # extra packs around Vaal Beacons |
| - [ ] | `tem_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | # extra packs around Vaal Beacons | % increased Pack Size for Monsters around Vaal Beacons |
| - [ ] | `tem_d07` | % increased Gold found in Map | % increased Experience gain in Map | % increased Pack Size for Monsters around Vaal Beacons | Unique Monsters have # additional Rare Modifiers |
| - [ ] | `tem_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `tem_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `tem_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `tem_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `tem_d12` | | | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `tem_d13` | | | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `tem_d14` | | | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `tem_d15` | | | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `tem_floor` | | | | |

---

## Irradiated Tablet
*(11 шагов + floor)*

| ✓ | Сессия | Префикс 1 | Префикс 2 | Суффикс 1 | Суффикс 2 |
|---|--------|-----------|-----------|-----------|-----------|
| - [ ] | `irr_d01` | Monsters have % increased Effectiveness | % increased Rarity of Items found in Map | Unique Monsters have # additional Rare Modifiers | Map has # additional random Modifiers |
| - [ ] | `irr_d02` | % increased Rarity of Items found in Map | % increased Pack Size in Map | Map has # additional random Modifiers | Map has % increased chance to contain a Summoning Circle |
| - [ ] | `irr_d03` | % increased Pack Size in Map | Map has % increased Magic Monsters | Map has % increased chance to contain a Summoning Circle | Map has % increased chance to contain Rogue Exiles |
| - [ ] | `irr_d04` | Map has % increased Magic Monsters | Map has % increased number of Rare Monsters | Map has % increased chance to contain Rogue Exiles | Map has % increased chance to contain Azmeri Spirits |
| - [ ] | `irr_d05` | Map has % increased number of Rare Monsters | Map has % increased Monster Rarity | Map has % increased chance to contain Azmeri Spirits | Map has % increased chance to contain Essences |
| - [ ] | `irr_d06` | Map has % increased Monster Rarity | % increased Gold found in Map | Map has % increased chance to contain Essences | Map has % increased chance to contain Strongboxes |
| - [ ] | `irr_d07` | % increased Gold found in Map | % increased Experience gain in Map | Map has % increased chance to contain Strongboxes | Map has % increased chance to contain Shrines |
| - [ ] | `irr_d08` | % increased Experience gain in Map | Map contains # additional Rare Chests | Map has % increased chance to contain Shrines | % increased Quantity of Waystones found in Map |
| - [ ] | `irr_d09` | Map contains # additional Rare Chests | Map contains # additional Essences | | |
| - [ ] | `irr_d10` | Map contains # additional Essences | % increased Quantity of Waystones found in Map | | |
| - [ ] | `irr_d11` | % increased Quantity of Waystones found in Map | Map contains # additional Summoning Circles | | |
| - [ ] | `irr_floor` | | | | |

---

## Проверка прогресса

WSL → `cd /mnt/c/Users/VVK/GameHelper/scripts/tabflow`

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
    status = '✓' if n >= 30 else f'ещё {30-n}'
    print(f'  {n:4d}  {bt}  {status}')
"
```

## Запуск обучения

```bash
.venv/bin/python3 train_evaluator.py
```
