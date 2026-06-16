using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using GameHelper.Native;
using GameHelper.Services;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace GameHelper;

public partial class MainWindow : Window
{
    private readonly ChaosCraftService _craft = new();
    private readonly AugAnnulCraftService _augAnnulCraft = new();
    private readonly ExaltationCraftServiceFracturedSide _exaltCraft;
    private readonly SharpenService _sharpen = new();
    private readonly OmenActivationService _omen = new();
    private CancellationTokenSource? _cts;
    private readonly ReforgeState _reforgeState = new();

    private ScreenRect? _currencyInventoryRegion;
    private ScreenRect? _ritualInventoryRegion;
    private Dictionary<string, ScreenRect> _ritualItemRegions = new();
    private List<ScreenRect> _itemCellRegions = new();
    private List<ScreenRect> _omenSinistralCells = new();
    private List<ScreenRect> _omenDextralCells = new();
    private List<ScreenRect> _omenGreaterCells = new();
    private ScreenRect? _traderNameOcrRegion;
    private ScreenRect? _marketRatioIHaveRect;
    private ScreenRect? _marketRatioIWantRect;
    private ScreenRect? _marketRatioPickerListRect;
    private ScreenRect? _marketRatioRateReadoutRect;
    private ScreenRect? _marketRatioGoldFeeReadoutRect;
    private ScreenRect? _marketRatioDepthHoverRect;
    private ScreenRect? _marketRatioOrderBookOcrRect;
    private List<ScreenRect> _marketRatioBothAvailableCells = new();
    private List<ScreenRect> _marketRatioBothCompetingCells = new();
    private List<ScreenRect> _marketRatioAvailableOnlyCells = new();
    private List<ScreenRect> _marketRatioCompetingOnlyCells = new();
    private bool _exchangeRateScanBusy;
    private bool _goldFeeLibraryScanBusy;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private List<AffixLibraryEntry> _affixEntries = new();
    private CraftConditionPlan _craftPlan = new();
    private HwndSource? _hwndSource;
    private bool _craftCancelHotkeyRegistered;
    private bool _trayToggleHotkeyRegistered;
    private int _trayToggleVirtualKey;
    private int _trayToggleModifiers;
    private bool _openLogHotkeyRegistered;
    private int _openLogVirtualKey;
    private int _openLogModifiers;
    private bool _craftStartStopHotkeyRegistered;
    private int _craftStartStopVirtualKey;
    private int _craftStartStopModifiers;
    private bool _reforgeStartStopHotkeyRegistered;
    private int _reforgeStartStopVirtualKey;
    private int _reforgeStartStopModifiers;
    private bool _autoReforgeStartStopHotkeyRegistered;
    private int _autoReforgeStartStopVirtualKey;
    private int _autoReforgeStartStopModifiers;
    private bool _networthStartStopHotkeyRegistered;
    private int _networthStartStopVirtualKey;
    private int _networthStartStopModifiers;
    private CancellationTokenSource? _rfCts;
    private CancellationTokenSource? _rfScanCts;
    private CancellationTokenSource? _autoRfCts;
    private readonly Services.ReforgeService _rfService = new();
    private readonly Services.AutoReforgeService _autoRfService;
    private Dictionary<string, ScreenRect> _currencyItemRegions = new();
    private Dictionary<string, ScreenRect> _breachCatalystRegions = new();
    private ScreenRect _breachInventoryRect;
    private Dictionary<string, ScreenRect> _deliriumItemRegions = new();
    private ScreenRect _deliriumInventoryRect;
    private Dictionary<string, ScreenRect> _socketableItemRegions = new();
    private ScreenRect _socketableInventoryRect;
    private CancellationTokenSource? _nwScanCts;
    private List<ScreenRect> _repricingCells = new();
    private CancellationTokenSource? _repricingCts;
    private readonly Services.RepricingService _repricingService = new();
    private bool _repricingStartStopHotkeyRegistered;
    private int _repricingStartStopVirtualKey;
    private int _repricingStartStopModifiers;
    private ScreenRect _stashOcrSearchRect;
    private ScreenRect _reforgingBenchOcrSearchRect;
    private List<ScreenRect> _fullInventoryCells = new();
    private Dictionary<string, int> _catalystGoldPrices = new();
    private string? _activeCraftLogPath;

