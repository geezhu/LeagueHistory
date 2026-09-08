namespace League.Models;

// Chart categories are independent of the full statistics table.
public static class MatchChartMetricCatalog
{
    public static IReadOnlyList<MatchMetricCatalog.Category> Categories { get; } = new MatchMetricCatalog.Category[]
    {
        new("对英雄造成伤害", new MatchMetricCatalog.Metric[]
        {
            new("对英雄的总伤害", "totalDamageDealtToChampions"),
            new("对英雄的物理伤害", "physicalDamageDealtToChampions"),
            new("对英雄的魔法伤害", "magicDamageDealtToChampions"),
            new("对英雄的真实伤害", "trueDamageDealtToChampions")
        }),
        new("总造成伤害", new MatchMetricCatalog.Metric[]
        {
            new("造成物理伤害", "physicalDamageDealt"),
            new("造成魔法伤害", "magicDamageDealt"),
            new("造成真实伤害", "trueDamageDealt"),
            new("对防御塔的总伤害", "damageDealtToTurrets"),
            new("对战略点的总伤害", "damageDealtToObjectives")
        }),
        new("承受伤害和治疗", new MatchMetricCatalog.Metric[]
        {
            new("输出治疗效果", "totalHeal"),
            new("承受伤害", "totalDamageTaken"),
            new("承受物理伤害", "physicalDamageTaken"),
            new("承受魔法伤害", "magicDamageTaken"),
            new("承受的真实伤害", "trueDamageTaken"),
            new("自我缓和的伤害", "damageSelfMitigated")
        }),
        new("收入", new MatchMetricCatalog.Metric[]
        {
            new("获得金钱", "goldEarned"),
            new("花费金钱", "goldSpent")
        }),
        new("视野", new MatchMetricCatalog.Metric[]
        {
            new("视野得分", "visionScore"),
            new("放置守卫", "wardsPlaced"),
            new("摧毁守卫", "wardsKilled"),
            new("购买控制守卫", "visionWardsBoughtInGame")
        }),
        new("野怪和小兵", new MatchMetricCatalog.Metric[]
        {
            new("击杀小兵", "totalMinionsKilled"),
            // The reference is cropped here; retain the existing supported jungle count.
            new("击杀野怪", "neutralMinionsKilled")
        })
    };
}
