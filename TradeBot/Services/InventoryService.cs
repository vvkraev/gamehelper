namespace TradeBot.Services;

public static class InventoryService
{
    public const int Cols = 12;
    public const int Rows = 5;
    public const int Total = Cols * Rows;

    // Сканирует инвентарь через буфер обмена: наводится на каждую ячейку,
    // нажимает Ctrl+C и проверяет есть ли текст предмета.
    // Требования: игра в фокусе, инвентарь открыт.
    // index = col * Rows + row
    public static async Task<bool[]> ScanOccupiedAsync(
        ScreenRect region, Action<string>? progress = null, CancellationToken ct = default)
    {
        var result = new bool[Total];
        var cells = ScreenRect.SplitIntoGrid(region, Cols, Rows);

        for (var i = 0; i < Total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (cx, cy) = cells[i].Center;
            Win32Input.MoveTo(cx, cy);
            await Task.Delay(110, ct);     // ждём появления тултипа
            Win32Input.ClearClipboard();
            await Task.Delay(30, ct);
            Win32Input.CtrlC();
            await Task.Delay(80, ct);      // ждём запись в clipboard

            var text = Win32Input.GetClipboardText();
            result[i] = !string.IsNullOrEmpty(text) && text.Contains("Item Class:");

            if (progress != null && i % Cols == Cols - 1)
                progress($"  сканирование: строка {i / Cols + 1}/{Rows}");
        }

        return result;
    }

    // Ctrl+Click по всем занятым ячейкам (стэш должен быть открыт).
    public static async Task DumpToStashAsync(
        ScreenRect region, bool[] occupied, CancellationToken ct = default)
    {
        var cells = ScreenRect.SplitIntoGrid(region, Cols, Rows);
        for (var i = 0; i < occupied.Length; i++)
        {
            if (!occupied[i]) continue;
            ct.ThrowIfCancellationRequested();
            var (cx, cy) = cells[i].Center;
            Win32Input.CtrlClickLeft(cx, cy);
            await Task.Delay(200, ct);
        }
    }
}
