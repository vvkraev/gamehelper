using System.Runtime.InteropServices;

namespace GameHelper.Native;

/// <summary>Минимальная симуляция мыши и клавиш через user32 (MVP).</summary>
internal static class Win32Input
{
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkC = 0x43;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Возврат false, если SetCursorPos отклонён ОС (см. GetLastWin32Error).</summary>
    public static bool MoveTo(int x, int y) =>
        SetCursorPos(x, y);

    public static bool TryGetCursorPos(out int x, out int y)
    {
        if (!GetCursorPos(out var p))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = p.X;
        y = p.Y;
        return true;
    }

    public static void ClickLeft()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void ClickRight()
    {
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void ShiftDown() =>
        keybd_event(VkShift, 0, 0, UIntPtr.Zero);

    public static void ShiftUp() =>
        keybd_event(VkShift, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    public static void SendCtrlC()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Сбросить модификаторы на случай прерванного цикла.</summary>
    public static void ReleaseShift()
    {
        keybd_event(VkShift, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
