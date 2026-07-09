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
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Ищем надпись «Waypoint» через OCR в заданной области
        log?.Report("[Travel] Поиск «Waypoint» через OCR…");
        var match = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
            config.WaypointSearchArea,
            "waypoint",
            log: null,
            cancellationToken: ct).ConfigureAwait(false);

        if (match is null)
        {
            log?.Report("[Travel] «Waypoint» не найден в указанной области.");
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

        // Кликаем по кнопке перехода к локации
        log?.Report($"[Travel] Клик по кнопке локации ({config.LocationButtonX}, {config.LocationButtonY})…");
        Win32Input.MoveTo(config.LocationButtonX, config.LocationButtonY);
        await DelayAsync(_mouseDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();

        // Ждём загрузки
        log?.Report($"[Travel] Ожидание загрузки {config.LoadingDelayMs} мс…");
        await DelayAsync(config.LoadingDelayMs, ct).ConfigureAwait(false);

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
