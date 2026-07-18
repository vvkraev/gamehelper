using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using TradeBot.Browser;
using TradeBot.Config;
using TradeBot.Services;
using TradeBot.Trade;

namespace TradeBot.Modes;

// Поддерживает несколько одновременных WS-подключений (несколько вкладок с live search).
// Каждая вкладка — отдельный продюсер; один консюмер обрабатывает батчи из общей очереди.
// Батч = один продавец = одна поездка в хайдаут; хранит ссылку на клиента для whisper.
public sealed class LiveSearchMode(TradeBotSettings cfg, InventoryState inventoryState, Action<string> log, Action? onInventoryFull = null)
{
    private record SellerBatch(List<TradeItem> Items, TcpWsClient Client);

    private int _processed;
    private int _pendingBatches;
    private int _activeProducers;
    private Channel<SellerBatch> _channel = Channel.CreateUnbounded<SellerBatch>();
    // Per-client TCS for whisper results sent back from Tampermonkey (ok, httpStatus).
    private readonly ConcurrentDictionary<TcpWsClient, TaskCompletionSource<(bool ok, int status)>> _whisperResults = new();

    /// <summary>Если задан — проверяет процесс игры при старте и после серии таймаутов хайдаута.</summary>
    public GameClientGuard? Guard { get; set; }

    /// <summary>Если задан — записывает цены всех входящих листингов для трекинга флора.</summary>
    public FloorTracker? FloorTracker { get; set; }

    /// <summary>Если задан — проверяет не заморожен ли экран после возврата в свой хайдаут.</summary>
    public FreezeDetector? FreezeDetector { get; set; }

    /// <summary>Если задан — выполняет вход в игру после перезапуска клиента.</summary>
    public GameHelper.Services.GameLoginService? LoginService { get; set; }

    // A/B stats: [0]=single, [1]=double. Indices: 0=whisperAttempts, 1=whisperOk, 2=merchantFound.
    private readonly int[] _abWhisperAttempts = new int[2];
    private readonly int[] _abWhisperOk = new int[2];
    private readonly int[] _abMerchantFound = new int[2];
    private int _abCounter;

    // Счётчик подряд идущих паттернов «whisper OK → таймаут Merchant».
    // FreezeDetector запускается только при _consecutiveFreezePattern >= 2.
    private int _consecutiveFreezePattern;

    // Скриншот, снятый когда OCR подтвердил что мы не переместились после первого таймаута.
    // Сравнивается при следующем таймауте — совпадение означает зависание на загрузке.
    private System.Drawing.Bitmap? _freezeSuspectScreenshot;

