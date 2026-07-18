using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GameHelper.Services;

/// <summary>Захват области экрана в физических пикселях (как <see cref="ScreenRect"/> и Win32).</summary>
public static class ScreenCaptureHelper
{
    public static Bitmap CaptureRegion(ScreenRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(rect), "Ширина и высота области захвата должны быть > 0.");

        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rect.X, rect.Y, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        }

        return bmp;
    }

    /// <summary>Целочисленное увеличение (×2, ×3) — для OCR мелкого текста в игре.</summary>
    public static Bitmap ScaleByIntegerFactor(Bitmap source, int factor)
    {
        if (factor <= 1)
            throw new ArgumentOutOfRangeException(nameof(factor));
        ArgumentNullException.ThrowIfNull(source);

        var w = source.Width * factor;
        var h = source.Height * factor;
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, 0, 0, w, h);
        }

        return dst;
    }
}
