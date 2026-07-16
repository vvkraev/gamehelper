using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
    private TradeListenerService? _listener;
    private readonly List<string> _pendingBatches = new();
    private int _pendingItemCount;

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

    private void BtnListenerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_listener is null || !_listener.IsRunning)
            StartListener();
        else
            StopListener();
    }

    private void StartListener()
    {
        _listener ??= new TradeListenerService();
        _listener.OnJsonReceived += HandleTradeJson;
        try
        {
            _listener.Start();
            BtnListenerToggle.Content = "⏹ Стоп";
            TxtListenerStatus.Text = "Слушаю :7123 — скролли страницу трейда";
            TxtListenerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#006600"));
        }
        catch (Exception ex)
        {
            TxtListenerStatus.Text = $"Ошибка: {ex.Message}";
        }
    }

    private void StopListener()
    {
        if (_listener is null) return;
        _listener.OnJsonReceived -= HandleTradeJson;
        _listener.Stop();
        BtnListenerToggle.Content = "▶ Слушать";
        TxtListenerStatus.Text = "Листнер выключен";
        TxtListenerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
    }

    private string HandleTradeJson(string rawJson)
    {
        return Dispatcher.Invoke(() =>
        {
            // Считаем предметы в батче для отображения счётчика
            var batchCount = CountResults(rawJson);
            _pendingBatches.Add(rawJson);
            _pendingItemCount += batchCount;
            UpdatePendingStatus();
            return $"получен батч {batchCount} пред. (буфер: {_pendingItemCount})";
        });
    }

    private void BtnCommitSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _pendingBatches.Count == 0) return;
        var merged = string.Join("\n\n", _pendingBatches);
        _vm.ImportJson = merged;
        _vm.ImportSnapshot();
        _pendingBatches.Clear();
        _pendingItemCount = 0;
        TxtListenerStatus.Text = $"[{DateTime.Now:HH:mm:ss}] {_vm.ImportStatus}";
        TxtListenerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#006600"));
        BtnCommitSnapshot.IsEnabled = false;
        RefreshSnapshotsList();
        RowsGrid.ItemsSource = _vm.Rows;
    }

    private void BtnDiscardBatches_Click(object sender, RoutedEventArgs e)
    {
        _pendingBatches.Clear();
        _pendingItemCount = 0;
        UpdatePendingStatus();
    }

    private void UpdatePendingStatus()
    {
        if (_pendingItemCount == 0)
        {
            TxtListenerStatus.Text = _listener?.IsRunning == true
                ? "Слушаю :7123 — скролли страницу трейда"
                : "Листнер выключен";
            BtnCommitSnapshot.IsEnabled = false;
            BtnDiscardBatches.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtListenerStatus.Text = $"Буфер: {_pendingItemCount} пред. ({_pendingBatches.Count} батч.) — нажми «Снимок»";
            TxtListenerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AA6600"));
            BtnCommitSnapshot.IsEnabled = true;
            BtnDiscardBatches.Visibility = Visibility.Visible;
        }
    }

    private static int CountResults(string json)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            return node?["result"]?.AsArray().Count ?? 0;
        }
        catch { return 0; }
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
        var sockSuffix  = d.Sockets > 0      ? $"  {d.Sockets}S"   : "";
        var sanctSuffix = d.Sanctified        ? "  [Освящён]"        : "";
        var corrSuffix  = d.TwiceCorrupted    ? "  [2× Коррупция]"
                        : d.Corrupted         ? "  [Корр]"           : "";
        TxtDetailHeader.Text = $"{d.Name}  ({d.BaseType})  —  {d.PriceAmount}{cur}  ilvl {d.Ilvl}{sockSuffix}{sanctSuffix}{corrSuffix}";

        var lines = new List<ModLine>();

        // История цен
        if (row.PriceHistory.Count > 0)
        {
            lines.Add(new ModLine { Text = "─── История цен ───────────────────────────────", Color = System.Windows.Media.Brushes.Gray });
            double? prevPrice = null;
            foreach (var pt in row.PriceHistory)
            {
                var priceCur = pt.Currency == "divine" ? "d" : pt.Currency;
                var deltaStr = "";
                if (prevPrice.HasValue)
                {
                    var delta = pt.Price - prevPrice.Value;
                    if (Math.Abs(delta) > 0.001)
                        deltaStr = delta > 0 ? $"  +{delta:0.#}d" : $"  −{Math.Abs(delta):0.#}d";
                }
                var deltaColor = deltaStr.StartsWith("  +") ? "#006600"
                               : deltaStr.StartsWith("  −") ? "#CC2200"
                               : "#444444";
                lines.Add(new ModLine
                {
                    Text = $"  {pt.Time:dd.MM HH:mm}  {pt.Price:0.#}{priceCur}{deltaStr}",
                    Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(deltaColor)),
                });
                prevPrice = pt.Price;
            }
            lines.Add(new ModLine { Text = "───────────────────────────────────────────────", Color = System.Windows.Media.Brushes.Gray });
        }

        // Corruption mods (enchantMods) — отдельная секция перед основными модами
        if (d.ModsCorrupted.Count > 0)
        {
            var corrLabel = d.TwiceCorrupted ? "─── Коррупция (2×) ────────────────────────────"
                                             : "─── Коррупция ─────────────────────────────────";
            lines.Add(new ModLine { Text = corrLabel, Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#880066")) });
            foreach (var m in d.ModsCorrupted)
                lines.Add(new ModLine
                {
                    Text = $"CORR             {m}",
                    Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#880066")),
                });
        }

        // Collect all mods with source annotation, sort PRE before SUF
        var allMods = new List<(string Source, string ColorHex, string Mod)>();
        foreach (var m in d.ModsFractured)  allMods.Add(("FRAC", "#5555AA", m));
        foreach (var m in d.ModsDesecrated) allMods.Add(("DES ", "#885500", m));
        foreach (var m in d.ModsImplicit)   allMods.Add(("IMPL", "#444444", m));
        foreach (var m in d.ModsExplicit)   allMods.Add(("    ", "#222222", m));
        foreach (var m in d.ModsCrafted)    allMods.Add(("CRFT", "#006633", m));
        allMods.Sort((a, b) => ModSortKey(a.Mod).CompareTo(ModSortKey(b.Mod)));
        foreach (var (source, color, mod) in allMods)
            lines.Add(MakeLine(mod, source, color));

        DetailModsList.ItemsSource = lines;

        // Показываем splitter и detail-строку — высота сохраняется между открытиями
        if (DetailRow.Height.Value < 80)
            DetailRow.Height = new GridLength(220);
        SplitterRow.Height = new GridLength(5);
        DetailSplitter.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Visible;
    }

    private static int ModSortKey(string mod)
    {
        var m = System.Text.RegularExpressions.Regex.Match(mod, @"\s([SP])\d+\s+—");
        if (!m.Success) return 2;
        return m.Groups[1].Value == "P" ? 0 : 1;
    }

    private static ModLine MakeLine(string mod, string source, string colorHex)
    {
        var kind = TradeSnapshotDiffService.ModKind(mod);
        var tier = TradeSnapshotDiffService.ModTier(mod);
        var kindTier = string.IsNullOrEmpty(kind) ? "          " : $"{kind} {tier,-4}";
        return new ModLine
        {
            Text = $"{source}  {kindTier}  {mod}",
            Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)),
        };
    }

    private void HideDetail()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailSplitter.Visibility = Visibility.Collapsed;
        SplitterRow.Height = new GridLength(0);
        DetailRow.Height = new GridLength(0);
        DetailModsList.ItemsSource = null;
    }

    private async void BtnEvaluateTablet_Click(object sender, RoutedEventArgs e)
    {
        string itemText;
        try
        {
            itemText = System.Windows.Clipboard.GetText();
        }
        catch
        {
            TxtEvalResult.Text = "Не удалось прочитать буфер обмена";
            return;
        }

        if (string.IsNullOrWhiteSpace(itemText))
        {
            TxtEvalResult.Text = "Буфер обмена пуст";
            return;
        }

        TxtEvalResult.Text = "Оцениваю...";
        BtnEvaluateTablet.IsEnabled = false;
        try
        {
            var result = await EvaluateTabletAsync(itemText);
            // Первая строка с ценой — в статус, полный вывод — во всплывающее окно
            var firstLine = result.Split('\n')[0];
            TxtEvalResult.Text = firstLine;
            MessageBox.Show(result, "Оценка планшетки",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnEvaluateTablet.IsEnabled = true;
        }
    }

    private static async Task<string> EvaluateTabletAsync(string itemText)
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpFile, itemText, System.Text.Encoding.UTF8);
            var wslTmp    = WinToWslPath(tmpFile);
            var scriptDir = WinToWslPath(
                Path.Combine(ProjectPaths.GetProjectRoot(), "scripts", "tabflow"));
            var python = $"{scriptDir}/.venv/bin/python3";
            var script = $"{scriptDir}/evaluate_clipboard.py";

            var psi = new ProcessStartInfo("wsl.exe",
                $"-e {python} {script} --file {wslTmp}")
            {
                RedirectStandardOutput  = true,
                RedirectStandardError   = true,
                UseShellExecute         = false,
                CreateNoWindow          = true,
                StandardOutputEncoding  = System.Text.Encoding.UTF8,
                StandardErrorEncoding   = System.Text.Encoding.UTF8,
            };

            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var output = stdout.Trim();
            if (string.IsNullOrEmpty(output) && !string.IsNullOrEmpty(stderr))
                output = stderr.Trim();
            return string.IsNullOrEmpty(output) ? "Нет ответа от скрипта" : output;
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    private static string WinToWslPath(string winPath)
    {
        // C:\Users\VVK\file.txt → /mnt/c/Users/VVK/file.txt
        var normalized = winPath.Replace('\\', '/');
        if (normalized.Length >= 2 && normalized[1] == ':')
            normalized = "/mnt/" + char.ToLower(normalized[0]) + normalized[2..];
        return normalized;
    }

    private class ModLine
    {
        public string Text { get; set; } = "";
        public System.Windows.Media.Brush Color { get; set; } = System.Windows.Media.Brushes.Black;
    }
}
