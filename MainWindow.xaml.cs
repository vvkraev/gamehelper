using System.Windows;
using GameHelper.Services;

namespace GameHelper;

public partial class MainWindow : Window
{
    private readonly ChaosCraftService _craft = new();
    private CancellationTokenSource? _cts;
    private ScreenRect? _orbRegion;
    private ScreenRect? _itemRegion;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        Closed += MainWindow_OnClosed;
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        SessionLogger.NewLine -= OnSessionNewLine;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();

        foreach (var line in SessionLogger.GetSnapshot())
            WriteUiOnly(line);

        SessionLogger.NewLine += OnSessionNewLine;
        SessionLogger.Info("Главное окно открыто.");
    }

    private void ApplySettings()
    {
        var s = SettingsStore.Load();
        if (s.OrbRect is { Width: > 0, Height: > 0 })
        {
            _orbRegion = s.OrbRect;
            OrbInfo.Text = FormatRect(s.OrbRect);
        }

        if (s.ItemRect is { Width: > 0, Height: > 0 })
        {
            _itemRegion = s.ItemRect;
            ItemInfo.Text = FormatRect(s.ItemRect);
        }

        AffixPattern.Text = s.AffixPattern;
        MinRoll.Text = s.MinRoll.ToString();
        MouseActionDelayMs.Text = s.MouseActionDelayMs.ToString();
        ClipboardDelayMs.Text = s.ClipboardDelayMs.ToString();
        MaxOps.Text = s.MaxOps.ToString();
        TraceInputCheckBox.IsChecked = s.TraceInput;
        StepConfirmCheckBox.IsChecked = s.StepConfirm;
        _craft.ClipboardDelayMs = s.ClipboardDelayMs;
    }

    private void SaveSettings()
    {
        var minRoll = int.TryParse(MinRoll.Text.Trim(), out var mr) ? mr : 0;
        var s = new AppSettings
        {
            OrbRect = _orbRegion ?? default,
            ItemRect = _itemRegion ?? default,
            AffixPattern = AffixPattern.Text ?? "",
            MinRoll = minRoll,
            MouseActionDelayMs = int.TryParse(MouseActionDelayMs.Text.Trim(), out var md) ? md : 80,
            ClipboardDelayMs = int.TryParse(ClipboardDelayMs.Text.Trim(), out var cd) ? cd : 220,
            MaxOps = int.TryParse(MaxOps.Text.Trim(), out var mo) ? mo : 20,
            TraceInput = TraceInputCheckBox.IsChecked == true,
            StepConfirm = StepConfirmCheckBox.IsChecked == true,
        };
        SettingsStore.Save(s);
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

    private static string FormatRect(ScreenRect r)
    {
        var (cx, cy) = r.Center;
        return $"X={r.X}, Y={r.Y}, W={r.Width}, H={r.Height} (клик: {cx},{cy})";
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

    private void PickItemBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region)
        {
            SessionLogger.Info("Выбор области предмета отменён.");
            return;
        }

        _itemRegion = region;
        ItemInfo.Text = FormatRect(region);
        SessionLogger.Info($"Область предмета задана: {FormatRect(region)}");
        SaveSettings();
    }

    private async void StartBtn_OnClick(object sender, RoutedEventArgs e)
    {
        if (_orbRegion is null)
        {
            MessageBox.Show("Задайте область Chaos Orb кнопкой «Задать область».", "Область орба", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_itemRegion is null)
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
            MessageBox.Show("Укажите задержку после Ctrl+C (мс) — целое число ≥ 0.", "Буфер обмена", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var pattern = AffixPattern.Text.Trim();
        if (pattern.Length == 0)
        {
            MessageBox.Show("Введите шаблон аффикса (или хотя бы фрагмент текста).", "Шаблон", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var minRoll = 0;
        if (pattern.Contains('n', StringComparison.Ordinal))
        {
            if (!int.TryParse(MinRoll.Text.Trim(), out minRoll))
            {
                MessageBox.Show("В шаблоне есть символ «n» — укажите целое минимальное значение числа.", "Порог n", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _craft.MouseActionDelayMs = mouseDelay;
        _craft.ClipboardDelayMs = clipboardDelay;
        _craft.TraceInputToLog = TraceInputCheckBox.IsChecked == true;
        _craft.StepConfirmAsync = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var runCts = _cts;

        if (StepConfirmCheckBox.IsChecked == true)
        {
            _craft.StepConfirmAsync = msg =>
                Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new StepConfirmDialog(msg) { Owner = this };
                    if (dlg.ShowDialog() != true)
                        runCts.Cancel();
                }).Task;
        }

        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;

        var progress = new Progress<string>(SessionLogger.Info);

        CraftRunFileLog? craftFile = null;
        try
        {
            var orb = _orbRegion.Value;
            var item = _itemRegion.Value;
            craftFile = CraftRunFileLog.Begin(orb, item, maxOps, pattern, minRoll);

            SessionLogger.Info(
                $"--- запуск крафта: орб {FormatRect(orb)}, предмет {FormatRect(item)}, N={maxOps}, задержка мыши {mouseDelay} мс, после Ctrl+C {clipboardDelay} мс; лог крафта — в папке Log после завершения ---");

            ChaosCraftResult result;
            try
            {
                result = await _craft.RunAsync(
                    orb,
                    item,
                    pattern,
                    minRoll,
                    maxOps,
                    progress,
                    _cts.Token,
                    craftFile);
            }
            catch (OperationCanceledException)
            {
                result = ChaosCraftResult.Cancelled;
            }

            craftFile.SetOutcome(result);
            SessionLogger.Info($"Итог крафта: {result}");
            SaveSettings();
        }
        catch (Exception ex)
        {
            craftFile?.SetOutcome(ChaosCraftResult.Error, ex.Message);
            SessionLogger.Info("Ошибка: " + ex.Message);
        }
        finally
        {
            _craft.StepConfirmAsync = null;
            craftFile?.Dispose();
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
        }
    }

    private void StopBtn_OnClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        SessionLogger.Info("(отмена запрошена)");
    }
}
