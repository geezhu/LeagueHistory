using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>Data Dragon's champion list, with the stats block as the CDN writes it.</summary>
public class ChampionBaseStatsTests
{
    // Newtonsoft reads unquoted property names, which keeps the stats blocks below short.
    private static JObject List(string stats) =>
        JObject.Parse("{data:{Annie:{key:'1',partype:'法力值',stats:{" + stats + "}}}}");

    [Fact]
    public void ParseAll_FullStats_ReadsTheOffensiveAndResourceStats()
    {
        var list = List(
            "hp:560,hpperlevel:96,armor:23,armorperlevel:4,spellblock:30,spellblockperlevel:1.3,"
                + "attackdamage:50,attackdamageperlevel:2.65,attackspeed:0.61,mp:418,mpperlevel:25,movespeed:335"
        );

        var stats = ChampionBaseStats.ParseAll(list)[1];

        Assert.Equal(
            (50.0, 2.65, 0.61, 418.0, 25.0, 335.0, "法力值"),
            (
                stats.AttackDamage,
                stats.AttackDamagePerLevel,
                stats.AttackSpeed,
                stats.Mana,
                stats.ManaPerLevel,
                stats.MoveSpeed,
                stats.Resource
            )
        );
    }

    [Fact]
    public void ParseAll_OptionalStatMissing_ReadsZero()
    {
        var list = List("hp:560,hpperlevel:96,armor:23,armorperlevel:4,spellblock:30,spellblockperlevel:1.3");

        var stats = ChampionBaseStats.ParseAll(list)[1];

        Assert.Equal(0, stats.AttackDamage);
    }

    [Fact]
    public void ParseAll_HealthMissing_LeavesTheChampionOut()
    {
        var list = List("armor:23");

        var stats = ChampionBaseStats.ParseAll(list);

        Assert.Empty(stats);
    }
}