    public async Task RunAsync(CancellationToken ct)
    {
        Guard?.EnsureRunning();

        using var server = new MiniWsServer(8765);
        server.Start();
        log("[LiveSearch] WS-сервер запущен на ws://127.0.0.1:8765");
        log("[LiveSearch] Перезагрузи страницы торговли и включи Live Search — Tampermonkey подключится автоматически.");

        _channel = Channel.CreateUnbounded<SellerBatch>();
        Interlocked.Exchange(ref _pendingBatches, 0);
        Interlocked.Exchange(ref _activeProducers, 0);

        var consumerTask = ConsumeAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            TcpWsClient? client = null;
            try
            {
                client = await server.AcceptAsync(ct);
                var tabCount = Interlocked.Increment(ref _activeProducers);
                log($"[LiveSearch] Tampermonkey подключился. Активных вкладок: {tabCount}");

                var captured = client!;
                _ = Task.Run(async () =>
                {
                    try { await ProduceAsync(captured, ct); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                        { log($"[LiveSearch] Ошибка вкладки: {ex.Message}"); }
                    finally
                    {
                        captured.Dispose();
                        var remaining = Interlocked.Decrement(ref _activeProducers);
                        log($"[LiveSearch] Вкладка отключилась. Активных вкладок: {remaining}");
                    }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log($"[LiveSearch] Ошибка подключения: {ex.Message}");
                client?.Dispose();
                await Task.Delay(1000, ct);
            }
        }

        _channel.Writer.TryComplete();
        await consumerTask;
    }

    // ── Продюсер: читает WS-фреймы, группирует по продавцу, кладёт батчи в очередь ──

    private async Task ProduceAsync(TcpWsClient client, CancellationToken ct)
    {
        await foreach (var msg in client.ReadTextFramesAsync(ct))
        {
            if (TryHandleWhisperResult(msg, client)) continue;

            foreach (var batch in ParseAndGroupBySeller(msg, client))
            {
                Interlocked.Increment(ref _pendingBatches);
                await _channel.Writer.WriteAsync(batch, ct);
            }
        }
        // не завершаем Writer — другие продюсеры могут быть активны
    }

    private bool TryHandleWhisperResult(string msg, TcpWsClient client)
    {
        try
        {
            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "whisper_result") return false;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetInt32() : 0;
            if (_whisperResults.TryGetValue(client, out var tcs))
                tcs.TrySetResult((ok, status));
            return true;
        }
        catch { return false; }
    }

    private List<SellerBatch> ParseAndGroupBySeller(string msg, TcpWsClient client)
    {
        var result = new List<SellerBatch>();
        try
        {
            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "items") return result;
            if (!root.TryGetProperty("data", out var data)) return result;
            if (!data.TryGetProperty("result", out var results)) return result;

            var sellerOrder = new List<string>();
            var groups = new Dictionary<string, List<TradeItem>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in results.EnumerateArray())
            {
                var item = TradeApiClient.ParseItem(entry);
                if (item == null) continue;

                // Записываем цену до любой фильтрации — нужен весь рынок для флора
                FloorTracker?.Record(item.TypeLine, item.Price, item.Currency);

                if (!string.IsNullOrEmpty(cfg.OwnAccountName) &&
                    item.SellerAccount.Equals(cfg.OwnAccountName, StringComparison.OrdinalIgnoreCase))
                {
                    log($"  [skip] {item.SellerAccount} — собственный листинг");
                    continue;
                }

                if (string.IsNullOrEmpty(item.HideoutToken))
                {
                    log($"  [skip] {item.SellerAccount} — нет hideout_token");
                    continue;
                }

                if (!groups.ContainsKey(item.SellerAccount))
                {
                    sellerOrder.Add(item.SellerAccount);
                    groups[item.SellerAccount] = [];
                }
                groups[item.SellerAccount].Add(item);
            }

            foreach (var seller in sellerOrder)
                result.Add(new SellerBatch(groups[seller], client));
        }
        catch (Exception ex)
        {
            log($"  ✗ Ошибка парсинга: {ex.Message}");
        }
        return result;
    }

    // ── Консюмер: обрабатывает батчи один за другим ──

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var batch in _channel.Reader.ReadAllAsync(ct))
        {
            await ProcessSellerBatchAsync(batch.Items, batch.Client, ct);

            var remaining = Interlocked.Decrement(ref _pendingBatches);
            if (remaining == 0)
                await GoToOwnHideoutAsync(ct);
        }
    }

    /// <summary>
    /// Подсчитывает активные (непроданные) листинги в listings_index.json
    /// и возвращает долю от 144 слотов витрины. Возвращает 0 если файл не найден.
    /// </summary>
    private double ReadShowcaseFillRate()
    {
        const int ShowcaseSlots = 144;

        // Поднимаемся по дереву директорий от exe до нахождения vault/tabflow/listings_index.json
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        string indexPath = "";
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "vault", "tabflow", "listings_index.json");
            if (System.IO.File.Exists(candidate)) { indexPath = candidate; break; }
        }

        if (string.IsNullOrEmpty(indexPath)) return 0.0;

        try
        {
            var json = System.IO.File.ReadAllText(indexPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0.0;

            var active = doc.RootElement.EnumerateArray()
                .Count(e => !(e.TryGetProperty("sold", out var s) && s.ValueKind == JsonValueKind.True));

            return (double)active / ShowcaseSlots;
        }
        catch { return 0.0; }
    }

    private async Task ProcessSellerBatchAsync(List<TradeItem> items, TcpWsClient client, CancellationToken ct)
    {
        // Проверка заполненности витрины перед поездкой к продавцу
        if (cfg.PauseFillRateThreshold > 0)
        {
            var fillRate = ReadShowcaseFillRate();
            if (fillRate >= cfg.PauseFillRateThreshold)
            {
                log($"  ⏸ витрина {fillRate:P0} ≥ порог {cfg.PauseFillRateThreshold:P0} — пропуск {items[0].SellerAccount}");
                return;
            }
        }

        // Проверка инвентаря перед поездкой к продавцу
        if (cfg.InventoryCheckEnabled)
        {
            var free = inventoryState.FreeCount;
            log($"  [инвентарь] свободно ячеек: {free}");

            var belowThreshold = free < cfg.InventoryMinFreeSlots;
            // Проверяем, влезет ли хоть один предмет из батча по реальным размерам
            var noneCanFit = cfg.AutoBuy && items.All(i => !inventoryState.CanFitItem(i.Width, i.Height));

            if (belowThreshold || noneCanFit)
            {
                var reason = noneCanFit
                    ? $"не влезет {items[0].Width}×{items[0].Height} (свободно {free} яч.)"
                    : $"свободно {free} < порог {cfg.InventoryMinFreeSlots}";
                log($"  ⚠ {reason}, пропуск продавца {items[0].SellerAccount}");

                if (onInventoryFull != null)
                {
                    log("  → лимит инвентаря достигнут, завершение...");
                    onInventoryFull();
                }
                return;
            }
        }

        var first = items[0];
        var countStr = items.Count > 1 ? $" ({items.Count} предм.)" : "";
        log($"  [{++_processed}] {first.SellerAccount}{countStr}  {first.Price} {first.Currency}");

        Win32Input.MoveTo(10, 110);
        log("  → мышь убрана из области торговли (10,110)");

        // A/B: чётные попытки = A (single), нечётные = B (double).
        var abIdx = Interlocked.Increment(ref _abCounter) % 2; // 0=A, 1=B
        var abLabel = abIdx == 0 ? "A-single" : "B-double";
        Interlocked.Increment(ref _abWhisperAttempts[abIdx]);

        // Регистрируем TCS до отправки вискера, чтобы не пропустить быстрый ответ.
        var whisperTcs = new TaskCompletionSource<(bool ok, int status)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _whisperResults[client] = whisperTcs;
        bool whisperOk;
        try
        {
            object whisperPayload = abIdx == 0
                ? new { type = "whisper", token = first.HideoutToken }
                : new { type = "whisper", token = first.HideoutToken, @double = true };
            var whisperCmd = JsonSerializer.Serialize(whisperPayload);
            await client.SendTextAsync(whisperCmd, ct);
            log($"  [{abLabel}] whisper отправлен");

            // Ждём подтверждения от Tampermonkey (v0.9+). При старой версии — таймаут, продолжаем.
            // Mode B: Tampermonkey делает два запроса (~300мс между ними) — увеличиваем таймаут до 10с.
            var timeoutMs = abIdx == 1 ? 10_000 : 6_000;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(timeoutMs);
                try
                {
                    var (ok, httpStatus) = await whisperTcs.Task.WaitAsync(timeoutCts.Token);
                    whisperOk = ok;
                    log($"  [{abLabel}] whisper HTTP {httpStatus}: {(ok ? "ok" : "отклонён")}");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    log($"  [{abLabel}] whisper ответ: нет (старый Tampermonkey?), продолжаем");
                    whisperOk = true;
                }
            }
        }
        finally
        {
            _whisperResults.TryRemove(client, out _);
        }

        if (!whisperOk)
        {
            log($"  [{abLabel}] ✗ вискер отклонён — пропуск");
            return;
        }
        Interlocked.Increment(ref _abWhisperOk[abIdx]);

        await Task.Delay(300, ct);
        if (!Win32Input.SwitchToProcess(cfg.GameProcessName))
            log($"  предупреждение: процесс «{cfg.GameProcessName}» не найден");
        await Task.Delay(150, ct);

        log($"  [{abLabel}] ожидание Merchant...");
        var detector = new HideoutDetector(cfg.MerchantRegion!.Value, cfg.HideoutTimeoutMs);
        if (!await detector.WaitForMerchantAsync(ct, log))
        {
            log($"  [{abLabel}] ✗ таймаут ожидания хайдаута");
            Guard?.RecordMiss();
            Interlocked.Increment(ref _consecutiveFreezePattern);
            return;
        }
        Guard?.RecordSuccess();
        Interlocked.Exchange(ref _consecutiveFreezePattern, 0);
        _freezeSuspectScreenshot?.Dispose();
        _freezeSuspectScreenshot = null;
        Interlocked.Increment(ref _abMerchantFound[abIdx]);
        log($"  [{abLabel}] ✓ Merchant найден");

        if (cfg.AutoBuy && cfg.StashRegion.HasValue)
        {
            await Task.Delay(100, ct);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!inventoryState.CanFitItem(item.Width, item.Height))
                {
                    log($"  пропуск {item.Width}×{item.Height} — не влезет в инвентарь");
                    continue;
                }
                var col = Math.Clamp(item.StashX, 0, cfg.StashCols - 1);
                var row = Math.Clamp(item.StashY, 0, cfg.StashRows - 1);
                var cells = ScreenRect.SplitIntoGrid(cfg.StashRegion.Value, cfg.StashCols, cfg.StashRows);
                var (cx, cy) = cells[col * cfg.StashRows + row].Center;
                log($"  Ctrl+ЛКМ → ({item.StashX},{item.StashY}) экран ({cx},{cy})");
                Win32Input.CtrlClickLeft(cx, cy);
                if (i < items.Count - 1)
                    await Task.Delay(150, ct);
            }
            foreach (var bought in items)
                inventoryState.RecordPurchasedItem(bought.Width, bought.Height);
            log($"  ✓ куплено. [инвентарь] свободно: {inventoryState.FreeCount}/{InventoryService.Total}");
            foreach (var line in inventoryState.ToVisualLines())
                log($"    {line}");
            await Task.Delay(cfg.DelayBetweenVisitsMs, ct);
        }
    }

    private void LogAbStats()
    {
        var aAttempts = _abWhisperAttempts[0]; var bAttempts = _abWhisperAttempts[1];
        if (aAttempts + bAttempts == 0) return;

        static string Pct(int num, int den) => den == 0 ? "—" : $"{100 * num / den}%";

        log($"  [A/B] A-single:  {aAttempts} попыток | whisperOk {_abWhisperOk[0]} ({Pct(_abWhisperOk[0], aAttempts)}) | merchant {_abMerchantFound[0]} ({Pct(_abMerchantFound[0], aAttempts)})");
        log($"  [A/B] B-double:  {bAttempts} попыток | whisperOk {_abWhisperOk[1]} ({Pct(_abWhisperOk[1], bAttempts)}) | merchant {_abMerchantFound[1]} ({Pct(_abMerchantFound[1], bAttempts)})");
    }

    private async Task GoToOwnHideoutAsync(CancellationToken ct)
    {
        LogAbStats();
        await Task.Delay(200, ct);
        if (_pendingBatches > 0)
        {
            log("  → новые продавцы в очереди — пропускаем возврат в хайдаут");
            return;
        }
        log("  → очередь пуста, возврат в свой хайдаут");
        Win32Input.TypeHideoutCommand();

        // ── Двухшаговая детекция зависания ───────────────────────────────────
        if (_consecutiveFreezePattern == 1 && FreezeDetector != null && cfg.LocationRegion.HasValue)
        {
            // Первый подозрительный таймаут: ждём загрузки и проверяем OCR локации
            await Task.Delay(cfg.LocationVerifyDelayMs, ct);
            var locationText = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(cfg.LocationRegion.Value, null, ct);
            var norm = WindowsOcrTextLocator.NormalizeForMatch(locationText);
            var expected = WindowsOcrTextLocator.NormalizeForMatch(cfg.ExpectedHideoutText);
            var moved = !string.IsNullOrEmpty(expected) && norm.Contains(expected, StringComparison.Ordinal);
            log($"  [Freeze] OCR локации: «{locationText.Trim()}» → {(moved ? "переместились ✓" : "не переместились ✗")}");

            if (moved)
            {
                Interlocked.Exchange(ref _consecutiveFreezePattern, 0);
                _freezeSuspectScreenshot?.Dispose();
                _freezeSuspectScreenshot = null;
            }
            else
            {
                // Не переместились — сохраняем скриншот для сравнения на следующем таймауте
                _freezeSuspectScreenshot?.Dispose();
                _freezeSuspectScreenshot = FreezeDetector.Capture();
                log("  [Freeze] скриншот сохранён — ждём подтверждения на следующем таймауте");
            }
        }
        else if (_consecutiveFreezePattern >= 2 && FreezeDetector != null)
        {
            Interlocked.Exchange(ref _consecutiveFreezePattern, 0);

            if (_freezeSuspectScreenshot != null)
            {
                // Второй таймаут + скриншот из прошлого раза: сравниваем экраны
                log("  [Freeze] второй таймаут подряд — сравниваем с сохранённым скриншотом...");
                var frozen = FreezeDetector.IsFrozenComparedTo(_freezeSuspectScreenshot);
                _freezeSuspectScreenshot.Dispose();
                _freezeSuspectScreenshot = null;
                if (frozen)
                {
                    log("  ⚠ экран не изменился — зависание на загрузке, перезапуск...");
                    await RestartAndLoginAsync(ct);
                    return;
                }
                log("  [Freeze] экран изменился — зависание не подтверждено");
            }
            else
            {
                // LocationRegion не задана — резервное поведение: прямое сравнение пикселей
                log($"  [Freeze] {_consecutiveFreezePattern + 2} таймаутов подряд — прямая проверка экрана...");
                var frozen = await FreezeDetector.IsFrozenAsync(cfg.FreezeDetectWaitMs, ct);
                if (frozen)
                {
                    log("  ⚠ экран не изменился — игра зависла, перезапуск...");
                    await RestartAndLoginAsync(ct);
                    return;
                }
            }
        }

        if (!cfg.AutoDumpToStash || !cfg.InventoryRegion.HasValue || !cfg.PersonalStashRegion.HasValue)
            return;

        await Task.Delay(2500, ct);

        var (sx, sy) = cfg.PersonalStashRegion.Value.Center;
        Win32Input.MoveTo(sx, sy);
        await Task.Delay(150, ct);
        Win32Input.LeftClick(sx, sy);
        await Task.Delay(600, ct);

        log("  → сброс инвентаря в стэш...");
        await InventoryService.DumpToStashAsync(
            cfg.InventoryRegion.Value, inventoryState.OccupiedSnapshot, ct);
        inventoryState.Reset();
        log("  ✓ инвентарь сброшен");
    }

    private async Task RestartAndLoginAsync(CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1)
                log($"  → повтор входа (попытка {attempt}/{maxAttempts})...");

            RestartGame();
            log($"  → ждём {cfg.LoginSettings.LoginInitialWaitMs / 1000}с открытия экрана входа...");
            await Task.Delay(cfg.LoginSettings.LoginInitialWaitMs, ct);

            if (LoginService != null)
            {
                if (!Win32Input.SwitchToProcess(cfg.GameProcessName))
                    log($"  предупреждение: процесс «{cfg.GameProcessName}» не найден");
                await LoginService.LoginAsync(ct);
            }

            log($"  → ждём {cfg.LoginSettings.PostLoginWaitMs / 1000}с загрузки локации...");
            await Task.Delay(cfg.LoginSettings.PostLoginWaitMs, ct);

            if (await CheckInHideoutAsync(ct))
            {
                log("  → продолжаем работу после перезапуска");
                return;
            }

            if (attempt < maxAttempts)
                log($"  ✗ хайдаут не подтверждён — перезапускаем игру повторно (попытка {attempt + 1}/{maxAttempts})...");
        }

        log($"  ✗ хайдаут не подтверждён после {maxAttempts} попыток — продолжаем без подтверждения");
    }

    private async Task<bool> CheckInHideoutAsync(CancellationToken ct)
    {
        if (!cfg.LocationRegion.HasValue)
            return true;

        if (!Win32Input.SwitchToProcess(cfg.GameProcessName))
            log($"  предупреждение: процесс «{cfg.GameProcessName}» не найден");
        Win32Input.PressKey(0x09); // Tab
        await Task.Delay(1000, ct);
        var locationText = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(cfg.LocationRegion.Value, null, ct);
        var norm     = WindowsOcrTextLocator.NormalizeForMatch(locationText);
        var expected = WindowsOcrTextLocator.NormalizeForMatch(cfg.ExpectedHideoutText);
        var inHideout = !string.IsNullOrEmpty(expected) && norm.Contains(expected, StringComparison.Ordinal);
        log($"  [Login] OCR локации: «{locationText.Trim()}» → {(inHideout ? "✓ хайдаут подтверждён" : "✗ хайдаут не подтверждён")}");
        return inHideout;
    }

    private void RestartGame()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(cfg.GameProcessName))
            {
                p.Kill();
                p.WaitForExit(5000);
            }
            log($"  → процесс {cfg.GameProcessName} завершён");
        }
        catch (Exception ex) { log($"  ✗ не удалось завершить игру: {ex.Message}"); }

        if (!string.IsNullOrEmpty(cfg.GameExePath) && System.IO.File.Exists(cfg.GameExePath))
        {
            try
            {
                System.Diagnostics.Process.Start(cfg.GameExePath);
                log($"  → игра запускается: {cfg.GameExePath}");
            }
            catch (Exception ex) { log($"  ✗ не удалось запустить игру: {ex.Message}"); }
        }
        else
        {
            log("  ⚠ GameExePath не задан — перезапуск вручную");
        }
    }
}
