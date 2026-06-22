using System.Globalization;
using System.Linq;
using System.Text;

namespace GameHelper.Services;

/// <summary>Разбор и проверка <see cref="CraftConditionPlan"/> по распарсенному предмету.</summary>
public static class CraftConditionEvaluator
{
    public static bool TryValidate(CraftConditionPlan plan, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(plan.ExpectedItemClass))
        {
            error = "Укажите класс предмета.";
            return false;
        }

        if (plan.OrAlternatives.Count == 0)
        {
            error = "Добавьте хотя бы один вариант условия (связь ИЛИ между вариантами).";
            return false;
        }

        var altIndex = 0;
        foreach (var alt in plan.OrAlternatives)
        {
            altIndex++;
            if (alt.Clauses.Count == 0)
            {
                error = $"В варианте {altIndex} нет ни одного условия (нужна связь И внутри варианта).";
                return false;
            }

            var cIndex = 0;
            foreach (var c in alt.Clauses)
            {
                cIndex++;
                if (c.Kind == CraftClauseKind.Single)
                {
                    c.Single?.EnsureLinesFromLegacy();
                    if (c.Single is null || string.IsNullOrWhiteSpace(c.Single.AffixType) ||
                        (c.Single.Lines.Count == 0 && string.IsNullOrWhiteSpace(c.Single.StatTemplate)))
                    {
                        error = $"В варианте {altIndex}, клоз {cIndex}: выберите тип модификатора и семейство стата.";
                        return false;
                    }

                    var sn = c.Single.EffectiveAffixNames();
                    if (sn.Count == 0)
                    {
                        error =
                            $"В варианте {altIndex}, клоз {cIndex}: выберите хотя бы одно имя аффикса.";
                        return false;
                    }

                    if (c.Single.AffixTier < 1)
                    {
                        error = $"В варианте {altIndex}, клоз {cIndex}: выберите тир аффикса.";
                        return false;
                    }

                    var libV = AffixLibrary.GetEntries();
                    var statsToCheck = c.Single.Lines.Count > 0
                        ? c.Single.Lines.Select(l => l.StatTemplate).ToList()
                        : new List<string> { c.Single.StatTemplate };
                    foreach (var nm in sn)
                    {
                        var ent = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                            libV,
                            plan.ExpectedItemClass,
                            c.Single.AffixType,
                            nm,
                            c.Single.AffixTier);
                        if (ent is null)
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}: в библиотеке нет «{nm}» ({c.Single.AffixType}, T{c.Single.AffixTier}).";
                            return false;
                        }

                        foreach (var st in statsToCheck)
                        {
                            if (CraftAffixCascadeHelper.FindStatIndexInEntry(ent, st) < 0)
                            {
                                error =
                                    $"В варианте {altIndex}, клоз {cIndex}: строка стата «{st}» не входит в «{nm}» (T{c.Single.AffixTier}).";
                                return false;
                            }
                        }
                    }
                }
                else if (c.Kind == CraftClauseKind.Sum)
                {
                    if (c.Sum is null || c.Sum.Parts.Count == 0)
                    {
                        error = $"В варианте {altIndex}, клоз {cIndex}: в сумме нет ни одного аффикса.";
                        return false;
                    }

                    foreach (var p in c.Sum.Parts)
                    {
                        if (string.IsNullOrWhiteSpace(p.AffixType) || string.IsNullOrWhiteSpace(p.StatTemplate))
                        {
                            error = $"В варианте {altIndex}: в сумме укажите тип модификатора и строку стата.";
                            return false;
                        }
                    }
                }
                else if (c.Kind == CraftClauseKind.Count)
                {
                    if (c.Count is null || c.Count.Members.Count == 0)
                    {
                        error = $"В варианте {altIndex}, клоз {cIndex}: в наборе COUNT добавьте хотя бы один аффикс.";
                        return false;
                    }

                    var n = c.Count.Members.Count;
                    if (c.Count.MinMatchCount < 1 || c.Count.MinMatchCount > n)
                    {
                        error =
                            $"В варианте {altIndex}, клоз {cIndex}: COUNT должен быть от 1 до {n} (сейчас {c.Count.MinMatchCount}).";
                        return false;
                    }

                    for (var mi = 0; mi < c.Count.Members.Count; mi++)
                    {
                        var m = c.Count.Members[mi];
                        if (string.IsNullOrWhiteSpace(m.AffixType))
                        {
                            error = $"В варианте {altIndex}, клоз {cIndex}, аффикс {mi + 1}: укажите тип модификатора.";
                            return false;
                        }

                        if (m.EffectiveWholeAffixNames().Count == 0)
                        {
                            error = $"В варианте {altIndex}, клоз {cIndex}, аффикс {mi + 1}: выберите хотя бы одно имя аффикса.";
                            return false;
                        }

                        if (m.AffixTier < 1)
                        {
                            error = $"В варианте {altIndex}, клоз {cIndex}, аффикс {mi + 1}: выберите тир аффикса.";
                            return false;
                        }

                        if (m.Lines.Count == 0)
                        {
                            error = $"В варианте {altIndex}, клоз {cIndex}, аффикс {mi + 1}: добавьте хотя бы одну строку стата.";
                            return false;
                        }

                        for (var li = 0; li < m.Lines.Count; li++)
                        {
                            if (string.IsNullOrWhiteSpace(m.Lines[li].StatTemplate))
                            {
                                error = $"В варианте {altIndex}, клоз {cIndex}, аффикс {mi + 1}, строка {li + 1}: пустая строка стата.";
                                return false;
                            }
                        }
                    }
                }
                else if (c.Kind == CraftClauseKind.WholeModifier)
                {
                    if (c.Whole is null ||
                        string.IsNullOrWhiteSpace(c.Whole.AffixType) ||
                        c.Whole.Lines.Count == 0)
                    {
                        error =
                            $"В варианте {altIndex}, клоз {cIndex}: укажите тип и хотя бы одну строку стата.";
                        return false;
                    }

                    var wn = c.Whole.EffectiveWholeAffixNames();
                    if (wn.Count == 0)
                    {
                        error =
                            $"В варианте {altIndex}, клоз {cIndex}: выберите хотя бы одно имя целого модификатора.";
                        return false;
                    }

                    if (c.Whole.AffixTier < 1)
                    {
                        error = $"В варианте {altIndex}, клоз {cIndex}: выберите тир модификатора.";
                        return false;
                    }

                    var lib = AffixLibrary.GetEntries();
                    AffixLibraryEntry? firstEnt = null;
                    foreach (var nm in wn)
                    {
                        var ent = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                            lib,
                            plan.ExpectedItemClass,
                            c.Whole.AffixType,
                            nm,
                            c.Whole.AffixTier);
                        if (ent is null)
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}: в библиотеке нет «{nm}» ({c.Whole.AffixType}, T{c.Whole.AffixTier}).";
                            return false;
                        }

                        if (ent.AffixStats.Count < 2)
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}: «{nm}» не является многострочным модификатором в библиотеке.";
                            return false;
                        }

                        if (firstEnt is null)
                            firstEnt = ent;
                        else if (!CraftAffixCascadeHelper.EntriesShareSameAffixStats(firstEnt, ent))
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}: все выбранные имена должны иметь один и тот же набор строк стата в библиотеке.";
                            return false;
                        }
                    }

                    var entry = firstEnt!;
                    for (var li = 0; li < c.Whole.Lines.Count; li++)
                    {
                        var line = c.Whole.Lines[li];
                        if (string.IsNullOrWhiteSpace(line.StatTemplate))
                        {
                            error = $"В варианте {altIndex}, клоз {cIndex}: пустая строка стата #{li + 1}.";
                            return false;
                        }

                        if (AffixCraftPatternBuilder.GetStatIndex(entry, line.StatTemplate) < 0 &&
                            CraftAffixCascadeHelper.FindStatIndexInEntry(entry, line.StatTemplate) < 0)
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}: строка «{line.StatTemplate}» не входит в выбранные записи библиотеки.";
                            return false;
                        }
                    }
                }
                else
                {
                    error = $"Неизвестный тип клоза в варианте {altIndex}.";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Выполняется ли план целиком (класс + один из вариантов ИЛИ полностью).</summary>
    public static bool TryEvaluate(CraftConditionPlan plan, ParsedItem? item, out string explanation)
    {
        explanation = "";
        if (item is not { IsValid: true })
        {
            explanation = "Предмет не распознан (парсер вернул пустой или невалидный результат).";
            return false;
        }

        if (!ParsedItemCraftEvaluator.ItemClassMatches(item, plan.ExpectedItemClass))
        {
            explanation =
                $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{plan.ExpectedItemClass}».";
            return false;
        }

        if (plan.OrAlternatives.Count == 0)
        {
            explanation = "Условие крафта пустое.";
            return false;
        }

        var orIndex = 0;
        foreach (var alt in plan.OrAlternatives)
        {
            orIndex++;
            if (TryEvaluateAndGroup(alt, item, plan.ExpectedItemClass, out var sub))
            {
                explanation = $"Выполнен вариант {orIndex} (ИЛИ): {sub}";
                return true;
            }
        }

        explanation = "Ни один вариант условия (ИЛИ) не выполнен.";
        return false;
    }

    private static bool TryEvaluateAndGroup(
        CraftAndGroup group,
        ParsedItem item,
        string expectedItemClass,
        out string detail)
    {
        if (group.Clauses is null || group.Clauses.Count == 0)
        {
            detail = "в варианте ИЛИ нет ни одного условия (пустой список клозов).";
            return false;
        }

        var lib = AffixLibrary.GetEntries();
        var sb = new StringBuilder();
        foreach (var clause in group.Clauses)
        {
            if (clause.Kind == CraftClauseKind.Single)
            {
                if (clause.Single is null)
                {
                    detail = "внутренняя ошибка: Single.";
                    return false;
                }

                if (!TryMatchSingleAffix(clause.Single, item, expectedItemClass, lib, out detail))
                    return false;

                var s = clause.Single;
                if (s.Lines.Count > 0)
                {
                    var minsStr = string.Join(", ", s.Lines.Select(l =>
                        $"{l.StatTemplate.Split(' ').FirstOrDefault() ?? "?"}≥{string.Join("/", l.GetEffectiveMinRolls(1).Select(FormatNum))}"));
                    sb.Append($"[{s.AffixType}]({minsStr}); ");
                }
                else
                {
                    var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(expectedItemClass, s.AffixType, s.StatTemplate, lib);
                    var mins = s.GetEffectiveMinRolls(slots);
                    sb.Append($"[{s.AffixType}]≥{string.Join("/", mins.Select(FormatNum))}; ");
                }
            }
            else if (clause.Kind == CraftClauseKind.Sum)
            {
                if (clause.Sum is null)
                {
                    detail = "внутренняя ошибка: Sum.";
                    return false;
                }

                double sum = 0;
                var parts = new List<string>();
                foreach (var p in clause.Sum.Parts)
                {
                    double contrib;
                    // В условиях крафта не привязываемся к affixName/affixTier — ищем только по (тип + строка стата).
                    if (ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStatNoLibrary(
                            item,
                            expectedItemClass,
                            p.AffixType,
                            p.StatTemplate,
                            out var vals,
                            out _))
                        contrib = vals.Sum();
                    else
                        contrib = 0;

                    sum += contrib;
                    parts.Add($"{contrib:0.##}");
                }

                if (sum < clause.Sum.MinSum)
                {
                    detail =
                        $"сумма по группе = {sum:0.##} (части {string.Join("+", parts)}), нужно ≥ {FormatNum(clause.Sum.MinSum)}.";
                    return false;
                }

                sb.Append($"Σ({string.Join("+", parts)})≥{FormatNum(clause.Sum.MinSum)}; ");
            }
            else if (clause.Kind == CraftClauseKind.Count)
            {
                if (clause.Count is null)
                {
                    detail = "внутренняя ошибка: Count.";
                    return false;
                }

                if (!TryEvaluateCountClause(clause.Count, item, expectedItemClass, lib, out var countDetail))
                {
                    detail = countDetail;
                    return false;
                }

                var matched = CountMatchedMembers(clause.Count, item, expectedItemClass, lib);
                sb.Append($"COUNT≥{clause.Count.MinMatchCount}({matched}/{clause.Count.Members.Count}); ");
            }
            else if (clause.Kind == CraftClauseKind.WholeModifier)
            {
                if (clause.Whole is null)
                {
                    detail = "внутренняя ошибка: WholeModifier.";
                    return false;
                }

                var w = clause.Whole;
                var okWhole = false;
                var lastWholeFail = "";
                foreach (var nm in w.EffectiveWholeAffixNames())
                {
                    if (ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(
                            w,
                            item,
                            expectedItemClass,
                            lib,
                            out var subWhole,
                            nm))
                    {
                        okWhole = true;
                        break;
                    }

                    lastWholeFail = subWhole;
                }

                if (!okWhole)
                {
                    detail = string.IsNullOrEmpty(lastWholeFail)
                        ? "целый модификатор: ни одно из выбранных имён не удовлетворяет всем строкам."
                        : lastWholeFail;
                    return false;
                }

                sb.Append($"[целый:{string.Join('|', w.EffectiveWholeAffixNames())}] ");
            }
            else
            {
                detail = "неизвестный тип клоза в варианте ИЛИ.";
                return false;
            }
        }

        detail = sb.ToString().TrimEnd(' ', ';');
        return true;
    }

    /// <summary>Клоз Single / член COUNT: имя и тир как в плане, затем пороги по строке стата.</summary>
    public static bool TryEvaluateSingleAffixClause(
        CraftSingleAffixData s,
        ParsedItem item,
        string expectedItemClass,
        out string detail) =>
        TryMatchSingleAffix(s, item, expectedItemClass, AffixLibrary.GetEntries(), out detail);

    private static int CountMatchedMembers(
        CraftCountAffixData cnt,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib)
    {
        var matched = 0;
        foreach (var m in cnt.Members)
        {
            if (TryMatchCountMember(m, item, expectedItemClass, lib, out _))
                matched++;
        }

        return matched;
    }

    private static bool TryMatchCountMember(
        CraftWholeModifierAffixData m,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail)
    {
        detail = "";
        if (m.Lines.Count == 0)
        {
            detail = "Нет строк стата для этого аффикса.";
            return false;
        }

        var names = m.EffectiveWholeAffixNames();
        if (names.Count == 0)
        {
            detail = "Не выбраны имена аффикса.";
            return false;
        }

        foreach (var nm in names)
        {
            if (ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(m, item, expectedItemClass, lib, out _, nm))
                return true;
        }

        ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(m, item, expectedItemClass, lib, out detail, names[0]);
        return false;
    }

    private static bool TryEvaluateCountClause(
        CraftCountAffixData cnt,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail)
    {
        detail = "";
        var matched = CountMatchedMembers(cnt, item, expectedItemClass, lib);
        if (matched >= cnt.MinMatchCount)
            return true;

        var parts = new List<string>();
        foreach (var m in cnt.Members)
        {
            var label = FormatCountMemberLabel(m);
            if (TryMatchCountMember(m, item, expectedItemClass, lib, out var fail))
                parts.Add($"{label}: OK");
            else
                parts.Add($"{label}: {fail}");
        }

        detail =
            $"набор COUNT: выполнено {matched} из {cnt.Members.Count} (нужно ≥ {cnt.MinMatchCount}). " +
            string.Join("; ", parts);
        return false;
    }

    private static string FormatCountMemberLabel(CraftWholeModifierAffixData m)
    {
        var nm = string.Join("|", m.EffectiveWholeAffixNames());
        if (nm.Length > 24)
            nm = nm[..21] + "…";
        return $"«{nm}» T{m.AffixTier} ({m.Lines.Count}стр.)";
    }

    private static bool TryMatchSingleAffix(
        CraftSingleAffixData s,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail)
    {
        detail = "";
        s.EnsureLinesFromLegacy();

        if (!ParsedItemCraftEvaluator.ItemClassMatches(item, expectedItemClass))
        {
            detail = $"Класс предмета в буфере: «{item.ItemClass}», ожидался «{expectedItemClass}».";
            return false;
        }

        var names = s.EffectiveAffixNames();
        if (names.Count == 0)
        {
            detail = "В условии не выбраны имена аффиксов.";
            return false;
        }

        // Multi-line mode: all Lines must match (AND), at least one name must satisfy (OR)
        if (s.Lines.Count > 0)
        {
            string? firstFailDetail = null;
            foreach (var name in names)
            {
                var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                    lib, expectedItemClass, s.AffixType, name, s.AffixTier)
                    ?? AffixCraftPatternBuilder.FindEntryByNameTypeAnyTier(lib, expectedItemClass, s.AffixType, name);
                if (entry is null)
                    continue;

                var allLinesMatch = true;
                var lineFailDetail = "";
                foreach (var line in s.Lines)
                {
                    var idx = CraftAffixCascadeHelper.FindStatIndexInEntry(entry, line.StatTemplate);
                    if (idx < 0)
                    {
                        allLinesMatch = false;
                        lineFailDetail = $"строка «{line.StatTemplate}» не найдена в записи «{name}».";
                        break;
                    }

                    var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, idx);
                    line.EnsureMinRollsSize(slots);
                    var mins = line.GetEffectiveMinRolls(slots).ToList();

                    if (!ParsedItemCraftEvaluator.TryGetRollValuesForNamedAffix(
                            item, expectedItemClass, s.AffixType, name, entry.AffixTier,
                            line.StatTemplate, slots, out var actual, out var expl))
                    {
                        allLinesMatch = false;
                        lineFailDetail = string.IsNullOrEmpty(expl)
                            ? $"нет аффикса «{name}» со строкой «{line.StatTemplate}»."
                            : expl;
                        break;
                    }

                    if (!ParsedItemCraftEvaluator.RollVectorMeetsMins(actual, mins, out _))
                    {
                        allLinesMatch = false;
                        lineFailDetail =
                            $"«{name}» строка «{line.StatTemplate}»: [{string.Join(", ", actual.Select(FormatNum))}] < [{string.Join(", ", mins.Select(FormatNum))}].";
                        break;
                    }
                }

                if (allLinesMatch)
                    return true;

                firstFailDetail ??= lineFailDetail;
            }

            // Variant 3: check for better tier of the same stat family on the item
            if (s.AffixTier > 1)
            {
                var familyRef = names
                    .Select(n => AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                                     lib, expectedItemClass, s.AffixType, n, s.AffixTier)
                                 ?? AffixCraftPatternBuilder.FindEntryByNameTypeAnyTier(lib, expectedItemClass, s.AffixType, n))
                    .FirstOrDefault(e => e is not null);
                if (familyRef is not null &&
                    ParsedItemCraftEvaluator.TryMatchBetterTierSameFamily(
                        item, expectedItemClass, s.AffixType, s.AffixTier, s.Lines, familyRef, lib, out var betterDetail))
                {
                    detail = betterDetail;
                    return true;
                }
            }

            detail = firstFailDetail is not null
                ? $"Ни одно имя не удовлетворяет всем строкам семейства. Пример: {firstFailDetail}"
                : $"На предмете нет ни одного из выбранных аффиксов ({string.Join(", ", names)}).";
            return false;
        }

        // Legacy single-stat fallback
        List<double>? firstActual = null;
        List<double>? firstMins = null;
        foreach (var name in names)
        {
            var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                lib, expectedItemClass, s.AffixType, name, s.AffixTier)
                ?? AffixCraftPatternBuilder.FindEntryByNameTypeAnyTier(lib, expectedItemClass, s.AffixType, name);
            if (entry is null)
                continue;
            var si = CraftAffixCascadeHelper.FindStatIndexInEntry(entry, s.StatTemplate);
            if (si < 0)
                continue;
            var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, si);
            s.EnsureMinRollsSize(slots);
            var mins = s.GetEffectiveMinRolls(slots).ToList();
            if (!ParsedItemCraftEvaluator.TryGetRollValuesForNamedAffix(
                    item, expectedItemClass, s.AffixType, name, entry.AffixTier,
                    s.StatTemplate, slots, out var actual, out _))
                continue;
            if (actual.Count != mins.Count)
                continue;
            if (ParsedItemCraftEvaluator.RollVectorMeetsMins(actual, mins, out _))
                return true;
            firstActual ??= actual;
            firstMins ??= mins;
        }

        if (firstActual is null)
        {
            detail =
                $"На предмете нет ни одного из выбранных аффиксов ({string.Join(", ", names)}) со строкой «{s.StatTemplate}».";
            return false;
        }

        var fa = firstActual;
        var fm = firstMins ?? new List<double>();
        detail =
            $"Ни одно имя из [{string.Join(", ", names)}] не достигает порога по «{s.StatTemplate}» (пример [{string.Join(", ", fa.Select(FormatNum))}], нужно ≥ [{string.Join(", ", fm.Select(FormatNum))}]).";
        return false;
    }

    private static string FormatNum(double v) =>
        v == Math.Truncate(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Краткое текстовое описание для главного окна и логов.</summary>
    public static string FormatSummary(CraftConditionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.ExpectedItemClass))
            return "(условие не задано)";
        if (plan.OrAlternatives.Count == 0)
            return $"Класс: {plan.ExpectedItemClass} — нет вариантов ИЛИ.";

        var lib = AffixLibrary.GetEntries();
        var sb = new StringBuilder();
        sb.Append("Класс: ").Append(plan.ExpectedItemClass).Append(". ИЛИ: ");
        var oi = 0;
        foreach (var alt in plan.OrAlternatives)
        {
            oi++;
            if (oi > 1)
                sb.Append(" | ");
            sb.Append('(');
            var first = true;
            foreach (var c in alt.Clauses)
            {
                if (!first)
                    sb.Append(" И ");
                first = false;
                if (c.Kind == CraftClauseKind.Single && c.Single is { } s)
                {
                    s.EnsureLinesFromLegacy();
                    var nm = string.Join("|", s.EffectiveAffixNames());
                    if (nm.Length > 40)
                        nm = nm[..37] + "…";
                    string statDesc;
                    if (s.Lines.Count > 0)
                    {
                        statDesc = string.Join(" / ", s.Lines.Select(l =>
                        {
                            var st = l.StatTemplate.Length > 22 ? l.StatTemplate[..19] + "…" : l.StatTemplate;
                            var m = FormatNum(l.GetEffectiveMinRolls(1)[0]);
                            return $"{st}≥{m}";
                        }));
                    }
                    else
                    {
                        var st = s.StatTemplate.Length > 28 ? s.StatTemplate[..25] + "…" : s.StatTemplate;
                        var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(plan.ExpectedItemClass, s.AffixType, s.StatTemplate, lib);
                        var minStr = string.Join(", ", s.GetEffectiveMinRolls(slots).Select(FormatNum));
                        statDesc = $"{st}≥{minStr}";
                    }
                    sb.Append($"{s.AffixType} «{nm}» T{s.AffixTier}: {statDesc}");
                }
                else if (c.Kind == CraftClauseKind.Sum && c.Sum is { } m)
                    sb.Append($"Σ≥{FormatNum(m.MinSum)}");
                else if (c.Kind == CraftClauseKind.Count && c.Count is { } k)
                {
                    sb.Append($"COUNT≥{k.MinMatchCount}/{k.Members.Count}аф.");
                    if (k.Members.Count > 0)
                    {
                        var names = k.Members
                            .Select(m =>
                            {
                                var nm = string.Join("|", m.EffectiveWholeAffixNames());
                                return nm.Length > 16 ? nm[..13] + "…" : nm;
                            })
                            .ToList();
                        sb.Append($" [{string.Join(", ", names)}]");
                    }
                }
                else if (c.Kind == CraftClauseKind.WholeModifier && c.Whole is { } w)
                {
                    var wn = string.Join("|", w.EffectiveWholeAffixNames());
                    if (wn.Length > 36)
                        wn = wn[..33] + "…";
                    sb.Append($"целый «{wn}» T{w.AffixTier} ({w.Lines.Count}стр.)");
                }
                else
                    sb.Append('?');
            }

            sb.Append(')');
        }

        return sb.ToString();
    }
}
