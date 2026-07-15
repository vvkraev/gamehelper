using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TradeBot.Browser;
using TradeBot.Config;
using TradeBot.Services;
using TradeBot.Trade;

namespace TradeBot;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int HotkeyId = 0x3001;
    private const uint VK_F5 = 0x74;
    private const int WM_HOTKEY = 0x0312;

    private TradeBotSettings _settings;
    private ChromeConnector? _chrome;
    private CancellationTokenSource? _cts;
    private readonly InventoryState _inventoryState = new();
    private StreamWriter? _logWriter;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        ApplySettingsToUi();
        Loaded += OnLoaded;
        var logPath = Path.Combine(AppContext.BaseDirectory, "tradebot.log");
        _logWriter = new StreamWriter(logPath, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
        _logWriter.WriteLine($"──── сессия {DateTime.Now:yyyy-MM-dd HH:mm:ss} ────");
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
            var ok = RegisterHotKey(hwnd, HotkeyId, 0, VK_F5);
            Log($"[F5] RegisterHotKey: {(ok ? "ok" : "failed — F5 уже занят другим приложением")}");
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.ScanInventoryOnStartup && _settings.InventoryRegion.HasValue)
        {
            Log("Автоскан инвентаря при запуске (убедись что игра в фокусе и инвентарь открыт)...");
            await Task.Delay(2000); // небольшая задержка чтобы пользователь успел переключиться
            await RunInventoryScanAsync();
        }
    }

    // ── Настройки ────────────────────────────────────────────────────────────

    private void ApplySettingsToUi()
    {
        StashColsBox.Text = _settings.StashCols.ToString();
        StashRowsBox.Text = _settings.StashRows.ToString();
        MaxItemsBox.Text = _settings.MaxItems.ToString();
        DelayBox.Text = _settings.DelayBetweenVisitsMs.ToString();
        TimeoutBox.Text = (_settings.HideoutTimeoutMs / 1000).ToString();
        AutoBuyCheck.IsChecked = _settings.AutoBuy;
        ProcessNameBox.Text = _settings.GameProcessName;
        InventoryCheckBox.IsChecked = _settings.InventoryCheckEnabled;
        InventoryMinSlotsBox.Text = _settings.InventoryMinFreeSlots.ToString();
        AutoDumpBox.IsChecked = _settings.AutoDumpToStash;
        ScanOnStartupBox.IsChecked = _settings.ScanInventoryOnStartup;
        CloseOnInventoryFullBox.IsChecked = _settings.CloseOnInventoryFull;
        UpdateStashLabel();
        UpdateMerchantLabel();
        UpdateInventoryLabel();
    }

    private void SaveSettingsFromUi()
    {
        _settings.StashCols = int.TryParse(StashColsBox.Text, out var c) ? c : 12;
        _settings.StashRows = int.TryParse(StashRowsBox.Text, out var r) ? r : 12;
        _settings.MaxItems = int.TryParse(MaxItemsBox.Text, out var m) ? m : 3;
        _settings.DelayBetweenVisitsMs = int.TryParse(DelayBox.Text, out var d) ? d : 1500;
        _settings.HideoutTimeoutMs = (int.TryParse(TimeoutBox.Text, out var t) ? t : 30) * 1000;
        _settings.AutoBuy = AutoBuyCheck.IsChecked == true;
        _settings.GameProcessName = ProcessNameBox.Text.Trim();
        _settings.InventoryCheckEnabled = InventoryCheckBox.IsChecked == true;
        _settings.InventoryMinFreeSlots = int.TryParse(InventoryMinSlotsBox.Text, out var ms) ? ms : 10;
        _settings.AutoDumpToStash = AutoDumpBox.IsChecked == true;
        _settings.ScanInventoryOnStartup = ScanOnStartupBox.IsChecked == true;
        _settings.CloseOnInventoryFull = CloseOnInventoryFullBox.IsChecked == true;
        SettingsStore.Save(_settings);
    }

    // ── Пикеры ───────────────────────────────────────────────────────────────

    private void PickStash_Click(object sender, RoutedEventArgs e)
    {
        var cols = int.TryParse(StashColsBox.Text, out var c) ? c : 12;
        var rows = int.TryParse(StashRowsBox.Text, out var r) ? r : 12;
        var picker = new RegionPickerWindow(cols, rows);
        if (picker.ShowDialog() == true && picker.SelectedRegion.HasValue)
        {
            _settings.StashRegion = picker.SelectedRegion.Value;
            _settings.StashCols = cols;
            _settings.StashRows = rows;
            SettingsStore.Save(_settings);
            UpdateStashLabel();
        }
    }

    private void PickMerchant_Click(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow();
        if (picker.ShowDialog() == true && picker.SelectedRegion.HasValue)
        {
            _settings.MerchantRegion = picker.SelectedRegion.Value;
            SettingsStore.Save(_settings);
            UpdateMerchantLabel();
        }
    }

    private async void TestOcr_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.MerchantRegion == null)
        {
            Log("Сначала задайте область Merchant.");
            return;
        }
        Log("OCR тест...");
        var text = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(_settings.MerchantRegion.Value, null, CancellationToken.None);
        Log($"OCR результат: «{text}»  (норм: «{WindowsOcrTextLocator.NormalizeForMatch(text)}»)");
        var found = WindowsOcrTextLocator.NormalizeForMatch(text).Contains("MERCHANT", StringComparison.Ordinal);
        Log(found ? "✓ MERCHANT найден" : "✗ MERCHANT не найден");
    }

    private void PickInventory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow(InventoryService.Cols, InventoryService.Rows);
        if (picker.ShowDialog() == true && picker.SelectedRegion.HasValue)
        {
            _settings.InventoryRegion = picker.SelectedRegion.Value;
            SettingsStore.Save(_settings);
            UpdateInventoryLabel();
        }
    }

    private void PickPersonalStash_Click(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow();
        if (picker.ShowDialog() == true && picker.SelectedRegion.HasValue)
        {
            _settings.PersonalStashRegion = picker.SelectedRegion.Value;
            SettingsStore.Save(_settings);
            UpdateInventoryLabel();
        }
    }

    private async void ScanInventory_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.InventoryRegion == null) { Log("Сначала задайте область инвентаря."); return; }
        Log("Сканирование инвентаря (убедись что игра в фокусе и инвентарь открыт)...");
        await RunInventoryScanAsync();
    }

    private async Task RunInventoryScanAsync()
    {
        if (_settings.InventoryRegion == null) return;
        if (!string.IsNullOrEmpty(_settings.GameProcessName))
        {
            if (!Win32Input.SwitchToProcess(_settings.GameProcessName))
                Log($"  предупреждение: процесс «{_settings.GameProcessName}» не найден");
            await Task.Delay(500); // ждём получения фокуса игрой
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await _inventoryState.ScanAsync(_settings.InventoryRegion.Value,
            msg => Dispatcher.Invoke(() => Log(msg)), cts.Token);
        var free = _inventoryState.FreeCount;
        Log($"Инвентарь: {free}/{InventoryService.Total} свободно, {InventoryService.Total - free} занято");
        foreach (var line in _inventoryState.ToVisualLines())
            Log($"  {line}");
    }

    private async void DumpInventory_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.InventoryRegion == null) { Log("Сначала задайте область инвентаря."); return; }
        if (!_inventoryState.IsScanned) { Log("Сначала просканируй инвентарь."); return; }
        Log("Сброс инвентаря в стэш (стэш должен быть открыт)...");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await InventoryService.DumpToStashAsync(
                _settings.InventoryRegion.Value, _inventoryState.OccupiedSnapshot, cts.Token);
            _inventoryState.Reset();
            Log("✓ Готово");
        }
        catch (Exception ex) { Log($"Ошибка: {ex.Message}"); }
    }

    private void UpdateStashLabel() =>
        StashRegionLabel.Text = _settings.StashRegion.HasValue
            ? $"{_settings.StashRegion.Value}  ({_settings.StashCols}×{_settings.StashRows} ячеек)"
            : "Область не задана";

    private void UpdateMerchantLabel() =>
        MerchantRegionLabel.Text = _settings.MerchantRegion.HasValue
            ? _settings.MerchantRegion.Value.ToString()
            : "Область не задана";

    private void UpdateInventoryLabel() =>
        InventoryRegionLabel.Text =
            $"Инвентарь: {(_settings.InventoryRegion.HasValue ? _settings.InventoryRegion.Value.ToString() : "не задан")}  |  " +
            $"Личный стэш: {(_settings.PersonalStashRegion.HasValue ? _settings.PersonalStashRegion.Value.ToString() : "не задан")}";

    // ── Переключение режима ───────────────────────────────────────────────────

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (StartBtn == null) return; // до инициализации XAML
        if (ModeLive.IsChecked == true)
            StartBtn.IsEnabled = true;  // Live не требует Chrome
        else
            StartBtn.IsEnabled = _chrome != null;
    }

    // ── Подключение ──────────────────────────────────────────────────────────

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Подключение...", "#FFA500");
        ConnectBtn.IsEnabled = false;
        try
        {
            await (_chrome?.DisposeAsync() ?? ValueTask.CompletedTask);
            _chrome = new ChromeConnector();
            await _chrome.ConnectAsync(_settings.ChromeDebugPort);
            SetStatus($"Подключён  ·  {_chrome.League}  ·  {_chrome.SearchId}", "#44ff88");
            StartBtn.IsEnabled = true;
            Log($"Chrome: {_chrome.League} / {_chrome.SearchId}");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка подключения", "#ff4444");
            Log($"Ошибка: {ex.Message}");
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
        }
    }

    // ── Запуск / Остановка ───────────────────────────────────────────────────

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();

        if (_settings.MerchantRegion == null)
        {
            Log("Задайте область «Merchant» перед запуском.");
            return;
        }

        if (ModeLive.IsChecked != true && _chrome == null)
        {
            Log("Для Bulk-режима сначала подключитесь к Chrome (кнопка «Подключить»).");
            return;
        }

        _cts = new CancellationTokenSource();
        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;

        try
        {
            if (ModeLive.IsChecked == true)
                await RunLiveAsync(_settings, _cts.Token);
            else
                await RunBulkAsync(_chrome!, _settings, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Остановлено.");
        }
        catch (Exception ex)
        {
            Log($"Ошибка: {ex.Message}");
        }
        finally
        {
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ── Основной цикл ────────────────────────────────────────────────────────

    private async Task RunBulkAsync(ChromeConnector chrome, TradeBotSettings cfg, CancellationToken ct)
    {
        var api = new TradeApiClient(chrome.TradePage);
        var detector = new HideoutDetector(_settings.MerchantRegion!.Value, cfg.HideoutTimeoutMs);

        Log($"Поиск результатов (макс. {cfg.MaxItems})...");
        var ids = await api.GetSearchResultIdsAsync(chrome.League, chrome.SearchId, cfg.MaxItems);
        Log($"Найдено: {ids.Count}");

        var n = 0;
        await foreach (var item in api.FetchItemsAsync(ids))
        {
            ct.ThrowIfCancellationRequested();
            n++;
            Log($"[{n}] {item.SellerAccount}  {item.Price} {item.Currency}  stash={item.StashName} ({item.StashX},{item.StashY})");

            if (string.IsNullOrEmpty(item.HideoutToken))
            {
                Log("  пропуск: нет hideout_token");
                continue;
            }

            // Убираем мышь перед телепортом
            Win32Input.MoveTo(10, 110);
            Log("  → мышь убрана из области торговли (10,110)");

            // Whisper → телепорт
            var (ok, whisperResp) = await api.VisitHideoutAsync(item.HideoutToken);
            Log($"  [whisper] ответ: {whisperResp ?? "(null)"}");
            if (!ok) { Log("  ✗ whisper не удался"); continue; }
            Log("  → whisper отправлен, переключаюсь в игру...");

            await Task.Delay(800, ct);
            if (!Win32Input.SwitchToProcess(cfg.GameProcessName))
                Log($"  предупреждение: процесс «{cfg.GameProcessName}» не найден");
            await Task.Delay(300, ct); // ждём перерисовки кадра без тултипа

            // Ждём загрузки хайдаута
            Log("  ожидание Merchant...");
            var loaded = await detector.WaitForMerchantAsync(ct);
            if (!loaded)
            {
                Log("  ✗ таймаут ожидания хайдаута");
                continue;
            }
            Log("  ✓ Merchant найден");

            // Ctrl+ЛКМ по ячейке стеша (только если включена автопокупка)
            if (cfg.AutoBuy && cfg.StashRegion.HasValue)
            {
                var cell = GetStashCell(cfg.StashRegion.Value, cfg.StashCols, cfg.StashRows, item.StashX, item.StashY);
                var (cx, cy) = cell.Center;
                Log($"  Ctrl+ЛКМ → ячейка ({item.StashX},{item.StashY}) экран ({cx},{cy})");
                await Task.Delay(250, ct);
                Win32Input.CtrlClickLeft(cx, cy);
                Log("  ✓ куплено");
                await Task.Delay(cfg.DelayBetweenVisitsMs, ct);
            }
            else if (cfg.AutoBuy)
            {
                Log("  (стеш не задан — пропуск клика)");
            }
        }

        Log($"Готово. Обработано: {n}");
    }

    private async Task RunLiveAsync(TradeBotSettings cfg, CancellationToken ct)
    {
        Action? onInventoryFull = cfg.CloseOnInventoryFull ? ShutdownOnInventoryFull : null;
        var mode = new Modes.LiveSearchMode(cfg, _inventoryState, msg => Dispatcher.Invoke(() => Log(msg)), onInventoryFull);
        await mode.RunAsync(ct);
    }

    private void ShutdownOnInventoryFull()
    {
        Dispatcher.Invoke(() =>
        {
            Log("  → инвентарь заполнен: завершение игры и бота...");
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(_settings.GameProcessName))
            {
                try { p.Kill(); } catch { }
            }
            _cts?.Cancel();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private static ScreenRect GetStashCell(ScreenRect stashRegion, int cols, int rows, int stashX, int stashY)
    {
        var col = Math.Clamp(stashX, 0, cols - 1);
        var row = Math.Clamp(stashY, 0, rows - 1);
        var cells = ScreenRect.SplitIntoGrid(stashRegion, cols, rows);
        // SplitIntoGrid: outer loop = cols, inner = rows → index = col * rows + row
        return cells[col * rows + row];
    }

    private void DetectProcess_Click(object sender, RoutedEventArgs e)
    {
        var names = Win32Input.GetWindowedProcessNames();
        Log("Процессы с окном: " + string.Join(", ", names));
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private void SetStatus(string text, string hexColor)
    {
        StatusText.Text = text;
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
        StatusDot.Fill = new SolidColorBrush(color);
    }

    private void Log(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var text = $"[{ts}] {line}\n";
        _logWriter?.WriteLine($"[{ts}] {line}");
        if (Dispatcher.CheckAccess())
        {
            LogBox.AppendText(text);
            LogBox.ScrollToEnd();
        }
        else
        {
            Dispatcher.InvokeAsync(() => { LogBox.AppendText(text); LogBox.ScrollToEnd(); });
        }
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        _cts?.Cancel();
        if (_chrome != null) await _chrome.DisposeAsync();
        _logWriter?.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(_settings.GameProcessName))
                {
                    var ok = Win32Input.SwitchToProcess(_settings.GameProcessName);
                    Log($"[F5] SwitchToProcess: {(ok ? "ok" : "not found")}");
                    if (ok) Thread.Sleep(200);
                }
                Win32Input.TypeHideoutCommand();
                Log("[F5] команда отправлена");
            });
        }
        return IntPtr.Zero;
    }
}
