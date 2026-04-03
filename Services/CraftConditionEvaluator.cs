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
                    if (c.Single is null ||
                        string.IsNullOrWhiteSpace(c.Single.AffixType) ||
                        string.IsNullOrWhiteSpace(c.Single.StatTemplate))
                    {
                        error = $"В варианте {altIndex}, клоз {cIndex}: выберите тип модификатора и строку стата.";
                        return false;
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
                        error = $"В варианте {altIndex}, клоз {cIndex}: в наборе COUNT добавьте хотя бы одну строку.";
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
                        if (string.IsNullOrWhiteSpace(m.AffixType) || string.IsNullOrWhiteSpace(m.StatTemplate))
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}, строка {mi + 1} набора: укажите тип и стата.";
                            return false;
                        }
                    }
                }
                else if (c.Kind == CraftClauseKind.WholeModifier)
                {
                    if (c.Whole is null ||
                        string.IsNullOrWhiteSpace(c.Whole.AffixType) ||
                        string.IsNullOrWhiteSpace(c.Whole.AffixName) ||
                        c.Whole.Lines.Count == 0)
                    {
                        error =
                            $"В варианте {altIndex}, клоз {cIndex}: укажите тип, имя модификатора и хотя бы одну строку стата.";
                        return false;
                    }

                    var lib = AffixLibrary.GetEntries();
                    var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                        lib,
                        plan.ExpectedItemClass,
                        c.Whole.AffixType,
                        c.Whole.AffixName,
                        c.Whole.AffixTier);
                    if (entry is null)
                    {
                        error =
                            $"В варианте {altIndex}, клоз {cIndex}: в библиотеке нет «{c.Whole.AffixName}» ({c.Whole.AffixType}, T{c.Whole.AffixTier}) для класса «{plan.ExpectedItemClass}».";
                        return false;
                    }

                    for (var li = 0; li < c.Whole.Lines.Count; li++)
                    {
                        var line = c.Whole.Lines[li];
                        if (string.IsNullOrWhiteSpace(line.StatTemplate))
                        {
                            error = $"В варианте {altIndex}, клоз {cIndex}: пустая строка стата #{li + 1}.";
                            return false;
                        }

                        if (AffixCraftPatternBuilder.GetStatIndex(entry, line.StatTemplate) < 0)
                        {
                            error =
                                $"В варианте {altIndex}, клоз {cIndex}: строка «{line.StatTemplate}» не входит в запись «{c.Whole.AffixName}» в библиотеке.";
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
                var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(
                    expectedItemClass,
                    s.AffixType,
                    s.StatTemplate,
                    lib);
                var mins = s.GetEffectiveMinRolls(slots);
                sb.Append($"[{s.AffixType}]≥{string.Join("/", mins.Select(FormatNum))}; ");
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

                var cnt = clause.Count;
                var matched = 0;
                foreach (var m in cnt.Members)
                {
                    if (TryMatchSingleAffix(m, item, expectedItemClass, lib, out _))
                        matched++;
                }

                if (matched < cnt.MinMatchCount)
                {
                    detail =
                        $"набор COUNT: выполнено {matched} из {cnt.Members.Count} строк, требуется ≥ {cnt.MinMatchCount}.";
                    return false;
                }

                sb.Append($"COUNT≥{cnt.MinMatchCount}({matched}/{cnt.Members.Count}); ");
            }
            else if (clause.Kind == CraftClauseKind.WholeModifier)
            {
                if (clause.Whole is null)
                {
                    detail = "внутренняя ошибка: WholeModifier.";
                    return false;
                }

                if (!ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(
                        clause.Whole,
                        item,
                        expectedItemClass,
                        lib,
                        out detail))
                    return false;

                sb.Append($"[целый:{clause.Whole.AffixName}] ");
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

    /// <summary>Одна строка как в клозе Single: тип, стата, пороги переката.</summary>
    private static bool TryMatchSingleAffix(
        CraftSingleAffixData s,
        ParsedItem item,
        string expectedItemClass,
        IReadOnlyList<AffixLibraryEntry> lib,
        out string detail)
    {
        detail = "";
        // В условиях крафта НЕ привязываемся к affixName/affixTier — только (тип + строка стата) и пороги.
        if (!ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStatNoLibrary(
                item,
                expectedItemClass,
                s.AffixType,
                s.StatTemplate,
                out var actual,
                out var expl))
        {
            detail = expl.Length > 0 ? expl : "одиночный аффикс не найден.";
            return false;
        }

        var slots = Math.Max(1, actual.Count);
        s.EnsureMinRollsSize(slots);
        var mins = s.GetEffectiveMinRolls(slots).ToList();

        if (!ParsedItemCraftEvaluator.RollVectorMeetsMins(actual, mins, out var failIdx))
        {
            detail =
                $"({s.AffixType}, стата): слот {failIdx + 1} — перекат ниже порога (есть [{string.Join(", ", actual.Select(FormatNum))}], нужно ≥ [{string.Join(", ", mins.Select(FormatNum))}]).";
            return false;
        }

        return true;
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
                    var st = s.StatTemplate.Length > 28 ? s.StatTemplate[..25] + "…" : s.StatTemplate;
                    var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(
                        plan.ExpectedItemClass,
                        s.AffixType,
                        s.StatTemplate,
                        lib);
                    var mins = s.GetEffectiveMinRolls(slots);
                    var minStr = string.Join(", ", mins.Select(FormatNum));
                    sb.Append($"{s.AffixType}: {st}≥{minStr}");
                }
                else if (c.Kind == CraftClauseKind.Sum && c.Sum is { } m)
                    sb.Append($"Σ≥{FormatNum(m.MinSum)}");
                else if (c.Kind == CraftClauseKind.Count && c.Count is { } k)
                    sb.Append(
                        $"COUNT≥{k.MinMatchCount}/{k.Members.Count}стр.");
                else if (c.Kind == CraftClauseKind.WholeModifier && c.Whole is { } w)
                    sb.Append(
                        $"целый «{w.AffixName}» T{w.AffixTier} ({w.Lines.Count}стр.)");
                else
                    sb.Append('?');
            }

            sb.Append(')');
        }

        return sb.ToString();
    }
}
