using System.Runtime.InteropServices;
using System.Windows;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Результат проверки предмета до применения орба.</summary>
public enum CraftPrecheckOutcome
{
    /// <summary>Можно тратить попытки N (условие ещё не выполнено).</summary>
    Ready,

    /// <summary>Предмет уже удовлетворяет условию — ячейку пропускаем, N не тратим.</summary>
    AlreadySatisfied,

    /// <summary>Буфер пуст после Ctrl+Alt+C — ячейка пустая, пропускаем её.</summary>
    EmptyCell,

    /// <summary>Предмет не Magic (для режимов, где требуется Magic).</summary>
    NonMagicCell,

    /// <summary>Нет текста / класс не совпал — сессию по этой ячейке нельзя продолжить.</summary>
    Failed,
}

/// <param name="Outcome"><see cref="CraftPrecheckOutcome.Ready"/> — запускать RunAsync для этой ячейки.</param>
public readonly record struct CraftPrecheckResult(
    CraftPrecheckOutcome Outcome,
    string Title,
    string Message,
    ParsedItem? ParsedItem = null,
    string? ClipboardText = null)
{
    public bool CanStart => Outcome == CraftPrecheckOutcome.Ready;
}

/// <summary>Цикл крафта по SRS: Shift+ПКМ орб, ЛКМ предмет, Ctrl+Alt+C, проверка буфера.</summary>
public sealed class ChaosCraftService : IChaosCraftService
{
    private const double DelayJitterFraction = 0.30;

    /// <summary>Задержка (мс) после каждого перемещения мыши и после каждого клика (ЛКМ/ПКМ), включая паузу перед следующим таким шагом.</summary>
    public int MouseActionDelayMs { get; set; } = 80;

    /// <summary>Ожидание после Ctrl+Alt+C перед чтением буфера.</summary>
    public int ClipboardDelayMs { get; set; } = 220;

    /// <summary>
    /// Дополнительная пауза после наведения на ячейку предмета и перед Ctrl+Alt+C — игра успевает обновить подсказку/цель копирования при смене ячейки.
    /// </summary>
    public int HoverSettleBeforeClipboardMs { get; set; } = 120;

    /// <summary>
    /// В лог попадут шаги: MoveTo, клики, Shift, Ctrl+Alt+C и фактическая позиция курсора после перемещения.
    /// Включайте при отладке; в релизе обычно выкл.
    /// </summary>
    public bool TraceInputToLog { get; set; }

