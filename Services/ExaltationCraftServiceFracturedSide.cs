using System.Runtime.InteropServices;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Крафт Rare-предмета через Orb of Exaltation с использованием оменов и Orb of Annulment по схеме из
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

    private static bool HasAnySatisfiedClause(CraftConditionPlan plan, ParsedItem item)
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
                double total = 0;
                foreach (var p in sum.Parts)
                {
                    if (ParsedItemCraftEvaluator.TryGetRollValuesForTypeAndStatNoLibrary(
                            item,
                            plan.ExpectedItemClass,
                            p.AffixType,
                            p.StatTemplate,
                            out var vals,
                            out _))
                        total += vals.Sum();
                }

                if (total >= sum.MinSum)
                    return true;
            }
            else if (c.Kind == CraftClauseKind.Count && c.Count is { } cnt)
            {
                var matched = 0;
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
                            matched++;
                    }
                }

                if (matched >= cnt.MinMatchCount)
                    return true;
            }
            else if (c.Kind == CraftClauseKind.WholeModifier && c.Whole is { } whole)
            {
                var lib = AffixLibrary.GetEntries();
                if (ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(whole, item, plan.ExpectedItemClass, lib, out _))
                    return true;
            }
        }

        return false;
    }

    private static bool HasAnySatisfiedElementForBranching(CraftConditionPlan plan, ParsedItem item)
    {
        // Для ветвления Exalt/Annul нам важно "есть ли прогресс" по условиям:
        // - Single: проходит ли порог
        // - Sum: есть ли вклад хотя бы от одного элемента (даже если суммарный порог ещё не пройден)
        // - Count: выполнен ли хотя бы 1 member (даже если MinMatchCount ещё не достигнут)
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

        // Кэш "пустых" ячеек оменов внутри одного RunAsync, чтобы не дёргать Ctrl+C по ним снова.
        var emptySinistralCells = new HashSet<ScreenRect>();
        var emptyDextralCells = new HashSet<ScreenRect>();
        var emptyGreaterCells = new HashSet<ScreenRect>();

        // В новом режиме омeны включаем/выключаем пачкой, чтобы не тратить время на toggle на каждом шаге.
        var greaterActive = false;
        var greaterOmenCells = new List<ScreenRect>();
        var sinDexOmenCells = new List<ScreenRect>();

        // 1) При запуске крафта активируем все омены во всех непустых ячейках (Sinistral/Dextral — по типу условия).
        if (prefixOnly)
        {
            var r = await _omen.ActivateAllAsync(
                    omenSinistralCells,
                    OmenActivationService.OmenSinistralExaltationName,
                    log,
                    ct,
                    skipCells: emptySinistralCells,
                    markEmptyCells: emptySinistralCells)
                .ConfigureAwait(false);
            sinDexOmenCells = r.OmenCells.ToList();
            if (sinDexOmenCells.Count == 0)
            {
                log?.Report("Экзальт: oмен Sinistral не найден ни в одной ячейке — остановка.");
                return (ChaosCraftResult.Error, 0);
            }
        }
        else if (suffixOnly)
        {
            var r = await _omen.ActivateAllAsync(
                    omenDextralCells,
                    OmenActivationService.OmenDextralExaltationName,
                    log,
                    ct,
                    skipCells: emptyDextralCells,
                    markEmptyCells: emptyDextralCells)
                .ConfigureAwait(false);
            sinDexOmenCells = r.OmenCells.ToList();
            if (sinDexOmenCells.Count == 0)
            {
                log?.Report("Экзальт: oмен Dextral не найден ни в одной ячейке — остановка.");
                return (ChaosCraftResult.Error, 0);
            }
        }

        // Greater мы будем включать/выключать по ситуации (canAdd2 vs только 1 слот).
        async Task EnsureGreaterStateAsync(bool wantActive)
        {
            if (wantActive == greaterActive)
                return;

            if (wantActive)
            {
                var r = await _omen.ActivateAllAsync(
                        omenGreaterCells,
                        OmenActivationService.OmenGreaterExaltationName,
                        log,
                        ct,
                        skipCells: emptyGreaterCells,
                        markEmptyCells: emptyGreaterCells)
                    .ConfigureAwait(false);
                greaterOmenCells = r.OmenCells.ToList();
                if (greaterOmenCells.Count == 0)
                {
                    log?.Report("Экзальт: oмен Greater не найден ни в одной ячейке — остановка.");
                    throw new InvalidOperationException("Omen Greater depleted.");
                }

                greaterActive = true;
                return;
            }

            // wantActive == false
            await _omen.DeactivateAllAsync(greaterOmenCells, log, ct).ConfigureAwait(false);
            greaterActive = false;
        }
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

                // NOTE: по умолчанию Rare = 3 префикса + 3 суффикса; исключения (другие лимиты) будут позже.
                const int pMax = 3;
                const int sMax = 3;

                // Для веток, завязанных на тип искомого аффикса, считаем ёмкость по слотам нужного типа.
                // Если тип аффикса распознать не удалось (unknown), консервативно считаем, что он занимает слот нужного типа
                // (иначе можно ошибочно решить, что префиксы свободны, хотя они заняты "неизвестным" модом/фрактурой).
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
                    // Mixed/unknown requirement: fallback to total free slots.
                    var totalFree = Math.Max(0, pMax - pCur) + Math.Max(0, sMax - sCur);
                    canAdd1 = totalFree >= 1;
                    canAdd2 = totalFree >= 2;
                }

                // Для COUNT≥K/N "совпадение есть" означает, что выполнен хотя бы 1 member (с порогами),
                // даже если K ещё не достигнут; иначе мы ошибочно уйдём в ветку Annul x2.
                var anyMatch = HasAnySatisfiedElementForBranching(plan, current);

                if (!anyMatch)
                {
                    if (canAdd2)
                    {
                        await EnsureGreaterStateAsync(wantActive: true).ConfigureAwait(false);

                        await ApplyCurrencyAsync(exaltArea, itemArea, log, ct, "Orb of Exaltation").ConfigureAwait(false);
                        used += 1;
                        annulsSinceLastExalt = 0;
                    }
                    else
                    {
                        // Annul branch when no matches and no room for 2 affixes.
                        // По схеме: если совпадений нет и нет места для двух аффиксов — делаем Annul x2.
                        if (annulsSinceLastExalt + 2 > 6)
                        {
                            log?.Report("Экзальт: слишком много Orb of Annulment подряд без Orb of Exaltation — остановка во избежание лишних удалений.");
                            return (ChaosCraftResult.Error, used);
                        }

                        // Новая механика Annul x2:
                        // ПКМ на Orb of Annulment, ShiftDown, 2×ЛКМ по предмету, ShiftUp.

                        // 1) ПКМ по Orb of Annulment (один раз)
                        var (ox, oy) = annulArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                        if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !annulArea.ContainsPoint(curX, curY, inset: 1))
                            LogMove(log, "MoveTo Orb of Annulment (случайная точка)", ox, oy);
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                        LogMouse(log, "ПКМ (Orb of Annulment)");
                        Win32Input.ClickRight();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        // 2) ShiftDown
                        LogKey(log, "Shift DOWN");
                        Win32Input.ShiftDown();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        // 3) MoveTo предмет (если нужно) и 2× ЛКМ
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

                        // 4) ShiftUp
                        LogKey(log, "Shift UP");
                        Win32Input.ShiftUp();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (canAdd1)
                    {
                        // свободен только 1 слот, есть частичное выполнение условия (ветка B) — выключаем Greater пачкой
                        // (Greater нужен только для "добавить 2 аффикса", а здесь добавляем 1 целевой аффикс).
                        try { await EnsureGreaterStateAsync(wantActive: false).ConfigureAwait(false); } catch { /* ignore */ }

                        await ApplyCurrencyAsync(exaltArea, itemArea, log, ct, "Orb of Exaltation").ConfigureAwait(false);
                        used += 1;
                        annulsSinceLastExalt = 0;
                    }
                    else
                    {
                        // Annul x1 (не расходует попытку)
                        await ApplyCurrencyAsync(annulArea, itemArea, log, ct, "Orb of Annulment").ConfigureAwait(false);
                        annulsSinceLastExalt++;
                    }
                }

                // После ветки читаем предмет заново
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

