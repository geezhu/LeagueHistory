using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Rebuilding an augment's stacks from the kill history: a takedown (a kill or an assist) adds one, and a death takes
/// away a share of what is there - except for the augment whose name is literally about not losing them.
///
/// Three details decide the numbers. The anchor is exclusive, so a kill at exactly the anchor's time belongs to
/// whatever came before it. A kill where the player is both killer and victim counts as a death, not a takedown.
/// And the loss rounds away from zero: ten stacks lose seven, not six, which is why the note calls the number an
/// estimate.
/// </summary>
public class AugmentStacksTests
{
    private static LiveMatchPlayer Player(string key, params string[] aliases) =>
        new(key, key, "Champion", "英雄", "ORDER", Self: false, Level: 11, Items: []) { EventAliases = aliases };

    private static LiveMatchEvent Kill(
        string killer,
        string victim,
        double time,
        long? id = null,
        params string[] assisters
    ) => new(id, "ChampionKill", time, killer, victim) { Assisters = assisters };

    private static LiveMatchEvent Start(double time = 0) => new(null, "GameStart", time, "", "");

    private static LiveMatchSnapshot Snapshot(
        double time,
        LiveMatchEvent[]? events,
        params LiveMatchPlayer[] players
    ) => new("CLASSIC", 11, time, players) { Events = events };

    private static readonly TrackedAugment Normal = TrackedAugment.ShrinkEngine;
    private static readonly TrackedAugment Recursion = TrackedAugment.InfiniteRecursion;

    private static AugmentStackAnchor Anchor(double time = 0, int stacks = 0) => new(time, stacks);

    private static AugmentStackState Count(
        LiveMatchSnapshot snapshot,
        string key = "me",
        TrackedAugment augment = TrackedAugment.ShrinkEngine,
        AugmentStackAnchor? anchor = null
    ) => AugmentStacks.Count(snapshot, key, augment, anchor ?? Anchor());

    // ---- the constants and the easy formulas ----

