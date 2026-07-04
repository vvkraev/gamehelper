using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Тесты цикла DivineCraftService.RunAsync.
/// Win32 и WPF Dispatcher изолированы через _testClipboardSequence и _testEvaluatorOverride.
/// P/Invoke вызовы (AltUp, ReleaseShift) в catch/finally остаются — они безвредны в тестовой среде.
/// </summary>
public sealed class DivineCraftServiceTests
{
    // ── Вспомогательные методы ────────────────────────────────────────────────

    /// <summary>Минимальный валидный текст предмета, который ItemParser распознаёт как Jewel.</summary>
    private static string JewelClipText(string itemClass = "Jewels") => string.Join("\r\n",
        $"Item Class: {itemClass}",
        "Rarity: Rare",
        "Test Jewel",
        "Cobalt Jewel",
        "--------",
        "Item Level: 83");

    private static DivineCraftService MakeService(
        string[]? clipSequence = null,
        Func<CraftConditionPlan, ParsedItem?, (bool, string)>? evaluatorOverride = null)
    {
        var svc = new DivineCraftService
        {
            MouseActionDelayMs = 0,
            ClipboardDelayMs = 0,
            HoverSettleBeforeClipboardMs = 0,
        };

        if (clipSequence is not null)
            svc._testClipboardSequence = new Queue<string>(clipSequence);

        svc._testEvaluatorOverride = evaluatorOverride;
        return svc;
    }

    private static ScreenRect DummyRect() => new(0, 0, 10, 10);

    private static CraftConditionPlan EmptyPlan() => new() { ExpectedItemClass = "Jewels" };

    private static Task<CraftResult> Run(
        DivineCraftService svc,
        CraftConditionPlan? plan = null,
        int maxOps = 10,
        CancellationToken ct = default) =>
        svc.RunAsync(DummyRect(), DummyRect(), plan ?? EmptyPlan(), "", maxOps, maxOps, 0, null, ct);

    // ── StopReason: MaxIterationsReached ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_ConditionNeverMet_ReturnsMaxIterationsReached()
    {
        // Evaluator always returns false → loop runs to MaxIterations
        var svc = MakeService(
            clipSequence: Enumerable.Repeat(JewelClipText(), 5).ToArray(),
            evaluatorOverride: (_, _) => (false, "не выполнено"));

        var result = await Run(svc, maxOps: 5);

        Assert.Equal(ChaosCraftResult.MaxAttemptsReached, result.StopReason);
        Assert.Equal(5, result.Attempts);
    }

    [Fact]
    public async Task RunAsync_MaxIterations_AttemptsEqualsLimit()
    {
        var svc = MakeService(
            clipSequence: Enumerable.Repeat(JewelClipText(), 3).ToArray(),
            evaluatorOverride: (_, _) => (false, "не выполнено"));

        var result = await Run(svc, maxOps: 3);

        Assert.Equal(ChaosCraftResult.MaxAttemptsReached, result.StopReason);
        Assert.Equal(3, result.Attempts);
    }

    // ── StopReason: ConditionMet ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ConditionMetOnFirstCheck_ReturnsFound_ZeroOrbs()
    {
        // Condition met immediately on first read → no orbs applied
        var svc = MakeService(
            clipSequence: new[] { JewelClipText() },
            evaluatorOverride: (_, _) => (true, "выполнено"));

        var result = await Run(svc, maxOps: 10);

        Assert.Equal(ChaosCraftResult.AffixFound, result.StopReason);
        Assert.Equal(0, result.Attempts); // attempt - 1 = 1 - 1 = 0 orbs used
    }

    [Fact]
    public async Task RunAsync_ConditionMetOnThirdCheck_ReturnsFound_TwoOrbs()
    {
        // Checks 1-2: false (apply orb). Check 3: true (stop before orb).
        var callCount = 0;
        var svc = MakeService(
            clipSequence: Enumerable.Repeat(JewelClipText(), 3).ToArray(),
            evaluatorOverride: (_, _) => (++callCount >= 3, callCount >= 3 ? "выполнено" : "не выполнено"));

        var result = await Run(svc, maxOps: 10);

        Assert.Equal(ChaosCraftResult.AffixFound, result.StopReason);
        Assert.Equal(2, result.Attempts); // 2 orbs applied, found on 3rd read
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task RunAsync_ConditionMet_FinalItemTextPreserved()
    {
        const string MatchingText = "Item Class: Jewels\r\nRarity: Rare\r\nGolden Eye\r\nCobalt Jewel\r\n--------\r\nItem Level: 83";
        var svc = MakeService(
            clipSequence: new[] { MatchingText },
            evaluatorOverride: (_, _) => (true, "выполнено"));

        var result = await Run(svc, maxOps: 10);

        Assert.Equal(ChaosCraftResult.AffixFound, result.StopReason);
        Assert.Equal(MatchingText, result.FinalItem);
    }

    // ── StopReason: Cancelled ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TokenCancelledBeforeStart_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var svc = MakeService(
            clipSequence: new[] { JewelClipText() },
            evaluatorOverride: (_, _) => (false, "не выполнено"));

        var result = await Run(svc, maxOps: 5, ct: cts.Token);

        Assert.Equal(ChaosCraftResult.Cancelled, result.StopReason);
    }

    [Fact]
    public async Task RunAsync_TokenCancelledAfterFirstIteration_ReturnsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        var svc = MakeService(
            clipSequence: Enumerable.Repeat(JewelClipText(), 10).ToArray(),
            evaluatorOverride: (_, _) =>
            {
                if (++callCount == 2) cts.Cancel();
                return (false, "не выполнено");
            });

        var result = await Run(svc, maxOps: 10, ct: cts.Token);

        Assert.Equal(ChaosCraftResult.Cancelled, result.StopReason);
    }

    // ── StopReason: EmptyCell ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_EmptyClipboard_ReturnsEmptyCell()
    {
        var svc = MakeService(
            clipSequence: new[] { "" },
            evaluatorOverride: (_, _) => (false, "не выполнено"));

        var result = await Run(svc, maxOps: 5);

        Assert.Equal(ChaosCraftResult.EmptyCell, result.StopReason);
    }

    // ── Защита от пустого сегмента ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ZeroMaxOps_ReturnsFailed()
    {
        var svc = MakeService();

        var result = await Run(svc, maxOps: 0);

        Assert.Equal(ChaosCraftResult.Error, result.StopReason);
    }

    // ── IDivineCraftService ────────────────────────────────────────────────────

    [Fact]
    public void DivineCraftService_ImplementsInterface()
    {
        Assert.IsAssignableFrom<IDivineCraftService>(new DivineCraftService());
    }
}
