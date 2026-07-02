using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Полный авто-цикл: Стэш → наполнить инвентарь → перековка → сбросить инвентарь в Стэш.
/// Оркестрирует <see cref="ReforgeService"/> и взаимодействия со Stash.
/// </summary>
public sealed class AutoReforgeService
{
    private const double DelayJitterFraction = 0.30;

    private static readonly byte VkKey3 = 0x33; // клавиша «3» — закрыть стэш/инвентарь

    public int MouseActionDelayMs           { get; set; } = 80;
    public int ClipboardDelayMs             { get; set; } = 220;
    public int HoverSettleBeforeClipboardMs { get; set; } = 150;
    /// <summary>Задержка после клика по STASH: персонаж идёт к стэшу, мс.</summary>
    public int StashOpenDelayMs             { get; set; } = 3000;
    /// <summary>Задержка после клика по Reforging Bench: персонаж идёт к станку, мс.</summary>
    public int ReforgingBenchOpenDelayMs    { get; set; } = 3000;
    /// <summary>Сколько катализаторов перекладывает один Ctrl+ЛКМ из Breach-вкладки (размер стака в инвентаре).</summary>
    public int StashItemsPerClick           { get; set; } = 10;
    /// <summary>Задержка после каждого Ctrl+ЛКМ/ПКМ при переносе предмета (игра обрабатывает перенос), мс.</summary>
    public int ItemTransferDelayMs          { get; set; } = 400;
    /// <summary>Порог каскада для обычных катализаторов: ≤ этого значения (ex) перековываются повторно. 0 = каскад отключён.</summary>
    public decimal CascadeThresholdEx { get; set; } = 0m;
    /// <summary>Порог каскада для Refined-катализаторов. При перековке Refined → выход тоже Refined. 0 = использовать CascadeThresholdEx.</summary>
    public decimal RefinedCascadeThresholdEx { get; set; } = 0m;
    /// <summary>Функция получения текущей ex-цены по отображаемому имени катализатора. Null = каскад отключён.</summary>
    public Func<string, decimal?>? CascadePriceFunc { get; set; }
    /// <summary>Минимум катализаторов в стэше для участия в перековке. Типы с меньшим остатком пропускаются.</summary>
    public int MinStashCount { get; set; } = 3;

    private readonly ReforgeService _rfService;

    public AutoReforgeService(ReforgeService rfService) => _rfService = rfService;

