using League.Models;

namespace League.Services;

/// <summary>
/// Words for a player's health: the active player's real stats from 2999, everyone else's estimate from base stats,
/// items and Tank Engine stacks. The estimate is labelled approximate and lists what it cannot see.
/// </summary>
public static class PlayerHealthText
{
    private const string Effective =
        "有效生命 = 生命 × (1 + 抗性/100)，即打掉这些生命需要的物理 / 法术伤害，未计穿透。";

    public static PlayerHealth? Describe(
        HealthEstimate? estimate,
        int level,
        ItemStats items,
        AugmentEffects augments,
        LiveChampionStats? actual
    )
    {
        if (actual != null)
        {
            var real = new HealthEstimate(actual.MaxHealth, actual.Armor, actual.MagicResist);
            string comparison =
                estimate != null && estimate.Health > 0
                    ? $"\n同一公式估算生命 {estimate.Health:0}，实际高 {(real.Health / estimate.Health - 1) * 100:0}%："
                        + "差额来自估算看不到的加成，其他玩家的估算也可能偏低这么多。"
                    : "";
            return new(
                Line(real, ""),
                $"2999 报告的实际属性：生命 {real.Health:0}、护甲 {real.Armor:0}、魔抗 {real.MagicResist:0}，"
                    + "已包含游戏里的全部加成。\n"
                    + Effective
                    + comparison
            );
        }

        if (estimate == null || level <= 0)
        {
            return null;
        }

        string tank = augments.Parts.Length > 0 ? $" + 已记录的海克斯：{augments.Summary}" : "";
        return new(
            Line(estimate, "约 "),
            $"{level} 级基础属性 + 装备（生命 +{items.Health:0}、护甲 +{items.Armor:0}、魔抗 +{items.MagicResist:0}）"
                + $"{tank}：生命 {estimate.Health:0}、护甲 {estimate.Armor:0}、魔抗 {estimate.MagicResist:0}。\n"
                + Effective
                + "\n未含未记录的海克斯、属性碎片、叠层装备与被动、魄罗佳肴和模式平衡调整，实际通常更高。"
        );
    }

    private static string Line(HealthEstimate health, string prefix) =>
        $"{prefix}{health.Health:0} · 物理 {health.PhysicalEffective:0} · 法术 {health.MagicEffective:0}";
}
