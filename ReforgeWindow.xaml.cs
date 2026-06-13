using System.Windows;
using System.Windows.Controls;
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
    }

    // ── Загрузка из состояния ────────────────────────────────────────────────

    private void LoadFromState()
    {
        ScanGridInfo.Text  = _state.ItemCells.Count > 0 ? $"{_state.ItemCells.Count} ячеек" : "(не задана)";
        Slot1Info.Text     = FormatRect(_state.Slot1Rect);
        Slot2Info.Text     = FormatRect(_state.Slot2Rect);
        Slot3Info.Text     = FormatRect(_state.Slot3Rect);
        ConfirmInfo.Text   = FormatRect(_state.ConfirmRect);
        ResultInfo.Text    = FormatRect(_state.ResultRect);
        PostAnimDelayBox.Text = _state.PostAnimationDelayMs.ToString();
        MaxOpsBox.Text        = _state.MaxOps.ToString();
        RefreshCatalystList();
    }

    private static string FormatRect(ScreenRect r) =>
        r is { Width: > 0, Height: > 0 } ? $"{r.X},{r.Y}  {r.Width}×{r.Height}" : "(не задано)";

    // ── Pick-кнопки для областей станка ─────────────────────────────────────

    private void PickScanGrid_Click(object sender, RoutedEventArgs e)
    {
        var dimDlg = new ItemGridDimensionsDialog { Owner = this };
        if (dimDlg.ShowDialog() != true) return;
        var picker = new RegionPickerWindow(dimDlg.GridColumns, dimDlg.GridRows) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedRegion is not { } region) return;
        _state.ItemCells = picker.SelectedCells is { Count: > 0 } c ? c.ToList() : new List<ScreenRect> { region };
        _saveSettings();
        ScanGridInfo.Text = $"{_state.ItemCells.Count} ячеек";
        Log($"Сетка задана: {_state.ItemCells.Count} ячеек");
    }

    private void PickSlot1_Click(object sender, RoutedEventArgs e) =>
        Pick("Слот 1 станка", r => { _state.Slot1Rect = r; Slot1Info.Text = FormatRect(r); });

    private void PickSlot2_Click(object sender, RoutedEventArgs e) =>
        Pick("Слот 2 станка", r => { _state.Slot2Rect = r; Slot2Info.Text = FormatRect(r); });

    private void PickSlot3_Click(object sender, RoutedEventArgs e) =>
        Pick("Слот 3 станка", r => { _state.Slot3Rect = r; Slot3Info.Text = FormatRect(r); });

    private void PickConfirm_Click(object sender, RoutedEventArgs e) =>
        Pick("Кнопка Reforge", r => { _state.ConfirmRect = r; ConfirmInfo.Text = FormatRect(r); });

    private void PickResult_Click(object sender, RoutedEventArgs e) =>
        Pick("Область результата", r => { _state.ResultRect = r; ResultInfo.Text = FormatRect(r); });

    private void Pick(string hint, Action<ScreenRect> apply)
    {
        var dlg = new RegionPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedRegion is not { } region) return;
        apply(region);
        _saveSettings();
        Log($"Область «{hint}» задана: {FormatRect(region)}");
    }

    // ── Список катализаторов с чекбоксами ────────────────────────────────────

    private void RefreshCatalystList()
    {
        CatalystCheckList.Items.Clear();
        foreach (var item in StackableItemRegistry.Items)
        {
            var cb = new System.Windows.Controls.CheckBox
            {
                Content = $"{item.DisplayName}  [{item.Kind}]",
                Tag     = item.Id,
                IsChecked = _state.SelectedCatalystIds.Contains(item.Id),
            };
            cb.Checked   += CatalystCheck_Changed;
            cb.Unchecked += CatalystCheck_Changed;
            CatalystCheckList.Items.Add(cb);
        }
        UpdateSelectionStatus();
    }

    private void CatalystCheck_Changed(object sender, RoutedEventArgs e)
    {
        SyncSelectedIds();
        _saveSettings();
        UpdateSelectionStatus();
    }

    private void SyncSelectedIds()
    {
        _state.SelectedCatalystIds.Clear();
        foreach (System.Windows.Controls.CheckBox cb in CatalystCheckList.Items)
            if (cb.IsChecked == true && cb.Tag is string id)
                _state.SelectedCatalystIds.Add(id);
    }

    private void UpdateSelectionStatus()
    {
        var total    = CatalystCheckList.Items.Count;
        var selected = CatalystCheckList.Items.Cast<System.Windows.Controls.CheckBox>().Count(cb => cb.IsChecked == true);
        CatalystSelectionStatus.Text = $"Выбрано: {selected} / {total}";
    }

    private void DeselectAllCatalysts_Click(object sender, RoutedEventArgs e)
    {
        foreach (System.Windows.Controls.CheckBox cb in CatalystCheckList.Items) cb.IsChecked = false;
    }

    // ── Сканирование реестра ─────────────────────────────────────────────────

    private async void ScanGridBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_state.ItemCells.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Задайте сетку инвентаря кнопкой «Задать сетку…».",
                "Сканирование", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        ScanGridBtn.IsEnabled = false;
        RegistryScanStatus.Text = "Сканирование…";

        var added = 0; var skipped = 0; var empty = 0;
        var mouseMs = ParseInt(MouseDelayBox.Text, 80);
        var clipMs  = ParseInt(ClipDelayBox.Text, 220);

        _service.MouseActionDelayMs = mouseMs;
        _service.ClipboardDelayMs   = clipMs;

        try
        {
            Native.ProcessForeground.TryBringProcessToForeground(
                Native.ProcessForeground.PathOfExile2SteamProcessName);
            await Task.Delay(150, ct);

            var scanned = await _service.ScanInventoryAsync(_state.ItemCells, null,
                new Progress<string>(Log), ct);

            // Регистрируем всё найденное как известные типы
            // (ScanInventoryAsync уже фильтрует через реестр — но здесь нам нужно
            //  добавлять НОВЫЕ типы, поэтому делаем независимый проход)
            foreach (var cell in _state.ItemCells)
            {
                ct.ThrowIfCancellationRequested();
                var (x, y) = cell.GetRandomInteriorPoint(inset: 2);
                Native.Win32Input.MoveTo(x, y);
                await Task.Delay(WithJitter(mouseMs), ct);
                await Task.Delay(Math.Clamp(mouseMs / 2, 50, 150), ct);
                await ClearClipboardAsync();
                Native.Win32Input.SendCtrlAltC();
                await Task.Delay(WithJitter(clipMs), ct);
                var text = await Dispatcher.InvokeAsync(GetClipboardTextSafe);
                if (string.IsNullOrWhiteSpace(text)) { empty++; continue; }
                var parsed = ItemParser.Parse(text);
                if (parsed == null || !parsed.IsValid) { skipped++; continue; }
                var (wasAdded, entry) = StackableItemRegistry.TryRegister(parsed);
                if (wasAdded) { added++; Log($"  + {entry!.DisplayName} ({entry.Kind})"); }
                else if (entry != null) skipped++;
                else { Log($"  ? пропущено: {parsed.Name}"); skipped++; }
            }

            StackableItemRegistry.Save();
            RefreshCatalystList();
            RegistryScanStatus.Text = $"+{added} новых, {skipped} пропущено, {empty} пустых";
        }
        catch (OperationCanceledException)
        {
            RegistryScanStatus.Text = "Отменено";
        }
        catch (Exception ex)
        {
            RegistryScanStatus.Text = $"Ошибка: {ex.Message}";
            Log($"[Scan] {ex.Message}");
        }
        finally
        {
            Native.Win32Input.ReleaseCtrlAlt();
            ScanGridBtn.IsEnabled = true;
        }
    }

    private void ClearRegistryBtn_Click(object sender, RoutedEventArgs e)
    {
        var r = System.Windows.MessageBox.Show(this,
            "Очистить реестр катализаторов?",
            "Очистить реестр", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        StackableItemRegistry.Clear();
        RefreshCatalystList();
        Log("[Registry] Реестр очищен");
    }

    // ── Старт / Стоп перековки ───────────────────────────────────────────────

    private void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateForRun()) return;

        _service.MouseActionDelayMs  = ParseInt(MouseDelayBox.Text, 80);
        _service.ClipboardDelayMs    = ParseInt(ClipDelayBox.Text, 220);
        _service.PostReforgeSettleMs = ParseInt(PostAnimDelayBox.Text, 800);

        _state.PostAnimationDelayMs = _service.PostReforgeSettleMs;
        _state.MaxOps = ParseInt(MaxOpsBox.Text, 0, allowZero: true);
        SyncSelectedIds();
        _saveSettings();

        _cts = new CancellationTokenSource();
        StartBtn.IsEnabled = false;
        StopBtn.IsEnabled  = true;

        var selectedIds = _state.SelectedCatalystIds.ToList();
        var maxOps      = _state.MaxOps;
        var progress    = new Progress<string>(Log);
        var ct          = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                Native.ProcessForeground.TryBringProcessToForeground(
                    Native.ProcessForeground.PathOfExile2SteamProcessName);
                await Task.Delay(200, ct);

                var reason = await _service.RunAsync(
                    _state.ItemCells, selectedIds,
                    _state.Slot1Rect, _state.Slot2Rect, _state.Slot3Rect,
                    _state.ConfirmRect, _state.ResultRect,
                    maxOps, progress,
                    r => Dispatcher.InvokeAsync(() => Log($"  → {r.InputTypeName} → {r.OutputItemName ?? "?"}")),
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
                    StartBtn.IsEnabled = true;
                    StopBtn.IsEnabled  = false;
                });
            }
        }, CancellationToken.None);
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StopBtn.IsEnabled = false;
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _scanCts?.Cancel();
        base.OnClosed(e);
    }

    // ── Валидация ─────────────────────────────────────────────────────────────

    private bool ValidateForRun()
    {
        var missing = new List<string>();
        if (_state.ItemCells.Count == 0)    missing.Add("Сетка инвентаря");
        if (_state.Slot1Rect is { Width: <= 0 }) missing.Add("Слот 1");
        if (_state.Slot2Rect is { Width: <= 0 }) missing.Add("Слот 2");
        if (_state.Slot3Rect is { Width: <= 0 }) missing.Add("Слот 3");
        if (_state.ConfirmRect is { Width: <= 0 }) missing.Add("Кнопка Reforge");
        if (_state.ResultRect  is { Width: <= 0 }) missing.Add("Область результата");

        if (missing.Count > 0)
        {
            System.Windows.MessageBox.Show(this,
                "Не заданы:\n• " + string.Join("\n• ", missing),
                "Перековка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        SyncSelectedIds();
        if (_state.SelectedCatalystIds.Count == 0)
        {
            System.Windows.MessageBox.Show(this,
                "Отметьте хотя бы один тип катализатора в списке.",
                "Перековка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    // ── Вспомогательные ──────────────────────────────────────────────────────

    private void Log(string msg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => Log(msg)); return; }
        LogBox.AppendText(msg + "\n");
        LogBox.ScrollToEnd();
    }

    private static int ParseInt(string s, int fallback, bool allowZero = false)
    {
        if (!int.TryParse(s.Trim(), out var v)) return fallback;
        return (allowZero && v == 0) || v > 0 ? v : fallback;
    }

    private static int WithJitter(int baseMs)
    {
        if (baseMs <= 0) return 0;
        var delta = (int)Math.Round(baseMs * 0.30);
        return Math.Max(0, baseMs + Random.Shared.Next(-delta, delta + 1));
    }

    private static string GetClipboardTextSafe()
    {
        try { return System.Windows.Clipboard.GetText(); }
        catch { return ""; }
    }

    private static async Task ClearClipboardAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { System.Windows.Clipboard.Clear(); } catch { }
        });
}
