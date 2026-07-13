using System.Text;

namespace GameHelper.Services;

/// <summary>
/// Определяет, на каком шаге пайплайна находится предмет по его текущему набору модов.
/// </summary>
public static class PipelineStepDetector
{
    /// <param name="StepIndex">Определённый шаг (null — не распознан).</param>
    /// <param name="Explanation">Человекочитаемое объяснение для лог-вывода.</param>
    public sealed record DetectResult(int? StepIndex, string Explanation);

    internal delegate bool EvaluateDelegate(CraftConditionPlan plan, ParsedItem item, out string explanation);

    /// <summary>
    /// Определяет шаг пайплайна по текущему состоянию предмета.
    ///
    /// Алгоритм:
    /// 1. Сканировать шаги с конца к началу.
    /// 2. Найти последний (наибольший индекс) шаг, чей EntryCondition совпадает с предметом.
    /// 3. Правило n-1: если шаг n-1 не меняет состояние предмета (CheckItem / OmenActivation)
    ///    и его EntryCondition тоже совпадает → предпочитаем более ранний шаг.
    /// 4. Правило TravelToLocation: перед принятием кандидата N проверяем последний checkpoint
    ///    (шаг с EntryCondition) до каждой TravelToLocation-вехи между 0 и N.
    ///    Если checkpoint не совпадает → предмет не прошёл эту фазу, ищем стадию ниже вехи.
    /// 5. Если ни один шаг не совпал → null.
    /// </summary>
    public static DetectResult Detect(CraftPipeline pipeline, ParsedItem item) =>
        DetectCore(pipeline, item, static (plan, parsedItem, out explanation) =>
            CraftConditionEvaluator.TryEvaluate(plan, parsedItem, out explanation));

    internal static DetectResult DetectCore(CraftPipeline pipeline, ParsedItem item, EvaluateDelegate eval)
    {
        var steps = pipeline.Steps;
        var sb = new StringBuilder();
        return DetectInRange(steps, item, eval, 0, steps.Count - 1, sb);
    }

    private static DetectResult DetectInRange(
        IList<CraftPipelineStep> steps,
        ParsedItem item,
        EvaluateDelegate eval,
        int fromInclusive,
        int toInclusive,
        StringBuilder sb)
    {
        for (int i = toInclusive; i >= fromInclusive; i--)
        {
            var step = steps[i];
            if (step.EntryCondition == null)
                continue;

            if (!eval(step.EntryCondition, item, out var detail))
            {
                sb.AppendLine($"[{i}] «{step.Name}»: не совпал — {detail}");
                continue;
            }

            sb.AppendLine($"[{i}] «{step.Name}»: совпал — {detail}");

            // Правило n-1: если предыдущий шаг не меняет состояние предмета (CheckItem / OmenActivation)
            // и его условие тоже совпадает — предпочитаем более ранний шаг.
            var candidate = i;
            if (i > fromInclusive
                && steps[i - 1].EntryCondition != null
                && IsStatePreservingAction(steps[i - 1].Action)
                && eval(steps[i - 1].EntryCondition!, item, out var prevDetail))
            {
                var prevActionLabel = steps[i - 1].Action == PipelineAction.CheckItem ? "CheckItem" : "OmenActivation";
                sb.AppendLine($"[{i - 1}] «{steps[i - 1].Name}» ({prevActionLabel} n-1): тоже совпал — {prevDetail}");
                candidate = i - 1;
            }

            // Правило TravelToLocation: кандидат N должен находиться в той же «фазе», что и предмет.
            // Для каждой TravelToLocation-вехи T (fromInclusive ≤ T < candidate) находим последний
            // checkpoint (шаг с EntryCondition) перед T. Если он не совпадает — предмет не прошёл
            // эту фазу и реально находится до вехи T.
            for (int t = candidate - 1; t >= fromInclusive; t--)
            {
                if (steps[t].Action != PipelineAction.TravelToLocation)
                    continue;

                // Найти последний checkpoint до t
                int lastCheckpoint = -1;
                for (int c = t - 1; c >= fromInclusive; c--)
                {
                    if (steps[c].EntryCondition != null)
                    {
                        lastCheckpoint = c;
                        break;
                    }
                }

                if (lastCheckpoint < 0)
                    continue; // нет checkpoint-а до вехи — не можем опровергнуть

                if (!eval(steps[lastCheckpoint].EntryCondition!, item, out var cpDetail))
                {
                    // Предмет не прошёл фазу до вехи T → ищем стадию в [fromInclusive .. t-1]
                    sb.AppendLine(
                        $"[Travel-check] Веха [{t}] «{steps[t].Name}»: последний checkpoint [{lastCheckpoint}] «{steps[lastCheckpoint].Name}» не совпал — {cpDetail}");
                    sb.AppendLine($"→ предмет в фазе до вехи [{t}], сужаю диапазон [{fromInclusive}..{t - 1}]");
                    return DetectInRange(steps, item, eval, fromInclusive, t - 1, sb);
                }
            }

            var label = candidate < i ? $"(правило n-1 от [{i}])" : "";
            sb.AppendLine($"→ Шаг {candidate} «{steps[candidate].Name}» {label}".TrimEnd());
            return new DetectResult(candidate, sb.ToString().TrimEnd());
        }

        sb.AppendLine($"Предмет не распознан ни на одном шаге [{fromInclusive}..{toInclusive}]");
        return new DetectResult(null, sb.ToString().TrimEnd());
    }

    /// <summary>Шаги, которые не меняют состояние предмета и могут быть пропущены правилом n-1.</summary>
    private static bool IsStatePreservingAction(PipelineAction action) =>
        action is PipelineAction.CheckItem or PipelineAction.OmenActivation;
}
