using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Howling Abyss turret stats over the game clock, their effective health and the damage multiplier structures take
/// late. The expected values come from the checks in Tests/StatisticsChecks/TurretChecks.cs.
/// </summary>
public class TurretRulesTests
{
    private static TurretRules Rules => TurretRules.HowlingAbyss;

    [Theory]
    [InlineData(LaneStructureKind.Outer, 0, 1300, 185, 60)]
    [InlineData(LaneStructureKind.Outer, 600, 1300, 275, 81)]
    [InlineData(LaneStructureKind.Outer, 720, 1300, 293, 40)]
    [InlineData(LaneStructureKind.Inner, 1200, 3900, 385, 84)]
    [InlineData(LaneStructureKind.InhibitorTurret, 0, 3900, 205, 63)]
    [InlineData(LaneStructureKind.Nexus, 0, 1800, 195, 60)]
    public void At_TurretKind_ReturnsStatsAtTheGameTime(
        LaneStructureKind kind,
        double time,
        double health,
        double damage,
        double resist
    )
    {
        var stats = Rules.At(kind, time, false);

        Assert.Equal(new TurretStats(health, damage, resist), stats);
    }

    [Theory]
    [InlineData(LaneStructureKind.Inhibitor)]
    [InlineData(LaneStructureKind.Base)]
    public void At_StructureThatIsNotATurret_ReturnsNull(LaneStructureKind kind)
    {
        var stats = Rules.At(kind, 0, false);

        Assert.Null(stats);
    }

    [Theory]
    [InlineData(LaneStructureKind.Nexus, true, 1200)]
    [InlineData(LaneStructureKind.Nexus, false, 1800)]
    [InlineData(LaneStructureKind.Outer, true, 1300)]
    public void At_Mayhem_LowersOnlyTheNexusTurrets(LaneStructureKind kind, bool mayhem, double expected)
    {
        var stats = Rules.At(kind, 0, mayhem);

        Assert.Equal(expected, stats!.Health);
    }

    [Theory]
    [InlineData(1300, 185, 60, 2080)]
    [InlineData(1000, 100, 0, 1000)]
    [InlineData(1000, 100, 100, 2000)]
    public void EffectiveHealth_Resistances_CountsDamageBeforeMitigation(
        double health,
        double damage,
        double resist,
        double expected
    )
    {
        var stats = new TurretStats(health, damage, resist);

        Assert.Equal(expected, stats.EffectiveHealth);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1080, 1.0)]
    [InlineData(1440, 1.125)]
    [InlineData(1800, 1.25)]
    [InlineData(3600, 1.25)]
    public void StructureDamage_GameTime_RisesFromEighteenToThirtyMinutes(double time, double expected)
    {
        double multiplier = Rules.StructureDamage(time);

        Assert.Equal(expected, multiplier);
    }

    [Theory]
    [InlineData(false, 0.3, 60)]
    [InlineData(true, 0.4, 70)]
    public void FortificationFor_Mode_ReturnsTheReductionAndItsWindow(bool mayhem, double reduction, double seconds)
    {
        var fortification = Rules.FortificationFor(mayhem);

        Assert.Equal((reduction, seconds), fortification);
    }
}