    /// <summary>
    /// Если задано, после каждого действия ввода показывается модальное окно; по закрытию без «Продолжить» — отмена через <see cref="CancellationToken"/>.
    /// </summary>
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
            if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !itemArea.ContainsPoint(curX, curY, inset: 1))
                LogMove(log, $"{tag}: MoveTo предмет перед Ctrl+Alt+C", x, y);
            LogAltState(log, $"{tag}: MoveTo(item)");
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            await Task.Delay(HoverSettleBeforeClipboardMs, ct).ConfigureAwait(false);
            LogCtrlAltCSequence(log, tag);
            LogKey(log, $"{tag}: Ctrl+Alt+C (send)");
            Win32Input.SendCtrlAltC();
            LogAltState(log, $"{tag}: SendCtrlAltC()");
            await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
            var text = await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
            SessionLogger.InfoClipboard(tag, text);
            return text;
        }

        var first = await OnceAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        // Retry: буфер может быть пуст из-за фокуса/обновления подсказки/гонки чтения.
        log?.Report($"{tag}: буфер пуст, повтор Ctrl+Alt+C (retry) …");
        await Task.Delay(1000, ct).ConfigureAwait(false);
        var second = await OnceAsync().ConfigureAwait(false);
        return second;
    }

    /// <summary>Очищает буфер обмена на UI-потоке, чтобы пустая ячейка не маскировалась старым текстом.</summary>
    public Task ClearClipboardAsync() =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(ClearClipboardSafe).Task;

    private static void ClearClipboardSafe()
    {
        try
        {
            System.Windows.Clipboard.Clear();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Читает текст предмета из буфера (наведение в область предмета, Ctrl+Alt+C). Без применения орба.
    /// </summary>
    public async Task<string> ReadItemClipboardTextAsync(
        ScreenRect itemArea,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(80, cancellationToken).ConfigureAwait(false);

        // важное: очищаем буфер перед Ctrl+Alt+C, чтобы «старый текст» не маскировал пустую ячейку
        await ClearClipboardAsync().ConfigureAwait(false);

        // Центр с отступом — стабильное попадание в иконку предмета; случайная точка давала старый буфер при смене ячейки.
        var (hoverX, hoverY) = itemArea.GetInteriorPoint(1);

        await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

        LogMove(log, "Проверка: MoveTo предмет (центр области) перед Ctrl+Alt+C", hoverX, hoverY);
        await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);
        await Task.Delay(HoverSettleBeforeClipboardMs, cancellationToken).ConfigureAwait(false);

        try
        {
            LogKey(log, "Ctrl+Alt+C (предмет)");
            LogCtrlAltCSequence(log, "предпроверка / Ctrl+Alt+C");
            Win32Input.SendCtrlAltC();
            LogAltState(log, "предпроверка: SendCtrlAltC()");
            await DelayJitterAsync(ClipboardDelayMs, cancellationToken).ConfigureAwait(false);

            var text = await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
            SessionLogger.InfoClipboard("предпроверка / чтение предмета", text);
            return text;
        }
        finally { }
    }

    /// <summary>
    /// Чтение предмета из буфера без орба: разбор, класс.
    /// <see cref="CraftPrecheckOutcome.Ready"/> — можно применять орб;
    /// <see cref="CraftPrecheckOutcome.AlreadySatisfied"/> — ячейку пропускаем (как после успешного крафта);
    /// <see cref="CraftPrecheckOutcome.Failed"/> — ошибка парсинга или класса.
    /// </summary>
    public async Task<CraftPrecheckResult> PrecheckAsync(
        ScreenRect itemArea,
        CraftConditionPlan plan,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        log?.Report("Предварительная проверка: чтение предмета в буфер (Ctrl+Alt+C)…");
        var clip = await ReadItemClipboardTextAsync(itemArea, log, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(clip))
        {
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.EmptyCell,
                "Пустая ячейка",
                "После Ctrl+Alt+C буфер пуст — вероятно, в этой ячейке нет предмета.",
                null,
                clip);
        }

        var parsed = ItemParser.Parse(clip);
        if (parsed is not { IsValid: true })
        {
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.Failed,
                "Предмет",
                "Не удалось разобрать текст предмета из буфера. Убедитесь, что область предмета задана верно и в буфере — текст из игры (Ctrl+Alt+C по предмету).",
                null,
                clip);
        }

        MergeAffixLibraryFromParsedItemIfValid(parsed, log);

        if (!ParsedItemCraftEvaluator.ItemClassMatches(parsed, plan.ExpectedItemClass))
        {
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.Failed,
                "Класс предмета",
                $"В буфере класс «{parsed.ItemClass}», а в условии крафта указан «{plan.ExpectedItemClass}». " +
                "Откройте «Условие крафта» и выберите тот же класс, что у предмета на экране, или поставьте другой предмет.",
                parsed,
                clip);
        }

        if (CraftConditionEvaluator.TryEvaluate(plan, parsed, out _))
        {
            return new CraftPrecheckResult(
                CraftPrecheckOutcome.AlreadySatisfied,
                "Условие уже выполнено",
                "Предмет в этой ячейке уже удовлетворяет условию остановки крафта.",
                parsed,
                clip);
        }

        return new CraftPrecheckResult(CraftPrecheckOutcome.Ready, "", "", parsed, clip);
    }

    /// <summary>
    /// Основной цикл Chaos Orb: Shift+ПКМ орб → ЛКМ предмет → Ctrl+Alt+C → проверка условия; повторяет до выполнения
    /// условия, исчерпания <paramref name="segmentMaxOperations"/> или отмены токена.
    /// </summary>
    /// <param name="segmentMaxOperations">Сколько попыток разрешено в этом вызове (остаток общего бюджета).</param>
    /// <param name="globalTotal">Общий N сессии — для подписей «попытка k / N».</param>
    /// <param name="globalAttemptOffset">Сколько полных попыток уже сделано ранее в этой сессии (по всем предыдущим ячейкам).</param>
    /// <returns>Итог и число израсходованных в этом вызове попыток (для вычитания из общего N).</returns>
    public async Task<CraftResult> RunAsync(
        ScreenRect orbArea,
        ScreenRect itemArea,
        CraftConditionPlan plan,
        string conditionSummary,
        int segmentMaxOperations,
        int globalTotal,
        int globalAttemptOffset,
        IProgress<string>? log,
        CancellationToken cancellationToken,
        CraftRunFileLog? craftLog = null)
    {
        // Включаем детальный трейс Alt/Ctrl на время выполнения.
        var prevTrace = Win32Input.InputTrace;
        if (TraceInputToLog)
            Win32Input.InputTrace = msg => log?.Report("[Ввод][AltTraceRaw] " + msg);

        if (segmentMaxOperations < 1)
        {
            craftLog?.WriteValidationError("Остаток попыток (N) для ячейки должен быть не меньше 1.");
            log?.Report("Остаток попыток (N) для ячейки должен быть не меньше 1.");
            Win32Input.InputTrace = prevTrace;
            return CraftResult.Failed();
        }

        if (globalTotal < 1)
        {
            craftLog?.WriteValidationError("Общий лимит N должен быть не меньше 1.");
            log?.Report("Общий лимит N должен быть не меньше 1.");
            Win32Input.InputTrace = prevTrace;
            return CraftResult.Failed();
        }

        var pattern = conditionSummary.Trim();
        if (pattern.Length == 0)
            pattern = "(разбор предмета, ItemParser)";

        if (TraceInputToLog)
            log?.Report($"[Ввод] задержка мыши={MouseActionDelayMs} мс, после Ctrl+Alt+C={ClipboardDelayMs} мс; клики — случайная точка внутри области");

        var orbSelected = false;
        var shiftHeld = false;
        // CRAFT-11: отслеживаем фактическое потребление орбов при overflow.
        // preClip в начале итерации N = пост-состояние орба из итерации N-1,
        // поэтому дополнительные clipboard-читы не нужны.
        string? prevPreClip = null;
        var orbsActuallyConsumed = 0;
        try
        {
            for (var attempt = 1; attempt <= segmentMaxOperations; attempt++)
            {
                var displayAttempt = globalAttemptOffset + attempt;
                log?.Report($"Операция {displayAttempt} / {globalTotal}…");

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 1) Сначала проверяем условие на текущем предмете (до применения орба).
                    _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
                    await Task.Delay(80, cancellationToken).ConfigureAwait(false);

                    var preClip = await ReadClipboardAfterCtrlAltCAsync(
                        itemArea,
                        log,
                        cancellationToken,
                        $"хаос: проверка перед орбом, попытка {displayAttempt} / {globalTotal}").ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(preClip))
                    {
                        log?.Report("Буфер пуст после Ctrl+Alt+C — ячейка, вероятно, пустая. Переходим к следующей ячейке.");
                        if (shiftHeld)
                            Win32Input.ReleaseShift();
                        return CraftResult.Empty(0);
                    }

                    // CRAFT-11: сравниваем текущий preClip с предыдущим.
                    // Если текст изменился — орб из предыдущей итерации был потреблён.
                    // Если нет — игра вернула орб (overflow-защита суффиксов).
                    if (prevPreClip != null)
                    {
                        if (preClip != prevPreClip)
                        {
                            orbsActuallyConsumed++;
                        }
                        else
                        {
                            log?.Report(
                                $"[Overflow] Орб возвращён: предмет не изменился после клика {displayAttempt - 1}. " +
                                $"Потреблено орбов: {orbsActuallyConsumed} из {attempt - 1} кликов.");
                        }
                    }
                    prevPreClip = preClip;

                    var preParsed = ItemParser.Parse(preClip);
                    var alreadyMatch = CraftConditionEvaluator.TryEvaluate(plan, preParsed, out var preExplanation);
                    craftLog?.WriteComparison(displayAttempt, globalTotal, preClip, pattern, alreadyMatch, "[проверка перед орбом] " + preExplanation);
                    log?.Report($"Проверка (попытка {displayAttempt}): {preExplanation}");
                    if (alreadyMatch)
                    {
                        log?.Report("Условие уже выполнено — орб не применяется, переходим к следующей ячейке.");
                        if (shiftHeld)
                            Win32Input.ReleaseShift();
                        return CraftResult.Found(attempt - 1, preClip, orbsActuallyConsumed);
                    }

                    // 2) Условие не выполнено — применяем Chaos Orb (расход попытки), удерживая Shift между попытками.
                    await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                    if (!orbSelected)
                    {
                        var (ox, oy) = orbArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                        if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !orbArea.ContainsPoint(curX, curY, inset: 1))
                            LogMove(log, "MoveTo Chaos Orb (случайная точка)", ox, oy);
                        LogAltState(log, "MoveTo(Chaos Orb)");
                        await StepPauseIfNeeded(
                            $"Попытка {displayAttempt} из {globalTotal}.{Environment.NewLine}Курсор перемещён к области Chaos Orb (случайные координаты: {ox}, {oy}).",
                            cancellationToken).ConfigureAwait(false);
                        await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                        LogKey(log, "Shift DOWN");
                        Win32Input.ShiftDown();
                        LogAltState(log, "ShiftDown()");
                        shiftHeld = true;
                        await StepPauseIfNeeded(
                            $"Попытка {displayAttempt} из {globalTotal}.{Environment.NewLine}Нажат и удерживается Shift.",
                            cancellationToken).ConfigureAwait(false);
                        await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                        LogMouse(log, "ПКМ (орб)");
                        Win32Input.ClickRight();
                        LogAltState(log, "ClickRight(orb)");
                        await StepPauseIfNeeded(
                            $"Попытка {displayAttempt} из {globalTotal}.{Environment.NewLine}Выполнен правый клик по Chaos Orb (Shift+ПКМ).",
                            cancellationToken).ConfigureAwait(false);
                        await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                        orbSelected = true;
                    }

                    var (ix, iy) = itemArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                    if (!Win32Input.TryGetCursorPos(out var curIX, out var curIY) || !itemArea.ContainsPoint(curIX, curIY, inset: 1))
                        LogMove(log, "MoveTo предмет (ЛКМ, случайная точка)", ix, iy);
                    LogAltState(log, "MoveTo(item for LClick)");
                    await StepPauseIfNeeded(
                        $"Попытка {displayAttempt} из {globalTotal}.{Environment.NewLine}Курсор перемещён к области предмета ({ix}, {iy}) для левого клика.",
                        cancellationToken).ConfigureAwait(false);
                    await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                    LogMouse(log, "ЛКМ (предмет)");
                    if (TraceInputToLog)
                        log?.Report($"[Ввод][ShiftState] перед ЛКМ: ShiftDown={Win32Input.IsShiftDown()}");
                    Win32Input.ClickLeft();
                    LogAltState(log, "ClickLeft(item)");
                    await StepPauseIfNeeded(
                        $"Попытка {displayAttempt} из {globalTotal}.{Environment.NewLine}Выполнен левый клик по предмету (применение орба).",
                        cancellationToken).ConfigureAwait(false);
                    await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                    // Пост-проверку убрали: условие проверяется только ПЕРЕД применением орба.
                }
                catch (OperationCanceledException)
                {
                    Win32Input.ReleaseShift();
                    shiftHeld = false;
                    LogAltTrace(log, "forced ReleaseCtrlAlt() after cancel");
                    Win32Input.ReleaseCtrlAlt();
                    LogAltState(log, "ReleaseCtrlAlt() after cancel");
                    orbSelected = false;
                    log?.Report("Остановлено пользователем.");
                    return CraftResult.Stopped(attempt - 1, orbsActuallyConsumed);
                }
                catch (Exception ex)
                {
                    Win32Input.ReleaseShift();
                    shiftHeld = false;
                    LogAltTrace(log, "forced ReleaseCtrlAlt() after exception");
                    Win32Input.ReleaseCtrlAlt();
                    LogAltState(log, "ReleaseCtrlAlt() after exception");
                    orbSelected = false;
                    craftLog?.WriteValidationError("Исключение: " + ex.Message);
                    log?.Report("Ошибка: " + ex.Message);
                    return CraftResult.Failed(attempt - 1);
                }
                finally
                {
                    // Shift НЕ отпускаем между попытками. Отпускаем только при выходе из RunAsync.
                    // Принудительно отпускаем Alt после шага, чтобы избежать "залипания" в Alt-menu mode.
                    Win32Input.AltUp();
                    LogAltTrace(log, "forced Alt UP after attempt step");
                    LogAltState(log, "forced AltUp() after attempt step");
                }

                await DelayJitterAsync(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Win32Input.InputTrace = prevTrace;
        }

        if (shiftHeld)
            Win32Input.ReleaseShift();
        log?.Report(
            $"Достигнут лимит попыток для текущей ячейки ({segmentMaxOperations} в блоке, всего в сессии не более {globalTotal}), аффикс не найден.");
        return CraftResult.LimitReached(segmentMaxOperations, orbsActuallyConsumed);
    }

    private async Task StepPauseIfNeeded(string description, CancellationToken cancellationToken)
    {
        if (StepConfirmAsync is null)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        await StepConfirmAsync(description).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
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
        else if (TraceInputToLog && Win32Input.TryGetCursorPos(out var ax, out var ay))
        {
            log?.Report($"[Ввод] курсор после перемещения (GetCursorPos): ({ax},{ay})");
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

    private void LogAltState(IProgress<string>? log, string afterWhat)
    {
        if (!TraceInputToLog)
            return;
        log?.Report($"[Ввод][AltState] after {afterWhat}: AltDown={Win32Input.IsAltDown()}");
    }

    private void LogAltTrace(IProgress<string>? log, string label)
    {
        if (TraceInputToLog)
            log?.Report($"[Ввод][AltTrace] {label}");
    }

    private void LogCtrlAltCSequence(IProgress<string>? log, string tag)
    {
        // Детализируем именно ALT, чтобы отловить лишние "дёргания".
        LogAltTrace(log, $"{tag}: Ctrl DOWN");
        LogAltTrace(log, $"{tag}: Alt DOWN");
        LogAltTrace(log, $"{tag}: C DOWN");
        LogAltTrace(log, $"{tag}: C UP");
        LogAltTrace(log, $"{tag}: Alt UP");
        LogAltTrace(log, $"{tag}: Ctrl UP");
    }

    private static string TruncateForUi(string s, int max = 120)
    {
        if (s.Length <= max)
            return s;

        return s[..max] + "…";
    }

    /// <summary>Как при ручном «Парсинг предмета»: новые ключи аффиксов дописываются в <c>affix_library.json</c>.</summary>
    private static void MergeAffixLibraryFromParsedItemIfValid(ParsedItem? parsed, IProgress<string>? log)
    {
        if (parsed is not { IsValid: true })
            return;

        var added = AffixLibrary.MergeFromParsedItem(parsed);
        if (added > 0)
            log?.Report($"Библиотека аффиксов: добавлено новых записей: {added}.");
    }

    private static string GetClipboardTextSafe()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
