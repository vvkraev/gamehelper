using System.Windows;
using System.Windows.Controls;
using GameHelper.Services;

namespace GameHelper;

public partial class PipelineStepDialog : Window
{
    public CraftPipelineStep Step { get; }

    private CraftConditionPlan _entry;
    private CraftConditionPlan _loop;

    /// <summary>Общий буфер для копирования условий между шагами пайплайна.</summary>
    private static CraftConditionPlan? _conditionClipboard;

    private readonly List<AffixLibraryEntry> _affixEntries;
    private readonly Services.AffixStatsData? _stats;
    private Services.KeyboardMacroRecorder? _walkRecorder;

    // Полный список оменов в порядке RitualItemGroups (только предметы-омены)
    internal static readonly string[] AllOmenNames =
    [
        // Экзальтация
        "Omen of Sinistral Exaltation",
        "Omen of Dextral Exaltation",
        "Omen of Greater Exaltation",
        "Omen of Catalysing Exaltation",
        // Аннулирование / Стирание
        "Omen of Sinistral Annulment",
        "Omen of Dextral Annulment",
        "Omen of Sinistral Erasure",
        "Omen of Dextral Erasure",
        "Omen of Whittling",
        // Некромантия / Кристаллизация
        "Omen of Sinistral Necromancy",
        "Omen of Dextral Necromancy",
        "Omen of Sinistral Crystallisation",
        "Omen of Dextral Crystallisation",
        // Хаос
        "Omen of Chaotic Quantity",
        "Omen of Chaotic Effectiveness",
        "Omen of Chaotic Monsters",
        "Omen of Chaotic Rarity",
        "Omen of Gambling",
        "Omen of Chance",
        // Другое
        "Omen of Amelioration",
        "Omen of Answered Prayers",
        "Omen of Bartering",
        "Omen of Refreshment",
        "Omen of Reinforcements",
        "Omen of Resurgence",
        "Omen of Sanctification",
        "Omen of Putrefaction",
        "Omen of Abyssal Echoes",
        "Omen of Light",
        "Omen of the Hunt",
        "Omen of the Liege",
        "Omen of the Ancients",
        "Omen of the Blackblooded",
        "Omen of the Blessed",
        "Omen of the Sovereign",
        "Omen of Secret Compartments",
    ];

    // Полный список валют из Currency stash — имена совпадают с ключами в _currencyItemRegions
    internal static readonly string[] CurrencyKnownItems =
    [
        "Divine Orb",
        "Fracturing Orb",
        "Orb of Annulment",
        "Orb of Extraction",
        "Crystallised Corruption",
        "Architect's Orb",
        "Vaal Orb",
        "Orb of Chance",
        "Orb of Alchemy",
        "Hinekora's Lock",
        "Chaos Orb",
        "Greater Chaos Orb",
        "Perfect Chaos Orb",
        "Exalted Orb",
        "Greater Exalted Orb",
        "Perfect Exalted Orb",
        "Orb of Augmentation",
        "Greater Orb of Augmentation",
        "Perfect Orb of Augmentation",
        "Orb of Transmutation",
        "Greater Orb of Transmutation",
        "Perfect Orb of Transmutation",
        "Regal Orb",
        "Greater Regal Orb",
        "Perfect Regal Orb",
        "Lesser Jeweller's Orb",
        "Greater Jeweller's Orb",
        "Perfect Jeweller's Orb",
        "Arcanist's Etcher",
        "Artificer's Orb",
        "Blacksmith's Whetstone",
        "Glassblower's Bauble",
        "Armourer's Scrap",
        "Gemcutter's Prism",
    ];

    internal static readonly (string Id, string DisplayName)[] AbyssKnownBones =
    [
        ("ancient_jawbone",    "Ancient Jawbone"),
        ("gnawed_jawbone",     "Gnawed Jawbone"),
        ("preserved_jawbone",  "Preserved Jawbone"),
        ("ancient_collarbone", "Ancient Collarbone"),
        ("gnawed_collarbone",  "Gnawed Collarbone"),
        ("preserved_collarbone", "Preserved Collarbone"),
        ("ancient_rib",        "Ancient Rib"),
        ("gnawed_rib",         "Gnawed Rib"),
        ("preserved_rib",      "Preserved Rib"),
        ("preserved_cranium",  "Preserved Cranium"),
    ];

