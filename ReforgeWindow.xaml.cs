using System.Windows;
using GameHelper.Services;

namespace GameHelper;

public partial class ReforgeWindow : Window
{
    private readonly ReforgeState _state;
    private readonly Action _saveSettings;

    private CancellationTokenSource? _cts;
    private readonly ReforgeService _service = new();

    public ReforgeWindow(ReforgeState state, Action saveSettings)
    {
        InitializeComponent();
        _state = state;
        _saveSettings = saveSettings;
        LoadFromState();
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
        base.OnClosed(e);
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
