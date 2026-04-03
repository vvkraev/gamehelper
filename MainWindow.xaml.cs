using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
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
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private List<AffixLibraryEntry> _affixEntries = new();
    private CraftConditionPlan _craftPlan = new();
    private HwndSource? _hwndSource;
    private bool _craftCancelHotkeyRegistered;

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
        }

        return IntPtr.Zero;
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        UnregisterCraftCancelHotkey();
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
    }

    private void SetupTrayIcon()
    {
        if (_trayIcon != null)
            return;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
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

    private void ApplySettings()
    {
        var s = SettingsStore.Load();
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
        };
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
        var dlg = new CraftConditionWindow(editCopy, _affixEntries) { Owner = this };
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
            MessageBox.Show(
                "Не удалось скопировать в буфер обмена: " + ex.Message,
                "Копирование",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearCraftCondition_OnClick(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
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
            MessageBox.Show("Не удалось сохранить рецепт: " + ex.Message, "Рецепт", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Не удалось загрузить рецепт: " + ex.Message, "Рецепт", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(
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
                MessageBox.Show("Не удалось прочитать буфер обмена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SessionLogger.InfoClipboard("ручной парсинг предмета (Ctrl+Alt+C)", clipboardContent);

            if (string.IsNullOrWhiteSpace(clipboardContent))
            {
                MessageBox.Show(
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
                MessageBox.Show(
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
            MessageBox.Show(
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
                MessageBox.Show("Задайте область «заточка» во вкладке «Настройки областей».", "Заточка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_itemCellRegions.Count == 0)
            {
                MessageBox.Show("Задайте область предмета (ячейки) во вкладке «Крафт».", "Область предмета", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Задайте область Chaos Orb во вкладке «Настройки областей».", "Область орба", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isExalt && (_exaltRegion is null || _annulRegion is null))
        {
            MessageBox.Show(
                "Задайте области Orb of Exaltation и Orb of Annulment во вкладке «Настройки областей».",
                "Области сфер",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (isExalt && _omenGreaterCells.Count == 0)
        {
            MessageBox.Show(
                "Задайте область омена Greater во вкладке «Настройки областей» (сетка X×Y).",
                "Омены",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (isExalt)
        {
            var (wantPrefix, wantSuffix) = GetWantedAffixTypes(_craftPlan);
            var prefixOnly = wantPrefix && !wantSuffix;
            var suffixOnly = wantSuffix && !wantPrefix;

            if (prefixOnly && _omenSinistralCells.Count == 0)
            {
                MessageBox.Show(
                    "В условии крафта используются только Prefix Modifier — задайте область омена Sinistral во вкладке «Настройки областей».",
                    "Омены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (suffixOnly && _omenDextralCells.Count == 0)
            {
                MessageBox.Show(
                    "В условии крафта используются только Suffix Modifier — задайте область омена Dextral во вкладке «Настройки областей».",
                    "Омены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (isAugAnnul && (_augRegion is null || _annulRegion is null))
        {
            MessageBox.Show(
                "Задайте области Orb of Augmentation и Orb of Annulment во вкладке «Настройки областей».",
                "Области сфер",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_itemCellRegions.Count == 0)
        {
            MessageBox.Show("Задайте область предмета кнопкой «Задать область».", "Область предмета", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MaxOps.Text.Trim(), out var maxOps) || maxOps < 1)
        {
            MessageBox.Show("Укажите целое N ≥ 1.", "N", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MouseActionDelayMs.Text.Trim(), out var mouseDelay) || mouseDelay < 0)
        {
            MessageBox.Show("Укажите задержку мыши (мс) — целое число ≥ 0.", "Задержка мыши", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(ClipboardDelayMs.Text.Trim(), out var clipboardDelay) || clipboardDelay < 0)
        {
            MessageBox.Show("Укажите задержку после Ctrl+Alt+C (мс) — целое число ≥ 0.", "Буфер обмена", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CraftConditionEvaluator.TryValidate(_craftPlan, out var planErr))
        {
            MessageBox.Show(planErr, "Условие крафта", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        MessageBox.Show(
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
                        MessageBox.Show(
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
                        craftFile.SetCurrentCell(ci + 1, cells.Count);

                        var (r, consumed) = await _exaltCraft.RunAsync(
                            exalt,
                            annul,
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

                        offset += consumed;
                        remaining -= consumed;
                        result = r;
                    }
                    else if (!isAugAnnul)
                    {
                        craftFile ??= CraftRunFileLog.Begin(orb, cells[0], maxOps, conditionSummary, cells);
                        craftFile.SetCurrentCell(ci + 1, cells.Count);
                        var (r, consumed) = await _craft.RunAsync(
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

                        offset += consumed;
                        remaining -= consumed;
                        result = r;
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
                        craftFile.SetCurrentCell(ci + 1, cells.Count);

                        var (r, consumed) = await _augAnnulCraft.RunAsync(
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

                        offset += consumed;
                        remaining -= consumed;
                        result = r;
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
                MessageBox.Show(
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