    // Mapping: ComboBox index → PipelineAction
    private static readonly PipelineAction[] ActionMap =
    [
        PipelineAction.CheckItem,
        PipelineAction.ChaosCraft,
        PipelineAction.AugAnnulCraft,
        PipelineAction.DivineCraft,
        PipelineAction.ExaltCraft,
        PipelineAction.SimpleCurrency,
        PipelineAction.OmenActivation,
        PipelineAction.DeliriumLiquid,
        PipelineAction.ManualPause,
        PipelineAction.TravelToLocation,
        PipelineAction.SimpleAbyssalBone,
        PipelineAction.WalkToPosition,
        PipelineAction.OpenStash,
    ];

    // Mapping: ComboBox index → TransitionTarget
    private static readonly TransitionTarget[] TransitionMap =
    [
        TransitionTarget.Next,
        TransitionTarget.Done,
        TransitionTarget.Abort,
        TransitionTarget.Step,
    ];

    private readonly IReadOnlyList<Services.StackableItemType> _availableBones;

    public PipelineStepDialog(
        CraftPipelineStep step,
        List<AffixLibraryEntry> affixEntries,
        Services.AffixStatsData? stats = null,
        IReadOnlyList<Services.StackableItemType>? availableBones = null)
    {
        Step = step;
        _affixEntries = affixEntries;
        _stats = stats;
        _availableBones = availableBones ?? Array.Empty<Services.StackableItemType>();
        _entry = SettingsStore.CloneCraftConditionPlan(step.EntryCondition ?? new CraftConditionPlan());
        _loop = SettingsStore.CloneCraftConditionPlan(step.LoopUntil ?? new CraftConditionPlan());
        InitializeComponent();
        OmenNameCombo.ItemsSource = AllOmenNames;
        LoadFromStep();
        Closed += (_, _) => { _walkRecorder?.Dispose(); _walkRecorder = null; };
    }

