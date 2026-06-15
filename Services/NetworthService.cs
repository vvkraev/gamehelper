namespace GameHelper.Services;

public sealed record NetworthItemResult(string ItemName, decimal PriceDiv, int Quantity, decimal TotalDiv);

public sealed class NetworthGroupResult
{
    public string GroupName { get; init; } = "";
    public List<NetworthItemResult> Items { get; init; } = [];
    public decimal TotalDiv => Items.Sum(i => i.TotalDiv);
}

/// <summary>
/// Сканирует вкладки стэша по заданным группам и считает стоимость по снэпшоту poe.ninja.
/// </summary>
public static class NetworthService
{
    /// <param name="groups">Список (Name, TabRect, ScanRect, Cols, Rows). TabRect.Width==0 — не кликать по вкладке.</param>
    /// <param name="mouseDelayMs">Базовая задержка мыши; варьируется ±30%.</param>
    /// <param name="clipboardDelayMs">Пауза после Ctrl+Alt+C перед чтением буфера.</param>
    /// <param name="clearClipboard">Очистка буфера (обязательно на UI-потоке).</param>
    /// <param name="readClipboard">Чтение буфера (обязательно на UI-потоке).</param>
    public static async Task<List<NetworthGroupResult>> ScanAsync(
        IReadOnlyList<(string Name, ScreenRect TabRect, ScreenRect ScanRect, int Cols, int Rows)> groups,
        int mouseDelayMs,
        int clipboardDelayMs,
        Func<Task> clearClipboard,
        Func<Task<string>> readClipboard,
        IProgress<string>? log,
        CancellationToken ct)
    {
        var results = new List<NetworthGroupResult>();

        foreach (var (name, tabRect, scanRect, cols, rows) in groups)
        {
            ct.ThrowIfCancellationRequested();

            if (scanRect.Width <= 0 || scanRect.Height <= 0 || cols <= 0 || rows <= 0)
            {
                log?.Report($"[Networth] {name}: область сканирования не задана — пропускаем.");
                continue;
            }

            log?.Report($"[Networth] Сканируем группу «{name}»…");

            if (tabRect.Width > 0 && tabRect.Height > 0)
            {
                var (tx, ty) = tabRect.GetRandomInteriorPoint();
                Native.Win32Input.MoveTo(tx, ty);
                await Task.Delay(Jitter(mouseDelayMs), ct);
                Native.Win32Input.ClickLeft();
                await Task.Delay(800, ct);
            }

            var cells = BuildGrid(scanRect, cols, rows);
            var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in cells)
            {
                ct.ThrowIfCancellationRequested();
                var (cx, cy) = cell.GetRandomInteriorPoint(inset: 2);
                Native.Win32Input.MoveTo(cx, cy);
                await Task.Delay(Jitter(mouseDelayMs), ct);
                await clearClipboard();
                Native.Win32Input.SendCtrlAltC();
                Native.Win32Input.ReleaseCtrlAlt();
                await Task.Delay(Jitter(clipboardDelayMs), ct);

                var text = await readClipboard();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var parsed = ItemParser.Parse(text);
                if (parsed is not { IsValid: true } || string.IsNullOrWhiteSpace(parsed.Name)) continue;

                var qty = parsed.StackSize > 0 ? parsed.StackSize : 1;
                itemCounts.TryGetValue(parsed.Name, out var prev);
                itemCounts[parsed.Name] = prev + qty;
            }

            var items = itemCounts
                .Select(kv =>
                {
                    var price = PoeNinjaPriceService.GetPrice(kv.Key);
                    var div = price?.DivineValue ?? 0m;
                    return new NetworthItemResult(kv.Key, Math.Round(div, 3), kv.Value, Math.Round(div * kv.Value, 3));
                })
                .OrderByDescending(i => i.TotalDiv)
                .ToList();

            results.Add(new NetworthGroupResult { GroupName = name, Items = items });
            log?.Report($"[Networth] {name}: {items.Count} поз., итого ≈ {items.Sum(i => i.TotalDiv):F2} div");
        }

        return results;
    }

    private static IReadOnlyList<ScreenRect> BuildGrid(ScreenRect area, int cols, int rows)
    {
        var cells = new List<ScreenRect>(cols * rows);
        var cellW = Math.Max(1, area.Width / cols);
        var cellH = Math.Max(1, area.Height / rows);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                cells.Add(new ScreenRect(area.X + c * cellW, area.Y + r * cellH, cellW, cellH));
        return cells;
    }

    private static int Jitter(int ms) =>
        ms <= 0 ? 0 : (int)(ms * (0.7 + Random.Shared.NextDouble() * 0.6));
}
