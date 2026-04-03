using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Крафт Rare через Orb of Exaltation + омeны + Orb of Annulment по
/// docs/EXALTATION_CRAFT_SERVICE_FRACTURED_SIDE_FLOW_ASCII.txt (ветка Fractured / inventory grids).
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
        if (!Win32Input.TryGetCursorPos(out var curX, out var curY) || !cell.ContainsPoint(curX, curY, inset: 1))
            LogMove(log, $"{tag}: MoveTo omen cell", x, y);
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

        return (true, null);
    }

    private async Task<bool> RefreshGreaterExaltationOmenCellStatesAsync(
        GreaterCellSnap[] states,
        GreaterRefreshMode mode,
        IReadOnlyList<ScreenRect> omenGreaterCells,
        IProgress<string>? log,
        CancellationToken ct)
    {
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

                var activated = _omen.IsOmenCellVisuallyActivated(omenGreaterCells[i]);
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
                var (px, py) = omenGreaterCells[i].GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
                if (!Win32Input.TryGetCursorPos(out var cx, out var cy) ||
                    !omenGreaterCells[i].ContainsPoint(cx, cy, inset: 1))
                    LogMove(log, $"RefreshGreater {mode}: MoveTo cell", px, py);
                await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                LogMouse(log, $"RefreshGreater {mode}: ПКМ");
                Win32Input.ClickRight();
                await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
                await Task.Delay(150, ct).ConfigureAwait(false);

                clip = await ReadOmenCellClipboardWithRetryAsync(
                    omenGreaterCells[i],
                    log,
                    ct,
                    $"RefreshGreater {mode} после ПКМ {i + 1}").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clip) || !ClipboardHasOmenHeader(clip))
                {
                    states[i] = new GreaterCellSnap(0, false);
                    continue;
                }

                if (!TryParseStackSizeN(clip, out n))
                    return false;
            }

            var vis = _omen.IsOmenCellVisuallyActivated(omenGreaterCells[i]);
            states[i] = new GreaterCellSnap(n, vis);
        }

        return true;
    }

    private async Task ClickLeftInRegionAsync(ScreenRect region, IProgress<string>? log, CancellationToken ct, string label)
    {
        var (x, y) = region.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
        if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !region.ContainsPoint(cx, cy, inset: 1))
            LogMove(log, $"{label}: ЛКМ область", x, y);
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
            if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !region.ContainsPoint(cx, cy, inset: 1))
                LogMove(log, $"{label} {t + 1}/{times}", x, y);
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
                if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !cell.ContainsPoint(cx, cy, inset: 1))
                    LogMove(log, $"RefillOmen ПКМ {name} {i + 1}/{cells.Count}", x, y);
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

        log?.Report("RefillOmen: шаг 1 — фокус ritual inventory (ЛКМ).");
        await ClickLeftInRegionAsync(ritualInventoryRegion, log, ct, "ritual inventory").ConfigureAwait(false);

        if (areas.UseSinistral && !areas.UseDextral)
        {
            if (!IsValidRegion(omenSinistralStashRegion) || !IsValidRegion(omenGreaterStashRegion))
            {
                log?.Report("RefillOmen: не заданы области stash Sinistral / Greater.");
                return false;
            }

            log?.Report("RefillOmen: ветка 1.1 Sinistral — 35× Ctrl+ЛКМ Sinistral stash, 25× Greater stash.");
            await RepeatCtrlLeftOnRegionAsync(omenSinistralStashRegion, 35, log, ct, "Sinistral stash").ConfigureAwait(false);
            await RepeatCtrlLeftOnRegionAsync(omenGreaterStashRegion, 25, log, ct, "Greater stash").ConfigureAwait(false);
        }
        else if (areas.UseDextral && !areas.UseSinistral)
        {
            if (!IsValidRegion(omenDextralStashRegion) || !IsValidRegion(omenGreaterStashRegion))
            {
                log?.Report("RefillOmen: не заданы области stash Dextral / Greater.");
                return false;
            }

            log?.Report("RefillOmen: ветка 1.2 Dextral — 35× Ctrl+ЛКМ Dextral stash, 25× Greater stash.");
            await RepeatCtrlLeftOnRegionAsync(omenDextralStashRegion, 35, log, ct, "Dextral stash").ConfigureAwait(false);
            await RepeatCtrlLeftOnRegionAsync(omenGreaterStashRegion, 25, log, ct, "Greater stash").ConfigureAwait(false);
        }
        else
        {
            if (!IsValidRegion(omenGreaterStashRegion))
            {
                log?.Report("RefillOmen: не задана область Greater stash (ветка mixed).");
                return false;
            }

            log?.Report("RefillOmen: ветка 1.3 mixed — 1× Ctrl+ПКМ Greater stash.");
            var (gx, gy) = omenGreaterStashRegion.GetRandomInteriorPoint(1, centerAreaFraction: 0.8);
            if (!Win32Input.TryGetCursorPos(out var cx, out var cy) ||
                !omenGreaterStashRegion.ContainsPoint(cx, cy, inset: 1))
                LogMove(log, "RefillOmen Greater stash Ctrl+ПКМ", gx, gy);
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
            LogKey(log, "RefillOmen: Ctrl+ПКМ Greater stash");
            Win32Input.SendCtrlRightClick();
            await DelayJitterAsync(MouseActionDelayMs, ct).ConfigureAwait(false);
        }

        log?.Report("RefillOmen: шаг 2 — ЛКМ currency inventory.");
        await ClickLeftInRegionAsync(currencyInventoryRegion, log, ct, "currency inventory").ConfigureAwait(false);

        log?.Report("RefillOmen: шаг 3 — RefreshRemainingExaltationOmenStacks.");
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
            return false;
        }

        log?.Report("RefillOmen: шаг 4 — ПКМ по всем ячейкам используемых сеток омнов.");
        await RightClickAllUsedOmenInventoryCellsAsync(
            areas,
            omenSinistralCells,
            omenDextralCells,
            omenGreaterCells,
            log,
            ct).ConfigureAwait(false);

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
        log?.Report("EnsureInitialExaltationOmenSupply: шаг 1 — RefreshRemaining.");
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
            return false;
        }

        var needRefill =
            rem.Greater <= 0
            || (areas.UseSinistral && rem.Sinistral <= 0)
            || (areas.UseDextral && rem.Dextral <= 0);

        if (needRefill)
        {
            log?.Report("EnsureInitial: обнаружен ноль у используемой области — RefillOmen.");
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
                return false;
        }

        log?.Report("EnsureInitial: контрольный RefreshRemaining после RefillOmen.");
        var r2 = await TryRefreshRemainingExaltationOmenStacksAsync(
            areas,
            omenSinistralCells,
            omenDextralCells,
            omenGreaterCells,
            rem,
            log,
            ct).ConfigureAwait(false);
        if (!r2.Ok)
        {
            log?.Report(r2.Error ?? "RefreshRemaining ошибка");
            return false;
        }

        if (rem.Greater <= 0
            || (areas.UseSinistral && rem.Sinistral <= 0)
            || (areas.UseDextral && rem.Dextral <= 0))
        {
            log?.Report("Невозможно пополнить область омнами: после RefillOmen остаток по используемой области всё ещё ноль.");
            return false;
        }

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
        if (!Win32Input.TryGetCursorPos(out var cx, out var cy) || !stashRegion.ContainsPoint(cx, cy, inset: 1))
            LogMove(log, "Stash: MoveTo", x, y);
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
        var greaterDepleted = rem.Greater <= 0;
        var sideDepleted =
            (areas.UseSinistral && rem.Sinistral <= 0)
            || (areas.UseDextral && rem.Dextral <= 0);

        if (greaterDepleted)
        {
            log?.Report("HandleRemaining: ветка B (исчерпан Greater) — снятие side-стака из stash.");
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
            return false;
        }

        rem.Sinistral = rem.Dextral = rem.Greater = 0;
        for (var i = 0; i < greaterStates.Length; i++)
            greaterStates[i] = default;

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
            return false;

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
            return false;
        }

        if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                greaterStates,
                GreaterRefreshMode.Full,
                omenGreaterCells,
                log,
                ct).ConfigureAwait(false))
            return false;

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

        if (!IsValidRegion(ritualInventoryRegion) || !IsValidRegion(currencyInventoryRegion))
        {
            log?.Report("Экзальт: задайте области Ritual inventory и Currency inventory для RefillOmen.");
            return (ChaosCraftResult.Error, 0);
        }

        if (!IsValidRegion(omenGreaterStashRegion))
        {
            log?.Report("Экзальт: задайте область Greater omen stash.");
            return (ChaosCraftResult.Error, 0);
        }

        var (wantPrefix, wantSuffix) = GetWantedTypes(plan);
        var prefixOnly = wantPrefix && !wantSuffix;
        var suffixOnly = wantSuffix && !wantPrefix;
        var areas = ResolveCraftExaltationAreas(prefixOnly, suffixOnly);

        if (areas.UseSinistral && !IsValidRegion(omenSinistralStashRegion))
        {
            log?.Report("Экзальт: для prefix-only задайте область Sinistral omen stash.");
            return (ChaosCraftResult.Error, 0);
        }

        if (areas.UseDextral && !IsValidRegion(omenDextralStashRegion))
        {
            log?.Report("Экзальт: для suffix-only задайте область Dextral omen stash.");
            return (ChaosCraftResult.Error, 0);
        }

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
            return (ChaosCraftResult.Error, 0);

        if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                greaterStates,
                GreaterRefreshMode.Full,
                omenGreaterCells,
                log,
                ct).ConfigureAwait(false))
            return (ChaosCraftResult.Error, 0);

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
                        if (previousBranch == CraftBranchKind.B)
                        {
                            if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                                    greaterStates,
                                    GreaterRefreshMode.Activate,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                                return (ChaosCraftResult.Error, used);
                        }

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
                        previousBranch = CraftBranchKind.A;

                        ApplyExaltConsumptionToRemaining(
                            areas,
                            rem,
                            consumedGreater: true,
                            consumedSinistral: prefixOnly,
                            consumedDextral: suffixOnly);

                        if (AnyUsedDepleted(areas, rem))
                        {
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
                                return (ChaosCraftResult.Error, used);
                        }
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
                        if (previousBranch == CraftBranchKind.A)
                        {
                            if (!await RefreshGreaterExaltationOmenCellStatesAsync(
                                    greaterStates,
                                    GreaterRefreshMode.Deactivate,
                                    omenGreaterCells,
                                    log,
                                    ct).ConfigureAwait(false))
                                return (ChaosCraftResult.Error, used);
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
                        previousBranch = CraftBranchKind.B;

                        ApplyExaltConsumptionToRemaining(
                            areas,
                            rem,
                            consumedGreater: false,
                            consumedSinistral: prefixOnly,
                            consumedDextral: suffixOnly);

                        if (AnyUsedDepleted(areas, rem))
                        {
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
                                return (ChaosCraftResult.Error, used);
                        }
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
