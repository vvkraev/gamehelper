using GameHelper.Native;

namespace GameHelper.Services;

public enum ReforgeStopReason
{
    Cancelled,
    InventoryFull,
    ResultReadFailed,
    Error,
}

public readonly record struct ReforgeAttemptResult(string? OutputItemName, string? RawClipboard);

public sealed class ReforgeService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public int PostReforgeSettleMs { get; set; } = 800;
    public bool TraceInputToLog { get; set; }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        if (delta <= 0) return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static Task DelayJ(int baseMs, CancellationToken ct) =>
        Task.Delay(WithJitter(baseMs), ct);

    // ── Публичный API ────────────────────────────────────────────────────────

    /// <summary>
    /// Одна попытка перековки: Ctrl+ЛКМ инвентарь ×3 → кнопка → ждём → Ctrl+ЛКМ результат → Ctrl+Alt+C → возврат.
    /// </summary>
    public async Task<(ReforgeAttemptResult? Result, ReforgeStopReason? StopReason)> RunOneAsync(
        ScreenRect catalystInventoryRect,
        ScreenRect slot1Rect,
        ScreenRect slot2Rect,
        ScreenRect slot3Rect,
        ScreenRect confirmRect,
        ScreenRect resultRect,
        IProgress<string>? log,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Раскладываем по слотам: Ctrl+ЛКМ по стаку → игра отправляет 1 шт. в первый свободный слот
        //    Делаем по очереди с явным перемещением в слот для надёжности:
        //    MoveTo слот → MoveTo инвентарь → Ctrl+ЛКМ (игра кладёт в слот под курсором? нет — в свободный)
        //    → если не работает, переключимся на явный перетаскивание через slot-rect клики.
        //    MVP: Ctrl+ЛКМ по инвентарному стаку 3 раза (авто-fill в открытый станок).

        log?.Report("[Reforge] Раскладываем 3 катализатора в слоты...");

        foreach (var (slotRect, idx) in new[] { (slot1Rect, 1), (slot2Rect, 2), (slot3Rect, 3) })
        {
            ct.ThrowIfCancellationRequested();

            // MoveTo → инвентарь (случайная точка)
            var (ix, iy) = catalystInventoryRect.GetRandomInteriorPoint(inset: 2);
            if (!Win32Input.MoveTo(ix, iy))
            {
                log?.Report($"[Reforge] MoveTo инвентарь (слот {idx}) — не удалось");
                return (null, ReforgeStopReason.Error);
            }
            await DelayJ(MouseActionDelayMs, ct);

            // Ctrl+ЛКМ → катализатор идёт в первый свободный слот станка
            Win32Input.SendCtrlLeftClick();
            log?.Report($"[Reforge] Ctrl+ЛКМ инвентарь → слот {idx}");
            await DelayJ(MouseActionDelayMs, ct);

            // Небольшая доп. пауза между укладкой в слоты
            await DelayJ(MouseActionDelayMs, ct);
        }

        ct.ThrowIfCancellationRequested();

        // 2. Нажимаем кнопку Reforge
        var (cx, cy) = confirmRect.GetRandomInteriorPoint(inset: 2);
        if (!Win32Input.MoveTo(cx, cy))
        {
            log?.Report("[Reforge] MoveTo кнопки Reforge — не удалось");
            return (null, ReforgeStopReason.Error);
        }
        await DelayJ(MouseActionDelayMs, ct);
        Win32Input.ClickLeft();
        log?.Report("[Reforge] Кнопка Reforge нажата");

        // 3. Ждём анимацию станка
        await Task.Delay(WithJitter(PostReforgeSettleMs), ct);
        ct.ThrowIfCancellationRequested();

        // 4. Ctrl+ЛКМ по зоне результата → уходит в инвентарь
        var (rx, ry) = resultRect.GetRandomInteriorPoint(inset: 2);
        if (!Win32Input.MoveTo(rx, ry))
        {
            log?.Report("[Reforge] MoveTo результата — не удалось");
            return (null, ReforgeStopReason.Error);
        }
        await DelayJ(MouseActionDelayMs, ct);
        Win32Input.SendCtrlLeftClick();
        log?.Report("[Reforge] Ctrl+ЛКМ результат → инвентарь");
        await DelayJ(MouseActionDelayMs, ct);

        // 5. Ctrl+Alt+C — читаем что получили (курсор всё ещё у результата)
        await ClearClipboardAsync();
        Win32Input.SendCtrlAltC();
        await DelayJ(ClipboardDelayMs, ct);

        var text = await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
        SessionLogger.InfoClipboard("[Reforge]", text);

        if (string.IsNullOrWhiteSpace(text))
        {
            log?.Report("[Reforge] Буфер пуст после Ctrl+Alt+C — результат не прочитан");
            return (null, ReforgeStopReason.ResultReadFailed);
        }

        // Извлекаем имя предмета из первой строки буфера (после "Item Class: ...")
        var name = ExtractItemName(text);
        log?.Report($"[Reforge] Результат: {name ?? "(не распознано)"}");

        return (new ReforgeAttemptResult(name, text), null);
    }

    /// <summary>
    /// Цикл на <paramref name="maxAttempts"/> попыток (0 = бесконечно).
    /// </summary>
    public async Task RunLoopAsync(
        ScreenRect catalystInventoryRect,
        ScreenRect slot1Rect,
        ScreenRect slot2Rect,
        ScreenRect slot3Rect,
        ScreenRect confirmRect,
        ScreenRect resultRect,
        int maxAttempts,
        IProgress<string>? log,
        Action<ReforgeAttemptResult>? onAttempt,
        CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested && (maxAttempts <= 0 || attempt < maxAttempts))
        {
            attempt++;
            log?.Report($"[Reforge] === Попытка {attempt} ===");

            var (result, stop) = await RunOneAsync(
                catalystInventoryRect, slot1Rect, slot2Rect, slot3Rect,
                confirmRect, resultRect, log, ct);

            if (stop is { } reason)
            {
                log?.Report($"[Reforge] Остановлено: {reason}");
                return;
            }

            if (result is { } r)
                onAttempt?.Invoke(r);
        }

        log?.Report("[Reforge] Цикл завершён.");
    }

    // ── Вспомогательные ─────────────────────────────────────────────────────

    private static string? ExtractItemName(string clipboardText)
    {
        foreach (var line in clipboardText.Split('\n'))
        {
            var trimmed = line.Trim('\r', ' ');
            if (trimmed.StartsWith("Item Class:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("--------", StringComparison.Ordinal)
                || string.IsNullOrEmpty(trimmed))
                continue;
            return trimmed;
        }
        return null;
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.GetText(); }
        catch { return ""; }
    }

    private static async Task ClearClipboardAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });
    }
}
