using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameHelper.Services;

/// <summary>
/// Записывает WASD-нажатия через глобальный WH_KEYBOARD_LL хук.
/// Клавиши, нажатые в пределах <see cref="GroupGapMs"/> мс друг от друга,
/// объединяются в один <see cref="WalkKeyPress"/> и исполняются одновременно.
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

    /// <summary>Клавиши, нажатые в пределах этого времени, считаются одновременными и объединяются.</summary>
    public int GroupGapMs { get; set; } = 150;

    private static readonly Dictionary<byte, string> WasdMap = new()
    {
        { 0x57, "W" }, { 0x41, "A" }, { 0x53, "S" }, { 0x44, "D" },
    };

    public event Action<List<WalkKeyPress>>? RecordingFinished;

    public bool IsRecording { get; private set; }

    private LowLevelKeyboardProc? _proc;   // держим живым — иначе GC уберёт
    private IntPtr _hook;
    private readonly Dictionary<byte, long> _downAt = new();
    private readonly List<(string Key, long DownAt, long UpAt)> _events = new();
    private bool _hadAnyPress;

    public void Start()
    {
        if (IsRecording) return;
        _downAt.Clear();
        _events.Clear();
        _hadAnyPress = false;
        IsRecording = true;

        _proc = HookProc;
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
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
                        _events.Add((keyName, t0, Stopwatch.GetTimestamp()));
                        _downAt.Remove(vk);
                    }

                    if (_hadAnyPress && _downAt.Count == 0)
                        FinishOnUiThread();
                }
            }
        }
        return CallNextHookEx(_hook, code, wp, lp);
    }

    private void FinishOnUiThread()
    {
        var grouped = GroupEvents(_events, GroupGapMs);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Unhook();
            RecordingFinished?.Invoke(grouped);
        });
    }

    /// <summary>
    /// Группирует события по времени нажатия: события, чьи DownAt попадают в одно
    /// окно GroupGapMs, объединяются в один WalkKeyPress с одновременными клавишами.
    /// Продолжительность группы = максимальная длительность клавиши в группе.
    /// </summary>
    private static List<WalkKeyPress> GroupEvents(
        List<(string Key, long DownAt, long UpAt)> events, int groupGapMs)
    {
        if (events.Count == 0) return [];

        var freq      = Stopwatch.Frequency;
        var gapTicks  = groupGapMs * freq / 1000L;
        var sorted    = events.OrderBy(e => e.DownAt).ToList();
        var result    = new List<WalkKeyPress>();

        var groupStart = sorted[0].DownAt;
        var group      = new List<(string Key, long DownAt, long UpAt)> { sorted[0] };

        for (var i = 1; i < sorted.Count; i++)
        {
            var e = sorted[i];
            if (e.DownAt - groupStart <= gapTicks)
            {
                group.Add(e);
            }
            else
            {
                result.Add(ToWalkKeyPress(group, freq));
                groupStart = e.DownAt;
                group = [e];
            }
        }
        result.Add(ToWalkKeyPress(group, freq));
        return result;
    }

    private static WalkKeyPress ToWalkKeyPress(
        List<(string Key, long DownAt, long UpAt)> group, long freq)
    {
        var maxDuration = (int)(group.Max(k => k.UpAt - k.DownAt) * 1000L / freq);
        return new WalkKeyPress
        {
            Keys      = group.Select(k => k.Key).ToList(),
            DurationMs = maxDuration,
        };
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
