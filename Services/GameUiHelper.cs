using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>Конфигурация открытия стэша через OCR.</summary>
public sealed record StashOpenConfig(
    ScreenRect SearchRect,
    string SearchText = "STASH",
    ScreenRect IsOpenCheckRect = default,
    string IsOpenCheckText = "Stash",
    int OpenDelayMs = 2000);

/// <summary>
/// Методы для открытия и верификации стандартных игровых интерфейсов:
/// стэш, станок перековки, магазин Ange.
/// Каждый метод идемпотентен: проверяет текущее состояние перед кликом.
/// </summary>
public static class GameUiHelper
{
    /// <summary>
    /// Убеждается что стэш открыт.
    /// Алгоритм: сначала OCR-проверка IsOpen → если нет, OCR-поиск иконки + клик + верификация.
    /// </summary>
    public static async Task<bool> EnsureStashOpenAsync(
        StashOpenConfig cfg,
        IProgress<string>? log,
        CancellationToken ct)
    {
        // Проверка: стэш уже открыт?
        var checkTarget = cfg.IsOpenCheckRect.Width > 0 && cfg.IsOpenCheckRect.Height > 0
            ? WindowsOcrTextLocator.NormalizeForMatch(cfg.IsOpenCheckText)
            : null;

        if (checkTarget is not null)
        {
            var alreadyOpen = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                cfg.IsOpenCheckRect, checkTarget, null, ct).ConfigureAwait(false);
            if (alreadyOpen is not null)
            {
                log?.Report($"[Стэш] Уже открыт (найдено «{alreadyOpen.Value.MatchedLineText}»).");
                return true;
            }
        }

        if (cfg.SearchRect.Width <= 0 || cfg.SearchRect.Height <= 0)
        {
            log?.Report("[Стэш] Область OCR-поиска не задана.");
            return false;
        }

