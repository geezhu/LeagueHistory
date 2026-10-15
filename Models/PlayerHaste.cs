namespace League.Models;

/// <summary>A player's ability haste: 2999's real value for the active player, an estimate for everyone else.</summary>
public sealed record PlayerHaste(double Value, bool Estimated, string Details)
{
    /// <summary>Purist - Caster turns each 1% of bonus attack speed into this much ability haste.</summary>
    public const double PuristHastePerAttackSpeed = 0.65;

    /// <summary>
    /// Items and the general haste of the recorded augments, plus Purist - Caster's conversion of bonus attack speed
    /// (items' and the level's); runes, unrecorded augments and mode bonuses are not visible to 2999 for other
    /// players, so the estimate can only be lower than the real value. The player's own value from 2999 already holds
    /// all of it.
    /// </summary>
    public static PlayerHaste Describe(
        ItemStats items,
        AugmentEffects augments,
        LiveChampionStats? actual,
        double bonusAttackSpeed = 0
    )
    {
        if (actual != null)
        {
            return new(
                actual.AbilityHaste,
                false,
                $"2999 报告的技能急速 {actual.AbilityHaste:0}，与游戏属性面板相同。"
            );
        }

        double purist = augments.Cooldown.Purist ? Math.Max(0, bonusAttackSpeed) * PuristHastePerAttackSpeed : 0;
        double total = items.AbilityHaste + augments.AbilityHaste + purist;
        string parts =
            $"装备 {items.AbilityHaste:0}"
            + (augments.AbilityHaste > 0 ? $" + 海克斯 {augments.AbilityHaste:0}" : "")
            + (purist > 0 ? $" + 纯粹主义者附加攻速 {bonusAttackSpeed:0}% 转换 {purist:0}" : "");
        return new(
            total,
            true,
            $"估算技能急速 {total:0}（{parts}）；未含符文、未记录的海克斯和模式加成，实际可能更高。"
        );
    }
}
