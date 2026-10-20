using League.Models;
using League.Services;

namespace League.Tests.Models;

/// <summary>
/// The chat line for one ally summoner spell, sent when the user right-clicks it on the card.
///
/// Four things are refused, each with its own reason, because they are different problems for the user: the seat no
/// longer holds that champion, it is not on our side, the game data has gone stale, or the timer was never started.
/// Telling them apart is the point - "wait for 2999" and "click the icon first" call for different actions.
///
/// The line names only the spell. Whose it is comes from clicking the champion on the Tab scoreboard, which keeps a
/// player's name out of the log and out of the channel.
/// </summary>
public class SummonerChatMessageTests
{
    private const long Now = 52_000;
    private const int OwnTeam = 100;

    private static LoadingPlayer Player(int team = OwnTeam, string key = "a") =>
        new(Key: key, Name: "名字", Puuid: "puuid", SummonerId: 1, ChampionId: 11, Team: team, Self: false);

    /// <summary>A timer that will be back at <paramref name="readyAt"/>. The ready time is the start plus the
    /// cooldown, so the start is derived rather than given.</summary>
    private static PlayerSummoner Summoner(int slot = 0, double readyAt = 200, double cooldown = 300) =>
        new(slot, new SummonerSpellInfo("SummonerFlash", "闪现", cooldown, "flash.png"), cooldown, readyAt - cooldown);

    private static PlayerSummoner NotStarted(int slot = 0, double cooldown = 300) =>
        new(slot, new SummonerSpellInfo("SummonerFlash", "闪现", cooldown, "flash.png"), cooldown, null);

    private static OverlayLiveState Live(double gameTime, long gameTimeAt, params PlayerSummoner[] summons) =>
        new(null!, [], "")
        {
            GameTime = gameTime,
            GameTimeAt = gameTimeAt,
            Summoners = new Dictionary<string, PlayerSummoner[]> { ["a"] = summons },
        };

    /// <summary>The usual clock: game time 100, polled at 50 000.</summary>
    private static OverlayLiveState Live(params PlayerSummoner[] summons) => Live(100, 50_000, summons);

    // ---- the four refusals ----

    [Fact]
    public void Build_ASeatThatNoLongerHoldsTheChampion_SaysSo()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(null, OwnTeam, Live(Summoner()), 0, Now)
        );

        Assert.Equal("未发送：该位置的英雄已变化。", error.Message);
    }

    [Fact]
    public void Build_AnEnemySeat_IsRefused()
    {
        // Riot's rules forbid tracking enemy summoner cooldowns, so the line is not built at all.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(team: 200), OwnTeam, Live(Summoner()), 0, Now)
        );

        Assert.Equal("未发送：只能发送我方的召唤师技能计时。", error.Message);
    }

    [Fact]
    public void Build_NoOwnTeamAtAll_IsRefused()
    {
        // Without knowing which side is ours nothing can be said to be on it.
        Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(), ownTeam: 0, Live(Summoner()), 0, Now)
        );
    }

    [Theory]
    [InlineData(null)]
    public void Build_NoLiveState_IsRefused(OverlayLiveState? live)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(), OwnTeam, live, 0, Now)
        );

        Assert.Equal("未发送：对局数据未更新，请等待 2999 连接恢复。", error.Message);
    }

    [Fact]
    public void Build_DataJustInsideTheStaleWindow_IsUsed()
    {
        // Fifteen seconds is how old the game data may be before the timer would be a guess.
        Assert.Single(SummonerChatMessage.Build(Player(), OwnTeam, Live(100, Now - 15_000, Summoner()), 0, Now));
    }

    [Fact]
    public void Build_DataOlderThanTheStaleWindow_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(), OwnTeam, Live(100, Now - 15_001, Summoner()), 0, Now)
        );
    }

    [Fact]
    public void Build_AClockFromTheFuture_IsRefused()
    {
        // The game time cannot be later than now; if it is, the two clocks have drifted apart.
        Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(), OwnTeam, Live(100, Now + 1, Summoner()), 0, Now)
        );
    }

    [Fact]
    public void Build_ATimerThatWasNeverStarted_AsksForAClick()
    {
        // This is a different problem from stale data, and calls for a different action.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(), OwnTeam, Live(NotStarted()), 0, Now)
        );

        Assert.Equal("未发送：先左键点击技能图标开始计时。", error.Message);
    }

    [Fact]
    public void Build_ASpellThatIsNotInTheSlot_IsTreatedAsNotStarted()
    {
        // Asking about slot two when only slot zero is timed says nothing about slot two.
        Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(), OwnTeam, Live(Summoner(slot: 0)), spell: 1, Now)
        );
    }

    [Fact]
    public void Build_APlayerWithNoSummonersAtAll_IsTreatedAsNotStarted()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SummonerChatMessage.Build(Player(key: "nobody"), OwnTeam, Live(Summoner()), 0, Now)
        );
    }

    // ---- the line itself ----

    [Fact]
    public void Build_ATimerStillRunning_SaysHowLongIsLeft()
    {
        // Traced: game time 100 plus the two seconds since the last poll is 102; ready at 200 is 98 seconds away.
        string line = Assert.Single(SummonerChatMessage.Build(Player(), OwnTeam, Live(Summoner(readyAt: 200)), 0, Now));

        Assert.Equal(
            "召唤师技能：闪现 还有 "
                + MinionWaveForecaster.Clock(98)
                + "，约 "
                + MinionWaveForecaster.Clock(200)
                + " 转好",
            line
        );
    }

    [Fact]
    public void Build_ATimerAlreadyBack_SaysSo()
    {
        // Traced: ready at 50 is before the current game time of 102, so it is back.
        string line = Assert.Single(SummonerChatMessage.Build(Player(), OwnTeam, Live(Summoner(readyAt: 50)), 0, Now));

        Assert.Equal("召唤师技能：闪现 已转好", line);
    }

    [Fact]
    public void Build_TheElapsedTimeIsAddedToTheGameClock()
    {
        // The game time is the last poll; the wall clock has moved on since. Traced: 100 + 5 = 105, ready at 110.
        string line = Assert.Single(
            SummonerChatMessage.Build(Player(), OwnTeam, Live(100, Now - 5_000, Summoner(readyAt: 110)), 0, Now)
        );

        Assert.Contains("还有 " + MinionWaveForecaster.Clock(5), line, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TheLineNamesTheSpellAndNobodyElse()
    {
        // Whose spell it is comes from the Tab scoreboard, which keeps names out of the channel.
        string line = Assert.Single(SummonerChatMessage.Build(Player(), OwnTeam, Live(Summoner(readyAt: 50)), 0, Now));

        Assert.Contains("闪现", line, StringComparison.Ordinal);
        Assert.DoesNotContain("名字", line, StringComparison.Ordinal);
        Assert.DoesNotContain("puuid", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OneLineIsSent()
    {
        // One message per click, so the channel is not flooded.
        Assert.Single(SummonerChatMessage.Build(Player(), OwnTeam, Live(Summoner()), 0, Now));
    }
}
