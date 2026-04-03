using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GameHelper.Native;

/// <summary>Показ окна поверх всех без передачи фокуса ввода.</summary>
public static class WindowTopHelper
{
    private static readonly IntPtr HwndTopMost = new(-1);

    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    public static void ShowTopmostWithoutActivation(Window window)
    {
        window.Show();
        var h = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(
            h,
            HwndTopMost,
            0,
            0,
            0,
            0,
            SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
        window.Topmost = true;
    }
}
