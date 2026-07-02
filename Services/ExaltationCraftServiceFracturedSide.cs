using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Крафт Rare через Orb of Exaltation + омeны + Orb of Annulment по
/// docs/EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt (ветка Fractured / inventory grids).
/// </summary>
public sealed class ExaltationCraftServiceFracturedSide : IExaltationCraftService
{
    private const double DelayJitterFraction = 0.30;

    private readonly OmenActivationService _omen;

    public ExaltationCraftServiceFracturedSide(OmenActivationService omen) => _omen = omen;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public int HoverSettleBeforeClipboardMs { get; set; } = 120;

    public bool TraceInputToLog { get; set; }

    /// <summary>
    /// Лог с префиксом <c>[ExaltSchema]</c>: якорь на схеме в
    /// <c>docs/EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt</c> (вкладка настроек «Трассировка схемы экзальт»).
    /// </summary>
    public bool SchemaTraceToLog { get; set; }

    public Func<string, Task>? StepConfirmAsync { get; set; }

    private void SchemaTrace(IProgress<string>? log, string anchor, string? state = null)
    {
        if (!SchemaTraceToLog || log is null)
            return;
        log.Report(string.IsNullOrEmpty(state)
            ? $"[ExaltSchema] {anchor}"
            : $"[ExaltSchema] {anchor} — {state}");
    }

    private static string FormatRem(RemainingExaltationState r) =>
        $"Rem Sin={r.Sinistral} Dex={r.Dextral} Gr={r.Greater}";

    private enum CraftBranchKind { A, B }

    private enum GreaterRefreshMode { Full, Activate, Deactivate }

    private readonly record struct CraftExaltationAreas(bool UseSinistral, bool UseDextral);

    private sealed class RemainingExaltationState
    {
        public int Sinistral;
        public int Dextral;
        public int Greater;
    }

    private readonly record struct GreaterCellSnap(int Count, bool Activated);

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

    /// <summary>MoveTo в (x,y) только если курсор ещё не внутри <paramref name="region"/> (без лишних движений).</summary>
    private void MoveToRandomInteriorIfOutside(ScreenRect region, IProgress<string>? log, string traceLabel, int x, int y)
    {
        if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !region.ContainsPoint(cx, cy, inset: 1))
            LogMove(log, traceLabel, x, y);
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

    private static bool IsValidRegion(ScreenRect r) => r.Width > 0 && r.Height > 0;

    private static CraftExaltationAreas ResolveCraftExaltationAreas(bool prefixOnly, bool suffixOnly) =>
        new(prefixOnly, suffixOnly);

    private static bool ClipboardHasOmenHeader(string clip)
    {
        var t = (clip ?? "").Replace("\r\n", "\n");
        return t.Contains("Item Class: Omen", StringComparison.OrdinalIgnoreCase)
            && t.Contains("Rarity: Currency", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ClipboardLooksLikeOmen(string clip, string omenName) =>
        ClipboardHasOmenHeader(clip) && (clip ?? "").Contains(omenName, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseStackSizeN(string clip, out int n)
    {
        n = 0;
        var m = Regex.Match(clip ?? "", @"Stack Size:\s*(\d+)\s*/", RegexOptions.IgnoreCase);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var v) || v < 0)
            return false;
        n = v;
        return true;
    }

    private async Task<string> ReadOmenCellClipboardAsync(
        ScreenRect cell,
        IProgress<string>? log,
        CancellationToken ct,
        string tag)
    {
        await ClearClipboardAsync().ConfigureAwait(false);

        var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        MoveToRandomInteriorIfOutside(cell, log, $"{tag}: MoveTo omen cell", x, y);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);

        LogKey(log, $"{tag}: Ctrl+C");
        Win32Input.SendCtrlC();
        await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);

