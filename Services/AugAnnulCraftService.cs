using System.Runtime.InteropServices;
using System.Windows;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Крафт по Magic-предмету: Orb of Augmentation / Orb of Annulment с проверкой условия через ItemParser.</summary>
public sealed class AugAnnulCraftService : IAugAnnulCraftService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public int HoverSettleBeforeClipboardMs { get; set; } = 120;

    public bool TraceInputToLog { get; set; }
    public Func<string, Task>? StepConfirmAsync { get; set; }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0)
            return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        if (delta <= 0)
            return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static Task DelayJitterAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    public Task ClearClipboardAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearClipboardSafe).Task;

    private static void ClearClipboardSafe()
    {
        try { System.Windows.Clipboard.Clear(); } catch { /* ignore */ }
    }

    private void LogMove(IProgress<string>? log, string label, int x, int y)
    {
        if (TraceInputToLog)
            log?.Report($"[Ввод] {label}: SetCursorPos({x},{y})");

        if (!Win32Input.MoveTo(x, y))
        {
            var err = Marshal.GetLastWin32Error();
            log?.Report($"[Ввод] ОШИБКА SetCursorPos ({x},{y}): Win32 код {err}");
        }
    }

    private void LogMouse(IProgress<string>? log, string label)
    {
        if (!TraceInputToLog)
            return;
        if (Win32Input.TryGetCursorPos(out var x, out var y))
            log?.Report($"[Ввод] {label} @ позиция курсора ({x},{y})");
        else
            log?.Report($"[Ввод] {label}");
    }

    private void LogKey(IProgress<string>? log, string label)
    {
        if (TraceInputToLog)
            log?.Report($"[Ввод] {label}");
    }

    private async Task StepPauseIfNeeded(string description, CancellationToken cancellationToken)
    {
        if (StepConfirmAsync is null)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        await StepConfirmAsync(description).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    private static (bool WantPrefix, bool WantSuffix) GetWantedAffixTypes(CraftConditionPlan plan)
    {
        var wantPrefix = false;
        var wantSuffix = false;

        void Mark(string affixType)
        {
            var t = (affixType ?? "").Trim();
            if (string.Equals(t, "Prefix Modifier", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase))
                wantPrefix = true;
            if (string.Equals(t, "Suffix Modifier", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase))
                wantSuffix = true;
        }

        foreach (var or in plan.OrAlternatives)
        foreach (var c in or.Clauses)
        {
            switch (c.Kind)
            {
                case CraftClauseKind.Single:
                    if (c.Single != null) Mark(c.Single.AffixType);
                    break;
                case CraftClauseKind.Sum:
                    if (c.Sum?.Parts != null)
                        foreach (var p in c.Sum.Parts)
                            Mark(p.AffixType);
                    break;
                case CraftClauseKind.Count:
                    if (c.Count?.Members != null)
                        foreach (var m in c.Count.Members)
                            Mark(m.AffixType);
                    break;
                case CraftClauseKind.WholeModifier:
                    if (c.Whole != null)
                        Mark(c.Whole.AffixType);
                    break;
            }
        }

        return (wantPrefix, wantSuffix);
    }

    private async Task<string> ReadClipboardForItemAsync(ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string tag)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        await ClearClipboardAsync().ConfigureAwait(false);

        var (hx, hy) = itemArea.GetInteriorPoint(1);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        LogMove(log, "MoveTo предмет (центр области) перед Ctrl+Alt+C", hx, hy);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        await Task.Delay(HoverSettleBeforeClipboardMs, ct).ConfigureAwait(false);

        try
        {
            LogKey(log, "Ctrl+Alt+C (предмет)");
            Win32Input.SendCtrlAltC();
            await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
            var text = await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
            SessionLogger.InfoClipboard(tag, text);
            return text;
        }
        finally
        {
            Win32Input.ReleaseCtrlAlt();
        }
    }

    private async Task<string> ReadClipboardForItemWithRetryAsync(ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string tag)
    {
        var first = await ReadClipboardForItemAsync(itemArea, log, ct, tag).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        log?.Report($"{tag}: буфер пуст, повтор Ctrl+Alt+C (retry) …");
        await Task.Delay(1000, ct).ConfigureAwait(false);
        var second = await ReadClipboardForItemAsync(itemArea, log, ct, tag + " (retry)").ConfigureAwait(false);
        return second;
    }

    public async Task<CraftPrecheckResult> PrecheckAsync(ScreenRect itemArea, CraftConditionPlan plan, IProgress<string>? log, CancellationToken ct)
    {
        log?.Report("Предпроверка (Ауг+Аннул): Ctrl+Alt+C…");
        var clip = await ReadClipboardForItemWithRetryAsync(itemArea, log, ct, "Ауг+Аннул: предпроверка").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(clip))
            return new CraftPrecheckResult(CraftPrecheckOutcome.EmptyCell, "Пустая ячейка", "После Ctrl+Alt+C буфер пуст — вероятно, в ячейке нет предмета.", null, clip);

        var parsed = ItemParser.Parse(clip);
        if (parsed is not { IsValid: true })
            return new CraftPrecheckResult(CraftPrecheckOutcome.Failed, "Предмет", "Не удалось разобрать текст предмета из буфера.", null, clip);

        if (!ParsedItemCraftEvaluator.ItemClassMatches(parsed, plan.ExpectedItemClass))
            return new CraftPrecheckResult(CraftPrecheckOutcome.Failed, "Класс предмета", $"В буфере класс «{parsed.ItemClass}», а в условии указан «{plan.ExpectedItemClass}».", parsed, clip);

        if (!string.Equals(parsed.Rarity?.Trim(), "Magic", StringComparison.OrdinalIgnoreCase))
            return new CraftPrecheckResult(CraftPrecheckOutcome.NonMagicCell, "Редкость", $"Для режима «Ауг+Аннул» предмет должен быть Magic, а сейчас: {parsed.Rarity}.", parsed, clip);

        if (CraftConditionEvaluator.TryEvaluate(plan, parsed, out _))
            return new CraftPrecheckResult(CraftPrecheckOutcome.AlreadySatisfied, "Условие уже выполнено", "Предмет уже удовлетворяет условию остановки крафта.", parsed, clip);

        return new CraftPrecheckResult(CraftPrecheckOutcome.Ready, "", "", parsed, clip);
    }

    private async Task ApplyOrbAsync(ScreenRect orbArea, ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string label)
    {
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        var (ox, oy) = orbArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        LogMove(log, $"MoveTo {label} (случайная точка)", ox, oy);
        await StepPauseIfNeeded($"{label}: курсор на орбе ({ox},{oy})", ct).ConfigureAwait(false);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogMouse(log, $"ПКМ ({label})");
        Win32Input.ClickRight();
        await StepPauseIfNeeded($"{label}: ПКМ по орбу", ct).ConfigureAwait(false);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        var (ix, iy) = itemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        LogMove(log, $"MoveTo предмет (ЛКМ) после {label}", ix, iy);
        await StepPauseIfNeeded($"{label}: курсор на предмете ({ix},{iy})", ct).ConfigureAwait(false);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogMouse(log, "ЛКМ (предмет)");
        Win32Input.ClickLeft();
        await StepPauseIfNeeded($"{label}: ЛКМ по предмету", ct).ConfigureAwait(false);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
    }

    public async Task<CraftResult> RunAsync(
        ScreenRect augOrbArea,
        ScreenRect annulOrbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        ParsedItem initialParsedItem,
        string? initialClipboardText,
        int segmentMaxOperations,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog = null)
    {
        if (segmentMaxOperations < 1 || globalTotal < 1)
            return CraftResult.Failed();

        var pattern = conditionSummary.Trim();
        if (pattern.Length == 0)
            pattern = "(разбор предмета, ItemParser)";

        var (wantPrefix, wantSuffix) = GetWantedAffixTypes(plan);
        var singleType = wantPrefix ^ wantSuffix
            ? (wantPrefix ? "Prefix Modifier" : "Suffix Modifier")
            : null;

        var current = initialParsedItem;
        var currentClipboard = initialClipboardText ?? "";
        var augStepsConsumed = 0;
        var annulOnlySafety = 0;

        // segmentMaxOperations/globalTotal здесь трактуются как "лимит Aug-шагов" (Annul-only не вычитает N).
        while (augStepsConsumed < segmentMaxOperations)
        {
            var displayAttempt = globalAttemptOffset + (augStepsConsumed + 1);
            log?.Report($"Операция {displayAttempt} / {globalTotal} (Ауг+Аннул) …");

            try
            {
                ct.ThrowIfCancellationRequested();
                _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);

                // ВАЖНО: проверку условия делаем в начале шага, до орбов.
                // Ctrl+Alt+C before делается только при входе в ячейку (PrecheckAsync),
                // дальше используем актуальный ParsedItem из прошлой попытки (после Ctrl+Alt+C after).

                var preMatch = CraftConditionEvaluator.TryEvaluate(plan, current, out var preExplanation);
                craftLog?.WriteComparison(
                    displayAttempt,
                    globalTotal,
                    currentClipboard,
                    pattern,
                    preMatch,
                    "[проверка перед орбами] " + preExplanation);
                log?.Report($"Проверка (попытка {displayAttempt}): {preExplanation}");

                if (preMatch)
                {
                    log?.Report("Условие уже выполнено — орбы не применяются, переходим к следующей ячейке.");
                    return CraftResult.Found(0, currentClipboard);
                }

                var prefixCount = current.Affixes.Count(a => string.Equals(a.Type, "Prefix Modifier", StringComparison.OrdinalIgnoreCase));
                var suffixCount = current.Affixes.Count(a => string.Equals(a.Type, "Suffix Modifier", StringComparison.OrdinalIgnoreCase));
                var totalAffixes = prefixCount + suffixCount;

                // ШАГ (N) считается только по применению Orb of Augmentation.
                // Решение об Annul принимается ПЕРЕД Aug.
                var didAugThisCycle = false;
                if (singleType != null)
                {
                    // Если план требует только Prefix или только Suffix:
                    // - если нужного типа СЛОТ пустой → делаем Aug (он добавит недостающий тип)
                    // - если слот занят (на предмете уже есть Prefix/Suffix, но он "не тот") → делаем только Annul,
                    //   чтобы освободить слот; Aug в этом цикле НЕ делаем.
                    var wantPrefixSlot = string.Equals(singleType, "Prefix Modifier", StringComparison.OrdinalIgnoreCase);
                    var slotEmpty = wantPrefixSlot ? prefixCount == 0 : suffixCount == 0;

                    if (slotEmpty)
                    {
                        await ApplyOrbAsync(augOrbArea, itemArea, log, ct, "Orb of Augmentation").ConfigureAwait(false);
                        didAugThisCycle = true;
                        augStepsConsumed++;
                        annulOnlySafety = 0;
                    }
                    else
                    {
                        await ApplyOrbAsync(annulOrbArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                        annulOnlySafety++;
                        if (annulOnlySafety > 10)
                        {
                            craftLog?.WriteValidationError("Слишком много Annul подряд без Aug (защита от бесконечного цикла).");
                            return CraftResult.Failed(augStepsConsumed);
                        }
                    }
                }
                else
                {
                    // Требуются и Prefix, и Suffix: нам нужно иметь возможность получить второй аффикс.
                    if (totalAffixes >= 2)
                        await ApplyOrbAsync(annulOrbArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);

                    await ApplyOrbAsync(augOrbArea, itemArea, log, ct, "Orb of Augmentation").ConfigureAwait(false);
                    didAugThisCycle = true;
                    augStepsConsumed++;
                    annulOnlySafety = 0;
                }

                // После действия(й) — обновляем состояние (для следующей итерации/шагов).
                var clip = await ReadClipboardForItemWithRetryAsync(
                    itemArea,
                    log,
                    ct,
                    $"Ауг+Аннул: after, попытка {displayAttempt}/{globalTotal}" + (didAugThisCycle ? " (после Aug)" : " (после Annul)")).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clip))
                    return CraftResult.Empty(augStepsConsumed);

                var parsed = ItemParser.Parse(clip);
                if (parsed is not { IsValid: true })
                {
                    craftLog?.WriteValidationError("ItemParser: не удалось разобрать текст после применения сфер.");
                    return CraftResult.Failed(augStepsConsumed);
                }

                currentClipboard = clip;
                current = parsed;

                // Финальная проверка после применения (Annul и/или Aug) — чтобы не пропустить успех на последнем шаге.
                var postMatch = CraftConditionEvaluator.TryEvaluate(plan, current, out var postExplanation);
                if (postMatch)
                {
                    craftLog?.WriteComparison(
                        displayAttempt,
                        globalTotal,
                        currentClipboard,
                        pattern,
                        true,
                        "[проверка после орбов] " + postExplanation);
                    log?.Report("Условие выполнено после применения сфер — переходим к следующей ячейке.");
                    return CraftResult.Found(augStepsConsumed, currentClipboard);
                }
            }
            catch (OperationCanceledException)
            {
                Win32Input.ReleaseCtrlAlt();
                log?.Report("Остановлено пользователем.");
                return CraftResult.Stopped(augStepsConsumed);
            }
            catch (Exception ex)
            {
                Win32Input.ReleaseCtrlAlt();
                craftLog?.WriteValidationError("Исключение: " + ex.Message);
                log?.Report("Ошибка: " + ex.Message);
                return CraftResult.Failed(augStepsConsumed);
            }
            finally
            {
                Win32Input.ReleaseCtrlAlt();
            }
        }

        return CraftResult.LimitReached(augStepsConsumed);
    }
}

