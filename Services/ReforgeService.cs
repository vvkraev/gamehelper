using GameHelper.Native;

namespace GameHelper.Services;

public enum ReforgeStopReason { Cancelled, MaxOpsReached, NoCatalystsFound, Error }

public readonly record struct ReforgeAttemptResult(string InputTypeName, string? OutputItemName, string? RawClipboard);

public readonly record struct ScannedCell(int Index, string TypeId, string DisplayName, int StackSize);

public sealed class ReforgeService
{
    private const double DelayJitterFraction = 0.30;

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs   { get; set; } = 220;
    public int PostReforgeSettleMs { get; set; } = 800;

    // ── Основной цикл ────────────────────────────────────────────────────────

    /// <summary>
    /// Полный цикл перековки:
    /// 1. Сканирует все ячейки инвентаря → определяет тип и размер стака.
    /// 2. Группирует по типу. Для каждого выбранного типа — пакеты по ≤3 стака:
    ///    Ctrl+ЛКМ ×batch → Reforge ×floor(total/3) → Ctrl+ЛКМ слоты (достать остаток).
    /// 3. Останавливается по maxOps (0 = без ограничений) или когда кончаются катализаторы.
    /// </summary>
    public async Task<ReforgeStopReason> RunAsync(
        IReadOnlyList<ScreenRect> inventoryCells,
        IReadOnlyList<string> selectedTypeIds,
        ScreenRect slot1, ScreenRect slot2, ScreenRect slot3,
        ScreenRect confirmRect, ScreenRect resultRect,
        int maxOps,
        IProgress<string>? log,
        Action<ReforgeAttemptResult>? onAttempt,
        CancellationToken ct)
    {
        var benchSlots = new[] { slot1, slot2, slot3 };
        var opsRemaining = maxOps > 0 ? maxOps : int.MaxValue;

        // ── Фаза сканирования ─────────────────────────────────────────────
        log?.Report("[Reforge] Сканируем инвентарь...");
        var scanned = await ScanInventoryAsync(inventoryCells, selectedTypeIds, log, ct);

        if (scanned.Count == 0)
        {
            log?.Report("[Reforge] Не найдено выбранных катализаторов.");
            return ReforgeStopReason.NoCatalystsFound;
        }

        // ── Фаза перековки: по одному типу, пакетами по ≤3 ячейки ─────────
        foreach (var typeGroup in scanned.GroupBy(c => c.TypeId))
        {
            if (opsRemaining <= 0 || ct.IsCancellationRequested) break;

            var typeCells = typeGroup.ToList();
            log?.Report($"[Reforge] Тип: {typeCells[0].DisplayName} — {typeCells.Count} стаков, " +
                        $"{typeCells.Sum(c => c.StackSize)} шт.");

            // Батчи ≤3 стака (столько слотов у станка)
            for (var bStart = 0; bStart < typeCells.Count; bStart += 3)
            {
                if (opsRemaining <= 0 || ct.IsCancellationRequested) break;

                var batch = typeCells.GetRange(bStart, Math.Min(3, typeCells.Count - bStart));
                var total = batch.Sum(c => c.StackSize);
                var nReforges = Math.Min(total / 3, opsRemaining);

                if (nReforges == 0)
                {
                    log?.Report($"  Пакет {bStart / 3 + 1}: всего {total} — недостаточно для перековки (нужно ≥3).");
                    continue;
                }

                log?.Report($"  Пакет {bStart / 3 + 1}: {batch.Count} стака(ов), {total} шт. → {nReforges} перековок");

                // Перекладываем стаки в станок
                foreach (var cell in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    await CtrlClickAsync(inventoryCells[cell.Index], ct);
                }

                // Перековываем
                for (var i = 0; i < nReforges; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Кнопка Reforge
                    await MoveAndClickAsync(confirmRect, ct);
                    await Task.Delay(WithJitter(PostReforgeSettleMs), ct);

                    // Ctrl+ЛКМ результат → в инвентарь
                    await CtrlClickAsync(resultRect, ct);
                    await Task.Delay(WithJitter(MouseActionDelayMs), ct);

                    // Ctrl+Alt+C — читаем что получили
                    var text = await ReadClipboardAtAsync(resultRect, ct);
                    var parsed = string.IsNullOrWhiteSpace(text) ? null : ItemParser.Parse(text);
                    var result = new ReforgeAttemptResult(typeCells[0].DisplayName, parsed?.Name, text);
                    onAttempt?.Invoke(result);
                    log?.Report($"    [{i + 1}/{nReforges}] → {result.OutputItemName ?? "?"}");

                    opsRemaining--;
                    if (opsRemaining == 0)
                    {
                        log?.Report("[Reforge] Достигнут лимит операций. Извлекаем остатки...");
                        await RetrieveFromBenchAsync(benchSlots, ct);
                        return ReforgeStopReason.MaxOpsReached;
                    }
                }

                // Извлекаем остатки из станка (total % 3 могут остаться)
                await RetrieveFromBenchAsync(benchSlots, ct);
            }
        }

        if (ct.IsCancellationRequested)
            return ReforgeStopReason.Cancelled;

        log?.Report($"[Reforge] Готово. Выполнено перековок: {(maxOps > 0 ? maxOps - opsRemaining : maxOps == 0 ? scanned.Sum(c => c.StackSize) / 3 : 0)}");
        return ReforgeStopReason.NoCatalystsFound;
    }

