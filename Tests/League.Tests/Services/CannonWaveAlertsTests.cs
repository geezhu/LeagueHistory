using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// When the wave page plays its cannon-wave sound: a spawn crossing between two polls, once, and only for the mode
/// the user chose. The expected values come from the cannon-sound checks in
/// Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class CannonWaveAlertsTests
{
    [Theory]
    [InlineData(CannonSoundModes.Spawn, 124, 125, true)]
    [InlineData(CannonSoundModes.Spawn, 125, 126, false)]
    [InlineData(CannonSoundModes.Spawn, 214, 215, true)]
    [InlineData(CannonSoundModes.Next, 94, 95, true)]
    [InlineData(CannonSoundModes.Next, 124, 125, false)]
    [InlineData(CannonSoundModes.Off, 124, 125, false)]
    [InlineData(CannonSoundModes.Spawn, 120, 130, false)]
    [InlineData(CannonSoundModes.Spawn, 126, 125, false)]
    public void Update_SecondPoll_SoundsOnlyForTheChosenModeAndCrossing(
        string mode,
        double first,
        double second,
        bool expected
    )
    {
        var alerts = new CannonWaveAlerts();
        alerts.Update(Upcoming(first), first, mode);

        bool sounded = alerts.Update(Upcoming(second), second, mode);

        Assert.Equal(expected, sounded);
    }

    [Fact]
    public void Update_FirstPoll_OnlyRemembers()
    {
        var alerts = new CannonWaveAlerts();

        bool sounded = alerts.Update(Upcoming(124), 124, CannonSoundModes.Spawn);

        Assert.Equal(false, sounded);
    }

    [Fact]
    public void Update_PollWithoutWaves_RemembersNothing()
    {
        var alerts = new CannonWaveAlerts();
        alerts.Update(Upcoming(300), 300, CannonSoundModes.Spawn);

        bool sounded = alerts.Update(null, 301, CannonSoundModes.Spawn);

        Assert.Equal(false, sounded);
    }

    [Fact]
    public void Update_PollWithoutWaves_StillAnnouncesTheCrossingThePreviousOneSaw()
    {
        var alerts = new CannonWaveAlerts();
        alerts.Update(Upcoming(124), 124, CannonSoundModes.Spawn);

        bool sounded = alerts.Update(null, 125, CannonSoundModes.Spawn);

        Assert.Equal(true, sounded);
    }

    [Fact]
    public void Update_TheSameWaveOnTheNextPoll_StaysSilent()
    {
        var alerts = new CannonWaveAlerts();
        alerts.Update(Upcoming(124), 124, CannonSoundModes.Spawn);
        alerts.Update(Upcoming(125), 125, CannonSoundModes.Spawn);

        bool sounded = alerts.Update(Upcoming(126), 126, CannonSoundModes.Spawn);

        Assert.Equal(false, sounded);
    }

    [Fact]
    public void Reset_NextPollOnlyRemembersAgain()
    {
        var alerts = new CannonWaveAlerts();
        alerts.Update(Upcoming(124), 124, CannonSoundModes.Spawn);
        alerts.Reset();

        bool sounded = alerts.Update(Upcoming(125), 125, CannonSoundModes.Spawn);

        Assert.Equal(false, sounded);
    }

    // The wave page polls once a second and keeps the next four spawns: a cannon every third wave from 1:05.
    private static WaveSpawn[] Upcoming(double now) =>
        Enumerable
            .Range(1, 12)
            .Select(number => new WaveSpawn(number, 65 + (number - 1) * 30, 3, 3, number % 3 == 0 ? 1 : 0))
            .Where(spawn => spawn.Time > now)
            .Take(4)
            .ToArray();
}
