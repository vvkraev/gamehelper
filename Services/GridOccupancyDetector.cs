using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GameHelper.Services;

/// <summary>
/// Определяет занятые ячейки координатной сетки по снимку экрана.
///
/// Принцип: пустые ячейки PoE2 очень тёмные (средняя яркость ~10-20 из 255),
/// занятые — значительно ярче из-за арта предмета.
/// Один снимок всей сетки → попиксельный анализ каждой ячейки без лишних кликов.
/// </summary>
public static class GridOccupancyDetector
{
    /// <summary>
    /// Порог средней яркости (0-255): ячейки ярче этого значения считаются занятыми.
    /// Пустые ячейки PoE2 ≈ 15, арт даже простого предмета ≈ 40+.
    /// </summary>
    public static int BrightnessThreshold { get; set; } = 28;

    /// <summary>
    /// Из списка ячеек возвращает только занятые (содержащие предмет).
    /// Делает один снимок bounding box всей сетки и анализирует каждую ячейку.
    /// </summary>
    public static IReadOnlyList<ScreenRect> FilterOccupied(IReadOnlyList<ScreenRect> cells)
    {
        if (cells.Count == 0) return cells;

        var minX = int.MaxValue; var minY = int.MaxValue;
        var maxX = int.MinValue; var maxY = int.MinValue;
        foreach (var c in cells)
        {
            if (c.X           < minX) minX = c.X;
            if (c.Y           < minY) minY = c.Y;
            if (c.X + c.Width > maxX) maxX = c.X + c.Width;
            if (c.Y + c.Height> maxY) maxY = c.Y + c.Height;
        }

        var area = new ScreenRect(minX, minY, maxX - minX, maxY - minY);
        using var bitmap = ScreenCaptureHelper.CaptureRegion(area);

        // Копируем всё изображение в байтовый массив (Format32bppArgb: BGRA, 4 байта/пиксель)
        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[bmpData.Height * bmpData.Stride];
        Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
        bitmap.UnlockBits(bmpData);

        var stride = bmpData.Stride;
        var result = new List<ScreenRect>(cells.Count);

        foreach (var cell in cells)
        {
            var brightness = MeanBrightness(pixels, stride,
                cell.X - area.X, cell.Y - area.Y, cell.Width, cell.Height, inset: 2);
            if (brightness > BrightnessThreshold)
                result.Add(cell);
        }

        return result;
    }

    /// <summary>
    /// Средняя яркость (R+G+B)/3 по выборке пикселей в ячейке с отступом <paramref name="inset"/>.
    /// Шаг выборки — каждые 3 пикселя для скорости.
    /// </summary>
    private static double MeanBrightness(byte[] pixels, int stride,
        int cellX, int cellY, int cellW, int cellH, int inset)
    {
        var x0 = cellX + inset;
        var y0 = cellY + inset;
        var x1 = cellX + cellW - inset;
        var y1 = cellY + cellH - inset;
        if (x1 <= x0 || y1 <= y0) return 0;

        const int step = 3;
        long sum   = 0;
        int  count = 0;

        for (var py = y0; py < y1; py += step)
        {
            var rowOffset = py * stride;
            for (var px = x0; px < x1; px += step)
            {
                var i = rowOffset + px * 4; // BGRA
                sum += pixels[i]     // B
                     + pixels[i + 1] // G
                     + pixels[i + 2];// R
                count += 3;
            }
        }

        return count > 0 ? (double)sum / count : 0;
    }
}