        var text = await ReadClipboardTextAsync().ConfigureAwait(false);
        SessionLogger.InfoClipboard(tag, text);
        return text;
    }

    private async Task<string> ReadOmenCellClipboardWithRetryAsync(
        ScreenRect cell,
        IProgress<string>? log,
        CancellationToken ct,
        string tag)
    {
        var first = await ReadOmenCellClipboardAsync(cell, log, ct, tag).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(first))
            return first;

        log?.Report($"{tag}: буфер пуст, retry через 200ms…");
        await Task.Delay(200, ct).ConfigureAwait(false);
        return await ReadOmenCellClipboardAsync(cell, log, ct, tag + " retry").ConfigureAwait(false);
    }

    /// <summary>Пересчёт Remaining* по сеткам инвентаря (см. RefreshRemainingExaltationOmenStacks в ASCII).</summary>
    private async Task<(bool Ok, string? Error)> TryRefreshRemainingExaltationOmenStacksAsync(
        CraftExaltationAreas areas,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        RemainingExaltationState target,
        IProgress<string>? log,
        CancellationToken ct)
    {
        target.Sinistral = target.Dextral = target.Greater = 0;
        SchemaTrace(log, "RefreshRemainingExaltationOmenStacks", "старт (ASCII: переменные §2, сумма N по ячейкам)");

        if (areas.UseSinistral)
        {
            for (var i = 0; i < omenSinistralCells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var clip = await ReadOmenCellClipboardWithRetryAsync(
                    omenSinistralCells[i],
                    log,
                    ct,
                    $"RefreshRemaining Sinistral {i + 1}/{omenSinistralCells.Count}").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
                    continue;
                if (!ClipboardLooksLikeOmen(clip, OmenActivationService.OmenSinistralExaltationName))
                {
                    return (false,
                        $"В ячейке Sinistral ожидался «{OmenActivationService.OmenSinistralExaltationName}», фактически другой омен или текст.");
                }

                if (!TryParseStackSizeN(clip, out var n))
                    return (false, "Не удалось разобрать Stack Size (Sinistral).");
                target.Sinistral += n;
            }
        }

        if (areas.UseDextral)
        {
            for (var i = 0; i < omenDextralCells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var clip = await ReadOmenCellClipboardWithRetryAsync(
                    omenDextralCells[i],
                    log,
                    ct,
                    $"RefreshRemaining Dextral {i + 1}/{omenDextralCells.Count}").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
                    continue;
                if (!ClipboardLooksLikeOmen(clip, OmenActivationService.OmenDextralExaltationName))
                {
                    return (false,
                        $"В ячейке Dextral ожидался «{OmenActivationService.OmenDextralExaltationName}», фактически другой омен или текст.");
                }

                if (!TryParseStackSizeN(clip, out var n))
                    return (false, "Не удалось разобрать Stack Size (Dextral).");
                target.Dextral += n;
            }
        }

        for (var i = 0; i < omenGreaterCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var clip = await ReadOmenCellClipboardWithRetryAsync(
                omenGreaterCells[i],
                log,
                ct,
                $"RefreshRemaining Greater {i + 1}/{omenGreaterCells.Count}").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
                continue;
            if (!ClipboardLooksLikeOmen(clip, OmenActivationService.OmenGreaterExaltationName))
            {
                return (false,
                    $"В ячейке Greater ожидался «{OmenActivationService.OmenGreaterExaltationName}», фактически другой омен или текст.");
            }

            if (!TryParseStackSizeN(clip, out var n))
                return (false, "Не удалось разобрать Stack Size (Greater).");
            target.Greater += n;
        }

        SchemaTrace(log, "RefreshRemainingExaltationOmenStacks → OK", FormatRem(target));
        return (true, null);
    }

    private async Task<bool> RefreshGreaterExaltationOmenCellStatesAsync(
        GreaterCellSnap[] states,
        GreaterRefreshMode mode,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var modeNote = mode switch
        {
            GreaterRefreshMode.Full => "полный снимок (ASCII §3 GreaterExaltationOmenCellStates)",
            GreaterRefreshMode.Activate => "Activate (AddTwoAffixes шаг 0, после PreviousBranch B)",
            GreaterRefreshMode.Deactivate => "Deactivate (AddOneAffix шаг 0, после PreviousBranch A)",
            _ => mode.ToString()
        };
        SchemaTrace(log, "RefreshGreaterExaltationOmenCellStates", modeNote);

        if (mode == GreaterRefreshMode.Full)
        {
            for (var i = 0; i < omenGreaterCells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var clip = await ReadOmenCellClipboardWithRetryAsync(
                    omenGreaterCells[i],
                    log,
                    ct,
                    $"RefreshGreater full {i + 1}/{omenGreaterCells.Count}").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
                {
                    states[i] = new GreaterCellSnap(0, false);
                    continue;
                }

                if (!ClipboardLooksLikeOmen(clip, OmenActivationService.OmenGreaterExaltationName))
                {
                    log?.Report("RefreshGreater: неверное имя омена в ячейке Greater.");
                    return false;
                }

                if (!TryParseStackSizeN(clip, out var n))
                {
                    log?.Report("RefreshGreater: не удалось разобрать Stack Size.");
                    return false;
                }

                var activated = _omen.IsOmenCellVisuallyActivated(omenGreaterCells[i], omenGreaterCells, i);
                states[i] = new GreaterCellSnap(n, activated);
            }

            return true;
        }

        var snapshot = (GreaterCellSnap[])states.Clone();

        for (var i = 0; i < omenGreaterCells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (snapshot[i].Count <= 0)
                continue;

            var clip = await ReadOmenCellClipboardWithRetryAsync(
                omenGreaterCells[i],
                log,
                ct,
                $"RefreshGreater {mode} {i + 1}/{omenGreaterCells.Count}").ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
            {
                states[i] = new GreaterCellSnap(0, false);
                continue;
            }

            if (!ClipboardLooksLikeOmen(clip, OmenActivationService.OmenGreaterExaltationName))
            {
                log?.Report("RefreshGreater: неверное имя омена.");
                return false;
            }

            if (!TryParseStackSizeN(clip, out var n))
                return false;

            var wasActivated = snapshot[i].Activated;
            var needToggle = mode == GreaterRefreshMode.Activate
                ? !wasActivated
                : wasActivated;

            if (needToggle)
            {
                var cell = omenGreaterCells[i];
                var expectedVis = mode == GreaterRefreshMode.Activate;

                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var (px, py) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                    MoveToRandomInteriorIfOutside(cell, log, $"RefreshGreater {mode}: MoveTo cell", px, py);
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                    LogMouse(log, attempt == 1
                        ? $"RefreshGreater {mode}: ПКМ"
                        : $"RefreshGreater {mode}: повторный ПКМ (проверка визуального состояния)");
                    Win32Input.ClickRight();
                    await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                    await Task.Delay(150, ct).ConfigureAwait(false);
                    await Task.Delay(200, ct).ConfigureAwait(false);

                    clip = await ReadOmenCellClipboardWithRetryAsync(
                            cell,
                            log,
                            ct,
                            $"RefreshGreater {mode} после ПКМ {i + 1} попытка {attempt}")
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
                    {
                        states[i] = new GreaterCellSnap(0, false);
                        break;
                    }

                    if (!TryParseStackSizeN(clip, out n))
                        return false;

                    var visAfter = _omen.IsOmenCellVisuallyActivated(cell, omenGreaterCells, i);
                    if (visAfter == expectedVis)
                    {
                        states[i] = new GreaterCellSnap(n, visAfter);
                        break;
                    }

                    if (attempt == 2)
                    {
                        log?.Report(
                            $"Экзальт RefreshGreater {mode}: ячейка Greater {i + 1}/{omenGreaterCells.Count} — после двух ПКМ визуальное состояние не совпадает с ожидаемым " +
                            $"(ожидалось Activated={expectedVis}, фактически {visAfter}). Остановка крафта.");
                        SchemaTrace(
                            log,
                            "RefreshGreater → STOP",
                            $"{mode} ячейка {i + 1}: визуальная проверка после ПКМ не прошла");
                        return false;
                    }
                }

                continue;
            }

            var vis = _omen.IsOmenCellVisuallyActivated(omenGreaterCells[i], omenGreaterCells, i);
            states[i] = new GreaterCellSnap(n, vis);
        }

        return true;
    }

    private async Task ClickLeftInRegionAsync(ScreenRect region, IProgress<string>? log, CancellationToken ct, string label)
    {
        var (x, y) = region.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        MoveToRandomInteriorIfOutside(region, log, $"{label}: ЛКМ область", x, y);
        await StepPauseIfNeeded($"Курсор к области {label} ({x}, {y}).", ct).ConfigureAwait(false);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        LogMouse(log, $"{label}: ЛКМ");
        Win32Input.ClickLeft();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
    }

    private async Task RepeatCtrlLeftOnRegionAsync(
        ScreenRect region,
        int times,
        IProgress<string>? log,
        CancellationToken ct,
        string label)
    {
        for (var t = 0; t < times; t++)
        {
            ct.ThrowIfCancellationRequested();
            var (x, y) = region.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(region, log, $"{label} {t + 1}/{times}", x, y);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogKey(log, $"{label}: Ctrl+ЛКМ");
            Win32Input.SendCtrlLeftClick();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        }
    }

    private async Task RightClickAllUsedOmenInventoryCellsAsync(
        CraftExaltationAreas areas,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        async Task WalkCellsAsync(IReadOnlyList<ScreenRect> cells, string name)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var cell = cells[i];
                var (x, y) = cell.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                MoveToRandomInteriorIfOutside(cell, log, $"RefillOmen ПКМ {name} {i + 1}/{cells.Count}", x, y);
                await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                LogMouse(log, $"RefillOmen ПКМ {name}");
                Win32Input.ClickRight();
                await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            }
        }

        if (areas.UseSinistral)
            await WalkCellsAsync(omenSinistralCells, "Sinistral").ConfigureAwait(false);
        if (areas.UseDextral)
            await WalkCellsAsync(omenDextralCells, "Dextral").ConfigureAwait(false);
        await WalkCellsAsync(omenGreaterCells, "Greater").ConfigureAwait(false);
    }

    /// <summary>RefillOmen — шаги 1–4 ASCII (ritual → stash-клики → currency → Refresh → ПКМ по ячейкам сеток).</summary>
    private async Task<bool> RefillOmenAsync(
        CraftExaltationAreas areas,
        ScreenRect ritualInventoryRegion,
        ScreenRect currencyInventoryRegion,
        ScreenRect omenSinistralStashRegion,
        ScreenRect omenDextralStashRegion,
        ScreenRect omenGreaterStashRegion,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        RemainingExaltationState rem,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        SchemaTrace(log, "RefillOmen", "вход (ASCII: блок RefillOmen, шаги 1–4)");
        log?.Report("RefillOmen: шаг 1 — фокус ritual inventory (ЛКМ).");
        SchemaTrace(log, "RefillOmen §1", "ЛКМ _ritualInventoryRegion");
        await ClickLeftInRegionAsync(ritualInventoryRegion, log, ct, "ritual inventory").ConfigureAwait(false);

        if (areas.UseSinistral && !areas.UseDextral)
        {
            if (!IsValidRegion(omenSinistralStashRegion) || !IsValidRegion(omenGreaterStashRegion))
            {
                log?.Report("RefillOmen: не заданы области stash Sinistral / Greater.");
                SchemaTrace(log, "RefillOmen → FAIL", "нет областей Sinistral/Greater stash");
                return false;
            }

            log?.Report("RefillOmen: ветка 1.1 Sinistral — 35× Ctrl+ЛКМ Sinistral stash, 25× Greater stash.");
            SchemaTrace(log, "RefillOmen §1.1", "prefix_only: 35× Ctrl+ЛКМ Sinistral stash, 25× Greater stash");
            await RepeatCtrlLeftOnRegionAsync(omenSinistralStashRegion, 35, log, ct, "Sinistral stash").ConfigureAwait(false);
            await RepeatCtrlLeftOnRegionAsync(omenGreaterStashRegion, 25, log, ct, "Greater stash").ConfigureAwait(false);
        }
        else if (areas.UseDextral && !areas.UseSinistral)
        {
            if (!IsValidRegion(omenDextralStashRegion) || !IsValidRegion(omenGreaterStashRegion))
            {
                log?.Report("RefillOmen: не заданы области stash Dextral / Greater.");
                SchemaTrace(log, "RefillOmen → FAIL", "нет областей Dextral/Greater stash");
                return false;
            }

            log?.Report("RefillOmen: ветка 1.2 Dextral — 35× Ctrl+ЛКМ Dextral stash, 25× Greater stash.");
            SchemaTrace(log, "RefillOmen §1.2", "suffix_only: 35× Ctrl+ЛКМ Dextral stash, 25× Greater stash");
            await RepeatCtrlLeftOnRegionAsync(omenDextralStashRegion, 35, log, ct, "Dextral stash").ConfigureAwait(false);
            await RepeatCtrlLeftOnRegionAsync(omenGreaterStashRegion, 25, log, ct, "Greater stash").ConfigureAwait(false);
        }
        else
        {
            if (!IsValidRegion(omenGreaterStashRegion))
            {
                log?.Report("RefillOmen: не задана область Greater stash (ветка mixed).");
                SchemaTrace(log, "RefillOmen → FAIL", "нет области Greater stash (mixed)");
                return false;
            }

            log?.Report("RefillOmen: ветка 1.3 mixed — 1× Ctrl+ПКМ Greater stash.");
            SchemaTrace(log, "RefillOmen §1.3", "mixed: 1× Ctrl+ПКМ Greater stash");
            var (gx, gy) = omenGreaterStashRegion.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            MoveToRandomInteriorIfOutside(omenGreaterStashRegion, log, "RefillOmen Greater stash Ctrl+ПКМ", gx, gy);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogKey(log, "RefillOmen: Ctrl+ПКМ Greater stash");
            Win32Input.SendCtrlRightClick();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        }

        log?.Report("RefillOmen: шаг 2 — ЛКМ currency inventory.");
        SchemaTrace(log, "RefillOmen §2", "ЛКМ _currencyInventoryRegion");
        await ClickLeftInRegionAsync(currencyInventoryRegion, log, ct, "currency inventory").ConfigureAwait(false);

        log?.Report("RefillOmen: шаг 3 — RefreshRemainingExaltationOmenStacks.");
        SchemaTrace(log, "RefillOmen §3", "RefreshRemaining → обновить Remaining*");
        var refresh = await TryRefreshRemainingExaltationOmenStacksAsync(
            areas,
            omenSinistralCells,
            omenDextralCells,
            omenGreaterCells,
            rem,
            log,
            ct).ConfigureAwait(false);
        if (!refresh.Ok)
        {
            log?.Report("RefillOmen: ошибка RefreshRemaining: " + refresh.Error);
            SchemaTrace(log, "RefillOmen §3 → FAIL", refresh.Error ?? "");
            return false;
        }

        log?.Report("RefillOmen: шаг 4 — ПКМ по всем ячейкам используемых сеток омнов.");
        SchemaTrace(log, "RefillOmen §4", "ПКМ по каждой ячейке используемых сеток; " + FormatRem(rem));
        await RightClickAllUsedOmenInventoryCellsAsync(
            areas,
            omenSinistralCells,
            omenDextralCells,
            omenGreaterCells,
            log,
            ct).ConfigureAwait(false);

        SchemaTrace(log, "RefillOmen → OK", "шаги 1–4 завершены; " + FormatRem(rem));
        return true;
    }

    private async Task<bool> EnsureInitialExaltationOmenSupplyAsync(
        CraftExaltationAreas areas,
        ScreenRect ritualInventoryRegion,
        ScreenRect currencyInventoryRegion,
        ScreenRect omenSinistralStashRegion,
        ScreenRect omenDextralStashRegion,
        ScreenRect omenGreaterStashRegion,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        RemainingExaltationState rem,
        IProgress<string>? log,
        CancellationToken ct)
    {
        SchemaTrace(
            log,
            "EnsureInitialExaltationOmenSupply",
            "вход (ASCII EnsureInitial): §1 RefreshRemaining → §2 при нуле RefillOmen → §3 проверка остатков (без отдельного Refresh)");
        log?.Report("EnsureInitialExaltationOmenSupply: шаг 1 — RefreshRemaining.");
        SchemaTrace(log, "EnsureInitial §1", "RefreshRemainingExaltationOmenStacks (док шаг 1)");
        var r1 = await TryRefreshRemainingExaltationOmenStacksAsync(
            areas,
            omenSinistralCells,
            omenDextralCells,
            omenGreaterCells,
            rem,
            log,
            ct).ConfigureAwait(false);
        if (!r1.Ok)
        {
            log?.Report(r1.Error ?? "RefreshRemaining ошибка");
            SchemaTrace(log, "EnsureInitial §1 → FAIL", r1.Error ?? "");
            return false;
        }

        SchemaTrace(log, "EnsureInitial после §1", FormatRem(rem));

        var needRefill =
            rem.Greater <= 0
            || (areas.UseSinistral && rem.Sinistral <= 0)
            || (areas.UseDextral && rem.Dextral <= 0);

        SchemaTrace(
            log,
            "EnsureInitial §2?",
            $"док шаг 2: при нуле у любой используемой области вызвать RefillOmen → needRefill={needRefill}");

        if (needRefill)
        {
            log?.Report("EnsureInitial: шаг 2 — ноль у используемой области, вызов RefillOmen.");
            SchemaTrace(log, "EnsureInitial §2 → RefillOmen", "полный блок RefillOmen (ASCII)");
            if (!await RefillOmenAsync(
                    areas,
                    ritualInventoryRegion,
                    currencyInventoryRegion,
                    omenSinistralStashRegion,
                    omenDextralStashRegion,
                    omenGreaterStashRegion,
                    omenSinistralCells,
                    omenDextralCells,
                    omenGreaterCells,
                    rem,
                    log,
                    ct).ConfigureAwait(false))
            {
                SchemaTrace(log, "EnsureInitial §2 → RefillOmen FAIL", "");
                return false;
            }
        }

        SchemaTrace(log, "EnsureInitial §3", "проверка остатков (док шаг 3, без контрольного Refresh); " + FormatRem(rem));

        if (rem.Greater <= 0
            || (areas.UseSinistral && rem.Sinistral <= 0)
            || (areas.UseDextral && rem.Dextral <= 0))
        {
            log?.Report("Невозможно пополнить область омнами: остаток по используемой области ноль.");
            SchemaTrace(log, "EnsureInitial §3 → STOP", "STOP: остаток 0");
            return false;
        }

        SchemaTrace(log, "EnsureInitial → OK", FormatRem(rem));
        return true;
    }

    private async Task<bool> TryStashCtrlRightFirstMatchingAsync(
        ScreenRect stashRegion,
        string expectedOmenName,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (!IsValidRegion(stashRegion))
            return false;

        await ClearClipboardAsync().ConfigureAwait(false);
        var (x, y) = stashRegion.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        MoveToRandomInteriorIfOutside(stashRegion, log, "Stash: MoveTo", x, y);
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        Win32Input.SendCtrlC();
        await DelayJitterAsync(ClipboardDelayMs, ct).ConfigureAwait(false);
        var clip = await ReadClipboardTextAsync().ConfigureAwait(false);
        SessionLogger.InfoClipboard("stash Ctrl+C", clip);
        if (string.IsNullOrWhiteSpace(clip) || !ClipboardLooksLikeOmen(clip, expectedOmenName))
            return false;

        LogKey(log, "Stash: Ctrl+ПКМ (снять стак)");
        Win32Input.SendCtrlRightClick();
        await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandleRemainingExaltationUsesDepletionAsync(
        CraftExaltationAreas areas,
        RemainingExaltationState rem,
        GreaterCellSnap[] greaterStates,
        ScreenRect ritualInventoryRegion,
        ScreenRect currencyInventoryRegion,
        ScreenRect omenSinistralStashRegion,
        ScreenRect omenDextralStashRegion,
        ScreenRect omenGreaterStashRegion,
        IReadOnlyList<ScreenRect> omenSinistralCells,
        IReadOnlyList<ScreenRect> omenDextralCells,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        IProgress<string>? log,
        CancellationToken ct)
    {
        SchemaTrace(log, "HandleRemainingExaltationUsesDepletion", "вход (ASCII: HandleRemaining… + RefillOmen)");
        var greaterDepleted = rem.Greater <= 0;
        var sideDepleted =
            (areas.UseSinistral && rem.Sinistral <= 0)
            || (areas.UseDextral && rem.Dextral <= 0);

        SchemaTrace(
            log,
            "HandleRemaining ветка",
            $"Greater<=0 → {greaterDepleted}; side исчерпана → {sideDepleted}; {FormatRem(rem)}");

        if (greaterDepleted)
        {
            log?.Report("HandleRemaining: ветка B (исчерпан Greater) — снятие side-стака из stash.");
            SchemaTrace(log, "Шаг B (ASCII)", "B1–B2: Ctrl+ПКМ по side stash (Sin или Dex), затем B3 обнуление");
            if (areas.UseSinistral)
            {
                if (!await TryStashCtrlRightFirstMatchingAsync(
                        omenSinistralStashRegion,
                        OmenActivationService.OmenSinistralExaltationName,
                        log,
                        ct).ConfigureAwait(false))
                    log?.Report("HandleRemaining B: Sinistral stash — подходящий стак не найден (продолжаем RefillOmen).");
            }
            else if (areas.UseDextral)
            {
                if (!await TryStashCtrlRightFirstMatchingAsync(
                        omenDextralStashRegion,
                        OmenActivationService.OmenDextralExaltationName,
                        log,
                        ct).ConfigureAwait(false))
                    log?.Report("HandleRemaining B: Dextral stash — подходящий стак не найден (продолжаем RefillOmen).");
            }
        }
        else if (sideDepleted)
        {
            log?.Report("HandleRemaining: ветка A (исчерпана side-область) — снятие Greater из stash.");
            SchemaTrace(log, "Шаг A (ASCII)", "A1: Ctrl+ПКМ Greater stash, затем A2 обнуление");
            if (!await TryStashCtrlRightFirstMatchingAsync(
                    omenGreaterStashRegion,
                    OmenActivationService.OmenGreaterExaltationName,
                    log,
                    ct).ConfigureAwait(false))
                log?.Report("HandleRemaining A: Greater stash — подходящий стак не найден (продолжаем RefillOmen).");
        }
        else
        {
            log?.Report("HandleRemaining: внутренняя ошибка — нет исчерпания Greater/side.");
            SchemaTrace(log, "HandleRemaining → FAIL", "не Greater и не side depleted");
            return false;
        }

        rem.Sinistral = rem.Dextral = rem.Greater = 0;
        for (var i = 0; i < greaterStates.Length; i++)
            greaterStates[i] = default;

        SchemaTrace(log, "HandleRemaining → RefillOmen", "A3/B4: вызов RefillOmen после обнуления локальных Rem");
        if (!await RefillOmenAsync(
                areas,
                ritualInventoryRegion,
                currencyInventoryRegion,
                omenSinistralStashRegion,
                omenDextralStashRegion,
                omenGreaterStashRegion,
                omenSinistralCells,
                omenDextralCells,
                omenGreaterCells,
                rem,
                log,
                ct).ConfigureAwait(false))
        {
            SchemaTrace(log, "HandleRemaining RefillOmen → FAIL", "");
            return false;
        }

        SchemaTrace(log, "HandleRemaining после RefillOmen", "RefreshRemaining + RefreshGreater Full (A4/B5), без ЛКМ ritual перед Refresh");

        var r = await TryRefreshRemainingExaltationOmenStacksAsync(
            areas,
            omenSinistralCells,
            omenDextralCells,
            omenGreaterCells,
            rem,
            log,
            ct).ConfigureAwait(false);
        if (!r.Ok)
        {
            log?.Report(r.Error ?? "Refresh после RefillOmen");
            SchemaTrace(log, "HandleRemaining RefreshRemaining → FAIL", r.Error ?? "");
            return false;
        }

        if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                greaterStates,
                GreaterRefreshMode.Full,
                omenGreaterCells,
                log,
                ct).ConfigureAwait(false))
        {
            SchemaTrace(log, "HandleRemaining RefreshGreater Full → FAIL", "");
            return false;
        }

        SchemaTrace(log, "HandleRemaining → OK", "LOOP с §1.1 без повторного Init; " + FormatRem(rem));
        return true;
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
                if (CraftConditionEvaluator.TryEvaluateSingleAffixClause(c.Single, item, plan.ExpectedItemClass, out _))
                    return true;
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
                var lib = AffixLibrary.GetEntries();
                foreach (var m in cnt.Members)
                {
                    var mNames = m.EffectiveWholeAffixNames();
                    if (mNames.Any(nm => ParsedItemCraftEvaluator.TryEvaluateWholeModifierAffix(m, item, plan.ExpectedItemClass, lib, out _, nm)))
                        return true;
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

    private static bool AnyUsedDepleted(CraftExaltationAreas areas, RemainingExaltationState rem) =>
        rem.Greater <= 0
        || (areas.UseSinistral && rem.Sinistral <= 0)
        || (areas.UseDextral && rem.Dextral <= 0);

    private void ApplyExaltConsumptionToRemaining(
        CraftExaltationAreas areas,
        RemainingExaltationState rem,
        bool consumedGreater,
        bool consumedSinistral,
        bool consumedDextral)
    {
        if (consumedGreater)
            rem.Greater = Math.Max(0, rem.Greater - 1);
        if (areas.UseSinistral && consumedSinistral)
            rem.Sinistral = Math.Max(0, rem.Sinistral - 1);
        if (areas.UseDextral && consumedDextral)
            rem.Dextral = Math.Max(0, rem.Dextral - 1);
    }

    /// <summary>
    /// Читает предмет из буфера, проверяет редкость (Rare) и сравнивает с условием крафта.
    /// <see cref="CraftPrecheckOutcome.Ready"/> — запускать <see cref="RunAsync"/>; остальные исходы — ячейку пропустить.
    /// </summary>
    public async Task<CraftPrecheckResult> PrecheckAsync(
        ScreenRect itemArea,
        CraftConditionPlan plan,
        IProgress<string>? log,
        CancellationToken ct)
    {
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(120, ct).ConfigureAwait(false);

        SchemaTrace(log, "PrecheckAsync", "выполняется в MainWindow до RunAsync; в доке Init craft идёт после EnsureInitial внутри RunAsync");
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

    /// <summary>
    /// Основной цикл Exaltation: Exalt → Ctrl+Alt+C → проверка условия; при неудаче Annul через Sinistral/Dextral/Greater омены
    /// согласно ASCII-схеме в <c>docs/EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt</c>.
    /// Повторяет до выполнения условия, исчерпания <paramref name="remainingAttempts"/> или отмены токена.
    /// </summary>
    /// <param name="remainingAttempts">Бюджет попыток для этого вызова.</param>
    /// <param name="globalTotal">Общий N сессии — для подписей «попытка k / N».</param>
    /// <param name="globalAttemptOffset">Сколько попыток уже сделано ранее в сессии (по предыдущим ячейкам).</param>
    /// <returns>Итог и число израсходованных попыток.</returns>
    public async Task<CraftResult> RunAsync(
        ScreenRect exaltArea,
        ScreenRect annulArea,
        ScreenRect ritualInventoryRegion,
        ScreenRect currencyInventoryRegion,
        ScreenRect omenSinistralStashRegion,
        ScreenRect omenDextralStashRegion,
        ScreenRect omenGreaterStashRegion,
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

        SchemaTrace(
            log,
            "START RunAsync",
            "схема: docs/EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt; строки с префиксом [ExaltSchema] — текущий узел");

        if (!IsValidRegion(ritualInventoryRegion) || !IsValidRegion(currencyInventoryRegion))
        {
            log?.Report("Экзальт: задайте области Ritual inventory и Currency inventory для RefillOmen.");
            return CraftResult.Failed();
        }

        if (!IsValidRegion(omenGreaterStashRegion))
        {
            log?.Report("Экзальт: задайте область Greater omen stash.");
            return CraftResult.Failed();
        }

        var (wantPrefix, wantSuffix) = GetWantedTypes(plan);
        var prefixOnly = wantPrefix && !wantSuffix;
        var suffixOnly = wantSuffix && !wantPrefix;
        var areas = ResolveCraftExaltationAreas(prefixOnly, suffixOnly);

        if (areas.UseSinistral && !IsValidRegion(omenSinistralStashRegion))
        {
            log?.Report("Экзальт: для prefix-only задайте область Sinistral omen stash.");
            return CraftResult.Failed();
        }

        if (areas.UseDextral && !IsValidRegion(omenDextralStashRegion))
        {
            log?.Report("Экзальт: для suffix-only задайте область Dextral omen stash.");
            return CraftResult.Failed();
        }

        SchemaTrace(
            log,
            "RunAsync Init | resolveCraftExaltationAreas",
            $"prefixOnly={prefixOnly} suffixOnly={suffixOnly} → UseSinistral={areas.UseSinistral} UseDextral={areas.UseDextral}; см. ASCII «CraftExaltationAreas»");
        SchemaTrace(
            log,
            "RunAsync Init | PreviousCraftBranch",
            ":= A (ASCII переменные §4); затем EnsureInitialExaltationOmenSupply");

        var rem = new RemainingExaltationState();
        var greaterStates = new GreaterCellSnap[omenGreaterCells.Count];

        if (!await EnsureInitialExaltationOmenSupplyAsync(
                areas,
                ritualInventoryRegion,
                currencyInventoryRegion,
                omenSinistralStashRegion,
                omenDextralStashRegion,
                omenGreaterStashRegion,
                omenSinistralCells,
                omenDextralCells,
                omenGreaterCells,
                rem,
                log,
                ct).ConfigureAwait(false))
        {
            SchemaTrace(log, "RunAsync → STOP", "EnsureInitial не прошёл");
            return CraftResult.Failed();
        }

        SchemaTrace(log, "Decision flow", "вход в LOOP (ASCII: §1.1–1.2 … без повторного Init craft)");

        var previousBranch = CraftBranchKind.A;
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
                SchemaTrace(
                    log,
                    "LOOP §1.1–1.2 | итерация",
                    $"попытка #{displayAttempt}, exalt used={used}, PreviousBranch={previousBranch}, {FormatRem(rem)}");
                var satisfied = CraftConditionEvaluator.TryEvaluate(plan, current, out var expl);
                craftLog?.WriteComparison(displayAttempt, globalTotal, currentClip, conditionSummary, satisfied, "[проверка перед действием] " + expl);
                log?.Report($"Проверка (попытка {displayAttempt}): {expl}");
                if (satisfied)
                {
                    SchemaTrace(log, "Decision flow §7", "условие выполнено → STOP success");
                    return CraftResult.Found(used, currentClip);
                }

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

                SchemaTrace(
                    log,
                    "LOOP §2 | ветвление match_count",
                    $"anyMatch={anyMatch} → {(anyMatch ? "ветка B (есть элементы условия)" : "ветка A (0 совпадений)")}; canAdd1={canAdd1} canAdd2={canAdd2}");

                if (!anyMatch)
                {
                    if (canAdd2)
                    {
                        SchemaTrace(log, "AddTwoAffixes", "ветка A: can_add_2 (ASCII AddTwoAffixes)");
                        if (previousBranch == CraftBranchKind.B)
                        {
                            SchemaTrace(log, "AddTwoAffixes §0", "PreviousBranch==B → RefreshGreater Full, затем Activate");
                            if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                                    greaterStates,
                                    GreaterRefreshMode.Full,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                            {
                                SchemaTrace(log, "AddTwoAffixes §0 Full → FAIL", "");
                                return CraftResult.Failed(used);
                            }

                            if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                                    greaterStates,
                                    GreaterRefreshMode.Activate,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                            {
                                SchemaTrace(log, "AddTwoAffixes §0 Activate → FAIL", "");
                                return CraftResult.Failed(used);
                            }
                        }

                        var gCell = await _omen.ActivateFirstAsync(
                            omenGreaterCells,
                            OmenActivationService.OmenGreaterExaltationName,
                            log,
                            ct).ConfigureAwait(false);
                        if (gCell is null)
                        {
                            log?.Report("Экзальт (ветка A): Omen Greater не найден — остановка.");
                            return CraftResult.Failed(used);
                        }

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
                                return CraftResult.Failed(used);
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
                                return CraftResult.Failed(used);
                            }
                        }

                        SchemaTrace(log, "AddTwoAffixes §1", "Orb of Exaltation → предмет");
                        await ApplyCurrencyAsync(exaltArea, itemArea, log, ct, "Orb of Exaltation").ConfigureAwait(false);
                        used += 1;
                        annulsSinceLastExalt = 0;
                        previousBranch = CraftBranchKind.A;
                        SchemaTrace(log, "AddTwoAffixes", "PreviousCraftBranch := A");

                        ApplyExaltConsumptionToRemaining(
                            areas,
                            rem,
                            consumedGreater: true,
                            consumedSinistral: prefixOnly,
                            consumedDextral: suffixOnly);

                        SchemaTrace(log, "AddTwoAffixes §2", "локальное уменьшение Remaining* — " + FormatRem(rem));

                        if (AnyUsedDepleted(areas, rem))
                        {
                            SchemaTrace(log, "AddTwoAffixes §3", "деплет → HandleRemainingExaltationUsesDepletion");
                            if (!await HandleRemainingExaltationUsesDepletionAsync(
                                    areas,
                                    rem,
                                    greaterStates,
                                    ritualInventoryRegion,
                                    currencyInventoryRegion,
                                    omenSinistralStashRegion,
                                    omenDextralStashRegion,
                                    omenGreaterStashRegion,
                                    omenSinistralCells,
                                    omenDextralCells,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                            {
                                SchemaTrace(log, "AddTwoAffixes §3 → FAIL", "HandleRemaining");
                                return CraftResult.Failed(used);
                            }
                        }
                    }
                    else
                    {
                        SchemaTrace(log, "Decision flow | A5", "Annulment ×2 (нет места для 2 аффиксов)");
                        if (annulsSinceLastExalt + 2 > 6)
                        {
                            log?.Report("Экзальт: слишком много Orb of Annulment подряд без Orb of Exaltation — остановка во избежание лишних удалений.");
                            return CraftResult.Failed(used);
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
                        SchemaTrace(log, "AddOneAffix", "ветка B: can_add_1 (ASCII AddOneAffix)");
                        if (previousBranch == CraftBranchKind.A)
                        {
                            SchemaTrace(log, "AddOneAffix §0", "PreviousBranch==A → RefreshGreater Full, затем Deactivate");
                            if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                                    greaterStates,
                                    GreaterRefreshMode.Full,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                            {
                                SchemaTrace(log, "AddOneAffix §0 Full → FAIL", "");
                                return CraftResult.Failed(used);
                            }

                            if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                                    greaterStates,
                                    GreaterRefreshMode.Deactivate,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                            {
                                SchemaTrace(log, "AddOneAffix §0 Deactivate → FAIL", "");
                                return CraftResult.Failed(used);
                            }
                        }

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
                                return CraftResult.Failed(used);
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
                                return CraftResult.Failed(used);
                            }
                        }

                        SchemaTrace(log, "AddOneAffix §1", "Orb of Exaltation → предмет");
                        await ApplyCurrencyAsync(exaltArea, itemArea, log, ct, "Orb of Exaltation").ConfigureAwait(false);
                        used += 1;
                        annulsSinceLastExalt = 0;
                        previousBranch = CraftBranchKind.B;
                        SchemaTrace(log, "AddOneAffix", "PreviousCraftBranch := B");

                        ApplyExaltConsumptionToRemaining(
                            areas,
                            rem,
                            consumedGreater: false,
                            consumedSinistral: prefixOnly,
                            consumedDextral: suffixOnly);

                        SchemaTrace(log, "AddOneAffix §2", "локальное уменьшение side Rem* — " + FormatRem(rem));

                        if (AnyUsedDepleted(areas, rem))
                        {
                            SchemaTrace(log, "AddOneAffix §3", "деплет → HandleRemainingExaltationUsesDepletion");
                            if (!await HandleRemainingExaltationUsesDepletionAsync(
                                    areas,
                                    rem,
                                    greaterStates,
                                    ritualInventoryRegion,
                                    currencyInventoryRegion,
                                    omenSinistralStashRegion,
                                    omenDextralStashRegion,
                                    omenGreaterStashRegion,
                                    omenSinistralCells,
                                    omenDextralCells,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                            {
                                SchemaTrace(log, "AddOneAffix §3 → FAIL", "HandleRemaining");
                                return CraftResult.Failed(used);
                            }
                        }
                    }
                    else
                    {
                        SchemaTrace(log, "Decision flow | B5", "Annulment ×1 (нет места для 1 аффикса)");
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
                    return CraftResult.Empty(used);

                current = ItemParser.Parse(currentClip) ?? new ParsedItem { IsValid = false };
                if (!IsRare(current))
                    return CraftResult.Failed(used);

                SchemaTrace(log, "LOOP → следующая итерация", "предмет обновлён (Ctrl+Alt+C); к Decision flow §1.1");
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

        return CraftResult.LimitReached(used);
    }
}