    private static (bool WantPrefix, bool WantSuffix) GetWantedAffixTypes(CraftConditionPlan plan)
    {
        var wantPrefix = false;
        var wantSuffix = false;

        void Mark(string affixType)
        {
            var t = (affixType ?? "").Trim();
            if (string.Equals(t, "Prefix Modifier", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Desecrated Prefix Modifier", StringComparison.OrdinalIgnoreCase))
                wantPrefix = true;
            if (string.Equals(t, "Suffix Modifier", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Desecrated Suffix Modifier", StringComparison.OrdinalIgnoreCase))
                wantSuffix = true;
        }

        foreach (var or in plan.OrAlternatives)
        foreach (var c in or.Clauses)
        {
            switch (c.Kind)
            {
                case CraftClauseKind.Single:
                    if (c.Single != null) Mark(c.Single.AffixType);
                    break;
                case CraftClauseKind.Sum:
                    if (c.Sum?.Parts != null)
                        foreach (var p in c.Sum.Parts)
                            Mark(p.AffixType);
                    break;
                case CraftClauseKind.Count:
                    if (c.Count?.Members != null)
                        foreach (var m in c.Count.Members)
                            Mark(m.AffixType);
                    break;
                case CraftClauseKind.WholeModifier:
                    if (c.Whole != null)
                        Mark(c.Whole.AffixType);
                    break;
            }
        }

        return (wantPrefix, wantSuffix);
    }

    public MainWindow()
    {
        InitializeComponent();
        _autoRfService = new Services.AutoReforgeService(_rfService);
        _exaltCraft = new ExaltationCraftServiceFracturedSide(_omen);
        WindowGeometryStore.Attach(this, "MainWindow");
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        Closed += MainWindow_OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkey.WmHotkey)
        {
            var id = wParam.ToInt32();
            if (id == GlobalHotkey.CraftCancelHotkeyId ||
                id == GlobalHotkey.CraftCancelHotkeyIdShift ||
                id == GlobalHotkey.CraftCancelHotkeyIdCtrl ||
                id == GlobalHotkey.CraftCancelHotkeyIdAlt ||
                id == GlobalHotkey.CraftCancelHotkeyIdCtrlShift ||
                id == GlobalHotkey.CraftCancelHotkeyIdAltShift)
            {
                handled = true;
                Dispatcher.BeginInvoke(() =>
                {
                    if (StopBtn.IsEnabled)
                        RequestCraftCancel();
                });
            }

            if (id >= GlobalHotkey.TrayToggleHotkeyIdBase && id < GlobalHotkey.TrayToggleHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(ToggleTray);
            }

            if (id >= GlobalHotkey.OpenLogHotkeyIdBase && id < GlobalHotkey.OpenLogHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(OpenCraftLog);
            }

            if (id >= GlobalHotkey.CraftStartStopHotkeyIdBase && id < GlobalHotkey.CraftStartStopHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(ToggleCraftStartStop);
            }

            if (id >= GlobalHotkey.ReforgeStartStopHotkeyIdBase && id < GlobalHotkey.ReforgeStartStopHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(RfToggleStartStop);
            }

            if (id >= GlobalHotkey.AutoReforgeStartStopHotkeyIdBase && id < GlobalHotkey.AutoReforgeStartStopHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(RfAutoToggleStartStop);
            }

            if (id >= GlobalHotkey.NetworthStartStopHotkeyIdBase && id < GlobalHotkey.NetworthStartStopHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(NetworthToggleStartStop);
            }

            if (id >= GlobalHotkey.RepricingStartStopHotkeyIdBase && id < GlobalHotkey.RepricingStartStopHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(RepricingToggleStartStop);
            }
        }

        return IntPtr.Zero;
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        UnregisterCraftCancelHotkey();
        UnregisterTrayToggleHotkey();
        UnregisterOpenLogHotkey();
        UnregisterCraftStartStopHotkey();
        UnregisterReforgeStartStopHotkey();
        UnregisterAutoReforgeStartStopHotkey();
        _rfCts?.Cancel();
        _rfScanCts?.Cancel();
        _autoRfCts?.Cancel();
        SaveSettings();
        DisposeTrayIcon();
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        SessionLogger.NewLine -= OnSessionNewLine;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupTrayIcon();
        ApplySettings();
        KillPoeAfterCraftCheckBox.IsChecked = false; // не сохраняется между запусками

        foreach (var line in SessionLogger.GetSnapshot())
            WriteUiOnly(line);

        SessionLogger.NewLine += OnSessionNewLine;
        SessionLogger.Info("Главное окно открыто.");
        RefreshAffixLibraryIntoCombos();
        RefreshGoldFeeLibraryPathHints();
        _ = Services.AffixStatsScanner.InitializeAsync();
        _ = Services.CatalystReforgeStatsScanner.InitializeAsync();
        Services.PoeNinjaPriceService.PricesUpdated += OnPoeNinjaPricesUpdated;
        Services.PoeNinjaPriceService.LoadFromFile();

        var lastSnapshot = Services.NetworthSnapshotStore.LoadLatest();
        if (lastSnapshot is { Groups.Count: > 0 })
            NwDisplayResults(lastSnapshot.Groups, lastSnapshot.ScannedAt);
    }

    private void SetupTrayIcon()
    {
        if (_trayIcon != null)
            return;

        var icoPath = System.IO.Path.Combine(GameHelper.ProjectPaths.GetProjectRoot(), "app.ico");
        var trayIconImage = System.IO.File.Exists(icoPath)
            ? new System.Drawing.Icon(icoPath, 16, 16)
            : System.Drawing.SystemIcons.Application;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = trayIconImage,
            Visible = false,
            Text = "GameHelper — PoE2",
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Развернуть окно", null, (_, _) => Dispatcher.BeginInvoke(RestoreFromTray));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.BeginInvoke(() => Close()));
        _trayIcon.ContextMenuStrip = menu;
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon == null)
            return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void RestoreFromTray()
    {
        if (_trayIcon != null)
            _trayIcon.Visible = false;
        WindowTopHelper.ShowTopmostWithoutActivation(this);
    }

    private void MinimizeToTray_OnClick(object sender, RoutedEventArgs e) => MinimizeToTrayOnStart();

    private void MinimizeToTrayOnStart()
    {
        if (!IsVisible) return;
        SetupTrayIcon();
        _trayIcon!.Visible = true;
        Hide();
    }

    private async void StartExchangeRateScanBtn_OnClick(object sender, RoutedEventArgs e)
    {
        if (_exchangeRateScanBusy)
        {
            MessageBox.Show(this,
                "Сканирование уже выполняется. Дождитесь завершения (уведомление в трее) или разверните окно и проверьте лог.",
                "Сбор информации",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SaveSettings();

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseMs) || mouseMs < 0)
            mouseMs = 80;

        if (!int.TryParse(MarketRatioDepthOffsetXPxTextBox.Text.Trim(), out var depthOffPx))
            depthOffPx = 200;

        var traderRect = _traderNameOcrRegion;
        var npcName = string.IsNullOrWhiteSpace(TraderNpcNameTextBox.Text)
            ? "ANGE"
            : TraderNpcNameTextBox.Text.Trim();

        _exchangeRateScanBusy = true;
        StartExchangeRateScanBtn.IsEnabled = false;
        InfoCollectionStatusTextBlock.Text = "Сканирование выполняется… окно в трее.";

        SetupTrayIcon();
        _trayIcon!.Visible = true;
        Hide();

        try
        {
            await ExchangeRateInfoCollectionScan.RunAsync(
                    traderRect,
                    npcName,
                    _marketRatioIHaveRect,
                    _marketRatioIWantRect,
                    _marketRatioPickerListRect,
                    _marketRatioRateReadoutRect,
                    _marketRatioGoldFeeReadoutRect,
                    _marketRatioDepthHoverRect,
                    _marketRatioOrderBookOcrRect,
                    depthOffPx,
                    mouseMs,
                    new Progress<string>(SessionLogger.Info),
                    CancellationToken.None)
                .ConfigureAwait(true);

            InfoCollectionStatusTextBlock.Text = $"Сканирование завершено ({DateTime.Now:HH:mm:ss}). См. сессионный лог.";
            try
            {
                _trayIcon?.ShowBalloonTip(
                    5000,
                    "GameHelper",
                    "Сбор информации (курс обмена) завершён. Разверните окно из трея при необходимости.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch
            {
                // уведомления из трея могут быть отключены в системе
            }
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"[Сбор курса] Ошибка: {ex.Message}");
            InfoCollectionStatusTextBlock.Text = "Ошибка — см. лог.";
            try
            {
                _trayIcon?.ShowBalloonTip(6000, "GameHelper", "Ошибка сканирования. См. лог.", System.Windows.Forms.ToolTipIcon.Error);
            }
            catch
            {
                // tray-уведомление может быть недоступно; основная ошибка уже залогирована выше
            }
        }
        finally
        {
            _exchangeRateScanBusy = false;
            StartExchangeRateScanBtn.IsEnabled = true;
        }
    }

    private void RefreshGoldFeeLibraryPathHints()
    {
        GoldFeeScanListPathTextBlock.Text = $"Список валют: {CurrencyIWantGoldScanList.GetDefaultPath()}";
        GoldFeeLibraryPathTextBlock.Text = $"Библиотека CSV: {GoldFeeLibraryStore.GetFilePath()}";
    }

    private async void CollectIWantGoldFeeLibraryBtn_OnClick(object sender, RoutedEventArgs e)
    {
        if (_goldFeeLibraryScanBusy)
        {
            MessageBox.Show(this,
                "Сбор библиотеки золота уже выполняется.",
                "Библиотека золота I WANT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SaveSettings();
        RefreshGoldFeeLibraryPathHints();

        if (_marketRatioIWantRect is not { Width: > 0, Height: > 0 } iw ||
            _marketRatioPickerListRect is not { Width: > 0, Height: > 0 } pl ||
            _marketRatioGoldFeeReadoutRect is not { Width: > 0, Height: > 0 } goldRect)
        {
            MessageBox.Show(this,
                "Задайте области I WANT, список валют и область золота (комиссия под курсом).",
                "Библиотека золота I WANT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var listPath = CurrencyIWantGoldScanList.GetDefaultPath();
        var labels = CurrencyIWantGoldScanList.ReadLabels(listPath);
        if (labels.Count == 0)
        {
            MessageBox.Show(this,
                $"Добавьте валюты в файл (по одной на строку):\n{listPath}",
                "Библиотека золота I WANT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseMs) || mouseMs < 0)
            mouseMs = 80;

        _goldFeeLibraryScanBusy = true;
        CollectIWantGoldFeeLibraryBtn.IsEnabled = false;
        InfoCollectionStatusTextBlock.Text = "Сбор библиотеки золота I WANT…";

        try
        {
            _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(280, CancellationToken.None).ConfigureAwait(true);

            var result = await GoldFeeLibraryScanRunner.RunAsync(
                    iw,
                    pl,
                    goldRect,
                    labels,
                    mouseMs,
                    new Progress<string>(SessionLogger.Info),
                    CancellationToken.None)
                .ConfigureAwait(true);

            SessionLogger.Info(
                $"[Золото I WANT] Итог: всего={result.Total}, добавлено строк={result.Appended}, " +
                $"пропуск (пара уже есть)={result.SkippedDuplicatePair}, OCR золота не разобрал={result.OcrGoldFailed}, список/OCR валюты={result.PickerFailed}.");

            InfoCollectionStatusTextBlock.Text =
                $"Библиотека золота: добавлено {result.Appended}, пропуск дубликата {result.SkippedDuplicatePair} ({DateTime.Now:HH:mm:ss}).";

            MessageBox.Show(this,
                $"Готово.\nДобавлено новых строк: {result.Appended}\nПропущено (та же валюта+золото): {result.SkippedDuplicatePair}\n" +
                $"Не разобрано золото: {result.OcrGoldFailed}\nСбой выбора валюты: {result.PickerFailed}\n\n{GoldFeeLibraryStore.GetFilePath()}",
                "Библиотека золота I WANT",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"[Золото I WANT] Ошибка: {ex.Message}");
            InfoCollectionStatusTextBlock.Text = "Ошибка сбора библиотеки золота — см. лог.";
            MessageBox.Show(this,ex.Message, "Библиотека золота I WANT", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _goldFeeLibraryScanBusy = false;
            CollectIWantGoldFeeLibraryBtn.IsEnabled = true;
        }
    }

    private void ApplySettings()
    {
        var s = SettingsStore.Load();
        var loaded = ReforgeState.FromSettings(s);
        _reforgeState.Slot1Rect             = loaded.Slot1Rect;
        _reforgeState.Slot2Rect             = loaded.Slot2Rect;
        _reforgeState.Slot3Rect             = loaded.Slot3Rect;
        _reforgeState.ConfirmRect           = loaded.ConfirmRect;
        _reforgeState.ResultRect            = loaded.ResultRect;
        _reforgeState.PostAnimationDelayMs  = loaded.PostAnimationDelayMs;
        _reforgeState.MaxOps                = loaded.MaxOps;
        _reforgeState.SelectedCatalystIds   = loaded.SelectedCatalystIds;
        _reforgeState.ItemCells             = loaded.ItemCells;

        // Миграция из старых полей в CurrencyItemRegions
        void MigrateIfMissing(string key, ScreenRect rect)
        {
            if (rect.Width > 0 && !_currencyItemRegions.ContainsKey(key))
                _currencyItemRegions[key] = rect;
        }
        MigrateIfMissing("Chaos Orb",                  s.OrbRect);
        MigrateIfMissing("Exalted Orb",                s.ExaltRect);
        MigrateIfMissing("Perfect Orb of Augmentation", s.AugRect);
        MigrateIfMissing("Orb of Annulment",           s.AnnulRect);
        MigrateIfMissing("Blacksmith's Whetstone",     s.SharpenRect);

        _ritualItemRegions = s.RitualItemRegions != null
            ? new Dictionary<string, ScreenRect>(s.RitualItemRegions)
            : new();

        void MigrateRitual(string key, ScreenRect rect)
        {
            if (rect.Width > 0 && !_ritualItemRegions.ContainsKey(key))
                _ritualItemRegions[key] = rect;
        }
        MigrateRitual("Omen of Sinistral Exaltation", s.OmenSinistralStashRect);
        MigrateRitual("Omen of Dextral Exaltation",   s.OmenDextralStashRect);
        MigrateRitual("Omen of Greater Exaltation",   s.OmenGreaterStashRect);
        RebuildRitualItemPanel();


        if (s.CurrencyInventoryRect is { Width: > 0, Height: > 0 })
        {
            _currencyInventoryRegion = s.CurrencyInventoryRect;
            CurrencyInventoryInfo.Text = FormatRect(s.CurrencyInventoryRect);
        }

        if (s.RitualInventoryRect is { Width: > 0, Height: > 0 })
        {
            _ritualInventoryRegion = s.RitualInventoryRect;
            RitualInventoryInfo.Text = FormatRect(s.RitualInventoryRect);
        }

        if (s.TraderNameOcrSearchRect is { Width: > 0, Height: > 0 })
        {
            _traderNameOcrRegion = s.TraderNameOcrSearchRect;
            TraderNameOcrRegionInfo.Text = FormatRect(s.TraderNameOcrSearchRect);
        }

        var traderNpcName = (s.TraderNpcNameForOcr ?? "").Trim();
        if (!string.IsNullOrEmpty(traderNpcName))
            TraderNpcNameTextBox.Text = traderNpcName;

        if (s.MarketRatioIHaveClickRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioIHaveRect = s.MarketRatioIHaveClickRect;
            MarketRatioIHaveInfo.Text = FormatRect(s.MarketRatioIHaveClickRect);
        }

        if (s.MarketRatioIWantClickRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioIWantRect = s.MarketRatioIWantClickRect;
            MarketRatioIWantInfo.Text = FormatRect(s.MarketRatioIWantClickRect);
        }

        if (s.MarketRatioCurrencyPickerListRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioPickerListRect = s.MarketRatioCurrencyPickerListRect;
            MarketRatioPickerListInfo.Text = FormatRect(s.MarketRatioCurrencyPickerListRect);
        }

        if (s.MarketRatioRateReadoutRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioRateReadoutRect = s.MarketRatioRateReadoutRect;
            MarketRatioRateReadoutInfo.Text = FormatRect(s.MarketRatioRateReadoutRect);
        }

        if (s.MarketRatioGoldFeeReadoutRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioGoldFeeReadoutRect = s.MarketRatioGoldFeeReadoutRect;
            MarketRatioGoldFeeReadoutInfo.Text = FormatRect(s.MarketRatioGoldFeeReadoutRect);
        }

        if (s.MarketRatioDepthHoverRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioDepthHoverRect = s.MarketRatioDepthHoverRect;
            MarketRatioDepthHoverInfo.Text = FormatRect(s.MarketRatioDepthHoverRect);
        }

        if (s.MarketRatioOrderBookOcrRect is { Width: > 0, Height: > 0 })
        {
            _marketRatioOrderBookOcrRect = s.MarketRatioOrderBookOcrRect;
            MarketRatioOrderBookOcrInfo.Text = FormatRect(s.MarketRatioOrderBookOcrRect);
        }

        _marketRatioBothAvailableCells = s.MarketRatioOrderBookBothAvailableCells is { Count: 12 }
            ? s.MarketRatioOrderBookBothAvailableCells.ToList()
            : new List<ScreenRect>();
        _marketRatioBothCompetingCells = s.MarketRatioOrderBookBothCompetingCells is { Count: 12 }
            ? s.MarketRatioOrderBookBothCompetingCells.ToList()
            : new List<ScreenRect>();
        _marketRatioAvailableOnlyCells = s.MarketRatioOrderBookAvailableOnlyCells is { Count: 12 }
            ? s.MarketRatioOrderBookAvailableOnlyCells.ToList()
            : new List<ScreenRect>();
        _marketRatioCompetingOnlyCells = s.MarketRatioOrderBookCompetingOnlyCells is { Count: 12 }
            ? s.MarketRatioOrderBookCompetingOnlyCells.ToList()
            : new List<ScreenRect>();
        MarketRatioBothAvailableGridInfo.Text = FormatOrderBookGridSummary(_marketRatioBothAvailableCells);
        MarketRatioBothCompetingGridInfo.Text = FormatOrderBookGridSummary(_marketRatioBothCompetingCells);
        MarketRatioAvailableOnlyGridInfo.Text = FormatOrderBookGridSummary(_marketRatioAvailableOnlyCells);
        MarketRatioCompetingOnlyGridInfo.Text = FormatOrderBookGridSummary(_marketRatioCompetingOnlyCells);

        MarketRatioDepthOffsetXPxTextBox.Text = s.MarketRatioDepthHoverOffsetXPx.ToString();

        if (s.OmenSinistralCells is { Count: > 0 })
            _omenSinistralCells = s.OmenSinistralCells.ToList();
        else if (s.OmenSinistralRect is { Width: > 0, Height: > 0 })
            _omenSinistralCells = new List<ScreenRect> { s.OmenSinistralRect };
        if (_omenSinistralCells.Count > 0)
            OmenSinistralInfo.Text = FormatItemCellsSummary(_omenSinistralCells);

        if (s.OmenDextralCells is { Count: > 0 })
            _omenDextralCells = s.OmenDextralCells.ToList();
        else if (s.OmenDextralRect is { Width: > 0, Height: > 0 })
            _omenDextralCells = new List<ScreenRect> { s.OmenDextralRect };
        if (_omenDextralCells.Count > 0)
            OmenDextralInfo.Text = FormatItemCellsSummary(_omenDextralCells);

        if (s.OmenGreaterCells is { Count: > 0 })
            _omenGreaterCells = s.OmenGreaterCells.ToList();
        else if (s.OmenGreaterRect is { Width: > 0, Height: > 0 })
            _omenGreaterCells = new List<ScreenRect> { s.OmenGreaterRect };
        if (_omenGreaterCells.Count > 0)
            OmenGreaterInfo.Text = FormatItemCellsSummary(_omenGreaterCells);

        if (s.ItemCells is { Count: > 0 })
            _itemCellRegions = s.ItemCells.ToList();
        else if (s.ItemRect is { Width: > 0, Height: > 0 })
            _itemCellRegions = new List<ScreenRect> { s.ItemRect };

        if (_itemCellRegions.Count > 0)
            ItemInfo.Text = FormatItemCellsSummary(_itemCellRegions);

        MouseActionDelayMs.Text = s.MouseActionDelayMs.ToString();
        ClipboardDelayMs.Text = s.ClipboardDelayMs.ToString();
        MaxOps.Text = s.MaxOps.ToString();
        TraceInputCheckBox.IsChecked = s.TraceInput;
        StepConfirmCheckBox.IsChecked = s.StepConfirm;
        TraceExaltationSchemaCheckBox.IsChecked = s.TraceExaltationSchema;
        _craft.ClipboardDelayMs = s.ClipboardDelayMs;

        var mode = (s.CraftMode ?? "").Trim();
        if (mode.Contains("Экзаль", StringComparison.OrdinalIgnoreCase) || mode.Contains("Exalt", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 1;
        else if (mode.Contains("Ауг", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 2;
        else if (mode.Contains("Заточ", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 3;
        else
            CraftModeCombo.SelectedIndex = 0;

        _trayToggleVirtualKey = s.TrayToggleVirtualKey;
        _trayToggleModifiers = s.TrayToggleModifiers;
        UpdateTrayHotkeyDisplay();
        RegisterTrayToggleHotkey();

        _openLogVirtualKey = s.OpenLogVirtualKey;
        _openLogModifiers = s.OpenLogModifiers;
        UpdateOpenLogHotkeyDisplay();
        RegisterOpenLogHotkey();

        _craftStartStopVirtualKey = s.CraftStartStopVirtualKey;
        _craftStartStopModifiers = s.CraftStartStopModifiers;
        UpdateCraftStartStopHotkeyDisplay();
        RegisterCraftStartStopHotkey();

        _reforgeStartStopVirtualKey = s.ReforgeStartStopVirtualKey;
        _reforgeStartStopModifiers = s.ReforgeStartStopModifiers;
        UpdateReforgeStartStopHotkeyDisplay();
        RegisterReforgeStartStopHotkey();

        _autoReforgeStartStopVirtualKey = s.AutoReforgeStartStopVirtualKey;
        _autoReforgeStartStopModifiers = s.AutoReforgeStartStopModifiers;
        UpdateAutoReforgeStartStopHotkeyDisplay();
        RegisterAutoReforgeStartStopHotkey();

        _networthStartStopVirtualKey = s.NetworthStartStopVirtualKey;
        _networthStartStopModifiers  = s.NetworthStartStopModifiers;
        UpdateNetworthStartStopHotkeyDisplay();
        RegisterNetworthStartStopHotkey();

        // Загружаем реестр и обновляем UI перековки и Breach-панели
        Services.StackableItemRegistry.Load();

        _currencyItemRegions = s.CurrencyItemRegions != null
            ? new Dictionary<string, ScreenRect>(s.CurrencyItemRegions)
            : new();
        RebuildCurrencyItemPanel();

        _breachInventoryRect = s.BreachInventoryRect;
        BreachInventoryInfo.Text = FormatRect(_breachInventoryRect);
        _breachCatalystRegions = s.BreachCatalystRegions != null
            ? new Dictionary<string, ScreenRect>(s.BreachCatalystRegions)
            : new();

        _deliriumInventoryRect = s.DeliriumInventoryRect;
        DeliriumInventoryInfo.Text = FormatRect(_deliriumInventoryRect);
        _deliriumItemRegions = s.DeliriumItemRegions != null
            ? new Dictionary<string, ScreenRect>(s.DeliriumItemRegions)
            : new();

        _socketableInventoryRect = s.SocketableInventoryRect;
        SocketableInventoryInfo.Text = FormatRect(_socketableInventoryRect);
        _socketableItemRegions = s.SocketableItemRegions != null
            ? new Dictionary<string, ScreenRect>(s.SocketableItemRegions)
            : new();

        _fullInventoryCells = s.FullInventoryCells is { Count: > 0 } fic ? fic.ToList() : new();
        FullInventoryGridInfo.Text = _fullInventoryCells.Count > 0 ? $"{_fullInventoryCells.Count} ячеек" : "не задана";

        _stashOcrSearchRect = s.StashOcrSearchRect;
        StashOcrSearchInfo.Text = FormatRect(_stashOcrSearchRect);
        StashOcrTextBox.Text = string.IsNullOrWhiteSpace(s.StashOcrText) ? "STASH" : s.StashOcrText;
        _reforgingBenchOcrSearchRect = s.ReforgingBenchOcrSearchRect;
        ReforgingBenchOcrSearchInfo.Text = FormatRect(_reforgingBenchOcrSearchRect);
        // Миграция: старое "Reforging Bench" → "Reforging" (OCR читает "Bench" как кириллицу)
        var benchOcrSetting = s.ReforgingBenchOcrText;
        if (string.IsNullOrWhiteSpace(benchOcrSetting) || benchOcrSetting.Equals("Reforging Bench", StringComparison.OrdinalIgnoreCase))
            benchOcrSetting = "Reforging";
        ReforgingBenchOcrTextBox.Text = benchOcrSetting;
        RfStashOpenDelayBox.Text       = s.StashOpenDelayMs.ToString();
        RfBenchOpenDelayBox.Text       = s.ReforgingBenchOpenDelayMs.ToString();
        RfStashItemsPerClickBox.Text   = s.AutoReforgeStashItemsPerClick.ToString();
        RfItemTransferDelayBox.Text    = s.AutoReforgeItemTransferDelayMs.ToString();
        RfCascadeCheckBox.IsChecked    = s.ReforgeCascadeEnabled;
        RfCascadeThresholdBox.Text     = s.ReforgeCascadeThresholdEx.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        RfCascadeMinStashBox.Text      = s.ReforgeCascadeMinStashCount.ToString();
        PoeNinjaLeagueBox.Text = string.IsNullOrWhiteSpace(s.PoeNinjaLeague) ? "Standard" : s.PoeNinjaLeague;

        RfLoadFromState();
        RebuildBreachPanel();
        RebuildDeliriumPanel();
        RebuildSocketablePanel();
        _catalystGoldPrices = s.CatalystGoldPrices != null
            ? new Dictionary<string, int>(s.CatalystGoldPrices)
            : new Dictionary<string, int>();
        RebuildProfitTable();

        _repricingCells = s.RepricingCells is { Count: > 0 } rc ? rc.ToList() : new();
        RepricingGridInfo.Text          = FormatItemCellsSummary(_repricingCells);
        RepricingPostClickDelayBox.Text    = s.RepricingPostClickDelayMs.ToString();
        RepricingHoverSettleBox.Text       = s.RepricingHoverSettleMs.ToString();
        RepricingRepeatCountBox.Text       = s.RepricingRepeatCount.ToString();
        RepricingRepeatIntervalBox.Text    = s.RepricingRepeatIntervalMinutes.ToString();

        _repricingStartStopVirtualKey = s.RepricingStartStopVirtualKey;
        _repricingStartStopModifiers  = s.RepricingStartStopModifiers;
        UpdateRepricingStartStopHotkeyDisplay();
        RegisterRepricingStartStopHotkey();
    }

    private void SaveSettings()
    {
        var uiMode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Хаос";
        var s = new AppSettings
        {
            CurrencyInventoryRect = _currencyInventoryRegion ?? default,
            RitualInventoryRect = _ritualInventoryRegion ?? default,
            RitualItemRegions = _ritualItemRegions.Count > 0
                ? new Dictionary<string, ScreenRect>(_ritualItemRegions)
                : null,
            TraderNameOcrSearchRect = _traderNameOcrRegion ?? default,
            TraderNpcNameForOcr = string.IsNullOrWhiteSpace(TraderNpcNameTextBox.Text)
                ? "ANGE"
                : TraderNpcNameTextBox.Text.Trim(),
            MarketRatioIHaveClickRect = _marketRatioIHaveRect ?? default,
            MarketRatioIWantClickRect = _marketRatioIWantRect ?? default,
            MarketRatioCurrencyPickerListRect = _marketRatioPickerListRect ?? default,
            MarketRatioRateReadoutRect = _marketRatioRateReadoutRect ?? default,
            MarketRatioGoldFeeReadoutRect = _marketRatioGoldFeeReadoutRect ?? default,
            MarketRatioDepthHoverRect = _marketRatioDepthHoverRect ?? default,
            MarketRatioOrderBookOcrRect = _marketRatioOrderBookOcrRect ?? default,
            MarketRatioDepthHoverOffsetXPx = int.TryParse(MarketRatioDepthOffsetXPxTextBox.Text.Trim(), out var dox) ? dox : 200,
            MarketRatioOrderBookBothAvailableCells = _marketRatioBothAvailableCells.Count == 12 ? _marketRatioBothAvailableCells : null,
            MarketRatioOrderBookBothCompetingCells = _marketRatioBothCompetingCells.Count == 12 ? _marketRatioBothCompetingCells : null,
            MarketRatioOrderBookAvailableOnlyCells = _marketRatioAvailableOnlyCells.Count == 12 ? _marketRatioAvailableOnlyCells : null,
            MarketRatioOrderBookCompetingOnlyCells = _marketRatioCompetingOnlyCells.Count == 12 ? _marketRatioCompetingOnlyCells : null,

            OmenSinistralRect = _omenSinistralCells.Count > 0 ? _omenSinistralCells[0] : default,
            OmenSinistralCells = _omenSinistralCells.Count > 0 ? _omenSinistralCells : null,
            OmenDextralRect = _omenDextralCells.Count > 0 ? _omenDextralCells[0] : default,
            OmenDextralCells = _omenDextralCells.Count > 0 ? _omenDextralCells : null,
            OmenGreaterRect = _omenGreaterCells.Count > 0 ? _omenGreaterCells[0] : default,
            OmenGreaterCells = _omenGreaterCells.Count > 0 ? _omenGreaterCells : null,

            ItemRect = _itemCellRegions.Count > 0 ? _itemCellRegions[0] : default,
            ItemCells = _itemCellRegions.Count > 0 ? _itemCellRegions : null,
            CraftCondition = SettingsStore.CloneCraftConditionPlan(_craftPlan),
            CraftItemClass = _craftPlan.ExpectedItemClass,
            CraftAffixType = "",
            CraftAffixStat = "",
            CraftAffixTier = 0,
            MinRollInput = "0",
            AffixPattern = CraftConditionEvaluator.FormatSummary(_craftPlan),
            MinRoll = 0,
            MouseActionDelayMs = int.TryParse(MouseActionDelayMs.Text.Trim(), out var md) ? md : 80,
            ClipboardDelayMs = int.TryParse(ClipboardDelayMs.Text.Trim(), out var cd) ? cd : 220,
            MaxOps = int.TryParse(MaxOps.Text.Trim(), out var mo) ? mo : 20,
            CraftMode = uiMode,
            TraceInput = TraceInputCheckBox.IsChecked == true,
            StepConfirm = StepConfirmCheckBox.IsChecked == true,
            TraceExaltationSchema = TraceExaltationSchemaCheckBox.IsChecked == true,
            TrayToggleVirtualKey = _trayToggleVirtualKey,
            TrayToggleModifiers = _trayToggleModifiers,
            OpenLogVirtualKey = _openLogVirtualKey,
            OpenLogModifiers = _openLogModifiers,
            CraftStartStopVirtualKey = _craftStartStopVirtualKey,
            CraftStartStopModifiers = _craftStartStopModifiers,
            ReforgeStartStopVirtualKey = _reforgeStartStopVirtualKey,
            ReforgeStartStopModifiers = _reforgeStartStopModifiers,
            AutoReforgeStartStopVirtualKey = _autoReforgeStartStopVirtualKey,
            AutoReforgeStartStopModifiers = _autoReforgeStartStopModifiers,
            NetworthStartStopVirtualKey = _networthStartStopVirtualKey,
            NetworthStartStopModifiers  = _networthStartStopModifiers,
            CurrencyItemRegions = _currencyItemRegions.Count > 0
                ? new Dictionary<string, ScreenRect>(_currencyItemRegions)
                : null,
            BreachInventoryRect = _breachInventoryRect,
            BreachCatalystRegions = _breachCatalystRegions.Count > 0
                ? new Dictionary<string, ScreenRect>(_breachCatalystRegions)
                : null,
            DeliriumInventoryRect = _deliriumInventoryRect,
            DeliriumItemRegions = _deliriumItemRegions.Count > 0
                ? new Dictionary<string, ScreenRect>(_deliriumItemRegions)
                : null,
            SocketableInventoryRect = _socketableInventoryRect,
            SocketableItemRegions = _socketableItemRegions.Count > 0
                ? new Dictionary<string, ScreenRect>(_socketableItemRegions)
                : null,
            FullInventoryCells = _fullInventoryCells.Count > 0 ? _fullInventoryCells : null,
            StashOcrSearchRect = _stashOcrSearchRect,
            StashOcrText = StashOcrTextBox.Text.Trim(),
            ReforgingBenchOcrSearchRect = _reforgingBenchOcrSearchRect,
            ReforgingBenchOcrText = ReforgingBenchOcrTextBox.Text.Trim(),
            StashOpenDelayMs = RfParseInt(RfStashOpenDelayBox.Text, 3000),
            ReforgingBenchOpenDelayMs = RfParseInt(RfBenchOpenDelayBox.Text, 3000),
            AutoReforgeStashItemsPerClick = RfParseInt(RfStashItemsPerClickBox.Text, 10),
            AutoReforgeItemTransferDelayMs = RfParseInt(RfItemTransferDelayBox.Text, 400),
            ReforgeCascadeEnabled = RfCascadeCheckBox.IsChecked == true,
            ReforgeCascadeThresholdEx = RfParseDecimal(RfCascadeThresholdBox.Text, 2.0m),
            ReforgeCascadeMinStashCount = RfParseInt(RfCascadeMinStashBox.Text, 30),
            PoeNinjaLeague = PoeNinjaLeagueBox.Text.Trim(),
            CatalystGoldPrices = _catalystGoldPrices.Count > 0
                ? new Dictionary<string, int>(_catalystGoldPrices)
                : null,
            RepricingCells = _repricingCells.Count > 0 ? new List<ScreenRect>(_repricingCells) : null,
            RepricingPostClickDelayMs      = RfParseInt(RepricingPostClickDelayBox.Text, 300),
            RepricingHoverSettleMs         = RfParseInt(RepricingHoverSettleBox.Text, 120),
            RepricingRepeatCount           = RfParseInt(RepricingRepeatCountBox.Text, 1),
            RepricingRepeatIntervalMinutes = RfParseInt(RepricingRepeatIntervalBox.Text, 5),
            RepricingStartStopVirtualKey = _repricingStartStopVirtualKey,
            RepricingStartStopModifiers  = _repricingStartStopModifiers,
        };
        _reforgeState.ApplyToSettings(s);
        SettingsStore.Save(s);
    }

    private void UpdateCraftConditionSummary()
    {
        CraftConditionSummary.Text = CraftConditionEvaluator.FormatSummary(_craftPlan);
    }


    private void CraftConditionBtn_OnClick(object sender, RoutedEventArgs e)
    {
        AffixLibrary.ReloadFromDisk();
        _affixEntries = AffixLibrary.GetEntries().ToList();
        var editCopy = SettingsStore.CloneCraftConditionPlan(_craftPlan);
        var dlg = new CraftConditionWindow(editCopy, _affixEntries, Services.AffixStatsScanner.Current) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;
        _craftPlan = editCopy;
        UpdateCraftConditionSummary();
        SaveSettings();
    }

    private void CopyCraftConditionForReport_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SettingsStore.FormatCraftConditionForClipboard(_craftPlan));
            SessionLogger.Info("Условие остановки крафта скопировано в буфер обмена (кратко + JSON).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Не удалось скопировать в буфер обмена: " + ex.Message,
                "Копирование",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearCraftCondition_OnClick(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(this,
            "Сбросить текущее условие остановки крафта?\n\nБудут очищены: класс предмета, все OR-варианты и все условия внутри них.",
            "Очистка условия",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes)
        {
            SessionLogger.Info("Очистка условия отменена.");
            return;
        }

        _craftPlan = new CraftConditionPlan();
        UpdateCraftConditionSummary();
        SaveSettings();
        SessionLogger.Info("Условие остановки крафта очищено.");
    }

    private void SaveRecipe_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = RecipeStore.RecipesDirectory;
            var suggested = RecipeStore.SanitizeRecipeName(_craftPlan.ExpectedItemClass.Length > 0 ? _craftPlan.ExpectedItemClass : "Recipe");
            if (suggested.Length == 0)
                suggested = "Recipe";

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить рецепт",
                InitialDirectory = dir,
                Filter = "Рецепты (*.json)|*.json|Все файлы (*.*)|*.*",
                FileName = suggested + ".json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) != true)
            {
                SessionLogger.Info("Сохранение рецепта отменено.");
                return;
            }

            var mode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Хаос";
            RecipeStore.SaveToFile(sfd.FileName, mode, SettingsStore.CloneCraftConditionPlan(_craftPlan));
            SessionLogger.Info($"Рецепт сохранён: {sfd.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,"Не удалось сохранить рецепт: " + ex.Message, "Рецепт", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadRecipe_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = RecipeStore.RecipesDirectory;
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Загрузить рецепт",
                InitialDirectory = dir,
                Filter = "Рецепты (*.json)|*.json|Все файлы (*.*)|*.*",
                Multiselect = false,
            };
            if (ofd.ShowDialog(this) != true)
            {
                SessionLogger.Info("Загрузка рецепта отменена.");
                return;
            }

            var loaded = RecipeStore.LoadFromFile(ofd.FileName);
            _craftPlan = SettingsStore.CloneCraftConditionPlan(loaded.Plan);
            UpdateCraftConditionSummary();
            var cm = (loaded.CraftMode ?? "").Trim();
            if (cm.Contains("Экзаль", StringComparison.OrdinalIgnoreCase) || cm.Contains("Exalt", StringComparison.OrdinalIgnoreCase))
                CraftModeCombo.SelectedIndex = 1;
            else if (cm.Contains("Ауг", StringComparison.OrdinalIgnoreCase))
                CraftModeCombo.SelectedIndex = 2;
            else if (cm.Contains("Заточ", StringComparison.OrdinalIgnoreCase))
                CraftModeCombo.SelectedIndex = 3;
            else
                CraftModeCombo.SelectedIndex = 0;
            SaveSettings();
            SessionLogger.Info($"Рецепт загружен: {ofd.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,"Не удалось загрузить рецепт: " + ex.Message, "Рецепт", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshAffixLibraryIntoCombos()
    {
        AffixLibrary.ReloadFromDisk();
        _affixEntries = AffixLibrary.GetEntries().ToList();

        var s = SettingsStore.Load();
        _craftPlan = s.CraftCondition is { } cc
            ? SettingsStore.CloneCraftConditionPlan(cc)
            : CraftConditionMigration.FromLegacy(s, _affixEntries);
        UpdateCraftConditionSummary();
    }

    private void OnSessionNewLine(string line)
    {
        Dispatcher.Invoke(() => WriteUiOnly(line));
    }

    private void WriteUiOnly(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private static string FormatRect(ScreenRect r) =>
        $"X={r.X}, Y={r.Y}, W={r.Width}, H={r.Height} (клик: случайная точка внутри)";

    private ScreenRect? GetCurrencyRect(params string[] names)
    {
        foreach (var name in names)
            if (_currencyItemRegions.TryGetValue(name, out var r) && r.Width > 0)
                return r;
        return null;
    }

    private ScreenRect? GetRitualItemRect(params string[] names)
    {
        foreach (var name in names)
            if (_ritualItemRegions.TryGetValue(name, out var r) && r.Width > 0)
                return r;
        return null;
    }

    private static string FormatItemCellsSummary(IReadOnlyList<ScreenRect> cells)
    {
        if (cells.Count == 0)
            return "не задана — нажмите кнопку ниже";
        if (cells.Count == 1)
            return FormatRect(cells[0]);
        return $"{cells.Count} ячеек; первая: {FormatRect(cells[0])}";
    }

    private static string FormatOrderBookGridSummary(IReadOnlyList<ScreenRect> cells)
    {
        if (cells.Count != 12)
            return "не задана";
        return $"12 ячеек (6×2); первая: {FormatRect(cells[0])}";
    }

    private static List<ScreenRect> SplitRegionToOrderBookGrid(ScreenRect region)
    {
        const int rows = 6;
        const int cols = 2;
        var cells = new List<ScreenRect>(rows * cols);
        var colW = region.Width / cols;
        var rowH = region.Height / rows;
        for (var r = 0; r < rows; r++)
        {
            var y = region.Y + r * rowH;
            var h = r == rows - 1 ? region.Height - rowH * (rows - 1) : rowH;
            for (var c = 0; c < cols; c++)
            {
                var x = region.X + c * colW;
                var w = c == cols - 1 ? region.Width - colW * (cols - 1) : colW;
                cells.Add(new ScreenRect(x, y, Math.Max(1, w), Math.Max(1, h)));
            }
        }

        return cells;
    }

    private void PickCurrencyInventoryBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области вкладки инвентаря Currency отменён.");
            return;
        }

        _currencyInventoryRegion = region;
        CurrencyInventoryInfo.Text = FormatRect(region);
        SessionLogger.Info($"Вкладка инвентаря Currency задана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickRitualInventoryBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области вкладки инвентаря Ritual отменён.");
            return;
        }

        _ritualInventoryRegion = region;
        RitualInventoryInfo.Text = FormatRect(region);
        SessionLogger.Info($"Вкладка инвентаря Ritual задана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickTraderNameOcrRegionBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области имени NPC (OCR) отменён.");
            return;
        }

        _traderNameOcrRegion = region;
        TraderNameOcrRegionInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область OCR имени NPC задана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private async void TraderNpcOpenTradeOcrBtn_OnClick(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (_traderNameOcrRegion is not { Width: > 0, Height: > 0 } region)
        {
            MessageBox.Show(this,
                "Сначала задайте область поиска имени NPC (кнопка выше).",
                "Торговец OCR",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var npcName = string.IsNullOrWhiteSpace(TraderNpcNameTextBox.Text)
            ? "ANGE"
            : TraderNpcNameTextBox.Text.Trim();

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseMs) || mouseMs < 0)
            mouseMs = 80;

        TraderNpcOpenTradeOcrBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(msg => SessionLogger.Info(msg));
            var ok = await TraderNpcNameOpenTradeAction.RunAsync(region, npcName, mouseMs, progress, CancellationToken.None)
                .ConfigureAwait(true);
            if (!ok)
            {
                MessageBox.Show(this,
                    "Имя не найдено в области или ошибка ввода. Проверьте область, текст имени, язык OCR в Windows и что игра не перекрыта окном GameHelper.",
                    "Торговец OCR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            SessionLogger.Info("Торговец OCR: отмена.");
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"Торговец OCR: ошибка — {ex.Message}");
            MessageBox.Show(this,ex.Message, "Торговец OCR", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TraderNpcOpenTradeOcrBtn.IsEnabled = true;
        }
    }

    private void PickMarketRatioIHaveBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Market Ratio I HAVE отменён.");
            return;
        }

        _marketRatioIHaveRect = region;
        MarketRatioIHaveInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio I HAVE: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioIWantBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Market Ratio I WANT отменён.");
            return;
        }

        _marketRatioIWantRect = region;
        MarketRatioIWantInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio I WANT: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioPickerListBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области списка валют Market Ratio отменён.");
            return;
        }

        _marketRatioPickerListRect = region;
        MarketRatioPickerListInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio список валют: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioRateReadoutBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области курса Market Ratio отменён.");
            return;
        }

        _marketRatioRateReadoutRect = region;
        MarketRatioRateReadoutInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio курс: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioGoldFeeReadoutBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области золота Market Ratio отменён.");
            return;
        }

        _marketRatioGoldFeeReadoutRect = region;
        MarketRatioGoldFeeReadoutInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio золото: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioDepthHoverBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области наведения Market Ratio (стакан) отменён.");
            return;
        }

        _marketRatioDepthHoverRect = region;
        MarketRatioDepthHoverInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio наведение для стакана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioOrderBookOcrBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области OCR стакана отменён.");
            return;
        }

        _marketRatioOrderBookOcrRect = region;
        MarketRatioOrderBookOcrInfo.Text = FormatRect(region);
        SessionLogger.Info($"Market Ratio OCR стакана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickMarketRatioBothAvailableGridBtn_OnClick(object sender, RoutedEventArgs e) =>
        PickOrderBookGrid(
            "сетка BOTH → AVAILABLE",
            "Выбор сетки стакана (both/available) отменён.",
            cells =>
            {
                _marketRatioBothAvailableCells = cells;
                MarketRatioBothAvailableGridInfo.Text = FormatOrderBookGridSummary(cells);
            });

    private void PickMarketRatioBothCompetingGridBtn_OnClick(object sender, RoutedEventArgs e) =>
        PickOrderBookGrid(
            "сетка BOTH → COMPETING",
            "Выбор сетки стакана (both/competing) отменён.",
            cells =>
            {
                _marketRatioBothCompetingCells = cells;
                MarketRatioBothCompetingGridInfo.Text = FormatOrderBookGridSummary(cells);
            });

    private void PickMarketRatioAvailableOnlyGridBtn_OnClick(object sender, RoutedEventArgs e) =>
        PickOrderBookGrid(
            "сетка AVAILABLE ONLY",
            "Выбор сетки стакана (available only) отменён.",
            cells =>
            {
                _marketRatioAvailableOnlyCells = cells;
                MarketRatioAvailableOnlyGridInfo.Text = FormatOrderBookGridSummary(cells);
            });

    private void PickMarketRatioCompetingOnlyGridBtn_OnClick(object sender, RoutedEventArgs e) =>
        PickOrderBookGrid(
            "сетка COMPETING ONLY",
            "Выбор сетки стакана (competing only) отменён.",
            cells =>
            {
                _marketRatioCompetingOnlyCells = cells;
                MarketRatioCompetingOnlyGridInfo.Text = FormatOrderBookGridSummary(cells);
            });

    private void PickOrderBookGrid(string caption, string cancelMessage, Action<List<ScreenRect>> apply)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info(cancelMessage);
            return;
        }

        var cells = SplitRegionToOrderBookGrid(region);
        apply(cells);
        SessionLogger.Info($"Market Ratio {caption}: {FormatRect(region)} → разбиение 6×2.");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private async void TestMarketRatioExaltedDivineBtn_OnClick(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        if (_marketRatioIHaveRect is not { Width: > 0, Height: > 0 } ih ||
            _marketRatioIWantRect is not { Width: > 0, Height: > 0 } iw ||
            _marketRatioPickerListRect is not { Width: > 0, Height: > 0 } pl)
        {
            MessageBox.Show(this,
                "Задайте все три области: I HAVE, I WANT и список валют (см. подсказки на вкладке).",
                "Market Ratio",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseMs) || mouseMs < 0)
            mouseMs = 80;

        if (!int.TryParse(MarketRatioDepthOffsetXPxTextBox.Text.Trim(), out var depthOffPx))
            depthOffPx = 200;

        var scanStartedUtc = DateTime.UtcNow;
        SessionLogger.Info($"Market Ratio тест: время запуска (UTC) {scanStartedUtc:O}");

        TestMarketRatioExaltedDivineBtn.IsEnabled = false;
        try
        {
            _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(280, CancellationToken.None).ConfigureAwait(true);
            var ok = await MarketRatioExaltedDivineAutomation.RunAsync(
                    ih,
                    iw,
                    pl,
                    mouseMs,
                    scanStartedUtc,
                    _marketRatioRateReadoutRect,
                    _marketRatioGoldFeeReadoutRect,
                    _marketRatioDepthHoverRect,
                    _marketRatioOrderBookOcrRect,
                    depthOffPx,
                    new Progress<string>(SessionLogger.Info),
                    CancellationToken.None)
                .ConfigureAwait(true);
            var csvNote = _marketRatioRateReadoutRect is { Width: > 0, Height: > 0 }
                          && _marketRatioGoldFeeReadoutRect is { Width: > 0, Height: > 0 }
                ? $"\n\nКурсы: {ExchangeRateCsvLog.GetFilePath()}"
                : "\n\nОбласти курса и золота не заданы — строка в CSV не записывалась.";
            if (_marketRatioDepthHoverRect is { Width: > 0, Height: > 0 }
                && _marketRatioOrderBookOcrRect is { Width: > 0, Height: > 0 })
                csvNote += $"\nСтакан: {OrderBookSnapshotCsvLog.GetSummaryPath()}";
            MessageBox.Show(this,
                ok
                    ? "Сценарий завершён. Проверьте в игре курс и золото." + csvNote
                    : "Сбой — см. сессионный лог (OCR или клики)." + csvNote,
                "Market Ratio",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            SessionLogger.Info($"Market Ratio тест: {ex.Message}");
            MessageBox.Show(this,ex.Message, "Market Ratio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestMarketRatioExaltedDivineBtn.IsEnabled = true;
        }
    }

    private void PickOmenSinistralBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true)
        {
            SessionLogger.Info("Выбор сетки (Omen of Sinistral Exaltation) отменён.");
            return;
        }

        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области (Omen of Sinistral Exaltation) отменён.");
            return;
        }

        _omenSinistralCells = picker.SelectedCells is { Count: > 0 } cells
            ? cells.ToList()
            : new List<ScreenRect> { region };

        OmenSinistralInfo.Text = FormatItemCellsSummary(_omenSinistralCells);
        SessionLogger.Info(
            $"Omen of Sinistral Exaltation: сетка {dimDlg.GridColumns}×{dimDlg.GridRows}, ячеек {_omenSinistralCells.Count}; первая — {FormatRect(_omenSinistralCells[0])}");
        SaveSettings();
    }

    private void PickOmenDextralBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true)
        {
            SessionLogger.Info("Выбор сетки (Omen Dextral) отменён.");
            return;
        }

        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области (Omen Dextral) отменён.");
            return;
        }

        _omenDextralCells = picker.SelectedCells is { Count: > 0 } cells
            ? cells.ToList()
            : new List<ScreenRect> { region };

        OmenDextralInfo.Text = FormatItemCellsSummary(_omenDextralCells);
        SessionLogger.Info(
            $"Omen of Dextral Exaltation: сетка {dimDlg.GridColumns}×{dimDlg.GridRows}, ячеек {_omenDextralCells.Count}; первая — {FormatRect(_omenDextralCells[0])}");
        SaveSettings();
    }

    private void PickOmenGreaterBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true)
        {
            SessionLogger.Info("Выбор сетки (Omen of Greater Exaltation) отменён.");
            return;
        }

        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области (Omen of Greater Exaltation) отменён.");
            return;
        }

        _omenGreaterCells = picker.SelectedCells is { Count: > 0 } cells
            ? cells.ToList()
            : new List<ScreenRect> { region };

        OmenGreaterInfo.Text = FormatItemCellsSummary(_omenGreaterCells);
        SessionLogger.Info(
            $"Omen of Greater Exaltation: сетка {dimDlg.GridColumns}×{dimDlg.GridRows}, ячеек {_omenGreaterCells.Count}; первая — {FormatRect(_omenGreaterCells[0])}");
        SaveSettings();
    }

    private void PickItemBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true)
        {
            SessionLogger.Info("Выбор сетки области предмета отменён.");
            return;
        }

        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области предмета отменён.");
            return;
        }

        if (picker.SelectedCells is { Count: > 0 } cells)
            _itemCellRegions = cells.ToList();
        else
            _itemCellRegions = new List<ScreenRect> { region };

        ItemInfo.Text = FormatItemCellsSummary(_itemCellRegions);
        SessionLogger.Info(
            $"Область предмета: сетка {dimDlg.GridColumns}×{dimDlg.GridRows}, ячеек {_itemCellRegions.Count}; первая — {FormatRect(_itemCellRegions[0])}");
        SaveSettings();
    }

