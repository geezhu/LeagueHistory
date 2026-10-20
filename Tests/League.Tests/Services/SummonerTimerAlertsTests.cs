using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The announcements for an ally's summoner timers: each comes back once per start, so a restarted timer can announce
/// again while one that is simply still ready stays quiet.
///
/// Two cases are deliberately silent. The first time a timer is seen it is recorded but not announced - otherwise a
/// program started mid-game would announce every timer already back. And a timer whose start time changed is treated
/// as a new timer, recorded but not announced either, because the change means someone restarted it and the old
/// announcement is a different spell coming back.
/// </summary>
public class SummonerTimerAlertsTests
{
    private static SummonerTimerState Timer(
        string key = "a",
        int slot = 0,
        double? started = 0,
        double cooldown = 300
    ) =>
        new(
            key,
            new PlayerSummoner(
                slot,
                new SummonerSpellInfo("SummonerFlash", "闪现", cooldown, "flash.png"),
                cooldown,
                started
            )
        );

    // ---- the first sighting ----

    [Fact]
    public void Update_ATimerSeenForTheFirstTime_IsNotAnnounced()
    {
        SummonerTimerAlerts alerts = new();

        Assert.Empty(alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 10));
    }

    [Fact]
    public void Update_ATimerAlreadyBackWhenFirstSeen_IsNotAnnounced()
    {
        // A program started mid-game would otherwise announce every timer that is already back.
        SummonerTimerAlerts alerts = new();

        Assert.Empty(alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 1_000));
    }

    [Fact]
    public void Update_ATimerWithoutAStart_IsIgnored()
    {
        // Nothing is running, so there is nothing to announce.
        SummonerTimerAlerts alerts = new();

        Assert.Empty(alerts.Update([Timer(started: null)], gameTime: 1_000));
    }

    // ---- coming back ----

    [Fact]
    public void Update_ATimerThatCameBack_IsAnnouncedOnce()
    {
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 10);

        SummonerTimerState announced = Assert.Single(alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 310));

        Assert.Equal("a", announced.Key);
    }

    [Fact]
    public void Update_ATimerThatStaysBack_IsNotAnnouncedAgain()
    {
        // A timer counts once per start; polling it again while it is still back says nothing new.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 10);
        alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 310);

        Assert.Empty(alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 400));
    }

    [Fact]
    public void Update_TheExactMomentItComesBack_Counts()
    {
        // "Ready at" is inclusive: arriving exactly on the second is arriving.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 10);

        Assert.Single(alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 300));
    }

    [Fact]
    public void Update_ARestartedTimer_IsQuietButCanAnnounceAgainLater()
    {
        // Someone restarted it: the new countdown is a new timer, and its own return is what gets announced.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 10);
        alerts.Update([Timer(started: 0, cooldown: 300)], gameTime: 310);
        Assert.Empty(alerts.Update([Timer(started: 320, cooldown: 300)], gameTime: 400));

        Assert.Single(alerts.Update([Timer(started: 320, cooldown: 300)], gameTime: 620));
    }

    [Fact]
    public void Update_TwoSlotsOfOnePlayer_AreTrackedApart()
    {
        // One player has two spells, so the slot is part of which timer this is.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer("a", 0), Timer("a", 1, cooldown: 600)], gameTime: 10);

        SummonerTimerState[] announced = alerts.Update([Timer("a", 0), Timer("a", 1, cooldown: 600)], gameTime: 310);

        Assert.Equal(0, Assert.Single(announced).Summoner.Slot);
    }

    [Fact]
    public void Update_TwoPlayers_AreTrackedApart()
    {
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer("a"), Timer("b", cooldown: 600)], gameTime: 10);

        Assert.Equal("a", Assert.Single(alerts.Update([Timer("a"), Timer("b", cooldown: 600)], gameTime: 310)).Key);
    }

    [Fact]
    public void Update_SeveralTimersComingBackTogether_AreAllAnnounced()
    {
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer("a"), Timer("b"), Timer("c")], gameTime: 10);

        Assert.Equal(3, alerts.Update([Timer("a"), Timer("b"), Timer("c")], gameTime: 310).Length);
    }

    // ---- timers that stop being reported ----

    [Fact]
    public void Update_ATimerThatStopsBeingReported_IsForgotten()
    {
        // After a reconnect the widget starts again from nothing; keeping the old record would suppress the next one.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer("a")], gameTime: 10);
        alerts.Update([], gameTime: 20);

        Assert.Empty(alerts.Update([Timer("a")], gameTime: 30));
        Assert.Single(alerts.Update([Timer("a")], gameTime: 310));
    }

    [Fact]
    public void Update_ATimerWhoseStartDisappeared_IsForgottenToo()
    {
        // A timer with no start is not running; it is not reported, so it is dropped like any other absent one.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer("a")], gameTime: 10);
        alerts.Update([Timer("a", started: null)], gameTime: 20);

        Assert.Empty(alerts.Update([Timer("a")], gameTime: 30));
    }

    // ---- resetting ----

    [Fact]
    public void Reset_ForgetsEveryTimer()
    {
        // A new game starts with nothing running, so the first sighting is quiet again.
        SummonerTimerAlerts alerts = new();
        alerts.Update([Timer("a")], gameTime: 10);

        alerts.Reset();

        Assert.Empty(alerts.Update([Timer("a")], gameTime: 310));
    }

    [Fact]
    public void Update_NothingReportedAndNothingKnown_AnnouncesNothing()
    {
        Assert.Empty(new SummonerTimerAlerts().Update([], gameTime: 100));
    }
}
