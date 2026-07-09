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
    /// 3. Если у шага n-1 Action == CheckItem или OmenActivation и его EntryCondition тоже совпадает → вернуть n-1.
    ///    Обоснование: эти шаги не меняют состояние предмета, поэтому их условие входа идентично следующему шагу;
    ///    предпочитаем более ранний шаг при неопределённости.
    /// 4. Иначе вернуть n.
    /// 5. Если ни один шаг не совпал → null.
    /// </summary>
    public static DetectResult Detect(CraftPipeline pipeline, ParsedItem item) =>
        DetectCore(pipeline, item, static (plan, parsedItem, out explanation) =>
            CraftConditionEvaluator.TryEvaluate(plan, parsedItem, out explanation));

    internal static DetectResult DetectCore(CraftPipeline pipeline, ParsedItem item, EvaluateDelegate eval)
    {
        var steps = pipeline.Steps;
        var sb = new StringBuilder();

        for (int i = steps.Count - 1; i >= 0; i--)
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
            if (i > 0
                && steps[i - 1].EntryCondition != null
                && IsStatePreservingAction(steps[i - 1].Action)
                && eval(steps[i - 1].EntryCondition!, item, out var prevDetail))
            {
                var prevActionLabel = steps[i - 1].Action == PipelineAction.CheckItem ? "CheckItem" : "OmenActivation";
                sb.AppendLine($"[{i - 1}] «{steps[i - 1].Name}» ({prevActionLabel} n-1): тоже совпал — {prevDetail}");
                sb.AppendLine($"→ Шаг {i - 1} «{steps[i - 1].Name}» (правило n-1)");
                return new DetectResult(i - 1, sb.ToString().TrimEnd());
            }

            sb.AppendLine($"→ Шаг {i} «{step.Name}»");
            return new DetectResult(i, sb.ToString().TrimEnd());
        }

        sb.AppendLine("Предмет не распознан ни на одном шаге пайплайна");
        return new DetectResult(null, sb.ToString().TrimEnd());
    }

    /// <summary>Шаги, которые не меняют состояние предмета и могут быть пропущены правилом n-1.</summary>
    private static bool IsStatePreservingAction(PipelineAction action) =>
        action is PipelineAction.CheckItem or PipelineAction.OmenActivation;
}
