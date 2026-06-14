using System.Runtime.InteropServices;

namespace GameHelper.Native;

/// <summary>Системная горячая клавиша: работает без фокуса на окне и при скрытии в трей.</summary>
internal static class GlobalHotkey
{
    internal const int WmHotkey = 0x0312;

    /// <summary>Идентификаторы для отмены крафта по Esc (6 вариантов модификаторов).</summary>
    internal const int CraftCancelHotkeyId = 0x47E1;
    internal const int CraftCancelHotkeyIdShift = 0x47E2;
    internal const int CraftCancelHotkeyIdCtrl = 0x47E3;
    internal const int CraftCancelHotkeyIdAlt = 0x47E4;
    internal const int CraftCancelHotkeyIdCtrlShift = 0x47E5;
    internal const int CraftCancelHotkeyIdAltShift = 0x47E6;

    // Каждый пользовательский хоткей занимает блок из 8 ID (base..base+7).
    // Все 8 комбинаций {±Alt, ±Ctrl, ±Shift} регистрируются поверх выбранных модификаторов,
    // чтобы горячая клавиша срабатывала даже когда крафт зажимает Shift/Ctrl через keybd_event.
    internal const int TrayToggleHotkeyIdBase   = 0x4800;
    internal const int OpenLogHotkeyIdBase      = 0x4810;
    internal const int CraftStartStopHotkeyIdBase  = 0x4820;
    internal const int ReforgeStartStopHotkeyIdBase     = 0x4830;
    internal const int AutoReforgeStartStopHotkeyIdBase = 0x4840;
    private  const int VariantsPerHotkey = 8;

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

    internal static bool TryRegisterTrayToggle(IntPtr hwnd, uint vk, uint modifiers) =>
        RegisterAllVariants(hwnd, TrayToggleHotkeyIdBase, vk, modifiers);

    internal static void UnregisterTrayToggle(IntPtr hwnd) =>
        UnregisterAllVariants(hwnd, TrayToggleHotkeyIdBase);

    internal static bool TryRegisterOpenLog(IntPtr hwnd, uint vk, uint modifiers) =>
        RegisterAllVariants(hwnd, OpenLogHotkeyIdBase, vk, modifiers);

    internal static void UnregisterOpenLog(IntPtr hwnd) =>
        UnregisterAllVariants(hwnd, OpenLogHotkeyIdBase);

    internal static bool TryRegisterCraftStartStop(IntPtr hwnd, uint vk, uint modifiers) =>
        RegisterAllVariants(hwnd, CraftStartStopHotkeyIdBase, vk, modifiers);

    internal static void UnregisterCraftStartStop(IntPtr hwnd) =>
        UnregisterAllVariants(hwnd, CraftStartStopHotkeyIdBase);

    internal static bool TryRegisterReforgeStartStop(IntPtr hwnd, uint vk, uint modifiers) =>
        RegisterAllVariants(hwnd, ReforgeStartStopHotkeyIdBase, vk, modifiers);

    internal static void UnregisterReforgeStartStop(IntPtr hwnd) =>
        UnregisterAllVariants(hwnd, ReforgeStartStopHotkeyIdBase);

    internal static bool TryRegisterAutoReforgeStartStop(IntPtr hwnd, uint vk, uint modifiers) =>
        RegisterAllVariants(hwnd, AutoReforgeStartStopHotkeyIdBase, vk, modifiers);

    internal static void UnregisterAutoReforgeStartStop(IntPtr hwnd) =>
        UnregisterAllVariants(hwnd, AutoReforgeStartStopHotkeyIdBase);

    // -----------------------------------------------------------------------

    /// <summary>
    /// Регистрирует хоткей (vk + userMods) в 8 вариантах: к пользовательским модификаторам
    /// добавляются все подмножества {Alt, Ctrl, Shift}. Это позволяет хоткею срабатывать
    /// даже когда крафт-сервис программно удерживает один или несколько модификаторов.
    /// </summary>
    private static bool RegisterAllVariants(IntPtr hwnd, int idBase, uint vk, uint userMods)
    {
        if (hwnd == IntPtr.Zero || vk == 0)
            return false;

        UnregisterAllVariants(hwnd, idBase);

        var seen = new HashSet<uint>();
        var anyOk = false;

        for (var extra = 0; extra < VariantsPerHotkey; extra++)
        {
            uint extraMods = ((extra & 1) != 0 ? ModAlt     : 0)
                           | ((extra & 2) != 0 ? ModControl : 0)
                           | ((extra & 4) != 0 ? ModShift   : 0);
            uint fullMods = userMods | extraMods;

            if (!seen.Add(fullMods))
                continue; // дубликат (extraMods — подмножество userMods)

            if (RegisterHotKey(hwnd, idBase + extra, fullMods, vk))
                anyOk = true;
        }

        return anyOk;
    }

    private static void UnregisterAllVariants(IntPtr hwnd, int idBase)
    {
        if (hwnd == IntPtr.Zero)
            return;
        for (var i = 0; i < VariantsPerHotkey; i++)
            UnregisterHotKey(hwnd, idBase + i);
    }
}
