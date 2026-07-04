using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class OmenActivationServiceTests
{
    // ── Фикстуры ─────────────────────────────────────────────────────────────

    private const string OmenName = "Omen of Dextral Exaltation";

    private static string OmenClipText(string name = OmenName) =>
        $"Item Class: Omen\r\nRarity: Currency\r\n{name}";

    private static IReadOnlyList<ScreenRect> MakeCells(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ScreenRect(i * 40, 0, 36, 36))
            .ToList();

    private static OmenActionConfig Cfg(int index, string name = OmenName) =>
        new() { OmenName = name, StashCellIndex = index };

    private static OmenActivationService MakeSvc(
        Func<ScreenRect, CancellationToken, Task<string>>? readClip = null,
        Func<ScreenRect, bool>? isActivated = null)
    {
        var svc = new OmenActivationService
        {
            MouseActionDelayMs = 0,
            ClipboardDelayMs = 0,
        };
        svc._testReadClipboard = readClip;
        svc._testIsActivatedOverride = isActivated;
        svc._testActionLog = new List<string>();
        return svc;
    }

    // ── Валидация (до любых кликов) ────────────────────────────────────────

    [Fact]
    public async Task ActivateFromStash_NegativeIndex_ThrowsArgumentException()
    {
        var svc = MakeSvc();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ActivateFromStashAsync(Cfg(-1), MakeCells(5), null, default));
        Assert.Empty(svc._testActionLog!);
        Assert.Contains("StashCellIndex", ex.Message);
    }

    [Fact]
    public async Task ActivateFromStash_CellIndexOutOfRange_ThrowsArgumentException()
    {
        var svc = MakeSvc();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ActivateFromStashAsync(Cfg(10), MakeCells(10), null, default));
        Assert.Empty(svc._testActionLog!);
        Assert.Contains("ячеек: 10", ex.Message);
    }

    // ── Последовательность действий ───────────────────────────────────────

    [Fact]
    public async Task ActivateFromStash_OmenFound_NotActivated_PerformsReadThenRClick()
    {
        var svc = MakeSvc(
            readClip: (_, _) => Task.FromResult(OmenClipText()),
            isActivated: _ => false);
        var callCount = 0;
        svc._testIsActivatedOverride = _ => ++callCount > 1;  // активирован после клика

        var result = await svc.ActivateFromStashAsync(Cfg(2), MakeCells(15), null, default);

        Assert.True(result);
        Assert.Equal(new[] { "ReadClipboard[2]", "RClick[2]" }, svc._testActionLog);
    }

    [Fact]
    public async Task ActivateFromStash_OmenAlreadyActivated_SkipsRClick()
    {
        var svc = MakeSvc(
            readClip: (_, _) => Task.FromResult(OmenClipText()),
            isActivated: _ => true);  // уже активирован

        var result = await svc.ActivateFromStashAsync(Cfg(5), MakeCells(15), null, default);

        Assert.True(result);
        Assert.Equal(new[] { "ReadClipboard[5]" }, svc._testActionLog);
        Assert.DoesNotContain(svc._testActionLog!, a => a.StartsWith("RClick"));
    }

    [Fact]
    public async Task ActivateFromStash_WrongOmenInCell_ReturnsFalse_NoRClick()
    {
        var svc = MakeSvc(
            readClip: (_, _) => Task.FromResult(OmenClipText("Omen of Sinistral Exaltation")),
            isActivated: _ => false);

        var result = await svc.ActivateFromStashAsync(Cfg(0), MakeCells(10), null, default);

        Assert.False(result);
        Assert.Equal(new[] { "ReadClipboard[0]" }, svc._testActionLog);
        Assert.DoesNotContain(svc._testActionLog!, a => a.StartsWith("RClick"));
    }

    [Fact]
    public async Task ActivateFromStash_EmptyClipboard_ReturnsFalse_NoRClick()
    {
        var svc = MakeSvc(
            readClip: (_, _) => Task.FromResult(""),
            isActivated: _ => false);

        var result = await svc.ActivateFromStashAsync(Cfg(0), MakeCells(10), null, default);

        Assert.False(result);
        Assert.DoesNotContain(svc._testActionLog!, a => a.StartsWith("RClick"));
    }

    [Fact]
    public async Task ActivateFromStash_RClickButRedBorderNotAppearing_ReturnsFalse()
    {
        var svc = MakeSvc(
            readClip: (_, _) => Task.FromResult(OmenClipText()),
            isActivated: _ => false);  // красная рамка не появляется даже после клика

        var result = await svc.ActivateFromStashAsync(Cfg(0), MakeCells(10), null, default);

        Assert.False(result);
        Assert.Contains("RClick[0]", svc._testActionLog!);
    }
}