    private void LoadFromStep()
    {
        StepNameBox.Text = Step.Name;
        MaxIterBox.Text = Step.MaxIterations.ToString();

        // Action
        var actionIdx = Array.IndexOf(ActionMap, Step.Action);
        ActionCombo.SelectedIndex = actionIdx >= 0 ? actionIdx : 0;

        // OnSuccess
        var sIdx = Array.IndexOf(TransitionMap, Step.OnSuccess.Target);
        SuccessCombo.SelectedIndex = sIdx >= 0 ? sIdx : 0;
        SuccessStepBox.Text = Step.OnSuccess.StepIndex.ToString();
        SuccessMsgBox.Text = Step.OnSuccess.Message ?? "";

        // OnFailure
        var fIdx = Array.IndexOf(TransitionMap, Step.OnFailure.Target);
        FailureCombo.SelectedIndex = fIdx >= 0 ? fIdx : 2; // default Abort
        FailureStepBox.Text = Step.OnFailure.StepIndex.ToString();
        FailureMsgBox.Text = Step.OnFailure.Message ?? "";

        // OmenConfig
        if (Step.OmenConfig is { } cfg)
        {
            var omenIdx = Array.IndexOf(AllOmenNames, cfg.OmenName);
            OmenNameCombo.SelectedIndex = omenIdx >= 0 ? omenIdx : 0;
            OmenInvRowBox.Text = cfg.InventoryRow.ToString();
            OmenInvColBox.Text = cfg.InventoryCol.ToString();
        }
        else
        {
            OmenNameCombo.SelectedIndex = 0;
        }

        // DeliriumLiquidConfig
        var deliriumItems = Services.StackableItemRegistry.Items
            .Where(i => i.Kind == Services.StackableItemKind.Delirium)
            .OrderBy(i => i.DisplayName)
            .ToList();
        DeliriumLiquidCombo.ItemsSource = deliriumItems;
        var savedId = Step.DeliriumLiquidConfig?.LiquidName ?? "";
        DeliriumLiquidCombo.SelectedItem = deliriumItems.FirstOrDefault(i => i.Id == savedId);

        // SimpleCurrency
        CurrencyCombo.ItemsSource = CurrencyKnownItems;
        CurrencyCombo.SelectedItem = CurrencyKnownItems.FirstOrDefault(c => c == (Step.CurrencyId ?? ""));

        // AbyssalBone — берём из переданного списка (кости с настроенными областями)
        AbyssalBoneCombo.ItemsSource = _availableBones;
        AbyssalBoneCombo.SelectedItem = _availableBones.FirstOrDefault(b => b.Id == (Step.AbyssalBoneId ?? ""));

        // TravelConfig
        if (Step.TravelConfig is { } tc)
        {
            TravelWpX.Text          = tc.WaypointSearchArea.X.ToString();
            TravelWpY.Text          = tc.WaypointSearchArea.Y.ToString();
            TravelWpW.Text          = tc.WaypointSearchArea.Width.ToString();
            TravelWpH.Text          = tc.WaypointSearchArea.Height.ToString();
            TravelLocX.Text         = tc.LocationButtonArea.X.ToString();
            TravelLocY.Text         = tc.LocationButtonArea.Y.ToString();
            TravelLocW.Text         = tc.LocationButtonArea.Width.ToString();
            TravelLocH.Text         = tc.LocationButtonArea.Height.ToString();
            TravelWpOcrText.Text             = tc.WaypointOcrText;
            TravelWpDelayBox.Text            = tc.AfterWaypointDelayMs.ToString();
            TravelLoadDelayBox.Text          = tc.LoadingDelayMs.ToString();
            TravelExpectedLocationBox.Text   = tc.ExpectedLocation;
        }

        // WalkConfig
        WalkEscapeCheckBox.IsChecked = Step.WalkConfig?.PressEscapeFirst ?? true;
        WalkKeyPressList.Children.Clear();
        foreach (var kp in Step.WalkConfig?.KeyPresses ?? [])
            WalkKeyPressList.Children.Add(MakeWalkKeyRow(string.Join(" ", kp.Keys), kp.DurationMs));

        EntryNegateChk.IsChecked = _entry.Negate;
        LoopNegateChk.IsChecked  = _loop.Negate;
        RefreshConditionSummaries();
    }