    // ── Сканирование инвентаря ───────────────────────────────────────────────

    public async Task<List<ScannedCell>> ScanInventoryAsync(
        IReadOnlyList<ScreenRect> cells,
        IReadOnlyList<string>? filterIds,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var result = new List<ScannedCell>();
        for (var i = 0; i < cells.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var text = await ReadClipboardAtAsync(cells[i], ct);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var parsed = ItemParser.Parse(text);
            if (parsed == null || !parsed.IsValid || string.IsNullOrWhiteSpace(parsed.Name)) continue;

            var entry = StackableItemRegistry.Items.FirstOrDefault(e =>
                e.DisplayName.Equals(parsed.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue;
            if (filterIds != null && filterIds.Count > 0 && !filterIds.Contains(entry.Id)) continue;

            var size = parsed.StackSize > 0 ? parsed.StackSize : 1;
            result.Add(new ScannedCell(i, entry.Id, entry.DisplayName, size));
            log?.Report($"  Ячейка {i + 1}: {entry.DisplayName} ×{size}");
        }
        return result;
    }

    // ── Низкоуровневые операции ──────────────────────────────────────────────

    private async Task<string> ReadClipboardAtAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetRandomInteriorPoint(inset: 2);
        Win32Input.MoveTo(x, y);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        await Task.Delay(Math.Clamp(MouseActionDelayMs / 2, 50, 150), ct); // hover settle

        await ClearClipboardAsync();
        Win32Input.SendCtrlAltC();
        await Task.Delay(WithJitter(ClipboardDelayMs), ct);

        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(GetClipboardTextSafe);
    }

    private async Task CtrlClickAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetRandomInteriorPoint(inset: 2);
        Win32Input.MoveTo(x, y);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        Win32Input.SendCtrlLeftClick();
        Win32Input.CtrlUp();
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
    }

    private async Task MoveAndClickAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetRandomInteriorPoint(inset: 2);
        Win32Input.MoveTo(x, y);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        Win32Input.ClickLeft();
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
    }

    private async Task RetrieveFromBenchAsync(ScreenRect[] slots, CancellationToken ct)
    {
        foreach (var slot in slots)
        {
            ct.ThrowIfCancellationRequested();
            await CtrlClickAsync(slot, ct);
        }
    }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * DelayJitterFraction);
        if (delta <= 0) return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.GetText(); }
        catch { return ""; }
    }

    private static async Task ClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });
}
