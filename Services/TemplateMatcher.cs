using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace GameHelper.Services;

/// <summary>
/// Поиск шаблона (PNG-файл) методом скользящего окна с пирамидным ускорением:
/// сначала поиск на уменьшенной копии (÷PyramidScale), затем уточнение в полном разрешении.
/// Для области 2200×1600 с шаблоном 80×80 занимает ~0.3–0.5 с.
/// </summary>
public static class TemplateMatcher
{
    private const int PyramidScale = 4;

    public static Task<TemplateMatchResult?> FindAsync(
        string templatePath,
        ScreenRect searchArea,
        double threshold = 0.80,
        int colorTolerance = 30,
        CancellationToken ct = default)
        => Task.Run(() => Find(templatePath, searchArea, threshold, colorTolerance, ct), ct);

    private static TemplateMatchResult? Find(
        string templatePath,
        ScreenRect searchArea,
        double threshold,
        int colorTolerance,
        CancellationToken ct)
    {
        if (!File.Exists(templatePath))
            return null;

        using var templateBmp = new Bitmap(templatePath);
        using var searchBmp   = ScreenCaptureHelper.CaptureRegion(searchArea);

        int tw = templateBmp.Width, th = templateBmp.Height;
        int sw = searchBmp.Width,  sh = searchBmp.Height;
        if (tw > sw || th > sh) return null;

        var tGray = ExtractGrayscale(templateBmp);
        var sGray = ExtractGrayscale(searchBmp);

        ct.ThrowIfCancellationRequested();

        // ── Шаг 1: поиск на уменьшенной пирамиде ──────────────────────────────
        int ps = PyramidScale;
        var tSmall = Downsample(tGray, tw, th, ps);
        var sSmall = Downsample(sGray, sw, sh, ps);
        int tsw = tw / ps, tsh = th / ps;
        int ssw = sw / ps, ssh = sh / ps;

        // Для низкого разрешения снижаем порог чтобы не пропустить хороший кандидат
        double coarseThreshold = Math.Max(0.0, threshold - 0.15);
        var candidates = FindTopCandidates(tSmall, tsw, tsh, sSmall, ssw, ssh,
                                           colorTolerance, coarseThreshold, ct, maxCount: 5);

        if (candidates.Count == 0) return null;

        // ── Шаг 2: уточнение кандидатов в полном разрешении ──────────────────
        double bestScore = -1;
        int bestX = 0, bestY = 0;

        int refineRadius = ps + 1;
        foreach (var (cx, cy) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            int fullX = cx * ps;
            int fullY = cy * ps;
            int x0 = Math.Max(0, fullX - refineRadius);
            int y0 = Math.Max(0, fullY - refineRadius);
            int x1 = Math.Min(sw - tw, fullX + refineRadius);
            int y1 = Math.Min(sh - th, fullY + refineRadius);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double score = ComputeScore(tGray, tw, th, sGray, sw, x, y, colorTolerance);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        if (bestScore < threshold) return null;

        return new TemplateMatchResult(
            searchArea.X + bestX + tw / 2,
            searchArea.Y + bestY + th / 2,
            bestScore);
    }

    private static List<(int X, int Y)> FindTopCandidates(
        byte[] tGray, int tw, int th,
        byte[] sGray, int sw, int sh,
        int colorTolerance, double threshold,
        CancellationToken ct, int maxCount)
    {
        int total = tw * th;
        int maxMisses = (int)Math.Floor((1.0 - threshold) * total);
        var result = new List<(int, int, double)>(); // x, y, score

        for (int y = 0; y <= sh - th; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x <= sw - tw; x++)
            {
                int misses = 0;
                bool rejected = false;
                for (int ty = 0; ty < th && !rejected; ty++)
                {
                    int sRow = (y + ty) * sw + x;
                    int tRow = ty * tw;
                    for (int tx = 0; tx < tw; tx++)
                        if (Math.Abs(sGray[sRow + tx] - tGray[tRow + tx]) > colorTolerance
                            && ++misses > maxMisses)
                        { rejected = true; break; }
                }
                if (!rejected)
                    result.Add((x, y, 1.0 - (double)misses / total));
            }
        }

        return result
            .OrderByDescending(r => r.Item3)
            .Take(maxCount)
            .Select(r => (r.Item1, r.Item2))
            .ToList();
    }

    private static double ComputeScore(
        byte[] tGray, int tw, int th,
        byte[] sGray, int sw,
        int ox, int oy, int colorTolerance)
    {
        int total = tw * th, misses = 0;
        for (int ty = 0; ty < th; ty++)
        {
            int sRow = (oy + ty) * sw + ox;
            int tRow = ty * tw;
            for (int tx = 0; tx < tw; tx++)
                if (Math.Abs(sGray[sRow + tx] - tGray[tRow + tx]) > colorTolerance)
                    misses++;
        }
        return 1.0 - (double)misses / total;
    }

    private static byte[] Downsample(byte[] gray, int w, int h, int scale)
    {
        int nw = w / scale, nh = h / scale;
        var result = new byte[nw * nh];
        for (int y = 0; y < nh; y++)
        for (int x = 0; x < nw; x++)
        {
            int sum = 0, count = 0;
            for (int dy = 0; dy < scale; dy++)
            for (int dx = 0; dx < scale; dx++)
            {
                int sy = y * scale + dy, sx = x * scale + dx;
                if (sy < h && sx < w) { sum += gray[sy * w + sx]; count++; }
            }
            result[y * nw + x] = count > 0 ? (byte)(sum / count) : (byte)0;
        }
        return result;
    }

    /// <summary>Захватывает регион экрана и сохраняет его как PNG-шаблон.</summary>
    public static void CaptureTemplate(ScreenRect region, string savePath)
    {
        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        using var bmp = ScreenCaptureHelper.CaptureRegion(region);
        bmp.Save(savePath, ImageFormat.Png);
    }

    private static byte[] ExtractGrayscale(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var result = new byte[w * h];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
                                 ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var raw = new byte[Math.Abs(stride) * h];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            for (int y = 0; y < h; y++)
            {
                int rowOff = y * stride, dstOff = y * w;
                for (int x = 0; x < w; x++)
                {
                    int p = rowOff + x * 4;
                    result[dstOff + x] = (byte)(77 * raw[p + 2] / 256
                                               + 150 * raw[p + 1] / 256
                                               + 29  * raw[p]     / 256);
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return result;
    }
}

public sealed record TemplateMatchResult(int ScreenX, int ScreenY, double Score);
