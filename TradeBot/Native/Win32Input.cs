using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TradeBot.Native;

public static class Win32Input
{
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const byte VkMenu = 0x12;
    private const byte VkTab = 0x09;
    private const byte VkControl = 0x11;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYDOWN = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public KEYBDINPUT ki; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] padding; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Активирует окно процесса. Использует AttachThreadInput для надёжного переключения
    /// (SetForegroundWindow не работает из фоновых процессов без этого).
    /// Возвращает false если процесс не найден.
    /// </summary>
    public static bool SwitchToProcess(string processName)
    {
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            var hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) continue;

            // Не вызываем ShowWindow — для fullscreen-игр он сдвигает окно.
            // AttachThreadInput позволяет SetForegroundWindow работать из фонового потока.
            var foreground = GetForegroundWindow();
            var fgThread = GetWindowThreadProcessId(foreground, out _);
            var targetThread = GetWindowThreadProcessId(hwnd, out _);

            if (fgThread != targetThread)
                AttachThreadInput(fgThread, targetThread, true);

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);

            if (fgThread != targetThread)
                AttachThreadInput(fgThread, targetThread, false);

            return true;
        }
        return false;
    }

    // Вводит команду /hideout через буфер обмена (Enter → Ctrl+V → Enter).
    public static void TypeHideoutCommand()
    {
        const byte VkReturn = 0x0D;
        const byte VkV = 0x56;

        PlaceOnClipboard("/hideout");

        keybd_event(VkReturn, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VkReturn, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(50); // ждём серверный тик (33мс) + запас

        keybd_event(VkControl, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        keybd_event(VkV, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VkV, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(5);

        keybd_event(VkReturn, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VkReturn, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static INPUT MakeKey(ushort vk, uint flags) => new INPUT
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = flags },
        padding = new byte[16],
    };

    private static void PlaceOnClipboard(string text)
    {
        const uint GMEM_MOVEABLE = 0x0002;
        if (!OpenClipboard(IntPtr.Zero)) return;
        EmptyClipboard();
        var bytes = (System.Text.Encoding.Unicode.GetByteCount(text) + 2); // +2 for null terminator
        var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (hMem == IntPtr.Zero) { CloseClipboard(); return; }
        var ptr = GlobalLock(hMem);
        System.Runtime.InteropServices.Marshal.Copy(System.Text.Encoding.Unicode.GetBytes(text + "\0"), 0, ptr, bytes);
        GlobalUnlock(hMem);
        SetClipboardData(CF_UNICODETEXT, hMem);
        CloseClipboard();
    }

    /// <summary>Список имён процессов у которых есть окно — для диагностики.</summary>
    public static IEnumerable<string> GetWindowedProcessNames() =>
        Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
            .Select(p => p.ProcessName)
            .Distinct()
            .OrderBy(n => n);

    public static bool TryGetCursorPos(out int x, out int y)
    {
        if (!GetCursorPos(out var p)) { x = 0; y = 0; return false; }
        x = p.X; y = p.Y; return true;
    }

    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void CtrlC()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(0x43, 0, 0, UIntPtr.Zero);
        Thread.Sleep(20);
        keybd_event(0x43, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void PressKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        Thread.Sleep(30);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // Нативные методы clipboard — работают из любого потока (не требуют STA).
    public static string? GetClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    public static void ClearClipboard()
    {
        if (OpenClipboard(IntPtr.Zero))
        {
            EmptyClipboard();
            CloseClipboard();
        }
    }

    public static void LeftClick(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void ClickLeft()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void CtrlClickLeft(int x, int y)
    {
        SetCursorPos(x, y);
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void PressAltTab()
    {
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkTab, 0, 0, UIntPtr.Zero);
        keybd_event(VkTab, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
