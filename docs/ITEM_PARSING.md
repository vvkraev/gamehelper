# Руководство по item parsing
## Формат буфера

разделитель структуры --------

Формат буфера состоит из следующих компонентов:
1. **1**: Item Class;Rarity;Художественное название предмета(необязательное);База предмета
2. **2**: Quality(необязательное);Характеристики предмета которые зависят от Item Class
3. **3**: Requires требования к персонажу - уровень, характеристики
4. **4**: Sockets(необязательное) Дырки в предмете, одна дырка S, две дырки S S и т.д.
5. **5**: Item Level от этого параметра зависит какие affix tier доступны для предмета
6. **6**: Какие Augmented item вставлены в предмет, каждая строка что делает Augmented item
7. **7**: Affixes предмета { Тип аффикса, может быть Prefix Modifier или Suffix Modifier; "Название аффикса в кавычках"; (Tier: числовое значение) — Тег аффикса }
8. **8**: Состояниие предмета(необязательное) может быть Corrupted или Sanctified что значит, что предмет неизменяемый

### Примеры буфера

#### Надеваемые предметы

Item Class: Boots
Rarity: Normal
Daggerfoot Shoes
--------
Evasion Rating: 119
Energy Shield: 45
--------
Requires: Level 80, 59 Dex, 59 Int
--------
Item Level: 82


Item Class: Body Armours
Rarity: Magic
Wolfskin Mantle
--------
Armour: 294
Energy Shield: 101
--------
Requires: Level 65, 67 Str, 67 Int
--------
Item Level: 80


Item Class: Body Armours
Rarity: Magic
Pope's Vile Robe
--------
Energy Shield: 261 (augmented)
--------
Requires: Level 65, 121 Int
--------
Item Level: 79
--------
{ Prefix Modifier "Pope's" (Tier: 1) — Life, Defences }
42(39-42)% increased Energy Shield
+42(42-49) to maximum Life

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 406 (augmented)
Energy Shield: 140 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Item Level: 80
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 406 (augmented)
Energy Shield: 140 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Item Level: 80
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 723 (augmented)
Energy Shield: 249 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Item Level: 80
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 723 (augmented)
Energy Shield: 249 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Sockets: S 
--------
Item Level: 80
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 723 (augmented)
Energy Shield: 249 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Sockets: S 
--------
Item Level: 80
--------
+14% to Lightning Resistance (rune)
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 723 (augmented)
Energy Shield: 249 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Sockets: S 
--------
Item Level: 80
--------
+11% to Chaos Resistance (rune)
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Armour: 723 (augmented)
Energy Shield: 249 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Sockets: S 
--------
Item Level: 80
--------
30% faster start of Energy Shield Recharge (rune)
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Quality: +1% (augmented)
Armour: 730 (augmented)
Energy Shield: 251 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Sockets: S 
--------
Item Level: 80
--------
30% faster start of Energy Shield Recharge (rune)
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance

Item Class: Body Armours
Rarity: Rare
Behemoth Shroud
Wolfskin Mantle
--------
Quality: +1% (augmented)
Armour: 730 (augmented)
Energy Shield: 251 (augmented)
--------
Requires: Level 65, 67 Str, 67 Int
--------
Sockets: S S 
--------
Item Level: 80
--------
+14% to Lightning Resistance (rune)
30% faster start of Energy Shield Recharge (rune)
--------
{ Prefix Modifier "Crusader's" (Tier: 3) — Defences }
+26(21-27) to Armour
+9(9-10) to maximum Energy Shield
27(27-32)% increased Armour and Energy Shield
{ Prefix Modifier "Rapturous" (Tier: 2) — Life }
+198(190-199) to maximum Life
{ Prefix Modifier "Inspired" (Tier: 2) — Defences }
99(92-100)% increased Armour and Energy Shield
{ Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
+32(31-35)% to Cold Resistance
{ Suffix Modifier "of the Troll" (Tier: 6) — Life }
9.7(9.1-13) Life Regeneration per second
{ Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
+24(21-25)% to Fire Resistance
--------
Corrupted


## Алгоритм синтаксического анализа
Алгоритм синтаксического анализа состоит из следующих этапов:
разработать описание этапов алгоритма

## Примеры
### Пример 1: базовый элемент


### Пример 2: Сложный элемент

## Особые случаи
1. **Пустые буферы**: корректно обрабатываются без ошибок, возвращается элемент null. 
2. **Некорректно сформированные буферы**: регистрируются ошибки, обработка некорректных входных данных пропускается. 

### Резюме
В этом руководстве представлен подробный обзор синтаксического анализа элементов, включая формат буфера, алгоритмы синтаксического анализа, примеры, особые случаи и практические фрагменты кода на C#. При необходимости адаптируйте синтаксический анализ под конкретные задачи.
