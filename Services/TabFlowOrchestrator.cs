using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Фиксированные зависимости TabFlow-цикла: экранные прямоугольники, сервисы и UI-обратные вызовы.
/// Создаётся в MainWindow при старте цикла и живёт всё время работы оркестратора.
/// </summary>
public sealed class TabFlowContext
{
    // ── Shop / Ange ─────────────────────────────────────────────────────────
    public required List<(GameHelper.ScreenRect Tab, IReadOnlyList<GameHelper.ScreenRect> Cells)> ShopTabs { get; init; }
    public required GameHelper.ScreenRect AngeShopSubTabRect          { get; init; }
    public required GameHelper.ScreenRect AngeShopVerifyRect          { get; init; }
    public required GameHelper.ScreenRect RepricingTraderOcrRect      { get; init; }
    public required GameHelper.ScreenRect RepricingManageShopOcrRect  { get; init; }

    // ── Листинг ─────────────────────────────────────────────────────────────
    public required GameHelper.ScreenRect ListingPriceInputRect       { get; init; }
    public required GameHelper.ScreenRect ListingCurrencyDropdownRect { get; init; }
    public required GameHelper.ScreenRect ListingDivineOrbOcrRect     { get; init; }
    public required GameHelper.ScreenRect ListingListItemBtnRect      { get; init; }
    public required TabletListingService  ListingService              { get; init; }

    // ── Стэш / Заполнение ───────────────────────────────────────────────────
    public required GameHelper.ScreenRect StashOcrSearchRect          { get; init; }
    public required GameHelper.ScreenRect StashIsOpenCheckRect        { get; init; }
    public required GameHelper.ScreenRect FragmentStashTabRect        { get; init; }
    public required GameHelper.ScreenRect FragmentSubTabTabletsRect   { get; init; }
    public required List<GameHelper.ScreenRect> FragmentGridCells     { get; init; }
    public required List<GameHelper.ScreenRect> FragmentPageRects     { get; init; }
    public required List<GameHelper.FragmentTabletTypeSetting> FragmentTabletTypeSettings { get; init; }

    // ── Скан инвентаря ──────────────────────────────────────────────────────
    public required List<GameHelper.ScreenRect> TabletScanCells       { get; init; }
    public required GameHelper.ScreenRect TabletScanNpcOcrRect        { get; init; }
    public required int TabletScanGridCols                            { get; init; }

    // ── Рефордж ─────────────────────────────────────────────────────────────
    public required GameHelper.ReforgeState ReforgeState              { get; init; }
    public required ReforgeService           RfService                { get; init; }

    // ── Валюта (ячейки орбов в стэше) ───────────────────────────────────────
    public required IReadOnlyDictionary<string, GameHelper.ScreenRect> CurrencyItemRegions { get; init; }

    // ── UI-обратные вызовы ──────────────────────────────────────────────────
    /// <summary>Лог + обновление строки статуса (аналог MainWindow.Report).</summary>
    public required Action<string> Report           { get; init; }
    public required Action RebuildTradeGrid         { get; init; }
    public required Action ScheduleSalesFetch       { get; init; }
    public required Action<List<SaleRecord>> UpdateTradeHistory { get; init; }
    public required Action MinimizeToTray           { get; init; }
    public required Action RestoreFromTray          { get; init; }
    public required Action RegisterHotkey           { get; init; }
    public required Action UnregisterHotkey         { get; init; }
    /// <summary>Передаёт активный CTS итерации в MainWindow, чтобы кнопка «Стоп» могла прерывать итерацию.</summary>
    public required Action<CancellationTokenSource?> SetIterCts { get; init; }

    /// <summary>Возвращает ScreenRect орба/валюты по одному из имён. null — ни одно не найдено.</summary>
    public GameHelper.ScreenRect? GetCurrencyRect(params string[] names)
    {
        foreach (var name in names)
            if (CurrencyItemRegions.TryGetValue(name, out var r) && r.Width > 0)
                return r;
        return null;
    }
}

