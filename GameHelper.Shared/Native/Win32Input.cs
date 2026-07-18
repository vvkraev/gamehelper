using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GameHelper.Native;

/// <summary>Симуляция мыши, клавиш и управления окнами через user32/kernel32.</summary>
public static class Win32Input
{
    /// <summary>Опциональный трейс ввода (для отладки "дёрганий" модификаторов).</summary>
    public static Action<string>? InputTrace { get; set; }

    // ── Константы ────────────────────────────────────────────────────────────

    private const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP    = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP   = 0x0010;
    private const uint KEYEVENTF_KEYUP       = 0x0002;
    private const uint KEYEVENTF_UNICODE     = 0x0004;
    private const uint INPUT_KEYBOARD        = 1u;
    private const byte VkShift   = 0x10;
    private const byte VkControl = 0x11;
    private const byte VkMenu    = 0x12; // Alt
    private const byte VkTab     = 0x09;
    private const byte VkReturn  = 0x0D;
    private const byte VkC       = 0x43;
    private const byte VkV       = 0x56;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE  = 0x0002;

    // ── Структуры ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT_SI
    {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT_DUMMY
    {
        public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)] public KEYBDINPUT_SI    ki;
        [FieldOffset(0)] public MOUSEINPUT_DUMMY mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SENDINPUT { public uint type; public INPUT_UNION u; }

