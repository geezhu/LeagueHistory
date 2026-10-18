using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Announcing enemies' and allies' new survival items. The expected values come from the checks in
/// Tests/StatisticsChecks/SurvivalSoundChecks.cs; no sound is played.
/// </summary>
public class SurvivalItemAlertsTests
{
    private readonly SurvivalItemAlerts _alerts = new();

    [Fact]
    public void Update_ItemsHeldWhenAPlayerIsFirstSeen_IsSilent()
    {
        var alert = _alerts.Update([Enemy("a", 3157), Enemy("b")]);

        Assert.Null(alert);
    }

    [Fact]
    public void Update_NewItem_SoundsWithItsOwnId()
    {
        _alerts.Update([Enemy("a", 3157), Enemy("b")]);

        var alert = _alerts.Update([Enemy("a", 3157), Enemy("b", 3026)]);

        Assert.Equal(new SurvivalAlert("b", 3026), alert);
    }

    [Fact]
    public void Update_ItemSeenBefore_IsSilent()
    {
        _alerts.Update([Enemy("b", 3026)]);

        var alert = _alerts.Update([Enemy("b", 3026)]);

        Assert.Null(alert);
    }

    [Fact]
    public void Update_ItemSoldAndBoughtAgain_SoundsAgain()
    {
        _alerts.Update([Enemy("a", 3157)]);
        _alerts.Update([Enemy("a")]);

        var alert = _alerts.Update([Enemy("a", 3157)]);

        Assert.Equal(new SurvivalAlert("a", 3157), alert);
    }

    [Fact]
    public void Update_TeamThatWasQuiet_DoesNotSoundWhenItTurnsAudible()
    {
        _alerts.Update([Quiet("a", 3157)]);
        _alerts.Update([Quiet("a", 3157, 3102)]);

        var alert = _alerts.Update([Enemy("a", 3157, 3102), Enemy("b", 3026)]);

        Assert.Null(alert);
    }

    [Fact]
    public void Update_OnlyTheAudibleTeamsPurchase_Sounds()
    {
        _alerts.Update([Quiet("a", 3157), Enemy("b", 3026)]);

        var alert = _alerts.Update([Quiet("a", 3157, 3102), Enemy("b", 3026, 3193)]);

        Assert.Equal(new SurvivalAlert("b", 3193), alert);
    }

    [Fact]
    public void Update_SeveralNewItemsInOnePoll_PlaysOnlyTheFirst()
    {
        _alerts.Update([Enemy("a")]);

        var alert = _alerts.Update([Enemy("a", 3157, 3026)]);

        Assert.Equal(new SurvivalAlert("a", 3157), alert);
    }

    [Fact]
    public void Update_QuietPlayerBeforeAnAudibleOne_SoundsTheAudibleOne()
    {
        _alerts.Update([Quiet("a"), Enemy("b")]);

        var alert = _alerts.Update([Quiet("a", 3157), Enemy("b", 3026)]);

        Assert.Equal(new SurvivalAlert("b", 3026), alert);
    }

    [Fact]
    public void Reset_AfterAGame_StartsSilentAgain()
    {
        _alerts.Update([Enemy("a", 3157)]);
        _alerts.Reset();

        var alert = _alerts.Update([Enemy("a", 3157)]);

        Assert.Null(alert);
    }

    private static SurvivalInventory Enemy(string key, params int[] items) => new(key, items, Audible: true);

    private static SurvivalInventory Quiet(string key, params int[] items) => new(key, items, Audible: false);
}
