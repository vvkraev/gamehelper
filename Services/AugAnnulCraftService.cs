using System.Runtime.InteropServices;
using System.Windows;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Крафт по Magic-предмету: Orb of Augmentation / Orb of Annulment с проверкой условия через ItemParser.</summary>
public sealed class AugAnnulCraftService : IAugAnnulCraftService
{
    private const double DelayJitterFraction = 0.30;

    /// <summary>Задержка (мс) после каждого перемещения мыши и клика (ЛКМ/ПКМ).</summary>
    public int MouseActionDelayMs { get; set; } = 80;

    /// <summary>Ожидание после Ctrl+Alt+C перед чтением буфера.</summary>
    public int ClipboardDelayMs { get; set; } = 220;

    /// <summary>Пауза после наведения на ячейку перед Ctrl+Alt+C — игра успевает обновить цель копирования при смене ячейки.</summary>
    public int HoverSettleBeforeClipboardMs { get; set; } = 120;

    /// <summary>В лог попадут шаги: MoveTo, клики. Включайте при отладке.</summary>
    public bool TraceInputToLog { get; set; }

    /// <summary>Если задано, после каждого шага ввода показывается модальное окно; закрытие без «Продолжить» — отмена через <see cref="CancellationToken"/>.</summary>
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

    /// <summary>Очищает буфер обмена на UI-потоке, чтобы пустая ячейка не маскировалась старым текстом.</summary>
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

