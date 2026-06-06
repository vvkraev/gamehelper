using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameHelper.Native;
using GameHelper.Services;

namespace GameHelper;

/// <summary>
/// Выделение области экрана. Итоговые координаты — <b>физические пиксели</b> (как у GetCursorPos / SetCursorPos),
/// чтобы совпадать с автоматизацией при любом масштабе Windows (125%, 150%, несколько мониторов).
/// Визуальная рамка — в WPF (DIP), на точность сохранённого прямоугольника не влияет.
/// </summary>
public partial class RegionPickerWindow : Window
{
    private System.Windows.Point _dragStart;
    private bool _dragging;
    private bool _physicalAnchorOk;
    private int _physStartX;
    private int _physStartY;
    private readonly int _gridCols;
    private readonly int _gridRows;

    public ScreenRect? SelectedRegion { get; private set; }

    /// <summary>Ячейки сетки после выделения (только если задана сетка X×Y).</summary>
    public IReadOnlyList<ScreenRect>? SelectedCells { get; private set; }

    public RegionPickerWindow(int gridColumns = 0, int gridRows = 0)
    {
        _gridCols = gridColumns;
        _gridRows = gridRows;
        InitializeComponent();
        WindowGeometryStore.Attach(this, "RegionPickerWindow");
        Focusable = true;
        Loaded += OnLoaded;
        if (_gridCols > 0 && _gridRows > 0)
        {
            HintText.Text =
                $"Зажмите ЛКМ и выделите прямоугольник (сетка {_gridCols}×{_gridRows}). Линии показывают ячейки. Esc — отмена.";
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        System.Windows.Controls.Panel.SetZIndex(GridLayer, 1);
        _ = Focus();
    }

    private void Surface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        GridLayer.Children.Clear();
        _dragStart = e.GetPosition(Surface);
        _physicalAnchorOk = Win32Input.TryGetCursorPos(out _physStartX, out _physStartY);
        Surface.CaptureMouse();
        Band.Visibility = Visibility.Visible;
        Canvas.SetLeft(Band, _dragStart.X);
        Canvas.SetTop(Band, _dragStart.Y);
        Band.Width = 0;
        Band.Height = 0;
    }

    private void Surface_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
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
        RedrawGridOverlay(x, y, w, h);
    }

    private void RedrawGridOverlay(double bandLeft, double bandTop, double bandW, double bandH)
    {
        GridLayer.Children.Clear();
        if (_gridCols < 2 && _gridRows < 2)
            return;
        if (bandW < 2 || bandH < 2)
            return;

        const double stroke = 1;
        var brush = System.Windows.Media.Brushes.White;
        for (var c = 1; c < _gridCols; c++)
        {
            var lx = bandLeft + c * bandW / _gridCols;
            GridLayer.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = lx,
                Y1 = bandTop,
                X2 = lx,
                Y2 = bandTop + bandH,
                Stroke = brush,
                StrokeThickness = stroke,
                IsHitTestVisible = false,
            });
        }

        for (var r = 1; r < _gridRows; r++)
        {
            var ly = bandTop + r * bandH / _gridRows;
            GridLayer.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = bandLeft,
                Y1 = ly,
                X2 = bandLeft + bandW,
                Y2 = ly,
                Stroke = brush,
                StrokeThickness = stroke,
                IsHitTestVisible = false,
            });
        }
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
            if (_gridCols > 0 && _gridRows > 0)
                SelectedCells = ScreenRect.SplitIntoGrid(SelectedRegion.Value, _gridCols, _gridRows);
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
            if (_gridCols > 0 && _gridRows > 0)
                SelectedCells = ScreenRect.SplitIntoGrid(SelectedRegion.Value, _gridCols, _gridRows);
        }

        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        if (_dragging)
        {
            _dragging = false;
            Surface.ReleaseMouseCapture();
        }

        SelectedRegion = null;
        SelectedCells = null;
        DialogResult = false;
    }
}