/// <summary>Настройки одной итерации TabFlow, считанные с UI перед стартом итерации.</summary>
public sealed record TabFlowIterSettings(
    int    ActionMs,
    int    ClipMs,
    string TraderOcrText,
    string ManageShopOcrText,
    int    TraderDelayMs,
    int    FillCount,
    int    TransferMs,
    string StashOcrText,
    string StashCheckText,
    int    StashDelayMs,
    int    MouseFillDelayMs,
    int    OrbDelayMs,
    int    UpgradeDelayMs,
    string NpcOcrText,
    double UpgradeThreshold
);

/// <summary>
/// Выполняет TabFlow-цикл (Buy→Fill→Craft→Scan→List→Reprice→Reforge).
/// UI-логика (ESC-хук, MinimizeToTray, кнопки, Dispatcher) остаётся в MainWindow;
/// весь алгоритм итерации/цикла находится здесь.
/// </summary>
public sealed class TabFlowOrchestrator
{
    private readonly TabFlowContext _ctx;

    public TabFlowOrchestrator(TabFlowContext ctx) => _ctx = ctx;

    // ── Цикл ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Запускает бесконечный цикл TabFlow с задержкой <paramref name="intervalMin"/> минут между итерациями.
    /// Завершается при отмене <paramref name="loopCt"/>.
    /// </summary>
    public async Task RunLoopAsync(
        string sessid, string league, int intervalMin, bool dryRun,
        Func<TabFlowIterSettings> getIterSettings,
        CancellationToken loopCt)
    {
        var modeTag = dryRun ? " [DRY-RUN]" : "";
        _ctx.Report($"Цикл TabFlow запущен (интервал {intervalMin} мин){modeTag}.");

        while (!loopCt.IsCancellationRequested)
        {
            using var iterCts = CancellationTokenSource.CreateLinkedTokenSource(loopCt);
            _ctx.SetIterCts(iterCts);
            var iterCt = iterCts.Token;

            _ctx.RegisterHotkey();
            if (!dryRun) _ctx.MinimizeToTray();
            try
            {
                var settings = getIterSettings();
                await RunIterationAsync(sessid, league, dryRun, settings, iterCt);
            }
            catch (OperationCanceledException) when (!loopCt.IsCancellationRequested)
            {
                _ctx.Report($"Итерация прервана. Следующая через {intervalMin} мин...");
            }
            finally
            {
                _ctx.UnregisterHotkey();
                if (!dryRun) _ctx.RestoreFromTray();
                _ctx.SetIterCts(null);
            }

            loopCt.ThrowIfCancellationRequested();
            _ctx.Report($"Следующая итерация через {intervalMin} мин...");
            await Task.Delay(TimeSpan.FromMinutes(intervalMin), loopCt);
        }
    }

    // ── Итерация ────────────────────────────────────────────────────────────

