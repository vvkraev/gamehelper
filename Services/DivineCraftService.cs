using System.Runtime.InteropServices;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Крафт Divine Orb: перебрасывает числовые значения существующих аффиксов в пределах диапазонов.
/// Перед каждым применением проверяет, что все аффиксы из условия присутствуют на предмете
/// (если какой-либо отсутствует — ячейка пропускается без трат орба).
/// </summary>
public sealed class DivineCraftService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public int HoverSettleBeforeClipboardMs { get; set; } = 120;
    public bool TraceInputToLog { get; set; }
    public Func<string, Task>? StepConfirmAsync { get; set; }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        if (delta <= 0) return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static Task DelayJitterAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    // ── Буфер обмена ────────────────────────────────────────────────────────

    private Task ClearClipboardAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearClipboardSafe).Task;

    private static void ClearClipboardSafe()
    {
        try { System.Windows.Clipboard.Clear(); } catch { }
    }

    private static string GetClipboardTextSafe()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : string.Empty;
        }
        catch { return string.Empty; }
    }

    private async Task<string> ReadClipboardAfterCtrlAltCAsync(
        ScreenRect itemArea,
        IProgress<string>? log,
        CancellationToken ct,
        string tag)
    {
        async Task<string> OnceAsync()
        {
            await ClearClipboardAsync().ConfigureAwait(false);
            var (x, y) = itemArea.GetInteriorPoint(1);
            if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !itemArea.ContainsPoint(cx, cy, inset: 1))
                LogMove(log, $"{tag}: MoveTo предмет перед Ctrl+Alt+C", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(HoverSettleBeforeClipboardMs, ct).ConfigureAwait(false);
            if (TraceInputToLog) log?.Report($"[Ввод] {tag}: Ctrl+Alt+C");
            Win32Input.SendCtrlAltC();
            await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
            var text = await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
            SessionLogger.InfoClipboard(tag, text);
            return text;
        }

        var first = await OnceAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        log?.Report($"{tag}: буфер пуст, повтор Ctrl+Alt+C…");
        await Task.Delay(1000, ct).ConfigureAwait(false);
        return await OnceAsync().ConfigureAwait(false);
    }

    // ── Предпроверка ────────────────────────────────────────────────────────

    /// <summary>
    /// Читает предмет, проверяет класс, условие и наличие всех аффиксов из условия.
    /// <list type="bullet">
    /// <item><see cref="CraftPrecheckOutcome.AlreadySatisfied"/> — значения уже удовлетворяют условию.</item>
    /// <item><see cref="CraftPrecheckOutcome.AffixesMissing"/> — не все аффиксы присутствуют; ячейку пропускаем.</item>
    /// <item><see cref="CraftPrecheckOutcome.Ready"/> — аффиксы есть, значения не выполняют условие → можно применять орб.</item>
    /// </list>
    /// </summary>
    public async Task<CraftPrecheckResult> PrecheckAsync(
        ScreenRect itemArea,
        CraftConditionPlan plan,
        IProgress<string>? log,
        CancellationToken ct)
    {
        log?.Report("Предварительная проверка (Divine): чтение предмета (Ctrl+Alt+C)…");
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        var clip = await ReadClipboardAfterCtrlAltCAsync(itemArea, log, ct, "divine precheck").ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(clip))
            return new CraftPrecheckResult(CraftPrecheckOutcome.EmptyCell, "Пустая ячейка",
                "После Ctrl+Alt+C буфер пуст — вероятно, в этой ячейке нет предмета.", null, clip);

        var parsed = ItemParser.Parse(clip);
        if (parsed is not { IsValid: true })
            return new CraftPrecheckResult(CraftPrecheckOutcome.Failed, "Предмет",
                "Не удалось разобрать текст предмета из буфера.", null, clip);

        MergeLibrary(parsed, log);

        if (!ParsedItemCraftEvaluator.ItemClassMatches(parsed, plan.ExpectedItemClass))
            return new CraftPrecheckResult(CraftPrecheckOutcome.Failed, "Класс предмета",
                $"В буфере класс «{parsed.ItemClass}», а в условии указан «{plan.ExpectedItemClass}».", parsed, clip);

        if (CraftConditionEvaluator.TryEvaluate(plan, parsed, out _))
            return new CraftPrecheckResult(CraftPrecheckOutcome.AlreadySatisfied, "Условие уже выполнено",
                "Предмет в этой ячейке уже удовлетворяет условию остановки крафта.", parsed, clip);

        if (!AreAllConditionAffixesPresent(plan, parsed))
        {
            var desc = DescribeMissingAffixes(plan, parsed);
            return new CraftPrecheckResult(CraftPrecheckOutcome.AffixesMissing, "Аффиксы отсутствуют",
                $"Не все аффиксы из условия присутствуют на предмете — Divine Orb здесь не нужен.\n{desc}", parsed, clip);
        }

        return new CraftPrecheckResult(CraftPrecheckOutcome.Ready, "", "", parsed, clip);
    }

    // ── Основной цикл ───────────────────────────────────────────────────────

    /// <summary>
    /// Цикл Divine Orb: читает предмет → проверяет условие и наличие аффиксов → применяет орб.
    /// </summary>
    public async Task<CraftResult> RunAsync(
        ScreenRect orbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        int segmentMaxOperations,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog = null)
    {
        if (segmentMaxOperations < 1)
        {
            log?.Report("Остаток попыток (N) должен быть ≥ 1.");
            return CraftResult.Failed();
        }

        var pattern = conditionSummary.Trim();
        if (pattern.Length == 0) pattern = "(разбор предмета)";

        var orbSelected = false;
        var shiftHeld = false;

        try
        {
            for (var attempt = 1; attempt <= segmentMaxOperations; attempt++)
            {
                var displayAttempt = globalAttemptOffset + attempt;
                log?.Report($"Операция {displayAttempt} / {globalTotal} (Divine)…");

                try
                {
                    ct.ThrowIfCancellationRequested();

                    _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
                    await Task.Delay(80, ct).ConfigureAwait(false);

                    // 1) Читаем предмет до применения орба.
                    var preClip = await ReadClipboardAfterCtrlAltCAsync(
                        itemArea, log, ct,
                        $"divine: чтение предмета, попытка {displayAttempt}/{globalTotal}").ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(preClip))
                    {
                        log?.Report("Буфер пуст — ячейка пустая, переходим к следующей.");
                        if (shiftHeld) Win32Input.ReleaseShift();
                        return CraftResult.Empty(0);
                    }

                    var preParsed = ItemParser.Parse(preClip);

                    // 2) Условие уже выполнено?
                    var alreadyMatch = CraftConditionEvaluator.TryEvaluate(plan, preParsed, out var explanation);
                    craftLog?.WriteComparison(displayAttempt, globalTotal, preClip, pattern, alreadyMatch, explanation);
                    log?.Report($"Проверка (попытка {displayAttempt}): {explanation}");

                    if (alreadyMatch)
                    {
                        log?.Report("Условие выполнено — Divine Orb не применяем, переходим к следующей ячейке.");
                        if (shiftHeld) Win32Input.ReleaseShift();
                        return CraftResult.Found(attempt - 1, preClip);
                    }

                    // 3) Все аффиксы условия присутствуют?
                    if (!AreAllConditionAffixesPresent(plan, preParsed))
                    {
                        var desc = DescribeMissingAffixes(plan, preParsed);
                        log?.Report($"Не все аффиксы из условия найдены на предмете — ячейку пропускаем (Divine не тратится). {desc}");
                        if (shiftHeld) Win32Input.ReleaseShift();
                        return CraftResult.NoAffixes(0);
                    }

                    // 4) Применяем Divine Orb (Shift+ПКМ орб, ЛКМ предмет).
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                    if (!orbSelected)
                    {
                        var (ox, oy) = orbArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                        LogMove(log, "MoveTo Divine Orb", ox, oy);
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        if (TraceInputToLog) log?.Report("[Ввод] Shift DOWN");
                        Win32Input.ShiftDown();
                        shiftHeld = true;
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                        if (TraceInputToLog) log?.Report("[Ввод] ПКМ (орб)");
                        Win32Input.ClickRight();
                        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                        orbSelected = true;
                    }

                    var (ix, iy) = itemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                    LogMove(log, "MoveTo предмет (ЛКМ)", ix, iy);
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

                    if (TraceInputToLog) log?.Report("[Ввод] ЛКМ (предмет)");
                    Win32Input.ClickLeft();
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Win32Input.ReleaseShift();
                    Win32Input.ReleaseCtrlAlt();
                    shiftHeld = false;
                    orbSelected = false;
                    log?.Report("Остановлено пользователем.");
                    return CraftResult.Stopped(attempt - 1);
                }
                catch (Exception ex)
                {
                    Win32Input.ReleaseShift();
                    Win32Input.ReleaseCtrlAlt();
                    shiftHeld = false;
                    orbSelected = false;
                    craftLog?.WriteValidationError("Исключение: " + ex.Message);
                    log?.Report("Ошибка: " + ex.Message);
                    return CraftResult.Failed(attempt - 1);
                }
                finally
                {
                    Win32Input.AltUp();
                }

                await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            }
        }
        finally { }

        if (shiftHeld) Win32Input.ReleaseShift();
        log?.Report($"Достигнут лимит попыток ({segmentMaxOperations}) для ячейки, условие не выполнено.");
        return CraftResult.LimitReached(segmentMaxOperations);
    }

    // ── Проверка наличия аффиксов ────────────────────────────────────────────

    /// <summary>
    /// Возвращает true, если хотя бы один OR-вариант имеет все свои аффиксы на предмете
    /// (пороги числовых значений игнорируются — только присутствие имени аффикса).
    /// </summary>
    internal static bool AreAllConditionAffixesPresent(CraftConditionPlan plan, ParsedItem? item)
    {
        if (item is null) return false;
        foreach (var group in plan.OrAlternatives)
        {
            if (IsGroupAffixesPresent(group, item))
                return true;
        }
        return false;
    }

    private static bool IsGroupAffixesPresent(CraftAndGroup group, ParsedItem item)
    {
        foreach (var clause in group.Clauses)
        {
            if (!IsClauseAffixPresent(clause, item))
                return false;
        }
        return true;
    }

    private static bool IsClauseAffixPresent(CraftClause clause, ParsedItem item) =>
        clause.Kind switch
        {
            CraftClauseKind.Single =>
                clause.Single?.EffectiveAffixNames()
                    .Any(n => item.Affixes.Any(a => string.Equals(a.Name, n, StringComparison.Ordinal)))
                ?? false,

            CraftClauseKind.WholeModifier =>
                clause.Whole?.EffectiveWholeAffixNames()
                    .Any(n => item.Affixes.Any(a => string.Equals(a.Name, n, StringComparison.Ordinal)))
                ?? false,

            // Sum: хотя бы один из частей даёт вклад → аффикс присутствует
            CraftClauseKind.Sum =>
                clause.Sum?.Parts
                    .Any(p => item.Affixes.Any(a => string.Equals(a.Name, p.AffixName, StringComparison.Ordinal)))
                ?? false,

            // Count: проверяем только присутствие имён (без числовых порогов)
            CraftClauseKind.Count =>
                clause.Count != null &&
                clause.Count.Members.Count(m =>
                    m.EffectiveWholeAffixNames().Any(n =>
                        item.Affixes.Any(a => string.Equals(a.Name, n, StringComparison.Ordinal))))
                    >= clause.Count.MinMatchCount,

            _ => true,
        };

    private static string DescribeMissingAffixes(CraftConditionPlan plan, ParsedItem? item)
    {
        if (item is null) return "";

        foreach (var group in plan.OrAlternatives)
        {
            var missing = group.Clauses
                .Where(c => !IsClauseAffixPresent(c, item))
                .Select(GetClauseLabel)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            if (missing.Count > 0)
                return "Отсутствуют: " + string.Join(", ", missing);
        }
        return "";
    }

    private static string GetClauseLabel(CraftClause c) =>
        c.Kind switch
        {
            CraftClauseKind.Single        => c.Single?.EffectiveAffixNames().FirstOrDefault() ?? "",
            CraftClauseKind.WholeModifier => c.Whole?.EffectiveWholeAffixNames().FirstOrDefault() ?? "",
            CraftClauseKind.Sum           => c.Sum?.Parts.Select(p => p.AffixName).FirstOrDefault() ?? "",
            CraftClauseKind.Count         => c.Count?.Members.Select(m => m.EffectiveWholeAffixNames().FirstOrDefault() ?? "").FirstOrDefault() ?? "",
            _                             => "",
        };

    // ── Вспомогательные ─────────────────────────────────────────────────────

    private void LogMove(IProgress<string>? log, string label, int x, int y)
    {
        if (TraceInputToLog)
            log?.Report($"[Ввод] {label}: SetCursorPos({x},{y})");

        if (!Win32Input.MoveTo(x, y))
        {
            var err = Marshal.GetLastWin32Error();
            log?.Report($"[Ввод] ОШИБКА SetCursorPos({x},{y}): Win32 код {err}");
        }
    }

    private static void MergeLibrary(ParsedItem? parsed, IProgress<string>? log)
    {
        if (parsed is not { IsValid: true }) return;
        var added = AffixLibrary.MergeFromParsedItem(parsed);
        if (added > 0)
            log?.Report($"Библиотека аффиксов: добавлено новых записей: {added}.");
    }
}