    /// <summary>
    /// Полный авто-цикл:
    /// 1. OCR-поиск stashOcrText в stashOcrSearchRect → клик → ждём StashOpenDelayMs.
    /// 2. Клик breachTabRect (открыть вкладку Breach).
    /// 3. Ctrl+Alt+C по catalystStashRegions → логируем доступность.
    /// 4. Ctrl+ЛКМ по catalystStashRegions (заполняем inventoryCells, max 40).
    /// 5. Клавиша «3» — закрыть стэш.
    /// 6. OCR-поиск benchOcrText в benchOcrSearchRect → клик → ждём ReforgingBenchOpenDelayMs.
    /// 7. ReforgeService.RunAsync.
    /// 8. OCR-поиск stashOcrText → клик → ждём StashOpenDelayMs.
    /// 9. Клик breachTabRect.
    /// 10. Ctrl+ПКМ по всем inventoryCells → возврат катализаторов в стэш.
    /// </summary>
    public async Task RunAsync(
        IReadOnlyList<ScreenRect> inventoryCells,
        IReadOnlyList<string> selectedTypeIds,
        ScreenRect stashOcrSearchRect,
        string stashOcrText,
        ScreenRect breachTabRect,
        IReadOnlyDictionary<string, ScreenRect> catalystStashRegions,
        IReadOnlyList<ScreenRect> reforgeItemCells,
        ScreenRect benchOcrSearchRect,
        string benchOcrText,
        ScreenRect slot1, ScreenRect slot2, ScreenRect slot3,
        ScreenRect confirmRect, ScreenRect resultRect,
        int maxOps,
        IProgress<string>? log,
        Action<ReforgeAttemptResult>? onAttempt,
        CancellationToken ct)
    {
        // fullInventoryCells — весь инвентарь (12×5), используется только для возврата (шаг 10).
        // reforgeItemCells   — сетка перековки (8×5), используется для заполнения (шаг 4) и самого ReforgeService.
        var fullCells    = inventoryCells.ToList();
        var reforgeGrid  = reforgeItemCells.ToList();

        // ── 1. Открываем стэш (OCR) ───────────────────────────────────────
        log?.Report($"[Авто] Ищем «{stashOcrText}» на экране (OCR)...");
        if (!await FindAndClickOcrAsync(stashOcrSearchRect, stashOcrText, log, ct))
        {
            log?.Report("[Авто] Не найдена надпись STASH — останавливаемся.");
            return;
        }
        log?.Report($"[Авто] Ждём {StashOpenDelayMs} мс (персонаж идёт к стэшу)...");
        await Task.Delay(StashOpenDelayMs, ct);

        // ── 2. Открываем вкладку Breach ──────────────────────────────────
        log?.Report("[Авто] Открываем вкладку Breach...");
        await MoveAndClickAsync(breachTabRect, ct);
        await Task.Delay(WithJitter(ClipboardDelayMs * 2), ct); // Ждём загрузку вкладки

        // ── 3. Сканируем стэш и определяем тип(ы) для этого цикла ──────
        var stashStackSizes = new Dictionary<string, int>();
        List<string> typesWithRegion;

        if (selectedTypeIds.Count == 0)
        {
            // Каскадный режим: все типы с ценой ≤ порога (обычные и refined — отдельные пороги).
            typesWithRegion = [];
            if (CascadeThresholdEx > 0 && CascadePriceFunc != null)
            {
                log?.Report($"[Каскад-стэш] Сканируем стэш (порог обычные ≤ {CascadeThresholdEx} ex, refined ≤ {(RefinedCascadeThresholdEx > 0 ? RefinedCascadeThresholdEx : CascadeThresholdEx)} ex)...");
                foreach (var (id, region) in catalystStashRegions.Where(kv => kv.Value.Width > 0))
                {
                    ct.ThrowIfCancellationRequested();
                    var dName = StackableItemRegistry.Items.FirstOrDefault(e => e.Id == id)?.DisplayName ?? id;
                    var price = CascadePriceFunc(dName);
                    var isRefined = dName.StartsWith("Refined ", StringComparison.OrdinalIgnoreCase);
                    var effectiveThreshold = isRefined && RefinedCascadeThresholdEx > 0
                        ? RefinedCascadeThresholdEx
                        : CascadeThresholdEx;
                    if (!price.HasValue || price.Value > effectiveThreshold) continue;
                    var txt = await ReadClipboardAtAsync(region, ct);
                    if (string.IsNullOrWhiteSpace(txt)) continue;
                    var p = ItemParser.Parse(txt);
                    if (p == null || !p.IsValid) continue;
                    var sz = p.StackSize > 0 ? p.StackSize : 0;
                    log?.Report($"[Каскад-стэш]  {dName}: {sz} шт. ({price:F1} ex)");
                    if (sz < MinStashCount) continue;
                    stashStackSizes[id] = sz;
                    typesWithRegion.Add(id);
                }
                if (typesWithRegion.Count > 0)
                    log?.Report($"[Каскад-стэш] Найдено {typesWithRegion.Count} тип(ов) для перековки — начинаем цикл");
            }
            else
            {
                log?.Report("[Авто] Нет выбранных типов и каскад не настроен — остановка.");
            }

            if (typesWithRegion.Count == 0)
            {
                log?.Report($"[Каскад-стэш] Дешёвых катализаторов (≥{MinStashCount} шт.) не найдено — завершение.");
                Win32Input.ReleaseCtrlAlt();
                Win32Input.PressKey(VkKey3);
                await Task.Delay(WithJitter(1000), ct);
                return;
            }
        }
        else
        {
            // Стандартный режим: только выбранные типы.
            typesWithRegion = [.. selectedTypeIds
                .Where(id => catalystStashRegions.TryGetValue(id, out var r) && r.Width > 0)];

            foreach (var id in typesWithRegion)
            {
                ct.ThrowIfCancellationRequested();
                var region = catalystStashRegions[id];
                var text = await ReadClipboardAtAsync(region, ct);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var parsed = ItemParser.Parse(text);
                if (parsed == null || !parsed.IsValid) continue;
                var stackSize = parsed.StackSize > 0 ? parsed.StackSize : 1;
                var displayName = StackableItemRegistry.Items.FirstOrDefault(e => e.Id == id)?.DisplayName ?? id;
                if (stackSize < MinStashCount)
                {
                    log?.Report($"[Авто] Стэш: {displayName} — {stackSize} шт. (< {MinStashCount}, пропускаем)");
                    continue;
                }
                stashStackSizes[id] = stackSize;
                log?.Report($"[Авто] Стэш: {displayName} — {stackSize} шт.");
            }
            typesWithRegion = [.. typesWithRegion.Where(stashStackSizes.ContainsKey)];

            // Fallback: выбранные типы исчерпаны — переходим к каскадному сканированию
            if (typesWithRegion.Count == 0 && CascadeThresholdEx > 0 && CascadePriceFunc != null)
            {
                log?.Report($"[Авто] Выбранные катализаторы исчерпаны — каскадное сканирование (порог ≤ {CascadeThresholdEx} ex / refined ≤ {(RefinedCascadeThresholdEx > 0 ? RefinedCascadeThresholdEx : CascadeThresholdEx)} ex)...");
                foreach (var (id, region) in catalystStashRegions.Where(kv => kv.Value.Width > 0))
                {
                    ct.ThrowIfCancellationRequested();
                    var dName = StackableItemRegistry.Items.FirstOrDefault(e => e.Id == id)?.DisplayName ?? id;
                    var price = CascadePriceFunc(dName);
                    var isRefined = dName.StartsWith("Refined ", StringComparison.OrdinalIgnoreCase);
                    var effectiveThreshold = isRefined && RefinedCascadeThresholdEx > 0
                        ? RefinedCascadeThresholdEx
                        : CascadeThresholdEx;
                    if (!price.HasValue || price.Value > effectiveThreshold) continue;
                    var txt = await ReadClipboardAtAsync(region, ct);
                    if (string.IsNullOrWhiteSpace(txt)) continue;
                    var p = ItemParser.Parse(txt);
                    if (p == null || !p.IsValid) continue;
                    var sz = p.StackSize > 0 ? p.StackSize : 0;
                    log?.Report($"[Каскад-стэш]  {dName}: {sz} шт. ({price:F1} ex)");
                    if (sz < MinStashCount) continue;
                    stashStackSizes[id] = sz;
                    typesWithRegion.Add(id);
                }
                if (typesWithRegion.Count > 0)
                    log?.Report($"[Каскад-стэш] Найдено {typesWithRegion.Count} тип(ов) для перековки — продолжаем");
            }
        }

        var totalAvailable = stashStackSizes.Values.Sum();
        if (maxOps > 0)
        {
            var needed = maxOps * 3;
            if (totalAvailable < needed)
                log?.Report($"[Авто] Предупреждение: доступно {totalAvailable} шт., нужно {needed} для {maxOps} перековок.");
            else
                log?.Report($"[Авто] Доступно {totalAvailable} шт. — хватит на {totalAvailable / 3} перековок.");
        }
        else if (selectedTypeIds.Count > 0)
        {
            log?.Report($"[Авто] Всего доступно: {totalAvailable} катализаторов.");
        }

        // ── 4. Наполняем инвентарь из стэша ──────────────────────────────
        var totalStacksMoved = 0; // сколько ячеек инвентаря реально заполнено (1 клик = 1 ячейка)
        if (typesWithRegion.Count > 0)
        {
            // Максимум кликов на тип: ёмкость сетки перековки поровну между типами.
            var maxClicksByGrid = reforgeGrid.Count / typesWithRegion.Count;
            var gridRemainder   = reforgeGrid.Count % typesWithRegion.Count;

            for (var ti = 0; ti < typesWithRegion.Count; ti++)
            {
                var id = typesWithRegion[ti];
                var region = catalystStashRegions[id];
                var displayName = StackableItemRegistry.Items.FirstOrDefault(e => e.Id == id)?.DisplayName ?? id;

                // Базовый лимит: поровну ячеек сетки перековки (8×5 = 40) на тип.
                var clicksByGrid = maxClicksByGrid + (ti < gridRemainder ? 1 : 0);

                // Если maxOps задан — нужно только ceil(maxOps×3 / types / itemsPerClick) кликов.
                // StashItemsPerClick = сколько штук перекладывает один Ctrl+ЛКМ из Breach-вкладки (обычно 10).
                var clicksByOps = maxOps > 0
                    ? (int)Math.Ceiling((double)maxOps * 3 / typesWithRegion.Count / StashItemsPerClick)
                    : clicksByGrid;

                // Лимит по реальному количеству в стэше: не кликаем больше, чем там физически есть стаков.
                var clicksByStash = stashStackSizes.TryGetValue(id, out var ssCount)
                    ? (int)Math.Ceiling((double)ssCount / StashItemsPerClick)
                    : clicksByGrid;

                var stacksToMove = Math.Min(Math.Min(clicksByGrid, clicksByOps), clicksByStash);
                if (stacksToMove <= 0) continue;

                var expectedItems = stashStackSizes.TryGetValue(id, out var knownSz) && clicksByStash == stacksToMove
                    ? Math.Min(knownSz, stacksToMove * StashItemsPerClick)
                    : stacksToMove * StashItemsPerClick;
                log?.Report($"[Авто] Переносим {stacksToMove} стак(ов) {displayName} (до {expectedItems} шт.)...");
                for (var s = 0; s < stacksToMove; s++)
                {
                    ct.ThrowIfCancellationRequested();
                    await CtrlClickAsync(region, ct);
                }
                totalStacksMoved += stacksToMove;
            }
        }
        else
        {
            log?.Report("[Авто] Нет выбранных катализаторов с заданными областями в стэше.");
        }

        // ── 5. Закрываем стэш клавишей «3» ───────────────────────────────
        // Дополнительный буфер после последнего переноса — игра должна успеть обработать предмет.
        log?.Report("[Авто] Закрываем стэш (клавиша 3)...");
        Win32Input.ReleaseCtrlAlt();
        await Task.Delay(WithJitter(ItemTransferDelayMs), ct);
        Win32Input.PressKey(VkKey3);
        await Task.Delay(WithJitter(1000), ct); // Ждём закрытия UI стэша перед OCR

        // Если инвентарь остался пустым — нет смысла идти к станку.
        if (totalStacksMoved == 0)
        {
            log?.Report("[Авто] Инвентарь пуст — пропускаем поход к станку.");
            return;
        }

        // ── 6. Открываем станок перековки (OCR) ──────────────────────────
        log?.Report($"[Авто] Ищем «{benchOcrText}» на экране (OCR)...");
        if (!await FindAndClickOcrAsync(benchOcrSearchRect, benchOcrText, log, ct))
        {
            log?.Report("[Авто] Не найдена надпись Reforging Bench — останавливаемся.");
            return;
        }
        log?.Report($"[Авто] Ждём {ReforgingBenchOpenDelayMs} мс (персонаж идёт к станку)...");
        await Task.Delay(ReforgingBenchOpenDelayMs, ct);

        // ── 7. Перековка ──────────────────────────────────────────────────
        // Передаём только реально заполненные ячейки: каждый Ctrl+ЛКМ заполнил ровно одну ячейку.
        var activeCells = totalStacksMoved < reforgeGrid.Count
            ? reforgeGrid.Take(totalStacksMoved).ToList()
            : reforgeGrid;
        log?.Report($"[Авто] Запускаем перековку ({activeCells.Count} из {reforgeGrid.Count} ячеек)...");
        var reason = await _rfService.RunAsync(
            activeCells, selectedTypeIds,  // только заполненные ячейки
            slot1, slot2, slot3, confirmRect, resultRect,
            maxOps, log, onAttempt, ct);

        log?.Report($"[Авто] Перековка завершена: {reason}");

        if (ct.IsCancellationRequested) return;

        // Закрываем UI станка перековки клавишей «3» перед поиском стэша.
        Win32Input.ReleaseCtrlAlt(); // защитный сброс — Alt/Ctrl не должны оставаться зажатыми
        Win32Input.PressKey(VkKey3);
        await Task.Delay(WithJitter(1000), ct); // Ждём закрытия UI станка перед навигацией

        // ── 8. Возвращаемся к стэшу (OCR) ────────────────────────────────
        log?.Report("[Авто] Ищем STASH для возврата катализаторов...");
        if (!await FindAndClickOcrAsync(stashOcrSearchRect, stashOcrText, log, ct))
        {
            log?.Report("[Авто] Не найдена надпись STASH при возврате — катализаторы остались в инвентаре.");
            return;
        }
        log?.Report($"[Авто] Ждём {StashOpenDelayMs} мс...");
        await Task.Delay(StashOpenDelayMs, ct);

        // ── 9. Открываем вкладку Breach ──────────────────────────────────
        await MoveAndClickAsync(breachTabRect, ct);
        await Task.Delay(WithJitter(ClipboardDelayMs * 2), ct); // Ждём загрузку вкладки

        // ── 10. Сбрасываем инвентарь в стэш (Ctrl+ПКМ по всему инвентарю) ──
        // Используем полную сетку инвентаря (12×5): катализаторы могли рассыпаться
        // по разным ячейкам после ReforgeService; пустые клики в стэш безвредны.
        log?.Report($"[Авто] Возвращаем катализаторы в стэш ({fullCells.Count} ячеек)...");
        foreach (var cell in fullCells)
        {
            ct.ThrowIfCancellationRequested();
            await CtrlRightClickAsync(cell, ct);
        }

        log?.Report("[Авто] Авто-цикл завершён.");
    }

