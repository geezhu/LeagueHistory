using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Each player's run of deaths in the current game, rebuilt from the event history rather than accumulated across
/// polls - so a reconnect or a restart cannot count a death twice.
///
/// The rules with teeth are about not lying with a number. A kill resets the killer's run, a death extends the
/// victim's, and the highest is kept; a player the events cannot be matched to is marked unavailable rather than being
/// shown as zero, because a missing record and a clean game look the same from outside.
/// </summary>
public class LiveDeathStreakAnalysisTests
{
    private static LiveMatchPlayer Player(
        string key,
        string name = "",
        int? deaths = null,
        int? kills = null,
        params string[] aliases
    ) =>
        new(
            Key: key,
            Name: name.Length == 0 ? key : name,
            ChampionAlias: "Champion",
            ChampionName: "英雄",
            Team: "ORDER",
            Self: false,
            Level: 11,
            Items: []
        )
        {
            EventAliases = aliases,
            Deaths = deaths,
            Kills = kills,
        };

    private static LiveMatchEvent Kill(string killer, string victim, double time, long? id = null) =>
        new(id, "ChampionKill", time, killer, victim);

    private static LiveMatchEvent Start(double time = 0) => new(null, "GameStart", time, "", "");

    private static LiveMatchSnapshot Snapshot(
        double time,
        LiveMatchEvent[]? events,
        params LiveMatchPlayer[] players
    ) => new("CLASSIC", 11, time, players) { Events = events };

    // ---- the record's own wording ----

    [Fact]
    public void Unavailable_HasNoNumbersAndSaysWhy()
    {
        LiveDeathStreak streak = LiveDeathStreak.Unavailable("因为某个原因");

        Assert.Null(streak.Current);
        Assert.Null(streak.Highest);
        Assert.False(streak.Complete);
        Assert.Equal("因为某个原因", streak.Description);
    }

    [Fact]
    public void Unavailable_WithoutAReason_SaysItIsWaitingForTheEventData()
    {
        Assert.Equal("等待 2999 事件数据。", LiveDeathStreak.Unavailable().Description);
    }

    [Fact]
    public void Text_WithNumbers_ShowsBothRuns()
    {
        Assert.Equal("连死 3 · 最高 5", new LiveDeathStreak(3, 5, true, "").Text);
    }

    [Fact]
    public void Text_AStillGrowingRun_IsMarked()
    {
        // The asterisk is what says the history may not be complete.
        Assert.Equal("连死 3 · 最高 5*", new LiveDeathStreak(3, 5, false, "").Text);
    }

    [Fact]
    public void Text_WithoutNumbers_IsDashes()
    {
        Assert.Equal("连死 — · 最高 —", LiveDeathStreak.Unavailable().Text);
    }

    [Fact]
    public void Rule_ExplainsHowTheCountWorks()
    {
        Assert.Equal(
            "原因\n死亡加一，击杀归零，助攻不归零；最高值仅统计本局。",
            new LiveDeathStreak(0, 0, true, "原因").Rule
        );
    }

    // ---- counting the deaths ----

    private static LiveDeathStreak Of(LiveMatchSnapshot snapshot, string key = "a") =>
        LiveDeathStreakAnalysis.Calculate(snapshot)[key];