    private async void ItemParsing_OnClick(object sender, RoutedEventArgs e)
    {
        if (_itemCellRegions.Count == 0)
        {
            MessageBox.Show(this,
                "Сначала задайте область предмета кнопкой «Задать область».",
                "Область предмета",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var itemRect = _itemCellRegions[0];

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseMs) || mouseMs < 0)
            mouseMs = 80;
        if (!int.TryParse(ClipboardDelayMs.Text.Trim(), out var clipMs) || clipMs < 0)
            clipMs = 220;

        try
        {
            if (!ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName))
                SessionLogger.Info("PathOfExileSteam.exe: окно не найдено или не удалось активировать — проверьте, что игра запущена.");
            await Task.Delay(120);

            var hoverSettle = Math.Clamp(mouseMs / 2, 80, 200);
            var (hx, hy) = itemRect.GetInteriorPoint(1);
            Win32Input.MoveTo(hx, hy);
            await Task.Delay(RandomizeDelay(mouseMs));
            await Task.Delay(hoverSettle);
            Win32Input.SendCtrlAltC();
            await Task.Delay(RandomizeDelay(clipMs));

            string clipboardContent;
            try
            {
                var dataObject = System.Windows.Clipboard.GetDataObject();
                clipboardContent = dataObject?.GetDataPresent(System.Windows.DataFormats.Text) == true
                    ? (string)dataObject.GetData(System.Windows.DataFormats.Text)!
                    : "";
            }
            catch (COMException)
            {
                MessageBox.Show(this,"Не удалось прочитать буфер обмена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SessionLogger.InfoClipboard("ручной парсинг предмета (Ctrl+Alt+C)", clipboardContent);

            if (string.IsNullOrWhiteSpace(clipboardContent))
            {
                MessageBox.Show(this,
                    "Буфер обмена пуст. Убедитесь, что область предмета на экране совпадает с предметом в игре (копирование Ctrl+Alt+C).",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var parsedItem = ItemParser.Parse(clipboardContent);

            if (parsedItem != null && parsedItem.IsValid)
            {
                var added = AffixLibrary.MergeFromParsedItem(parsedItem);
                SessionLogger.Info(
                    added > 0
                        ? $"Библиотека аффиксов: добавлено новых записей: {added}. Файл: {AffixLibrary.FilePath}"
                        : $"Библиотека аффиксов обновлена (без новых уникальных аффиксов). Файл: {AffixLibrary.FilePath}");

                var itemWindow = new ItemParsingWindow(parsedItem)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                itemWindow.ShowDialog();
                SessionLogger.Info("Предмет успешно распарсен.");
                LogBox.AppendText("✓ Предмет распарсен успешно.\n");
            }
            else
            {
                MessageBox.Show(this,
                    "Ошибка парсинга текста из буфера после Ctrl+Alt+C.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                SessionLogger.Info("Ошибка при парсинге предмета.");
                LogBox.AppendText("✗ Ошибка парсинга предмета.\n");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Произошла ошибка при парсинге: {ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SessionLogger.Info($"Ошибка парсинга: {ex.Message}");
            LogBox.AppendText($"✗ Ошибка: {ex.Message}\n");
        }
        finally
        {
            Win32Input.ReleaseCtrlAlt();
        }
    }

    private void ReloadAffixLibrary_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshAffixLibraryIntoCombos();
        var n = AffixLibrary.EntryCount;
        SessionLogger.Info($"Библиотека аффиксов перезагружена с диска, записей: {n}. {AffixLibrary.FilePath}");
        LogBox.AppendText($"Библиотека аффиксов: перезагрузка с диска, записей {n}.\n");
        LogBox.ScrollToEnd();
    }

    private async void StartBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var mode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Хаос";
        var isExalt = mode.Contains("Экзаль", StringComparison.OrdinalIgnoreCase) || mode.Contains("Exalt", StringComparison.OrdinalIgnoreCase);
        var isSharpen = mode.Contains("Заточ", StringComparison.OrdinalIgnoreCase);
        var isAugAnnul = mode.Contains("Ауг", StringComparison.OrdinalIgnoreCase)
                      || mode.Contains("Аннул", StringComparison.OrdinalIgnoreCase)
                      || mode.Contains("+", StringComparison.OrdinalIgnoreCase);

        if (isSharpen)
        {
            if (GetCurrencyRect("Blacksmith's Whetstone", "Armourer's Scrap", "Glassblower's Bauble") is null)
            {
                MessageBox.Show(this,"Задайте область предмета заточки (Blacksmith's Whetstone / Armourer's Scrap / Glassblower's Bauble) в «Настройки областей → Currency».", "Заточка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_itemCellRegions.Count == 0)
            {
                MessageBox.Show(this,"Задайте область предмета (ячейки) во вкладке «Крафт».", "Область предмета", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseDelayMs) || mouseDelayMs < 0)
                mouseDelayMs = 80;

            _sharpen.MouseActionDelayMs = mouseDelayMs;
            _sharpen.TraceInputToLog = TraceInputCheckBox.IsChecked == true;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            StartBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;
            TryRegisterCraftCancelHotkey();

            var sharpenLog = new Progress<string>(SessionLogger.Info);
            try
            {
                SessionLogger.Info($"--- запуск: заточка, ячеек {_itemCellRegions.Count}, кликов на ячейку 20, задержка мыши {mouseDelayMs} мс ---");
                await _sharpen.RunAsync(GetCurrencyRect("Blacksmith's Whetstone", "Armourer's Scrap", "Glassblower's Bauble")!.Value, _itemCellRegions, 20, sharpenLog, _cts.Token);
                SessionLogger.Info("Заточка завершена.");
            }
            catch (OperationCanceledException)
            {
                SessionLogger.Info("Заточка остановлена пользователем.");
            }
            catch (Exception ex)
            {
                SessionLogger.Info("Ошибка заточки: " + ex.Message);
            }
            finally
            {
                UnregisterCraftCancelHotkey();
                StartBtn.IsEnabled = true;
                StopBtn.IsEnabled = false;
                MaybeKillPoeProcessAfterCraft();
            }

            return;
        }

        // Омены не запускаются как отдельный режим — используются ExaltationCraftService как вспомогательный сервис.

        if (!isAugAnnul && !isExalt && GetCurrencyRect("Chaos Orb", "Greater Chaos Orb", "Perfect Chaos Orb") is null)
        {
            MessageBox.Show(this,"Задайте область Chaos Orb в «Настройки областей → Currency».", "Область орба", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isExalt && (GetCurrencyRect("Exalted Orb", "Greater Exalted Orb", "Perfect Exalted Orb") is null || GetCurrencyRect("Orb of Annulment") is null))
        {
            MessageBox.Show(this,
                "Задайте области Exalted Orb и Orb of Annulment в «Настройки областей → Currency».",
                "Области сфер",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (isExalt && _omenGreaterCells.Count == 0)
        {
            MessageBox.Show(this,
                "Задайте область омена Greater во вкладке «Настройки областей» (сетка X×Y).",
                "Омены",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (isExalt && (_ritualInventoryRegion is null || _currencyInventoryRegion is null || GetRitualItemRect("Omen of Greater Exaltation") is null))
        {
            MessageBox.Show(this,
                "Для крафта Orb of Exaltation задайте области: Ritual inventory, Currency inventory и Omen of Greater Exaltation (в «Настройки областей → Ritual») для автопополнения омнов.",
                "Области RefillOmen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (isExalt)
        {
            var (wantPrefix, wantSuffix) = GetWantedAffixTypes(_craftPlan);
            var prefixOnly = wantPrefix && !wantSuffix;
            var suffixOnly = wantSuffix && !wantPrefix;

            if (prefixOnly && GetRitualItemRect("Omen of Sinistral Exaltation") is null)
            {
                MessageBox.Show(this,
                    "Для условия только с префиксами задайте область Omen of Sinistral Exaltation в «Настройки областей → Ritual».",
                    "Области RefillOmen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (suffixOnly && GetRitualItemRect("Omen of Dextral Exaltation") is null)
            {
                MessageBox.Show(this,
                    "Для условия только с суффиксами задайте область Omen of Dextral Exaltation в «Настройки областей → Ritual».",
                    "Области RefillOmen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (prefixOnly && _omenSinistralCells.Count == 0)
            {
                MessageBox.Show(this,
                    "В условии крафта используются только Prefix Modifier — задайте область омена Sinistral во вкладке «Настройки областей».",
                    "Омены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (suffixOnly && _omenDextralCells.Count == 0)
            {
                MessageBox.Show(this,
                    "В условии крафта используются только Suffix Modifier — задайте область омена Dextral во вкладке «Настройки областей».",
                    "Омены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (isAugAnnul && (GetCurrencyRect("Perfect Orb of Augmentation", "Greater Orb of Augmentation", "Orb of Augmentation") is null || GetCurrencyRect("Orb of Annulment") is null))
        {
            MessageBox.Show(this,
                "Задайте области Orb of Augmentation и Orb of Annulment в «Настройки областей → Currency».",
                "Области сфер",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_itemCellRegions.Count == 0)
        {
            MessageBox.Show(this,"Задайте область предмета кнопкой «Задать область».", "Область предмета", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MaxOps.Text.Trim(), out var maxOps) || maxOps < 1)
        {
            MessageBox.Show(this,"Укажите целое N ≥ 1.", "N", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseDelay) || mouseDelay < 0)
        {
            MessageBox.Show(this,"Укажите задержку мыши (мс) — целое число ≥ 0.", "Задержка мыши", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(ClipboardDelayMs.Text.Trim(), out var clipboardDelay) || clipboardDelay < 0)
        {
            MessageBox.Show(this,"Укажите задержку после Ctrl+Alt+C (мс) — целое число ≥ 0.", "Буфер обмена", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CraftConditionPlanNormalizer.NormalizeInPlace(_craftPlan, _affixEntries);
        if (!CraftConditionEvaluator.TryValidate(_craftPlan, out var planErr))
        {
            MessageBox.Show(this,planErr, "Условие крафта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var conditionSummary = CraftConditionEvaluator.FormatSummary(_craftPlan);

        var trace = TraceInputCheckBox.IsChecked == true;
        var hoverSettle = Math.Clamp(clipboardDelay / 2, 80, 220);

        _craft.MouseActionDelayMs = mouseDelay;
        _craft.ClipboardDelayMs = clipboardDelay;
        _craft.HoverSettleBeforeClipboardMs = hoverSettle;
        _craft.TraceInputToLog = trace;
        _craft.StepConfirmAsync = null;

        _augAnnulCraft.MouseActionDelayMs = mouseDelay;
        _augAnnulCraft.ClipboardDelayMs = clipboardDelay;
        _augAnnulCraft.HoverSettleBeforeClipboardMs = hoverSettle;
        _augAnnulCraft.TraceInputToLog = trace;
        _augAnnulCraft.StepConfirmAsync = null;

        _exaltCraft.MouseActionDelayMs = mouseDelay;
        _exaltCraft.ClipboardDelayMs = clipboardDelay;
        _exaltCraft.HoverSettleBeforeClipboardMs = hoverSettle;
        _exaltCraft.TraceInputToLog = trace;
        _exaltCraft.SchemaTraceToLog = TraceExaltationSchemaCheckBox.IsChecked == true;
        _exaltCraft.StepConfirmAsync = null;

        _omen.MouseActionDelayMs = mouseDelay;
        _omen.ClipboardDelayMs = clipboardDelay;
        _omen.TraceInputToLog = trace;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var runCts = _cts;

        if (StepConfirmCheckBox.IsChecked == true)
        {
            Func<string, Task> confirm = msg =>
                Dispatcher.InvokeAsync(() =>
                    {
                        var dlg = new StepConfirmDialog(msg) { Owner = this };
                        if (dlg.ShowDialog() != true)
                            runCts.Cancel();
                    })
                    .Task;

            _craft.StepConfirmAsync = confirm;
            _augAnnulCraft.StepConfirmAsync = confirm;
            _exaltCraft.StepConfirmAsync = confirm;
        }

        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;
        TryRegisterCraftCancelHotkey();

        var progress = new Progress<string>(SessionLogger.Info);

        CraftRunFileLog? craftFile = null;
        try
        {
            var orb   = GetCurrencyRect("Chaos Orb", "Greater Chaos Orb", "Perfect Chaos Orb") ?? default;
            var exalt = GetCurrencyRect("Exalted Orb", "Greater Exalted Orb", "Perfect Exalted Orb") ?? default;
            var aug   = GetCurrencyRect("Perfect Orb of Augmentation", "Greater Orb of Augmentation", "Orb of Augmentation") ?? default;
            var annul = GetCurrencyRect("Orb of Annulment") ?? default;
            var cells = _itemCellRegions;

            SessionLogger.Info(
                $"--- запуск крафта ({mode}): ячеек предмета {cells.Count}, N={maxOps} общий на сессию, задержка мыши {mouseDelay} мс, после Ctrl+Alt+C {clipboardDelay} мс; лог крафта — в папке Log после завершения ---");

            ChaosCraftResult result = ChaosCraftResult.MaxAttemptsReached;
            var remaining = maxOps;
            var offset = 0;
            var precheckFailed = false;
            try
            {
                for (var ci = 0; ci < cells.Count; ci++)
                {
                    if (remaining < 1)
                    {
                        SessionLogger.Info(
                            $"Общий лимит N={maxOps} исчерпан ({offset} попыток уже сделано); ячейки {ci + 1}…{cells.Count} не обрабатываются.");
                        result = ChaosCraftResult.MaxAttemptsReached;
                        break;
                    }

                    var item = cells[ci];
                    SessionLogger.Info($"--- ячейка предмета {ci + 1} / {cells.Count}: {FormatRect(item)} (осталось попыток в сессии: {remaining}) ---");

                    var pre =
                        isExalt
                            ? await _exaltCraft.PrecheckAsync(item, _craftPlan, progress, _cts.Token)
                            : !isAugAnnul
                                ? await _craft.PrecheckAsync(item, _craftPlan, progress, _cts.Token)
                                : await _augAnnulCraft.PrecheckAsync(item, _craftPlan, progress, _cts.Token);
                    if (pre.Outcome == CraftPrecheckOutcome.AlreadySatisfied)
                    {
                        SessionLogger.Info(
                            $"Предпроверка: ячейка {ci + 1} — условие остановки уже выполнено, орб не тратим — переход к следующей ячейке.");
                        if (ci + 1 < cells.Count)
                            SessionLogger.Info("Следующая ячейка будет проверена так же.");
                        continue;
                    }

                    if (pre.Outcome == CraftPrecheckOutcome.EmptyCell)
                    {
                        SessionLogger.Info(
                            $"Предпроверка: ячейка {ci + 1} — буфер пуст после Ctrl+Alt+C, считаем ячейку пустой — переход к следующей ячейке.");
                        continue;
                    }

                    if (pre.Outcome == CraftPrecheckOutcome.NonMagicCell)
                    {
                        SessionLogger.Info(
                            $"Предпроверка: ячейка {ci + 1} — предмет не Magic, пропускаем ячейку (режим «Ауг+Аннул»).");
                        continue;
                    }

                    if (pre.Outcome == CraftPrecheckOutcome.Failed)
                    {
                        precheckFailed = true;
                        result = ChaosCraftResult.Error;
                        MessageBox.Show(this,
                            pre.Message,
                            string.IsNullOrEmpty(pre.Title) ? "Крафт" : pre.Title,
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        break;
                    }

                    if ((isAugAnnul || isExalt) && pre.ParsedItem is null)
                    {
                        precheckFailed = true;
                        result = ChaosCraftResult.Error;
                        MessageBox.Show(this,
                            "Внутренняя ошибка: предпроверка не вернула ParsedItem.",
                            "Крафт",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        break;
                    }

                    if (isExalt)
                    {
                        craftFile ??= CraftRunFileLog.Begin(
                            "Orb of Exaltation",
                            exalt,
                            annul,
                            "Orb of Annulment",
                            cells[0],
                            maxOps,
                            conditionSummary,
                            cells);
                        _activeCraftLogPath = craftFile.WipPath;
                        craftFile.SetCurrentCell(ci + 1, cells.Count);

                        var cr = await _exaltCraft.RunAsync(
                            exalt,
                            annul,
                            _ritualInventoryRegion ?? default,
                            _currencyInventoryRegion ?? default,
                            GetRitualItemRect("Omen of Sinistral Exaltation") ?? default,
                            GetRitualItemRect("Omen of Dextral Exaltation") ?? default,
                            GetRitualItemRect("Omen of Greater Exaltation") ?? default,
                            _omenSinistralCells,
                            _omenDextralCells,
                            _omenGreaterCells,
                            item,
                            _craftPlan,
                            conditionSummary,
                            pre.ParsedItem!,
                            pre.ClipboardText,
                            remaining,
                            maxOps,
                            offset,
                            progress,
                            _cts.Token,
                            craftFile);

                        offset += cr.Attempts;
                        remaining -= cr.Attempts;
                        result = cr.StopReason;
                    }
                    else if (!isAugAnnul)
                    {
                        craftFile ??= CraftRunFileLog.Begin(orb, cells[0], maxOps, conditionSummary, cells);
                        _activeCraftLogPath = craftFile.WipPath;
                        craftFile.SetCurrentCell(ci + 1, cells.Count);
                        var cr = await _craft.RunAsync(
                            orb,
                            item,
                            _craftPlan,
                            conditionSummary,
                            remaining,
                            maxOps,
                            offset,
                            progress,
                            _cts.Token,
                            craftFile);

                        offset += cr.Attempts;
                        remaining -= cr.Attempts;
                        result = cr.StopReason;
                    }
                    else
                    {
                        craftFile ??= CraftRunFileLog.Begin(
                            "Orb of Augmentation",
                            aug,
                            annul,
                            "Orb of Annulment",
                            cells[0],
                            maxOps,
                            conditionSummary,
                            cells);
                        _activeCraftLogPath = craftFile.WipPath;
                        craftFile.SetCurrentCell(ci + 1, cells.Count);

                        var cr = await _augAnnulCraft.RunAsync(
                            aug,
                            annul,
                            item,
                            _craftPlan,
                            conditionSummary,
                            pre.ParsedItem!,
                            pre.ClipboardText,
                            remaining,
                            maxOps,
                            offset,
                            progress,
                            _cts.Token,
                            craftFile);

                        offset += cr.Attempts;
                        remaining -= cr.Attempts;
                        result = cr.StopReason;
                    }

                    if (result == ChaosCraftResult.Cancelled || result == ChaosCraftResult.Error)
                        break;

                    if (result == ChaosCraftResult.EmptyCell)
                    {
                        SessionLogger.Info(
                            $"Крафт: ячейка {ci + 1} — буфер пуст после Ctrl+Alt+C, считаем ячейку пустой — переход к следующей ячейке (осталось попыток: {remaining}).");
                        continue;
                    }

                    if (result == ChaosCraftResult.AffixFound)
                    {
                        if (ci + 1 < cells.Count)
                            SessionLogger.Info(
                                $"Условие остановки выполнено для ячейки {ci + 1} — автоматически продолжаем со следующей предметной ячейки (осталось попыток: {remaining}).");
                        else
                            SessionLogger.Info($"Условие остановки выполнено для ячейки {ci + 1} (последняя в сетке).");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result = ChaosCraftResult.Cancelled;
            }

            if (craftFile == null && !precheckFailed && result is not ChaosCraftResult.Cancelled)
            {
                MessageBox.Show(this,
                    "Во всех выбранных ячейках условие остановки уже выполнено. Попытки (N) не расходовались.",
                    "Крафт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SessionLogger.Info("Сессия: все ячейки уже удовлетворяют условию — файл лога крафта не создавался.");
            }
            else if (craftFile != null)
            {
                craftFile.SetOutcome(result);
                SessionLogger.Info($"Итог крафта: {result}");
            }
            else if (result == ChaosCraftResult.Cancelled)
                SessionLogger.Info("Итог крафта: отмена (файл лога не создавался).");

            SaveSettings();
        }
        catch (Exception ex)
        {
            craftFile?.SetOutcome(ChaosCraftResult.Error, ex.Message);
            SessionLogger.Info("Ошибка: " + ex.Message);
        }
        finally
        {
            UnregisterCraftCancelHotkey();
            _craft.StepConfirmAsync = null;
            _exaltCraft.StepConfirmAsync = null;
            craftFile?.Dispose();
            _ = Services.AffixStatsScanner.ScanNewLogsAsync();
            _activeCraftLogPath = null;
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            MaybeKillPoeProcessAfterCraft();
        }
    }

    private void StopBtn_OnClick(object sender, RoutedEventArgs e) =>
        RequestCraftCancel();

    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape)
            return;
        if (!StopBtn.IsEnabled)
            return;
        e.Handled = true;
        RequestCraftCancel();
    }

    private void RequestCraftCancel()
    {
        _cts?.Cancel();
        SessionLogger.Info("(отмена запрошена)");
    }

    private void MaybeKillPoeProcessAfterCraft()
    {
        if (KillPoeAfterCraftCheckBox.IsChecked != true)
            return;

        try
        {
            var name = ProcessForeground.PathOfExile2SteamProcessName;
            var procs = Process.GetProcessesByName(name);
            if (procs.Length == 0)
            {
                SessionLogger.Info($"Завершение процесса: {name}.exe не найден.");
                return;
            }

            foreach (var p in procs)
            {
                try
                {
                    var pid = p.Id;
                    p.Kill(entireProcessTree: true);
                    SessionLogger.Info($"Завершение процесса: {name}.exe (pid={pid})");
                }
                catch (Exception ex)
                {
                    SessionLogger.Info($"Не удалось завершить {name}.exe: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            SessionLogger.Info("Завершение процесса игры: ошибка: " + ex.Message);
        }
    }

    private static int RandomizeDelay(int baseMs)
    {
        if (baseMs <= 0)
            return 0;
        var delta = (int)Math.Round(baseMs * 0.30);
        if (delta <= 0)
            return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private void ToggleTray()
    {
        if (IsVisible)
        {
            SetupTrayIcon();
            _trayIcon!.Visible = true;
            Hide();
        }
        else
        {
            RestoreFromTray();
        }
    }

    private void RegisterTrayToggleHotkey()
    {
        UnregisterTrayToggleHotkey();
        if (_trayToggleVirtualKey == 0)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (!GlobalHotkey.TryRegisterTrayToggle(hwnd, (uint)_trayToggleVirtualKey, (uint)_trayToggleModifiers))
            SessionLogger.Info("Горячая клавиша «Трей» не зарегистрирована — возможно, занята другим процессом.");
        else
            _trayToggleHotkeyRegistered = true;
    }

    private void UnregisterTrayToggleHotkey()
    {
        if (!_trayToggleHotkeyRegistered)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterTrayToggle(hwnd);
        _trayToggleHotkeyRegistered = false;
    }

    private void UpdateTrayHotkeyDisplay()
    {
        TrayHotkeyBox.Text = FormatHotkey(_trayToggleVirtualKey, _trayToggleModifiers);
    }

    private static string FormatHotkey(int vk, int modifiers)
    {
        if (vk == 0)
            return "(не задано)";
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(KeyInterop.KeyFromVirtualKey(vk).ToString());
        return string.Join("+", parts);
    }

    private void TrayHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            _trayToggleVirtualKey = 0;
            _trayToggleModifiers = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _trayToggleVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _trayToggleModifiers = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                 | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                 | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }

        UpdateTrayHotkeyDisplay();
        RegisterTrayToggleHotkey();
        SaveSettings();
    }

    private CraftLogWindow? _craftLogWindow;

    private void OpenLogBtn_OnClick(object sender, RoutedEventArgs e) => OpenCraftLog();

    private void OpenCraftLog()
    {
        // Уже открыто — закрываем (toggle)
        if (_craftLogWindow != null)
        {
            _craftLogWindow.Close();
            _craftLogWindow = null;
            return;
        }

        var filePath = ResolveCraftLogPath();
        if (filePath == null)
        {
            MessageBox.Show(this,
                "Файл лога крафта не найден. Запустите крафт хотя бы один раз.",
                "Лог крафта",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _craftLogWindow = new CraftLogWindow { Owner = this, ShowActivated = false };
        _craftLogWindow.Closed += (_, _) => _craftLogWindow = null;
        _craftLogWindow.LoadFile(filePath);
        _craftLogWindow.Show();
    }

    private string? ResolveCraftLogPath()
    {
        if (_activeCraftLogPath != null && File.Exists(_activeCraftLogPath))
            return _activeCraftLogPath;

        var logDir = ProjectPaths.GetLogDirectory();
        // Сначала ищем активный WIP-файл (крафт запущен, но _activeCraftLogPath ещё не выставлен)
        var wip = Directory.EnumerateFiles(logDir, "craft_*_wip.tmp")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
        if (wip != null) return wip;

        return Directory.EnumerateFiles(logDir, "craft_*.txt")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    private void RegisterOpenLogHotkey()
    {
        UnregisterOpenLogHotkey();
        if (_openLogVirtualKey == 0)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (!GlobalHotkey.TryRegisterOpenLog(hwnd, (uint)_openLogVirtualKey, (uint)_openLogModifiers))
            SessionLogger.Info("Горячая клавиша «Открыть лог» не зарегистрирована — возможно, занята другим процессом.");
        else
            _openLogHotkeyRegistered = true;
    }

    private void UnregisterOpenLogHotkey()
    {
        if (!_openLogHotkeyRegistered)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterOpenLog(hwnd);
        _openLogHotkeyRegistered = false;
    }

    private void UpdateOpenLogHotkeyDisplay()
    {
        OpenLogHotkeyBox.Text = FormatHotkey(_openLogVirtualKey, _openLogModifiers);
    }

    private void OpenLogHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            _openLogVirtualKey = 0;
            _openLogModifiers = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _openLogVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _openLogModifiers = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                              | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                              | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }

        UpdateOpenLogHotkeyDisplay();
        RegisterOpenLogHotkey();
        SaveSettings();
    }

    private void ToggleCraftStartStop()
    {
        if (StopBtn.IsEnabled)
            RequestCraftCancel();
        else if (StartBtn.IsEnabled)
            StartBtn_OnClick(StartBtn, new RoutedEventArgs());
    }

    private void RegisterCraftStartStopHotkey()
    {
        UnregisterCraftStartStopHotkey();
        if (_craftStartStopVirtualKey == 0)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        if (!GlobalHotkey.TryRegisterCraftStartStop(hwnd, (uint)_craftStartStopVirtualKey, (uint)_craftStartStopModifiers))
            SessionLogger.Info("Горячая клавиша «Старт/Стоп» не зарегистрирована — возможно, занята другим процессом.");
        else
            _craftStartStopHotkeyRegistered = true;
    }

    private void UnregisterCraftStartStopHotkey()
    {
        if (!_craftStartStopHotkeyRegistered)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterCraftStartStop(hwnd);
        _craftStartStopHotkeyRegistered = false;
    }

    private void UpdateCraftStartStopHotkeyDisplay()
    {
        CraftStartStopHotkeyBox.Text = FormatHotkey(_craftStartStopVirtualKey, _craftStartStopModifiers);
    }

    private void CraftStartStopHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                 or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            _craftStartStopVirtualKey = 0;
            _craftStartStopModifiers = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _craftStartStopVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _craftStartStopModifiers = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                     | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                     | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }

        UpdateCraftStartStopHotkeyDisplay();
        RegisterCraftStartStopHotkey();
        SaveSettings();
    }

    private void TryRegisterCraftCancelHotkey()
    {
        UnregisterCraftCancelHotkey();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            SessionLogger.Info("Esc (глобально): HWND окна ещё нет — отмена только кнопкой «Стоп».");
            return;
        }

        if (!GlobalHotkey.TryRegisterEscape(hwnd))
        {
            SessionLogger.Info(
                "Глобальная горячая клавиша Esc не зарегистрирована (возможно, занята другим процессом). Отмена — кнопка «Стоп».");
            return;
        }

        _craftCancelHotkeyRegistered = true;
        SessionLogger.Info(
            "Глобальный Esc активен: отмена крафта с любым фокусом (в т.ч. в трее), пока идёт сессия крафта.");
    }

    private void UnregisterCraftCancelHotkey()
    {
        if (!_craftCancelHotkeyRegistered)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterCraftCancel(hwnd);
        _craftCancelHotkeyRegistered = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ПЕРЕКОВКА (вкладка)
    // ═══════════════════════════════════════════════════════════════════════

    private void RfLoadFromState()
    {
        RfScanGridInfo.Text    = _reforgeState.ItemCells.Count > 0 ? $"{_reforgeState.ItemCells.Count} ячеек" : "(не задана)";
        RfSlot1Info.Text       = FormatRect(_reforgeState.Slot1Rect);
        RfSlot2Info.Text       = FormatRect(_reforgeState.Slot2Rect);
        RfSlot3Info.Text       = FormatRect(_reforgeState.Slot3Rect);
        RfConfirmInfo.Text     = FormatRect(_reforgeState.ConfirmRect);
        RfResultInfo.Text      = FormatRect(_reforgeState.ResultRect);
        RfPostAnimDelayBox.Text = _reforgeState.PostAnimationDelayMs.ToString();
        RfMaxOpsBox.Text        = _reforgeState.MaxOps.ToString();
        RfRefreshCatalystList();
    }

    // ── Pick-кнопки ──────────────────────────────────────────────────────

    private void RfPickScanGrid_Click(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true) return;
        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region) return;
        _reforgeState.ItemCells = picker.SelectedCells is { Count: > 0 } c ? c.ToList() : new List<ScreenRect> { region };
        SaveSettings();
        RfScanGridInfo.Text = $"{_reforgeState.ItemCells.Count} ячеек";
        RfLog($"Сетка задана: {_reforgeState.ItemCells.Count} ячеек");
    }

    private void RfPickSlot1_Click(object sender, RoutedEventArgs e) =>
        RfPick("Слот 1 станка", r => { _reforgeState.Slot1Rect = r; RfSlot1Info.Text = FormatRect(r); });

    private void RfPickSlot2_Click(object sender, RoutedEventArgs e) =>
        RfPick("Слот 2 станка", r => { _reforgeState.Slot2Rect = r; RfSlot2Info.Text = FormatRect(r); });

    private void RfPickSlot3_Click(object sender, RoutedEventArgs e) =>
        RfPick("Слот 3 станка", r => { _reforgeState.Slot3Rect = r; RfSlot3Info.Text = FormatRect(r); });

    private void RfPickConfirm_Click(object sender, RoutedEventArgs e) =>
        RfPick("Кнопка Reforge", r => { _reforgeState.ConfirmRect = r; RfConfirmInfo.Text = FormatRect(r); });

    private void RfPickResult_Click(object sender, RoutedEventArgs e) =>
        RfPick("Область результата", r => { _reforgeState.ResultRect = r; RfResultInfo.Text = FormatRect(r); });

    private void RfPick(string hint, Action<ScreenRect> apply)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        apply(region);
        SaveSettings();
        RfLog($"Область «{hint}» задана: {FormatRect(region)}");
    }

    // ── Список катализаторов ──────────────────────────────────────────────

    private void RfRefreshCatalystList()
    {
        RfCatalystCheckList.Items.Clear();
        foreach (var item in Services.StackableItemRegistry.Items)
        {
            var cb = new System.Windows.Controls.CheckBox
            {
                Content   = $"{item.DisplayName}  [{item.Kind}]",
                Tag       = item.Id,
                IsChecked = _reforgeState.SelectedCatalystIds.Contains(item.Id),
            };
            cb.Checked   += RfCatalystCheck_Changed;
            cb.Unchecked += RfCatalystCheck_Changed;
            RfCatalystCheckList.Items.Add(cb);
        }
        RfUpdateSelectionStatus();
    }

    private void RfCatalystCheck_Changed(object sender, RoutedEventArgs e)
    {
        RfSyncSelectedIds();
        SaveSettings();
        RfUpdateSelectionStatus();
        RfUpdatePriceDisplay();
    }

    private void RfSyncSelectedIds()
    {
        _reforgeState.SelectedCatalystIds.Clear();
        foreach (System.Windows.Controls.CheckBox cb in RfCatalystCheckList.Items)
            if (cb.IsChecked == true && cb.Tag is string id)
                _reforgeState.SelectedCatalystIds.Add(id);
    }

    private void RfUpdateSelectionStatus()
    {
        var total    = RfCatalystCheckList.Items.Count;
        var selected = RfCatalystCheckList.Items.Cast<System.Windows.Controls.CheckBox>().Count(cb => cb.IsChecked == true);
        RfCatalystSelectionStatus.Text = $"Выбрано: {selected} / {total}";
    }

    private void RfDeselectAllCatalysts_Click(object sender, RoutedEventArgs e)
    {
        foreach (System.Windows.Controls.CheckBox cb in RfCatalystCheckList.Items) cb.IsChecked = false;
    }

    // ── Сканирование реестра ──────────────────────────────────────────────

    private async void RfScanGridBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_reforgeState.ItemCells.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Задайте сетку инвентаря кнопкой «Задать сетку…».",
                "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _rfScanCts?.Cancel();
        _rfScanCts = new CancellationTokenSource();
        var ct = _rfScanCts.Token;

        RfScanGridBtn.IsEnabled = false;
        RfRegistryScanStatus.Text = "Сканирование…";

        var added = 0; var skipped = 0; var empty = 0;
        var mouseMs = RfParseInt(RfMouseDelayBox.Text, 80);
        var clipMs  = RfParseInt(RfClipDelayBox.Text, 220);

        _rfService.MouseActionDelayMs = mouseMs;
        _rfService.ClipboardDelayMs   = clipMs;

        try
        {
            Native.ProcessForeground.TryBringProcessToForeground(
                Native.ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(150, ct);

            foreach (var cell in _reforgeState.ItemCells)
            {
                ct.ThrowIfCancellationRequested();
                var (x, y) = cell.GetRandomInteriorPoint(inset: 2);
                Native.Win32Input.MoveTo(x, y);
                await Task.Delay(RfWithJitter(mouseMs), ct);
                await Task.Delay(Math.Clamp(mouseMs / 2, 50, 150), ct);
                await RfClearClipboardAsync();
                Native.Win32Input.SendCtrlAltC();
                Native.Win32Input.ReleaseCtrlAlt();
                await Task.Delay(RfWithJitter(clipMs), ct);
                var text = await Dispatcher.InvokeAsync(RfGetClipboardTextSafe);
                if (string.IsNullOrWhiteSpace(text)) { empty++; continue; }
                var parsed = Services.ItemParser.Parse(text);
                if (parsed == null || !parsed.IsValid) { skipped++; continue; }
                var (wasAdded, entry) = Services.StackableItemRegistry.TryRegister(parsed);
                if (wasAdded) { added++; RfLog($"  + {entry!.DisplayName} ({entry.Kind})"); }
                else if (entry != null) skipped++;
                else { RfLog($"  ? пропущено: {parsed.Name}"); skipped++; }
            }

            Services.StackableItemRegistry.Save();
            RfRefreshCatalystList();
            RebuildBreachPanel();
            RebuildDeliriumPanel();
            RebuildSocketablePanel();
            RebuildProfitTable();
            RfRegistryScanStatus.Text = $"+{added} новых, {skipped} пропущено, {empty} пустых";
        }
        catch (OperationCanceledException)
        {
            RfRegistryScanStatus.Text = "Отменено";
        }
        catch (Exception ex)
        {
            RfRegistryScanStatus.Text = $"Ошибка: {ex.Message}";
            RfLog($"[Scan] {ex.Message}");
        }
        finally
        {
            Native.Win32Input.ReleaseCtrlAlt();
            RfScanGridBtn.IsEnabled = true;
        }
    }

    private void RfClearRegistryBtn_Click(object sender, RoutedEventArgs e)
    {
        var r = System.Windows.MessageBox.Show(this,
            "Очистить реестр катализаторов?",
            "Очистить реестр", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        Services.StackableItemRegistry.Clear();
        RfRefreshCatalystList();
        RebuildBreachPanel();
        RebuildDeliriumPanel();
        RebuildSocketablePanel();
        RebuildProfitTable();
        RfLog("[Registry] Реестр очищен");
    }

    // ── poe.ninja цены ───────────────────────────────────────────────────

    private async void PoeNinjaFetchBtn_Click(object sender, RoutedEventArgs e)
    {
        var league = PoeNinjaLeagueBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(league)) league = "Standard";
        SaveSettings();

        PoeNinjaStatusText.Text = "Загрузка цен…";
        try
        {
            await Services.PoeNinjaPriceService.FetchAsync(league);
        }
        catch (Exception ex)
        {
            PoeNinjaStatusText.Text = $"Ошибка: {ex.Message}";
        }
    }

    private void OnPoeNinjaPricesUpdated() =>
        Dispatcher.InvokeAsync(RfUpdatePriceDisplay);

    private void RfUpdatePriceDisplay()
    {
        var at = Services.PoeNinjaPriceService.LastFetchedAt;
        PoeNinjaStatusText.Text = at.HasValue
            ? $"Актуально на {at.Value:dd.MM HH:mm} · {Services.PoeNinjaPriceService.ItemCount} предметов · {Services.PoeNinjaPriceService.SnapshotCount} снэпшотов"
            : "Цены не загружены.";
        RebuildProfitTable();
    }

    // Комиссия рынка при продаже за экзальты: 240 зол. за каждый полученный экзальт
    private const int ExaltGoldRate = 240;

    private void RebuildProfitTable()
    {
        ProfitTablePanel.Children.Clear();

        var catalysts = Services.StackableItemRegistry.Items
            .Where(i => i.Kind == Services.StackableItemKind.Catalyst)
            .ToList();

        if (catalysts.Count == 0) return;

        const decimal budget = 100_000m;

        var rfDist = ComputeReforgeDistribution();
        decimal sell300kEx = ComputeSell300kEx(rfDist);

        var rows = new System.Collections.Generic.List<(string Name, decimal ExPrice, int GoldPrice, decimal Units, decimal ProfitPer100k, decimal Buy900kEx, decimal Sell300kEx)>();

        foreach (var cat in catalysts)
        {
            var ninja = Services.PoeNinjaPriceService.GetPrice(cat.DisplayName);
            if (ninja == null) continue;

            _catalystGoldPrices.TryGetValue(cat.Id, out var goldPrice);
            if (goldPrice <= 0) continue;

            var exPrice = ninja.ExaltedValue;
            if (exPrice <= 0) continue;

            // Покупаем N катализаторов за золото, продаём за экзальты.
            // Продажа за экзальты сама стоит золото: earnExalts * ExaltGoldRate.
            // Итоговое золото = покупка + стоимость продажи.
            // Прибыль/100к = earnExalts * budget / totalGold
            decimal n = budget / goldPrice;
            decimal earnExalts = n * exPrice;
            decimal totalGold = budget + earnExalts * ExaltGoldRate;
            decimal profitPer100k = Math.Round(earnExalts * budget / totalGold);

            decimal buy900kEx = Math.Round(900_000m * exPrice);

            rows.Add((cat.DisplayName, exPrice, goldPrice, Math.Round(n), profitPer100k, buy900kEx, sell300kEx));
        }

        if (rows.Count == 0)
        {
            ProfitTablePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Нет данных — загрузите цены poe.ninja.",
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            });
            return;
        }

        rows = [.. rows.OrderByDescending(r => r.ProfitPer100k)];

        int rfTotal = rfDist.Values.Sum();
        string sell300kHeader = rfTotal > 0 ? $"→300к ex ({rfTotal})" : "→300к ex";
        ProfitTablePanel.Children.Add(CreateProfitRow("Катализатор", "ex/шт.", "зол./шт.", "шт./100к", "ex/100к ↑", "×900к", sell300kHeader, isHeader: true));
        foreach (var row in rows)
            ProfitTablePanel.Children.Add(CreateProfitRow(
                row.Name,
                $"{row.ExPrice:0.##}",
                $"{row.GoldPrice:N0}",
                $"{row.Units:N0}",
                $"{row.ProfitPer100k:N0}",
                $"{row.Buy900kEx:N0}",
                $"{row.Sell300kEx:N0}",
                isHeader: false));
    }

    private static Dictionary<string, int> ComputeReforgeDistribution()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var logDir = ProjectPaths.GetLogDirectory();
        if (!Directory.Exists(logDir)) return counts;

        var rx = new System.Text.RegularExpressions.Regex(@"\[(\d+)/(\d+)\] → (.+ Catalyst)");
        foreach (var file in Directory.GetFiles(logDir, "session_*.txt"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var m = rx.Match(line);
                if (!m.Success) continue;
                var name = m.Groups[3].Value.Trim();
                counts.TryGetValue(name, out var cnt);
                counts[name] = cnt + 1;
            }
        }
        return counts;
    }

    private static decimal ComputeSell300kEx(Dictionary<string, int> dist)
    {
        int total = dist.Values.Sum();
        if (total == 0) return 0;
        decimal sell = 0;
        foreach (var (name, cnt) in dist)
        {
            var ninja = Services.PoeNinjaPriceService.GetPrice(name);
            if (ninja == null) continue;
            sell += 300_000m * cnt / total * ninja.ExaltedValue;
        }
        return Math.Round(sell);
    }

    private static System.Windows.UIElement CreateProfitRow(
        string name, string exPrice, string goldPrice, string units, string profit,
        string buy900k, string sell300k, bool isHeader)
    {
        var grid = new System.Windows.Controls.Grid
        {
            Margin = new System.Windows.Thickness(0, 0, 0, isHeader ? 4 : 1),
        };
        int[] widths = [145, 50, 60, 60, 75, 75, 80];
        foreach (var w in widths)
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = new System.Windows.GridLength(w) });

        string[] texts = [name, exPrice, goldPrice, units, profit, buy900k, sell300k];
        for (var i = 0; i < texts.Length; i++)
        {
            System.Windows.Media.Brush? fg = isHeader
                ? System.Windows.Media.Brushes.Gray
                : i == 4 ? System.Windows.Media.Brushes.DarkGreen
                : i == 6 ? System.Windows.Media.Brushes.SteelBlue
                : null;
            var tb = new System.Windows.Controls.TextBlock
            {
                Text        = texts[i],
                FontSize    = 11,
                FontWeight  = isHeader ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal,
                TextAlignment = i > 0 ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Padding     = new System.Windows.Thickness(i > 0 ? 8 : 0, 0, 0, 0),
            };
            if (fg != null) tb.Foreground = fg;
            System.Windows.Controls.Grid.SetColumn(tb, i);
            grid.Children.Add(tb);
        }
        return grid;
    }

    // ── Старт / Стоп ─────────────────────────────────────────────────────

    private void RfStartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!RfValidateForRun()) return;

        _rfService.MouseActionDelayMs           = RfParseInt(RfMouseDelayBox.Text, 80);
        _rfService.ClipboardDelayMs             = RfParseInt(RfClipDelayBox.Text, 220);
        _rfService.PostReforgeSettleMs          = RfParseInt(RfPostAnimDelayBox.Text, 800);
        _rfService.HoverSettleBeforeClipboardMs = RfParseInt(RfHoverSettleBox.Text, 150);
        _rfService.ResultRetryDelayMs           = RfParseInt(RfRetryDelayBox.Text, 400);
        _rfService.ReforgeAttemptRetries        = RfParseInt(RfAttemptRetriesBox.Text, 1, allowZero: true);
        _rfService.ItemTransferDelayMs          = RfParseInt(RfItemTransferDelayBox.Text, 400);

        _reforgeState.PostAnimationDelayMs = _rfService.PostReforgeSettleMs;
        _reforgeState.MaxOps = RfParseInt(RfMaxOpsBox.Text, 0, allowZero: true);
        RfSyncSelectedIds();
        SaveSettings();
        MinimizeToTrayOnStart();

        _rfCts = new CancellationTokenSource();
        RfStartBtn.IsEnabled = false;
        RfStopBtn.IsEnabled  = true;

        var selectedIds = _reforgeState.SelectedCatalystIds.ToList();
        var maxOps      = _reforgeState.MaxOps;
        var progress    = new Progress<string>(RfLog);
        var ct          = _rfCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                Native.ProcessForeground.TryBringProcessToForeground(
                    Native.ProcessForeground.PathOfExile2SteamProcessName);
                await Task.Delay(200, ct);

                var reason = await _rfService.RunAsync(
                    _reforgeState.ItemCells, selectedIds,
                    _reforgeState.Slot1Rect, _reforgeState.Slot2Rect, _reforgeState.Slot3Rect,
                    _reforgeState.ConfirmRect, _reforgeState.ResultRect,
                    maxOps, progress,
                    r => Dispatcher.InvokeAsync(() => RfLog($"  → {r.InputTypeName} → {r.OutputItemName ?? "?"}")),
                    ct);

                ((IProgress<string>)progress).Report($"[Reforge] Стоп: {reason}");
            }
            catch (OperationCanceledException)
            {
                ((IProgress<string>)progress).Report("[Reforge] Отменено.");
            }
            catch (Exception ex)
            {
                ((IProgress<string>)progress).Report($"[Reforge] Ошибка: {ex.Message}");
            }
            finally
            {
                Native.Win32Input.ReleaseCtrlAlt();
                await Dispatcher.InvokeAsync(() =>
                {
                    RfStartBtn.IsEnabled = true;
                    RfStopBtn.IsEnabled  = false;
                    MaybeKillPoeProcessAfterCraft();
                });
            }
        }, CancellationToken.None);
    }

    private void RfStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _rfCts?.Cancel();
        RfStopBtn.IsEnabled = false;
    }

    private void RfToggleStartStop()
    {
        if (RfStopBtn.IsEnabled)
            RfStopBtn_Click(RfStopBtn, new RoutedEventArgs());
        else
            RfStartBtn_Click(RfStartBtn, new RoutedEventArgs());
    }

    private void RfAutoToggleStartStop()
    {
        if (RfAutoStopBtn.IsEnabled)
            RfAutoStopBtn_Click(RfAutoStopBtn, new RoutedEventArgs());
        else
            RfAutoStartBtn_Click(RfAutoStartBtn, new RoutedEventArgs());
    }

    // ── Горячая клавиша перековки ─────────────────────────────────────────

    private void RegisterReforgeStartStopHotkey()
    {
        UnregisterReforgeStartStopHotkey();
        if (_reforgeStartStopVirtualKey == 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!GlobalHotkey.TryRegisterReforgeStartStop(hwnd, (uint)_reforgeStartStopVirtualKey, (uint)_reforgeStartStopModifiers))
            SessionLogger.Info("Горячая клавиша «Старт/Стоп перековки» не зарегистрирована — возможно, занята другим процессом.");
        else
            _reforgeStartStopHotkeyRegistered = true;
    }

    private void UnregisterReforgeStartStopHotkey()
    {
        if (!_reforgeStartStopHotkeyRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterReforgeStartStop(hwnd);
        _reforgeStartStopHotkeyRegistered = false;
    }

    private void UpdateReforgeStartStopHotkeyDisplay()
    {
        ReforgeStartStopHotkeyBox.Text = FormatHotkey(_reforgeStartStopVirtualKey, _reforgeStartStopModifiers);
    }

    private void ReforgeStartStopHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
            return;

        if (key == Key.Escape)
        {
            _reforgeStartStopVirtualKey = 0;
            _reforgeStartStopModifiers  = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _reforgeStartStopVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _reforgeStartStopModifiers  = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                        | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                        | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }

        UpdateReforgeStartStopHotkeyDisplay();
        RegisterReforgeStartStopHotkey();
        SaveSettings();
    }

    // ── Горячая клавиша авто-перековки ───────────────────────────────────

    private void RegisterAutoReforgeStartStopHotkey()
    {
        UnregisterAutoReforgeStartStopHotkey();
        if (_autoReforgeStartStopVirtualKey == 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!GlobalHotkey.TryRegisterAutoReforgeStartStop(hwnd, (uint)_autoReforgeStartStopVirtualKey, (uint)_autoReforgeStartStopModifiers))
            SessionLogger.Info("Горячая клавиша «Авто Старт/Стоп перековки» не зарегистрирована — возможно, занята другим процессом.");
        else
            _autoReforgeStartStopHotkeyRegistered = true;
    }

    private void UnregisterAutoReforgeStartStopHotkey()
    {
        if (!_autoReforgeStartStopHotkeyRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterAutoReforgeStartStop(hwnd);
        _autoReforgeStartStopHotkeyRegistered = false;
    }

    private void UpdateAutoReforgeStartStopHotkeyDisplay()
    {
        AutoReforgeStartStopHotkeyBox.Text = FormatHotkey(_autoReforgeStartStopVirtualKey, _autoReforgeStartStopModifiers);
    }

    private void AutoReforgeStartStopHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
            return;

        if (key == Key.Escape)
        {
            _autoReforgeStartStopVirtualKey = 0;
            _autoReforgeStartStopModifiers  = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _autoReforgeStartStopVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _autoReforgeStartStopModifiers  = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                            | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                            | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }

        UpdateAutoReforgeStartStopHotkeyDisplay();
        RegisterAutoReforgeStartStopHotkey();
        SaveSettings();
    }

    private void RegisterNetworthStartStopHotkey()
    {
        UnregisterNetworthStartStopHotkey();
        if (_networthStartStopVirtualKey == 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!GlobalHotkey.TryRegisterNetworthStartStop(hwnd, (uint)_networthStartStopVirtualKey, (uint)_networthStartStopModifiers))
            SessionLogger.Info("Горячая клавиша «Networth Старт/Стоп» не зарегистрирована — возможно, занята другим процессом.");
        else
            _networthStartStopHotkeyRegistered = true;
    }

    private void UnregisterNetworthStartStopHotkey()
    {
        if (!_networthStartStopHotkeyRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterNetworthStartStop(hwnd);
        _networthStartStopHotkeyRegistered = false;
    }

    private void UpdateNetworthStartStopHotkeyDisplay()
    {
        NetworthStartStopHotkeyBox.Text = FormatHotkey(_networthStartStopVirtualKey, _networthStartStopModifiers);
    }

    private void NetworthStartStopHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
            return;

        if (key == Key.Escape)
        {
            _networthStartStopVirtualKey = 0;
            _networthStartStopModifiers  = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _networthStartStopVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _networthStartStopModifiers  = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                         | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                         | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }

        UpdateNetworthStartStopHotkeyDisplay();
        RegisterNetworthStartStopHotkey();
        SaveSettings();
    }

    private void NetworthToggleStartStop()
    {
        if (_nwScanCts != null && !_nwScanCts.IsCancellationRequested)
            _nwScanCts.Cancel();
        else
            NetworthScanBtn_Click(this, new RoutedEventArgs());
    }

    // ── Валидация ─────────────────────────────────────────────────────────

    private bool RfValidateForRun(bool requireCatalystSelection = true)
    {
        var missing = new List<string>();
        if (_reforgeState.ItemCells.Count == 0)          missing.Add("Сетка инвентаря");
        if (_reforgeState.Slot1Rect is { Width: <= 0 })  missing.Add("Слот 1");
        if (_reforgeState.Slot2Rect is { Width: <= 0 })  missing.Add("Слот 2");
        if (_reforgeState.Slot3Rect is { Width: <= 0 })  missing.Add("Слот 3");
        if (_reforgeState.ConfirmRect is { Width: <= 0 }) missing.Add("Кнопка Reforge");
        if (_reforgeState.ResultRect  is { Width: <= 0 }) missing.Add("Область результата");

        if (missing.Count > 0)
        {
            System.Windows.MessageBox.Show(this,
                "Не заданы:\n• " + string.Join("\n• ", missing),
                "Перековка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        RfSyncSelectedIds();
        if (requireCatalystSelection && _reforgeState.SelectedCatalystIds.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Отметьте хотя бы один тип катализатора в списке.",
                "Перековка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    // ── Вспомогательные ──────────────────────────────────────────────────

    private void RfLog(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => RfLog(msg)); return; }
        RfLogBox.AppendText(msg + "\n");
        RfLogBox.ScrollToEnd();
        Services.SessionLogger.Info($"[Reforge] {msg}");
    }

    private static int RfParseInt(string s, int fallback, bool allowZero = false)
    {
        if (!int.TryParse(s.Trim(), out var v)) return fallback;
        return (allowZero && v == 0) || v > 0 ? v : fallback;
    }

    private static decimal RfParseDecimal(string s, decimal fallback)
    {
        var normalized = s.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
    }

    private static int RfWithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * 0.30);
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static string RfGetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.GetText(); }
        catch { return ""; }
    }

    private static async Task RfClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });

    // ═══════════════════════════════════════════════════════════════════════
    // BREACH — панель областей катализаторов
    // ═══════════════════════════════════════════════════════════════════════

    private void PickBreachInventoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _breachInventoryRect = region;
        BreachInventoryInfo.Text = FormatRect(region);
        SaveSettings();
    }

    // Статический список валюты с точными именами poe.ninja, сгруппированный для UI.
    private static readonly (string Header, string[] Items)[] CurrencyItemGroups =
    [
        ("Редкая валюта", ["Divine Orb", "Fracturing Orb", "Orb of Annulment", "Orb of Extraction",
                           "Crystallised Corruption", "Architect's Orb", "Vaal Orb", "Orb of Chance", "Orb of Alchemy",
                           "Hinekora's Lock"]),
        ("Chaos Orb",        ["Chaos Orb", "Greater Chaos Orb", "Perfect Chaos Orb"]),
        ("Exalted Orb",      ["Exalted Orb", "Greater Exalted Orb", "Perfect Exalted Orb"]),
        ("Orb of Augmentation", ["Orb of Augmentation", "Greater Orb of Augmentation", "Perfect Orb of Augmentation"]),
        ("Orb of Transmutation", ["Orb of Transmutation", "Greater Orb of Transmutation", "Perfect Orb of Transmutation"]),
        ("Regal Orb",        ["Regal Orb", "Greater Regal Orb", "Perfect Regal Orb"]),
        ("Jeweller's Orb",   ["Lesser Jeweller's Orb", "Greater Jeweller's Orb", "Perfect Jeweller's Orb"]),
        ("Другое",           ["Arcanist's Etcher", "Artificer's Orb", "Blacksmith's Whetstone",
                               "Glassblower's Bauble", "Armourer's Scrap", "Gemcutter's Prism"]),
    ];

    private void RebuildCurrencyItemPanel()
    {
        CurrencyItemRegionsPanel.Children.Clear();
        bool firstGroup = true;
        foreach (var (header, items) in CurrencyItemGroups)
        {
            if (!firstGroup)
                CurrencyItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                    { Margin = new System.Windows.Thickness(0, 6, 0, 6) });
            firstGroup = false;

            CurrencyItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = header,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            });

            foreach (var itemName in items)
            {
                _currencyItemRegions.TryGetValue(itemName, out var rect);
                var infoText = new System.Windows.Controls.TextBlock
                {
                    Text = FormatRect(rect),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                };

                var btn = new System.Windows.Controls.Button
                {
                    Content = "Задать…",
                    Padding = new System.Windows.Thickness(8, 4, 8, 4),
                    Tag = (itemName, infoText),
                };
                btn.Click += CurrencyItemPickBtn_Click;

                var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(240) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                var label = new System.Windows.Controls.TextBlock
                {
                    Text = itemName,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                };
                System.Windows.Controls.Grid.SetColumn(label, 0);
                System.Windows.Controls.Grid.SetColumn(infoText, 1);
                System.Windows.Controls.Grid.SetColumn(btn, 2);

                row.Children.Add(label);
                row.Children.Add(infoText);
                row.Children.Add(btn);
                CurrencyItemRegionsPanel.Children.Add(row);
            }
        }
    }

    private void CurrencyItemPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string itemName, System.Windows.Controls.TextBlock infoBlock)) return;

        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;

        _currencyItemRegions[itemName] = region;
        infoBlock.Text = FormatRect(region);
        SaveSettings();
    }

    // Статический список ритуальных предметов с точными именами poe.ninja, сгруппированный для UI.
    private static readonly (string Header, string[] Items)[] RitualItemGroups =
    [
        ("Экзальтация",
            ["Omen of Sinistral Exaltation", "Omen of Dextral Exaltation",
             "Omen of Greater Exaltation",   "Omen of Catalysing Exaltation"]),
        ("Аннулирование / Стирание",
            ["Omen of Sinistral Annulment", "Omen of Dextral Annulment",
             "Omen of Sinistral Erasure",   "Omen of Dextral Erasure",
             "Omen of Whittling"]),
        ("Некромантия / Кристаллизация",
            ["Omen of Sinistral Necromancy", "Omen of Dextral Necromancy",
             "Omen of Sinistral Crystallisation", "Omen of Dextral Crystallisation"]),
        ("Хаос",
            ["Omen of Chaotic Quantity", "Omen of Chaotic Effectiveness",
             "Omen of Chaotic Monsters",  "Omen of Chaotic Rarity",
             "Omen of Gambling",          "Omen of Chance"]),
        ("Уникальные предметы",
            ["An Audience with the King", "Head of the King", "Call of the Shadows",
             "Raven-Touched Shard",       "Omen of Answered Prayers"]),
        ("Другое",
            ["Omen of Amelioration",       "Omen of Bartering",        "Omen of Refreshment",
             "Omen of Reinforcements",     "Omen of Resurgence",       "Omen of Sanctification",
             "Omen of Putrefaction",       "Omen of Abyssal Echoes",   "Omen of Light",
             "Omen of the Hunt",           "Omen of the Liege",        "Omen of the Ancients",
             "Omen of the Blackblooded",   "Omen of the Blessed",      "Omen of the Sovereign",
             "Omen of Secret Compartments"]),
    ];

    private void RebuildRitualItemPanel()
    {
        RitualItemRegionsPanel.Children.Clear();
        bool firstGroup = true;
        foreach (var (header, items) in RitualItemGroups)
        {
            if (!firstGroup)
                RitualItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                    { Margin = new System.Windows.Thickness(0, 6, 0, 6) });
            firstGroup = false;

            RitualItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = header,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            });

            foreach (var itemName in items)
            {
                _ritualItemRegions.TryGetValue(itemName, out var rect);
                var infoText = new System.Windows.Controls.TextBlock
                {
                    Text = FormatRect(rect),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                };

                var btn = new System.Windows.Controls.Button
                {
                    Content = "Задать…",
                    Padding = new System.Windows.Thickness(8, 4, 8, 4),
                    Tag = (itemName, infoText),
                };
                btn.Click += RitualItemPickBtn_Click;

                var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(240) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                var label = new System.Windows.Controls.TextBlock
                {
                    Text = itemName,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                };
                System.Windows.Controls.Grid.SetColumn(label, 0);
                System.Windows.Controls.Grid.SetColumn(infoText, 1);
                System.Windows.Controls.Grid.SetColumn(btn, 2);

                row.Children.Add(label);
                row.Children.Add(infoText);
                row.Children.Add(btn);
                RitualItemRegionsPanel.Children.Add(row);
            }
        }
    }

    private void RitualItemPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string itemName, System.Windows.Controls.TextBlock infoBlock)) return;

        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;

        _ritualItemRegions[itemName] = region;
        infoBlock.Text = FormatRect(region);
        SaveSettings();
    }

    private void RebuildBreachPanel()
    {
        BreachCatalystRegionsPanel.Children.Clear();

        var catalysts = Services.StackableItemRegistry.Items
            .Where(i => i.Kind == Services.StackableItemKind.Catalyst)
            .ToList();

        if (catalysts.Count == 0)
        {
            BreachCatalystRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Реестр катализаторов пуст. Сначала отсканируйте катализаторы на вкладке «Перековка».",
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
            });
            return;
        }

        foreach (var item in catalysts)
        {
            _breachCatalystRegions.TryGetValue(item.Id, out var rect);
            var infoText = new System.Windows.Controls.TextBlock
            {
                Text = FormatRect(rect),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
            };

            var btn = new System.Windows.Controls.Button
            {
                Content = "Задать…",
                Padding = new System.Windows.Thickness(8, 4, 8, 4),
                Tag = (item.Id, infoText),
            };
            btn.Click += BreachCatalystPickBtn_Click;

            var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(200) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = item.DisplayName,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            System.Windows.Controls.Grid.SetColumn(label, 0);
            System.Windows.Controls.Grid.SetColumn(infoText, 1);
            System.Windows.Controls.Grid.SetColumn(btn, 2);

            row.Children.Add(label);
            row.Children.Add(infoText);
            row.Children.Add(btn);
            BreachCatalystRegionsPanel.Children.Add(row);
        }
    }