    // ── OCR-поиск и клик ─────────────────────────────────────────────────

    /// <summary>
    /// Ищет текст в области экрана через OCR, кликает в центр найденного.
    /// Возвращает false если текст не найден.
    /// </summary>
    private async Task<bool> FindAndClickOcrAsync(ScreenRect searchRect, string labelText, IProgress<string>? log, CancellationToken ct)
    {
        var target = WindowsOcrTextLocator.NormalizeForMatch(labelText);
        if (string.IsNullOrEmpty(target))
        {
            log?.Report($"[Авто] OCR: текст для поиска пустой после нормализации (исходный: «{labelText}»).");
            return false;
        }

        var match = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(searchRect, target, log, ct)
            .ConfigureAwait(false);

        if (match is not { } found)
        {
            log?.Report($"[Авто] OCR: «{labelText}» не найдено в области {searchRect.X},{searchRect.Y} {searchRect.Width}×{searchRect.Height}.");
            return false;
        }

        var (cx, cy) = found.BoundsOnScreen.GetInteriorPoint(inset: 1);
        log?.Report($"[Авто] OCR нашёл «{found.MatchedLineText}» → клик ({cx},{cy})");

        Win32Input.MoveTo(cx, cy);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        Win32Input.ClickLeft();
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        return true;
    }

    // ── Низкоуровневые хелперы ───────────────────────────────────────────

    private async Task<string> ReadClipboardAtAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetRandomInteriorPoint(inset: 2);
        Win32Input.MoveTo(x, y);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        await Task.Delay(HoverSettleBeforeClipboardMs, ct);

        await ClearClipboardAsync();
        Win32Input.SendCtrlAltC();
        Win32Input.ReleaseCtrlAlt();
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
        await Task.Delay(WithJitter(ItemTransferDelayMs), ct);
    }

    private async Task CtrlRightClickAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetRandomInteriorPoint(inset: 2);
        Win32Input.MoveTo(x, y);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        Win32Input.SendCtrlRightClick();
        Win32Input.CtrlUp();
        await Task.Delay(WithJitter(ItemTransferDelayMs), ct);
    }

    private async Task MoveAndClickAsync(ScreenRect rect, CancellationToken ct)
    {
        var (x, y) = rect.GetRandomInteriorPoint(inset: 2);
        Win32Input.MoveTo(x, y);
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
        Win32Input.ClickLeft();
        await Task.Delay(WithJitter(MouseActionDelayMs), ct);
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
