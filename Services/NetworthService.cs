namespace GameHelper.Services;

public sealed record NwGroupDef(
    string Name,
    ScreenRect TabRect,
    IReadOnlyList<(string ItemName, ScreenRect Area)> Items);

public sealed record NetworthItemResult(string ItemName, decimal PriceDiv, int Quantity, decimal TotalDiv);

public sealed class NetworthGroupResult
{
    public string GroupName { get; init; } = "";
    public List<NetworthItemResult> Items { get; init; } = [];
    public decimal TotalDiv => Items.Sum(i => i.TotalDiv);
}

/// <summary>
/// Сканирует вкладки стэша по уже заданным областям предметов и считает стоимость по снэпшоту poe.ninja.
/// </summary>
public static class NetworthService
{
    /// <param name="stashOcrSearchRect">Область экрана с меткой STASH — для OCR-навигации. Width==0 → не кликать.</param>
    /// <param name="stashOcrText">Текст для поиска в OCR (например «STASH»).</param>
    /// <param name="stashOpenDelayMs">Задержка после клика на стэш (мс).</param>
    /// <param name="groups">Список групп: каждая содержит вкладку и список (имя предмета, область).</param>
    public static async Task<List<NetworthGroupResult>> ScanAsync(
        ScreenRect stashOcrSearchRect,
        string stashOcrText,
        int stashOpenDelayMs,
        IReadOnlyList<NwGroupDef> groups,
        int mouseDelayMs,
        int clipboardDelayMs,
        Func<Task> clearClipboard,
        Func<Task<string>> readClipboard,
        IProgress<string>? log,
        CancellationToken ct)
    {
        // Даём PoE выйти на передний план до OCR
        Native.ProcessForeground.TryBringProcessToForeground(
            Native.ProcessForeground.PathOfExile2SteamProcessName);
        await Task.Delay(200, ct).ConfigureAwait(false);

        // OCR-навигация к стэшу
        if (stashOcrSearchRect.Width > 0 && !string.IsNullOrWhiteSpace(stashOcrText))
        {
            log?.Report("[Networth] OCR: ищем стэш…");
            var normalized = WindowsOcrTextLocator.NormalizeForMatch(stashOcrText);
            var match = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                stashOcrSearchRect, normalized, log, ct).ConfigureAwait(false);

            if (match is { } found)
            {
                var (sx, sy) = found.BoundsOnScreen.GetInteriorPoint(inset: 1);
                log?.Report($"[Networth] OCR нашёл стэш → клик ({sx},{sy})");
                Native.Win32Input.MoveTo(sx, sy);
                await Task.Delay(Jitter(mouseDelayMs), ct);
                Native.Win32Input.ClickLeft();
            }
            else
            {
                log?.Report("[Networth] Стэш в OCR не найден — пропускаем навигацию.");
            }
            await Task.Delay(Math.Max(stashOpenDelayMs, 500), ct);
        }

        var results = new List<NetworthGroupResult>();

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            if (group.Items.Count == 0)
            {
                log?.Report($"[Networth] {group.Name}: нет заданных областей — пропускаем.");
                continue;
            }

            log?.Report($"[Networth] Сканируем «{group.Name}» ({group.Items.Count} предм.)…");

            if (group.TabRect.Width > 0 && group.TabRect.Height > 0)
            {
                var (tx, ty) = group.TabRect.GetRandomInteriorPoint();
                Native.Win32Input.MoveTo(tx, ty);
                await Task.Delay(Jitter(mouseDelayMs), ct);
                Native.Win32Input.ClickLeft();
                await Task.Delay(500, ct);
            }

            var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var (itemName, area) in group.Items)
            {
                ct.ThrowIfCancellationRequested();
                var (cx, cy) = area.GetRandomInteriorPoint(inset: 2);
                Native.Win32Input.MoveTo(cx, cy);
                await Task.Delay(Jitter(mouseDelayMs), ct);
                await clearClipboard();
                Native.Win32Input.SendCtrlAltC();
                Native.Win32Input.ReleaseCtrlAlt();
                await Task.Delay(Jitter(clipboardDelayMs), ct);

                var text = await readClipboard();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var parsed = ItemParser.Parse(text);
                if (parsed is not { IsValid: true }) continue;

                // Используем распознанное имя; при неудаче — заранее известное itemName
                var name = string.IsNullOrWhiteSpace(parsed.Name) ? itemName : parsed.Name;
                var qty = parsed.StackSize > 0 ? parsed.StackSize : 1;
                itemCounts.TryGetValue(name, out var prev);
                itemCounts[name] = prev + qty;
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

            results.Add(new NetworthGroupResult { GroupName = group.Name, Items = items });
            log?.Report($"[Networth] {group.Name}: {items.Count} поз., итого ≈ {items.Sum(i => i.TotalDiv):F2} div");
        }

        return results;
    }

    private static int Jitter(int ms) =>
        ms <= 0 ? 0 : (int)(ms * (0.7 + Random.Shared.NextDouble() * 0.6));
}
