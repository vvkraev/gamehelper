namespace GameHelper.Services;

/// <summary>
/// Координирует запуск крафт-сессии: выбирает нужный сервис по <see cref="CraftMode"/>,
/// итерирует по ячейкам предметов, ведёт учёт попыток и управляет файловым логом.
/// UI-логика (MessageBox, кнопки, CTS) остаётся в MainWindow.
/// </summary>
public sealed class CraftOrchestrator
{
    private readonly ChaosCraftService _chaos;
    private readonly AugAnnulCraftService _augAnnul;
    private readonly ExaltationCraftServiceFracturedSide _exalt;
    private readonly FracturingOrbService _fracturing;
    private readonly OmenActivationService _omen;
    private readonly DivineCraftService _divine;

    public CraftOrchestrator(
        ChaosCraftService chaos,
        AugAnnulCraftService augAnnul,
        ExaltationCraftServiceFracturedSide exalt,
        FracturingOrbService fracturing,
        OmenActivationService omen,
        DivineCraftService divine)
    {
        _chaos = chaos;
        _augAnnul = augAnnul;
        _exalt = exalt;
        _fracturing = fracturing;
        _omen = omen;
        _divine = divine;
    }

    /// <summary>
    /// Запускает крафт-сессию согласно <paramref name="ctx"/>.
    /// Не бросает исключений кроме <see cref="OperationCanceledException"/> (отмена через <paramref name="ct"/>).
    /// </summary>
    public async Task<CraftSessionResult> RunAsync(
        CraftSessionContext ctx,
        IProgress<string> progress,
        CancellationToken ct)
    {
        ApplySettings(ctx);

        var cells = ctx.ItemCells;
        var maxOps = ctx.MaxOps;
        var mode = ctx.Mode;

        ChaosCraftResult result = ChaosCraftResult.MaxAttemptsReached;
        var remaining = maxOps;
        var offset = 0;
        var orbsActualTotal = 0;
        var orbsActualTracked = false;
        CraftRunFileLog? craftFile = null;
        string? wipPath = null;
        string? precheckErrorMsg = null;
        string? precheckErrorTitle = null;

        SessionLogger.Info(
            $"--- запуск крафта ({mode}): ячеек предмета {cells.Count}, N={maxOps} общий на сессию, " +
            $"задержка мыши {ctx.MouseActionDelayMs} мс, после Ctrl+Alt+C {ctx.ClipboardDelayMs} мс; " +
            $"лог крафта — в папке Log после завершения ---");

        try
        {
            for (var ci = 0; ci < cells.Count; ci++)
            {
                ct.ThrowIfCancellationRequested();

                if (remaining < 1)
                {
                    SessionLogger.Info(
                        $"Общий лимит N={maxOps} исчерпан ({offset} попыток уже сделано); ячейки {ci + 1}…{cells.Count} не обрабатываются.");
                    result = ChaosCraftResult.MaxAttemptsReached;
                    break;
                }

                var item = cells[ci];
                SessionLogger.Info(
                    $"--- ячейка предмета {ci + 1} / {cells.Count}: X={item.X},Y={item.Y} (осталось попыток в сессии: {remaining}) ---");

                var pre = await RunPrecheckAsync(ctx, item, progress, ct);

                if (pre.Outcome == CraftPrecheckOutcome.AlreadySatisfied)
                {
                    SessionLogger.Info(
                        $"Предпроверка: ячейка {ci + 1} — условие остановки уже выполнено, орб не тратим — переход к следующей ячейке.");
                    if (ci + 1 < cells.Count)
                        SessionLogger.Info("Следующая ячейка будет проверена так же.");
                    continue;
                }

                if (pre.Outcome == CraftPrecheckOutcome.EmptyCell)
                {
                    SessionLogger.Info(
                        $"Предпроверка: ячейка {ci + 1} — буфер пуст после Ctrl+Alt+C, считаем ячейку пустой — переход к следующей ячейке.");
                    continue;
                }

                if (pre.Outcome == CraftPrecheckOutcome.NonMagicCell)
                {
                    SessionLogger.Info(
                        $"Предпроверка: ячейка {ci + 1} — предмет не Magic, пропускаем ячейку (режим «Ауг+Аннул»).");
                    continue;
                }

                if (pre.Outcome == CraftPrecheckOutcome.AffixesMissing)
                {
                    SessionLogger.Info(
                        $"Предпроверка: ячейка {ci + 1} — не все аффиксы из условия найдены на предмете, пропускаем (Divine Orb не тратится). {pre.Message}");
                    continue;
                }

                if (pre.Outcome == CraftPrecheckOutcome.Failed)
                {
                    precheckErrorMsg = pre.Message;
                    precheckErrorTitle = string.IsNullOrEmpty(pre.Title) ? "Крафт" : pre.Title;
                    result = ChaosCraftResult.Error;
                    break;
                }

                if (mode is CraftMode.AugAnnul or CraftMode.Exaltation && pre.ParsedItem is null)
                {
                    precheckErrorMsg = "Внутренняя ошибка: предпроверка не вернула ParsedItem.";
                    precheckErrorTitle = "Крафт";
                    result = ChaosCraftResult.Error;
                    break;
                }

                craftFile ??= CreateCraftLog(ctx);
                wipPath = craftFile.WipPath;
                craftFile.SetCurrentCell(ci + 1, cells.Count);

                var cr = await RunCraftAsync(ctx, item, pre, remaining, maxOps, offset, progress, ct, craftFile);
                offset += cr.Attempts;
                remaining -= cr.Attempts;
                result = cr.StopReason;
                if (cr.ActualOrbsConsumed >= 0)
                {
                    orbsActualTotal += cr.ActualOrbsConsumed;
                    orbsActualTracked = true;
                }

                if (result is ChaosCraftResult.Cancelled or ChaosCraftResult.Error)
                    break;

                if (result == ChaosCraftResult.EmptyCell)
                {
                    SessionLogger.Info(
                        $"Крафт: ячейка {ci + 1} — буфер пуст после Ctrl+Alt+C, считаем ячейку пустой — переход к следующей ячейке (осталось попыток: {remaining}).");
                    continue;
                }

                if (result == ChaosCraftResult.AffixesMissing)
                {
                    SessionLogger.Info(
                        $"Крафт: ячейка {ci + 1} — не все аффиксы из условия найдены, переходим к следующей ячейке (осталось попыток: {remaining}).");
                    continue;
                }

                if (result == ChaosCraftResult.AffixFound)
                {
                    if (mode == CraftMode.FracturingOrb)
                    {
                        SessionLogger.Info($"Нужный аффикс зафиксирован в ячейке {ci + 1} — сессия Fracturing Orb завершена.");
                        break;
                    }

                    if (ci + 1 < cells.Count)
                        SessionLogger.Info(
                            $"Условие остановки выполнено для ячейки {ci + 1} — автоматически продолжаем со следующей предметной ячейки (осталось попыток: {remaining}).");
                    else
                        SessionLogger.Info($"Условие остановки выполнено для ячейки {ci + 1} (последняя в сетке).");
                }
            }
        }
        catch (OperationCanceledException)
        {
            result = ChaosCraftResult.Cancelled;
        }
        finally
        {
            if (craftFile != null)
            {
                craftFile.SetOutcome(result);
                craftFile.Dispose();
            }
        }

        var allSatisfied = craftFile == null && precheckErrorMsg == null && result is not ChaosCraftResult.Cancelled;

        if (allSatisfied)
            SessionLogger.Info("Сессия: все ячейки уже удовлетворяют условию — файл лога крафта не создавался.");
        else if (craftFile != null)
        {
            var orbSuffix = orbsActualTracked
                ? $"; кликов={offset}, орбов потреблено={orbsActualTotal} (overflow-возвраты не считаются)"
                : string.Empty;
            SessionLogger.Info($"Итог крафта: {result}{orbSuffix}");
        }
        else if (result == ChaosCraftResult.Cancelled)
            SessionLogger.Info("Итог крафта: отмена (файл лога не создавался).");

        return new CraftSessionResult
        {
            FinalResult = result,
            TotalAttempts = offset,
            TotalActualOrbsConsumed = orbsActualTracked ? orbsActualTotal : -1,
            AllCellsAlreadySatisfied = allSatisfied,
            ActiveCraftLogWipPath = wipPath,
            PrecheckErrorMessage = precheckErrorMsg,
            PrecheckErrorTitle = precheckErrorTitle,
        };
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private void ApplySettings(CraftSessionContext ctx)
    {
        var hover = Math.Clamp(ctx.ClipboardDelayMs / 2, 80, 220);

        _chaos.MouseActionDelayMs = ctx.MouseActionDelayMs;
        _chaos.ClipboardDelayMs = ctx.ClipboardDelayMs;
        _chaos.HoverSettleBeforeClipboardMs = hover;
        _chaos.TraceInputToLog = ctx.TraceInputToLog;
        _chaos.StepConfirmAsync = ctx.StepConfirmAsync;

        _augAnnul.MouseActionDelayMs = ctx.MouseActionDelayMs;
        _augAnnul.ClipboardDelayMs = ctx.ClipboardDelayMs;
        _augAnnul.HoverSettleBeforeClipboardMs = hover;
        _augAnnul.TraceInputToLog = ctx.TraceInputToLog;
        _augAnnul.StepConfirmAsync = ctx.StepConfirmAsync;

        _fracturing.MouseActionDelayMs = ctx.MouseActionDelayMs;
        _fracturing.ClipboardDelayMs = ctx.ClipboardDelayMs;
        _fracturing.HoverSettleBeforeClipboardMs = hover;
        _fracturing.TraceInputToLog = ctx.TraceInputToLog;
        _fracturing.StepConfirmAsync = ctx.StepConfirmAsync;

        _exalt.MouseActionDelayMs = ctx.MouseActionDelayMs;
        _exalt.ClipboardDelayMs = ctx.ClipboardDelayMs;
        _exalt.HoverSettleBeforeClipboardMs = hover;
        _exalt.TraceInputToLog = ctx.TraceInputToLog;
        _exalt.SchemaTraceToLog = ctx.SchemaTraceToLog;
        _exalt.StepConfirmAsync = ctx.StepConfirmAsync;

        _omen.MouseActionDelayMs = ctx.MouseActionDelayMs;
        _omen.ClipboardDelayMs = ctx.ClipboardDelayMs;
        _omen.TraceInputToLog = ctx.TraceInputToLog;

        var divineHover = Math.Clamp(ctx.ClipboardDelayMs / 2, 80, 220);
        _divine.MouseActionDelayMs = ctx.MouseActionDelayMs;
        _divine.ClipboardDelayMs = ctx.ClipboardDelayMs;
        _divine.HoverSettleBeforeClipboardMs = divineHover;
        _divine.TraceInputToLog = ctx.TraceInputToLog;
        _divine.StepConfirmAsync = ctx.StepConfirmAsync;
    }

    private Task<CraftPrecheckResult> RunPrecheckAsync(
        CraftSessionContext ctx, ScreenRect item, IProgress<string> progress, CancellationToken ct) =>
        ctx.Mode switch
        {
            CraftMode.Exaltation   => _exalt.PrecheckAsync(item, ctx.Plan, progress, ct),
            CraftMode.FracturingOrb => _fracturing.PrecheckAsync(item, ctx.Plan, progress, ct),
            CraftMode.AugAnnul     => _augAnnul.PrecheckAsync(item, ctx.Plan, progress, ct),
            CraftMode.Divine       => _divine.PrecheckAsync(item, ctx.Plan, progress, ct),
            _                      => _chaos.PrecheckAsync(item, ctx.Plan, progress, ct),
        };

    private Task<CraftResult> RunCraftAsync(
        CraftSessionContext ctx,
        ScreenRect item,
        CraftPrecheckResult pre,
        int remaining,
        int maxOps,
        int offset,
        IProgress<string> progress,
        CancellationToken ct,
        CraftRunFileLog craftFile) =>
        ctx.Mode switch
        {
            CraftMode.Exaltation => _exalt.RunAsync(
                ctx.ExaltRect, ctx.AnnulRect,
                ctx.RitualInventoryRect, ctx.CurrencyInventoryRect,
                ctx.OmenSinistralRect, ctx.OmenDextralRect, ctx.OmenGreaterRect,
                ctx.OmenSinistralCells, ctx.OmenDextralCells, ctx.OmenGreaterCells,
                item, ctx.Plan, ctx.ConditionSummary, pre.ParsedItem!, pre.ClipboardText,
                remaining, maxOps, offset, progress, ct, craftFile),

            CraftMode.FracturingOrb => _fracturing.RunAsync(
                ctx.OrbRect, item, ctx.Plan, ctx.ConditionSummary,
                maxOps, offset, progress, ct, craftFile),

            CraftMode.AugAnnul => _augAnnul.RunAsync(
                ctx.AugRect, ctx.AnnulRect,
                item, ctx.Plan, ctx.ConditionSummary,
                pre.ParsedItem!, pre.ClipboardText,
                remaining, maxOps, offset, progress, ct, craftFile),

            CraftMode.Divine => _divine.RunAsync(
                ctx.OrbRect, item, ctx.Plan, ctx.ConditionSummary,
                remaining, maxOps, offset, progress, ct, craftFile),

            _ => _chaos.RunAsync(
                ctx.OrbRect, item, ctx.Plan, ctx.ConditionSummary,
                remaining, maxOps, offset, progress, ct, craftFile),
        };

    private static CraftRunFileLog CreateCraftLog(CraftSessionContext ctx)
    {
        var cells = ctx.ItemCells;
        return ctx.Mode switch
        {
            CraftMode.Exaltation => CraftRunFileLog.Begin(
                "Orb of Exaltation", ctx.ExaltRect, ctx.AnnulRect, "Orb of Annulment",
                cells[0], ctx.MaxOps, ctx.ConditionSummary, cells),

            CraftMode.FracturingOrb => CraftRunFileLog.Begin(
                ctx.OrbRect, cells[0], ctx.MaxOps, ctx.ConditionSummary, cells, "Fracturing Orb"),

            CraftMode.AugAnnul => CraftRunFileLog.Begin(
                "Orb of Augmentation", ctx.AugRect, ctx.AnnulRect, "Orb of Annulment",
                cells[0], ctx.MaxOps, ctx.ConditionSummary, cells, ctx.AugOrbDisplayName),

            CraftMode.Divine => CraftRunFileLog.Begin(
                ctx.OrbRect, cells[0], ctx.MaxOps, ctx.ConditionSummary, cells, "Divine Orb"),

            _ => CraftRunFileLog.Begin(
                ctx.OrbRect, cells[0], ctx.MaxOps, ctx.ConditionSummary, cells, ctx.OrbDisplayName),
        };
    }
}
