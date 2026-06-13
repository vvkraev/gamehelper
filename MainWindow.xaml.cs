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

    private ScreenRect? _orbRegion;
    private ScreenRect? _exaltRegion;
    private ScreenRect? _augRegion;
    private ScreenRect? _annulRegion;
    private ScreenRect? _sharpenRegion;
    private ScreenRect? _currencyInventoryRegion;
    private ScreenRect? _ritualInventoryRegion;
    private ScreenRect? _omenSinistralStashRegion;
    private ScreenRect? _omenDextralStashRegion;
    private ScreenRect? _omenGreaterStashRegion;
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
        }

        return IntPtr.Zero;
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        UnregisterCraftCancelHotkey();
        UnregisterTrayToggleHotkey();
        UnregisterOpenLogHotkey();
        UnregisterCraftStartStopHotkey();
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

    private void MinimizeToTray_OnClick(object sender, RoutedEventArgs e)
    {
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
        _reforgeState.CatalystInventoryRect = loaded.CatalystInventoryRect;
        _reforgeState.Slot1Rect             = loaded.Slot1Rect;
        _reforgeState.Slot2Rect             = loaded.Slot2Rect;
        _reforgeState.Slot3Rect             = loaded.Slot3Rect;
        _reforgeState.ConfirmRect           = loaded.ConfirmRect;
        _reforgeState.ResultRect            = loaded.ResultRect;
        _reforgeState.PostAnimationDelayMs  = loaded.PostAnimationDelayMs;

        if (s.OrbRect is { Width: > 0, Height: > 0 })
        {
            _orbRegion = s.OrbRect;
            OrbInfo.Text = FormatRect(s.OrbRect);
        }

        if (s.ExaltRect is { Width: > 0, Height: > 0 })
        {
            _exaltRegion = s.ExaltRect;
            ExaltInfo.Text = FormatRect(s.ExaltRect);
        }

        if (s.AugRect is { Width: > 0, Height: > 0 })
        {
            _augRegion = s.AugRect;
            AugInfo.Text = FormatRect(s.AugRect);
        }

        if (s.AnnulRect is { Width: > 0, Height: > 0 })
        {
            _annulRegion = s.AnnulRect;
            AnnulInfo.Text = FormatRect(s.AnnulRect);
        }

        if (s.SharpenRect is { Width: > 0, Height: > 0 })
        {
            _sharpenRegion = s.SharpenRect;
            SharpenInfo.Text = FormatRect(s.SharpenRect);
        }

        if (s.OmenSinistralStashRect is { Width: > 0, Height: > 0 })
        {
            _omenSinistralStashRegion = s.OmenSinistralStashRect;
            OmenSinistralStashInfo.Text = FormatRect(s.OmenSinistralStashRect);
        }

        if (s.OmenDextralStashRect is { Width: > 0, Height: > 0 })
        {
            _omenDextralStashRegion = s.OmenDextralStashRect;
            OmenDextralStashInfo.Text = FormatRect(s.OmenDextralStashRect);
        }

        if (s.OmenGreaterStashRect is { Width: > 0, Height: > 0 })
        {
            _omenGreaterStashRegion = s.OmenGreaterStashRect;
            OmenGreaterStashInfo.Text = FormatRect(s.OmenGreaterStashRect);
        }

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
    }

    private void SaveSettings()
    {
        var uiMode = (CraftModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Хаос";
        var s = new AppSettings
        {
            OrbRect = _orbRegion ?? default,
            ExaltRect = _exaltRegion ?? default,
            AugRect = _augRegion ?? default,
            AnnulRect = _annulRegion ?? default,
            SharpenRect = _sharpenRegion ?? default,
            CurrencyInventoryRect = _currencyInventoryRegion ?? default,
            RitualInventoryRect = _ritualInventoryRegion ?? default,
            OmenSinistralStashRect = _omenSinistralStashRegion ?? default,
            OmenDextralStashRect = _omenDextralStashRegion ?? default,
            OmenGreaterStashRect = _omenGreaterStashRegion ?? default,
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
        };
        _reforgeState.ApplyToSettings(s);
        SettingsStore.Save(s);
    }

    private void UpdateCraftConditionSummary()
    {
        CraftConditionSummary.Text = CraftConditionEvaluator.FormatSummary(_craftPlan);
    }

    private void ReforgeBtn_OnClick(object sender, RoutedEventArgs e)
    {
        _reforgeState.ItemCells = _itemCellRegions.ToList();
        var wnd = new ReforgeWindow(_reforgeState, SaveSettings) { Owner = this };
        wnd.Show();
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

    private void PickOrbBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Chaos Orb отменён.");
            return;
        }

        _orbRegion = region;
        OrbInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область Chaos Orb задана: {FormatRect(region)}");
        SaveSettings();
    }

    private void PickExaltBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Orb of Exaltation отменён.");
            return;
        }

        _exaltRegion = region;
        ExaltInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область Orb of Exaltation задана: {FormatRect(region)}");
        SaveSettings();
    }

    private void PickAugBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Orb of Augmentation отменён.");
            return;
        }

        _augRegion = region;
        AugInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область Orb of Augmentation задана: {FormatRect(region)}");
        SaveSettings();
    }

    private void PickAnnulBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Orb of Annulment отменён.");
            return;
        }

        _annulRegion = region;
        AnnulInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область Orb of Annulment задана: {FormatRect(region)}");
        SaveSettings();
    }

    private void PickSharpenBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области заточки отменён.");
            return;
        }

        _sharpenRegion = region;
        SharpenInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область заточки задана: {FormatRect(region)}");
        SaveSettings();
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

    private void PickOmenSinistralStashBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Omen of Sinistral Exaltation Stash отменён.");
            return;
        }

        _omenSinistralStashRegion = region;
        OmenSinistralStashInfo.Text = FormatRect(region);
        SessionLogger.Info($"Omen of Sinistral Exaltation Stash задана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickOmenDextralStashBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Omen of Dextral Exaltation Stash отменён.");
            return;
        }

        _omenDextralStashRegion = region;
        OmenDextralStashInfo.Text = FormatRect(region);
        SessionLogger.Info($"Omen of Dextral Exaltation Stash задана: {FormatRect(region)}");
        SaveSettings();
        _ = ProcessForeground.TryBringProcessToForeground(ProcessForeground.PathOfExile2SteamProcessName);
    }

    private void PickOmenGreaterStashBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области Omen of Greater Exaltation Stash отменён.");
            return;
        }

        _omenGreaterStashRegion = region;
        OmenGreaterStashInfo.Text = FormatRect(region);
        SessionLogger.Info($"Omen of Greater Exaltation Stash задана: {FormatRect(region)}");
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
            if (_sharpenRegion is null)
            {
                MessageBox.Show(this,"Задайте область «заточка» во вкладке «Настройки областей».", "Заточка", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                await _sharpen.RunAsync(_sharpenRegion.Value, _itemCellRegions, 20, sharpenLog, _cts.Token);
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

        if (!isAugAnnul && !isExalt && _orbRegion is null)
        {
            MessageBox.Show(this,"Задайте область Chaos Orb во вкладке «Настройки областей».", "Область орба", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isExalt && (_exaltRegion is null || _annulRegion is null))
        {
            MessageBox.Show(this,
                "Задайте области Orb of Exaltation и Orb of Annulment во вкладке «Настройки областей».",
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

        if (isExalt && (_ritualInventoryRegion is null || _currencyInventoryRegion is null || _omenGreaterStashRegion is null))
        {
            MessageBox.Show(this,
                "Для крафта Orb of Exaltation задайте области: Ritual inventory, Currency inventory и Omen of Greater Exaltation Stash (для автопополнения омнов).",
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

            if (prefixOnly && _omenSinistralStashRegion is null)
            {
                MessageBox.Show(this,
                    "Для условия только с префиксами задайте область Omen of Sinistral Exaltation Stash.",
                    "Области RefillOmen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (suffixOnly && _omenDextralStashRegion is null)
            {
                MessageBox.Show(this,
                    "Для условия только с суффиксами задайте область Omen of Dextral Exaltation Stash.",
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

        if (isAugAnnul && (_augRegion is null || _annulRegion is null))
        {
            MessageBox.Show(this,
                "Задайте области Orb of Augmentation и Orb of Annulment во вкладке «Настройки областей».",
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
            var orb = _orbRegion ?? default;
            var exalt = _exaltRegion ?? default;
            var aug = _augRegion ?? default;
            var annul = _annulRegion ?? default;
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
                            _omenSinistralStashRegion ?? default,
                            _omenDextralStashRegion ?? default,
                            _omenGreaterStashRegion ?? default,
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
}
