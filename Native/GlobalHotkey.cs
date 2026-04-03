using System.Runtime.InteropServices;

namespace GameHelper.Native;

/// <summary>Системная горячая клавиша: работает без фокуса на окне и при скрытии в трей.</summary>
internal static class GlobalHotkey
{
    internal const int WmHotkey = 0x0312;

    /// <summary>Идентификатор для отмены крафта по Esc (диапазон id для RegisterHotKey в user32).</summary>
    internal const int CraftCancelHotkeyId = 0x47E1;
    internal const int CraftCancelHotkeyIdShift = 0x47E2;
    internal const int CraftCancelHotkeyIdCtrl = 0x47E3;
    internal const int CraftCancelHotkeyIdAlt = 0x47E4;
    internal const int CraftCancelHotkeyIdCtrlShift = 0x47E5;
    internal const int CraftCancelHotkeyIdAltShift = 0x47E6;

    private const uint VkEscape = 27;
    private const uint FsModifiersNone = 0;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal static bool TryRegisterEscape(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        // Регистрируем несколько комбинаций: во время крафта мы иногда удерживаем Shift программно,
        // и "голый" Esc (без модификаторов) тогда не срабатывает.
        var ok = true;
        ok &= RegisterHotKey(hwnd, CraftCancelHotkeyId, FsModifiersNone, VkEscape);
        ok &= RegisterHotKey(hwnd, CraftCancelHotkeyIdShift, ModShift, VkEscape);
        ok &= RegisterHotKey(hwnd, CraftCancelHotkeyIdCtrl, ModControl, VkEscape);
        ok &= RegisterHotKey(hwnd, CraftCancelHotkeyIdAlt, ModAlt, VkEscape);
        ok &= RegisterHotKey(hwnd, CraftCancelHotkeyIdCtrlShift, ModControl | ModShift, VkEscape);
        ok &= RegisterHotKey(hwnd, CraftCancelHotkeyIdAltShift, ModAlt | ModShift, VkEscape);
        return ok;
    }

    internal static void UnregisterCraftCancel(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        UnregisterHotKey(hwnd, CraftCancelHotkeyId);
        UnregisterHotKey(hwnd, CraftCancelHotkeyIdShift);
        UnregisterHotKey(hwnd, CraftCancelHotkeyIdCtrl);
        UnregisterHotKey(hwnd, CraftCancelHotkeyIdAlt);
        UnregisterHotKey(hwnd, CraftCancelHotkeyIdCtrlShift);
        UnregisterHotKey(hwnd, CraftCancelHotkeyIdAltShift);
    }
}
