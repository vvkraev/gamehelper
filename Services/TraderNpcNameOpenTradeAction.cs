using GameHelper.Native;

namespace GameHelper.Services;

/// <summary>
/// По области экрана ищет подпись имени NPC (OCR), ставит курсор в центр подписи и выполняет Ctrl+ЛКМ (открытие торговли в PoE2).
/// </summary>
public static class TraderNpcNameOpenTradeAction
{
    private static int JitterDelay(int baseMs, Random? rng = null)
    {
        rng ??= Random.Shared;
        if (baseMs <= 0)
            return 0;
        var lo = Math.Max(0, baseMs - 15);
        var hi = baseMs + 25;
        return rng.Next(lo, hi + 1);
    }

    public static async Task<bool> RunAsync(
        ScreenRect searchArea,
        string npcNameRaw,
        int mouseActionDelayMs,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var target = WindowsOcrTextLocator.NormalizeForMatch(npcNameRaw);
        if (string.IsNullOrEmpty(target))
        {
            log?.Report("Имя NPC пустое после нормализации.");
            return false;
        }

        log?.Report($"Поиск имени NPC (OCR), цель (нормализовано): «{target}», область {searchArea.X},{searchArea.Y} {searchArea.Width}×{searchArea.Height}");

        var match = await WindowsOcrTextLocator.TryFindNormalizedSubstringAsync(searchArea, target, log, cancellationToken)
            .ConfigureAwait(false);
        if (match is not { } found)
            return false;

        var (cx, cy) = found.BoundsOnScreen.GetInteriorPoint(inset: 1);
        log?.Report($"Курсор → ({cx},{cy}), затем Ctrl+ЛКМ");

        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        if (!Win32Input.MoveTo(cx, cy))
        {
            log?.Report($"SetCursorPos({cx},{cy}) не удался.");
            return false;
        }

        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);
        Win32Input.SendCtrlLeftClick();
        await Task.Delay(JitterDelay(mouseActionDelayMs), cancellationToken).ConfigureAwait(false);

        log?.Report("Ctrl+ЛКМ выполнен.");
        return true;
    }
}
