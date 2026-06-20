using System.Text.RegularExpressions;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Переоценка выставленных товаров через асинхронный трейд:
/// проходит по вкладкам торговца, внутри каждой — по сетке ячеек.
/// Считывает текущую цену через Ctrl+Alt+C, снижает по алгоритму и
/// задаёт новую через ПКМ → Ctrl+A → Type → Enter.
/// </summary>
public sealed class RepricingService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs   { get; set; } = 220;
    public int PostClickDelayMs   { get; set; } = 300;
    public int HoverSettleMs      { get; set; } = 120;

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        return delta <= 0 ? baseMs : Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static Task DelayAsync(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    private static async Task<string> ReadClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
        catch { return string.Empty; }
    }

    private static async Task ClearClipboardAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });
    }

    /// <summary>
    /// Парсит "Note: ~b/o 90 exalted" → 90.
    /// Возвращает null, если формат не совпал.
    /// </summary>
    public static decimal? ParseNotePrice(string clipboardText)
    {
        var m = Regex.Match(clipboardText,
            @"Note:\s*~b/o\s+([\d]+(?:[.,]\d+)?)\s+\S",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var numStr = m.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(numStr,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : null;
    }

    /// <summary>Шаги снижения цены по умолчанию (применяются если вкладка не задала свои).</summary>
    public static readonly RepricingPriceStep[] DefaultPriceSteps =
    [
        new() { FromPrice = 500, Step = 50, StrictlyGreater = false, Enabled = true },
        new() { FromPrice = 200, Step = 25, StrictlyGreater = false, Enabled = true },
        new() { FromPrice = 100, Step = 20, StrictlyGreater = false, Enabled = true },
        new() { FromPrice = 50,  Step = 10, StrictlyGreater = false, Enabled = true },
        new() { FromPrice = 5,   Step = 5,  StrictlyGreater = true,  Enabled = true },
        new() { FromPrice = 1,   Step = 1,  StrictlyGreater = true,  Enabled = true },
    ];

    /// <summary>
    /// Снижает цену по алгоритму. Возвращает null если ни один активный шаг не применим
    /// или достигнут отключённый шаг (переоценка останавливается).
    /// </summary>
    public static decimal? CalcNewPrice(decimal current, IReadOnlyList<RepricingPriceStep>? steps = null)
    {
        var effectiveSteps = steps is { Count: > 0 }
            ? steps
            : (IReadOnlyList<RepricingPriceStep>)DefaultPriceSteps;
        foreach (var step in effectiveSteps)
        {
            var matches = step.StrictlyGreater ? current > step.FromPrice : current >= step.FromPrice;
            if (!matches) continue;
            return step.Enabled ? current - step.Step : null;
        }
        return null;
    }

    /// <summary>
    /// Переоценивает одну вкладку: кликает по TabRect (если задана) и обходит все ячейки.
    /// Используется в независимых per-tab задачах планировщика.
    /// </summary>
    public async Task RunSingleTabAsync(
        RepricingTabConfig tab,
        IProgress<string>? log,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cells = tab.Cells;
        if (cells == null || cells.Count == 0) return;

        if (tab.TabRect.Width > 0 && tab.TabRect.Height > 0)
        {
            log?.Report($"[{tab.Name}] Переход на вкладку…");
            var (tx, ty) = tab.TabRect.GetRandomInteriorPoint();
            Win32Input.MoveTo(tx, ty);
            await DelayAsync(MouseActionDelayMs, ct);
            Win32Input.ClickLeft();
            await Task.Delay(WithJitter(PostClickDelayMs), ct);
        }

        await RepriceCellsAsync(cells, tab.PriceSteps, $"[{tab.Name}] ", log, ct);
    }

    private async Task<(int Repriced, int Skipped)> RepriceCellsAsync(
        IReadOnlyList<ScreenRect> cells,
        IReadOnlyList<RepricingPriceStep>? priceSteps,
        string prefix,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var repriced = 0;
        var skipped  = 0;

        for (var i = 0; i < cells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = cells[i];
            var cx = cell.X + cell.Width / 2;
            var cy = cell.Y + cell.Height / 2;

            Win32Input.MoveTo(cx, cy);
            await DelayAsync(HoverSettleMs, ct);

            await ClearClipboardAsync();
            Win32Input.SendCtrlAltC();
            await DelayAsync(ClipboardDelayMs, ct);
            Win32Input.ReleaseCtrlAlt();

            var text = await ReadClipboardAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                log?.Report($"{prefix}[{i + 1}/{cells.Count}] пустой буфер — пропускаем");
                skipped++;
                continue;
            }

            var current = ParseNotePrice(text);
            if (current is null)
            {
                log?.Report($"{prefix}[{i + 1}/{cells.Count}] цена не найдена — пропускаем");
                skipped++;
                continue;
            }

            var newPrice = CalcNewPrice(current.Value, priceSteps);
            if (newPrice is null)
            {
                log?.Report($"{prefix}[{i + 1}/{cells.Count}] {current} — не снижаем (≤5)");
                skipped++;
                continue;
            }

            var priceText = newPrice.Value == Math.Floor(newPrice.Value)
                ? ((long)newPrice.Value).ToString()
                : newPrice.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            // ── ПКМ + обнаружение: открылось ли окно ввода цены ─────────────
            await ClearClipboardAsync();
            Win32Input.MoveTo(cx, cy);
            await DelayAsync(MouseActionDelayMs, ct);
            Win32Input.ClickRight();
            await DelayAsync(PostClickDelayMs, ct);

            // Ctrl+C-детекция: если диалог НЕ открылся — PoE2 скопирует текст
            // предмета в буфер. Если открылся — буфер останется пустым или
            // получит только число (цену из поля ввода).
            Win32Input.SendCtrlC();
            await DelayAsync(MouseActionDelayMs, ct);
            var detectionText = await ReadClipboardAsync();

            // Нет цифры в буфере, но есть нецифровой текст → текст предмета → заблокирован
            var isItemText = !string.IsNullOrWhiteSpace(detectionText)
                          && !decimal.TryParse(detectionText.Trim(),
                                 System.Globalization.NumberStyles.Number,
                                 System.Globalization.CultureInfo.InvariantCulture, out _);
            if (isItemText)
            {
                log?.Report($"{prefix}[{i + 1}/{cells.Count}] заблокирован — диалог цены не открылся, пропускаем");
                skipped++;
                continue;
            }

            // ── Вводим новую цену ─────────────────────────────────────────────
            Win32Input.SendCtrlA();
            await DelayAsync(MouseActionDelayMs, ct);
            Win32Input.TypeText(priceText);
            await DelayAsync(MouseActionDelayMs, ct);
            Win32Input.PressEnter();
            await DelayAsync(PostClickDelayMs, ct);

            // ── Верификация через повторный Ctrl+Alt+C ────────────────────────
            await ClearClipboardAsync();
            Win32Input.MoveTo(cx, cy);
            await DelayAsync(HoverSettleMs, ct);
            Win32Input.SendCtrlAltC();
            await DelayAsync(ClipboardDelayMs, ct);
            Win32Input.ReleaseCtrlAlt();

            var verifyText    = await ReadClipboardAsync();
            var verifiedPrice = ParseNotePrice(verifyText);

            if (verifiedPrice == newPrice)
            {
                log?.Report($"{prefix}[{i + 1}/{cells.Count}] ✓ {current} → {newPrice}");
                repriced++;
            }
            else
            {
                log?.Report($"{prefix}[{i + 1}/{cells.Count}] переоценка не применилась (цена: {verifiedPrice?.ToString() ?? "?"})");
                skipped++;
            }
        }

        log?.Report($"{prefix}Готово: переоценено {repriced}, пропущено {skipped}.");
        return (repriced, skipped);
    }
}
