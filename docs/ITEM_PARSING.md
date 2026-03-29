# Руководство по item parsing
## 1. Формат буфера
Буфер представляет собой текстовое описание игрового предмета, разделённое строками -------- (десять дефисов).
Структура буфера состоит из последовательных блоков:

### Блок 1 — класс, редкость, художественное название (необязательно), база.
Формат:
Item Class: <класс>
Rarity: <редкость>
<художественное название>
<база предмета>

### Блок 2 — характеристики, зависящие от класса (броня, урон и т.п.). Может включать Quality: ... (необязательно).

### Блок 3 — требования: Requires: Level <уровень>, <характеристики>...

### Блок 4 — сокеты (необязательно): Sockets: S, S S и т.д.

### Блок 5 — уровень предмета: Item Level: <число>

### Блок 6 — вставленные предметы (rune, soul core) — каждая строка после уровня или сокетов до аффиксов.

### Блок 7 — аффиксы:
{ <Тип> "<Название>" (Tier: <число>) — <Теги> }
следующая строка — эффект.

### Блок 8 — состояние (необязательно): Corrupted или Sanctified в конце.

### 2. Примеры буфера (сгруппированы по типам)

#### 2.1. Нормальный (Normal) предмет

##### Пример 1:
###### Item Class: Boots
###### Rarity: Normal
###### Daggerfoot Shoes
###### --------
###### Evasion Rating: 119
###### Energy Shield: 45
###### --------
###### Requires: Level 80, 59 Dex, 59 Int
###### --------
###### Item Level: 82

#### 2.2. Магические (Magic) предметы
##### Пример 1: Body Armour без художественного названия

###### Item Class: Body Armours
###### Rarity: Magic
###### Wolfskin Mantle
###### --------
###### Armour: 294
###### Energy Shield: 101
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Item Level: 80

##### Пример 2: Body Armour с художественным названием и пометкой (augmented)
###### Item Class: Body Armours
###### Rarity: Magic
###### Pope's Vile Robe
###### --------
###### Energy Shield: 261 (augmented)
###### --------
###### Requires: Level 65, 121 Int
###### --------
###### Item Level: 79
###### --------
###### { Prefix Modifier "Pope's" (Tier: 1) — Life, Defences }
###### 42(39-42)% increased Energy Shield
###### +42(42-49) to maximum Life

##### 2.3. Редкие (Rare) предметы
###### 2.3.1. Без сокетов, без рун, без качества
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Armour: 406 (augmented)
###### Energy Shield: 140 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Item Level: 80
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield

##### 2.3.2. С двумя аффиксами
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Armour: 406 (augmented)
###### Energy Shield: 140 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Item Level: 80
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield
###### { Prefix Modifier "Rapturous" (Tier: 2) — Life }
###### +198(190-199) to maximum Life

##### 2.3.3. С шестью аффиксами
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Armour: 723 (augmented)
###### Energy Shield: 249 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Item Level: 80
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield
###### { Prefix Modifier "Rapturous" (Tier: 2) — Life }
###### +198(190-199) to maximum Life
###### { Prefix Modifier "Inspired" (Tier: 2) — Defences }
###### 99(92-100)% increased Armour and Energy Shield
###### { Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
###### +32(31-35)% to Cold Resistance
###### { Suffix Modifier "of the Troll" (Tier: 6) — Life }
###### 9.7(9.1-13) Life Regeneration per second
###### { Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
###### +24(21-25)% to Fire Resistance

##### 2.3.4. С сокетами (один сокет)
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Armour: 723 (augmented)
###### Energy Shield: 249 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Sockets: S 
###### --------
###### Item Level: 80
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield
###### { Prefix Modifier "Rapturous" (Tier: 2) — Life }
###### +198(190-199) to maximum Life
###### { Prefix Modifier "Inspired" (Tier: 2) — Defences }
###### 99(92-100)% increased Armour and Energy Shield
###### { Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
###### +32(31-35)% to Cold Resistance
###### { Suffix Modifier "of the Troll" (Tier: 6) — Life }
###### 9.7(9.1-13) Life Regeneration per second
###### { Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
###### +24(21-25)% to Fire Resistance

##### 2.3.5. С рунами (после Item Level или Sockets)
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Armour: 723 (augmented)
###### Energy Shield: 249 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Sockets: S 
###### --------
###### Item Level: 80
###### --------
###### +14% to Lightning Resistance (rune)
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield
###### { Prefix Modifier "Rapturous" (Tier: 2) — Life }
###### +198(190-199) to maximum Life
###### { Prefix Modifier "Inspired" (Tier: 2) — Defences }
###### 99(92-100)% increased Armour and Energy Shield
###### { Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
###### +32(31-35)% to Cold Resistance
###### { Suffix Modifier "of the Troll" (Tier: 6) — Life }
###### 9.7(9.1-13) Life Regeneration per second
###### { Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
###### +24(21-25)% to Fire Resistance