    private void BreachCatalystPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string id, System.Windows.Controls.TextBlock infoBlock)) return;

        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;

        _breachCatalystRegions[id] = region;
        infoBlock.Text = FormatRect(region);
        SaveSettings();
    }

    // ── Delirium ──────────────────────────────────────────────────────────

    private void PickDeliriumInventoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _deliriumInventoryRect = region;
        DeliriumInventoryInfo.Text = FormatRect(region);
        SaveSettings();
    }

    private void RebuildDeliriumPanel()
    {
        DeliriumItemRegionsPanel.Children.Clear();

        var all = Services.StackableItemRegistry.Items
            .Where(i => i.Kind == Services.StackableItemKind.Delirium)
            .ToList();

        if (all.Count == 0)
        {
            DeliriumItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Реестр предметов делирия пуст. Сначала отсканируйте предметы на вкладке «Перековка».",
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
            });
            return;
        }

        static bool IsAncient(Services.StackableItemType i) =>
            i.DisplayName.StartsWith("Ancient ", StringComparison.OrdinalIgnoreCase);
        static bool IsPlain(Services.StackableItemType i) =>
            i.DisplayName.StartsWith("Liquid ", StringComparison.OrdinalIgnoreCase);

        var groups = new (string Header, IEnumerable<Services.StackableItemType> Items)[]
        {
            ("Liquid (просто эмоция)", all.Where(IsPlain)),
            ("Ancient",               all.Where(IsAncient)),
            ("Potent / Concentrated / Diluted", all.Where(i => !IsPlain(i) && !IsAncient(i))),
        };

        bool firstGroup = true;
        foreach (var (header, items) in groups)
        {
            var list = items.OrderBy(i => i.DisplayName).ToList();
            if (list.Count == 0) continue;

            if (!firstGroup)
                DeliriumItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                    { Margin = new System.Windows.Thickness(0, 6, 0, 6) });
            firstGroup = false;

            DeliriumItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = header,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            });

            foreach (var item in list)
            {
                _deliriumItemRegions.TryGetValue(item.Id, out var rect);
                var infoText = new System.Windows.Controls.TextBlock
                {
                    Text = FormatRect(rect),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                };

                var btn = new System.Windows.Controls.Button
                {
                    Content = "Задать…",
                    Padding = new System.Windows.Thickness(8, 4, 8, 4),
                    Tag = (item.Id, infoText),
                };
                btn.Click += DeliriumItemPickBtn_Click;

                var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(240) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                var label = new System.Windows.Controls.TextBlock
                {
                    Text = item.DisplayName,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                };
                System.Windows.Controls.Grid.SetColumn(label, 0);
                System.Windows.Controls.Grid.SetColumn(infoText, 1);
                System.Windows.Controls.Grid.SetColumn(btn, 2);

                row.Children.Add(label);
                row.Children.Add(infoText);
                row.Children.Add(btn);
                DeliriumItemRegionsPanel.Children.Add(row);
            }
        }
    }

    private void DeliriumItemPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string id, System.Windows.Controls.TextBlock infoBlock)) return;

        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;

        _deliriumItemRegions[id] = region;
        infoBlock.Text = FormatRect(region);
        SaveSettings();
    }

    private void PickSocketableInventoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _socketableInventoryRect = region;
        SocketableInventoryInfo.Text = FormatRect(region);
        SaveSettings();
    }

    private void RebuildSocketablePanel()
    {
        SocketableItemRegionsPanel.Children.Clear();

        var runes     = Services.StackableItemRegistry.Items.Where(i => i.Kind == Services.StackableItemKind.Rune).ToList();
        var soulCores = Services.StackableItemRegistry.Items.Where(i => i.Kind == Services.StackableItemKind.SoulCore).ToList();

        if (runes.Count == 0 && soulCores.Count == 0)
        {
            SocketableItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Реестр Socketable-предметов пуст.",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
            });
            return;
        }

        static bool StartsWithCI(Services.StackableItemType i, string prefix) =>
            i.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        var runeGroups = new (string Header, IEnumerable<Services.StackableItemType> Items)[]
        {
            ("Perfect",          runes.Where(i => StartsWithCI(i, "Perfect "))),
            ("Greater",          runes.Where(i => StartsWithCI(i, "Greater "))),
            ("Base runes",       runes.Where(i => !StartsWithCI(i, "Perfect ") && !StartsWithCI(i, "Greater ") && !StartsWithCI(i, "Lesser ") && !StartsWithCI(i, "Ancient ") && !StartsWithCI(i, "Warding ") && i.DisplayName.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase))),
            ("Lesser",           runes.Where(i => StartsWithCI(i, "Lesser "))),
            ("Ancient",          runes.Where(i => StartsWithCI(i, "Ancient "))),
            ("Warding",          runes.Where(i => StartsWithCI(i, "Warding "))),
            ("Named / Unique",   runes.Where(i => !StartsWithCI(i, "Perfect ") && !StartsWithCI(i, "Greater ") && !StartsWithCI(i, "Lesser ") && !StartsWithCI(i, "Ancient ") && !StartsWithCI(i, "Warding ") && !i.DisplayName.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase))),
        };

        var scGroups = new (string Header, IEnumerable<Services.StackableItemType> Items)[]
        {
            ("Soul Core of",               soulCores.Where(i => StartsWithCI(i, "Soul Core of "))),
            ("Named Soul Cores",           soulCores.Where(i => !StartsWithCI(i, "Soul Core of ") && i.DisplayName.Contains("Soul Core", StringComparison.OrdinalIgnoreCase))),
            ("Other (Carved / Emergent / Thesis)", soulCores.Where(i => !i.DisplayName.Contains("Soul Core", StringComparison.OrdinalIgnoreCase))),
        };

        void AddSectionHeader(string text)
        {
            SocketableItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontWeight = System.Windows.FontWeights.Bold,
                FontSize = 12,
                Margin = new System.Windows.Thickness(0, 8, 0, 4),
            });
        }

        bool firstGroup = true;
        void RenderGroups((string Header, IEnumerable<Services.StackableItemType> Items)[] groups)
        {
            foreach (var (header, items) in groups)
            {
                var list = items.OrderBy(i => i.DisplayName).ToList();
                if (list.Count == 0) continue;

                if (!firstGroup)
                    SocketableItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                        { Margin = new System.Windows.Thickness(0, 4, 0, 4) });
                firstGroup = false;

                SocketableItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = header,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Margin = new System.Windows.Thickness(0, 0, 0, 4),
                });

                foreach (var item in list)
                {
                    _socketableItemRegions.TryGetValue(item.Id, out var rect);
                    var infoText = new System.Windows.Controls.TextBlock
                    {
                        Text = FormatRect(rect),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        FontSize = 11,
                    };

                    var btn = new System.Windows.Controls.Button
                    {
                        Content = "Задать…",
                        Padding = new System.Windows.Thickness(8, 4, 8, 4),
                        Tag = (item.Id, infoText),
                    };
                    btn.Click += SocketableItemPickBtn_Click;

                    var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(240) });
                    row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                    var label = new System.Windows.Controls.TextBlock
                    {
                        Text = item.DisplayName,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    };
                    System.Windows.Controls.Grid.SetColumn(label, 0);
                    System.Windows.Controls.Grid.SetColumn(infoText, 1);
                    System.Windows.Controls.Grid.SetColumn(btn, 2);

                    row.Children.Add(label);
                    row.Children.Add(infoText);
                    row.Children.Add(btn);
                    SocketableItemRegionsPanel.Children.Add(row);
                }
            }
        }

        if (runes.Count > 0)
        {
            AddSectionHeader("── Runes ──");
            RenderGroups(runeGroups);
        }

        if (soulCores.Count > 0)
        {
            if (runes.Count > 0)
                SocketableItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                    { Margin = new System.Windows.Thickness(0, 10, 0, 6) });
            firstGroup = true;
            AddSectionHeader("── Soul Cores ──");
            RenderGroups(scGroups);
        }
    }

    private void SocketableItemPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string id, System.Windows.Controls.TextBlock infoBlock)) return;

        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;

        _socketableItemRegions[id] = region;
        infoBlock.Text = FormatRect(region);
        SaveSettings();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NETWORTH
    // ═══════════════════════════════════════════════════════════════════════

    private async void NetworthScanBtn_Click(object sender, RoutedEventArgs e)
    {
        _nwScanCts?.Cancel();
        _nwScanCts = new CancellationTokenSource();
        var ct = _nwScanCts.Token;

        NetworthScanBtn.IsEnabled = false;
        NetworthStopBtn.IsEnabled = true;
        NetworthStatusText.Text = "Сканирование…";
        NetworthResultsPanel.Children.Clear();

        var mouseMs = RfParseInt(MouseActionDelayMs.Text, 80);
        var clipMs  = RfParseInt(ClipboardDelayMs.Text, 220);
        var registry = Services.StackableItemRegistry.Items;

        // Строим список групп из уже заданных областей
        Services.NwGroupDef MakeGroup(string name, ScreenRect tabRect, IReadOnlyDictionary<string, ScreenRect>? regions)
        {
            var items = regions?
                .Where(kv => kv.Value.Width > 0 && kv.Value.Height > 0)
                .Select(kv => (
                    ItemName: registry.FirstOrDefault(r => r.Id == kv.Key)?.DisplayName ?? kv.Key,
                    Area: kv.Value))
                .ToList()
                ?? new List<(string, ScreenRect)>();
            return new Services.NwGroupDef(name, tabRect, items);
        }

        var groups = new[]
        {
            MakeGroup("Currency",   _currencyInventoryRegion ?? default, _currencyItemRegions),
            MakeGroup("Ritual",     _ritualInventoryRegion   ?? default, _ritualItemRegions),
            MakeGroup("Breach",     _breachInventoryRect,    _breachCatalystRegions),
            MakeGroup("Delirium",   _deliriumInventoryRect,  _deliriumItemRegions),
            MakeGroup("Socketable", _socketableInventoryRect, _socketableItemRegions),
        };

        var totalItems = groups.Sum(g => g.Items.Count);
        if (totalItems == 0)
        {
            NetworthStatusText.Text = "Нет заданных областей предметов. Задайте области в «Настройки областей → Breach» и «→ Delirium».";
            Services.SessionLogger.Info("[Networth] Сканирование отменено: ни у одной группы нет заданных областей предметов.");
            NetworthScanBtn.IsEnabled = true;
            NetworthStopBtn.IsEnabled = false;
            return;
        }

        var stashOcrText   = StashOcrTextBox.Text.Trim();
        var stashOpenDelay = RfParseInt(RfStashOpenDelayBox.Text, 3000);
        var progress = new Progress<string>(msg => NetworthStatusText.Text = msg);
        Services.SessionLogger.Info($"[Networth] Старт сканирования: {groups.Length} групп, {totalItems} предметов.");

        MinimizeToTrayOnStart();

        try
        {
            var results = await Task.Run(() =>
                Services.NetworthService.ScanAsync(
                    _stashOcrSearchRect,
                    stashOcrText,
                    stashOpenDelay,
                    groups,
                    mouseMs,
                    clipMs,
                    () => RfClearClipboardAsync(),
                    () => Dispatcher.InvokeAsync(RfGetClipboardTextSafe).Task,
                    progress,
                    ct), ct);

            var scannedAt = DateTime.Now;
            Services.NetworthSnapshotStore.Save(results);
            NwDisplayResults(results, scannedAt);
        }
        catch (OperationCanceledException)
        {
            NetworthStatusText.Text = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            NetworthStatusText.Text = $"Ошибка: {ex.Message}";
            Services.SessionLogger.Info($"[Networth] Ошибка: {ex.Message}");
        }
        finally
        {
            Native.Win32Input.ReleaseCtrlAlt();
            NetworthScanBtn.IsEnabled = true;
            NetworthStopBtn.IsEnabled = false;
            Dispatcher.Invoke(RestoreFromTray);
        }
    }

    private void NetworthStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _nwScanCts?.Cancel();
    }

    private void NwDisplayResults(List<Services.NetworthGroupResult> results, DateTime? scannedAt = null)
    {
        NetworthResultsPanel.Children.Clear();

        var grandTotal = results.Sum(g => g.TotalDiv);

        var headerPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        headerPanel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"Итого: {grandTotal:F3} div",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
        });
        if (scannedAt.HasValue)
            headerPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"  (снэпшот от {scannedAt.Value:dd.MM.yyyy HH:mm})",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 12,
            });
        NetworthResultsPanel.Children.Add(headerPanel);

        foreach (var group in results)
        {
            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(90) });

            var rowCount = group.Items.Count + 2;
            for (var i = 0; i < rowCount; i++)
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            NwAddCell(grid, "Предмет",    0, 0, bold: true);
            NwAddCell(grid, "Цена (div)", 1, 0, bold: true);
            NwAddCell(grid, "Кол-во",     2, 0, bold: true);
            NwAddCell(grid, "Сумма (div)",3, 0, bold: true);

            for (var i = 0; i < group.Items.Count; i++)
            {
                var item = group.Items[i];
                var row = i + 1;
                var bg = i % 2 == 0 ? "#F5F5F5" : null;
                NwAddCell(grid, item.ItemName,                 0, row, bg: bg);
                NwAddCell(grid, item.PriceDiv.ToString("F3"),  1, row, bg: bg);
                NwAddCell(grid, item.Quantity.ToString(),      2, row, bg: bg);
                NwAddCell(grid, item.TotalDiv.ToString("F3"),  3, row, bg: bg);
            }

            var totalRow = group.Items.Count + 1;
            NwAddCell(grid, "ИТОГО",                        0, totalRow, bold: true);
            NwAddCell(grid, "",                             1, totalRow);
            NwAddCell(grid, "",                             2, totalRow);
            NwAddCell(grid, group.TotalDiv.ToString("F3"),  3, totalRow, bold: true);

            var expander = new System.Windows.Controls.Expander
            {
                Header = $"{group.GroupName}  —  {group.TotalDiv:F3} div  ({group.Items.Count} поз.)",
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 10),
                Content = new System.Windows.Controls.Border { Margin = new Thickness(4, 4, 0, 0), Child = grid },
            };
            NetworthResultsPanel.Children.Add(expander);
        }

        if (results.Count > 0)
        {
            var whenStr = scannedAt.HasValue ? $" (снэпшот от {scannedAt.Value:dd.MM.yyyy HH:mm})" : "";
            NetworthStatusText.Text = $"Итого: {grandTotal:F3} div по {results.Count} группам.{whenStr}";
        }
        else
            NetworthStatusText.Text = "Данных нет — пустые группы или ни одна Ctrl+Alt+C не вернула предмет.";

        Services.SessionLogger.Info($"[Networth] Готово. Итого: {grandTotal:F3} div.");
    }

    private static void NwAddCell(System.Windows.Controls.Grid grid, string text, int col, int row, bool bold = false, string? bg = null)
    {
        var tb = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        if (bg != null)
        {
            var border = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bg)),
                Child = tb,
            };
            System.Windows.Controls.Grid.SetColumn(border, col);
            System.Windows.Controls.Grid.SetRow(border, row);
            grid.Children.Add(border);
        }
        else
        {
            System.Windows.Controls.Grid.SetColumn(tb, col);
            System.Windows.Controls.Grid.SetRow(tb, row);
            grid.Children.Add(tb);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // АВТО-ПЕРЕКОВКА — навигация (STASH / Reforging Bench)
    // ═══════════════════════════════════════════════════════════════════════

    private void PickFullInventoryGridBtn_Click(object sender, RoutedEventArgs e)
    {
        // Фиксированная сетка 12 столбцов × 5 строк = 60 ячеек инвентаря персонажа
        var picker = new RegionPickerWindow(12, 5) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { }) return;
        _fullInventoryCells = picker.SelectedCells is { Count: > 0 } c ? c.ToList() : new();
        FullInventoryGridInfo.Text = _fullInventoryCells.Count > 0 ? $"{_fullInventoryCells.Count} ячеек" : "не задана";
        SaveSettings();
    }

    private void PickStashOcrSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _stashOcrSearchRect = region;
        StashOcrSearchInfo.Text = FormatRect(region);
        SaveSettings();
    }

    private void PickReforgingBenchOcrSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _reforgingBenchOcrSearchRect = region;
        ReforgingBenchOcrSearchInfo.Text = FormatRect(region);
        SaveSettings();
    }

    // ── Авто Старт / Стоп ────────────────────────────────────────────────

    private void RfAutoStartBtn_Click(object sender, RoutedEventArgs e)
    {
        // В режиме авто-каскада (нет выбранных типов + каскад включён) выбор типов не обязателен
        var cascadeOnly = _reforgeState.SelectedCatalystIds.Count == 0
                       && RfCascadeCheckBox.IsChecked == true;
        if (!RfValidateForRun(requireCatalystSelection: !cascadeOnly)) return;

        // Если нет выбранных типов и каскад не включён — блокируем с подсказкой
        if (!cascadeOnly && _reforgeState.SelectedCatalystIds.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Отметьте хотя бы один тип катализатора, или включите «Каскадный рефордж» " +
                "для авто-режима без выбора (типы выбираются динамически по стэшу).",
                "Авто-перековка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Дополнительная проверка областей авто-режима
        var missing = new List<string>();
        if (_stashOcrSearchRect is { Width: <= 0 })          missing.Add("Область поиска STASH");
        if (_breachInventoryRect is { Width: <= 0 })         missing.Add("Вкладка Breach Stash");
        if (_reforgingBenchOcrSearchRect is { Width: <= 0 }) missing.Add("Область поиска Reforging Bench");
        if (_fullInventoryCells.Count == 0)                  missing.Add("Полный инвентарь 12×5 (Разметка инвентаря)");

        if (!cascadeOnly)
        {
            var typesWithRegion = _reforgeState.SelectedCatalystIds
                .Where(id => _breachCatalystRegions.TryGetValue(id, out var r) && r.Width > 0)
                .ToList();
            if (typesWithRegion.Count == 0)
                missing.Add("Области катализаторов в Breach (хотя бы одна)");
        }
        else
        {
            if (!_breachCatalystRegions.Any(kv => kv.Value.Width > 0))
                missing.Add("Области катализаторов в Breach (хотя бы одна, для каскад-режима)");
        }

        if (missing.Count > 0)
        {
            System.Windows.MessageBox.Show(this,
                "Для авто-режима не заданы:\n• " + string.Join("\n• ", missing),
                "Авто-перековка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _rfService.MouseActionDelayMs           = RfParseInt(RfMouseDelayBox.Text, 80);
        _rfService.ClipboardDelayMs             = RfParseInt(RfClipDelayBox.Text, 220);
        _rfService.PostReforgeSettleMs          = RfParseInt(RfPostAnimDelayBox.Text, 800);
        _rfService.HoverSettleBeforeClipboardMs = RfParseInt(RfHoverSettleBox.Text, 150);
        _rfService.ResultRetryDelayMs           = RfParseInt(RfRetryDelayBox.Text, 400);
        _rfService.ReforgeAttemptRetries        = RfParseInt(RfAttemptRetriesBox.Text, 1, allowZero: true);
        _rfService.ItemTransferDelayMs          = RfParseInt(RfItemTransferDelayBox.Text, 400);

        _autoRfService.MouseActionDelayMs           = RfParseInt(RfMouseDelayBox.Text, 80);
        _autoRfService.ClipboardDelayMs             = RfParseInt(RfClipDelayBox.Text, 220);
        _autoRfService.HoverSettleBeforeClipboardMs = RfParseInt(RfHoverSettleBox.Text, 150);
        _autoRfService.StashOpenDelayMs             = RfParseInt(RfStashOpenDelayBox.Text, 3000);
        _autoRfService.ReforgingBenchOpenDelayMs    = RfParseInt(RfBenchOpenDelayBox.Text, 3000);
        _autoRfService.StashItemsPerClick           = RfParseInt(RfStashItemsPerClickBox.Text, 10);
        _autoRfService.ItemTransferDelayMs          = RfParseInt(RfItemTransferDelayBox.Text, 400);

        var cascadeEnabled = RfCascadeCheckBox.IsChecked == true;
        _autoRfService.CascadeThresholdEx = cascadeEnabled ? RfParseDecimal(RfCascadeThresholdBox.Text, 0m) : 0m;
        _autoRfService.CascadePriceFunc = cascadeEnabled
            ? name => Services.PoeNinjaPriceService.GetPrice(name)?.ExaltedValue
            : null;
        _autoRfService.MinStashCount = RfParseInt(RfCascadeMinStashBox.Text, 30);

        _reforgeState.PostAnimationDelayMs = _rfService.PostReforgeSettleMs;
        _reforgeState.MaxOps = RfParseInt(RfMaxOpsBox.Text, 0, allowZero: true);
        if (!cascadeOnly)
            RfSyncSelectedIds();
        SaveSettings();
        MinimizeToTrayOnStart();

        _autoRfCts = new CancellationTokenSource();
        RfAutoStartBtn.IsEnabled = false;
        RfAutoStopBtn.IsEnabled  = true;
        RfStartBtn.IsEnabled     = false;

        var selectedIds = _reforgeState.SelectedCatalystIds.ToList();
        var maxOps      = _reforgeState.MaxOps;
        var progress    = new Progress<string>(RfLog);
        var ct              = _autoRfCts.Token;
        var stashOcrRect    = _stashOcrSearchRect;
        var stashOcrText    = StashOcrTextBox.Text.Trim();
        var breachRect      = _breachInventoryRect;
        var benchOcrRect    = _reforgingBenchOcrSearchRect;
        var benchOcrText    = ReforgingBenchOcrTextBox.Text.Trim();
        var fullCells       = _fullInventoryCells.ToList();
        var regions         = new Dictionary<string, ScreenRect>(_breachCatalystRegions);

        _ = Task.Run(async () =>
        {
            try
            {
                Native.ProcessForeground.TryBringProcessToForeground(
                    Native.ProcessForeground.PathOfExile2SteamProcessName);
                await Task.Delay(200, ct);

                if (selectedIds.Count == 0)
                    ((IProgress<string>)progress).Report(
                        $"[Каскад-стэш] Авто-каскад: типы выбираются динамически (порог ≤ {_autoRfService.CascadeThresholdEx} ex).");

                var totalPerformed = 0;
                while (!ct.IsCancellationRequested)
                {
                    var cycleBefore = totalPerformed;
                    var cycleLimit  = maxOps > 0 ? maxOps - totalPerformed : 0;

                    await _autoRfService.RunAsync(
                        fullCells, selectedIds,
                        stashOcrRect, stashOcrText,
                        breachRect, regions,
                        _reforgeState.ItemCells,   // Сетка перековки (8×5) — только для сканирования
                        benchOcrRect, benchOcrText,
                        _reforgeState.Slot1Rect, _reforgeState.Slot2Rect, _reforgeState.Slot3Rect,
                        _reforgeState.ConfirmRect, _reforgeState.ResultRect,
                        cycleLimit, progress,
                        r => { totalPerformed++; Dispatcher.InvokeAsync(() => RfLog($"  → {r.InputTypeName} → {r.OutputItemName ?? "?"}")); },
                        ct);

                    if (ct.IsCancellationRequested) break;
                    // Нет прогресса = стэш пуст, продолжать бессмысленно
                    if (totalPerformed == cycleBefore) break;
                    // Достигли лимита операций
                    if (maxOps > 0 && totalPerformed >= maxOps) break;
                }
            }
            catch (OperationCanceledException)
            {
                ((IProgress<string>)progress).Report("[Авто] Отменено.");
            }
            catch (Exception ex)
            {
                ((IProgress<string>)progress).Report($"[Авто] Ошибка: {ex.Message}");
            }
            finally
            {
                Native.Win32Input.ReleaseCtrlAlt();
                _ = Services.CatalystReforgeStatsScanner.ScanNewLogsAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    RfAutoStartBtn.IsEnabled = true;
                    RfAutoStopBtn.IsEnabled  = false;
                    RfStartBtn.IsEnabled     = true;
                    MaybeKillPoeProcessAfterCraft();
                });
            }
        }, CancellationToken.None);
    }

    private void RfAutoStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _autoRfCts?.Cancel();
        RfAutoStopBtn.IsEnabled = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ПЕРЕОЦЕНКА
    // ═══════════════════════════════════════════════════════════════════════

    private void RegisterRepricingStartStopHotkey()
    {
        UnregisterRepricingStartStopHotkey();
        if (_repricingStartStopVirtualKey == 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!GlobalHotkey.TryRegisterRepricingStartStop(hwnd, (uint)_repricingStartStopVirtualKey, (uint)_repricingStartStopModifiers))
            SessionLogger.Info("Горячая клавиша «Переоценка Старт/Стоп» не зарегистрирована — возможно, занята.");
        else
            _repricingStartStopHotkeyRegistered = true;
    }

    private void UnregisterRepricingStartStopHotkey()
    {
        if (!_repricingStartStopHotkeyRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterRepricingStartStop(hwnd);
        _repricingStartStopHotkeyRegistered = false;
    }

    private void UpdateRepricingStartStopHotkeyDisplay()
    {
        RepricingStartStopHotkeyBox.Text = FormatHotkey(_repricingStartStopVirtualKey, _repricingStartStopModifiers);
    }

    private void RepricingToggleStartStop()
    {
        if (_repricingCts != null && !_repricingCts.IsCancellationRequested)
        {
            _repricingCts.Cancel();
            RepricingStopBtn.IsEnabled = false;
        }
        else
        {
            RepricingStartBtn_Click(this, new RoutedEventArgs());
        }
    }

    private void RepricingStartStopHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _repricingStartStopVirtualKey = 0;
            _repricingStartStopModifiers  = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _repricingStartStopVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _repricingStartStopModifiers  = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                          | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                          | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }
        UpdateRepricingStartStopHotkeyDisplay();
        RegisterRepricingStartStopHotkey();
        SaveSettings();
    }

    private void RepricingPickGridBtn_Click(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true) return;
        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region) return;
        _repricingCells = picker.SelectedCells is { Count: > 0 } c ? c.ToList() : new List<ScreenRect> { region };
        RepricingGridInfo.Text = FormatItemCellsSummary(_repricingCells);
        SaveSettings();
    }

    private async void RepricingStartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_repricingCells.Count == 0)
        {
            MessageBox.Show(this, "Задайте сетку ячеек для переоценки.", "Переоценка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _repricingCts?.Cancel();
        _repricingCts = new CancellationTokenSource();
        var ct = _repricingCts.Token;

        RepricingStartBtn.IsEnabled = false;
        RepricingStopBtn.IsEnabled  = true;
        RepricingStatusText.Text    = "Переоценка…";
        RepricingLogBox.Clear();

        _repricingService.MouseActionDelayMs = RfParseInt(MouseActionDelayMs.Text, 80);
        _repricingService.ClipboardDelayMs   = RfParseInt(ClipboardDelayMs.Text, 220);
        _repricingService.PostClickDelayMs   = RfParseInt(RepricingPostClickDelayBox.Text, 300);
        _repricingService.HoverSettleMs      = RfParseInt(RepricingHoverSettleBox.Text, 120);
        var repeatCount    = RfParseInt(RepricingRepeatCountBox.Text, 1);
        var intervalMinutes = RfParseInt(RepricingRepeatIntervalBox.Text, 5);
        SaveSettings();
        MinimizeToTrayOnStart();

        var cells      = _repricingCells.ToList();
        var dispatcher = Dispatcher;
        var progress   = new Progress<string>(msg =>
        {
            RepricingLogBox.AppendText(msg + "\n");
            RepricingLogBox.ScrollToEnd();
        });

        try
        {
            await Task.Run(async () =>
            {
                for (var iter = 1; !ct.IsCancellationRequested; iter++)
                {
                    var label = repeatCount > 0 ? $"{iter}/{repeatCount}" : $"{iter}";
                    ((IProgress<string>)progress).Report($"── Итерация {label} ──");
                    await dispatcher.InvokeAsync(() => RepricingStatusText.Text = $"Итерация {label}…");

                    Native.ProcessForeground.TryBringProcessToForeground(
                        Native.ProcessForeground.PathOfExile2SteamProcessName);
                    await Task.Delay(200, ct);
                    await _repricingService.RunAsync(cells, progress, ct);

                    if (repeatCount > 0 && iter >= repeatCount)
                        break;

                    var next = DateTime.Now.AddMinutes(intervalMinutes);
                    ((IProgress<string>)progress).Report(
                        $"Пауза {intervalMinutes} мин — следующая итерация в {next:HH:mm}");
                    await dispatcher.InvokeAsync(() =>
                        RepricingStatusText.Text = $"Ждём до {next:HH:mm}…");
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), ct);
                }
            }, CancellationToken.None);
            RepricingStatusText.Text = "Готово.";
        }
        catch (OperationCanceledException)
        {
            RepricingStatusText.Text = "Отменено.";
        }
        catch (Exception ex)
        {
            RepricingStatusText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            Native.Win32Input.ReleaseCtrlAlt();
            RepricingStartBtn.IsEnabled = true;
            RepricingStopBtn.IsEnabled  = false;
            Dispatcher.Invoke(RestoreFromTray);
        }
    }

    private void RepricingStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _repricingCts?.Cancel();
        RepricingStopBtn.IsEnabled = false;
    }
}
