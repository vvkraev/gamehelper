using System.IO;
using System.Windows;
using System.Windows.Threading;
using GameHelper.Services;

namespace GameHelper;

public partial class CraftLogWindow : Window
{
    private string? _filePath;
    private readonly DispatcherTimer _refreshTimer;
    private long _lastFileBytes;

    public CraftLogWindow()
    {
        InitializeComponent();
        WindowGeometryStore.Attach(this, "CraftLogWindow");
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _refreshTimer.Tick += (_, _) => RefreshIfChanged();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    /// <summary>Загружает файл и запускает авто-обновление если это WIP (.tmp).</summary>
    public void LoadFile(string filePath)
    {
        _filePath = filePath;
        _lastFileBytes = 0;
        FilePathText.Text = Path.GetFileName(filePath);
        FilePathText.ToolTip = filePath;
        LoadContent(scrollToEnd: false);

        // Откладываем прокрутку: TextBox должен быть отрендерен до вызова ScrollToEnd
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => LogContentBox.ScrollToEnd());

        var isWip = filePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        StatusText.Text = isWip ? "● активный крафт" : "";
        StatusText.Foreground = isWip
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Gray;

        if (isWip)
            _refreshTimer.Start();
        else
            _refreshTimer.Stop();
    }

    private void RefreshIfChanged()
    {
        if (_filePath == null)
            return;

        if (!File.Exists(_filePath))
        {
            _refreshTimer.Stop();

            // Dispose() переименовывает "craft_START_wip.tmp" → "craft_START--END.txt"
            // Ищем финальный файл по префиксу до "_wip"
            var dir = Path.GetDirectoryName(_filePath) ?? "";
            var name = Path.GetFileName(_filePath); // "craft_START_wip.tmp"
            var prefix = name.Replace("_wip.tmp", "", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(prefix))
            {
                var finalFile = Directory.EnumerateFiles(dir, $"{prefix}--*.txt")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();
                if (finalFile != null)
                {
                    _filePath = finalFile;
                    LoadContent(scrollToEnd: false);
                }
                // Если финальный .txt ещё не появился — оставляем текущий текст
            }

            StatusText.Text = "✓ завершён";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        try
        {
            var size = new FileInfo(_filePath).Length;
            if (size == _lastFileBytes)
                return;
            LoadContent(scrollToEnd: true);
        }
        catch { /* файл может быть заблокирован на запись — пропускаем тик */ }
    }

    private void LoadContent(bool scrollToEnd)
    {
        if (_filePath == null)
            return;

        string text;
        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            text = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            text = $"(не удалось прочитать файл: {ex.Message})";
        }

        _lastFileBytes = new FileInfo(_filePath!).Length;
        var atBottom = IsScrolledToBottom();
        LogContentBox.Text = text;

        if (scrollToEnd && atBottom)
            LogContentBox.ScrollToEnd();
    }

    private bool IsScrolledToBottom()
    {
        var sv = LogContentBox.Template?.FindName("PART_ContentHost", LogContentBox)
            as System.Windows.Controls.ScrollViewer;
        if (sv == null) return true;
        return sv.VerticalOffset >= sv.ScrollableHeight - 4;
    }

    private void RefreshBtn_OnClick(object sender, RoutedEventArgs e) =>
        LoadContent(scrollToEnd: false);
}
