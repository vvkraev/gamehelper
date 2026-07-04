using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameHelper.Services;
using GameHelper.ViewModels;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;

namespace GameHelper;

public partial class TrackingTab : UserControl
{
    private TrackingViewModel? _vm;

    public TrackingTab()
    {
        InitializeComponent();
        SetActiveFilter("Все");
    }

    public void Initialize(TrackingViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        RefreshSessionsList();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrackingViewModel.Rows))
                RowsGrid.ItemsSource = vm.Rows;
            if (e.PropertyName == nameof(TrackingViewModel.Sessions))
                RefreshSessionsList();
            if (e.PropertyName == nameof(TrackingViewModel.SelectedSession))
                RefreshSnapshotsList();
        };
        RowsGrid.ItemsSource = vm.Rows;
    }

    private void RefreshSessionsList()
    {
        if (_vm is null) return;
        SessionsList.ItemsSource = null;
        SessionsList.ItemsSource = _vm.Sessions;
        SessionsList.DisplayMemberPath = "Name";
    }

    private void SessionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.SelectedSession = SessionsList.SelectedItem as Services.TrackingSession;
        RowsGrid.ItemsSource = _vm.Rows;
        RefreshSnapshotsList();
        HideDetail();
    }

    private void BtnCreateSession_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.NewSessionName = TxtNewSession.Text;
        _vm.CreateSession();
        TxtNewSession.Text = "";
        RefreshSessionsList();
    }

    private void BtnDeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedSession is null) return;
        var r = MessageBox.Show($"Удалить сессию '{_vm.SelectedSession.Name}'?",
            "Подтверждение", MessageBoxButton.YesNo);
        if (r != MessageBoxResult.Yes) return;
        _vm.DeleteSelectedSession();
        SessionsList.SelectedItem = null;
        RefreshSessionsList();
        HideDetail();
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ImportJson = TxtImportJson.Text;
        _vm.ImportSnapshot();
        TxtImportStatus.Text = _vm.ImportStatus;
        if (string.IsNullOrEmpty(_vm.ImportJson))
            TxtImportJson.Text = "";
        RowsGrid.ItemsSource = _vm.Rows;
        RefreshSnapshotsList();
    }

    private void BtnDeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not Services.SnapshotRef snap) return;
        var r = MessageBox.Show($"Удалить снимок '{snap.DisplayLabel}'?",
            "Подтверждение", MessageBoxButton.YesNo);
        if (r != MessageBoxResult.Yes) return;
        _vm.DeleteSnapshot(snap);
        RefreshSnapshotsList();
        RowsGrid.ItemsSource = _vm.Rows;
    }

    private void RefreshSnapshotsList()
    {
        SnapshotsList.ItemsSource = null;
        SnapshotsList.ItemsSource = _vm?.SelectedSession?.Snapshots;
    }

    private void FilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not System.Windows.Controls.Button btn) return;
        var tag = btn.Tag?.ToString() ?? "Все";
        _vm.FilterStatus = tag;
        SetActiveFilter(tag);
        RowsGrid.ItemsSource = _vm.Rows;
        HideDetail();
    }

    private void SetActiveFilter(string active)
    {
        if (BtnFilterAll.Parent is not StackPanel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is System.Windows.Controls.Button b)
                b.SetValue(FontWeightProperty, b.Tag?.ToString() == active ? FontWeights.Bold : FontWeights.Normal);
        }
    }

    private void RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RowsGrid.SelectedItem is TrackingItemRow row)
            ShowDetail(row);
        else
            HideDetail();
    }

    private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
    {
        RowsGrid.SelectedItem = null;
        HideDetail();
    }

    private void ShowDetail(TrackingItemRow row)
    {
        if (row.Detail is null) { HideDetail(); return; }
        var d = row.Detail;
        var cur = d.PriceCurrency == "divine" ? "d" : d.PriceCurrency;
        TxtDetailHeader.Text = $"{d.Name}  —  {d.PriceAmount}{cur}  ilvl {d.Ilvl}";

        var lines = new List<ModLine>();
        foreach (var m in d.ModsFractured)  lines.Add(MakeLine(m, "[Fractured] ", "#5555AA"));
        foreach (var m in d.ModsDesecrated) lines.Add(MakeLine(m, "[Desecrated]", "#885500"));
        foreach (var m in d.ModsImplicit)   lines.Add(MakeLine(m, "[Implicit]  ", "#444444"));
        foreach (var m in d.ModsExplicit)   lines.Add(MakeLine(m, "[Explicit]  ", "#222222"));
        foreach (var m in d.ModsCrafted)    lines.Add(MakeLine(m, "[Crafted]   ", "#006633"));

        DetailModsList.ItemsSource = lines;
        DetailPanel.Visibility = Visibility.Visible;
    }

    private static ModLine MakeLine(string mod, string tag, string colorHex)
    {
        var kind = TradeSnapshotDiffService.ModKind(mod);
        var tier = TradeSnapshotDiffService.ModTier(mod);
        var prefix = string.IsNullOrEmpty(kind) ? "" : $" {kind} {tier}";
        return new ModLine
        {
            Text = $"{tag}{prefix}  {mod}",
            Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
        };
    }

    private void HideDetail()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailModsList.ItemsSource = null;
    }

    private class ModLine
    {
        public string Text { get; set; } = "";
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.Black;
    }
}
