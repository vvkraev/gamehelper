using System.Text.RegularExpressions;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Переоценка выставленных товаров через асинхронный трейд:
/// обходит сетку ячеек, считывает текущую цену через Ctrl+Alt+C,
/// снижает по алгоритму и задаёт новую через ПКМ → Ctrl+A → Type → Enter.
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

    /// <summary>
    /// Снижает цену по алгоритму. Возвращает null если цена ≤5 (не снижаем).
    /// </summary>
    public static decimal? CalcNewPrice(decimal current) =>
        current switch
        {
            >= 500 => current - 50m,
            >= 200 => current - 25m,
            >= 100 => current - 20m,
            >= 50  => current - 10m,
            > 5    => current - 5m,   // строго > 5: при цене ровно 5 не снижаем
            _      => null,
        };

    public async Task RunAsync(
        IReadOnlyList<ScreenRect> cells,
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
                log?.Report($"[{i + 1}/{cells.Count}] пустой буфер — пропускаем");
                skipped++;
                continue;
            }

            var current = ParseNotePrice(text);
            if (current is null)
            {
                log?.Report($"[{i + 1}/{cells.Count}] цена не найдена — пропускаем");
                skipped++;
                continue;
            }

            var newPrice = CalcNewPrice(current.Value);
            if (newPrice is null)
            {
                log?.Report($"[{i + 1}/{cells.Count}] {current} — не снижаем (≤5)");
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
                log?.Report($"[{i + 1}/{cells.Count}] заблокирован — диалог цены не открылся, пропускаем");
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
                log?.Report($"[{i + 1}/{cells.Count}] ✓ {current} → {newPrice}");
                repriced++;
            }
            else
            {
                log?.Report($"[{i + 1}/{cells.Count}] переоценка не применилась (цена: {verifiedPrice?.ToString() ?? "?"})");
                skipped++;
            }
        }

        log?.Report($"Готово: переоценено {repriced}, пропущено {skipped}.");
    }
}
