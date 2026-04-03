using System.Runtime.InteropServices;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Крафт Rare через Orb of Exaltation + омeны + Orb of Annulment строго по
/// docs/EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt.
/// Попытки (N) расходуются только на применения Orb of Exaltation.
/// </summary>
public sealed class ExaltationCraftServiceFracturedSide
{
    private const double DelayJitterFraction = 0.30;

    private readonly OmenActivationService _omen;

    public ExaltationCraftServiceFracturedSide(OmenActivationService omen) => _omen = omen;

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

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    private async Task<string> ReadClipboardTextAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);

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

    private static bool IsRare(ParsedItem? item) =>
        item is { IsValid: true } &&
        string.Equals(item.Rarity?.Trim(), "Rare", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrefixLike(string? type)
    {
        var t = (type ?? "").Trim();
        if (t.Length == 0)
            return false;
        return string.Equals(t, "Prefix Modifier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuffixLike(string? type)
    {
        var t = (type ?? "").Trim();
        if (t.Length == 0)
            return false;
        return string.Equals(t, "Suffix Modifier", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountPrefixLikeAffixes(ParsedItem item) =>
        item.Affixes?.Count(a => IsPrefixLike(a.Type)) ?? 0;

    private static int CountSuffixLikeAffixes(ParsedItem item) =>
        item.Affixes?.Count(a => IsSuffixLike(a.Type)) ?? 0;

    private static int CountUnknownAffixType(ParsedItem item)
    {
        if (item.Affixes is null || item.Affixes.Count == 0)
            return 0;
        var unknown = 0;
        foreach (var a in item.Affixes)
        {
            var t = (a.Type ?? "").Trim();
            if (t.Length == 0)
            {
                unknown++;
                continue;
            }

            if (IsPrefixLike(t) || IsSuffixLike(t))
                continue;
            unknown++;
        }

        return unknown;
    }

    private static bool HasAnySatisfiedElementForBranching(CraftConditionPlan plan, ParsedItem item)
    {
        foreach (var or in plan.OrAlternatives)
        foreach (var c in or.Clauses)
        {
            if (c.Kind == CraftClauseKind.Single && c.Single != null)
            {
                var s = c.Single;
                if (ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStatNoLibrary(
                        item,
                        plan.ExpectedItemClass,
                        s.AffixType,
                        s.StatTemplate,
                        out var actual,
                        out _))
                {
                    var slots = Math.Max(1, actual.Count);
                    s.EnsureMinRollsSize(slots);
                    var mins = s.GetEffectiveMinRolls(slots).ToList();
                    if (ParsedItemCraftEvaluator.RollVectorMeetsMins(actual, mins, out _))
                        return true;
                }
            }
            else if (c.Kind == CraftClauseKind.Sum && c.Sum is { } sum)
            {
                foreach (var p in sum.Parts)
                {
                    if (ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStatNoLibrary(
                            item,
                            plan.ExpectedItemClass,
                            p.AffixType,
                            p.StatTemplate,
                            out var vals,
                            out _))
                    {
                        if (vals.Count > 0 && vals.Sum() > 0)
                            return true;
                    }
                }
            }
            else if (c.Kind == CraftClauseKind.Count && c.Count is { } cnt)
            {
                foreach (var m in cnt.Members)
                {
                    if (ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStatNoLibrary(
                            item,
                            plan.ExpectedItemClass,
                            m.AffixType,
                            m.StatTemplate,
                            out var actual,
                            out _))
                    {
                        var slots = Math.Max(1, actual.Count);
                        m.EnsureMinRollsSize(slots);
                        var mins = m.GetEffectiveMinRolls(slots).ToList();
                        if (ParsedItemCraftEvaluator.RollVectorMeetsMins(actual, mins, out _))
                            return true;
                    }
                }
            }
            else if (c.Kind == CraftClauseKind.WholeModifier && c.Whole is { } wholeBranch)
            {
                var libBranch = AffixLibrary.GetEntries();
                if (ParsedItemCraftEvaluator.TryWholeModifierAnyLineFullySatisfied(
                        wholeBranch,
                        item,
                        plan.ExpectedItemClass,
                        libBranch))
                    return true;
            }
        }

        return false;
    }

    private static (bool WantPrefix, bool WantSuffix) GetWantedTypes(CraftConditionPlan plan)
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

    private async Task<string> ReadClipboardAfterCtrlAltCAsync(ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string tag)
    {
        async Task<string> OnceAsync()
        {
            await ClearClipboardAsync().ConfigureAwait(false);

            var (hoverX, hoverY) = itemArea.GetInteriorPoint(1);
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !itemArea.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, $"{tag}: MoveTo item (center)", hoverX, hoverY);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await DelayJitterAsync(HoverSettleBeforeClipboardMs, ct).ConfigureAwait(false);

            try
            {
                LogKey(log, $"{tag}: Ctrl+Alt+C");
                Win32Input.SendCtrlAltC();
                await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);

                var text = await ReadClipboardTextAsync().ConfigureAwait(false);
                SessionLogger.InfoClipboard(tag, text);
                return text;
            }
            finally
            {
                Win32Input.ReleaseCtrlAlt();
            }
        }

        var first = await OnceAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        log?.Report($"{tag}: буфер пуст, retry через 1000ms…");
        await Task.Delay(1000, ct).ConfigureAwait(false);
        return await OnceAsync().ConfigureAwait(false);
    }

    private async Task ApplyCurrencyAsync(ScreenRect currencyArea, ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string currencyLabel)
    {
        var (ox, oy) = currencyArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !currencyArea.ContainsPoint(curX, curY, inset: 1))
            LogMove(log, $"MoveTo {currencyLabel} (случайная точка)", ox, oy);
        await StepPauseIfNeeded($"Курсор перемещён к области {currencyLabel} ({ox}, {oy}).", ct).ConfigureAwait(false);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogKey(log, "Shift DOWN");
        Win32Input.ShiftDown();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogMouse(log, $"ПКМ ({currencyLabel})");
        Win32Input.ClickRight();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        var (ix, iy) = itemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        if (!Win32Input.TryGetCursorPos(out curX, out curY) || !itemArea.ContainsPoint(curX, curY, inset: 1))
            LogMove(log, $"MoveTo item (ЛКМ, случайная точка)", ix, iy);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogMouse(log, "ЛКМ (предмет)");
        Win32Input.ClickLeft();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogKey(log, "Shift UP");
        Win32Input.ShiftUp();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
    }

    public async Task<CraftPrecheckResult> PrecheckAsync(
        ScreenRect itemArea,
        CraftConditionPlan plan,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        var clip = await ReadClipboardAfterCtrlAltCAsync(itemArea, log, ct, "экзальт: предпроверка Ctrl+Alt+C").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(clip))
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.EmptyCell,
                "Exaltation",
                "Буфер пуст после Ctrl+Alt+C (ячейка, вероятно, пустая).");

        var parsed = ItemParser.Parse(clip);
        if (!IsRare(parsed))
        {
            var r = parsed?.Rarity?.Length > 0 ? parsed.Rarity : "(не распознано)";
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.Failed,
                "Exaltation",
                $"Предмет должен быть Rare. Сейчас: {r}.",
                parsed,
                clip);
        }

        if (CraftConditionEvaluator.TryEvaluate(plan, parsed, out _))
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.AlreadySatisfied,
                "Exaltation",
                "Условие уже выполнено.",
                parsed,
                clip);

        return new CraftPrecheckResult(
            CraftPrecheckOutcome.Ready,
            "Exaltation",
            "OK",
            parsed,
            clip);
    }

    public async Task<(ChaosCraftResult Result, int AttemptsConsumed)> RunAsync(
        ScreenRect exaltArea,
        ScreenRect annulArea,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        ParsedItem initialParsedItem,
        string? initialClipboardText,
        int remainingAttempts,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        var (wantPrefix, wantSuffix) = GetWantedTypes(plan);
        var prefixOnly = wantPrefix && !wantSuffix;
        var suffixOnly = wantSuffix && !wantPrefix;
        var current = initialParsedItem;
        var currentClip = initialClipboardText ?? "";

        var used = 0;
        var annulsSinceLastExalt = 0;

        try
        {
            while (used < remainingAttempts)
            {
                ct.ThrowIfCancellationRequested();

                var displayAttempt = globalAttemptOffset + used + 1;
                var satisfied = CraftConditionEvaluator.TryEvaluate(plan, current, out var expl);
                craftLog?.WriteComparison(displayAttempt, globalTotal, currentClip, conditionSummary, satisfied, "[проверка перед действием] " + expl);
                log?.Report($"Проверка (попытка {displayAttempt}): {expl}");
                if (satisfied)
                    return (ChaosCraftResult.AffixFound, 0);

                var pCur = CountPrefixLikeAffixes(current);
                var sCur = CountSuffixLikeAffixes(current);
                var unknown = CountUnknownAffixType(current);

                const int pMax = 3;
                const int sMax = 3;

                var pFree = pMax - pCur - ((wantPrefix && !wantSuffix) ? unknown : 0);
                var sFree = sMax - sCur - ((wantSuffix && !wantPrefix) ? unknown : 0);

                bool canAdd1;
                bool canAdd2;
                if (wantPrefix && !wantSuffix)
                {
                    canAdd1 = pFree >= 1;
                    canAdd2 = pFree >= 2;
                }
                else if (wantSuffix && !wantPrefix)
                {
                    canAdd1 = sFree >= 1;
                    canAdd2 = sFree >= 2;
                }
                else
                {
                    var totalFree = Math.Max(0, pMax - pCur) + Math.Max(0, sMax - sCur);
                    canAdd1 = totalFree >= 1;
                    canAdd2 = totalFree >= 2;
                }

                var anyMatch = HasAnySatisfiedElementForBranching(plan, current);

                if (!anyMatch)
                {
                    if (canAdd2)
                    {
                        // A2: первая подходящая ячейка Greater
                        var gCell = await _omen.ActivateFirstAsync(
                            omenGreaterCells,
                            OmenActivationService.OmenGreaterExaltationName,
                            log,
                            ct).ConfigureAwait(false);
                        if (gCell is null)
                        {
                            log?.Report("Экзальт (ветка A): Omen Greater не найден — остановка.");
                            return (ChaosCraftResult.Error, used);
                        }

                        // A3: Sinistral / Dextral или нет (mixed)
                        ScreenRect? sdCell = null;
                        if (prefixOnly)
                        {
                            sdCell = await _omen.ActivateFirstAsync(
                                omenSinistralCells,
                                OmenActivationService.OmenSinistralExaltationName,
                                log,
                                ct).ConfigureAwait(false);
                            if (sdCell is null)
                            {
                                log?.Report("Экзальт (ветка A): OSinistral не найден — остановка.");
                                return (ChaosCraftResult.Error, used);
                            }
                        }
                        else if (suffixOnly)
                        {
                            sdCell = await _omen.ActivateFirstAsync(
                                omenDextralCells,
                                OmenActivationService.OmenDextralExaltationName,
                                log,
                                ct).ConfigureAwait(false);
                            if (sdCell is null)
                            {
                                log?.Report("Экзальт (ветка A): Omen Dextral не найден — остановка.");
                                return (ChaosCraftResult.Error, used);
                            }
                        }

                        await ApplyCurrencyAsync(exaltArea, itemArea, log, ct, "Orb of Exaltation").ConfigureAwait(false);
                        used += 1;
                        annulsSinceLastExalt = 0;

                        // A4.1, A4.2 — деактивировать те же ячейки, что задействовали в этом шаге
                        await _omen.DeactivateAsync(gCell.Value, log, ct).ConfigureAwait(false);
                        if (sdCell is { } sc)
                            await _omen.DeactivateAsync(sc, log, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        if (annulsSinceLastExalt + 2 > 6)
                        {
                            log?.Report("Экзальт: слишком много Orb of Annulment подряд без Orb of Exaltation — остановка во избежание лишних удалений.");
                            return (ChaosCraftResult.Error, used);
                        }

                        var (ox, oy) = annulArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                        if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !annulArea.ContainsPoint(curX, curY, inset: 1))
                            LogMove(log, "MoveTo Orb of Annulment (случайная точка)", ox, oy);
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                        LogMouse(log, "ПКМ (Orb of Annulment)");
                        Win32Input.ClickRight();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        LogKey(log, "Shift DOWN");
                        Win32Input.ShiftDown();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        var (ix, iy) = itemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                        if (!Win32Input.TryGetCursorPos(out var curX2, out var curY2) || !itemArea.ContainsPoint(curX2, curY2, inset: 1))
                            LogMove(log, "MoveTo предмет (Annul x2, случайная точка)", ix, iy);
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        LogMouse(log, "ЛКМ (Annul 1)");
                        Win32Input.ClickLeft();
                        annulsSinceLastExalt++;
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        LogMouse(log, "ЛКМ (Annul 2)");
                        Win32Input.ClickLeft();
                        annulsSinceLastExalt++;
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        LogKey(log, "Shift UP");
                        Win32Input.ShiftUp();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (canAdd1)
                    {
                        // B3: один омен Sin или Dex; mixed — без омна
                        ScreenRect? bCell = null;
                        if (prefixOnly)
                        {
                            bCell = await _omen.ActivateFirstAsync(
                                omenSinistralCells,
                                OmenActivationService.OmenSinistralExaltationName,
                                log,
                                ct).ConfigureAwait(false);
                            if (bCell is null)
                            {
                                log?.Report("Экзальт (ветка B): Omen Sinistral не найден — остановка.");
                                return (ChaosCraftResult.Error, used);
                            }
                        }
                        else if (suffixOnly)
                        {
                            bCell = await _omen.ActivateFirstAsync(
                                omenDextralCells,
                                OmenActivationService.OmenDextralExaltationName,
                                log,
                                ct).ConfigureAwait(false);
                            if (bCell is null)
                            {
                                log?.Report("Экзальт (ветка B): Omen Dextral не найден — остановка.");
                                return (ChaosCraftResult.Error, used);
                            }
                        }

                        await ApplyCurrencyAsync(exaltArea, itemArea, log, ct, "Orb of Exaltation").ConfigureAwait(false);
                        used += 1;
                        annulsSinceLastExalt = 0;

                        if (bCell is { } bc)
                            await _omen.DeactivateAsync(bc, log, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await ApplyCurrencyAsync(annulArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                        annulsSinceLastExalt++;
                    }
                }

                currentClip = await ReadClipboardAfterCtrlAltCAsync(
                    itemArea,
                    log,
                    ct,
                    $"экзальт: Ctrl+Alt+C после действий (попытка {globalAttemptOffset + Math.Max(1, used)} / {globalTotal})").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(currentClip))
                    return (ChaosCraftResult.EmptyCell, used);

                current = ItemParser.Parse(currentClip) ?? new ParsedItem { IsValid = false };
                if (!IsRare(current))
                    return (ChaosCraftResult.Error, used);
            }
        }
        catch (OperationCanceledException)
        {
            Win32Input.ReleaseShift();
            throw;
        }
        catch
        {
            Win32Input.ReleaseShift();
            throw;
        }

        return (ChaosCraftResult.MaxAttemptsReached, used);
    }
}
