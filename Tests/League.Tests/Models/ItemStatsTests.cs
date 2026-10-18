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
}
