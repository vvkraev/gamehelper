using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameHelper.Native;

/// <summary>Вывод окна процесса на передний план (для передачи фокуса игре перед вводом).</summary>
public static class ProcessForeground
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Имя исполняемого файла без <c>.exe</c>, например <c>PathOfExileSteam</c>.
    /// </summary>
    public const string PathOfExile2SteamProcessName = "PathOfExileSteam";

    /// <summary>
    /// Активирует главное или первое видимое окно процесса.
    /// </summary>
    /// <returns><c>true</c>, если окно найдено и вызов SetForegroundWindow выполнен.</returns>
    public static bool TryBringProcessToForeground(string processNameWithoutExe)
    {
        foreach (var p in Process.GetProcessesByName(processNameWithoutExe))
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle))
                {
                    BringWindowToForeground(p.MainWindowHandle);
                    return true;
                }

                var hwnd = FindVisibleWindowForProcessId(p.Id);
                if (hwnd != IntPtr.Zero)
                {
                    BringWindowToForeground(hwnd);
                    return true;
                }
            }
            catch
            {
                // процесс мог завершиться
            }
        }

        return false;
    }

    /// <summary>
    /// SW_RESTORE только для свёрнутого окна. Для уже видимого окна повторный SW_RESTORE
    /// может сдвинуть позицию (сохранённый placement ≠ текущий кадр, рамка/DPI).
    /// </summary>
    private static void BringWindowToForeground(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
    }

    private static IntPtr FindVisibleWindowForProcessId(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if ((int)pid != processId)
                return true;
            if (!IsWindowVisible(hWnd))
                return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