    // ── P/Invoke: мышь и клавиши ─────────────────────────────────────────────

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
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, SENDINPUT[] pInputs, int cbSize);

    // ── P/Invoke: управление окнами ──────────────────────────────────────────

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);

    // ── P/Invoke: буфер обмена ────────────────────────────────────────────────

    [DllImport("user32.dll")]   private static extern bool   OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]   private static extern bool   CloseClipboard();
    [DllImport("user32.dll")]   private static extern bool   EmptyClipboard();
    [DllImport("user32.dll")]   private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]   private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool   GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);

    // ── Курсор ────────────────────────────────────────────────────────────────

    /// <summary>Возврат false, если SetCursorPos отклонён ОС (см. GetLastWin32Error).</summary>
    public static bool MoveTo(int x, int y) => SetCursorPos(x, y);

    public static bool TryGetCursorPos(out int x, out int y)
    {
        if (!GetCursorPos(out var p)) { x = 0; y = 0; return false; }
        x = p.X; y = p.Y; return true;
    }

    // ── Мышь ─────────────────────────────────────────────────────────────────

    public static void ClickLeft()
    {
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero);
    }

    public static void ClickRight()
    {
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_RIGHTUP,   0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>Перемещает курсор и кликает левой кнопкой (с задержкой 40мс для надёжности).</summary>
    public static void LeftClick(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>Ctrl+ЛКМ по заданным координатам (перенос предмета из стэша).</summary>
    public static void CtrlClickLeft(int x, int y)
    {
        SetCursorPos(x, y);
        keybd_event(VkControl, 0, 0,             UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP,   0, 0, 0, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ── Модификаторы ─────────────────────────────────────────────────────────

    public static void ShiftDown() => keybd_event(VkShift,   0, 0,               UIntPtr.Zero);
    public static void ShiftUp()   => keybd_event(VkShift,   0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    public static void CtrlDown()  => keybd_event(VkControl, 0, 0,               UIntPtr.Zero);
    public static void CtrlUp()    => keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

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

    // ── Сброс модификаторов ───────────────────────────────────────────────────

    public static void ReleaseCtrl()
    {
        keybd_event(VkC,       0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void ReleaseCtrlAlt()
    {
        keybd_event(VkC,       0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkMenu,    0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void ReleaseShift() => keybd_event(VkShift, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    // ── Состояние клавиш ─────────────────────────────────────────────────────

    public static bool IsShiftDown()      => (GetAsyncKeyState(VkShift) & 0x8000) != 0;
    public static bool IsAltDown()        => (GetAsyncKeyState(VkMenu)  & 0x8000) != 0;
    public static bool IsLeftButtonDown() => (GetAsyncKeyState(0x01)    & 0x8000) != 0;

    // ── Клавиатура ────────────────────────────────────────────────────────────

    public static void PressKey(byte vk)
    {
        keybd_event(vk, 0, 0,               UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void KeyDown(byte vk) => keybd_event(vk, 0, 0,               UIntPtr.Zero);
    public static void KeyUp(byte vk)   => keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    public static void PressEnter() => PressKey(VkReturn);

    public static void PressAltTab()
    {
        keybd_event(VkMenu, 0, 0,               UIntPtr.Zero);
        keybd_event(VkTab,  0, 0,               UIntPtr.Zero);
        keybd_event(VkTab,  0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ── Комбинации клавиш ─────────────────────────────────────────────────────

    /// <summary>Ctrl+Alt+C — копирование описания предмета в PoE2.</summary>
    public static void SendCtrlAltC()
    {
        try
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
        finally
        {
            // Гарантированный сброс — даже если исключение между DOWN и UP
            keybd_event(VkC,       0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VkMenu,    0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    /// <summary>Ctrl+C — копирование текста / описания в PoE2 для валюты/омена.</summary>
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

    /// <summary>Ctrl+A — выделить всё (для очистки поля ввода перед вводом цены).</summary>
    public static void SendCtrlA()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(0x41,      0, 0, UIntPtr.Zero);
        keybd_event(0x41,      0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Ctrl+ЛКМ без перемещения курсора (перенос стака из ritual stash в инвентарь).</summary>
    public static void SendCtrlLeftClick()
    {
        InputTrace?.Invoke("[Key] Ctrl DOWN");
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        ClickLeft();
        InputTrace?.Invoke("[Key] Ctrl UP");
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Ctrl+ПКМ без перемещения курсора (частичное снятие стака в stash).</summary>
    public static void SendCtrlRightClick()
    {
        InputTrace?.Invoke("[Key] Ctrl DOWN");
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        ClickRight();
        InputTrace?.Invoke("[Key] Ctrl UP");
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ── Ввод текста ───────────────────────────────────────────────────────────

    /// <summary>Вводит строку через SendInput (Unicode events) — для полей ввода цены.</summary>
    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new SENDINPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[2 * i]     = new SENDINPUT { type = INPUT_KEYBOARD, u = new INPUT_UNION { ki = new KEYBDINPUT_SI { wScan = text[i], dwFlags = KEYEVENTF_UNICODE } } };
            inputs[2 * i + 1] = new SENDINPUT { type = INPUT_KEYBOARD, u = new INPUT_UNION { ki = new KEYBDINPUT_SI { wScan = text[i], dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<SENDINPUT>());
    }

    // ── Буфер обмена ──────────────────────────────────────────────────────────

    /// <summary>Записывает текст в буфер обмена через WinAPI (работает из любого потока, не требует STA).</summary>
    public static void PlaceOnClipboard(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        EmptyClipboard();
        var bytes = Encoding.Unicode.GetByteCount(text) + 2; // +2 для null-терминатора
        var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (hMem == IntPtr.Zero) { CloseClipboard(); return; }
        var ptr = GlobalLock(hMem);
        Marshal.Copy(Encoding.Unicode.GetBytes(text + "\0"), 0, ptr, bytes);
        GlobalUnlock(hMem);
        SetClipboardData(CF_UNICODETEXT, hMem);
        CloseClipboard();
    }

    /// <summary>Читает текст из буфера обмена через WinAPI (работает из любого потока, не требует STA).</summary>
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
        if (OpenClipboard(IntPtr.Zero)) { EmptyClipboard(); CloseClipboard(); }
    }

    // ── Команды игрового чата ─────────────────────────────────────────────────

    /// <summary>Вводит команду /hideout через буфер обмена (Enter → Ctrl+V → Enter).</summary>
    public static void TypeHideoutCommand()
    {
        PlaceOnClipboard("/hideout");

        keybd_event(VkReturn, 0, 0,               UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VkReturn, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(50); // ждём серверный тик (33мс) + запас

        keybd_event(VkControl, 0, 0,               UIntPtr.Zero);
        keybd_event(VkV,       0, 0,               UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VkV,       0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VkControl, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(5);

        keybd_event(VkReturn, 0, 0,               UIntPtr.Zero);
        Thread.Sleep(5);
        keybd_event(VkReturn, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ── Управление окнами ─────────────────────────────────────────────────────

    /// <summary>
    /// Активирует окно процесса на переднем плане. Использует AttachThreadInput для надёжного
    /// переключения из фонового потока (SetForegroundWindow иначе игнорируется ОС).
    /// Возвращает false если процесс не найден или у него нет окна.
    /// </summary>
    public static bool SwitchToProcess(string processName)
    {
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            var hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) continue;

            var foreground    = GetForegroundWindow();
            var fgThread      = GetWindowThreadProcessId(foreground, out _);
            var targetThread  = GetWindowThreadProcessId(hwnd, out _);

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

    /// <summary>Список имён процессов у которых есть окно — для диагностики.</summary>
    public static IEnumerable<string> GetWindowedProcessNames() =>
        Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
            .Select(p => p.ProcessName)
            .Distinct()
            .OrderBy(n => n);

    // ── Устаревший алиас ──────────────────────────────────────────────────────

    /// <inheritdoc cref="SendCtrlC"/>
    public static void CtrlC() => SendCtrlC();
}
