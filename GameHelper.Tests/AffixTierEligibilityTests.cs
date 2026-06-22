using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class AffixTierEligibilityTests
{
    private static readonly OrbCraftProperties ChaosOrb   = OrbCraftProperties.Known["Chaos Orb"];
    private static readonly OrbCraftProperties PerfectChaos = OrbCraftProperties.Known["Perfect Chaos Orb"];
    private static readonly OrbCraftProperties DivineOrb  = OrbCraftProperties.Known["Divine Orb"];

    private static AffixLibraryEntry Tier(int tier, int modifierLevel) =>
        new() { AffixTier = tier, AffixTierLevel = modifierLevel };

    // Типичное семейство: T1(75), T2(50), T3(30), T4(1)
    private static readonly IReadOnlyList<AffixLibraryEntry> FourTiers =
    [
        Tier(1, 75), Tier(2, 50), Tier(3, 30), Tier(4, 1),
    ];

    [Fact]
    public void ChaosOrb_HighIlvl_ReturnsAllTiers()
    {
        var result = AffixTierEligibility.GetEligibleTiers(FourTiers, itemIlvl: 80, ChaosOrb);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void ChaosOrb_Ilvl60_ReturnsTiersUpToIlvl()
    {
        // ilvl=60: T1(75) исключён, T2(50), T3(30), T4(1) — проходят
        var result = AffixTierEligibility.GetEligibleTiers(FourTiers, itemIlvl: 60, ChaosOrb);
        Assert.Equal(3, result.Count);
        Assert.DoesNotContain(result, e => e.AffixTier == 1);
    }

    [Fact]
    public void PerfectChaosOrb_ReturnsOnlyHighTiers()
    {
        // min=50: только T1(75) и T2(50) проходят
        var result = AffixTierEligibility.GetEligibleTiers(FourTiers, itemIlvl: 80, PerfectChaos);
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.True(e.AffixTier <= 2));
    }

    [Fact]
    public void T1Exception_WhenNoTiersPass_ReturnsT1()
    {
        // ilvl=20, min=1: только T4(1) и T3(30 > 20) — T4 проходит, T3/T2/T1 нет
        // Ждём T4 (не T1-исключение)
        var result = AffixTierEligibility.GetEligibleTiers(FourTiers, itemIlvl: 20, ChaosOrb);
        Assert.Single(result);
        Assert.Equal(4, result[0].AffixTier);

        // ilvl=80, min=80: только T1(75) — нет! ни один не проходит (75 < 80)
        // T1-исключение: возвращаем T1
        var highMinOrb = new OrbCraftProperties { Name = "Test", MinModifierLevel = 80, SelectsTier = true };
        var t1exc = AffixTierEligibility.GetEligibleTiers(FourTiers, itemIlvl: 80, highMinOrb);
        Assert.Single(t1exc);
        Assert.Equal(1, t1exc[0].AffixTier);
    }

    [Fact]
    public void DivineOrb_SelectsTierFalse_ReturnsEmpty()
    {
        var result = AffixTierEligibility.GetEligibleTiers(FourTiers, itemIlvl: 80, DivineOrb);
        Assert.Empty(result);
    }

    [Fact]
    public void NullAffixTierLevel_TreatedAsOne()
    {
        var entry = new AffixLibraryEntry { AffixTier = 1, AffixTierLevel = null };
        Assert.True(AffixTierEligibility.IsEligible(entry, itemIlvl: 1, minModifierLevel: 1));
        Assert.True(AffixTierEligibility.IsEligible(entry, itemIlvl: 80, minModifierLevel: 1));
        Assert.False(AffixTierEligibility.IsEligible(entry, itemIlvl: 80, minModifierLevel: 50));
    }

    [Fact]
    public void EmptyFamily_ReturnsEmpty()
    {
        var result = AffixTierEligibility.GetEligibleTiers([], itemIlvl: 80, ChaosOrb);
        Assert.Empty(result);
    }
}