    private async Task RunIterationAsync(
        string sessid, string league, bool dryRun,
        TabFlowIterSettings s, CancellationToken ct)
    {
        // 1. Fetch продаж
        if (!string.IsNullOrWhiteSpace(sessid))
        {
            _ctx.Report("Обновление истории продаж...");
            try
            {
                var existing = TradeHistoryService.LoadFromFile();
                var (merged, newCount) = await TradeHistoryService
                    .FetchAndMergeAsync(league, sessid, existing, ct);
                TradeHistoryService.Save(merged);
                _ctx.UpdateTradeHistory(merged);
                var marked = SoldDetector.DetectAndMark(merged);
                _ctx.Report($"+{newCount} продаж, {marked} закрыто в индексе.");
                _ctx.RebuildTradeGrid();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _ctx.Report($"Fetch продаж: {ex.Message}"); }
        }

        ct.ThrowIfCancellationRequested();

        // 2. Обновить флор-цены (пропускается если данные свежее 4 часов)
        if (!string.IsNullOrWhiteSpace(sessid))
        {
            _ctx.Report("Флор-цены: проверка...");
            var floorLog = await SmartRepricingService.FetchFloorPricesAsync(
                ProjectPaths.GetProjectRoot(), sessid, league, ct).ConfigureAwait(false);
            _ctx.Report($"Флор: {floorLog}");
        }

        // 2a. Обновить poe.ninja (курс chaos/divine + цены орбов) раз в час
        {
            var lastFetch = PoeNinjaPriceService.LastFetchedAt;
            if (lastFetch is null || (DateTime.Now - lastFetch.Value).TotalHours >= 1)
            {
                _ctx.Report("poe.ninja: обновление цен...");
                try
                {
                    await PoeNinjaPriceService.FetchAsync(league, ct).ConfigureAwait(false);
                    _ctx.Report($"poe.ninja: {PoeNinjaPriceService.ItemCount} предметов");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _ctx.Report($"poe.ninja: {ex.Message}"); }
            }
        }

        ct.ThrowIfCancellationRequested();

        // 2b. Крафт-цикл: заполнение → орбы → скан → апгрейд → ресканирование
        var craftScanPrices = new List<(GameHelper.ScreenRect Cell, double Price, string ItemText)>();
        if (!dryRun && (_ctx.FragmentGridCells.Count > 0 || _ctx.TabletScanCells.Count > 0))
        {
            var craftLog = new Progress<string>(_ctx.Report);
            var craftStashCfg = new StashOpenConfig(
                _ctx.StashOcrSearchRect, s.StashOcrText,
                _ctx.StashIsOpenCheckRect, s.StashCheckText,
                s.StashDelayMs);
            craftScanPrices = await RunCraftCycleAsync(
                s.MouseFillDelayMs, s.TransferMs, s.FillCount,
                s.OrbDelayMs, s.ActionMs, s.ClipMs, s.NpcOcrText,
                s.UpgradeThreshold, s.UpgradeDelayMs,
                craftStashCfg, craftLog, ct).ConfigureAwait(false);
        }
        else if (dryRun)
        {
            var hasFrag = _ctx.FragmentGridCells.Count > 0;
            var hasScan = _ctx.TabletScanCells.Count > 0;
            if (!hasFrag && !hasScan)
            {
                _ctx.Report("[DRY-RUN] Крафт-цикл: ячейки не настроены — пропуск.");
            }
            else
            {
                var enabledTypes = _ctx.FragmentTabletTypeSettings
                    .Where(ts => ts.IsEnabled)
                    .Select(ts => ts.Name)
                    .ToList();
                var typeStr = enabledTypes.Count > 0
                    ? string.Join(", ", enabledTypes) : "нет включённых";
                _ctx.Report($"[DRY-RUN] Крафт-цикл: фрагменты={_ctx.FragmentGridCells.Count} ячеек | " +
                            $"инвентарь={_ctx.TabletScanCells.Count} ячеек | лимит={s.FillCount}");
                _ctx.Report($"[DRY-RUN] Типы: {typeStr}");
                _ctx.Report("[DRY-RUN] Заполнение, крафт и листинг пропущены (нет кликов).");
            }
        }

        ct.ThrowIfCancellationRequested();

        // 3. Генерация плана переоценки
        _ctx.Report("Генерация плана переоценки...");
        var (plan, scriptLog) = await SmartRepricingService
            .GeneratePlanAsync(ProjectPaths.GetProjectRoot(), ct);
        var lastLine = scriptLog.Split('\n').LastOrDefault("") ?? "";
        _ctx.Report(lastLine);

        if (plan.Count == 0) { _ctx.Report("Нет позиций для переоценки."); return; }

        ct.ThrowIfCancellationRequested();

        // 4. Выполнение переоценки / рефорджа
        var progress = new Progress<string>(_ctx.Report);

        int done = 0, skipped = 0, delisted = 0;
        if (dryRun)
        {
            var reprice = plan.Where(p => p.Action != "delist_for_reforge").ToList();
            var delist  = plan.Where(p => p.Action == "delist_for_reforge").ToList();
            _ctx.Report($"[DRY-RUN] Репрайс: {reprice.Count} позиций, снять на рефордж: {delist.Count}");
            foreach (var p in reprice)
                ((IProgress<string>)progress).Report(
                    $"  [DRY-RUN] 📉 [{p.Col},{p.Row}] {p.BaseType} " +
                    $"{p.CurrentPrice}{p.CurrentCurrency[0]} → {p.NewPrice}{p.NewCurrency[0]}  ({p.Reason})");
            foreach (var p in delist)
                ((IProgress<string>)progress).Report(
                    $"  [DRY-RUN] ♻ [{p.Col},{p.Row}] {p.BaseType} {p.CurrentPrice}{p.CurrentCurrency[0]} — delist");
            await Task.Delay(500, ct);
        }
        else
        {
            ProcessForeground.TryBringProcessToForeground(
                ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(500, ct).ConfigureAwait(false);

            _ctx.Report(
                $"[Ange-DBG] traderRect={_ctx.RepricingTraderOcrRect.Width}×{_ctx.RepricingTraderOcrRect.Height}" +
                $" text='{s.TraderOcrText}' shopRect={_ctx.RepricingManageShopOcrRect.Width}×{_ctx.RepricingManageShopOcrRect.Height}" +
                $" text='{s.ManageShopOcrText}'");

            var angeOk = await GameUiHelper.EnsureAngeOpenAsync(
                _ctx.RepricingTraderOcrRect,       s.TraderOcrText,
                _ctx.RepricingManageShopOcrRect,   s.ManageShopOcrText,
                traderOpenDelayMs: s.TraderDelayMs,
                mouseDelayMs:      s.ActionMs,
                log:               progress,
                ct:                ct,
                shopSubTabRect:    _ctx.AngeShopSubTabRect,
                shopVerifyRect:    _ctx.AngeShopVerifyRect);

            if (!angeOk)
            {
                _ctx.Report("Не удалось открыть магазин Ange — переоценка пропущена.");
                return;
            }

            // 4a. Листинг скрафченных табличек (Ange уже открыт)
            if (craftScanPrices.Count > 0)
            {
                _ctx.Report($"[Крафт] Листинг {craftScanPrices.Count} новых табличек...");
                var logPath  = System.IO.Path.Combine(ProjectPaths.GetProjectRoot(), "vault", "tabflow", "listings.jsonl");
                var gridRows = _ctx.TabletScanGridCols > 0 && _ctx.TabletScanCells.Count > 0
                    ? _ctx.TabletScanCells.Count / _ctx.TabletScanGridCols : 5;
                var lSvc = _ctx.ListingService;
                lSvc.ResetState();
                int newListed = 0, newSkipped = 0;
                for (var i = 0; i < craftScanPrices.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (cell, price, itemText) = craftScanPrices[i];
                    var cellIdx = _ctx.TabletScanCells.IndexOf(cell);
                    var (col, row) = cellIdx >= 0
                        ? TabletListingsIndex.CellIndexToColRow(cellIdx, gridRows)
                        : (i + 1, 1);
                    var wasListed = await lSvc.ListAsync(
                        cell, price, col, row, itemText, logPath,
                        _ctx.ListingPriceInputRect, _ctx.ListingCurrencyDropdownRect,
                        _ctx.ListingDivineOrbOcrRect, _ctx.ListingListItemBtnRect,
                        progress, ct).ConfigureAwait(false);
                    if (wasListed) newListed++; else newSkipped++;
                }
                _ctx.Report($"[Крафт] Выставлено {newListed}, пропущено (пустых) {newSkipped}.");
                if (newListed > 0) _ctx.ScheduleSalesFetch();
            }

            var repriceSvc = new SmartRepricingService
            {
                ActionDelayMs    = s.ActionMs,
                ClipboardDelayMs = s.ClipMs,
            };
            (done, skipped, delisted) = await repriceSvc.ExecutePlanAsync(
                plan, _ctx.ShopTabs,
                chaosOrbOcrRect:      _ctx.ListingDivineOrbOcrRect,
                priceInputRect:       _ctx.ListingPriceInputRect,
                currencyDropdownRect: _ctx.ListingCurrencyDropdownRect,
                listItemBtnRect:      _ctx.ListingListItemBtnRect,
                log:                  progress,
                ct:                   ct);
            _ctx.Report($"Переоценка: {done} готово, {delisted} на рефордж, {skipped} пропущено.");
        }

        ct.ThrowIfCancellationRequested();

        // 5. Рефордж «3 в 1»
        var reforgeQueue = TabletReforgeQueue.Count;

        if (dryRun)
        {
            var wouldDelist = plan.Count(p => p.Action == "delist_for_reforge");
            var queueAfter  = reforgeQueue + wouldDelist;
            _ctx.Report($"[DRY-RUN] Очередь рефорджа: {reforgeQueue} + {wouldDelist} = {queueAfter}/{s.FillCount}" +
                        (queueAfter >= s.FillCount ? " → запустил бы цикл рефорджа" : " → накапливаем"));
        }
        else if (delisted > 0)
        {
            var reforgeInv = _ctx.ReforgeState.ItemCells.Count > 0
                ? (IReadOnlyList<GameHelper.ScreenRect>)_ctx.ReforgeState.ItemCells
                : [];

            var tabletReforge = new TabletReforgeService(_ctx.RfService)
            {
                ActionDelayMs   = s.ActionMs,
                TransferDelayMs = s.TransferMs,
            };

            var stashCfg = new StashOpenConfig(
                _ctx.StashOcrSearchRect, s.StashOcrText,
                _ctx.StashIsOpenCheckRect, s.StashCheckText,
                s.StashDelayMs);

            if (reforgeQueue >= s.FillCount && reforgeInv.Count > 0 && _ctx.FragmentGridCells.Count > 0)
            {
                _ctx.Report($"[Рефордж] Очередь {reforgeQueue} ≥ {s.FillCount} — запускаем цикл...");
                await tabletReforge.RunAsync(
                    reforgeInv,
                    _ctx.FragmentStashTabRect, _ctx.FragmentSubTabTabletsRect, _ctx.FragmentGridCells,
                    _ctx.ReforgeState.Slot1Rect, _ctx.ReforgeState.Slot2Rect, _ctx.ReforgeState.Slot3Rect,
                    _ctx.ReforgeState.ConfirmRect, _ctx.ReforgeState.ResultRect,
                    stashCfg: stashCfg, targetCount: s.FillCount, log: progress, ct: ct);
            }
            else if (reforgeInv.Count > 0)
            {
                _ctx.Report($"[Рефордж] Очередь {reforgeQueue}/{s.FillCount} — сброс в стэш...");
                await tabletReforge.DumpInventoryToStashAsync(
                    reforgeInv, _ctx.FragmentStashTabRect, _ctx.FragmentSubTabTabletsRect,
                    stashCfg: stashCfg, log: progress, ct: ct);
            }
        }
    }

    // ── Крафт-цикл ──────────────────────────────────────────────────────────

    /// <summary>
    /// Заполнение из стэша → идентификация → OoT+OoA → скан Magic → апгрейд → скан Rare.
    /// Возвращает список оценённых Rare-табличек для последующего листинга.
    /// </summary>
    private async Task<List<(GameHelper.ScreenRect Cell, double Price, string ItemText)>> RunCraftCycleAsync(
        int mouseFillDelayMs, int transferDelayMs, int maxFillCount,
        int orbDelayMs, int hoverMs, int clipMs, string npcOcrText,
        double upgradeThreshold, int upgradeDelayMs,
        StashOpenConfig stashCfg,
        IProgress<string> log, CancellationToken ct)
    {
        var scanPrices = new List<(GameHelper.ScreenRect Cell, double Price, string ItemText)>();

        // 1. Заполнение из стэша
        if (_ctx.FragmentGridCells.Count > 0)
        {
            _ctx.Report("[Крафт] Заполнение из стэша...");
            var fillSvc = new FragmentStashFillService
            {
                MouseActionDelayMs    = mouseFillDelayMs,
                TransferDelayMs       = transferDelayMs,
                ClipboardDelayMs      = clipMs,
                SkipReforgeQueueItems = true,
            };
            var enabledTypes = _ctx.FragmentTabletTypeSettings.Where(t => t.IsEnabled && t.IconRect.Width > 0).ToList();
            var remaining    = maxFillCount > 0 ? maxFillCount : int.MaxValue;
            var totalTaken   = 0;
            if (enabledTypes.Count == 0)
            {
                totalTaken = await fillSvc.FillAsync(
                    _ctx.FragmentStashTabRect, _ctx.FragmentSubTabTabletsRect,
                    default, _ctx.FragmentPageRects, _ctx.FragmentGridCells,
                    maxFillCount, log, ct).ConfigureAwait(false);
            }
            else
            {
                foreach (var ts in enabledTypes)
                {
                    ct.ThrowIfCancellationRequested();
                    if (remaining <= 0) break;
                    var taken = await fillSvc.FillAsync(
                        _ctx.FragmentStashTabRect, _ctx.FragmentSubTabTabletsRect,
                        ts.IconRect, _ctx.FragmentPageRects, _ctx.FragmentGridCells,
                        remaining == int.MaxValue ? 0 : remaining,
                        log, ct).ConfigureAwait(false);
                    totalTaken += taken;
                    if (maxFillCount > 0) remaining -= taken;
                }
            }
            _ctx.Report($"[Крафт] Взято {totalTaken} шт. из стэша.");
        }

        ct.ThrowIfCancellationRequested();

        if (_ctx.TabletScanCells.Count == 0)
            return scanPrices;

        // 2. Идентификация через NPC (Ctrl+ЛКМ)
        if (_ctx.TabletScanNpcOcrRect.Width > 0 && !string.IsNullOrEmpty(npcOcrText))
        {
            _ctx.Report("[Крафт] Поиск NPC для идентификации...");
            var norm  = WindowsOcrTextLocator.NormalizeForMatch(npcOcrText);
            var match = await WindowsOcrTextLocator
                .TryFindNormalizedSubstringAsync(_ctx.TabletScanNpcOcrRect, norm, null, ct)
                .ConfigureAwait(false);
            if (match is { } found)
            {
                var (nx, ny) = found.BoundsOnScreen.GetInteriorPoint(1);
                _ctx.Report($"[Крафт] Identify All: Ctrl+ЛКМ по «{found.MatchedLineText}» ({nx},{ny})...");
                Win32Input.MoveTo(nx, ny);
                await Task.Delay(hoverMs, ct).ConfigureAwait(false);
                Win32Input.SendCtrlLeftClick();
                await Task.Delay(hoverMs * 3, ct).ConfigureAwait(false);
            }
            else
            {
                _ctx.Report($"[Крафт] NPC «{npcOcrText}» не найден — идентификация пропущена.");
            }
        }

        ct.ThrowIfCancellationRequested();

        // 2b. Определяем занятые ячейки инвентаря
        var occupiedCells = GridOccupancyDetector.FilterOccupied(_ctx.TabletScanCells);
        _ctx.Report($"[Крафт] Занято {occupiedCells.Count} из {_ctx.TabletScanCells.Count} ячеек.");
        if (occupiedCells.Count == 0)
        {
            _ctx.Report("[Крафт] Инвентарь пуст — пропускаем орбы и листинг.");
            return scanPrices;
        }

        ct.ThrowIfCancellationRequested();

        // 3. Открытие стэша (орбы берём из стэша)
        _ctx.Report("[Крафт] Открываю стэш...");
        var stashOk = await GameUiHelper.EnsureStashOpenAsync(stashCfg, log, ct).ConfigureAwait(false);
        if (!stashOk)
        {
            _ctx.Report("[Крафт] Стэш не открылся — пропускаем орбы и апгрейд.");
            return scanPrices;
        }

        // 4. OoT → занятые ячейки (Normal → Magic)
        var orbSvc = new TabletOrbApplicationService { ActionDelayMs = orbDelayMs };
        if (_ctx.GetCurrencyRect("Orb of Transmutation") is { } ootRect)
        {
            _ctx.Report("[Крафт] Применяю OoT...");
            await orbSvc.ApplyAsync(ootRect, occupiedCells, log, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        // 5. OoA → занятые ячейки (+1 мод к Magic)
        if (_ctx.GetCurrencyRect("Orb of Augmentation") is { } ooaRect)
        {
            _ctx.Report("[Крафт] Применяю OoA...");
            await orbSvc.ApplyAsync(ooaRect, occupiedCells, log, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        // 6. Скан и оценка Magic-табличек
        (int x, int y)? npcTarget = null;
        if (_ctx.TabletScanNpcOcrRect.Width > 0 && !string.IsNullOrEmpty(npcOcrText))
        {
            var norm  = WindowsOcrTextLocator.NormalizeForMatch(npcOcrText);
            var match = await WindowsOcrTextLocator
                .TryFindNormalizedSubstringAsync(_ctx.TabletScanNpcOcrRect, norm, null, ct)
                .ConfigureAwait(false);
            if (match is { } found)
                npcTarget = found.BoundsOnScreen.GetInteriorPoint(1);
        }

        var scanSvc = new TabletInventoryScanService
        {
            HoverSettleMs    = hoverMs,
            ClipboardDelayMs = clipMs,
        };

        _ctx.Report("[Крафт] Сканирование (Magic)...");
        var magicResults = await scanSvc.ScanAsync(npcTarget, occupiedCells, log, ct).ConfigureAwait(false);

        var magicPrices = new List<(GameHelper.ScreenRect Cell, double Price, string ItemText)>();
        ct.ThrowIfCancellationRequested();
        var magicNonEmpty = magicResults.Where(r => !r.IsEmpty).ToList();
        var magicEvals    = await TabletEvaluator.EvaluateBatchAsync(
            magicNonEmpty.Select(r => r.ItemText).ToList()).ConfigureAwait(false);
        for (var i = 0; i < magicNonEmpty.Count; i++)
        {
            var eval  = i < magicEvals.Count ? magicEvals[i] : "Нет ответа";
            var price = TabletEvaluator.ParsePrice(eval);
            if (price.HasValue)
                magicPrices.Add((magicNonEmpty[i].Cell, price.Value, magicNonEmpty[i].ItemText));
        }
        _ctx.Report($"[Крафт] Magic-оценка: {magicPrices.Count} табличек.");

        ct.ThrowIfCancellationRequested();

        // 7. Апгрейд: Regal+Exalt / Alchemy
        var expensive = magicPrices.Where(p => p.Price >= upgradeThreshold).Select(p => p.Cell).ToList();
        var cheap     = magicPrices.Where(p => p.Price <  upgradeThreshold).Select(p => p.Cell).ToList();
        _ctx.Report($"[Крафт] Апгрейд: {expensive.Count} × Regal+Exalt, {cheap.Count} × Alchemy.");

        var upgSvc    = new TabletOrbApplicationService { ActionDelayMs = upgradeDelayMs };
        var regalRect = _ctx.GetCurrencyRect("Regal Orb", "Greater Regal Orb", "Perfect Regal Orb");
        var exaltRect = _ctx.GetCurrencyRect("Exalted Orb", "Greater Exalted Orb", "Perfect Exalted Orb");
        var alchRect  = _ctx.GetCurrencyRect("Orb of Alchemy");

        if (expensive.Count > 0 && regalRect is { } reg && exaltRect is { } ext)
        {
            await upgSvc.ApplyAsync(reg, expensive, log, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await upgSvc.ApplyAsync(ext, expensive, log, ct).ConfigureAwait(false);
        }
        if (cheap.Count > 0 && alchRect is { } alc)
        {
            ct.ThrowIfCancellationRequested();
            await upgSvc.ApplyAsync(alc, cheap, log, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        // 8. Ресканирование Rare-табличек (финальная оценка)
        _ctx.Report("[Крафт] Сканирование (Rare)...");
        var rareResults = await scanSvc.ScanAsync(npcTarget, occupiedCells, log, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        var rareNonEmpty = rareResults.Where(r => !r.IsEmpty).ToList();
        var rareEvals    = await TabletEvaluator.EvaluateBatchAsync(
            rareNonEmpty.Select(r => r.ItemText).ToList()).ConfigureAwait(false);
        for (var i = 0; i < rareNonEmpty.Count; i++)
        {
            var eval  = i < rareEvals.Count ? rareEvals[i] : "Нет ответа";
            var price = TabletEvaluator.ParsePrice(eval);
            if (price.HasValue)
                scanPrices.Add((rareNonEmpty[i].Cell, price.Value, rareNonEmpty[i].ItemText));
        }
        _ctx.Report($"[Крафт] Rare-оценка: {scanPrices.Count} табличек готово к листингу.");

        return scanPrices;
    }
}
