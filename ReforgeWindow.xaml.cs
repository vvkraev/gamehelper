using System.Windows;
using GameHelper.Services;

namespace GameHelper;

public partial class ReforgeWindow : Window
{
    private readonly ReforgeState _state;
    private readonly Action _saveSettings;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _scanCts;
    private readonly ReforgeService _service = new();

    public ReforgeWindow(ReforgeState state, Action saveSettings)
    {
        InitializeComponent();
        _state = state;
        _saveSettings = saveSettings;
        StackableItemRegistry.Load();
        LoadFromState();
        RefreshRegistryList();
    }

    // ── Загрузка из состояния ────────────────────────────────────────────────

    private void LoadFromState()
    {
        CatalystInventoryInfo.Text = FormatRect(_state.CatalystInventoryRect);
        Slot1Info.Text             = FormatRect(_state.Slot1Rect);
        Slot2Info.Text             = FormatRect(_state.Slot2Rect);
        Slot3Info.Text             = FormatRect(_state.Slot3Rect);
        ConfirmInfo.Text           = FormatRect(_state.ConfirmRect);
        ResultInfo.Text            = FormatRect(_state.ResultRect);
        PostAnimDelayBox.Text      = _state.PostAnimationDelayMs.ToString();
    }

    private static string FormatRect(ScreenRect r) =>
        r is { Width: > 0, Height: > 0 } ? $"{r.X},{r.Y}  {r.Width}×{r.Height}" : "(не задано)";

    // ── Pick-кнопки ──────────────────────────────────────────────────────────

    private void PickCatalystInventory_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Pick("Стак катализаторов в инвентаре", r =>
        {
            _state.CatalystInventoryRect = r;
            CatalystInventoryInfo.Text = FormatRect(r);
        });

    private void PickSlot1_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Pick("Слот 1 станка", r =>
        {
            _state.Slot1Rect = r;
            Slot1Info.Text = FormatRect(r);
        });

    private void PickSlot2_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Pick("Слот 2 станка", r =>
        {
            _state.Slot2Rect = r;
            Slot2Info.Text = FormatRect(r);
        });

    private void PickSlot3_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Pick("Слот 3 станка", r =>
        {
            _state.Slot3Rect = r;
            Slot3Info.Text = FormatRect(r);
        });

    private void PickConfirm_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Pick("Кнопка Reforge", r =>
        {
            _state.ConfirmRect = r;
            ConfirmInfo.Text = FormatRect(r);
        });

    private void PickResult_Click(object sender, System.Windows.RoutedEventArgs e) =>
        Pick("Область результата", r =>
        {
            _state.ResultRect = r;
            ResultInfo.Text = FormatRect(r);
        });

