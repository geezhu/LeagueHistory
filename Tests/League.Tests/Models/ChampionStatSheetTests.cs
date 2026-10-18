using League.Models;

namespace League.Tests.Models;

public class ChampionStatSheetTests
{
    private static readonly ChampionBaseStats Annie = new(560, 96, 23, 4, 30, 1.3)
    {
        AttackDamage = 50,
        AttackDamagePerLevel = 2.65,
        AttackSpeed = 0.61,
        AttackSpeedPerLevel = 1.36,
        Mana = 418,
        ManaPerLevel = 25,
        MoveSpeed = 335,
        AttackRange = 625,
    };

    private static readonly ChampionRecord Record = new() { AttackSpeedRatio = 0.625 };

    [Fact]
    public void Estimate_Items_AddToTheGrownBase()
    {
        var items = new ItemStats(0, 0, 0, 0) { AttackDamage = 40, AbilityPower = 90 };

        var sheet = ChampionStatSheet.Estimate(Annie, Record, 1, items, AugmentEffects.None, 0);

        Assert.Equal((50.0, 90.0, 90.0), (sheet.AttackDamage.Base, sheet.AttackDamage.Total, sheet.AbilityPower));
    }

    [Fact]
    public void Estimate_LevelGrowth_CountsAsBonusAttackSpeed()
    {
        var sheet = ChampionStatSheet.Estimate(Annie, Record, 18, ItemStats.None, AugmentEffects.None, 0);

        Assert.Equal(1.36 * 17 / 100, sheet.BonusAttackSpeed, 6);
    }

    [Fact]
    public void Estimate_BonusAttackSpeed_UsesTheChampionsRatio()
    {
        var items = new ItemStats(0, 0, 0, 0, AttackSpeed: 40);

        var sheet = ChampionStatSheet.Estimate(Annie, Record, 1, items, AugmentEffects.None, 0);

        Assert.Equal(0.61 + 0.625 * 0.4, sheet.AttackSpeed.Total, 6);
    }

    [Fact]
    public void Estimate_HasteArgument_IsTheSheetsHaste()
    {
        var sheet = ChampionStatSheet.Estimate(Annie, Record, 1, ItemStats.None, AugmentEffects.None, 35);

        Assert.Equal(35, sheet.AbilityHaste);
    }

    [Fact]
    public void FromLive_PanelTotals_KeepTheGrowthAsBase()
    {
        var live = new LiveChampionStats(1500, 60, 40, 20, 0, 0, 0, 0) { AttackDamage = 120, AttackSpeed = 0.61 };

        var sheet = ChampionStatSheet.FromLive(Annie, Record, 1, live);

        Assert.Equal(70, sheet.AttackDamage.Bonus, 6);
    }

    [Fact]
    public void FromLive_CritDamagePercent_BecomesAMultiplier()
    {
        var live = new LiveChampionStats(1500, 60, 40, 20, 0, 0, 0, 0) { CritDamage = 215 };

        var sheet = ChampionStatSheet.FromLive(Annie, Record, 1, live);

        Assert.Equal(2.15, sheet.CritDamage, 6);
    }

    [Fact]
    public void FromLive_UnknownChampion_UsesTheTotalsAsBase()
    {
        var live = new LiveChampionStats(1500, 60, 40, 20, 0, 0, 0, 0) { AttackDamage = 120 };

        var sheet = ChampionStatSheet.FromLive(null, ChampionRecord.None, 1, live);

        Assert.Equal(0, sheet.AttackDamage.Bonus);
    }

    [Theory]
    [InlineData(0, 0, 90)]
    [InlineData(0, 1, 0)]
    [InlineData(4, 2, 0.25)]
    [InlineData(8, 0, 0.3)]
    [InlineData(9, 0, 1.75)]
    [InlineData(12, 2, 400)]
    public void Stat_GameDataNumber_PicksTheStat(int stat, int formula, double expected)
    {
        var sheet = new ChampionStatSheet(9, true)
        {
            AbilityPower = 90,
            BonusAttackSpeed = 0.25,
            CritChance = 0.3,
            Health = new(1600, 2000),
        };

        var value = sheet.Stat(stat, formula);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Stat_UnknownNumber_ReturnsNull()
    {
        var sheet = new ChampionStatSheet(9, true);

        var value = sheet.Stat(16, 0);

        Assert.Null(value);
    }
}
