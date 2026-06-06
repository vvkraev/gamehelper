using System.IO;
using System.Text.Json;
using System.Windows;

namespace GameHelper.Services;

/// <summary>
/// Сохраняет и восстанавливает позицию и размер окон между запусками.
/// Вызов: <c>WindowGeometryStore.Attach(this, "UniqueKey")</c> в конструкторе окна.
/// </summary>
public static class WindowGeometryStore
{
    private sealed record Geometry(double Left, double Top, double Width, double Height, bool Maximized);

    private static readonly string FilePath =
        Path.Combine(ProjectPaths.GetProjectRoot(), "window_geometry.json");

    private static Dictionary<string, Geometry> _cache = LoadFromDisk();

    public static void Attach(Window window, string key)
    {
        window.Loaded += (_, _) => Restore(window, key);
        window.Closing += (_, _) => Store(window, key);
    }

    private static void Restore(Window window, string key)
    {
        if (!_cache.TryGetValue(key, out var g))
            return;

        var centerX = g.Left + g.Width / 2;
        var centerY = g.Top + g.Height / 2;
        var onScreen = centerX >= SystemParameters.VirtualScreenLeft
                    && centerX <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                    && centerY >= SystemParameters.VirtualScreenTop
                    && centerY <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

        if (onScreen)
        {
            window.Left = g.Left;
            window.Top = g.Top;
            window.Width = g.Width;
            window.Height = g.Height;
        }

        if (g.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    private static void Store(Window window, string key)
    {
        Rect bounds;
        bool saveMaximized;

        if (window.WindowState == WindowState.Normal)
        {
            bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            saveMaximized = false;
        }
        else
        {
            // Maximized or Minimized: RestoreBounds gives pre-state bounds with valid screen coordinates
            bounds = window.RestoreBounds;
            saveMaximized = window.WindowState == WindowState.Maximized;
        }

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _cache[key] = new Geometry(bounds.Left, bounds.Top, bounds.Width, bounds.Height, saveMaximized);
        SaveToDisk();
    }

    private static Dictionary<string, Geometry> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, Geometry>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void SaveToDisk()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
