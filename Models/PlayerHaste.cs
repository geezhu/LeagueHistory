namespace League.Models;

/// <summary>A player's ability haste: 2999's real value for the active player, an estimate for everyone else.</summary>
public sealed record PlayerHaste(double Value, bool Estimated, string Details)
{
    /// <summary>
    /// Items and the general haste of the recorded augments; runes, unrecorded augments and mode bonuses are not
    /// visible to 2999 for other players, so the estimate can only be lower than the real value.
    /// </summary>
    public static PlayerHaste Describe(ItemStats items, AugmentEffects augments, LiveChampionStats? actual)
    {
        if (actual != null)
        {
            return new(
                actual.AbilityHaste,
                false,
                $"2999 报告的技能急速 {actual.AbilityHaste:0}，与游戏属性面板相同。"
            );
        }

        double total = items.AbilityHaste + augments.AbilityHaste;
        string parts =
            augments.AbilityHaste > 0
                ? $"装备 {items.AbilityHaste:0} + 海克斯 {augments.AbilityHaste:0}"
                : $"装备 {items.AbilityHaste:0}";
        return new(
            total,
            true,
            $"估算技能急速 {total:0}（{parts}）；未含符文、未记录的海克斯和模式加成，实际可能更高。"
        );
    }
}
