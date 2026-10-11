using Newtonsoft.Json.Linq;

namespace League.Models;

public static class MatchMetricCatalog
{
    public sealed record Metric(string Title, string Key);

    public sealed record Category(string Title, Metric[] Metrics);

    public static IReadOnlyList<Category> Categories { get; } = Create();

    private static Category[] Create()
    {
        var result = new List<Category>();
        void Group(string name, params (string Title, string Key)[] metrics) =>
            result.Add(new(name, metrics.Select(m => new Metric(m.Title, m.Key)).ToArray()));
        Group(
            "战斗",
            ("战损比", "kda"),
            ("最高连杀", "largestKillingSpree"),
            ("最高多杀", "largestMultiKill"),
            ("控制得分", "timeCCingOthers"),
            ("第一滴血", "firstBloodKill")
        );
        Group(
            "造成伤害",
            ("对英雄的伤害总和", "totalDamageDealtToChampions"),
            ("对英雄的物理伤害", "physicalDamageDealtToChampions"),
            ("对英雄的魔法伤害", "magicDamageDealtToChampions"),
            ("对英雄的真实伤害", "trueDamageDealtToChampions"),
            ("造成的伤害总和", "totalDamageDealt"),
            ("造成的物理伤害", "physicalDamageDealt"),
            ("造成的魔法伤害", "magicDamageDealt"),
            ("造成的真实伤害", "trueDamageDealt"),
            ("最大暴击伤害", "largestCriticalStrike"),
            ("对防御塔的伤害", "damageDealtToTurrets"),
            ("对战略点的伤害", "damageDealtToObjectives")
        );
        Group(
            "承受及治疗伤害",
            ("治疗量", "totalHeal"),
            ("承受伤害", "totalDamageTaken"),
            ("承受的物理伤害", "physicalDamageTaken"),
            ("承受的魔法伤害", "magicDamageTaken"),
            ("承受的真实伤害", "trueDamageTaken"),
            ("自我缓和的伤害", "damageSelfMitigated")
        );
        Group(
            "视野",
            ("视野得分", "visionScore"),
            ("放置守卫", "wardsPlaced"),
            ("摧毁守卫", "wardsKilled"),
            ("购买控制守卫", "visionWardsBoughtInGame")
        );
        Group(
            "收入",
            ("金币获取", "goldEarned"),
            ("金币使用", "goldSpent"),
            ("击杀小兵", "totalMinionsKilled"),
            ("击杀野怪", "neutralMinionsKilled")
        );
        Group("杂项", ("摧毁防御塔", "turretKills"), ("摧毁召唤水晶", "inhibitorKills"));
        return result.ToArray();
    }

    public static long? Number(JObject player, string key)
    {
        var data = player["stats"] as JObject ?? player;
        return data[key]?.Type == JTokenType.Integer ? (long?)data[key] : null;
    }
}