        var target = WindowsOcrTextLocator.NormalizeForMatch(cfg.SearchText);
        var delay  = cfg.OpenDelayMs > 0 ? cfg.OpenDelayMs : 2000;

        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var m = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                cfg.SearchRect, target, null, ct, exactMatch: true).ConfigureAwait(false);

            if (m is null)
            {
                log?.Report($"[Стэш] Попытка {attempt}/{maxAttempts}: «{cfg.SearchText}» не найден — жду 1000 мс…");
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            var (lx, ly) = m.Value.BoundsOnScreen.GetInteriorPoint(inset: 1);
            log?.Report($"[Стэш] Попытка {attempt}/{maxAttempts}: клик ({lx},{ly})…");
            Win32Input.MoveTo(lx, ly);
            await Task.Delay(100, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(delay, ct).ConfigureAwait(false);

            if (checkTarget is null)
                return true;

            var verify = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                cfg.IsOpenCheckRect, checkTarget, null, ct).ConfigureAwait(false);
            if (verify is not null)
            {
                log?.Report($"[Стэш] Открыт (попытка {attempt}, найдено «{verify.Value.MatchedLineText}»).");
                return true;
            }

            log?.Report($"[Стэш] Попытка {attempt}/{maxAttempts}: стэш не открылся после клика — жду 1000 мс…");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        log?.Report($"[Стэш] Не удалось открыть за {maxAttempts} попыток.");
        return false;
    }

    /// <summary>
    /// Открывает станок перековки через OCR-поиск → клик → ожидание.
    /// </summary>
    public static async Task<bool> EnsureReforgingBenchOpenAsync(
        ScreenRect searchRect, string searchText,
        int openDelayMs,
        IProgress<string>? log,
        CancellationToken ct)
    {
        if (searchRect.Width <= 0 || searchRect.Height <= 0)
        {
            log?.Report("[Станок] Область OCR-поиска не задана.");
            return false;
        }

        var target = WindowsOcrTextLocator.NormalizeForMatch(searchText);
        if (string.IsNullOrEmpty(target))
        {
            log?.Report("[Станок] OCR-текст пуст.");
            return false;
        }

        var delay = openDelayMs > 0 ? openDelayMs : 2000;

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var m = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                searchRect, target, null, ct).ConfigureAwait(false);

            if (m is null)
            {
                log?.Report($"[Станок] Попытка {attempt}/{maxAttempts}: «{searchText}» не найден.");
                if (attempt < maxAttempts) await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }

            var (lx, ly) = m.Value.BoundsOnScreen.GetInteriorPoint(inset: 1);
            log?.Report($"[Станок] Клик ({lx},{ly})…");
            Win32Input.MoveTo(lx, ly);
            await Task.Delay(100, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return true;
        }

        log?.Report($"[Станок] Не найден за {maxAttempts} попыток.");
        return false;
    }

    /// <summary>
    /// Открывает магазин Ange: NPC-клик → «Manage Shop» → клик.
    /// Если диалог NPC уже открыт (Manage Shop видна) — пропускает клик по NPC.
    /// </summary>
    public static async Task<bool> EnsureAngeOpenAsync(
        ScreenRect traderOcrRect, string traderOcrText,
        ScreenRect manageShopOcrRect, string manageShopOcrText,
        int traderOpenDelayMs, int mouseDelayMs,
        IProgress<string>? log,
        CancellationToken ct,
        ScreenRect shopSubTabRect  = default,
        ScreenRect shopVerifyRect  = default)
    {
        var shopTarget   = WindowsOcrTextLocator.NormalizeForMatch(manageShopOcrText);
        var verifyTarget = "merchant";

        // Витрина уже открыта? (надпись Merchant видна)
        if (shopVerifyRect.Width > 0)
        {
            var alreadyOpen = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                shopVerifyRect, verifyTarget, null, ct).ConfigureAwait(false);
            if (alreadyOpen is not null)
            {
                log?.Report("[Ange] Merchant уже виден — витрина открыта.");
                return true;
            }
        }

        // Диалог уже открыт? Manage Shop уже видна?
        if (manageShopOcrRect.Width > 0 && !string.IsNullOrEmpty(shopTarget))
        {
            var existing = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                manageShopOcrRect, shopTarget, null, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                log?.Report("[Ange] Manage Shop уже видна — кликаем.");
                await ClickManageShopAsync(existing.Value.BoundsOnScreen, mouseDelayMs, traderOpenDelayMs, ct, log);
                await ClickShopSubTabIfSetAsync(shopSubTabRect, mouseDelayMs, ct);
                return await VerifyShopOpenAsync(shopVerifyRect, verifyTarget, log, ct);
            }
        }

        // Кликаем по торговцу чтобы открыть диалог
        if (traderOcrRect.Width <= 0 || string.IsNullOrWhiteSpace(traderOcrText))
        {
            log?.Report("[Ange] OCR-область торговца не задана.");
            return false;
        }

        var traderTarget = WindowsOcrTextLocator.NormalizeForMatch(traderOcrText);
        var traderMatch  = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
            traderOcrRect, traderTarget, log, ct).ConfigureAwait(false);

        if (traderMatch is null)
        {
            log?.Report("[Ange] Торговец не найден по OCR.");
            return false;
        }

        // До 3 попыток открыть диалог торговца
        if (manageShopOcrRect.Width <= 0 || string.IsNullOrEmpty(shopTarget))
        {
            var (tx0, ty0) = traderMatch.Value.BoundsOnScreen.GetInteriorPoint(1);
            log?.Report($"[Ange] Клик по торговцу ({tx0},{ty0})…");
            Win32Input.MoveTo(tx0, ty0);
            await Task.Delay(mouseDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(traderOpenDelayMs, ct).ConfigureAwait(false);
            await ClickShopSubTabIfSetAsync(shopSubTabRect, mouseDelayMs, ct);
            return await VerifyShopOpenAsync(shopVerifyRect, verifyTarget, log, ct);
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Проверяем не открылась ли уже витрина (может, интерфейс просто не успел прогрузиться)
            if (shopVerifyRect.Width > 0)
            {
                var earlyCheck = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(shopVerifyRect, null, ct).ConfigureAwait(false);
                if (WindowsOcrTextLocator.NormalizeForMatch(earlyCheck).Contains(verifyTarget, StringComparison.OrdinalIgnoreCase))
                {
                    log?.Report($"[Ange] Merchant уже виден (попытка {attempt}) — витрина открыта.");
                    return true;
                }
            }

            var (tx, ty) = traderMatch.Value.BoundsOnScreen.GetInteriorPoint(1);
            log?.Report($"[Ange] Клик по торговцу ({tx},{ty}), попытка {attempt}/3…");
            Win32Input.MoveTo(tx, ty);
            await Task.Delay(mouseDelayMs, ct).ConfigureAwait(false);
            Win32Input.ClickLeft();
            await Task.Delay(traderOpenDelayMs, ct).ConfigureAwait(false);

            var shopMatch = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(
                manageShopOcrRect, shopTarget, null, ct).ConfigureAwait(false);

            if (shopMatch is not null)
            {
                var sb = shopMatch.Value.BoundsOnScreen;
                log?.Report($"[Ange] Manage Shop OCR: «{shopMatch.Value.MatchedLineText}» bounds={sb.X},{sb.Y} {sb.Width}×{sb.Height}");
                await ClickManageShopAsync(sb, mouseDelayMs, traderOpenDelayMs, ct, log);
                await ClickShopSubTabIfSetAsync(shopSubTabRect, mouseDelayMs, ct);
                if (await VerifyShopOpenAsync(shopVerifyRect, verifyTarget, log, ct))
                    return true;
                log?.Report($"[Ange] Витрина не открылась после попытки {attempt}/3 — повтор…");
                continue;
            }

            log?.Report($"[Ange] Manage Shop не найдена (попытка {attempt}/3).");
        }

        log?.Report("[Ange] Не удалось открыть витрину после 3 попыток.");
        return false;
    }

    private static async Task<bool> VerifyShopOpenAsync(
        ScreenRect verifyRect, string verifyTarget,
        IProgress<string>? log, CancellationToken ct)
    {
        if (verifyRect.Width <= 0)
            return true; // rect не задан — считаем успехом

        var rawText = await WindowsOcrTextLocator.RecognizeRegionRawTextAsync(verifyRect, null, ct).ConfigureAwait(false);
        var normalized = WindowsOcrTextLocator.NormalizeForMatch(rawText);
        if (normalized.Contains(verifyTarget, StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("[Ange] Merchant найден — витрина открыта.");
            return true;
        }
        var preview = rawText.Replace("\n", " ").Trim();
        if (preview.Length > 80) preview = preview[..80] + "…";
        log?.Report($"[Ange] Merchant не найден. OCR: «{preview}»");
        return false;
    }

    private static async Task ClickShopSubTabIfSetAsync(ScreenRect subTabRect, int mouseDelayMs, CancellationToken ct)
    {
        if (subTabRect.Width <= 0 || subTabRect.Height <= 0) return;
        var (sx, sy) = subTabRect.GetInteriorPoint(1);
        Win32Input.MoveTo(sx, sy);
        await Task.Delay(mouseDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(mouseDelayMs, ct).ConfigureAwait(false);
    }

    private static async Task ClickManageShopAsync(
        ScreenRect bounds, int mouseDelayMs, int settleMs, CancellationToken ct,
        IProgress<string>? log = null)
    {
        var (mx, my) = bounds.GetInteriorPoint(1);
        log?.Report($"[Ange] Клик Manage Shop ({mx},{my}) bounds={bounds.X},{bounds.Y} {bounds.Width}×{bounds.Height}…");
        Win32Input.MoveTo(mx, my);
        await Task.Delay(mouseDelayMs, ct).ConfigureAwait(false);
        Win32Input.ClickLeft();
        await Task.Delay(settleMs, ct).ConfigureAwait(false);
    }
}