    private void SaveToStep()
    {
        Step.Name = StepNameBox.Text.Trim();
        Step.Action = ActionMap[Math.Clamp(ActionCombo.SelectedIndex, 0, ActionMap.Length - 1)];
        Step.MaxIterations = int.TryParse(MaxIterBox.Text, out var n) && n > 0 ? n : 100;

        Step.OnSuccess = new PipelineTransition
        {
            Target = TransitionMap[Math.Clamp(SuccessCombo.SelectedIndex, 0, TransitionMap.Length - 1)],
            StepIndex = int.TryParse(SuccessStepBox.Text, out var si) ? si : 0,
            Message = SuccessMsgBox.Text.Trim(),
        };
        Step.OnFailure = new PipelineTransition
        {
            Target = TransitionMap[Math.Clamp(FailureCombo.SelectedIndex, 0, TransitionMap.Length - 1)],
            StepIndex = int.TryParse(FailureStepBox.Text, out var fi) ? fi : 0,
            Message = FailureMsgBox.Text.Trim(),
        };

        if (Step.Action == PipelineAction.OmenActivation)
        {
            var omenName = OmenNameCombo.SelectedItem as string ?? AllOmenNames[0];
            Step.OmenConfig = new OmenActionConfig
            {
                OmenName       = omenName,
                StashCellIndex = 0,
                InventoryRow   = int.TryParse(OmenInvRowBox.Text, out var ir) ? ir : 0,
                InventoryCol   = int.TryParse(OmenInvColBox.Text, out var ic) ? ic : 0,
            };
        }
        else
        {
            Step.OmenConfig = null;
        }

        if (Step.Action == PipelineAction.DeliriumLiquid)
        {
            var selectedLiquid = DeliriumLiquidCombo.SelectedItem as Services.StackableItemType;
            Step.DeliriumLiquidConfig = new DeliriumLiquidActionConfig
            {
                LiquidName = selectedLiquid?.Id ?? "",
            };
        }
        else
        {
            Step.DeliriumLiquidConfig = null;
        }

        Step.CurrencyId = Step.Action == PipelineAction.SimpleCurrency
            ? CurrencyCombo.SelectedItem as string ?? ""
            : null;

        Step.AbyssalBoneId = Step.Action == PipelineAction.SimpleAbyssalBone
            ? (AbyssalBoneCombo.SelectedItem as Services.StackableItemType)?.Id ?? ""
            : null;

        if (Step.Action == PipelineAction.TravelToLocation)
        {
            Step.TravelConfig = new Services.TravelActionConfig
            {
                WaypointSearchArea = new ScreenRect(
                    int.TryParse(TravelWpX.Text, out var wpx) ? wpx : 0,
                    int.TryParse(TravelWpY.Text, out var wpy) ? wpy : 0,
                    int.TryParse(TravelWpW.Text, out var wpw) ? wpw : 400,
                    int.TryParse(TravelWpH.Text, out var wph) ? wph : 200),
                LocationButtonArea = new ScreenRect(
                    int.TryParse(TravelLocX.Text, out var lx) ? lx : 0,
                    int.TryParse(TravelLocY.Text, out var ly) ? ly : 0,
                    int.TryParse(TravelLocW.Text, out var lw) ? lw : 100,
                    int.TryParse(TravelLocH.Text, out var lh) ? lh : 40),
                WaypointOcrText      = TravelWpOcrText.Text.Trim(),
                AfterWaypointDelayMs = int.TryParse(TravelWpDelayBox.Text, out var wdms) ? wdms : 10000,
                LoadingDelayMs       = int.TryParse(TravelLoadDelayBox.Text, out var ldms) ? ldms : 15000,
                ExpectedLocation     = TravelExpectedLocationBox.Text.Trim(),
            };
        }
        else
        {
            Step.TravelConfig = null;
        }

        if (Step.Action == PipelineAction.WalkToPosition)
        {
            var kps = WalkKeyPressList.Children
                .OfType<Grid>()
                .Select(row =>
                {
                    var boxes = row.Children.OfType<System.Windows.Controls.TextBox>().ToList();
                    var keysText = boxes.Count > 0 ? boxes[0].Text : "S";
                    var keys = keysText.Split(new[] { ' ', '+', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(k => k.Trim().ToUpperInvariant())
                                       .Where(k => k.Length > 0)
                                       .ToList();
                    if (keys.Count == 0) keys.Add("S");
                    var dur = boxes.Count > 1 && int.TryParse(boxes[1].Text, out var d) ? d : 2000;
                    return new Services.WalkKeyPress { Keys = keys, DurationMs = dur };
                })
                .ToList();
            Step.WalkConfig = new Services.WalkToPositionConfig
            {
                KeyPresses       = kps,
                PressEscapeFirst = WalkEscapeCheckBox.IsChecked == true,
            };
        }
        else
        {
            Step.WalkConfig = null;
        }

        _entry.Negate = EntryNegateChk.IsChecked == true;
        _loop.Negate  = LoopNegateChk.IsChecked  == true;
        Step.EntryCondition = HasClauses(_entry) ? _entry : null;
        Step.LoopUntil = HasClauses(_loop) ? _loop : null;
    }

    private static bool HasClauses(CraftConditionPlan plan) =>
        plan.OrAlternatives?.Any(g => g.Clauses?.Count > 0) == true;

    private void RefreshConditionSummaries()
    {
        EntryCondSummary.Text = HasClauses(_entry)
            ? $"EntryCondition: {CraftConditionEvaluator.FormatSummary(_entry)}"
            : "EntryCondition: (нет)";

        LoopUntilSummary.Text = HasClauses(_loop)
            ? $"LoopUntil: {CraftConditionEvaluator.FormatSummary(_loop)}"
            : "LoopUntil: (нет)";
    }

    // ── Обработчики ComboBox ─────────────────────────────────────────────────

    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionCombo.SelectedIndex < 0) return;
        var action = ActionMap[Math.Clamp(ActionCombo.SelectedIndex, 0, ActionMap.Length - 1)];
        OmenConfigPanel.Visibility = action == PipelineAction.OmenActivation
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeliriumLiquidConfigPanel.Visibility = action == PipelineAction.DeliriumLiquid
            ? Visibility.Visible
            : Visibility.Collapsed;
        TravelConfigPanel.Visibility = action == PipelineAction.TravelToLocation
            ? Visibility.Visible
            : Visibility.Collapsed;
        AbyssalBoneConfigPanel.Visibility = action == PipelineAction.SimpleAbyssalBone
            ? Visibility.Visible
            : Visibility.Collapsed;
        CurrencyConfigPanel.Visibility = action == PipelineAction.SimpleCurrency
            ? Visibility.Visible
            : Visibility.Collapsed;
        WalkConfigPanel.Visibility = action == PipelineAction.WalkToPosition
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SuccessCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateTransitionVisibility(SuccessCombo, SuccessStepLbl, SuccessStepBox, SuccessMsgBox);

    private void FailureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateTransitionVisibility(FailureCombo, FailureStepLbl, FailureStepBox, FailureMsgBox);

    private void UpdateTransitionVisibility(System.Windows.Controls.ComboBox combo, TextBlock stepLbl, System.Windows.Controls.TextBox stepBox, System.Windows.Controls.TextBox msgBox)
    {
        if (combo.SelectedIndex < 0) return;
        var target = TransitionMap[Math.Clamp(combo.SelectedIndex, 0, TransitionMap.Length - 1)];
        var isStep = target == TransitionTarget.Step;
        var isMsg = target is TransitionTarget.Done or TransitionTarget.Abort;
        stepLbl.Visibility = isStep ? Visibility.Visible : Visibility.Collapsed;
        stepBox.Visibility = isStep ? Visibility.Visible : Visibility.Collapsed;
        msgBox.Visibility = isMsg ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Условия ──────────────────────────────────────────────────────────────

    private void EditEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CraftConditionWindow(_entry, _affixEntries, _stats) { Owner = this };
        dlg.ShowDialog();
        RefreshConditionSummaries();
    }

    private void ClearEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        _entry = new CraftConditionPlan();
        EntryNegateChk.IsChecked = false;
        RefreshConditionSummaries();
    }

