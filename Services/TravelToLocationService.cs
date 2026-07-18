using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// Переход между локациями: OCR-поиск «Waypoint» → клик → задержка → клик по кнопке локации → задержка загрузки.
/// </summary>
public sealed class TravelToLocationService
{
    private readonly int _mouseDelayMs;
    private readonly double _jitter;

    public TravelToLocationService(int mouseDelayMs = 120, double jitter = 0.30)
    {
        _mouseDelayMs = mouseDelayMs;
        _jitter = jitter;
    }

    public async Task<bool> TravelAsync(
        TravelActionConfig config,
        IProgress<string>? log,
        CancellationToken ct,
        ScreenRect locationNameArea = default)
    {
        ct.ThrowIfCancellationRequested();

        // Отпускаем Ctrl/Alt перед навигацией — иначе на экране остаётся всплывающее окно предмета
        Win32Input.ReleaseCtrlAlt();
        await DelayAsync(_mouseDelayMs, ct).ConfigureAwait(false);

        // Логируем оба прохода OCR (1× и 2×) для диагностики
        log?.Report("[Travel] Поиск «Waypoint» через OCR…");
        var raw1 = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(
            config.WaypointSearchArea, log: null, cancellationToken: ct).ConfigureAwait(false);
        log?.Report($"[Travel] OCR 1×: «{raw1.Replace('\n', ' ').Trim()}»");

        using var bmp2 = Services.ScreenCaptureHelper.CaptureRegion(config.WaypointSearchArea);
        using var bmp2x = Services.ScreenCaptureHelper.ScaleByIntegerFactor(bmp2, 2);
        var raw2 = await WindowsOcrTextLocator.RecognizeBitmapCollapsedAsync(bmp2x, ct).ConfigureAwait(false);
        log?.Report($"[Travel] OCR 2×: «{raw2.Replace('\n', ' ').Trim()}»");

        var rawSearch = string.IsNullOrWhiteSpace(config.WaypointOcrText) ? "waypoint" : config.WaypointOcrText;
        var searchText = WindowsOcrTextLocator.NormalizeForMatch(rawSearch);
        var match = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
            config.WaypointSearchArea,
            searchText,
            log: null,
            cancellationToken: ct).ConfigureAwait(false);

        if (match is null)
        {
            log?.Report("[Travel] «Waypoint» не найден в указанной области. Проверьте область поиска и текст на экране.");
            return false;
        }

        var (wx, wy) = match.Value.BoundsOnScreen.Center;
        log?.Report($"[Travel] «Waypoint» найден на ({wx}, {wy}), кликаем…");

        Win32Input.MoveTo(wx, wy);
        await DelayAsync(_mouseDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();

        // Ждём открытия меню Waypoint
        await DelayAsync(config.AfterWaypointDelayMs, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // Кликаем по кнопке перехода к локации (центр области)
        var (lx, ly) = config.LocationButtonArea.GetRandomInteriorPoint(1, centerAreaFraction: 0.7);
        log?.Report($"[Travel] Клик по кнопке локации ({lx}, {ly})…");
        Win32Input.MoveTo(lx, ly);
        await DelayAsync(_mouseDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();

        // Ждём загрузки
        log?.Report($"[Travel] Ожидание загрузки {config.LoadingDelayMs} мс…");
        await DelayAsync(config.LoadingDelayMs, ct).ConfigureAwait(false);

        // OCR-верификация: проверяем что оказались в нужной локации
        if (!string.IsNullOrEmpty(config.ExpectedLocation) && locationNameArea.Width > 0)
        {
            log?.Report($"[Travel] Проверяем локацию OCR: ожидаем «{config.ExpectedLocation}»…");
            const int maxAttempts = 8;
            for (var i = 0; i < maxAttempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                var detected = await LocationDetector.DetectAsync(locationNameArea, ct).ConfigureAwait(false);
                if (LocationDetector.LocationMatchesExpected(detected, config.ExpectedLocation))
                {
                    log?.Report($"[Travel] Локация подтверждена: «{detected}»");
                    return true;
                }
                log?.Report($"[Travel] OCR: «{detected}» ≠ «{config.ExpectedLocation}» (попытка {i + 1}/{maxAttempts}), ждём 2с…");
                await DelayAsync(2000, ct).ConfigureAwait(false);
            }
            log?.Report($"[Travel] Локация «{config.ExpectedLocation}» не подтверждена за {maxAttempts} попыток.");
            return false;
        }

        log?.Report("[Travel] Переход выполнен.");
        return true;
    }

    private async Task DelayAsync(int baseMs, CancellationToken ct)
    {
        if (baseMs <= 0) return;
        var jittered = (int)(baseMs * (1.0 + (_jitter * (Random.Shared.NextDouble() * 2 - 1))));
        await Task.Delay(Math.Max(10, jittered), ct).ConfigureAwait(false);
    }
}
