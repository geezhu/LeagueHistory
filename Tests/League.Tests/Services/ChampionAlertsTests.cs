using League.Services;

namespace League.Tests.Services;

/// <summary>When a watched enemy champion chimes at game start: once per champion per game.</summary>
public class ChampionAlertsTests
{
    private int _plays;

    private ChampionAlerts Create() => new(() => _plays++);

    [Fact]
    public void Update_WatchedEnemy_ReturnsItAndChimesOnce()
    {
        using var alerts = Create();

        var found = alerts.Update(1, [11, 35, 103], [35, 266]);

        Assert.Equal([35], found);
        Assert.Equal(1, _plays);
    }

    [Fact]
    public void Update_NoWatchedEnemy_StaysSilent()
    {
        using var alerts = Create();

        var found = alerts.Update(1, [11, 103], [35]);

        Assert.Empty(found);
        Assert.Equal(0, _plays);
    }

    [Fact]
    public void Update_SameGameReadAgain_DoesNotChimeAgain()
    {
        using var alerts = Create();
        alerts.Update(1, [35], [35]);

        var found = alerts.Update(1, [35, 11], [35]);

        Assert.Empty(found);
        Assert.Equal(1, _plays);
    }

    [Fact]
    public void Update_FullerRosterSameGame_ChimesOnlyForAddedChampion()
    {
        using var alerts = Create();
        alerts.Update(1, [35, 0], [35, 266]);

        var found = alerts.Update(1, [35, 266], [35, 266]);

        Assert.Equal([266], found);
        Assert.Equal(2, _plays);
    }

    [Fact]
    public void Update_NewGame_ChimesAgain()
    {
        using var alerts = Create();
        alerts.Update(1, [35], [35]);

        var found = alerts.Update(2, [35], [35]);

        Assert.Equal([35], found);
        Assert.Equal(2, _plays);
    }

    [Fact]
    public void Update_UnknownChampionId_IsIgnored()
    {
        using var alerts = Create();

        var found = alerts.Update(1, [0, -1], [0, -1]);

        Assert.Empty(found);
        Assert.Equal(0, _plays);
    }
}