    private void EditLoopBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CraftConditionWindow(_loop, _affixEntries, _stats) { Owner = this };
        dlg.ShowDialog();
        RefreshConditionSummaries();
    }

    private void ClearLoopBtn_Click(object sender, RoutedEventArgs e)
    {
        _loop = new CraftConditionPlan();
        LoopNegateChk.IsChecked = false;
        RefreshConditionSummaries();
    }

    private void CopyEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        _conditionClipboard = SettingsStore.CloneCraftConditionPlan(_entry);
    }

    private void PasteEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_conditionClipboard is null) return;
        _entry = SettingsStore.CloneCraftConditionPlan(_conditionClipboard);
        EntryNegateChk.IsChecked = _entry.Negate;
        RefreshConditionSummaries();
    }

    private void CopyLoopBtn_Click(object sender, RoutedEventArgs e)
    {
        _conditionClipboard = SettingsStore.CloneCraftConditionPlan(_loop);
    }

    private void PasteLoopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_conditionClipboard is null) return;
        _loop = SettingsStore.CloneCraftConditionPlan(_conditionClipboard);
        LoopNegateChk.IsChecked = _loop.Negate;
        RefreshConditionSummaries();
    }

    // ── TravelToLocation — захват координат ──────────────────────────────────

    private void TravelPickWaypointAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } r)
            return;

        TravelWpX.Text = r.X.ToString();
        TravelWpY.Text = r.Y.ToString();
        TravelWpW.Text = r.Width.ToString();
        TravelWpH.Text = r.Height.ToString();
    }

    private void TravelPickLocBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } r)
            return;

        TravelLocX.Text = r.X.ToString();
        TravelLocY.Text = r.Y.ToString();
        TravelLocW.Text = r.Width.ToString();
        TravelLocH.Text = r.Height.ToString();
    }

    private void TravelSnapshotWpBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TravelWpX.Text, out var x) ||
            !int.TryParse(TravelWpY.Text, out var y) ||
            !int.TryParse(TravelWpW.Text, out var w) ||
            !int.TryParse(TravelWpH.Text, out var h) ||
            w <= 0 || h <= 0)
        {
            System.Windows.MessageBox.Show("Сначала задайте область поиска (X/Y/W/H).", "Скриншот", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var area = new ScreenRect(x, y, w, h);
        try
        {
            using var bmp = Services.ScreenCaptureHelper.CaptureRegion(area);
            var path = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"travel_waypoint_snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            System.Windows.MessageBox.Show(
                $"Скриншот области сохранён:\n{path}",
                "Скриншот",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "Скриншот", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── WalkToPosition ───────────────────────────────────────────────────────

    // keysText — пробел/плюс-разделённые клавиши, например "W D" или "W+D" для диагонали
    private Grid MakeWalkKeyRow(string keysText = "S", int duration = 2000)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyLbl = new TextBlock { Text = "Клавиши:", VerticalAlignment = VerticalAlignment.Center,
                                     ToolTip = "Одна или несколько через пробел: W, W D, W+D" };
        Grid.SetColumn(keyLbl, 0);

        var keyBox = new System.Windows.Controls.TextBox
        {
            Text = keysText.ToUpperInvariant(), Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Например: W  или  W D  (диагональ)"
        };
        Grid.SetColumn(keyBox, 1);

        var durLbl = new TextBlock { Text = "Время (мс):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(durLbl, 2);

        var durBox = new System.Windows.Controls.TextBox { Text = duration.ToString(), Width = 70, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(durBox, 3);

        var removeBtn = new System.Windows.Controls.Button { Content = "✕", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        removeBtn.Click += (_, _) => WalkKeyPressList.Children.Remove(grid);
        Grid.SetColumn(removeBtn, 4);

        grid.Children.Add(keyLbl);
        grid.Children.Add(keyBox);
        grid.Children.Add(durLbl);
        grid.Children.Add(durBox);
        grid.Children.Add(removeBtn);
        return grid;
    }

    private void WalkAddKeyBtn_Click(object sender, RoutedEventArgs e) =>
        WalkKeyPressList.Children.Add(MakeWalkKeyRow());

    private async void WalkRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        WalkRecordBtn.IsEnabled = false;
        WalkAddKeyBtn.IsEnabled = false;
        WalkRecordStatus.Visibility = Visibility.Visible;

        for (var i = 3; i > 0; i--)
        {
            WalkRecordStatus.Text = $"Переключитесь в игру… {i}";
            await Task.Delay(1000).ConfigureAwait(true);
        }
        WalkRecordStatus.Text = "● Запись — отпустите все клавиши для остановки";
        WalkKeyPressList.Children.Clear();

        _walkRecorder?.Dispose();
        _walkRecorder = new Services.KeyboardMacroRecorder();
        _walkRecorder.RecordingFinished += OnWalkRecordingFinished;
        _walkRecorder.Start();
    }

    private void OnWalkRecordingFinished(List<Services.WalkKeyPress> recorded)
    {
        _walkRecorder?.Dispose();
        _walkRecorder = null;

        WalkKeyPressList.Children.Clear();
        foreach (var kp in recorded)
            WalkKeyPressList.Children.Add(MakeWalkKeyRow(string.Join(" ", kp.Keys), kp.DurationMs));

        WalkRecordStatus.Visibility = Visibility.Collapsed;
        WalkRecordBtn.IsEnabled = true;
        WalkAddKeyBtn.IsEnabled = true;
    }

    // ── OK ────────────────────────────────────────────────────────────────────

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveToStep();
        DialogResult = true;
    }
}