##### 2.3.6. С качеством (Quality)
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Quality: +1% (augmented)
###### Armour: 730 (augmented)
###### Energy Shield: 251 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Sockets: S 
###### --------
###### Item Level: 80
###### --------
###### 30% faster start of Energy Shield Recharge (rune)
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield
###### { Prefix Modifier "Rapturous" (Tier: 2) — Life }
###### +198(190-199) to maximum Life
###### { Prefix Modifier "Inspired" (Tier: 2) — Defences }
###### 99(92-100)% increased Armour and Energy Shield
###### { Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
###### +32(31-35)% to Cold Resistance
###### { Suffix Modifier "of the Troll" (Tier: 6) — Life }
###### 9.7(9.1-13) Life Regeneration per second
###### { Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
###### +24(21-25)% to Fire Resistance

##### 2.3.7. С несколькими сокетами и несколькими рунами (Corrupted)
###### Item Class: Body Armours
###### Rarity: Rare
###### Behemoth Shroud
###### Wolfskin Mantle
###### --------
###### Quality: +1% (augmented)
###### Armour: 730 (augmented)
###### Energy Shield: 251 (augmented)
###### --------
###### Requires: Level 65, 67 Str, 67 Int
###### --------
###### Sockets: S S 
###### --------
###### Item Level: 80
###### --------
###### +14% to Lightning Resistance (rune)
###### 30% faster start of Energy Shield Recharge (rune)
###### --------
###### { Prefix Modifier "Crusader's" (Tier: 3) — Defences }
###### +26(21-27) to Armour
###### +9(9-10) to maximum Energy Shield
###### 27(27-32)% increased Armour and Energy Shield
###### { Prefix Modifier "Rapturous" (Tier: 2) — Life }
###### +198(190-199) to maximum Life
###### { Prefix Modifier "Inspired" (Tier: 2) — Defences }
###### 99(92-100)% increased Armour and Energy Shield
###### { Suffix Modifier "of the Polar Bear" (Tier: 3) — Elemental, Cold, Resistance }
###### +32(31-35)% to Cold Resistance
###### { Suffix Modifier "of the Troll" (Tier: 6) — Life }
###### 9.7(9.1-13) Life Regeneration per second
###### { Suffix Modifier "of the Kiln" (Tier: 5) — Elemental, Fire, Resistance }
###### +24(21-25)% to Fire Resistance
###### --------
###### Corrupted

## 3. Алгоритм синтаксического анализа
Разделение на блоки — разбить текст по --------. Первый блок всегда содержит Item Class и Rarity.

Разбор блока 1 — извлечь класс, редкость, художественное название (если есть), базу.

Разбор блока 2 — строки с характеристиками (броня, урон и т.д.). Если есть Quality — запомнить.

Разбор блока 3 — строка Requires: — уровень и требования к характеристикам.

Разбор блока 4 — если есть строка Sockets: — разобрать сокеты.

Разбор блока 5 — строка Item Level:.

Разбор блока 6 — все строки между блоком 5 (или сокетами) и первым аффиксом — вставленные предметы.

Разбор блока 7 — аффиксы: искать пары {...} + следующая строка (эффект). Эффект может быть многострочным — нужно собирать до следующего { или конца.

Разбор блока 8 — последняя строка может быть Corrupted или Sanctified.

Особые случаи — пустой буфер → null, некорректные строки логировать и пропускать.

## 4. Примеры разбора
### 4.1. Базовый элемент (Normal)
По буферу из п. 2.1 извлекается:

Class: Boots

Rarity: Normal

Название: Daggerfoot Shoes

База: Daggerfoot Shoes

Блок 2: Evasion Rating: 119, Energy Shield: 45

Requires: Level 80, 59 Dex, 59 Int

Item Level: 82

### 4.2. Сложный элемент (Rare с сокетами, рунами и качеством)
По буферу из п. 2.3.7 извлекается:

Class: Body Armours

Rarity: Rare

Название: Behemoth Shroud

База: Wolfskin Mantle

Quality: +1% (augmented)

Характеристики: Armour: 730 (augmented), Energy Shield: 251 (augmented)

Requires: Level 65, 67 Str, 67 Int

Sockets: S S

Item Level: 80

Вставленные предметы: +14% to Lightning Resistance (rune), 30% faster start of Energy Shield Recharge (rune)

Аффиксы: 3 префикса, 3 суффикса

Состояние: Corrupted

## 5. Особые случаи
Пустые буферы — возвращать null.

Некорректно сформированные буферы — логировать ошибку, продолжать разбор по возможности (fallback).

## 6. Резюме
Документ описывает структуру буфера предмета, содержит полный набор примеров с нарастающей сложностью, алгоритм парсинга и обработку особых случаев. Используйте его как спецификацию для разработки парсера.
