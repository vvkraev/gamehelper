using System.Runtime.InteropServices;
using System.Windows;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Цикл крафта по SRS: Shift+ПКМ орб, ЛКМ предмет, Ctrl+C, проверка буфера.</summary>
public sealed class ChaosCraftService
{
    /// <summary>Задержка (мс) после каждого перемещения мыши и после каждого клика (ЛКМ/ПКМ), включая паузу перед следующим таким шагом.</summary>
    public int MouseActionDelayMs { get; set; } = 80;

    /// <summary>Ожидание после Ctrl+C перед чтением буфера.</summary>
    public int ClipboardDelayMs { get; set; } = 220;

    /// <summary>
    /// В лог попадут шаги: MoveTo, клики, Shift, Ctrl+C и фактическая позиция курсора после перемещения.
    /// Включайте при отладке; в релизе обычно выкл.
    /// </summary>
    public bool TraceInputToLog { get; set; }

    /// <summary>
    /// Если задано, после каждого действия ввода показывается модальное окно; по закрытию без «Продолжить» — отмена через <see cref="CancellationToken"/>.
    /// </summary>
    public Func<string, Task>? StepConfirmAsync { get; set; }

    public async Task<ChaosCraftResult> RunAsync(
        ScreenRect orbArea,
        ScreenRect itemArea,
        string affixPattern,
        int minRoll,
        int maxOperations,
        IProgress<string>? log,
        CancellationToken cancellationToken,
        CraftRunFileLog? craftLog = null)
    {
        if (maxOperations < 1)
        {
            craftLog?.WriteValidationError("N должно быть не меньше 1.");
            log?.Report("N должно быть не меньше 1.");
            return ChaosCraftResult.Error;
        }

        var pattern = affixPattern.Trim();
        if (pattern.Length == 0)
        {
            craftLog?.WriteValidationError("Шаблон аффикса пуст.");
            log?.Report("Шаблон аффикса пуст.");
            return ChaosCraftResult.Error;
        }

        var (ox, oy) = orbArea.Center;
        var (ix, iy) = itemArea.Center;
        var (hoverX, hoverY) = itemArea.GetInteriorPoint(1);

        if (TraceInputToLog)
        {
            log?.Report($"[Ввод] координаты: орб центр=({ox},{oy}), предмет ЛКМ=({ix},{iy}), предмет перед Ctrl+C=({hoverX},{hoverY})");
            log?.Report($"[Ввод] задержка мыши={MouseActionDelayMs} мс, после Ctrl+C={ClipboardDelayMs} мс");
        }

        for (var attempt = 1; attempt <= maxOperations; attempt++)
        {
            log?.Report($"Операция {attempt} / {maxOperations}…");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogMove(log, "MoveTo Chaos Orb", ox, oy);
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Курсор перемещён к области Chaos Orb (координаты: {ox}, {oy}).",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogKey(log, "Shift DOWN");
                Win32Input.ShiftDown();
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Нажат и удерживается Shift.",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogMouse(log, "ПКМ (орб)");
                Win32Input.ClickRight();
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Выполнен правый клик по Chaos Orb (Shift+ПКМ).",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogMove(log, "MoveTo предмет (ЛКМ)", ix, iy);
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Курсор перемещён к области предмета ({ix}, {iy}) для левого клика.",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogMouse(log, "ЛКМ (предмет)");
                Win32Input.ClickLeft();
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Выполнен левый клик по предмету (применение орба).",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogKey(log, "Shift UP");
                Win32Input.ShiftUp();
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Отпущен Shift.",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                // Наведение внутри области предмета непосредственно перед копированием (Ctrl+C)
                LogMove(log, "MoveTo предмет перед Ctrl+C", hoverX, hoverY);
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Курсор перемещён внутрь области предмета перед копированием ({hoverX}, {hoverY}).",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);

                LogKey(log, "Ctrl+C");
                Win32Input.SendCtrlC();
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Нажаты Ctrl+C — копирование текста предмета в буфер обмена.",
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(ClipboardDelayMs, cancellationToken).ConfigureAwait(false);

                var clip = await Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
                await StepPauseIfNeeded(
                    $"Попытка {attempt} из {maxOperations}.{Environment.NewLine}Текст прочитан из буфера обмена. Далее — сравнение с искомым аффиксом.",
                    cancellationToken).ConfigureAwait(false);

                var match = AffixMatch.TryMatch(clip, pattern, minRoll, out var explanation);

                craftLog?.WriteComparison(
                    attempt,
                    maxOperations,
                    clip,
                    pattern,
                    minRoll,
                    match,
                    explanation);

                log?.Report(
                    $"Сравнение (попытка {attempt}): {explanation} | буфер (фрагмент): «{TruncateForUi(clip)}»");

                if (match)
                {
                    log?.Report($"Аффикс найден (попытка {attempt}).");
                    return ChaosCraftResult.AffixFound;
                }
            }
            catch (OperationCanceledException)
            {
                Win32Input.ReleaseShift();
                log?.Report("Остановлено пользователем.");
                return ChaosCraftResult.Cancelled;
            }
            catch (Exception ex)
            {
                Win32Input.ReleaseShift();
                craftLog?.WriteValidationError("Исключение: " + ex.Message);
                log?.Report("Ошибка: " + ex.Message);
                return ChaosCraftResult.Error;
            }
            finally
            {
                Win32Input.ReleaseShift();
            }

            await Task.Delay(MouseActionDelayMs, cancellationToken).ConfigureAwait(false);
        }

        log?.Report($"Достигнут лимит операций ({maxOperations}), аффикс не найден.");
        return ChaosCraftResult.MaxAttemptsReached;
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

    private static string TruncateForUi(string s, int max = 120)
    {
        if (s.Length <= max)
            return s;

        return s[..max] + "…";
    }

    private static string GetClipboardTextSafe()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
