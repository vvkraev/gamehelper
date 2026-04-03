using System.Runtime.InteropServices;

namespace GameHelper.Native;

/// <summary>Минимальная симуляция мыши и клавиш через user32 (MVP).</summary>
public static class Win32Input
{
    /// <summary>
    /// Опциональный трейс ввода (в UI-сессионный лог). Используется для отладки "дёрганий" модификаторов.
    /// </summary>
    public static Action<string>? InputTrace { get; set; }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const byte VkShift = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu = 0x12; // Alt
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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

    public static void AltDown()
    {
        InputTrace?.Invoke("[Key] Alt DOWN");
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        var s = GetAsyncKeyState(VkMenu);
        InputTrace?.Invoke($"[Key] Alt DOWN state: IsAltDown={(s & 0x8000) != 0} GetAsyncKeyState=0x{(ushort)s:X4}");
    }

    public static void AltUp()
    {
        InputTrace?.Invoke("[Key] Alt UP");
        keybd_event(VkMenu, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        var s = GetAsyncKeyState(VkMenu);
        InputTrace?.Invoke($"[Key] Alt UP state: IsAltDown={(s & 0x8000) != 0} GetAsyncKeyState=0x{(ushort)s:X4}");
    }

    public static void CtrlDown() =>
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);

    public static void CtrlUp() =>
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    /// <summary>Ctrl+Alt+C (копирование описания предмета в PoE2).</summary>
    public static void SendCtrlAltC()
    {
        InputTrace?.Invoke("[Key] Ctrl DOWN");
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        AltDown();
        InputTrace?.Invoke("[Key] C DOWN");
        keybd_event(VkC, 0, 0, UIntPtr.Zero);
        InputTrace?.Invoke("[Key] C UP");
        keybd_event(VkC, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        AltUp();
        InputTrace?.Invoke("[Key] Ctrl UP");
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Ctrl+C (копирование текста / описания в PoE2 для валюты/омена).</summary>
    public static void SendCtrlC()
    {
        InputTrace?.Invoke("[Key] Ctrl DOWN");
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        InputTrace?.Invoke("[Key] C DOWN");
        keybd_event(VkC, 0, 0, UIntPtr.Zero);
        InputTrace?.Invoke("[Key] C UP");
        keybd_event(VkC, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        InputTrace?.Invoke("[Key] Ctrl UP");
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Ctrl+ЛКМ (например перенос стака из ritual stash в инвентарь в PoE2).</summary>
    public static void SendCtrlLeftClick()
    {
        InputTrace?.Invoke("[Key] Ctrl DOWN");
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        ClickLeft();
        InputTrace?.Invoke("[Key] Ctrl UP");
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Ctrl+ПКМ (например частичное снятие стака в stash).</summary>
    public static void SendCtrlRightClick()
    {
        InputTrace?.Invoke("[Key] Ctrl DOWN");
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        ClickRight();
        InputTrace?.Invoke("[Key] Ctrl UP");
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void ReleaseCtrl()
    {
        keybd_event(VkC, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Сбросить Ctrl/Alt после сбоя.</summary>
    public static void ReleaseCtrlAlt()
    {
        keybd_event(VkC, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        // аварийный сброс: делаем KEYUP напрямую (без SendInput)
        keybd_event(VkMenu, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Сбросить модификаторы на случай прерванного цикла.</summary>
    public static void ReleaseShift()
    {
        keybd_event(VkShift, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static bool IsShiftDown() =>
        (GetAsyncKeyState(VkShift) & 0x8000) != 0;

    public static bool IsAltDown() =>
        (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
}
