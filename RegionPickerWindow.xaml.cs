using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameHelper.Native;

namespace GameHelper;

/// <summary>
/// Выделение области экрана. Итоговые координаты — <b>физические пиксели</b> (как у GetCursorPos / SetCursorPos),
/// чтобы совпадать с автоматизацией при любом масштабе Windows (125%, 150%, несколько мониторов).
/// Визуальная рамка — в WPF (DIP), на точность сохранённого прямоугольника не влияет.
/// </summary>
public partial class RegionPickerWindow : Window
{
    private Point _dragStart;
    private bool _dragging;
    private bool _physicalAnchorOk;
    private int _physStartX;
    private int _physStartY;

    public ScreenRect? SelectedRegion { get; private set; }

    public RegionPickerWindow()
    {
        InitializeComponent();
        Focusable = true;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        _ = Focus();
    }

    private void Surface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(Surface);
        _physicalAnchorOk = Win32Input.TryGetCursorPos(out _physStartX, out _physStartY);
        Surface.CaptureMouse();
        Band.Visibility = Visibility.Visible;
        Canvas.SetLeft(Band, _dragStart.X);
        Canvas.SetTop(Band, _dragStart.Y);
        Band.Width = 0;
        Band.Height = 0;
    }

    private void Surface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var pos = e.GetPosition(Surface);
        var x = Math.Min(_dragStart.X, pos.X);
        var y = Math.Min(_dragStart.Y, pos.Y);
        var w = Math.Abs(pos.X - _dragStart.X);
        var h = Math.Abs(pos.Y - _dragStart.Y);
        Canvas.SetLeft(Band, x);
        Canvas.SetTop(Band, y);
        Band.Width = w;
        Band.Height = h;
    }

    private void Surface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        Surface.ReleaseMouseCapture();

        if (_physicalAnchorOk && Win32Input.TryGetCursorPos(out var endX, out var endY))
        {
            var minX = Math.Min(_physStartX, endX);
            var minY = Math.Min(_physStartY, endY);
            var maxX = Math.Max(_physStartX, endX);
            var maxY = Math.Max(_physStartY, endY);
            var w = Math.Max(1, maxX - minX);
            var h = Math.Max(1, maxY - minY);
            SelectedRegion = new ScreenRect(minX, minY, w, h);
        }
        else
        {
            // Запасной вариант (GetCursorPos недоступен): старая геометрия — может промахиваться при масштабе DPI
            var pos = e.GetPosition(Surface);
            var x = Math.Min(_dragStart.X, pos.X);
            var y = Math.Min(_dragStart.Y, pos.Y);
            var w = Math.Max(1, Math.Abs(pos.X - _dragStart.X));
            var h = Math.Max(1, Math.Abs(pos.Y - _dragStart.Y));
            var screenX = (int)Math.Round(Left + x);
            var screenY = (int)Math.Round(Top + y);
            var screenW = (int)Math.Round(w);
            var screenH = (int)Math.Round(h);
            SelectedRegion = new ScreenRect(screenX, screenY, screenW, screenH);
        }

        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedRegion = null;
            DialogResult = false;
        }
    }
}