    [Fact]
    public void Calculate_ADeath_StartsTheRun()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("k", "v", 10)],
            Player("a", aliases: "v"),
            Player("b", aliases: "k")
        );

        Assert.Equal(1, Of(snapshot, "a").Current);
        Assert.Equal(1, Of(snapshot, "a").Highest);
    }

    [Fact]
    public void Calculate_ManyDeaths_KeepTheHighest()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("k", "v", 10), Kill("k", "v", 20), Kill("k", "v", 30)],
            Player("a", aliases: "v"),
            Player("b", aliases: "k")
        );

        Assert.Equal(3, Of(snapshot, "a").Current);
        Assert.Equal(3, Of(snapshot, "a").Highest);
    }

    [Fact]
    public void Calculate_AKill_ResetsTheKillersRun()
    {
        // Dying then killing ends the run, but the highest stays on record.
        // Traced in order: k1 kills v three times (k1's run stays 0, its highest 0), then v2 kills k1 (k1 dies, so its
        // run is 1 and its highest 1), then k1 kills v again - which resets k1's run to zero.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [
                Start(),
                Kill("k1", "v", 10),
                Kill("k1", "v", 20),
                Kill("k1", "v", 30),
                Kill("v2", "k1", 40),
                Kill("k1", "v", 50),
            ],
            Player("a", aliases: "k1"),
            Player("b", aliases: "v"),
            Player("c", aliases: "v2")
        );

        Assert.Equal(0, Of(snapshot, "a").Current);
        Assert.Equal(1, Of(snapshot, "a").Highest);
    }

    [Fact]
    public void Calculate_ADeathAfterAKill_RestartsTheRunAtOne()
    {
        // Traced in order: a dies (run 1, highest 1), a kills k (run back to 0), a dies again (run 1, highest 1).
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("k", "a", 10), Kill("a", "k", 20), Kill("k", "a", 30)],
            Player("a", aliases: "a"),
            Player("b", aliases: "k")
        );

        Assert.Equal(1, Of(snapshot, "a").Current);
        Assert.Equal(1, Of(snapshot, "a").Highest);
    }

    [Fact]
    public void Calculate_AKillOfOneself_DoesNotResetTheRun()
    {
        // An event naming the same player as killer and victim is not a kill; treating it as one would hide a death.
        LiveMatchSnapshot snapshot = Snapshot(100, [Start(), Kill("a", "a", 10)], Player("a", aliases: "a"));

        Assert.Equal(1, Of(snapshot, "a").Current);
    }

    [Fact]
    public void Calculate_TheSameEventTwice_CountsOnce()
    {
        // A poll that repeats an event must not add a death.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("k", "v", 10, id: 7), Kill("k", "v", 10, id: 7)],
            Player("a", aliases: "v"),
            Player("b", aliases: "k")
        );

        Assert.Equal(1, Of(snapshot, "a").Current);
    }

    [Fact]
    public void Calculate_AnEventFromTheFuture_IsIgnored()
    {
        // The clock may lag the event list by a moment; a whole second is tolerated and no more.
        LiveMatchSnapshot tolerated = Snapshot(
            100,
            [Start(), Kill("k", "v", 101)],
            Player("a", aliases: "v"),
            Player("b", aliases: "k")
        );
        LiveMatchSnapshot ignored = Snapshot(
            100,
            [Start(), Kill("k", "v", 102)],
            Player("a", aliases: "v"),
            Player("b", aliases: "k")
        );

        Assert.Equal(1, Of(tolerated, "a").Current);
        Assert.Equal(0, Of(ignored, "a").Current);
    }

    [Fact]
    public void Calculate_EventsOutOfOrder_AreReadByTime()
    {
        // The history arrives newest first; the run is a sequence, so it has to be put in order first.
        // In time order: k kills a at 10, k kills a at 20 (run 2, highest 2), then a kills k at 30 (run back to 0).
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("a", "k", 30), Kill("k", "a", 20), Kill("k", "a", 10)],
            Player("a", aliases: "a"),
            Player("b", aliases: "k")
        );

        Assert.Equal(0, Of(snapshot, "a").Current);
        Assert.Equal(2, Of(snapshot, "a").Highest);
    }

    // ---- when a number cannot be given ----

    [Fact]
    public void Calculate_NoEventsAtAll_LeavesEveryoneUnavailable()
    {
        LiveMatchSnapshot snapshot = Snapshot(100, null, Player("a", aliases: "a"));

        Assert.False(Of(snapshot, "a").Current.HasValue);
        Assert.Equal("等待 2999 事件数据。", Of(snapshot, "a").Description);
    }

    [Fact]
    public void Calculate_APlayerTheAliasesCannotResolve_IsUnavailable()
    {
        // Without a unique alias the events cannot be attributed, and a guess would be worse than nothing.
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("a"));

        Assert.Equal("玩家身份隐藏或无法唯一匹配事件，不能推测连死次数。", Of(snapshot, "a").Description);
    }

    [Fact]
    public void Calculate_APlayerWithDeathsButNoEvents_IsNotShownAsZero()
    {
        // The score already says this player died; zero deaths from the events means the events are behind.
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("a", deaths: 2, kills: 0, aliases: "a"));

        Assert.Equal("尚未取得该玩家的死亡事件，不能将缺失记录显示为零。", Of(snapshot, "a").Description);
    }

    [Fact]
    public void Calculate_EventsAheadOfTheScore_AreUnavailableRatherThanBelieved()
    {
        // The events know a death the score has not caught up with, so they are ahead of the game and cannot be
        // trusted yet - the guard is for this direction, not the other one.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("k", "a", 10)],
            Player("a", deaths: 0, kills: 0, aliases: "a"),
            Player("b", aliases: "k")
        );

        Assert.Equal("事件记录与当前比分尚未同步，等待更新。", Of(snapshot, "a").Description);
    }

    // ---- whether the numbers are complete ----

    [Fact]
    public void Calculate_AGameThatStartedAndAgreesWithTheScore_IsComplete()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Start(), Kill("k", "a", 10)],
            Player("a", deaths: 1, kills: 0, aliases: "a"),
            Player("b", aliases: "k")
        );

        Assert.True(Of(snapshot, "a").Complete);
        Assert.Equal("依据本局事件历史统计。", Of(snapshot, "a").Description);
    }

    [Fact]
    public void Calculate_WithoutAGameStartMarkerButWithAScore_IsStillComplete()
    {
        // Either the marker or the score is enough: the score agreeing with the events says they are all of them.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Kill("k", "a", 10)],
            Player("a", deaths: 1, kills: 0, aliases: "a"),
            Player("b", aliases: "k")
        );

        Assert.True(Of(snapshot, "a").Complete);
    }

    [Fact]
    public void Calculate_WithNeitherAMarkerNorAScore_IsNotComplete()
    {
        // Nothing says whether the events are all of them, so the run is only what the events show.
        LiveMatchSnapshot snapshot = Snapshot(
            100,
            [Kill("k", "a", 10)],
            Player("a", aliases: "a"),
            Player("b", aliases: "k")
        );

        Assert.False(Of(snapshot, "a").Complete);
        Assert.StartsWith("* 仅统计已取得的事件", Of(snapshot, "a").Description, StringComparison.Ordinal);
    }

    // ---- looking a player up in a roster ----

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game", 420, "CLASSIC", players);

    private static LoadingPlayer Seat(string key, bool hidden = false) =>
        new(key, key, "puuid-" + key, 1, 1, 100, Self: false, IdentityHidden: hidden);

    [Fact]
    public void ForRoster_ASeatTheSnapshotCanFind_HandsOverItsStreak()
    {
        LiveDeathStreak expected = new(2, 3, true, "");
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("a", name: "a", aliases: "a"));
        var withStreaks = snapshot with { DeathStreaks = new Dictionary<string, LiveDeathStreak> { ["a"] = expected } };

        Assert.Same(expected, LiveDeathStreakAnalysis.ForRoster(Roster(Seat("a")), withStreaks)["a"]);
    }

    [Fact]
    public void ForRoster_ASeatWithNoSnapshot_IsUnavailable()
    {
        Assert.Equal(
            "等待游戏事件与玩家名单匹配。",
            LiveDeathStreakAnalysis.ForRoster(Roster(Seat("a")), null)["a"].Description
        );
    }

    [Fact]
    public void ForRoster_ASeatTheSnapshotCannotFind_IsUnavailable()
    {
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("other", aliases: "other"));

        Assert.Equal(
            "等待游戏事件与玩家名单匹配。",
            LiveDeathStreakAnalysis.ForRoster(Roster(Seat("a")), snapshot)["a"].Description
        );
    }

    [Fact]
    public void ForRoster_ASeatWhoseIdentityIsHidden_IsUnavailableWithoutLooking()
    {
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("a", aliases: "a"));

        Assert.Equal(
            "玩家身份隐藏，不能唯一匹配死亡事件。",
            LiveDeathStreakAnalysis.ForRoster(Roster(Seat("a", hidden: true)), snapshot)["a"].Description
        );
    }

    [Fact]
    public void ForRoster_ADuplicateSeat_DoesNotBreakTheList()
    {
        // A roster should hold each player once, but a duplicate must not throw away the whole poll.
        LiveMatchSnapshot snapshot = Snapshot(100, [Start()], Player("a", name: "a", aliases: "a"));

        IReadOnlyDictionary<string, LiveDeathStreak> streaks = LiveDeathStreakAnalysis.ForRoster(
            Roster(Seat("a"), Seat("a")),
            snapshot
        );

        Assert.Single(streaks);
    }
}
