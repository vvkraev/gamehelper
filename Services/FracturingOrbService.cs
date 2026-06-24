using System.Runtime.InteropServices;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Применяет Fracturing Orb к одному предмету и проверяет, зафиксировался ли нужный аффикс.
/// Одно обращение к RunAsync = одно применение орба к одному предмету (одной ячейке).
/// </summary>
public sealed class FracturingOrbService : IFracturingOrbService
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

    public Task ClearClipboardAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearClipboardSafe).Task;

    private static void ClearClipboardSafe()
    {
        try { System.Windows.Clipboard.Clear(); }
        catch { }
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    private async Task<string> ReadClipboardAsync(ScreenRect itemArea, IProgress<string>? log, CancellationToken ct, string tag)
    {
        async Task<string> OnceAsync()
        {
            await ClearClipboardAsync().ConfigureAwait(false);
            var (x, y) = itemArea.GetInteriorPoint(1);
            if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !itemArea.ContainsPoint(cx, cy, inset: 1))
                LogMove(log, $"{tag}: MoveTo предмет", x, y);
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
        if (!string.IsNullOrWhiteSpace(first)) return first;

        log?.Report($"{tag}: буфер пуст, повтор Ctrl+Alt+C…");
        await Task.Delay(1000, ct).ConfigureAwait(false);
        return await OnceAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Предпроверка перед применением Fracturing Orb.
    /// AlreadySatisfied = нужный аффикс уже зафиксирован (IsFractured) — ячейку пропускаем.
    /// </summary>
    public async Task<CraftPrecheckResult> PrecheckAsync(
        ScreenRect itemArea,
        CraftConditionPlan plan,
        IProgress<string>? log,
        CancellationToken ct)
    {
        log?.Report("Предварительная проверка (Fracturing Orb)…");
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, ct).ConfigureAwait(false);

        var clip = await ReadClipboardAsync(itemArea, log, ct, "предпроверка").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(clip))
            return new CraftPrecheckResult(CraftPrecheckOutcome.EmptyCell, "Пустая ячейка",
                "После Ctrl+Alt+C буфер пуст — вероятно, в ячейке нет предмета.");

        var parsed = ItemParser.Parse(clip);
        if (parsed is not { IsValid: true })
            return new CraftPrecheckResult(CraftPrecheckOutcome.Failed, "Предмет",
                "Не удалось разобрать текст предмета из буфера. Убедитесь, что область предмета задана верно.", null, clip);

        if (!ParsedItemCraftEvaluator.ItemClassMatches(parsed, plan.ExpectedItemClass))
            return new CraftPrecheckResult(CraftPrecheckOutcome.Failed, "Класс предмета",
                $"В буфере класс «{parsed.ItemClass}», ожидался «{plan.ExpectedItemClass}».", parsed, clip);

        // Уже есть нужный зафиксированный аффикс — ячейку пропускаем, орб не тратим.
        if (ParsedItemCraftEvaluator.FracturedAffixMatchesPlan(parsed, plan, out _))
            return new CraftPrecheckResult(CraftPrecheckOutcome.AlreadySatisfied,
                "Уже зафиксирован", "Нужный аффикс уже зафиксирован на этом предмете.", parsed, clip);

        return new CraftPrecheckResult(CraftPrecheckOutcome.Ready, "", "", parsed, clip);
    }

    /// <summary>
    /// Применяет Fracturing Orb к предмету в ячейке <paramref name="itemArea"/>.
    /// Один вызов = одно применение орба = один предмет.
    /// </summary>
    public async Task<CraftResult> RunAsync(
        ScreenRect orbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken ct,
        CraftRunFileLog? craftLog = null)
    {
        var displayAttempt = globalAttemptOffset + 1;
        log?.Report($"Попытка {displayAttempt} / {globalTotal}: применение Fracturing Orb…");

        var prevTrace = Win32Input.InputTrace;
        if (TraceInputToLog)
            Win32Input.InputTrace = msg => log?.Report("[Ввод][Raw] " + msg);

        var shiftHeld = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(80, ct).ConfigureAwait(false);

            // 1) Shift+ПКМ по орбу — выбираем Fracturing Orb.
            var (ox, oy) = orbArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            LogMove(log, "MoveTo Fracturing Orb", ox, oy);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            if (TraceInputToLog) log?.Report("[Ввод] Shift DOWN");
            Win32Input.ShiftDown();
            shiftHeld = true;
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            if (TraceInputToLog) log?.Report("[Ввод] ПКМ (орб)");
            Win32Input.ClickRight();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            // 2) ЛКМ по предмету — применяем орб.
            var (ix, iy) = itemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            LogMove(log, "MoveTo предмет (ЛКМ)", ix, iy);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            if (TraceInputToLog) log?.Report("[Ввод] ЛКМ (предмет)");
            Win32Input.ClickLeft();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

            Win32Input.ReleaseShift();
            shiftHeld = false;

            // 3) Читаем результат.
            var clip = await ReadClipboardAsync(itemArea, log, ct, $"результат попытки {displayAttempt}").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(clip))
            {
                log?.Report("Буфер пуст после применения орба — не удалось прочитать результат.");
                craftLog?.WriteValidationError("Буфер пуст после Fracturing Orb.");
                return CraftResult.Failed(1);
            }

            var parsed = ItemParser.Parse(clip);
            if (parsed is not { IsValid: true })
            {
                log?.Report("Не удалось разобрать предмет после применения орба.");
                craftLog?.WriteValidationError("ParsedItem невалиден после Fracturing Orb.");
                return CraftResult.Failed(1);
            }

            // 4) Проверяем: нужный аффикс зафиксирован?
            var matched = ParsedItemCraftEvaluator.FracturedAffixMatchesPlan(parsed, plan, out var explanation);
            craftLog?.WriteComparison(displayAttempt, globalTotal, clip, conditionSummary, matched, explanation);
            log?.Report($"Результат: {explanation}");

            if (matched)
            {
                log?.Report("Нужный аффикс зафиксирован — успех!");
                return CraftResult.Found(1, clip);
            }

            // Логируем что именно зафиксировалось.
            var fractured = parsed.Affixes.Where(a => a.IsFractured).ToList();
            if (fractured.Count > 0)
                log?.Report($"Зафиксировался другой аффикс: {string.Join(", ", fractured.Select(f => $"«{f.Name}» T{f.Tier}"))} — предмет не подходит.");
            else
                log?.Report("Зафиксированных аффиксов не обнаружено — возможно, орб не применился или предмет уже был заблокирован.");

            return CraftResult.LimitReached(1);
        }
        catch (OperationCanceledException)
        {
            if (shiftHeld) Win32Input.ReleaseShift();
            Win32Input.ReleaseCtrlAlt();
            log?.Report("Остановлено пользователем.");
            return CraftResult.Stopped(0);
        }
        catch (Exception ex)
        {
            if (shiftHeld) Win32Input.ReleaseShift();
            Win32Input.ReleaseCtrlAlt();
            craftLog?.WriteValidationError("Исключение: " + ex.Message);
            log?.Report("Ошибка: " + ex.Message);
            return CraftResult.Failed(0);
        }
        finally
        {
            Win32Input.AltUp();
            Win32Input.InputTrace = prevTrace;
        }
    }

    private void LogMove(IProgress<string>? log, string label, int x, int y)
    {
        if (TraceInputToLog)
            log?.Report($"[Ввод] {label}: SetCursorPos({x},{y})");

        if (!Win32Input.MoveTo(x, y))
        {
            var err = Marshal.GetLastWin32Error();
            log?.Report($"[Ввод] ОШИБКА SetCursorPos({x},{y}): Win32 код {err}");
        }
        else if (TraceInputToLog && Win32Input.TryGetCursorPos(out var ax, out var ay))
        {
            log?.Report($"[Ввод] курсор после перемещения: ({ax},{ay})");
        }
    }
}
