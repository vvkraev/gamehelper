using System.Text.Json;
using System.Threading.Channels;
using TradeBot.Browser;
using TradeBot.Config;
using TradeBot.Native;
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

    public async Task RunAsync(CancellationToken ct)
    {
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
            foreach (var batch in ParseAndGroupBySeller(msg, client))
            {
                Interlocked.Increment(ref _pendingBatches);
                await _channel.Writer.WriteAsync(batch, ct);
            }
        }
        // не завершаем Writer — другие продюсеры могут быть активны
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

    private async Task ProcessSellerBatchAsync(List<TradeItem> items, TcpWsClient client, CancellationToken ct)
    {
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

        var whisperCmd = JsonSerializer.Serialize(new { type = "whisper", token = first.HideoutToken });
        await client.SendTextAsync(whisperCmd, ct);
        log("  → whisper отправлен");

        await Task.Delay(800, ct);
        if (!Win32Input.SwitchToProcess(cfg.GameProcessName))
            log($"  предупреждение: процесс «{cfg.GameProcessName}» не найден");
        await Task.Delay(300, ct);

        log("  ожидание Merchant...");
        var detector = new HideoutDetector(cfg.MerchantRegion!.Value, cfg.HideoutTimeoutMs);
        if (!await detector.WaitForMerchantAsync(ct, log))
        {
            log("  ✗ таймаут ожидания хайдаута");
            return;
        }
        log($"  ✓ Merchant найден");

        if (cfg.AutoBuy && cfg.StashRegion.HasValue)
        {
            await Task.Delay(200, ct);
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
                    await Task.Delay(250, ct);
            }
            foreach (var bought in items)
                inventoryState.RecordPurchasedItem(bought.Width, bought.Height);
            log($"  ✓ куплено. [инвентарь] свободно: {inventoryState.FreeCount}/{InventoryService.Total}");
            foreach (var line in inventoryState.ToVisualLines())
                log($"    {line}");
            await Task.Delay(cfg.DelayBetweenVisitsMs, ct);
        }
    }

    private async Task GoToOwnHideoutAsync(CancellationToken ct)
    {
        log("  → очередь пуста, возврат в свой хайдаут");
        await Task.Delay(500, ct);
        Win32Input.TypeHideoutCommand();

        if (!cfg.AutoDumpToStash || !cfg.InventoryRegion.HasValue || !cfg.PersonalStashRegion.HasValue)
            return;

        await Task.Delay(3000, ct);

        var (sx, sy) = cfg.PersonalStashRegion.Value.Center;
        Win32Input.MoveTo(sx, sy);
        await Task.Delay(200, ct);
        Win32Input.LeftClick(sx, sy);
        await Task.Delay(800, ct);

        log("  → сброс инвентаря в стэш...");
        await InventoryService.DumpToStashAsync(
            cfg.InventoryRegion.Value, inventoryState.OccupiedSnapshot, ct);
        inventoryState.Reset();
        log("  ✓ инвентарь сброшен");
    }
}
