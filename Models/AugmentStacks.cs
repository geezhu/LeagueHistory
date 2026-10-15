namespace League.Models;

/// <summary>
/// Where counting starts: the game time the augment was taken, or the time the player corrected the count, and the
/// stacks at that moment. Later events count on top of it, so a correction is never overwritten by a recount.
/// </summary>
public sealed record AugmentStackAnchor(double Time, int Stacks);

/// <summary>The stacks now, with what was counted since the anchor; incomplete when the history cannot vouch for it.</summary>
public sealed record AugmentStackState(int Stacks, int Takedowns, int Deaths, bool Complete, string Note);

/// <summary>
/// Rebuilds augment stacks from the game's champion kill history (2999 events), the same history the death streaks
/// use: a takedown is a kill or an assist by the player, a death is the player as victim. The Wiki does not say how
/// the 65% loss rounds, so it rounds to the nearest stack and the count is shown as approximate.
/// </summary>
public static class AugmentStacks
{
    public const double DeathLoss = 0.65;

    public static AugmentStackState Count(
        LiveMatchSnapshot snapshot,
        string playerKey,
        TrackedAugment augment,
        AugmentStackAnchor anchor
    )
    {
        if (snapshot.Events == null)
        {
            return new(anchor.Stacks, 0, 0, false, "等待 2999 事件数据，暂按起点层数显示。");
        }

        var aliases = snapshot.EventAliasMap();
        if (!aliases.Values.Contains(playerKey))
        {
            return new(anchor.Stacks, 0, 0, false, "玩家身份隐藏或无法唯一匹配事件，层数需要手动修正。");
        }

        // The same ordering and de-duplication as the death streaks: repeated polls never count an event twice.
        var events = snapshot
            .Events.Where(e => e.Name == "ChampionKill" && e.Time > anchor.Time && e.Time <= snapshot.Time + 1)
            .DistinctBy(e => (e.Id, e.Name, e.Time, e.Killer, e.Victim))
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Id);
        int stacks = anchor.Stacks,
            takedowns = 0,
            deaths = 0;
        foreach (var kill in events)
        {
            bool died = aliases.GetValueOrDefault(kill.Victim) == playerKey;
            bool scored =
                !died
                && (
                    aliases.GetValueOrDefault(kill.Killer) == playerKey
                    || kill.Assisters.Any(name => aliases.GetValueOrDefault(name) == playerKey)
                );
            if (scored)
            {
                stacks++;
                takedowns++;
            }
            else if (died)
            {
                deaths++;
                if (augment != TrackedAugment.InfiniteRecursion)
                {
                    stacks -= (int)Math.Round(stacks * DeathLoss, MidpointRounding.AwayFromZero);
                }
            }
        }

        // Without the game start, kills before the program saw the game may be missing from a count from zero.
        bool complete = anchor.Time > 0 || snapshot.Events.Any(e => e.Name == "GameStart" && e.Time <= 1);
        return new(
            stacks,
            takedowns,
            deaths,
            complete,
            !complete ? "本局事件历史不完整，层数可能偏少，可手动修正。"
                : augment == TrackedAugment.InfiniteRecursion ? "依据本局击杀事件计数，死亡不掉层。"
                : "依据本局击杀事件推算，死亡掉层按四舍五入估算，可手动修正。"
        );
    }

    /// <summary>The ability haste the takedown augments grant; Tank Engine grants health instead.</summary>
    public static int AbilityHaste(TrackedAugment augment, int stacks) =>
        augment switch
        {
            TrackedAugment.InfiniteRecursion => 60 + 3 * stacks,
            TrackedAugment.ShrinkEngine => 8 * stacks,
            _ => 0,
        };
}
