using League.Models;

namespace League.Tests.Models;

/// <summary>Stats from the client's item text, written as its stats block writes them.</summary>
public class ItemStatsTests
{
    [Fact]
    public void Parse_OffensiveStats_ReadsEachKind()
    {
        const string text =
            "<mainText><stats><attention>60</attention>攻击力<br><attention>25%</attention>暴击几率<br>"
            + "<attention>18</attention>穿甲<br><attention>4%</attention>移动速度</stats></mainText>";

        var stats = ItemStats.Parse(text);

        Assert.Equal(
            (60.0, 25.0, 18.0, 4.0),
            (stats.AttackDamage, stats.CritChance, stats.Lethality, stats.MoveSpeedPercent)
        );
    }

    [Fact]
    public void Parse_CasterStats_ReadsPowerManaAndPenetration()
    {
        const string text =
            "<stats><attention>90</attention>法术强度<br><attention>300</attention>法力<br>"
            + "<attention>15</attention>法术穿透<br><attention>45</attention>移动速度</stats>";

        var stats = ItemStats.Parse(text);

        Assert.Equal(
            (90.0, 300.0, 15.0, 45.0),
            (stats.AbilityPower, stats.Mana, stats.MagicPenetration, stats.MoveSpeed)
        );
    }

    [Fact]
    public void Parse_RegenerationPercent_IsNotMana()
    {
        var stats = ItemStats.Parse("<stats><attention>100%</attention>基础法力回复</stats>");

        Assert.Equal(0, stats.Mana);
    }

    [Fact]
    public void Sum_OffensiveStats_AddUp()
    {
        var one = new ItemStats(0, 0, 0, 0) { AttackDamage = 10, AbilityPower = 20 };

        var total = ItemStats.Sum([one, one]);

        Assert.Equal((20.0, 40.0), (total.AttackDamage, total.AbilityPower));
    }

    // ---- descriptions with nothing to read ----

    [Fact]
    public void Parse_NoDescription_IsTheEmptyStats()
    {
        // An item the client has no text for still has to produce something the panel can add.
        Assert.Equal(ItemStats.None, ItemStats.Parse(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<mainText>no stats block here</mainText>")]
    [InlineData("<mainText><passive>只有被动文字</passive></mainText>")]
    public void Parse_NoStatsBlock_IsTheEmptyStats(string text)
    {
        // Everything is read out of the stats block; without one there is nothing to look at, and the passive text
        // around it is deliberately not parsed.
        Assert.Equal(ItemStats.None, ItemStats.Parse(text));
    }

    // ---- bonus attack speed keeps its own field ----

    [Fact]
    public void Parse_BonusAttackSpeed_IsReadRatherThanCountedAsSomethingElse()
    {
        // It is a percentage like the crit and move-speed lines, but it belongs to AttackSpeed - the haste estimate
        // reads it, and folding it into another field would silently change that estimate.
        var stats = ItemStats.Parse("<stats><attention>15%</attention>攻击速度</stats>");

        Assert.Equal(15, stats.AttackSpeed);
        Assert.Equal(0, stats.CritChance);
        Assert.Equal(0, stats.MoveSpeedPercent);
    }

    [Fact]
    public void Parse_TwoAttackSpeedLines_AddUp()
    {
        // An item can carry the line twice (a base and a bonus); both count.
        var stats = ItemStats.Parse(
            "<stats><attention>15%</attention>攻击速度<br><attention>10%</attention>攻击速度</stats>"
        );

        Assert.Equal(25, stats.AttackSpeed);
    }

    [Fact]
    public void Parse_AttackSpeedAlongsideOtherStats_KeepsThemAll()
    {
        // The three loops read separate lines of the same block; none of them may swallow another's.
        const string text =
            "<stats><attention>30</attention>攻击力<br><attention>20%</attention>攻击速度<br>"
            + "<attention>150</attention>生命值</stats>";

        var stats = ItemStats.Parse(text);

        Assert.Equal((30.0, 20.0, 150.0), (stats.AttackDamage, stats.AttackSpeed, stats.Health));
    }
}