    [Fact]
    public void DeathLoss_IsSixtyFivePercent()
    {
        Assert.Equal(0.65, AugmentStacks.DeathLoss);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 63)]
    [InlineData(10, 90)]
    public void AbilityHaste_TheRecursionAugment_GrowsWithTheStacks(int stacks, int expected)
    {
        Assert.Equal(expected, AugmentStacks.AbilityHaste(Recursion, stacks));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 8)]
    [InlineData(10, 80)]
    public void AbilityHaste_TheShrinkingAugment_GivesEightPerStack(int stacks, int expected)
    {
        Assert.Equal(expected, AugmentStacks.AbilityHaste(Normal, stacks));
    }

    [Fact]
    public void AbilityHaste_AnAugmentWithoutHaste_GivesNone()
    {
        // Tank Engine is the one that grants health instead.
        Assert.Equal(0, AugmentStacks.AbilityHaste(TrackedAugment.TankEngine, 10));
    }

    // ---- counting ----

    [Fact]
    public void Count_ATakedown_AddsAStack()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        AugmentStackState state = Count(snapshot);

        Assert.Equal(1, state.Stacks);
        Assert.Equal(1, state.Takedowns);
        Assert.Equal(0, state.Deaths);
    }

    [Fact]
    public void Count_AnAssist_CountsAsATakedown()
    {
        // Assisting is taking part in the kill, which is what the augment counts.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("ally", "enemy", 10, id: 1, "me")],
            Player("me", "me"),
            Player("ally", "ally"),
            Player("enemy", "enemy")
        );

        Assert.Equal(1, Count(snapshot).Takedowns);
    }

    [Fact]
    public void Count_AnotherPlayersKill_CountsForNothing()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("ally", "enemy", 10)],
            Player("me", "me"),
            Player("ally", "ally"),
            Player("enemy", "enemy")
        );

        AugmentStackState state = Count(snapshot);

        Assert.Equal(0, state.Stacks);
        Assert.Equal(0, state.Takedowns);
        Assert.Equal(0, state.Deaths);
    }

    [Fact]
    public void Count_AKillWhereThePlayerIsAlsoTheVictim_IsADeathNotATakedown()
    {
        // Otherwise the event would both add and subtract.
        LiveMatchSnapshot snapshot = Snapshot(100, [Start(), Kill("me", "me", 10)], Player("me", "me"));

        AugmentStackState state = Count(snapshot, anchor: Anchor(stacks: 10));

        Assert.Equal(0, state.Takedowns);
        Assert.Equal(1, state.Deaths);
    }

    [Fact]
    public void Count_EventsAtTheAnchorsOwnMoment_AreLeftOut()
    {
        // The anchor already includes everything up to its own time; counting it again would double it.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 50)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal(0, Count(snapshot, anchor: Anchor(time: 50)).Takedowns);
        Assert.Equal(1, Count(snapshot, anchor: Anchor(time: 49)).Takedowns);
    }

    [Fact]
    public void Count_AnEventFromTheFuture_IsIgnored()
    {
        // The clock may lag the event list by a moment, and no more.
        LiveMatchSnapshot tolerated = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 101)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );
        LiveMatchSnapshot ignored = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 102)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal(1, Count(tolerated).Takedowns);
        Assert.Equal(0, Count(ignored).Takedowns);
    }

    [Fact]
    public void Count_TheSameEventTwice_CountsOnce()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 10, id: 7), Kill("me", "enemy", 10, id: 7)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal(1, Count(snapshot).Takedowns);
    }

    [Fact]
    public void Count_EventsOutOfOrder_AreReadByTime()
    {
        // Traced in time order, one step per event:
        //   10  death      - ten stacks lose seven (6.5 rounds away from zero), leaving 3
        //   20  takedown   - 3 + 1 = 4
        //   30  takedown   - 4 + 1 = 5
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 30), Kill("enemy", "me", 10), Kill("me", "enemy", 20)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        AugmentStackState state = Count(snapshot, anchor: Anchor(stacks: 10));

        Assert.Equal(5, state.Stacks);
    }

    // ---- losing stacks on a death ----

    [Theory]
    [InlineData(10, 3)]
    [InlineData(3, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void Count_ADeath_TakesAwayTwoThirdsRoundedAwayFromZero(int before, int after)
    {
        // Ten lose seven (6.5 rounds away from zero), three lose two (1.95), one loses one (0.65).
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("enemy", "me", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal(after, Count(snapshot, anchor: Anchor(stacks: before)).Stacks);
    }

    [Fact]
    public void Count_OnTheRecursionAugment_ADeathCostsNothing()
    {
        // Its whole point is keeping the stacks; the death is still counted for the display.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("enemy", "me", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        AugmentStackState state = Count(snapshot, augment: Recursion, anchor: Anchor(stacks: 10));

        Assert.Equal(10, state.Stacks);
        Assert.Equal(1, state.Deaths);
    }

    [Fact]
    public void Count_ATakedownAfterADeath_ClimbsFromWhereTheLossLeftIt()
    {
        // Traced: ten stacks, a death leaves three, then two takedowns bring it to five.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("enemy", "me", 10), Kill("me", "enemy", 20), Kill("me", "enemy", 30)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal(5, Count(snapshot, anchor: Anchor(stacks: 10)).Stacks);
    }

    // ---- when the number cannot be vouched for ----

    [Fact]
    public void Count_NoEventsAtAll_KeepsTheAnchorsCountAndSaysWhy()
    {
        LiveMatchSnapshot snapshot = Snapshot(100, null, Player("me", "me"));

        AugmentStackState state = Count(snapshot, anchor: Anchor(stacks: 4));

        Assert.Equal(4, state.Stacks);
        Assert.False(state.Complete);
        Assert.Equal("等待 2999 事件数据，暂按起点层数显示。", state.Note);
    }

    [Fact]
    public void Count_APlayerTheAliasesCannotResolve_AsksForACorrection()
    {
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("me"));

        AugmentStackState state = Count(snapshot);

        Assert.False(state.Complete);
        Assert.Equal("玩家身份隐藏或无法唯一匹配事件，层数需要手动修正。", state.Note);
    }

    [Fact]
    public void Count_AnAnchorTakenAfterTheStart_IsCompleteWhateverTheHistory()
    {
        // A count anchored inside the game does not depend on the game's start being in the history.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Kill("me", "enemy", 50)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.True(Count(snapshot, anchor: Anchor(time: 10)).Complete);
    }

    [Fact]
    public void Count_AnAnchorAtZeroWithAGameStart_IsComplete()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.True(Count(snapshot).Complete);
    }

    [Fact]
    public void Count_AnAnchorAtZeroWithoutAGameStart_IsNotComplete()
    {
        // Kills before the program saw the game may be missing, so a count from zero may be short.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Kill("me", "enemy", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        AugmentStackState state = Count(snapshot);

        Assert.False(state.Complete);
        Assert.Equal("本局事件历史不完整，层数可能偏少，可手动修正。", state.Note);
    }

    [Fact]
    public void Count_ACompleteCountOnAnOrdinaryAugment_CallsItselfAnEstimate()
    {
        // The loss rounds, so the number is not exact.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal("依据本局击杀事件推算，死亡掉层按四舍五入估算，可手动修正。", Count(snapshot).Note);
    }

    [Fact]
    public void Count_ACompleteCountOnTheRecursionAugment_SaysDeathsCostNothing()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("me", "enemy", 10)],
            Player("me", "me"),
            Player("enemy", "enemy")
        );

        Assert.Equal("依据本局击杀事件计数，死亡不掉层。", Count(snapshot, augment: Recursion).Note);
    }
}
