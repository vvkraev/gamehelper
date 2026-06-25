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
    private readonly FracturingOrbService _fracturingCraft = new();
    private readonly OmenActivationService _omen = new();
    private CancellationTokenSource? _cts;
    private readonly ReforgeState _reforgeState = new();
    private List<Services.SaleRecord> _tradeHistory = [];

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
    private Dictionary<string, ScreenRect> _abyssItemRegions = new();
    private ScreenRect _abyssInventoryRect;
    private Dictionary<string, ScreenRect> _socketableItemRegions = new();
    private ScreenRect _socketableInventoryRect;
    private ScreenRect _sockSubRunesRect;
    private ScreenRect _sockSubKulguuranRect;
    private ScreenRect _sockSubSoulCoresRect;
    private ScreenRect _sockSubIdolsRect;
    private ScreenRect _sockSubAugmentsRect;
    private CancellationTokenSource? _nwScanCts;
    private List<RepricingTabConfig> _repricingTabs = [new RepricingTabConfig { Name = "Вкладка 1" }];
    private ScreenRect _repricingTraderOcrRect;
    private ScreenRect _repricingManageShopOcrRect;
    private ScreenRect _repricingGoodsRect;
    private CancellationTokenSource? _repricingCts;
    private readonly Services.RepricingService _repricingService = new();
    private bool _repricingStartStopHotkeyRegistered;
    private ScreenRect _chancingOocRect;
    private ScreenRect _chancingOmenRect;
    private List<ScreenRect> _chancingTabletCells = new();
    private CancellationTokenSource? _chancingCts;
    private readonly Services.ChancingService _chancingService = new();
    private bool _chancingStartStopHotkeyRegistered;
    private int _chancingStartStopVirtualKey;
    private int _chancingStartStopModifiers;
    private List<Services.ReferenceCategory> _referenceCategories = new();
    private int _repricingStartStopVirtualKey;
    private int _repricingStartStopModifiers;
    private ScreenRect _stashOcrSearchRect;
    private ScreenRect _reforgingBenchOcrSearchRect;
    private List<ScreenRect> _fullInventoryCells = new();
    private Dictionary<string, int> _catalystGoldPrices = new();
    private string? _activeCraftLogPath;

    private string _augOrbName = "Perfect Orb of Augmentation";
    private string _chaosOrbName = "Chaos Orb";
    private bool _isApplyingSettings;

    private static readonly string[] AugOrbChoices =
        ["Orb of Augmentation", "Greater Orb of Augmentation", "Perfect Orb of Augmentation"];
    private static readonly string[] ChaosOrbChoices =
        ["Chaos Orb", "Greater Chaos Orb", "Perfect Chaos Orb"];

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
                Dispatcher.BeginInvoke(RequestCancelAll);
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

            if (id >= GlobalHotkey.ChancingStartStopHotkeyIdBase && id < GlobalHotkey.ChancingStartStopHotkeyIdBase + 8)
            {
                handled = true;
                Dispatcher.BeginInvoke(ChancingToggleStartStop);
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
        UnregisterChancingStartStopHotkey();
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
        _isApplyingSettings = true;
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

        _augOrbName = !string.IsNullOrEmpty(s.AugOrbName) ? s.AugOrbName : "Perfect Orb of Augmentation";
        _chaosOrbName = !string.IsNullOrEmpty(s.ChaosOrbName) ? s.ChaosOrbName : "Chaos Orb";

        FractOrbBaseCostInput.Text = s.FractOrbBaseCostDiv > 0 ? s.FractOrbBaseCostDiv.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "6";
        FractOrbNInput.Text = s.FractOrbNAffixes > 0 ? s.FractOrbNAffixes.ToString() : "3";
        FractOrbSalePriceInput.Text = s.FractOrbTargetPriceDiv > 0 ? s.FractOrbTargetPriceDiv.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "0";
        // Cranium / Orb costs: prefer ninja prices; fallback to saved value
        var savedCranium = s.FractOrbCraniumCostDiv > 0 ? s.FractOrbCraniumCostDiv : 1.51m;
        var savedOrb = s.FractOrbOrbCostDiv > 0 ? s.FractOrbOrbCostDiv : 6.76m;
        FractOrbCraniumInput.Text = savedCranium.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        FractOrbOrbCostInput.Text = savedOrb.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        var mode = (s.CraftMode ?? "").Trim();
        if (mode.Contains("Экзаль", StringComparison.OrdinalIgnoreCase) || mode.Contains("Exalt", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 1;
        else if (mode.Contains("Ауг", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 2;
        else if (mode.Contains("Заточ", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 3;
        else if (mode.Contains("Фракт", StringComparison.OrdinalIgnoreCase))
            CraftModeCombo.SelectedIndex = 4;
        else
            CraftModeCombo.SelectedIndex = 0;
        RefreshCraftOrbCombo();

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

        _abyssInventoryRect = s.AbyssInventoryRect;
        AbyssInventoryInfo.Text = FormatRect(_abyssInventoryRect);
        _abyssItemRegions = s.AbyssItemRegions != null
            ? new Dictionary<string, ScreenRect>(s.AbyssItemRegions)
            : new();

        _socketableInventoryRect = s.SocketableInventoryRect;
        SocketableInventoryInfo.Text = FormatRect(_socketableInventoryRect);
        _sockSubRunesRect    = s.SocketableSubTabRunesRect;    SockSubRunesInfo.Text    = FormatRect(_sockSubRunesRect);
        _sockSubKulguuranRect = s.SocketableSubTabKulguuranRect; SockSubKulguuranInfo.Text = FormatRect(_sockSubKulguuranRect);
        _sockSubSoulCoresRect = s.SocketableSubTabSoulCoresRect; SockSubSoulCoresInfo.Text = FormatRect(_sockSubSoulCoresRect);
        _sockSubIdolsRect    = s.SocketableSubTabIdolsRect;    SockSubIdolsInfo.Text    = FormatRect(_sockSubIdolsRect);
        _sockSubAugmentsRect = s.SocketableSubTabAugmentsRect; SockSubAugmentsInfo.Text = FormatRect(_sockSubAugmentsRect);
        _socketableItemRegions = s.SocketableItemRegions != null
            ? new Dictionary<string, ScreenRect>(s.SocketableItemRegions)
            : new();
        // Perfect rune скрыты в UI — удаляем их регионы чтобы не попадали в нетворс
        foreach (var id in Services.StackableItemRegistry.Items
            .Where(i => i.Kind == Services.StackableItemKind.Rune &&
                        i.DisplayName.StartsWith("Perfect ", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Id)
            .ToList())
            _socketableItemRegions.Remove(id);

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
        RebuildAbyssPanel();
        RebuildSocketablePanel();
        _catalystGoldPrices = s.CatalystGoldPrices != null
            ? new Dictionary<string, int>(s.CatalystGoldPrices)
            : new Dictionary<string, int>();
        RebuildProfitTable();

        if (s.RepricingTabs is { Count: > 0 } savedTabs)
            _repricingTabs = savedTabs.Select(t => new RepricingTabConfig
            {
                Name = t.Name,
                TabRect = t.TabRect,
                Cells = t.Cells?.ToList(),
                RepeatCount = t.RepeatCount,
                RepeatIntervalMinutes = t.RepeatIntervalMinutes,
                PriceSteps = t.PriceSteps?.Select(s => new RepricingPriceStep
                {
                    FromPrice = s.FromPrice, Step = s.Step,
                    StrictlyGreater = s.StrictlyGreater, Enabled = s.Enabled
                }).ToList()
            }).ToList();
        else if (s.RepricingCells is { Count: > 0 } legacyCells)
            _repricingTabs = [new RepricingTabConfig { Name = "Вкладка 1", Cells = legacyCells.ToList() }];
        else
            _repricingTabs = [new RepricingTabConfig { Name = "Вкладка 1" }];
        _repricingTraderOcrRect = s.RepricingTraderOcrSearchRect;
        RepricingTraderOcrInfo.Text = _repricingTraderOcrRect.Width > 0 ? FormatRect(_repricingTraderOcrRect) : "не задана";
        RepricingTraderOcrTextBox.Text = s.RepricingTraderOcrText;
        RepricingTraderOpenDelayBox.Text = s.RepricingTraderOpenDelayMs.ToString();
        _repricingManageShopOcrRect = s.RepricingManageShopOcrSearchRect;
        RepricingManageShopOcrInfo.Text = _repricingManageShopOcrRect.Width > 0 ? FormatRect(_repricingManageShopOcrRect) : "не задана";
        RepricingManageShopOcrTextBox.Text = s.RepricingManageShopOcrText;

        _repricingGoodsRect = s.RepricingGoodsRect;
        RepricingGoodsInfo.Text = _repricingGoodsRect.Width > 0 ? FormatRect(_repricingGoodsRect) : "не задана";
        RebuildRepricingTabsPanel();
        RepricingPostClickDelayBox.Text    = s.RepricingPostClickDelayMs.ToString();
        RepricingHoverSettleBox.Text       = s.RepricingHoverSettleMs.ToString();
        RepricingRepeatCountBox.Text       = s.RepricingRepeatCount.ToString();
        RepricingRepeatIntervalBox.Text    = s.RepricingRepeatIntervalMinutes.ToString();

        _repricingStartStopVirtualKey = s.RepricingStartStopVirtualKey;
        _repricingStartStopModifiers  = s.RepricingStartStopModifiers;
        UpdateRepricingStartStopHotkeyDisplay();
        RegisterRepricingStartStopHotkey();

        _chancingOocRect = s.ChancingOocRect;
        ChancingOocRectInfo.Text = _chancingOocRect.Width > 0 ? FormatRect(_chancingOocRect) : "не задана";
        _chancingOmenRect = s.ChancingOmenRect;
        _chancingTabletCells = s.ChancingTabletCells is { Count: > 0 } cc ? cc.ToList() : new List<ScreenRect>();
        ChancingTabletCellsInfo.Text = _chancingTabletCells.Count > 0 ? $"{_chancingTabletCells.Count} ячеек" : "не задана";
        var inputBase = s.ChancingInputBase ?? "Irradiated Tablet";
        foreach (System.Windows.Controls.ComboBoxItem item in ChancingInputBaseCombo.Items)
        {
            if (item.Content?.ToString() == inputBase)
            { ChancingInputBaseCombo.SelectedItem = item; break; }
        }
        ChancingUseOmenCheckBox.IsChecked = s.ChancingUseOmen;
        ChancingUpdateOmenVisibility();
        ChancingMouseDelayBox.Text       = s.ChancingMouseDelayMs.ToString();
        ChancingPostApplyWaitBox.Text    = s.ChancingPostApplyWaitMs > 0 ? s.ChancingPostApplyWaitMs.ToString() : "600";
        _chancingStartStopVirtualKey     = s.ChancingStartStopVirtualKey;
        _chancingStartStopModifiers      = s.ChancingStartStopModifiers;
        UpdateChancingStartStopHotkeyDisplay();
        RegisterChancingStartStopHotkey();

        TradeSessionIdBox.Text = s.TradeSessionId ?? "";
        TradeHistoryLeagueBox.Text = string.IsNullOrWhiteSpace(s.TradeHistoryLeague)
            ? (string.IsNullOrWhiteSpace(s.PoeNinjaLeague) ? "Runes of Aldur" : s.PoeNinjaLeague)
            : s.TradeHistoryLeague;
        _tradeHistory = Services.TradeHistoryService.LoadFromFile();
        RebuildTradeHistoryGrid();

        RefLoadCategories();
        _isApplyingSettings = false;
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
            AugOrbName = _augOrbName,
            ChaosOrbName = _chaosOrbName,
            FractOrbBaseCostDiv = decimal.TryParse(FractOrbBaseCostInput.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fcb) ? fcb : 6m,
            FractOrbCraniumCostDiv = decimal.TryParse(FractOrbCraniumInput.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fcc) ? fcc : 0m,
            FractOrbOrbCostDiv = decimal.TryParse(FractOrbOrbCostInput.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fco) ? fco : 0m,
            FractOrbNAffixes = int.TryParse(FractOrbNInput.Text.Trim(), out var fni) && fni >= 1 ? fni : 3,
            FractOrbTargetPriceDiv = decimal.TryParse(FractOrbSalePriceInput.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fsp) ? fsp : 0m,
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
            AbyssInventoryRect = _abyssInventoryRect,
            AbyssItemRegions = _abyssItemRegions.Count > 0
                ? new Dictionary<string, ScreenRect>(_abyssItemRegions)
                : null,
            SocketableInventoryRect = _socketableInventoryRect,
            SocketableSubTabRunesRect    = _sockSubRunesRect,
            SocketableSubTabKulguuranRect = _sockSubKulguuranRect,
            SocketableSubTabSoulCoresRect = _sockSubSoulCoresRect,
            SocketableSubTabIdolsRect    = _sockSubIdolsRect,
            SocketableSubTabAugmentsRect = _sockSubAugmentsRect,
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
            RepricingTabs = _repricingTabs.Count > 0
                ? _repricingTabs.Select(t => new RepricingTabConfig
                {
                    Name = t.Name, TabRect = t.TabRect, Cells = t.Cells?.ToList(),
                    RepeatCount = t.RepeatCount, RepeatIntervalMinutes = t.RepeatIntervalMinutes,
                    PriceSteps = t.PriceSteps?.Select(s => new RepricingPriceStep
                    {
                        FromPrice = s.FromPrice, Step = s.Step,
                        StrictlyGreater = s.StrictlyGreater, Enabled = s.Enabled
                    }).ToList()
                }).ToList()
                : null,
            RepricingTraderOcrSearchRect        = _repricingTraderOcrRect,
            RepricingTraderOcrText              = RepricingTraderOcrTextBox.Text.Trim(),
            RepricingTraderOpenDelayMs          = RfParseInt(RepricingTraderOpenDelayBox.Text, 1000),
            RepricingManageShopOcrSearchRect    = _repricingManageShopOcrRect,
            RepricingManageShopOcrText          = RepricingManageShopOcrTextBox.Text.Trim(),
            RepricingGoodsRect                  = _repricingGoodsRect,
            RepricingPostClickDelayMs      = RfParseInt(RepricingPostClickDelayBox.Text, 300),
            RepricingHoverSettleMs         = RfParseInt(RepricingHoverSettleBox.Text, 120),
            RepricingRepeatCount           = RfParseInt(RepricingRepeatCountBox.Text, 1),
            RepricingRepeatIntervalMinutes = RfParseInt(RepricingRepeatIntervalBox.Text, 5),
            RepricingStartStopVirtualKey = _repricingStartStopVirtualKey,
            RepricingStartStopModifiers  = _repricingStartStopModifiers,
        };
        s.ChancingOocRect          = _chancingOocRect;
        s.ChancingOmenRect         = _chancingOmenRect;
        s.ChancingTabletCells      = _chancingTabletCells.Count > 0 ? _chancingTabletCells : null;
        s.ChancingInputBase        = (ChancingInputBaseCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Irradiated Tablet";
        s.ChancingUseOmen          = ChancingUseOmenCheckBox.IsChecked == true;
        s.ChancingMouseDelayMs     = RfParseInt(ChancingMouseDelayBox.Text, 120);
        s.ChancingPostApplyWaitMs  = RfParseInt(ChancingPostApplyWaitBox.Text, 600);
        s.ChancingStartStopVirtualKey = _chancingStartStopVirtualKey;
        s.ChancingStartStopModifiers  = _chancingStartStopModifiers;
        s.TradeSessionId      = TradeSessionIdBox.Text.Trim();
        s.TradeHistoryLeague  = TradeHistoryLeagueBox.Text.Trim();
        _reforgeState.ApplyToSettings(s);
        SettingsStore.Save(s);
    }

    private void UpdateCraftConditionSummary()
    {
        CraftConditionSummary.Text = CraftConditionEvaluator.FormatSummary(_craftPlan);
    }


    private void RefreshCraftOrbCombo()
    {
        if (MainCraftOrbCombo is null) return;
        var mode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        var isAug = mode.Contains("Ауг", StringComparison.OrdinalIgnoreCase)
                 || mode.Contains("+", StringComparison.OrdinalIgnoreCase);
        var isChaos = mode.Contains("Хаос", StringComparison.OrdinalIgnoreCase);
        if (!isAug && !isChaos)
        {
            MainCraftOrbCombo.ItemsSource = Array.Empty<string>();
            MainCraftOrbCombo.IsEnabled = false;
            return;
        }
        var choices = isAug ? AugOrbChoices : ChaosOrbChoices;
        var current = isAug ? _augOrbName : _chaosOrbName;
        MainCraftOrbCombo.IsEnabled = true;
        MainCraftOrbCombo.ItemsSource = choices;
        var idx = Array.IndexOf(choices, current);
        MainCraftOrbCombo.SelectedIndex = idx >= 0 ? idx : choices.Length - 1;
    }

    private void CraftModeCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshCraftOrbCombo();
        if (FractOrbEvalPanel is null) return;  // FractOrbEvalPanel определён после CraftModeCombo в XAML — защита от порядка инициализации
        var mode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        var isFract = mode.Contains("Фракт", StringComparison.OrdinalIgnoreCase);
        FractOrbEvalPanel.Visibility = isFract ? Visibility.Visible : Visibility.Collapsed;
        if (isFract)
        {
            FillFractOrbPricesFromNinja();
            UpdateFractOrbEvaluation();
        }
    }

    private void FillFractOrbPricesFromNinja()
    {
        var cranium = Services.PoeNinjaPriceService.GetPrice("preserved cranium")?.DivineValue;
        if (cranium is > 0 && FractOrbCraniumInput.Text is "0" or "1.51")
            FractOrbCraniumInput.Text = cranium.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        var orb = Services.PoeNinjaPriceService.GetPrice("fracturing orb")?.DivineValue;
        if (orb is > 0 && FractOrbOrbCostInput.Text is "0" or "6.76")
            FractOrbOrbCostInput.Text = orb.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FractOrbInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateFractOrbEvaluation();

    private void UpdateFractOrbEvaluation()
    {
        if (FractOrbEvalResult is null) return;

        if (!decimal.TryParse(FractOrbBaseCostInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var baseCost) || baseCost < 0)
        { FractOrbEvalResult.Text = "Стоимость предмета-основы: неверное значение."; return; }

        if (!decimal.TryParse(FractOrbCraniumInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var cranium) || cranium < 0)
        { FractOrbEvalResult.Text = "Стоимость Preserved Cranium: неверное значение."; return; }

        if (!decimal.TryParse(FractOrbOrbCostInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var orbCost) || orbCost < 0)
        { FractOrbEvalResult.Text = "Стоимость Fracturing Orb: неверное значение."; return; }

        if (!int.TryParse(FractOrbNInput.Text.Trim(), out var n) || n < 1)
        { FractOrbEvalResult.Text = "N (аффиксов): укажите целое ≥ 1."; return; }

        decimal.TryParse(FractOrbSalePriceInput.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var salePrice);

        var perAttempt = baseCost + cranium + orbCost;
        var expected = n * perAttempt;
        var prob = 100m / n;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"За попытку:  {baseCost:F2} + {cranium:F2} + {orbCost:F2} = {perAttempt:F2} div");
        sb.AppendLine($"Вероятность: 1/{n} ({prob:F1}%)  →  ожидание {n} попыток");
        sb.AppendLine($"Ожидаемые затраты: {expected:F2} div");

        if (salePrice > 0)
        {
            var profit = salePrice - expected;
            var sign = profit >= 0 ? "+" : "";
            sb.AppendLine($"Цена продажи:  {salePrice:F2} div");
            sb.Append($"Прибыль:       {sign}{profit:F2} div  {(profit >= 0 ? "✓" : "✗")}");
        }

        FractOrbEvalResult.Text = sb.ToString();
        FractOrbEvalResult.Foreground = salePrice > 0
            ? (salePrice - expected >= 0
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkRed)
            : System.Windows.Media.Brushes.Black;
    }

    private void MainCraftOrbCombo_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isApplyingSettings) return;
        var orb = MainCraftOrbCombo.SelectedItem as string ?? "";
        if (string.IsNullOrEmpty(orb)) return;
        var mode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        if (mode.Contains("Ауг", StringComparison.OrdinalIgnoreCase) || mode.Contains("+", StringComparison.OrdinalIgnoreCase))
            _augOrbName = orb;
        else if (mode.Contains("Хаос", StringComparison.OrdinalIgnoreCase))
            _chaosOrbName = orb;
        SaveSettings();
    }

    private void CraftConditionBtn_OnClick(object sender, RoutedEventArgs e)
    {
        AffixLibrary.ReloadFromDisk();
        _affixEntries = AffixLibrary.GetEntries().ToList();
        var editCopy = SettingsStore.CloneCraftConditionPlan(_craftPlan);
        if (string.IsNullOrEmpty(editCopy.CraftOrbName))
            editCopy.CraftOrbName = MainCraftOrbCombo.SelectedItem as string ?? "";
        var dlg = new CraftConditionWindow(editCopy, _affixEntries, Services.AffixStatsScanner.Current) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;
        _craftPlan = editCopy;
        // Sync orb name from plan back to main window
        if (!string.IsNullOrEmpty(_craftPlan.CraftOrbName))
        {
            var mode2 = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
            if (mode2.Contains("Ауг", StringComparison.OrdinalIgnoreCase) || mode2.Contains("+", StringComparison.OrdinalIgnoreCase))
                _augOrbName = _craftPlan.CraftOrbName;
            else if (mode2.Contains("Хаос", StringComparison.OrdinalIgnoreCase))
                _chaosOrbName = _craftPlan.CraftOrbName;
            RefreshCraftOrbCombo();
        }
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
            else if (cm.Contains("Фракт", StringComparison.OrdinalIgnoreCase))
                CraftModeCombo.SelectedIndex = 4;
            else
                CraftModeCombo.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(_craftPlan.CraftOrbName))
            {
                if (cm.Contains("Ауг", StringComparison.OrdinalIgnoreCase) || cm.Contains("+", StringComparison.OrdinalIgnoreCase))
                    _augOrbName = _craftPlan.CraftOrbName;
                else if (cm.Contains("Хаос", StringComparison.OrdinalIgnoreCase))
                    _chaosOrbName = _craftPlan.CraftOrbName;
                RefreshCraftOrbCombo();
            }
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
        var isFractOrb = mode.Contains("Фракт", StringComparison.OrdinalIgnoreCase);

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

        if (isFractOrb && GetCurrencyRect("Fracturing Orb") is null)
        {
            MessageBox.Show(this, "Задайте область Fracturing Orb в «Настройки областей → Currency».", "Область орба", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!isAugAnnul && !isExalt && !isFractOrb && GetCurrencyRect("Chaos Orb", "Greater Chaos Orb", "Perfect Chaos Orb") is null)
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

        _fracturingCraft.MouseActionDelayMs = mouseDelay;
        _fracturingCraft.ClipboardDelayMs = clipboardDelay;
        _fracturingCraft.HoverSettleBeforeClipboardMs = hoverSettle;
        _fracturingCraft.TraceInputToLog = trace;
        _fracturingCraft.StepConfirmAsync = null;

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
            _fracturingCraft.StepConfirmAsync = confirm;
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
                            : isFractOrb
                                ? await _fracturingCraft.PrecheckAsync(item, _craftPlan, progress, _cts.Token)
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

                    if ((isAugAnnul || isExalt) && !isFractOrb && pre.ParsedItem is null)
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
                    else if (isFractOrb)
                    {
                        var fracOrb = GetCurrencyRect("Fracturing Orb") ?? default;
                        craftFile ??= CraftRunFileLog.Begin(fracOrb, cells[0], maxOps, conditionSummary, cells, "Fracturing Orb");
                        _activeCraftLogPath = craftFile.WipPath;
                        craftFile.SetCurrentCell(ci + 1, cells.Count);

                        var cr = await _fracturingCraft.RunAsync(
                            fracOrb,
                            item,
                            _craftPlan,
                            conditionSummary,
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
                        craftFile ??= CraftRunFileLog.Begin(orb, cells[0], maxOps, conditionSummary, cells, _chaosOrbName);
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
                            cells,
                            _augOrbName);
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
                        if (isFractOrb)
                        {
                            SessionLogger.Info($"Нужный аффикс зафиксирован в ячейке {ci + 1} — сессия Fracturing Orb завершена.");
                            break;
                        }

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
            _fracturingCraft.StepConfirmAsync = null;
            craftFile?.Dispose();
            _ = Services.AffixStatsScanner.ScanNewLogsAsync()
                .ContinueWith(_ => Dispatcher.Invoke(RefLoadCategories), TaskScheduler.Default);
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
        e.Handled = true;
        RequestCancelAll();
    }

    private void RequestCraftCancel() => RequestCancelAll();

    private void RequestCancelAll()
    {
        _cts?.Cancel();
        _nwScanCts?.Cancel();
        _repricingCts?.Cancel();
        _rfCts?.Cancel();
        _rfScanCts?.Cancel();
        _autoRfCts?.Cancel();
        _chancingCts?.Cancel();
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
            RebuildAbyssPanel();
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
        RebuildAbyssPanel();
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

    // ── Abyss ─────────────────────────────────────────────────────────────

    private void PickAbyssInventoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _abyssInventoryRect = region;
        AbyssInventoryInfo.Text = FormatRect(region);
        SaveSettings();
    }

    // Фиксированный список предметов вкладки Abyss — не требует сканирования реестра.
    private static readonly (string Id, string DisplayName, string? Group)[] AbyssKnownItems =
    [
        ("ancient_jawbone",              "Ancient Jawbone",              "Abyssal Bones"),
        ("gnawed_jawbone",               "Gnawed Jawbone",               "Abyssal Bones"),
        ("preserved_jawbone",            "Preserved Jawbone",            "Abyssal Bones"),
        ("ancient_collarbone",           "Ancient Collarbone",           "Abyssal Bones"),
        ("gnawed_collarbone",            "Gnawed Collarbone",            "Abyssal Bones"),
        ("preserved_collarbone",         "Preserved Collarbone",         "Abyssal Bones"),
        ("ancient_rib",                  "Ancient Rib",                  "Abyssal Bones"),
        ("gnawed_rib",                   "Gnawed Rib",                   "Abyssal Bones"),
        ("preserved_rib",                "Preserved Rib",                "Abyssal Bones"),
        ("preserved_cranium",            "Preserved Cranium",            "Abyssal Bones"),
        ("omen_of_light",                "Omen of Light",                "Omens"),
        ("omen_of_abyssal_echoes",       "Omen of Abyssal Echoes",       "Omens"),
        ("omen_of_sinistral_necromancy", "Omen of Sinistral Necromancy", "Omens"),
        ("omen_of_dextral_necromancy",   "Omen of Dextral Necromancy",   "Omens"),
        ("omen_of_putrefaction",         "Omen of Putrefaction",         "Omens"),
    ];

    private void RebuildAbyssPanel()
    {
        AbyssItemRegionsPanel.Children.Clear();

        string? currentGroup = null;
        foreach (var item in AbyssKnownItems)
        {
            if (item.Group != currentGroup)
            {
                if (currentGroup != null)
                    AbyssItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                        { Margin = new System.Windows.Thickness(0, 4, 0, 4) });
                AbyssItemRegionsPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = item.Group,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Margin = new System.Windows.Thickness(0, 0, 0, 4),
                });
                currentGroup = item.Group;
            }

            _abyssItemRegions.TryGetValue(item.Id, out var rect);
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
                Margin  = new System.Windows.Thickness(0, 0, 4, 0),
                Tag = (item.Id, infoText),
            };
            btn.Click += AbyssItemPickBtn_Click;

            var clearBtn = new System.Windows.Controls.Button
            {
                Content = "✕",
                Padding = new System.Windows.Thickness(6, 4, 6, 4),
                Tag = (item.Id, infoText),
            };
            clearBtn.Click += AbyssItemClearBtn_Click;

            var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(240) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = item.DisplayName,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            System.Windows.Controls.Grid.SetColumn(label, 0);
            System.Windows.Controls.Grid.SetColumn(infoText, 1);
            System.Windows.Controls.Grid.SetColumn(btn, 2);
            System.Windows.Controls.Grid.SetColumn(clearBtn, 3);

            row.Children.Add(label);
            row.Children.Add(infoText);
            row.Children.Add(btn);
            row.Children.Add(clearBtn);
            AbyssItemRegionsPanel.Children.Add(row);
        }
    }

    private void AbyssItemPickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string id, System.Windows.Controls.TextBlock infoBlock)) return;
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        _abyssItemRegions[id] = region;
        infoBlock.Text = FormatRect(region);
        SaveSettings();
    }

    private void AbyssItemClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string id, System.Windows.Controls.TextBlock infoBlock)) return;
        _abyssItemRegions.Remove(id);
        infoBlock.Text = FormatRect(default);
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

    private void PickSockSubBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var tag = btn.Tag as string ?? "";
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        switch (tag)
        {
            case "Runes":     _sockSubRunesRect    = region; SockSubRunesInfo.Text    = FormatRect(region); break;
            case "Kulguuran": _sockSubKulguuranRect = region; SockSubKulguuranInfo.Text = FormatRect(region); break;
            case "SoulCores": _sockSubSoulCoresRect = region; SockSubSoulCoresInfo.Text = FormatRect(region); break;
            case "Idols":     _sockSubIdolsRect    = region; SockSubIdolsInfo.Text    = FormatRect(region); break;
            case "Augments":  _sockSubAugmentsRect = region; SockSubAugmentsInfo.Text = FormatRect(region); break;
        }
        SaveSettings();
    }

    private void ClearSockSubBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var tag = btn.Tag as string ?? "";
        switch (tag)
        {
            case "Runes":     _sockSubRunesRect    = default; SockSubRunesInfo.Text    = "не задана"; break;
            case "Kulguuran": _sockSubKulguuranRect = default; SockSubKulguuranInfo.Text = "не задана"; break;
            case "SoulCores": _sockSubSoulCoresRect = default; SockSubSoulCoresInfo.Text = "не задана"; break;
            case "Idols":     _sockSubIdolsRect    = default; SockSubIdolsInfo.Text    = "не задана"; break;
            case "Augments":  _sockSubAugmentsRect = default; SockSubAugmentsInfo.Text = "не задана"; break;
        }
        SaveSettings();
    }

    private void SocketableItemClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not (string id, System.Windows.Controls.TextBlock infoBlock)) return;
        _socketableItemRegions.Remove(id);
        infoBlock.Text = FormatRect(default);
        SaveSettings();
    }

    /// <summary>Возвращает имя под-вкладки Socketable Stash для предмета.</summary>
    private static string GetSocketableSubTab(Services.StackableItemType? item)
    {
        if (item == null) return "Runes";
        if (item.Kind == Services.StackableItemKind.SoulCore) return "Soul Cores";
        if (item.Kind == Services.StackableItemKind.AncientAugment) return "Ancient Augments";
        if (item.Kind == Services.StackableItemKind.Rune)
        {
            var name = item.DisplayName;
            if (name.StartsWith("Ancient Rune of", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Warding ", StringComparison.OrdinalIgnoreCase))
                return "Kulguuran Runes";
            return "Runes";
        }
        return "Other";
    }

    private void RebuildSocketablePanel()
    {
        SocketableItemRegionsPanel.Children.Clear();

        var all = Services.StackableItemRegistry.Items
            .Where(i => i.Kind == Services.StackableItemKind.Rune
                     || i.Kind == Services.StackableItemKind.SoulCore
                     || i.Kind == Services.StackableItemKind.AncientAugment)
            .ToList();

        if (all.Count == 0)
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

        // Порядок секций соответствует под-вкладкам в игре
        var sections = new (string Header, IEnumerable<Services.StackableItemType> Items)[]
        {
            ("Runes — Greater",        all.Where(i => i.Kind == Services.StackableItemKind.Rune && StartsWithCI(i, "Greater "))),
            ("Runes — Base",           all.Where(i => i.Kind == Services.StackableItemKind.Rune
                                                    && !StartsWithCI(i, "Perfect ") && !StartsWithCI(i, "Greater ")
                                                    && !StartsWithCI(i, "Lesser ")  && !StartsWithCI(i, "Ancient ")
                                                    && !StartsWithCI(i, "Warding ")
                                                    && i.DisplayName.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase))),
            ("Runes — Lesser",         all.Where(i => i.Kind == Services.StackableItemKind.Rune && StartsWithCI(i, "Lesser "))),
            ("Runes — Named / Unique", all.Where(i => i.Kind == Services.StackableItemKind.Rune
                                                    && !StartsWithCI(i, "Perfect ") && !StartsWithCI(i, "Greater ")
                                                    && !StartsWithCI(i, "Lesser ")  && !StartsWithCI(i, "Ancient ")
                                                    && !StartsWithCI(i, "Warding ")
                                                    && !i.DisplayName.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase))),
            ("Kulguuran Runes",        all.Where(i => i.Kind == Services.StackableItemKind.Rune
                                                    && (StartsWithCI(i, "Ancient Rune of") || StartsWithCI(i, "Warding ")))),
            ("Soul Cores — Standard",  all.Where(i => i.Kind == Services.StackableItemKind.SoulCore && StartsWithCI(i, "Soul Core of "))),
            ("Soul Cores — Named",     all.Where(i => i.Kind == Services.StackableItemKind.SoulCore
                                                    && !StartsWithCI(i, "Soul Core of ")
                                                    && i.DisplayName.Contains("Soul Core", StringComparison.OrdinalIgnoreCase))),
            ("Soul Cores — Other",     all.Where(i => i.Kind == Services.StackableItemKind.SoulCore
                                                    && !i.DisplayName.Contains("Soul Core", StringComparison.OrdinalIgnoreCase))),
            ("Ancient Augments",       all.Where(i => i.Kind == Services.StackableItemKind.AncientAugment)),
        };

        bool first = true;
        foreach (var (header, items) in sections)
        {
            var list = items.OrderBy(i => i.DisplayName).ToList();
            if (list.Count == 0) continue;

            if (!first)
                SocketableItemRegionsPanel.Children.Add(new System.Windows.Controls.Separator
                    { Margin = new System.Windows.Thickness(0, 4, 0, 4) });
            first = false;

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
                    Margin  = new System.Windows.Thickness(0, 0, 4, 0),
                    Tag = (item.Id, infoText),
                };
                btn.Click += SocketableItemPickBtn_Click;

                var clearBtn = new System.Windows.Controls.Button
                {
                    Content = "✕",
                    Padding = new System.Windows.Thickness(6, 4, 6, 4),
                    Tag = (item.Id, infoText),
                };
                clearBtn.Click += SocketableItemClearBtn_Click;

                var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(240) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

                var label = new System.Windows.Controls.TextBlock
                {
                    Text = item.DisplayName,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                };
                System.Windows.Controls.Grid.SetColumn(label, 0);
                System.Windows.Controls.Grid.SetColumn(infoText, 1);
                System.Windows.Controls.Grid.SetColumn(btn, 2);
                System.Windows.Controls.Grid.SetColumn(clearBtn, 3);

                row.Children.Add(label);
                row.Children.Add(infoText);
                row.Children.Add(btn);
                row.Children.Add(clearBtn);
                SocketableItemRegionsPanel.Children.Add(row);
            }
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
                    ItemName: registry.FirstOrDefault(r => r.Id == kv.Key)?.DisplayName
                              ?? AbyssKnownItems.FirstOrDefault(a => a.Id == kv.Key).DisplayName
                              ?? kv.Key,
                    Area: kv.Value))
                .ToList()
                ?? new List<(string, ScreenRect)>();
            return new Services.NwGroupDef(name, tabRect, items);
        }

        // Socketable: 5 под-вкладок, каждая сначала кликает основную вкладку (NavTabRect)
        Services.NwGroupDef MakeSockGroup(string subTabName, ScreenRect subTabRect)
        {
            var items = _socketableItemRegions
                .Where(kv => kv.Value.Width > 0 && kv.Value.Height > 0)
                .Select(kv =>
                {
                    var entry = registry.FirstOrDefault(r => r.Id == kv.Key);
                    return (entry, kv.Key, kv.Value);
                })
                .Where(t => GetSocketableSubTab(t.entry) == subTabName)
                .Select(t => (
                    ItemName: t.entry?.DisplayName ?? t.Key,
                    Area: t.Value))
                .ToList();
            return new Services.NwGroupDef($"Socketable/{subTabName}", subTabRect, items)
                { NavTabRect = _socketableInventoryRect };
        }

        var groups = new[]
        {
            MakeGroup("Currency",   _currencyInventoryRegion ?? default, _currencyItemRegions),
            MakeGroup("Ritual",     _ritualInventoryRegion   ?? default, _ritualItemRegions),
            MakeGroup("Breach",     _breachInventoryRect,    _breachCatalystRegions),
            MakeGroup("Delirium",   _deliriumInventoryRect,  _deliriumItemRegions),
            MakeGroup("Abyss",      _abyssInventoryRect,     _abyssItemRegions),
            MakeSockGroup("Runes",           _sockSubRunesRect),
            MakeSockGroup("Kulguuran Runes", _sockSubKulguuranRect),
            MakeSockGroup("Soul Cores",      _sockSubSoulCoresRect),
            MakeSockGroup("Idols",           _sockSubIdolsRect),
            MakeSockGroup("Ancient Augments", _sockSubAugmentsRect),
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
                await Services.GameInputLock.WaitAsync(ct);
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
                    // Закрываем открытые окна игры
                    Native.Win32Input.PressKey(0x33);
                    await Task.Delay(150, CancellationToken.None);
                }
                finally
                {
                    Services.GameInputLock.Release();
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

    private void RebuildRepricingTabsPanel()
    {
        RepricingTabsPanel.Children.Clear();

        for (var i = 0; i < _repricingTabs.Count; i++)
        {
            var tab = _repricingTabs[i];
            var idx = i;

            var border = new System.Windows.Controls.Border
            {
                BorderBrush     = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new System.Windows.Thickness(1),
                Margin  = new System.Windows.Thickness(0, 0, 0, 8),
                Padding = new System.Windows.Thickness(8)
            };

            var panel = new System.Windows.Controls.StackPanel();

            // Строка: имя + кнопка удаления
            var headerGrid = new System.Windows.Controls.Grid();
            headerGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                { Width = System.Windows.GridLength.Auto });

            var nameBox = new System.Windows.Controls.TextBox
            {
                Text   = tab.Name,
                Margin = new System.Windows.Thickness(0, 0, 8, 0)
            };
            nameBox.TextChanged += (_, _) =>
            {
                if (idx < _repricingTabs.Count)
                    _repricingTabs[idx].Name = nameBox.Text;
            };
            System.Windows.Controls.Grid.SetColumn(nameBox, 0);

            var removeBtn = new System.Windows.Controls.Button
            {
                Content = "✕",
                Padding = new System.Windows.Thickness(6, 2, 6, 2)
            };
            removeBtn.Click += (_, _) =>
            {
                _repricingTabs.RemoveAt(idx);
                RebuildRepricingTabsPanel();
                SaveSettings();
            };
            System.Windows.Controls.Grid.SetColumn(removeBtn, 1);
            headerGrid.Children.Add(nameBox);
            headerGrid.Children.Add(removeBtn);
            panel.Children.Add(headerGrid);

            // Строка: область вкладки
            var tabAreaRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new System.Windows.Thickness(0, 6, 0, 0)
            };
            var tabAreaInfo = new System.Windows.Controls.TextBlock
            {
                Text = "Вкладка: " + (tab.TabRect.Width > 0 ? $"({tab.TabRect.X},{tab.TabRect.Y})" : "не задана"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                MinWidth = 200
            };
            var tabAreaBtn = new System.Windows.Controls.Button
            {
                Content = "Задать…",
                Padding = new System.Windows.Thickness(6, 2, 6, 2),
                Margin  = new System.Windows.Thickness(8, 0, 0, 0)
            };
            tabAreaBtn.Click += (_, _) =>
            {
                var picker = new RegionPickerWindow(1, 1) { Owner = this };
                if (picker.ShowDialog() != true || picker.SelectedRegion is not { } r) return;
                _repricingTabs[idx].TabRect = r;
                tabAreaInfo.Text = "Вкладка: " + $"({r.X},{r.Y})";
                SaveSettings();
            };
            tabAreaRow.Children.Add(tabAreaInfo);
            tabAreaRow.Children.Add(tabAreaBtn);
            panel.Children.Add(tabAreaRow);

            // Строка: сетка ячеек
            var cellsRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new System.Windows.Thickness(0, 4, 0, 0)
            };
            var cellsList = (IReadOnlyList<ScreenRect>)(tab.Cells ?? []);
            var cellsInfo = new System.Windows.Controls.TextBlock
            {
                Text = "Сетка: " + FormatItemCellsSummary(cellsList),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                MinWidth = 200
            };
            var cellsBtn = new System.Windows.Controls.Button
            {
                Content = "Задать…",
                Padding = new System.Windows.Thickness(6, 2, 6, 2),
                Margin  = new System.Windows.Thickness(8, 0, 0, 0)
            };
            cellsBtn.Click += (_, _) =>
            {
                var dimDlg = new ItemGridDimensionsDialog { Owner = this };
                if (dimDlg.ShowDialog() != true) return;
                var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
                if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region) return;
                _repricingTabs[idx].Cells = picker.SelectedCells is { Count: > 0 } c ? c.ToList() : [region];
                cellsInfo.Text = "Сетка: " + FormatItemCellsSummary(_repricingTabs[idx].Cells!);
                SaveSettings();
            };
            cellsRow.Children.Add(cellsInfo);
            cellsRow.Children.Add(cellsBtn);
            panel.Children.Add(cellsRow);

            // Строка: кнопка индивидуальных настроек
            var settingsBtn = new System.Windows.Controls.Button
            {
                Content = "Настройки повторений и цены…",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new System.Windows.Thickness(8, 3, 8, 3),
                Margin  = new System.Windows.Thickness(0, 6, 0, 0)
            };
            settingsBtn.Click += (_, _) =>
            {
                var globalCount    = RfParseInt(RepricingRepeatCountBox.Text, 1);
                var globalInterval = RfParseInt(RepricingRepeatIntervalBox.Text, 5);
                var dlg = new RepricingTabSettingsWindow(_repricingTabs[idx], globalCount, globalInterval)
                    { Owner = this };
                if (dlg.ShowDialog() != true) return;
                dlg.ApplyTo(_repricingTabs[idx]);
                SaveSettings();
            };
            panel.Children.Add(settingsBtn);

            border.Child = panel;
            RepricingTabsPanel.Children.Add(border);
        }
    }

    private void RepricingTraderOcrPickBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow(1, 1) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } r) return;
        _repricingTraderOcrRect = r;
        RepricingTraderOcrInfo.Text = FormatRect(r);
        SaveSettings();
    }

    private void RepricingGoodsPickBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow(1, 1) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } r) return;
        _repricingGoodsRect = r;
        RepricingGoodsInfo.Text = FormatRect(r);
        SaveSettings();
    }

    private void RepricingManageShopOcrPickBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new RegionPickerWindow(1, 1) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } r) return;
        _repricingManageShopOcrRect = r;
        RepricingManageShopOcrInfo.Text = FormatRect(r);
        SaveSettings();
    }

    private void AddRepricingTabBtn_Click(object sender, RoutedEventArgs e)
    {
        _repricingTabs.Add(new RepricingTabConfig { Name = $"Вкладка {_repricingTabs.Count + 1}" });
        RebuildRepricingTabsPanel();
        SaveSettings();
    }

    private async void RepricingStartBtn_Click(object sender, RoutedEventArgs e)
    {
        var hasAnyCells = _repricingTabs.Any(t => t.Cells is { Count: > 0 });
        if (!hasAnyCells)
        {
            MessageBox.Show(this, "Задайте сетку ячеек хотя бы для одной вкладки.", "Переоценка",
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
        var repeatCount     = RfParseInt(RepricingRepeatCountBox.Text, 1);
        var intervalMinutes = RfParseInt(RepricingRepeatIntervalBox.Text, 5);
        SaveSettings();
        MinimizeToTrayOnStart();

        var tabs = _repricingTabs
            .Where(t => t.Cells is { Count: > 0 })
            .Select(t => new RepricingTabConfig
            {
                Name = t.Name, TabRect = t.TabRect, Cells = t.Cells?.ToList(),
                RepeatCount = t.RepeatCount, RepeatIntervalMinutes = t.RepeatIntervalMinutes,
                PriceSteps = t.PriceSteps?.Select(s => new RepricingPriceStep
                {
                    FromPrice = s.FromPrice, Step = s.Step,
                    StrictlyGreater = s.StrictlyGreater, Enabled = s.Enabled
                }).ToList()
            })
            .ToList();
        var traderOcrRect      = _repricingTraderOcrRect;
        var traderOcrText      = RepricingTraderOcrTextBox.Text.Trim();
        var traderOpenDelay    = RfParseInt(RepricingTraderOpenDelayBox.Text, 1000);
        var manageShopOcrRect  = _repricingManageShopOcrRect;
        var manageShopOcrText  = RepricingManageShopOcrTextBox.Text.Trim();
        var goodsRect          = _repricingGoodsRect;
        var mouseMs            = _repricingService.MouseActionDelayMs;
        var postClickMs        = _repricingService.PostClickDelayMs;
        var dispatcher         = Dispatcher;
        var progress           = new Progress<string>(msg =>
        {
            RepricingLogBox.AppendText(msg + "\n");
            RepricingLogBox.ScrollToEnd();
            Services.SessionLogger.Info($"[Repricing] {msg}");
        });

        async Task RunTabScheduleAsync(RepricingTabConfig tab)
        {
            var tabRepeatCount    = tab.RepeatCount    ?? repeatCount;
            var tabIntervalMin    = tab.RepeatIntervalMinutes ?? intervalMinutes;

            for (var iter = 1; !ct.IsCancellationRequested; iter++)
            {
                var label = tabRepeatCount > 0 ? $"[{tab.Name}] {iter}/{tabRepeatCount}" : $"[{tab.Name}] {iter}";
                ((IProgress<string>)progress).Report($"── {label} ──");
                await dispatcher.InvokeAsync(() => RepricingStatusText.Text = $"{label}…");

                await Services.GameInputLock.WaitAsync(ct);
                try
                {
                    Native.ProcessForeground.TryBringProcessToForeground(
                        Native.ProcessForeground.PathOfExile2SteamProcessName);
                    await Task.Delay(200, ct);

                    if (traderOcrRect.Width > 0 && !string.IsNullOrWhiteSpace(traderOcrText))
                    {
                        ((IProgress<string>)progress).Report($"[{tab.Name}] OCR: ищем торговца…");
                        var traderNorm = WindowsOcrTextLocator.NormalizeForMatch(traderOcrText);
                        var traderMatch = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                            traderOcrRect, traderNorm, progress, ct);
                        if (traderMatch is { } traderFound)
                        {
                            var (tx, ty) = traderFound.BoundsOnScreen.GetInteriorPoint(inset: 1);
                            Native.Win32Input.MoveTo(tx, ty);
                            await Task.Delay(mouseMs, ct);
                            Native.Win32Input.ClickLeft();
                        }
                        else
                        {
                            ((IProgress<string>)progress).Report($"[{tab.Name}] OCR: торговец не найден — продолжаем.");
                        }
                        await Task.Delay(traderOpenDelay, ct);

                        if (manageShopOcrRect.Width > 0 && !string.IsNullOrWhiteSpace(manageShopOcrText))
                        {
                            ((IProgress<string>)progress).Report($"[{tab.Name}] OCR: ищем Manage Shop…");
                            var shopNorm = WindowsOcrTextLocator.NormalizeForMatch(manageShopOcrText);
                            // Manage Shop всегда на 500-540px ниже торговца; диапазон [+450, +600] отсекает ложные срабатывания
                            var shopMinY = traderMatch?.BoundsOnScreen.Y + 450 ?? 0;
                            var shopMaxY = traderMatch?.BoundsOnScreen.Y + 600 ?? int.MaxValue;
                            var shopMatch = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                                manageShopOcrRect, shopNorm, progress, ct, minScreenY: shopMinY, maxScreenY: shopMaxY);
                            if (shopMatch is { } shopFound)
                            {
                                var r = shopFound.BoundsOnScreen;
                                var mx = r.X + r.Width / 2;
                                // При склейке строк «Currency Exchange» + «Manage Shop» блок высокий;
                                // кликаем в 12px от нижней границы — гарантированно в Manage Shop.
                                var my = r.Y + r.Height - 12;
                                ((IProgress<string>)progress).Report($"[{tab.Name}] OCR: Manage Shop найден (блок {r.Width}×{r.Height} @ {r.X},{r.Y}) → клик ({mx},{my})");
                                Native.Win32Input.MoveTo(mx, my);
                                await Task.Delay(postClickMs, ct);
                                Native.Win32Input.ClickLeft();
                                await Task.Delay(traderOpenDelay, ct);
                            }
                            else
                            {
                                ((IProgress<string>)progress).Report($"[{tab.Name}] OCR: Manage Shop не найден — продолжаем.");
                            }
                        }
                    }

                    if (goodsRect.Width > 0 && goodsRect.Height > 0)
                    {
                        var (gx, gy) = goodsRect.GetRandomInteriorPoint();
                        Native.Win32Input.MoveTo(gx, gy);
                        await Task.Delay(postClickMs, ct);
                        Native.Win32Input.ClickLeft();
                        await Task.Delay(postClickMs, ct);
                    }

                    await _repricingService.RunSingleTabAsync(tab, progress, ct);
                    Native.Win32Input.PressKey(0x33);
                    await Task.Delay(150, CancellationToken.None);
                }
                finally
                {
                    Services.GameInputLock.Release();
                }

                if (tabRepeatCount > 0 && iter >= tabRepeatCount) break;

                var next = DateTime.Now.AddMinutes(tabIntervalMin);
                ((IProgress<string>)progress).Report(
                    $"[{tab.Name}] Пауза {tabIntervalMin} мин — следующая в {next:HH:mm}");
                await Task.Delay(TimeSpan.FromMinutes(tabIntervalMin), ct);
            }
        }

        try
        {
            await Task.WhenAll(tabs.Select(tab => Task.Run(() => RunTabScheduleAsync(tab), CancellationToken.None)));
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

    // ── Шансинг — хоткей ────────────────────────────────────────────────────

    private void RegisterChancingStartStopHotkey()
    {
        UnregisterChancingStartStopHotkey();
        if (_chancingStartStopVirtualKey == 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (GlobalHotkey.TryRegisterChancingStartStop(hwnd, (uint)_chancingStartStopVirtualKey, (uint)_chancingStartStopModifiers))
            _chancingStartStopHotkeyRegistered = true;
    }

    private void UnregisterChancingStartStopHotkey()
    {
        if (!_chancingStartStopHotkeyRegistered) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkey.UnregisterChancingStartStop(hwnd);
        _chancingStartStopHotkeyRegistered = false;
    }

    private void UpdateChancingStartStopHotkeyDisplay()
    {
        ChancingStartStopHotkeyBox.Text = FormatHotkey(_chancingStartStopVirtualKey, _chancingStartStopModifiers);
    }

    private void ChancingToggleStartStop()
    {
        if (_chancingCts != null && !_chancingCts.IsCancellationRequested)
        {
            _chancingCts.Cancel();
            ChancingStopBtn.IsEnabled = false;
        }
        else
        {
            ChancingStartBtn_Click(this, new RoutedEventArgs());
        }
    }

    private void ChancingStartStopHotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _chancingStartStopVirtualKey = 0;
            _chancingStartStopModifiers  = 0;
        }
        else
        {
            var mods = Keyboard.Modifiers;
            _chancingStartStopVirtualKey = KeyInterop.VirtualKeyFromKey(key);
            _chancingStartStopModifiers  = ((mods & ModifierKeys.Alt) != 0 ? 1 : 0)
                                         | ((mods & ModifierKeys.Control) != 0 ? 2 : 0)
                                         | ((mods & ModifierKeys.Shift) != 0 ? 4 : 0);
        }
        UpdateChancingStartStopHotkeyDisplay();
        RegisterChancingStartStopHotkey();
        SaveSettings();
    }

    // ── Шансинг ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, (decimal TabletEx, decimal OocEx)> ChancingDefaultCosts = new()
    {
        ["Irradiated Tablet"] = (47m,  9.9m),
        ["Delirium Tablet"]   = (25m,  9.9m),
        ["Temple Tablet"]     = (25m,  9.9m),
        ["Ritual Tablet"]     = (50m,  9.9m),
        ["Breach Tablet"]     = (79m,  9.9m),
        ["Abyss Tablet"]      = (80m,  9.9m),
    };

    private void ChancingInputBaseCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var key = (ChancingInputBaseCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        if (ChancingDefaultCosts.TryGetValue(key, out var costs))
        {
            ChancingTabletCostBox.Text = costs.TabletEx.ToString("0.#");
            ChancingOocCostBox.Text    = costs.OocEx.ToString("0.#");
        }
        SaveSettings();
    }

    private void ChancingUseOmenCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ChancingUpdateOmenVisibility();
        SaveSettings();
    }

    private void ChancingUpdateOmenVisibility()
    {
        ChancingOmenCostRow.Visibility = ChancingUseOmenCheckBox.IsChecked == true
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void ChancingPickOocRect_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } r) return;
        _chancingOocRect = r;
        ChancingOocRectInfo.Text = FormatRect(r);
        SaveSettings();
    }

    private void ChancingPickTabletCells_Click(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true) return;
        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region) return;
        _chancingTabletCells = picker.SelectedCells is { Count: > 0 } c ? c.ToList() : new List<ScreenRect> { region };
        ChancingTabletCellsInfo.Text = $"{_chancingTabletCells.Count} ячеек";
        SaveSettings();
    }

    private void ChancingClearStatsBtn_Click(object sender, RoutedEventArgs e)
    {
        var stats = _chancingService.SessionStats;
        if (stats.TotalAttempts > 0)
            ChancingSaveStats(stats, _chancingService.DivineEx);
        stats.Clear();
        ChancingStatsBox.Clear();
        ChancingLogBox.Clear();
        ChancingStatusText.Text = "Статистика сброшена.";
    }

    private async void ChancingStartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_chancingTabletCells.Count == 0)
        {
            MessageBox.Show(this, "Задайте сетку ячеек таблетов.", "Шансинг", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_chancingOocRect.Width == 0)
        {
            MessageBox.Show(this, "Задайте ячейку Orb of Chance.", "Шансинг", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var useOmen = ChancingUseOmenCheckBox.IsChecked == true;

        _chancingCts?.Cancel();
        _chancingCts = new CancellationTokenSource();
        var ct = _chancingCts.Token;

        ChancingStartBtn.IsEnabled = false;
        ChancingStopBtn.IsEnabled  = true;
        ChancingStatusText.Text    = "Шансинг…";
        ChancingLogBox.Clear();

        var inputBase = (ChancingInputBaseCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Irradiated Tablet";
        _chancingService.InputBase      = inputBase;
        _chancingService.UseOmen        = useOmen;
        _chancingService.TabletCostEx   = decimal.TryParse(ChancingTabletCostBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tc) ? tc : 47m;
        _chancingService.OocCostEx      = decimal.TryParse(ChancingOocCostBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var oc) ? oc : 9.9m;
        _chancingService.OmenCostEx     = decimal.TryParse(ChancingOmenCostBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var om) ? om : 3.7m;
        _chancingService.MouseActionDelayMs = RfParseInt(ChancingMouseDelayBox.Text, 120);
        _chancingService.ClipboardDelayMs   = RfParseInt(ClipboardDelayMs.Text, 220);
        _chancingService.PostApplyWaitMs    = RfParseInt(ChancingPostApplyWaitBox.Text, 600);

        var oocRect    = _chancingOocRect;
        var cells      = _chancingTabletCells.ToList();
        var dispatcher = Dispatcher;
        var divineEx   = _chancingService.DivineEx;

        var progress = new Progress<string>(msg =>
        {
            ChancingLogBox.AppendText(msg + "\n");
            ChancingLogBox.ScrollToEnd();
        });

        SaveSettings();
        MinimizeToTrayOnStart();

        try
        {
            await _chancingService.RunGridAsync(oocRect, cells, null, progress,
                (attempt, stats) =>
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        ChancingStatsBox.Text = stats.FormatSummary(divineEx);
                        ChancingStatusText.Text = $"{stats.TotalAttempts} поп. | Unique {stats.UniqueRatePct:F1}% | Прибыль {stats.NetProfitEx:F0} ex";
                    });
                }, ct);

            ChancingStatusText.Text = $"Готово. {_chancingService.SessionStats.TotalAttempts} попыток.";
        }
        catch (OperationCanceledException)
        {
            ChancingStatusText.Text = "Остановлено.";
        }
        catch (Exception ex)
        {
            ChancingStatusText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            ChancingStartBtn.IsEnabled = true;
            ChancingStopBtn.IsEnabled  = false;
            var stats = _chancingService.SessionStats;
            ChancingStatsBox.Text = stats.FormatSummary(divineEx);
            if (stats.TotalAttempts > 0)
            {
                ChancingSaveStats(stats, divineEx);
                RefLoadCategories();
            }
        }
    }

    private static string GetProjectDocsPath() =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "docs"));

    private void ChancingSaveStats(Services.ChancingSessionStats stats, decimal divineEx)
    {
        try
        {
            var docs = GetProjectDocsPath();
            stats.SaveToJson(System.IO.Path.Combine(docs, "chancing_stats.json"), divineEx);

            var inputBase      = _chancingService.InputBase;
            var normalizedBase = inputBase.ToLowerInvariant().Replace(" ", "_");
            stats.SaveReferenceJson(
                System.IO.Path.Combine(docs, "stats", $"chancing_{normalizedBase}.json"),
                $"Шансинг → {inputBase}",
                $"Шансинг/{inputBase}");

            Services.SessionLogger.Info($"[Шансинг] Статистика сохранена ({stats.TotalAttempts} попыток)");
        }
        catch (Exception ex)
        {
            Services.SessionLogger.Info($"[Шансинг] Ошибка сохранения статистики: {ex.Message}");
        }
    }

    private void ChancingStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _chancingCts?.Cancel();
        ChancingStopBtn.IsEnabled = false;
    }

    // ── Справочник вероятностей ──────────────────────────────────────────────

    private void RefLoadCategories()
    {
        var statsDir = System.IO.Path.Combine(GetProjectDocsPath(), "stats");
        _referenceCategories = Services.ReferenceStatsService.LoadAll(statsDir);
        _referenceCategories.AddRange(BuildAffixStatCategories());
        RefBuildTree();
    }

    private List<Services.ReferenceCategory> BuildAffixStatCategories()
    {
        var result = new List<Services.ReferenceCategory>();
        var data   = Services.AffixStatsScanner.Current;
        var today  = DateTime.Today.ToString("yyyy-MM-dd");

        // Быстрый словарь имя → запись библиотеки (берём первую подходящую по item class)
        var libByName = _affixEntries
            .GroupBy(e => e.AffixName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (cls, cs) in data.PerClass.OrderBy(kv => kv.Key))
        {
            if (cs.TotalSnapshots == 0) continue;

            var prefixes = new List<Services.ReferenceEntry>();
            var suffixes = new List<Services.ReferenceEntry>();

            foreach (var (affixName, count) in cs.AffixCounts)
            {
                libByName.TryGetValue(affixName, out var candidates);

                // Предпочитаем запись, совпадающую по классу предмета
                var lib = candidates?.FirstOrDefault(e =>
                              e.ItemClasses.Any(ic => string.Equals(ic, cls, StringComparison.OrdinalIgnoreCase)))
                          ?? candidates?.FirstOrDefault();

                var statText  = lib?.AffixStats.FirstOrDefault() ?? affixName;
                var isPrefix  = lib?.AffixType.Contains("Prefix", StringComparison.OrdinalIgnoreCase) ?? false;

                var entry = new Services.ReferenceEntry(statText, count, affixName);
                (isPrefix ? prefixes : suffixes).Add(entry);
            }

            // Сортировка по смысловому слову: убираем ведущие +#%()цифры и "to "
            prefixes.Sort((a, b) => string.Compare(StatSortKey(a.Outcome), StatSortKey(b.Outcome), StringComparison.OrdinalIgnoreCase));
            suffixes.Sort((a, b) => string.Compare(StatSortKey(a.Outcome), StatSortKey(b.Outcome), StringComparison.OrdinalIgnoreCase));

            if (prefixes.Count > 0)
                result.Add(new Services.ReferenceCategory(
                    DisplayName:  $"Хаос-крафт → {cls} · Префиксы",
                    CategoryPath: $"Аффиксы хаос-крафта/{cls} · Префиксы",
                    Updated:      today,
                    TotalSamples: cs.TotalSnapshots,
                    Entries:      prefixes,
                    FilePath:     ""));

            if (suffixes.Count > 0)
                result.Add(new Services.ReferenceCategory(
                    DisplayName:  $"Хаос-крафт → {cls} · Суффиксы",
                    CategoryPath: $"Аффиксы хаос-крафта/{cls} · Суффиксы",
                    Updated:      today,
                    TotalSamples: cs.TotalSnapshots,
                    Entries:      suffixes,
                    FilePath:     ""));
        }

        return result;
    }

    private void RefBuildTree()
    {
        RefCategoryTree.Items.Clear();

        var groups = _referenceCategories
            .GroupBy(c => c.CategoryPath.Contains('/')
                ? c.CategoryPath[..c.CategoryPath.IndexOf('/')]
                : c.CategoryPath)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var cats = group.ToList();
            if (cats.Count == 1 && !cats[0].CategoryPath.Contains('/'))
            {
                var item = new System.Windows.Controls.TreeViewItem
                    { Header = cats[0].DisplayName, Tag = cats[0] };
                RefCategoryTree.Items.Add(item);
            }
            else
            {
                var groupItem = new System.Windows.Controls.TreeViewItem
                    { Header = group.Key, IsExpanded = true };
                foreach (var cat in cats.OrderBy(c => c.DisplayName))
                {
                    var leaf = cat.CategoryPath.IndexOf('/') is var idx and >= 0
                        ? cat.CategoryPath[(idx + 1)..]
                        : cat.DisplayName;
                    groupItem.Items.Add(new System.Windows.Controls.TreeViewItem
                        { Header = leaf, Tag = cat });
                }
                RefCategoryTree.Items.Add(groupItem);
            }
        }
    }

    // Убирает ведущий числовой мусор для сортировки: "+# to maximum Life" → "maximum Life"
    private static string StatSortKey(string stat)
    {
        var s = stat.AsSpan().TrimStart();
        // Пропускаем символы +−#%() цифры пробелы
        var i = 0;
        while (i < s.Length && "+-–#%() 0123456789".Contains(s[i]))
            i++;
        s = s[i..].TrimStart();
        // Пропускаем ведущее "to " (например после "+# to ")
        if (s.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
            s = s[3..].TrimStart();
        return s.ToString();
    }

    private void RefCategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is System.Windows.Controls.TreeViewItem { Tag: Services.ReferenceCategory cat })
            RefShowCategory(cat);
    }

    private void RefShowCategory(Services.ReferenceCategory cat)
    {
        RefCategoryTitle.Text = cat.DisplayName;
        RefCategoryMeta.Text  = $"{cat.TotalSamples} наблюдений · обновлено {cat.Updated}";
        RefDataGrid.ItemsSource = cat.Entries
            .Select(e => Services.ReferenceEntryRow.From(e, cat.TotalSamples))
            .ToList();
    }

    private void RefReloadBtn_Click(object sender, RoutedEventArgs e) => RefLoadCategories();

    // ── История продаж (TRADE-3) ─────────────────────────────────────────────

    private async void TradeHistoryFetchBtn_Click(object sender, RoutedEventArgs e)
    {
        var sessid = TradeSessionIdBox.Text.Trim();
        var league = TradeHistoryLeagueBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sessid))
        {
            TradeHistoryStatusText.Text = "Введите POESESSID.";
            return;
        }
        if (string.IsNullOrWhiteSpace(league)) league = "Runes of Aldur";
        SaveSettings();
        TradeHistoryStatusText.Text = "Загрузка…";
        try
        {
            var (merged, newCount) = await Services.TradeHistoryService.FetchAndMergeAsync(
                league, sessid, _tradeHistory);
            _tradeHistory = merged;
            Services.TradeHistoryService.Save(_tradeHistory);
            RebuildTradeHistoryGrid();
            var countMsg = newCount > 0
                ? $"Загружено +{newCount} новых. Всего: {_tradeHistory.Count}."
                : $"Новых нет. Всего: {_tradeHistory.Count}.";
            TradeHistoryStatusText.Text = countMsg + "  " + BuildRateLimitSummary();
        }
        catch (Exception ex)
        {
            TradeHistoryStatusText.Text = $"Ошибка: {ex.Message}";
        }
    }

    private void RebuildTradeHistoryGrid()
    {
        var rows = _tradeHistory.Select(r => new SaleRow(r)).ToList();
        TradeHistoryGrid.ItemsSource = rows;
        UpdateTradeHistorySummary();
        RebuildTradeGroupGrid();
    }

    private void RebuildTradeGroupGrid()
    {
        // Синие предметы: name пустой, typeLine отличается от baseType
        var groups = _tradeHistory
            .Where(r => string.IsNullOrEmpty(r.ItemName)
                     && !string.IsNullOrEmpty(r.BaseType)
                     && r.TypeLine != r.BaseType)
            .GroupBy(r => r.BaseType)
            .OrderByDescending(g => g.Count())
            .Select(g => new SaleGroupRow(g.Key, g.ToList(), () =>
                Dispatcher.InvokeAsync(() =>
                {
                    Services.TradeHistoryService.Save(_tradeHistory);
                    // Обновляем основной грид чтобы BaseCostDiv отобразился
                    TradeHistoryGrid.ItemsSource = _tradeHistory.Select(r => new SaleRow(r)).ToList();
                    UpdateTradeHistorySummary();
                }, System.Windows.Threading.DispatcherPriority.Background)))
            .ToList();

        TradeGroupGrid.ItemsSource = groups;

        if (groups.Count > 0)
        {
            var totalRev = groups.Sum(g =>
                _tradeHistory.Where(r => string.IsNullOrEmpty(r.ItemName)
                    && r.BaseType == g.BaseType && r.TypeLine != r.BaseType
                    && r.PriceCurrency == "divine").Sum(r => r.PriceAmount));
            TradeGroupSummaryText.Text = groups.Count > 0
                ? $"Синих баз: {groups.Count} типов · {groups.Sum(g => int.Parse(g.CountStr))} продаж · Выручка: {totalRev:0.##} div"
                : "";
        }
        else
        {
            TradeGroupSummaryText.Text = "";
        }
    }

    private void TradeGroupGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != System.Windows.Controls.DataGridEditAction.Commit) return;
        // Сохранение и обновление происходит внутри SaleGroupRow.BaseCostStr.set через _onChanged
    }

    private void CalcCraftFromLogs_Click(object sender, RoutedEventArgs e)
    {
        TradeHistoryStatusText.Text = "Анализ логов…";
        List<Services.CraftCompletion> completions;
        try
        {
            completions = Services.CraftLogAnalyzer.AnalyzeRecentLogs(4);
        }
        catch (Exception ex)
        {
            TradeHistoryStatusText.Text = $"Ошибка чтения логов: {ex.Message}";
            return;
        }

        if (completions.Count == 0)
        {
            TradeHistoryStatusText.Text = "Логи за 4 дня не найдены или не содержат завершённых крафтов.";
            return;
        }

        // Стоимость крафта на единицу по TypeLine (усредняем по нескольким крафтам)
        var costByTypeLine = completions
            .GroupBy(c => c.TypeLine, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var costs = g.Select(Services.CraftLogAnalyzer.CalcCostDiv).Where(v => v > 0).ToList();
                    return costs.Count > 0 ? costs.Average() : 0m;
                },
                StringComparer.OrdinalIgnoreCase);

        // Применяем к синим предметам у которых crافт-стоимость ещё не задана
        int updated = 0;
        foreach (var record in _tradeHistory)
        {
            // Синий предмет: ItemName пустой, TypeLine != BaseType
            if (!string.IsNullOrEmpty(record.ItemName)) continue;
            if (string.IsNullOrEmpty(record.TypeLine) || record.TypeLine == record.BaseType) continue;

            if (costByTypeLine.TryGetValue(record.TypeLine, out var cost) && cost > 0)
            {
                record.CraftCostDiv = Math.Round(cost, 4);
                updated++;
            }
        }

        if (updated > 0)
        {
            Services.TradeHistoryService.Save(_tradeHistory);
            RebuildTradeHistoryGrid();
        }

        var logsSummary = string.Join(", ",
            costByTypeLine.Where(kv => kv.Value > 0)
                          .Select(kv => $"{kv.Key}: {kv.Value:0.####} div"));

        TradeHistoryStatusText.Text =
            $"Логи: {completions.Count} крафтов за 4 дня. Обновлено записей: {updated}."
            + (logsSummary.Length > 0 ? $"  [{logsSummary}]" : " Цены орбов не загружены.");
    }

    private static string BuildRateLimitSummary()
    {
        var rl = Services.TradeHistoryService.LastRateLimit;
        if (rl == null || rl.Length == 0) return "";

        var parts = rl.Select(r =>
        {
            string warn = r.IsExceeded ? "⛔" : r.IsNearLimit ? "⚠️" : "";
            return $"{warn}{r.Current}/{r.Limit} ({r.WindowLabel})";
        });
        return "Лимит: " + string.Join(" · ", parts);
    }

    private void UpdateTradeHistorySummary()
    {
        var rows = (TradeHistoryGrid.ItemsSource as List<SaleRow>) ?? [];
        if (rows.Count == 0) { TradeHistorySummaryText.Text = ""; return; }

        var divSales = rows.Where(r => r.Record.PriceCurrency == "divine").ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($"Всего: {rows.Count} продаж");
        if (divSales.Count > 0)
        {
            var totalDiv = divSales.Sum(r => r.Record.PriceAmount);
            sb.Append($" · {divSales.Count} в div · Выручка: {totalDiv:0.##} div");

            var withAnyCost = rows.Where(r => r.Record.CraftCostDiv > 0 || r.Record.BaseCostDiv > 0).ToList();
            if (withAnyCost.Count > 0)
            {
                var totalCraft = withAnyCost.Sum(r => r.Record.CraftCostDiv);
                var totalBase  = withAnyCost.Sum(r => r.Record.BaseCostDiv);
                var totalCost  = totalCraft + totalBase;
                var profit     = totalDiv - totalCost;
                sb.Append($" · Затраты ({withAnyCost.Count}): {totalCost:0.##} div");
                if (totalCraft > 0) sb.Append($" (крафт {totalCraft:0.##} + база {totalBase:0.##})");
                sb.Append($" · Прибыль: {profit:+0.##;-0.##;0} div");
            }
        }
        TradeHistorySummaryText.Text = sb.ToString();
    }

    private void TradeHistoryGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != System.Windows.Controls.DataGridEditAction.Commit) return;
        Dispatcher.InvokeAsync(() =>
        {
            Services.TradeHistoryService.Save(_tradeHistory);
            UpdateTradeHistorySummary();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private sealed class SaleRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public Services.SaleRecord Record { get; }

        private string _craftCostStr;
        private string _baseCostStr;

        private static string DecToStr(decimal v) =>
            v > 0 ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";

        private static decimal ParseCostStr(string s) =>
            decimal.TryParse(s.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v) ? v : 0;

        public SaleRow(Services.SaleRecord record)
        {
            Record = record;
            _craftCostStr = DecToStr(record.CraftCostDiv);
            _baseCostStr  = DecToStr(record.BaseCostDiv);
        }

        public string DateStr => Record.Time.ToLocalTime().ToString("dd.MM.yy HH:mm");

        public string DisplayName
        {
            get
            {
                var name = Record.ItemName.Trim();
                var type = Record.TypeLine.Trim();
                return string.IsNullOrEmpty(name) ? type : $"{name} {type}".Trim();
            }
        }

        public int ItemLevel => Record.ItemLevel;

        public string PriceStr
        {
            get
            {
                var curr = Record.PriceCurrency switch
                {
                    "divine" => "div",
                    "exalted" => "ex",
                    "chaos" => "c",
                    _ => Record.PriceCurrency,
                };
                return $"{Record.PriceAmount} {curr}";
            }
        }

        public string CraftCostStr
        {
            get => _craftCostStr;
            set
            {
                if (_craftCostStr == value) return;
                _craftCostStr = value;
                Record.CraftCostDiv = ParseCostStr(value);
                PropertyChanged?.Invoke(this, new(nameof(CraftCostStr)));
                PropertyChanged?.Invoke(this, new(nameof(ProfitStr)));
            }
        }

        public string BaseCostStr
        {
            get => _baseCostStr;
            set
            {
                if (_baseCostStr == value) return;
                _baseCostStr = value;
                Record.BaseCostDiv = ParseCostStr(value);
                PropertyChanged?.Invoke(this, new(nameof(BaseCostStr)));
                PropertyChanged?.Invoke(this, new(nameof(ProfitStr)));
            }
        }

        public string ProfitStr
        {
            get
            {
                // Считаем прибыль только если задана хотя бы одна из стоимостей и цена в div
                if (string.IsNullOrWhiteSpace(_craftCostStr) && string.IsNullOrWhiteSpace(_baseCostStr))
                    return "";
                if (Record.PriceCurrency != "divine") return "";
                var totalCost = Record.CraftCostDiv + Record.BaseCostDiv;
                var profit = Record.PriceAmount - totalCost;
                return profit >= 0 ? $"+{profit:0.##} div" : $"{profit:0.##} div";
            }
        }

        public string ModsPreview =>
            string.Join(" · ", Record.ExplicitMods.Concat(Record.FracturedMods).Take(4));
    }

    private sealed class SaleGroupRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private readonly List<Services.SaleRecord> _records;
        private string _baseCostStr;
        private readonly Action _onChanged;

        public string BaseType { get; }
        public string CountStr => _records.Count.ToString();

        private static string DecToStr(decimal v) =>
            v > 0 ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";

        private static decimal ParseDecStr(string s) =>
            decimal.TryParse(s.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        private string _craftCostStr;

        public SaleGroupRow(string baseType, List<Services.SaleRecord> records, Action onChanged)
        {
            BaseType   = baseType;
            _records   = records;
            _onChanged = onChanged;
            var existingBase  = records.FirstOrDefault(r => r.BaseCostDiv  > 0)?.BaseCostDiv  ?? 0;
            var existingCraft = records.FirstOrDefault(r => r.CraftCostDiv > 0)?.CraftCostDiv ?? 0;
            _baseCostStr  = DecToStr(existingBase);
            _craftCostStr = DecToStr(existingCraft);
        }

        public string AvgPriceStr
        {
            get
            {
                var div = _records.Where(r => r.PriceCurrency == "divine").ToList();
                return div.Count > 0 ? $"{div.Average(r => r.PriceAmount):0.##} div" : "";
            }
        }

        public string TotalRevenueStr
        {
            get
            {
                var total = _records.Where(r => r.PriceCurrency == "divine").Sum(r => r.PriceAmount);
                return total > 0 ? $"{total:0.##} div" : "";
            }
        }

        public string CraftCostStr
        {
            get => _craftCostStr;
            set
            {
                if (_craftCostStr == value) return;
                _craftCostStr = value;
                var cost = ParseDecStr(value);
                foreach (var r in _records)
                    r.CraftCostDiv = cost;
                PropertyChanged?.Invoke(this, new(nameof(CraftCostStr)));
                PropertyChanged?.Invoke(this, new(nameof(AvgProfitStr)));
                _onChanged();
            }
        }

        public string BaseCostStr
        {
            get => _baseCostStr;
            set
            {
                if (_baseCostStr == value) return;
                _baseCostStr = value;
                var cost = ParseDecStr(value);
                foreach (var r in _records)
                    r.BaseCostDiv = cost;
                PropertyChanged?.Invoke(this, new(nameof(BaseCostStr)));
                PropertyChanged?.Invoke(this, new(nameof(AvgProfitStr)));
                _onChanged();
            }
        }

        public string AvgProfitStr
        {
            get
            {
                var divSales = _records.Where(r => r.PriceCurrency == "divine").ToList();
                if (divSales.Count == 0) return "";
                var craftCost = ParseDecStr(_craftCostStr);
                var baseCost  = ParseDecStr(_baseCostStr);
                var avgPrice  = divSales.Average(r => r.PriceAmount);
                var profit    = avgPrice - craftCost - baseCost;
                return profit >= 0 ? $"+{profit:0.##} div" : $"{profit:0.##} div";
            }
        }
    }
}
