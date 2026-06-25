using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameHelper.Services;

/// <summary>
/// Проверка распарсенного предмета на соответствие цели крафта: класс, тип модификатора, строка стата из библиотеки и порог переката.
/// </summary>
public static class ParsedItemCraftEvaluator
{
    private static readonly Regex LabeledRollParts = new(
        @"\b([A-Za-z]\w*)\s*:\s*([\d.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Убирает ведущий перекат в нормализованной строке:
    /// «+180(175-200) to …» → «+ to …», «+(47–50) to …» → «+ to …», «77(75-79)% …» → «% …».
    /// </summary>
    private static readonly Regex LeadingPlusRollPrefix = new(
        @"^\+(?:\d+(?:\.\d+)?(?:\([^)]*\))?|\([^)]*\))\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingPercentRollPrefix = new(
        @"^\d+(?:\([^)]*\))?\s*(?=%)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// «+(36–40)% to …» → «+% to …» (библиотека хранит диапазон вплотную к знаку %).
    private static readonly Regex LeadingPlusRangeBeforePercent = new(
        @"^\+(?:\d+(?:\.\d+)?|\([^)]+\))(?=%)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// «(101–110)% increased …» → «% increased …» (стат без знака + с диапазоном перед %).
    private static readonly Regex LeadingParenRangeBeforePercent = new(
        @"^\([^)]+\)(?=%)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Одиночные буквенные плейсхолдеры, которые ItemParser подставляет вместо перекатов в мультиролл-строках:
    /// «Adds X to Y Cold damage» — X и Y заменяют числа 22 и 34. Плейсхолдеры: X Y Z W V U T S R Q P O N M L K.
    /// </summary>
    private static readonly Regex LetterRollPlaceholder = new(
        @"\b[xyzwvutsrqponmlk]\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Числовые константы в тексте стата (напр. «2» в «within 2m»): фиксированные значения,
    /// не переваты; в шаблоне библиотеки хранятся как «#». Удаляются при финальном сравнении.
    /// </summary>
    private static readonly Regex FixedNumericInStat = new(
        @"\d[\d,.]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool ItemClassMatches(ParsedItem item, string expectedItemClass) =>
        string.Equals(item.ItemClass.Trim(), expectedItemClass.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Тип из условия/библиотеки и тип с предмета считаются одним семейством для поиска по имени+тиру
    /// (например <c>Desecrated Prefix Modifier</c> в плане и <c>Prefix Modifier</c> в Ctrl+Alt+C — один и тот же слот).
    /// </summary>
    public static bool AffixTypesCompatibleForNamedMatch(string typeFromPlan, string typeOnItem)
    {
        var p = (typeFromPlan ?? "").Trim();
        var a = (typeOnItem ?? "").Trim();
        if (string.Equals(p, a, StringComparison.Ordinal))
            return true;

        static bool IsPrefixFamily(string t) =>
            string.Equals(t, "Prefix Modifier", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase);

        static bool IsSuffixFamily(string t) =>
            string.Equals(t, "Suffix Modifier", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase);

        if (IsPrefixFamily(p) && IsPrefixFamily(a))
            return true;
        if (IsSuffixFamily(p) && IsSuffixFamily(a))
            return true;
        return false;
    }

    private static string StripLeadingRollFromNormalizedStat(string normalized)
    {
        var t = normalized.Trim();
        if (t.Length == 0)
            return t;
        var afterPlus = LeadingPlusRollPrefix.Replace(t, "+ ");
        if (!string.Equals(afterPlus, t, StringComparison.Ordinal))
            return afterPlus.TrimStart();
        var afterPercent = LeadingPercentRollPrefix.Replace(t, "").TrimStart();
        if (!string.Equals(afterPercent, t, StringComparison.Ordinal))
            return afterPercent;
        // «+(36–40)% to X» → «+% to X»
        var afterPlusRange = LeadingPlusRangeBeforePercent.Replace(t, "+");
        if (!string.Equals(afterPlusRange, t, StringComparison.Ordinal))
            return afterPlusRange;
        // «(101–110)% increased X» → «% increased X»
        return LeadingParenRangeBeforePercent.Replace(t, "").TrimStart();
    }

    /// <summary>
    /// Строка стата из парсера совпадает с шаблоном из библиотеки (нормализация, затем точное совпадение или вхождение длинной подстроки).
    /// Короткие общие фрагменты вроде «ritual altars in map» намеренно не считаются совпадением — иначе разные статы путаются.
    /// </summary>
    public static bool StatLineMatchesTemplate(string parsedStatText, string libraryTemplate)
    {
        var a = NormalizeStat(parsedStatText);
        var b = NormalizeStat(libraryTemplate);
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        var aNoRoll = StripLeadingRollFromNormalizedStat(a);
        if (aNoRoll.Length > 0)
        {
            if (string.Equals(aNoRoll, b, StringComparison.Ordinal))
                return true;

            // Стрипуем ведущий перекат и из шаблона (напр. "+(47–50) to Spirit" → "+ to Spirit"),
            // чтобы сравнить обе стороны без числовой части.
            var bNoRoll = StripLeadingRollFromNormalizedStat(b);
            if (bNoRoll.Length > 0 && string.Equals(aNoRoll, bNoRoll, StringComparison.Ordinal))
                return true;

            // Гибридный мод: библиотека хранит два стата одной строкой без разделителя
            // (напр. "+(26–30) to maximum Energy Shield(39–42)% increased Energy Shield").
            // На предмете они показываются раздельно. Считаем совпадением, если один stripped-стат
            // является строгим префиксом другого с границей на не-буквенно-цифровом символе.
            const int minLenForPrefixMatch = 15;
            if (aNoRoll.Length >= minLenForPrefixMatch && bNoRoll.Length >= minLenForPrefixMatch)
            {
                static bool IsWordPrefix(string shorter, string longer) =>
                    longer.StartsWith(shorter, StringComparison.Ordinal) &&
                    (longer.Length == shorter.Length || !char.IsLetterOrDigit(longer[shorter.Length]));

                if (IsWordPrefix(aNoRoll, bNoRoll) || IsWordPrefix(bNoRoll, aNoRoll))
                    return true;
            }

            // Шаблон может содержать '#' как плейсхолдер числа (напр. "+# to Level of all Spell Skills").
            // Парсер уже извлёк число в RolledValue, оставив в StatText "+ to Level…".
            // Сравниваем aNoRoll с шаблоном без '#'.
            var bNoHash = b.Replace("#", "", StringComparison.Ordinal).Trim();
            while (bNoHash.Contains("  ", StringComparison.Ordinal))
                bNoHash = bNoHash.Replace("  ", " ", StringComparison.Ordinal);
            if (bNoHash.Length > 0 && string.Equals(aNoRoll, bNoHash, StringComparison.Ordinal))
                return true;

            // Stat вида "+N(range)%" теряет знак в StatText (aNoRoll = "% to X"),
            // шаблон хранит его (bNoHash = "+% to X"). Пробуем сравнение без ведущего знака из шаблона.
            if (bNoHash.Length > 1 && (bNoHash[0] == '+' || bNoHash[0] == '-'))
            {
                var bStripped = bNoHash[1..];
                if (string.Equals(aNoRoll, bStripped, StringComparison.Ordinal))
                    return true;
            }

            // Мультиролл-стат: ItemParser ставит буквенные плейсхолдеры x/y/z (после нормализации),
            // рецепт хранит '#'. Убираем буквы-плейсхолдеры из a и сравниваем с bNoHash.
            var aNoLetters = LetterRollPlaceholder.Replace(a, "").Trim();
            while (aNoLetters.Contains("  ", StringComparison.Ordinal))
                aNoLetters = aNoLetters.Replace("  ", " ", StringComparison.Ordinal);
            if (aNoLetters.Length > 0 && bNoHash.Length > 0 &&
                string.Equals(aNoLetters, bNoHash, StringComparison.Ordinal))
                return true;

            // Финальный шаг: стат содержит числовые константы (напр. «2» в «within 2m»),
            // которые не являются перекатом, но шаблон библиотеки хранит их как «#».
            // Убираем числа из aNoLetters и сравниваем с bNoHash.
            var aNoFixed = FixedNumericInStat.Replace(aNoLetters, "");
            while (aNoFixed.Contains("  ", StringComparison.Ordinal))
                aNoFixed = aNoFixed.Replace("  ", " ", StringComparison.Ordinal);
            aNoFixed = aNoFixed.Trim();
            if (aNoFixed.Length >= minLenForPrefixMatch && bNoHash.Length >= minLenForPrefixMatch &&
                string.Equals(aNoFixed, bNoHash, StringComparison.Ordinal))
                return true;

            // Короткий шаблон целиком в строке предмета (в т.ч. суффикс вроде «(rune)»), без общих 2–3 слов.
            const int minTemplateLenForContains = 14;
            if (b.Length >= minTemplateLenForContains && aNoRoll.Contains(b, StringComparison.Ordinal))
                return true;
        }

        // Только короткая сторона достаточно длинная — иначе Contains даёт ложные совпадения между разными аффиксами.
        const int minLenForSubstringMatch = 44;
        var shorter = a.Length < b.Length ? a : b;
        var longer = a.Length < b.Length ? b : a;
        if (shorter.Length < minLenForSubstringMatch)
            return false;

        // Длинная строка не должна «поглощать» по Contains другую при большой разнице длин — иначе разные статы путаются (в т.ч. COUNT).
        if (longer.Length - shorter.Length > 12)
            return false;

        return longer.Contains(shorter, StringComparison.Ordinal);
    }

    /// <summary>
    /// Условие остановки крафта: нужный префикс/суффикс, строка стата и значение переката ≥ minRoll.
    /// </summary>
    public static bool TryMatchCraftTarget(
        ParsedItem? item,
        string expectedItemClass,
        AffixLibraryEntry entry,
        string selectedAffixStat,
        double minRoll,
        out string explanation)
    {
        explanation = "";
        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан (парсер вернул пустой или невалидный результат).";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        var statIdx = AffixCraftPatternBuilder.GetStatIndex(entry, selectedAffixStat);
        var rangeStr = statIdx >= 0 && statIdx < entry.AffixRanges.Count ? entry.AffixRanges[statIdx] : null;
        var slots = CraftAffixCascadeHelper.CountSlotsFromRangeString(rangeStr);
        var mins = Enumerable.Repeat(minRoll, slots).ToList();

        if (!TryGetRollValuesForNamedAffix(
                item,
                expectedItemClass,
                entry.AffixType,
                entry.AffixName,
                entry.AffixTier,
                selectedAffixStat,
                slots,
                out var values,
                out var rollExpl))
        {
            explanation = string.IsNullOrEmpty(rollExpl)
                ? $"На предмете нет модификатора «{entry.AffixName}» ({entry.AffixType}, T{entry.AffixTier}) со строчкой «{selectedAffixStat}»."
                : rollExpl;
            return false;
        }

        var pass = RollVectorMeetsMins(values, mins, out var failIdx);
        var minStr = string.Join(", ", mins.Select(FormatMin));
        var valStr = string.Join(", ", values.Select(FormatMin));
        explanation = pass
            ? $"Найден модификатор «{entry.AffixName}», стата «{selectedAffixStat}», значения [{valStr}] ≥ [{minStr}]."
            : $"Найден модификатор «{entry.AffixName}», стата «{selectedAffixStat}», слот {failIdx + 1}: значение не достигает порога (есть [{valStr}], нужно ≥ [{minStr}]).";
        return pass;
    }

    /// <summary>
    /// Ищет модификатор по типу, имени и тиру; из строки стата извлекает ровно <paramref name="expectedSlotCount"/> чисел (X, Y, …).
    /// </summary>
    public static bool TryGetRollValuesForNamedAffix(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string affixName,
        int affixTier,
        string statTemplate,
        int expectedSlotCount,
        out List<double> values,
        out string explanation)
    {
        values = new List<double>();
        explanation = "";

        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан.";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        if (expectedSlotCount < 1)
            expectedSlotCount = 1;

        // Multiple affixes may share the same (name, tier) — e.g. rune affixes from the same rune
        // or desecrated items where two different stat families share a name and tier.
        // We must continue past the first (name, tier) match when its stat doesn't match,
        // so that a subsequent affix with the same (name, tier) but a different stat can be found.
        var foundByNameTier = false;
        foreach (var affix in item.Affixes)
        {
            if (!AffixTypesCompatibleForNamedMatch(affixType, affix.Type))
                continue;
            if (!string.Equals(affix.Name, affixName.Trim(), StringComparison.Ordinal))
                continue;
            if (affix.Tier != affixTier)
                continue;

            foundByNameTier = true;

            foreach (var line in affix.EffectDetails)
            {
                if (!StatLineMatchesTemplate(line.StatText, statTemplate))
                    continue;

                if (!TryGetOrderedRollValues(line, out var vals, out var rollNote))
                {
                    explanation = $"Строка стата найдена, но не удалось извлечь числа. {rollNote}";
                    return false;
                }

                if (vals.Count < expectedSlotCount)
                {
                    explanation =
                        $"Ожидалось {expectedSlotCount} перекат(ов), в строке предмета — {vals.Count} ({string.Join(", ", vals)}).";
                    return false;
                }

                // Для мультиролл-статов (Adds X to Y…) библиотека может хранить только первый диапазон;
                // берём первые expectedSlotCount значений.
                values = vals.Count == expectedSlotCount ? vals : vals.Take(expectedSlotCount).ToList();
                return true;
            }
        }

        if (foundByNameTier)
            explanation = $"Модификатор «{affixName}» найден, но без подходящей строки стата «{statTemplate}».";

        return false;
    }

    /// <summary>
    /// Перекаты по множеству записей библиотеки: класс + тип + строка стата (все подходящие имя/тир из affix_library.json).
    /// </summary>
    public static bool TryGetRollValuesForTypeAndStat(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string statTemplate,
        IReadOnlyList<AffixLibraryEntry> libraryEntries,
        int expectedSlotCount,
        out List<double> values,
        out string explanation)
    {
        values = new List<double>();
        explanation = "";

        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан.";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        if (expectedSlotCount < 1)
            expectedSlotCount = 1;

        var candidates = CraftAffixCascadeHelper.GetCandidateNameTiers(
            expectedItemClass,
            affixType,
            statTemplate,
            libraryEntries);
        if (candidates.Count == 0)
        {
            explanation =
                $"В библиотеке нет аффиксов для класса «{expectedItemClass}», типа «{affixType}» и стата «{statTemplate}».";
            return false;
        }

        foreach (var affix in item.Affixes)
        {
            if (!string.Equals(affix.Type, affixType.Trim(), StringComparison.Ordinal))
                continue;

            var key = (affix.Name.Trim(), affix.Tier);
            if (!candidates.Contains(key))
                continue;

            foreach (var line in affix.EffectDetails)
            {
                if (!StatLineMatchesTemplate(line.StatText, statTemplate))
                    continue;
                if (!TryGetOrderedRollValues(line, out var vals, out _))
                    continue;
                if (vals.Count < expectedSlotCount)
                    continue;
                values = vals.Count == expectedSlotCount ? vals : vals.Take(expectedSlotCount).ToList();
                return true;
            }
        }

        explanation =
            $"На предмете нет модификатора из заданного набора (тип «{affixType}», стата «{statTemplate}», нужно {expectedSlotCount} перекат(ов)).";
        return false;
    }

    /// <summary>
    /// Все наборы перекатов для пары (тип в плане / семейство префикса·суффикса, шаблон стата): по одному на каждую подходящую строку.
    /// Нужно для клозов Single без имени аффикса — на предмете может быть несколько префиксов с одним и тем же статом (напр. Dictator + Merciless).
    /// </summary>
    public static IEnumerable<List<double>> EnumerateRollValuesForTypeAndStatNoLibrary(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string statTemplate)
    {
        if (item is not { IsValid: true })
            yield break;
        if (!ItemClassMatches(item, expectedItemClass))
            yield break;

        foreach (var affix in item.Affixes)
        {
            if (!AffixTypesCompatibleForNamedMatch(affixType, affix.Type))
                continue;

            foreach (var line in affix.EffectDetails)
            {
                if (!StatLineMatchesTemplate(line.StatText, statTemplate))
                    continue;
                if (!TryGetOrderedRollValues(line, out var vals, out _))
                    continue;
                if (vals.Count > 0)
                    yield return vals;
            }
        }
    }

    /// <summary>
    /// Перекаты по типу модификатора и строке стата без привязки к <c>affix_library.json</c>.
    /// Используется как fallback, когда библиотека не содержит нужного имени/тира, но на предмете строка явно есть.
    /// Возвращает первое найденное вхождение; для порогов по «любому» аффиксу используется перебор (клоз Single в CraftConditionEvaluator).
    /// </summary>
    public static bool TryGetRollValuesForTypeAndStatNoLibrary(
        ParsedItem? item,
        string expectedItemClass,
        string affixType,
        string statTemplate,
        out List<double> values,
        out string explanation)
    {
        values = new List<double>();
        explanation = "";

        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан.";
            return false;
        }

        if (!ItemClassMatches(item, expectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        foreach (var vals in EnumerateRollValuesForTypeAndStatNoLibrary(item, expectedItemClass, affixType, statTemplate))
        {
            values = vals;
            return true;
        }

        explanation =
            $"На предмете нет модификатора типа «{affixType}» со строчкой стата «{statTemplate}».";
        return false;
    }

    public static bool RollVectorMeetsMins(IReadOnlyList<double> actual, IReadOnlyList<double> mins, out int failIndex)
    {
        failIndex = -1;
        if (actual.Count != mins.Count)
            return false;
        for (var i = 0; i < actual.Count; i++)
        {
            if (actual[i] < mins[i])
            {
                failIndex = i;
                return false;
            }
        }

        return true;
    }

    /// <summary>Числа переката в порядке появления в строке (X, Y, …).</summary>
    public static bool TryGetOrderedRollValues(AffixEffectLine line, out List<double> values, out string note)
    {
        values = new List<double>();
        note = "";
        var raw = line.RolledValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            note = "RolledValue пуст.";
            return false;
        }

        values = ParseRollValues(raw);
        if (values.Count == 0)
        {
            note = $"Не удалось разобрать числа из «{raw}».";
            return false;
        }

        return true;
    }

    private static List<double> ParseRollValues(string rolled)
    {
        var list = new List<double>();
        if (rolled.Contains(';', StringComparison.Ordinal))
        {
            foreach (Match m in LabeledRollParts.Matches(rolled))
            {
                if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }

            if (list.Count > 0)
                return list;
        }

        if (double.TryParse(rolled, NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
        {
            list.Add(single);
            return list;
        }

        if (double.TryParse(rolled, NumberStyles.Float, CultureInfo.CurrentCulture, out single))
        {
            list.Add(single);
            return list;
        }

        return list;
    }

    /// <summary>
    /// Проверяет, совпадает ли хотя бы один зафиксированный (Fractured) аффикс на предмете с условием крафта.
    /// Создаёт временный вид предмета с только <c>IsFractured=true</c> аффиксами и прогоняет через стандартный эвалюатор.
    /// </summary>
    public static bool FracturedAffixMatchesPlan(ParsedItem item, CraftConditionPlan plan, out string explanation)
    {
        var fracturedAffixes = item.Affixes.Where(a => a.IsFractured).ToList();
        if (fracturedAffixes.Count == 0)
        {
            explanation = "На предмете нет зафиксированных (Fractured) аффиксов.";
            return false;
        }

        var filteredItem = new ParsedItem
        {
            IsValid = item.IsValid,
            ItemClass = item.ItemClass,
            Rarity = item.Rarity,
            Name = item.Name,
            Base = item.Base,
            Affixes = fracturedAffixes,
        };
        return CraftConditionEvaluator.TryEvaluate(plan, filteredItem, out explanation);
    }

    private static string NormalizeStat(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t;
    }

    private static string FormatMin(double v) =>
        v == Math.Truncate(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Ищет на предмете аффикс того же семейства статов (те же шаблоны, нормализованные до #),
    /// но с тиром ЛУЧШЕ (меньший номер) чем <paramref name="conditionTier"/>,
    /// и проверяет, удовлетворяет ли он порогам из <paramref name="lines"/>.
    /// </summary>
    public static bool TryMatchBetterTierSameFamily(
        ParsedItem item,
        string expectedItemClass,
        string affixType,
        int conditionTier,
        IReadOnlyList<CraftWholeModifierLine> lines,
        AffixLibraryEntry familyRef,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail)
    {
        detail = "";
        foreach (var itemAffix in item.Affixes)
        {
            if (!string.Equals(itemAffix.Type, affixType, StringComparison.Ordinal))
                continue;
            var libEntry = AffixCraftPatternBuilder.FindEntryByNameTypeAnyTier(
                lib, expectedItemClass, affixType, itemAffix.Name);
            if (libEntry is null)
                continue;
            if (libEntry.AffixTier >= conditionTier)
                continue;
            if (libEntry.AffixStats.Count != familyRef.AffixStats.Count)
                continue;
            var sameFam = true;
            for (var i = 0; i < familyRef.AffixStats.Count; i++)
            {
                var n1 = CraftAffixCascadeHelper.NormalizeStatToTemplate(familyRef.AffixStats[i]);
                var n2 = CraftAffixCascadeHelper.NormalizeStatToTemplate(libEntry.AffixStats[i]);
                if (!string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase))
                {
                    sameFam = false;
                    break;
                }
            }
            if (!sameFam)
                continue;
            var allMatch = true;
            foreach (var line in lines)
            {
                var idx = CraftAffixCascadeHelper.FindStatIndexInEntry(libEntry, line.StatTemplate);
                if (idx < 0) { allMatch = false; break; }
                var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(libEntry, idx);
                line.EnsureMinRollsSize(slots);
                var mins = line.GetEffectiveMinRolls(slots).ToList();
                if (!TryGetRollValuesForNamedAffix(
                        item, expectedItemClass, affixType, itemAffix.Name,
                        libEntry.AffixTier, line.StatTemplate, slots, out var actual, out _))
                {
                    allMatch = false; break;
                }
                if (!RollVectorMeetsMins(actual, mins, out _))
                {
                    allMatch = false; break;
                }
            }
            if (allMatch)
            {
                detail = $"лучший тир T{libEntry.AffixTier} «{itemAffix.Name}» удовлетворяет всем строкам семейства (условие T{conditionTier}).";
                return true;
            }
        }
        return false;
    }

    /// <summary>Все строки целого модификатора на одном аффиксе удовлетворяют порогам. Имя: <paramref name="affixNameOverride"/> или <see cref="CraftWholeModifierAffixData.AffixName"/>.</summary>
    public static bool TryEvaluateWholeModifierAffix(
        CraftWholeModifierAffixData whole,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail,
        string? affixNameOverride = null)
    {
        detail = "";
        if (whole.Lines.Count == 0)
        {
            detail = "целый модификатор: нет строк стата.";
            return false;
        }

        var useName = (affixNameOverride ?? whole.AffixName).Trim();
        if (useName.Length == 0)
        {
            detail = "целый модификатор: не задано имя.";
            return false;
        }

        var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            lib,
            expectedItemClass,
            whole.AffixType,
            useName,
            whole.AffixTier);
        if (entry is null)
        {
            detail =
                $"В библиотеке нет записи «{useName}» ({whole.AffixType}, T{whole.AffixTier}) для класса «{expectedItemClass}».";
            return false;
        }

        foreach (var line in whole.Lines)
        {
            var idx = ResolveStatIndexInEntry(entry, line.StatTemplate);
            if (idx < 0)
            {
                detail = $"Строка «{line.StatTemplate}» не входит в выбранную запись библиотеки «{useName}».";
                return false;
            }

            var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, idx);
            line.EnsureMinRollsSize(slots);
            if (!TryGetRollValuesForNamedAffix(
                    item,
                    expectedItemClass,
                    whole.AffixType,
                    useName,
                    whole.AffixTier,
                    line.StatTemplate,
                    slots,
                    out var actual,
                    out var expl))
            {
                detail = string.IsNullOrEmpty(expl) ? $"Нет модификатора «{useName}» со строкой «{line.StatTemplate}»." : expl;
                return false;
            }

            var mins = line.GetEffectiveMinRolls(slots).ToList();
            if (!RollVectorMeetsMins(actual, mins, out var failIdx))
            {
                detail =
                    $"«{useName}», «{line.StatTemplate}»: слот {failIdx + 1} — ниже порога (есть [{string.Join(", ", actual.Select(FormatMin))}], нужно ≥ [{string.Join(", ", mins.Select(FormatMin))}]).";
                return false;
            }
        }

        return true;
    }

    /// <summary>Для ветвления: есть ли хотя бы одна строка целого модификатора, полностью удовлетворяющая порогам.</summary>
    public static bool TryWholeModifierAnyLineFullySatisfied(
        CraftWholeModifierAffixData whole,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib)
    {
        foreach (var nm in whole.EffectiveWholeAffixNames())
        {
            var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                lib,
                expectedItemClass,
                whole.AffixType,
                nm,
                whole.AffixTier);
            if (entry is null)
                continue;

            foreach (var line in whole.Lines)
            {
                var idx = ResolveStatIndexInEntry(entry, line.StatTemplate);
                if (idx < 0)
                    continue;
                var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, idx);
                line.EnsureMinRollsSize(slots);
                if (!TryGetRollValuesForNamedAffix(
                        item,
                        expectedItemClass,
                        whole.AffixType,
                        nm,
                        whole.AffixTier,
                        line.StatTemplate,
                        slots,
                        out var actual,
                        out _))
                    continue;
                var mins = line.GetEffectiveMinRolls(slots).ToList();
                if (RollVectorMeetsMins(actual, mins, out _))
                    return true;
            }
        }

        return false;
    }

    private static int ResolveStatIndexInEntry(AffixLibraryEntry entry, string statTemplate)
    {
        var idx = AffixCraftPatternBuilder.GetStatIndex(entry, statTemplate);
        if (idx >= 0)
            return idx;
        for (var i = 0; i < entry.AffixStats.Count; i++)
        {
            if (StatLineMatchesTemplate(entry.AffixStats[i], statTemplate) ||
                StatLineMatchesTemplate(statTemplate, entry.AffixStats[i]) ||
                CraftAffixCascadeHelper.StatMatchesNormalizedTemplate(entry.AffixStats[i], statTemplate))
                return i;
        }

        return -1;
    }
}
