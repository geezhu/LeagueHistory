using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// Basic-attack arithmetic against the next enemy turret: damage type, mitigation, the melee bonus, fortification
/// and the turret's warming-up shots. The expected values come from the checks in
/// Tests/StatisticsChecks/TurretChecks.cs.
/// </summary>
public class TurretSiegeCalculatorTests
{
    private static readonly LiveMatchEvent EnemyOuterFell = LiveMatchFixture.Structure(
        "TurretKilled",
        500,
        "Turret_TChaos_L1_P3_671196429_0"
    );

    private static readonly LiveMatchEvent EnemyInnerFell = LiveMatchFixture.Structure(
        "TurretKilled",
        500,
        "Turret_TChaos_L1_P2_620863572_0"
    );

    private static readonly LiveChampionStats Ranged = LiveMatchFixture.Attacker(200, 0, 550);

    [Fact]
    public void Evaluate_RangedAttacksAgainstTheOuterTurret_CountsHitsAndSeconds()
    {
        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, Ranged));

        Assert.Equal("敌方一塔", siege!.Target);
        Assert.Equal(12, siege.Hits);
        Assert.Equal(11, siege.Seconds);
        Assert.Equal(57, siege.HitsAlone);
        Assert.False(siege.Magic);
        Assert.Equal(200 / 1.75, siege.HitDamage, 9);
    }

    [Fact]
    public void Evaluate_RangedAttacks_ReportsTheWarmingUpShots()
    {
        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, Ranged));

        Assert.Equal([170, 256, 341, 426], siege!.Shots.Select(shot => Math.Round(shot)));
        Assert.Equal(4, siege.ShotsToDown);
    }

    [Fact]
    public void Evaluate_MeleeChampion_HitsHarderThanARangedOne()
    {
        var melee = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, LiveMatchFixture.Attacker(200, 0, 125)));

        Assert.Equal(10, melee!.Hits);
    }

    [Fact]
    public void Evaluate_AbilityPowerOverAttackDamage_MakesTheHitMagic()
    {
        var mage = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, LiveMatchFixture.Attacker(60, 400, 550)));

        Assert.True(mage!.Magic);
        Assert.Equal(8, mage.Hits);
    }

    [Fact]
    public void Evaluate_FortifiedInnerTurret_ReducesTheHitByTheReduction()
    {
        var fortified = Siege(LiveMatchFixture.SinglePlayer("ARAM", 530, Ranged, EnemyOuterFell));
        var open = Siege(LiveMatchFixture.SinglePlayer("ARAM", 565, Ranged, EnemyOuterFell));

        Assert.Equal("敌方二塔", fortified!.Target);
        Assert.Equal(0.3, fortified.Fortification);
        Assert.Equal(open!.HitDamage * 0.7, fortified.HitDamage);
    }

    [Fact]
    public void Evaluate_InhibitorIsNext_ReturnsNull()
    {
        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 700, Ranged, EnemyOuterFell, EnemyInnerFell));

        Assert.Null(siege);
    }

    [Fact]
    public void Evaluate_NoActivePlayerStats_ReturnsNull()
    {
        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, null));

        Assert.Null(siege);
    }

    [Fact]
    public void Evaluate_ChampionThatSurvivesThirtyShots_ReportsTheShotCap()
    {
        var stats = Ranged with { CurrentHealth = 1_000_000 };

        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, stats));

        Assert.Equal(30, siege!.ShotsToDown);
    }

    [Fact]
    public void Evaluate_PenetrationBeyondTheResist_AmplifiesPastTheRawDamage()
    {
        var stats = LiveMatchFixture.Attacker(10, 400, 550) with { MagicPenetrationFlat = 100 };

        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, stats));

        Assert.True(siege!.Magic, "Ability power with penetration past the resistance takes the magic path.");
        Assert.Equal(300, siege.HitDamage, 9);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 0)]
    public void Evaluate_NoAttackDamageOrSpeed_ReturnsNull(double attackDamage, double attackSpeed)
    {
        var stats = LiveMatchFixture.Attacker(200, 0, 550) with
        {
            AttackDamage = attackDamage,
            AttackSpeed = attackSpeed,
        };

        var siege = Siege(LiveMatchFixture.SinglePlayer("ARAM", 300, stats));

        Assert.Null(siege);
    }

    [Fact]
    public void Evaluate_OwnTeamIsChaos_TargetsTheOtherTeamOuterTurret()
    {
        var game = LiveMatchFixture.WithTeam(LiveMatchFixture.SinglePlayer("ARAM", 300, Ranged), "CHAOS");

        var siege = Siege(game);

        Assert.Equal("敌方一塔", siege!.Target);
    }

    private static TurretSiege? Siege(LiveMatchSnapshot game) =>
        MinionWaveForecaster.Calculate(game, 0)!.Lanes[0].Siege;
}
