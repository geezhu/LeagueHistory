using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>Character facts from the game data; Annie's numbers are the fixture's, her windup the Wiki's 19.58%.</summary>
public class ChampionRecordTests
{
    private static readonly ChampionRecord Annie = ChampionRecord.Parse(CommunityDragonFixture.AnnieBin(), "Annie");

    [Fact]
    public void Parse_AttackDelayOffset_OffsetsTheDefaultWindup()
    {
        var windup = Annie.AttackWindup;

        Assert.Equal(0.1958, windup, 4);
    }

    [Fact]
    public void Parse_Annie_ReadsRatioAndRadii()
    {
        var facts = (Annie.AttackSpeedRatio, Annie.GameplayRadius, Annie.SelectionRadius, Annie.AcquisitionRange);

        Assert.Equal((0.625, 55.0, 100.0, 625.0), facts);
    }

    [Fact]
    public void Parse_CastAndTotalTime_GiveTheWindupShare()
    {
        var bin = JObject.Parse(
            """{"Characters/Aatrox/CharacterRecords/Root":{"basicAttack":{"mAttackTotalTime":1.52,"mAttackCastTime":0.3}}}"""
        );

        var record = ChampionRecord.Parse(bin, "Aatrox");

        Assert.Equal(0.3 / 1.52, record.AttackWindup, 6);
    }

    [Fact]
    public void Parse_NoGameplayRadius_UsesTheGameDefault()
    {
        var bin = JObject.Parse("""{"Characters/Test/CharacterRecords/Root":{}}""");

        var record = ChampionRecord.Parse(bin, "Test");

        Assert.Equal(65, record.GameplayRadius);
    }

    [Fact]
    public void Parse_MissingRecord_ReturnsNone()
    {
        var record = ChampionRecord.Parse(new JObject(), "Annie");

        Assert.Same(ChampionRecord.None, record);
    }
}
