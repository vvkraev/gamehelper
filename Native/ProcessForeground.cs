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
                    ShowWindow(p.MainWindowHandle, SwRestore);
                    return SetForegroundWindow(p.MainWindowHandle);
                }

                var hwnd = FindVisibleWindowForProcessId(p.Id);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SwRestore);
                    return SetForegroundWindow(hwnd);
                }
            }
            catch
            {
                // процесс мог завершиться
            }
        }

        return false;
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
