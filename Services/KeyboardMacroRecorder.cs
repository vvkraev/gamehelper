using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameHelper.Services;

/// <summary>
/// Записывает WASD-нажатия через глобальный WH_KEYBOARD_LL хук.
/// Запись останавливается автоматически когда все нажатые клавиши отпущены.
/// </summary>
public sealed class KeyboardMacroRecorder : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc fn, IntPtr hMod, uint tid);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hk, int code, IntPtr wp, IntPtr lp);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? mod);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wp, IntPtr lp);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_KEYUP      = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP   = 0x0105;

    private static readonly Dictionary<byte, string> WasdMap = new()
    {
        { 0x57, "W" }, { 0x41, "A" }, { 0x53, "S" }, { 0x44, "D" },
    };

    public event Action<List<WalkKeyPress>>? RecordingFinished;

    public bool IsRecording { get; private set; }

    private LowLevelKeyboardProc? _proc;     // держим живым — иначе GC уберёт
    private IntPtr _hook;
    private readonly Dictionary<byte, long> _downAt = new();
    private readonly List<WalkKeyPress> _recorded = new();
    private bool _hadAnyPress;

    public void Start()
    {
        if (IsRecording) return;
        _downAt.Clear();
        _recorded.Clear();
        _hadAnyPress = false;
        IsRecording = true;

        _proc = HookProc;
        using var proc = Process.GetCurrentProcess();
        using var mod  = proc.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    private IntPtr HookProc(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lp);
            var vk  = (byte)(kbd.vkCode & 0xFF);

            if (WasdMap.TryGetValue(vk, out var keyName))
            {
                if (wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN)
                {
                    _hadAnyPress = true;
                    _downAt.TryAdd(vk, Stopwatch.GetTimestamp());
                }
                else if (wp == WM_KEYUP || wp == WM_SYSKEYUP)
                {
                    if (_downAt.TryGetValue(vk, out var t0))
                    {
                        var ms = (int)((Stopwatch.GetTimestamp() - t0) * 1000L / Stopwatch.Frequency);
                        _recorded.Add(new WalkKeyPress { Key = keyName, DurationMs = ms });
                        _downAt.Remove(vk);
                    }

                    // Все клавиши отпущены после хотя бы одного нажатия → стоп
                    if (_hadAnyPress && _downAt.Count == 0)
                        FinishOnUiThread();
                }
            }
        }
        return CallNextHookEx(_hook, code, wp, lp);
    }

    private void FinishOnUiThread()
    {
        var result = new List<WalkKeyPress>(_recorded);
        // BeginInvoke — не блокируем хук-колбэк, выходим из него первым
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Unhook();
            RecordingFinished?.Invoke(result);
        });
    }

    private void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook    = IntPtr.Zero;
        _proc    = null;
        IsRecording = false;
    }

    public void Dispose() => Unhook();
}