    private void Pick(string hint, Action<ScreenRect> apply)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        apply(region);
        _saveSettings();
        Log($"Область «{hint}» задана: {FormatRect(region)}");
    }

    // ── Старт / Стоп ─────────────────────────────────────────────────────────

    private void StartBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!ValidateAreas()) return;

        _service.MouseActionDelayMs  = ParseInt(MouseDelayBox.Text, 80);
        _service.ClipboardDelayMs    = ParseInt(ClipDelayBox.Text, 220);
        _service.PostReforgeSettleMs = ParseInt(PostAnimDelayBox.Text, 800);

        _state.PostAnimationDelayMs = _service.PostReforgeSettleMs;
        _saveSettings();

        _cts = new CancellationTokenSource();
        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled  = true;

        var progress = new Progress<string>(Log);
        var token    = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _service.RunLoopAsync(
                    _state.CatalystInventoryRect,
                    _state.Slot1Rect,
                    _state.Slot2Rect,
                    _state.Slot3Rect,
                    _state.ConfirmRect,
                    _state.ResultRect,
                    maxAttempts: 0,
                    log: progress,
                    onAttempt: r => Dispatcher.InvokeAsync(() =>
                        Log($"  → {r.OutputItemName ?? "?"}")),
                    ct: token);
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
                await Dispatcher.InvokeAsync(() =>
                {
                    StartBtn.IsEnabled = true;
                    StopBtn.IsEnabled  = false;
                });
            }
        }, CancellationToken.None);
    }

    private void StopBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _cts?.Cancel();
        StopBtn.IsEnabled = false;
    }

    private void CloseBtn_Click(object sender, System.Windows.RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _scanCts?.Cancel();
        base.OnClosed(e);
    }

    // ── Реестр / сканирование ────────────────────────────────────────────────

    private async void ScanGridBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var cells = _state.ItemCells;
        if (cells.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Сетка предмета не задана. Задайте её в главном окне (вкладка «Крафт» → «Задать область»).",
                "Сканирование", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        ScanGridBtn.IsEnabled = false;
        RegistryScanStatus.Text = "Сканирование…";

        var mouseMs = ParseInt(MouseDelayBox.Text, 80);
        var clipMs  = ParseInt(ClipDelayBox.Text, 220);
        var added   = 0;
        var skipped = 0;
        var empty   = 0;

        try
        {
            Native.ProcessForeground.TryBringProcessToForeground(
                Native.ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(150, ct);

            foreach (var cell in cells)
            {
                ct.ThrowIfCancellationRequested();

                var (x, y) = cell.GetRandomInteriorPoint(inset: 2);
                Native.Win32Input.MoveTo(x, y);
                await Task.Delay(WithJitter(mouseMs), ct);
                await Task.Delay(Math.Clamp(mouseMs / 2, 60, 180), ct); // hover settle

                await ClearClipboardAsync();
                Native.Win32Input.SendCtrlAltC();
                await Task.Delay(WithJitter(clipMs), ct);

                var text = await Dispatcher.InvokeAsync(GetClipboardTextSafe);
                if (string.IsNullOrWhiteSpace(text)) { empty++; continue; }

                var parsed = ItemParser.Parse(text);
                if (parsed == null || !parsed.IsValid) { skipped++; continue; }

                var (wasAdded, entry) = StackableItemRegistry.TryRegister(parsed);
                if (wasAdded)
                {
                    added++;
                    Log($"  + {entry!.DisplayName} ({entry.Kind})");
                }
                else if (entry != null)
                {
                    skipped++;
                }
                else
                {
                    // не каталог и не soul core
                    Log($"  ? пропущено: {parsed.Name} [{parsed.ItemClass}]");
                    skipped++;
                }
            }

            StackableItemRegistry.Save();
            RefreshRegistryList();
            RegistryScanStatus.Text = $"Готово: +{added} новых, {skipped} пропущено, {empty} пустых";
            Log($"[Scan] Сканирование завершено: +{added} новых записей");
        }
        catch (OperationCanceledException)
        {
            RegistryScanStatus.Text = "Отменено";
        }
        catch (Exception ex)
        {
            RegistryScanStatus.Text = $"Ошибка: {ex.Message}";
            Log($"[Scan] Ошибка: {ex.Message}");
        }
        finally
        {
            Native.Win32Input.ReleaseCtrlAlt();
            ScanGridBtn.IsEnabled = true;
        }
    }

    private void ClearRegistryBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var r = System.Windows.MessageBox.Show(this,
            "Очистить реестр катализаторов? Это действие нельзя отменить.",
            "Очистить реестр",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (r != System.Windows.MessageBoxResult.Yes) return;
        StackableItemRegistry.Clear();
        RefreshRegistryList();
        Log("[Registry] Реестр очищен");
    }

    private void RefreshRegistryList()
    {
        RegistryListBox.Items.Clear();
        foreach (var item in StackableItemRegistry.Items)
            RegistryListBox.Items.Add($"{item.DisplayName}  [{item.Kind}]");
        RegistryScanStatus.Text = $"{StackableItemRegistry.Items.Count} записей";
    }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * 0.30);
        if (delta <= 0) return baseMs;
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.GetText(); }
        catch { return ""; }
    }

    private static async Task ClearClipboardAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private bool ValidateAreas()
    {
        var missing = new List<string>();
        if (_state.CatalystInventoryRect is { Width: <= 0 }) missing.Add("Стак в инвентаре");
        if (_state.Slot1Rect             is { Width: <= 0 }) missing.Add("Слот 1");
        if (_state.Slot2Rect             is { Width: <= 0 }) missing.Add("Слот 2");
        if (_state.Slot3Rect             is { Width: <= 0 }) missing.Add("Слот 3");
        if (_state.ConfirmRect           is { Width: <= 0 }) missing.Add("Кнопка Reforge");
        if (_state.ResultRect            is { Width: <= 0 }) missing.Add("Область результата");

        if (missing.Count == 0) return true;

        System.Windows.MessageBox.Show(this,
            "Не заданы области:\n• " + string.Join("\n• ", missing),
            "Перековка", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return false;
    }

    private void Log(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => Log(msg)); return; }
        LogBox.AppendText(msg + "\n");
        LogBox.ScrollToEnd();
    }

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s.Trim(), out var v) && v > 0 ? v : fallback;
}
