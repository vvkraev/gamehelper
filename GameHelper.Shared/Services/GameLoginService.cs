using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Выполняет вход в игру после перезапуска клиента.
/// Ожидает текст на экране входа, кликает кнопку, затем аналогично для выбора персонажа.
/// На экране выбора персонажа проверяет наличие иконки домика — если есть, кликает её
/// (прямой вход в хайдаут), иначе — обычную кнопку «Войти в игру».
/// </summary>
public sealed class GameLoginService(GameLoginSettings settings, Action<string> log)
{
    public static string GetIconRefPath() =>
        Path.Combine(AppContext.BaseDirectory, "hideout_icon_ref.png");

    private const int PollIntervalMs = 500;
    private const double IconMatchThreshold = 0.90;

    public async Task<bool> LoginAsync(CancellationToken ct)
    {
        // Шаг 1: экран входа
        if (settings.LoginScreenDetectRegion.HasValue && !string.IsNullOrEmpty(settings.LoginScreenText))
        {
            log($"[Login] Ожидание экрана входа («{settings.LoginScreenText}»)...");
            if (!await WaitForTextAsync(settings.LoginScreenDetectRegion.Value, settings.LoginScreenText, settings.LoginTimeoutMs, ct))
            {
                log("[Login] ✗ Экран входа не появился за отведённое время");
                return false;
            }
            log("[Login] ✓ Экран входа обнаружен");

            if (settings.LoginClickRegion.HasValue)
            {
                await Task.Delay(800, ct);
                var (x, y) = settings.LoginClickRegion.Value.Center;
                Win32Input.LeftClick(x, y);
                log($"[Login] → клик «Войти» ({x},{y})");
            }
        }

        // Шаг 2: выбор персонажа
        if (settings.CharSelectDetectRegion.HasValue && !string.IsNullOrEmpty(settings.CharSelectText))
        {
            log($"[Login] Ожидание выбора персонажа («{settings.CharSelectText}»)...");
            if (!await WaitForTextAsync(settings.CharSelectDetectRegion.Value, settings.CharSelectText, settings.LoginTimeoutMs, ct))
            {
                log("[Login] ✗ Экран выбора персонажа не появился");
                return false;
            }
            log("[Login] ✓ Выбор персонажа обнаружен");

            await Task.Delay(500, ct);

            // Проверяем наличие иконки домика
            var iconRefPath = GetIconRefPath();
            if (settings.HideoutIconRegion.HasValue && settings.HideoutIconClickRegion.HasValue && File.Exists(iconRefPath))
            {
                var iconFound = CheckIconPresent(settings.HideoutIconRegion.Value, iconRefPath);
                if (iconFound)
                {
                    await Task.Delay(500, ct);
                    var (ix, iy) = settings.HideoutIconClickRegion.Value.Center;
                    Win32Input.LeftClick(ix, iy);
                    log($"[Login] → иконка домика найдена, клик ({ix},{iy})");

                    // Экран подтверждения хайдаута — ждём текст для точного тайминга,
                    // но кликаем в любом случае если регион задан
                    if (settings.HideoutConfirmClickRegion.HasValue)
                    {
                        if (settings.HideoutConfirmDetectRegion.HasValue && !string.IsNullOrEmpty(settings.HideoutConfirmText))
                        {
                            log($"[Login] Ожидание подтверждения («{settings.HideoutConfirmText}»)...");
                            var found = await WaitForTextAsync(settings.HideoutConfirmDetectRegion.Value, settings.HideoutConfirmText, 15_000, ct);
                            if (!found)
                                log("[Login] ✗ текст подтверждения не найден — кликаем по заданной позиции");
                        }
                        await Task.Delay(500, ct);
                        var (cx, cy) = settings.HideoutConfirmClickRegion.Value.Center;
                        Win32Input.LeftClick(cx, cy);
                        log($"[Login] → клик подтверждения ({cx},{cy})");
                    }
                    return true;
                }
                log("[Login] → иконка домика не найдена, используем обычную кнопку");
            }

            if (settings.CharSelectClickRegion.HasValue)
            {
                await Task.Delay(500, ct);
                var (x, y) = settings.CharSelectClickRegion.Value.Center;
                Win32Input.LeftClick(x, y);
                log($"[Login] → клик «Войти в игру» ({x},{y})");
            }
        }

        return true;
    }

    /// <summary>Захватывает текущий скриншот области иконки и сохраняет как эталон.</summary>
    public static void CaptureIconReference(ScreenRect region)
    {
        using var bmp = ScreenCaptureHelper.CaptureRegion(region);
        bmp.Save(GetIconRefPath(), ImageFormat.Png);
    }

    private bool CheckIconPresent(ScreenRect region, string iconRefPath)
    {
        try
        {
            using var reference = new Bitmap(iconRefPath);
            using var current = ScreenCaptureHelper.CaptureRegion(region);
            var similarity = ComputeSimilarity(reference, current);
            log($"[Login] сходство с эталоном иконки: {similarity:P1}");
            return similarity >= IconMatchThreshold;
        }
        catch (Exception ex)
        {
            log($"[Login] ✗ ошибка сравнения иконки: {ex.Message}");
            return false;
        }
    }

    private static double ComputeSimilarity(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 0;
        var rect = new Rectangle(0, 0, a.Width, a.Height);
        var dataA = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dataB = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytesA = new byte[dataA.Stride * dataA.Height];
            var bytesB = new byte[dataB.Stride * dataB.Height];
            Marshal.Copy(dataA.Scan0, bytesA, 0, bytesA.Length);
            Marshal.Copy(dataB.Scan0, bytesB, 0, bytesB.Length);
            long same = 0;
            var total = bytesA.Length / 4;
            for (var i = 0; i < bytesA.Length; i += 4)
            {
                if (bytesA[i] == bytesB[i] && bytesA[i + 1] == bytesB[i + 1] && bytesA[i + 2] == bytesB[i + 2])
                    same++;
            }
            return (double)same / total;
        }
        finally
        {
            a.UnlockBits(dataA);
            b.UnlockBits(dataB);
        }
    }

    private async Task<bool> WaitForTextAsync(ScreenRect region, string expected, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var norm = WindowsOcrTextLocator.NormalizeForMatch(expected);
        var lastText = "";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var raw = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(region, null, ct);
            var detected = WindowsOcrTextLocator.NormalizeForMatch(raw);
            if (detected != lastText)
            {
                log($"[Login] OCR: «{raw.Trim()}»");
                lastText = detected;
            }
            if (detected.Contains(norm, StringComparison.Ordinal))
                return true;
            await Task.Delay(PollIntervalMs, ct);
        }
        return false;
    }
}