    /// <summary>Совпадает ли тип аффикса на предмете с искомым типом (prefix/suffix).</summary>
    private static bool IsTypeMatch(string? affixType, bool isPrefix) =>
        isPrefix
            ? string.Equals(affixType, "Prefix Modifier", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(affixType, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase)
            : string.Equals(affixType, "Suffix Modifier", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(affixType, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Проверяет, является ли аффикс нужного типа (prefix/suffix) «правильным» по условию крафта.
    /// Строит частичный план только из клозов, относящихся к данному типу, и вычисляет его против предмета.
    /// </summary>
    private static bool IsAffixRight(ParsedItem item, bool isPrefix, CraftConditionPlan plan)
    {
        foreach (var alt in plan.OrAlternatives)
        {
            var relevant = FilterClausesForType(alt.Clauses, isPrefix);
            if (relevant.Count == 0)
                continue;

            var partialPlan = new CraftConditionPlan
            {
                ExpectedItemClass = plan.ExpectedItemClass,
                OrAlternatives = [new CraftAndGroup { Clauses = relevant }]
            };
            if (CraftConditionEvaluator.TryEvaluate(partialPlan, item, out _))
                return true;
        }

        return false;
    }

    private static List<CraftClause> FilterClausesForType(List<CraftClause> clauses, bool isPrefix)
    {
        var result = new List<CraftClause>();
        foreach (var clause in clauses)
        {
            var filtered = FilterClauseForType(clause, isPrefix);
            if (filtered != null)
                result.Add(filtered);
        }

        return result;
    }

    private static CraftClause? FilterClauseForType(CraftClause clause, bool isPrefix)
    {
        switch (clause.Kind)
        {
            case CraftClauseKind.Single:
                if (clause.Single == null)
                    return null;
                return IsTypeMatch(clause.Single.AffixType, isPrefix) ? clause : null;

            case CraftClauseKind.WholeModifier:
                if (clause.Whole == null)
                    return null;
                return IsTypeMatch(clause.Whole.AffixType, isPrefix) ? clause : null;

            case CraftClauseKind.Sum:
                // Sum может охватывать оба типа; пропускаем для упрощения
                return null;

            case CraftClauseKind.Count:
                if (clause.Count == null)
                    return null;
                var matching = clause.Count.Members
                    .Where(m => IsTypeMatch(m.AffixType, isPrefix))
                    .ToList();
                if (matching.Count == 0)
                    return null;
                // Для частичной оценки: достаточно совпадения хотя бы одного члена нужного типа
                return new CraftClause
                {
                    Kind = CraftClauseKind.Count,
                    Count = new CraftCountAffixData { Members = matching, MinMatchCount = 1 }
                };

            default:
                return null;
        }
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

    /// <summary>Читает предмет из буфера и парсит; возвращает null если буфер пуст.</summary>
    private async Task<(ParsedItem? Parsed, string Clip)> ReadAndParseAsync(
        ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string tag)
    {
        var clip = await ReadClipboardForItemWithRetryAsync(itemArea, log, ct, tag).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(clip))
            return (null, clip);
        var parsed = ItemParser.Parse(clip);
        if (parsed is { IsValid: true })
            log?.Report($"  Состояние: {FormatItemSummary(parsed)}");
        return (parsed, clip);
    }

    private static string FormatItemSummary(ParsedItem item)
    {
        static string AffixLabel(AffixInfo a)
        {
            var parts = a.EffectDetails.Count > 0
                ? a.EffectDetails.Take(2).Select(e =>
                    e.RolledValue != null ? $"{e.RolledValue} {e.StatText}" : e.StatText)
                : a.Effects.Take(2).Select(e => e);
            var effect = string.Join("; ", parts);
            return string.IsNullOrEmpty(effect) ? $"{a.Name} T{a.Tier}" : $"{a.Name} T{a.Tier} ({effect})";
        }

        var p = string.Join(", ", item.Affixes.Where(a => IsTypeMatch(a.Type, true)).Select(AffixLabel));
        var s = string.Join(", ", item.Affixes.Where(a => IsTypeMatch(a.Type, false)).Select(AffixLabel));
        return $"P: {(p.Length > 0 ? p : "—")}  |  S: {(s.Length > 0 ? s : "—")}";
    }

    /// <summary>
    /// Читает предмет из буфера, проверяет класс и редкость (Magic), сравнивает с условием крафта.
    /// <see cref="CraftPrecheckOutcome.Ready"/> — запускать <see cref="RunAsync"/>; остальные исходы — ячейку пропустить.
    /// </summary>
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

    /// <summary>
    /// Основной цикл Aug+Annul: Aug если нужный тип аффикса отсутствует, Annul если нужный тип есть но условие не выполнено,
    /// повторяет до выполнения условия, исчерпания <paramref name="segmentMaxOperations"/> или отмены токена.
    /// </summary>
    /// <param name="segmentMaxOperations">Сколько попыток разрешено в этом вызове (остаток общего бюджета).</param>
    /// <param name="globalTotal">Общий N сессии — для подписей «попытка k / N».</param>
    /// <param name="globalAttemptOffset">Сколько попыток уже сделано ранее в сессии (по предыдущим ячейкам).</param>
    /// <returns>Итог и число израсходованных попыток.</returns>
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

        // segmentMaxOperations трактуется как лимит Aug-шагов; Annul-only циклы не вычитают из лимита.
        while (augStepsConsumed < segmentMaxOperations)
        {
            var displayAttempt = globalAttemptOffset + (augStepsConsumed + 1);
            log?.Report($"Операция {displayAttempt} / {globalTotal} (Ауг+Аннул) …");

            try
            {
                ct.ThrowIfCancellationRequested();
                _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);

                var preMatch = CraftConditionEvaluator.TryEvaluate(plan, current, out var preExplanation);
                log?.Report($"Проверка (попытка {displayAttempt}): {preExplanation}");

                if (preMatch)
                {
                    craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, true, "[до орбов] " + preExplanation);
                    log?.Report("Условие уже выполнено — орбы не применяются, переходим к следующей ячейке.");
                    return CraftResult.Found(augStepsConsumed, currentClipboard);
                }

                var prefixCount = current.Affixes.Count(a => IsTypeMatch(a.Type, isPrefix: true));
                var suffixCount = current.Affixes.Count(a => IsTypeMatch(a.Type, isPrefix: false));

                if (singleType != null)
                {
                    // Режим одного типа: Aug если слот пустой, иначе Annul чтобы освободить слот.
                    var wantPrefixSlot = string.Equals(singleType, "Prefix Modifier", StringComparison.OrdinalIgnoreCase);
                    var slotEmpty = wantPrefixSlot ? prefixCount == 0 : suffixCount == 0;

                    if (slotEmpty)
                    {
                        craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, false, "[до Aug] " + preExplanation);
                        await ApplyOrbAsync(augOrbArea, itemArea, log, ct, "Orb of Augmentation").ConfigureAwait(false);
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

                    var (parsedSingle, clipSingle) = await ReadAndParseAsync(
                        itemArea, log, ct,
                        $"Ауг+Аннул: after, попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);

                    if (parsedSingle is null)
                        return CraftResult.Empty(augStepsConsumed);
                    if (!parsedSingle.IsValid)
                    {
                        craftLog?.WriteValidationError("ItemParser: не удалось разобрать текст после применения сфер.");
                        return CraftResult.Failed(augStepsConsumed);
                    }

                    currentClipboard = clipSingle;
                    current = parsedSingle;

                    if (CraftConditionEvaluator.TryEvaluate(plan, current, out var postExplSingle))
                    {
                        craftLog?.WriteComparison(
                            displayAttempt, globalTotal, currentClipboard, pattern, true,
                            "[после орбов] " + postExplSingle);
                        log?.Report("Условие выполнено — переходим к следующей ячейке.");
                        return CraftResult.Found(augStepsConsumed, currentClipboard);
                    }
                    log?.Report($"  Условие: {postExplSingle}");
                }
                else
                {
                    // ============================================================
                    // Режим dual (нужны и Prefix, и Suffix): оптимальный алгоритм.
                    //
                    // Состояния (P, S) где каждый: E=пусто, R=правильный, W=неправильный.
                    //
                    // (E,E)        → Aug → read → если 1 неправильный → немедленный Annul → read
                    // (R,E)/(E,R)  → Aug → read
                    // (R,W)/(W,R)  → Annul → read → если 1 неправильный остался → повторный Annul → read
                    //                (Aug в этом цикле НЕ делаем)
                    // (W,E)/(E,W)/(W,W) → Annul → read (защитная ветка)
                    // ============================================================

                    var prefixRight = prefixCount == 1 && IsAffixRight(current, isPrefix: true, plan);
                    var suffixRight = suffixCount == 1 && IsAffixRight(current, isPrefix: false, plan);
                    var totalAffixes = prefixCount + suffixCount;

                    if (totalAffixes == 0)
                    {
                        // (E,E): Aug
                        craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, false, "[до Aug (E,E)] " + preExplanation);
                        log?.Report($"Состояние (E,E): применяем Orb of Augmentation.");
                        await ApplyOrbAsync(augOrbArea, itemArea, log, ct, "Orb of Augmentation").ConfigureAwait(false);
                        augStepsConsumed++;
                        annulOnlySafety = 0;

                        var (p1, c1) = await ReadAndParseAsync(
                            itemArea, log, ct,
                            $"Ауг+Аннул: after Aug (EE), попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);
                        if (p1 is null) return CraftResult.Empty(augStepsConsumed);
                        if (!p1.IsValid) { craftLog?.WriteValidationError("ItemParser: ошибка после Aug (EE)."); return CraftResult.Failed(augStepsConsumed); }

                        currentClipboard = c1;
                        current = p1;

                        if (CraftConditionEvaluator.TryEvaluate(plan, current, out var expl1))
                        {
                            craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, true, "[после Aug EE] " + expl1);
                            log?.Report("Условие выполнено после Aug (EE).");
                            return CraftResult.Found(augStepsConsumed, currentClipboard);
                        }
                        log?.Report($"  Условие: {expl1}");

                        // Если Aug добавил один неправильный аффикс — немедленный Annul
                        var p1c = current.Affixes.Count(a => IsTypeMatch(a.Type, true));
                        var s1c = current.Affixes.Count(a => IsTypeMatch(a.Type, false));
                        var p1r = p1c == 1 && IsAffixRight(current, true, plan);
                        var s1r = s1c == 1 && IsAffixRight(current, false, plan);

                        if ((p1c + s1c) == 1 && !p1r && !s1r)
                        {
                            log?.Report("После Aug (EE) получен один неправильный аффикс — немедленный Annul.");
                            await ApplyOrbAsync(annulOrbArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                            annulOnlySafety++;

                            var (p2, c2) = await ReadAndParseAsync(
                                itemArea, log, ct,
                                $"Ауг+Аннул: after немедленный Annul (EE→W), попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);
                            if (p2 is null) return CraftResult.Empty(augStepsConsumed);
                            if (!p2.IsValid) { craftLog?.WriteValidationError("ItemParser: ошибка после немедленного Annul."); return CraftResult.Failed(augStepsConsumed); }

                            currentClipboard = c2;
                            current = p2;
                        }
                    }
                    else if (totalAffixes == 1 && (prefixRight || suffixRight))
                    {
                        // (R,E) или (E,R): Aug
                        craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, false, $"[до Aug ({(prefixRight ? "R,E" : "E,R")})] " + preExplanation);
                        log?.Report($"Состояние ({(prefixRight ? "R,E" : "E,R")}): применяем Orb of Augmentation.");
                        await ApplyOrbAsync(augOrbArea, itemArea, log, ct, "Orb of Augmentation").ConfigureAwait(false);
                        augStepsConsumed++;
                        annulOnlySafety = 0;

                        var (p1, c1) = await ReadAndParseAsync(
                            itemArea, log, ct,
                            $"Ауг+Аннул: after Aug ({(prefixRight ? "RE" : "ER")}), попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);
                        if (p1 is null) return CraftResult.Empty(augStepsConsumed);
                        if (!p1.IsValid) { craftLog?.WriteValidationError("ItemParser: ошибка после Aug (R+E)."); return CraftResult.Failed(augStepsConsumed); }

                        currentClipboard = c1;
                        current = p1;

                        if (CraftConditionEvaluator.TryEvaluate(plan, current, out var expl1))
                        {
                            craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, true, $"[после Aug {(prefixRight ? "RE" : "ER")}] " + expl1);
                            log?.Report("Условие выполнено после Aug.");
                            return CraftResult.Found(augStepsConsumed, currentClipboard);
                        }
                        log?.Report($"  Условие: {expl1}");
                    }
                    else if (totalAffixes == 2 && (prefixRight || suffixRight))
                    {
                        // (R,W) или (W,R): Annul → read → если остался 1 неправильный → повторный Annul → read
                        log?.Report($"Состояние ({(prefixRight ? "R,W" : "W,R")}): применяем Orb of Annulment.");
                        await ApplyOrbAsync(annulOrbArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                        annulOnlySafety++;

                        var (p1, c1) = await ReadAndParseAsync(
                            itemArea, log, ct,
                            $"Ауг+Аннул: after Annul ({(prefixRight ? "RW" : "WR")}), попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);
                        if (p1 is null) return CraftResult.Empty(augStepsConsumed);
                        if (!p1.IsValid) { craftLog?.WriteValidationError("ItemParser: ошибка после Annul (RW/WR)."); return CraftResult.Failed(augStepsConsumed); }

                        currentClipboard = c1;
                        current = p1;

                        if (CraftConditionEvaluator.TryEvaluate(plan, current, out var expl1))
                        {
                            craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, true, $"[после Annul {(prefixRight ? "RW" : "WR")}] " + expl1);
                            log?.Report("Условие выполнено после Annul.");
                            return CraftResult.Found(augStepsConsumed, currentClipboard);
                        }
                        log?.Report($"  Условие: {expl1}");

                        // Если остался 1 неправильный аффикс — повторный Annul (Aug в этом цикле НЕ делаем)
                        var p1c = current.Affixes.Count(a => IsTypeMatch(a.Type, true));
                        var s1c = current.Affixes.Count(a => IsTypeMatch(a.Type, false));
                        var p1r = p1c == 1 && IsAffixRight(current, true, plan);
                        var s1r = s1c == 1 && IsAffixRight(current, false, plan);

                        if ((p1c + s1c) == 1 && !p1r && !s1r)
                        {
                            log?.Report("После Annul остался один неправильный аффикс — повторный Annul (Aug пропускаем).");
                            await ApplyOrbAsync(annulOrbArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                            annulOnlySafety++;

                            var (p2, c2) = await ReadAndParseAsync(
                                itemArea, log, ct,
                                $"Ауг+Аннул: after повторный Annul (RW→W→E), попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);
                            if (p2 is null) return CraftResult.Empty(augStepsConsumed);
                            if (!p2.IsValid) { craftLog?.WriteValidationError("ItemParser: ошибка после повторного Annul."); return CraftResult.Failed(augStepsConsumed); }

                            currentClipboard = c2;
                            current = p2;
                        }
                        // Aug в этом цикле НЕ делаем — continue к следующей итерации
                    }
                    else
                    {
                        // Защитная ветка: (W,E), (E,W) или (W,W) — только Annul, Aug не делаем
                        log?.Report($"Защитная ветка (W/W или 1 неправильный): применяем Orb of Annulment (prefix={prefixCount},right={prefixRight}; suffix={suffixCount},right={suffixRight}).");
                        await ApplyOrbAsync(annulOrbArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                        annulOnlySafety++;

                        var (p1, c1) = await ReadAndParseAsync(
                            itemArea, log, ct,
                            $"Ауг+Аннул: after Annul (defensive), попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);
                        if (p1 is null) return CraftResult.Empty(augStepsConsumed);
                        if (!p1.IsValid) { craftLog?.WriteValidationError("ItemParser: ошибка после Annul (defensive)."); return CraftResult.Failed(augStepsConsumed); }

                        currentClipboard = c1;
                        current = p1;

                        if (CraftConditionEvaluator.TryEvaluate(plan, current, out var expl1))
                        {
                            craftLog?.WriteComparison(displayAttempt, globalTotal, currentClipboard, pattern, true, "[после Annul defensive] " + expl1);
                            log?.Report("Условие выполнено после Annul (defensive).");
                            return CraftResult.Found(augStepsConsumed, currentClipboard);
                        }
                        log?.Report($"  Условие: {expl1}");
                    }

                    if (annulOnlySafety > 10)
                    {
                        craftLog?.WriteValidationError("Слишком много Annul подряд без Aug (защита от бесконечного цикла).");
                        return CraftResult.Failed(augStepsConsumed);
                    }
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
