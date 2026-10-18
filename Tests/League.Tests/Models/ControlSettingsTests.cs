using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The widget's control settings, including the older files whose only team switch was the enemy one. The expected
/// values come from the checks in Tests/StatisticsChecks/SurvivalSoundChecks.cs, MinionWaveChecks.cs and
/// SummonerTimerChecks.cs.
/// </summary>
public class ControlSettingsTests
{
    [Fact]
    public void Default_Settings_LeaveTheEnemySoundOnAndTheAllySoundOff()
    {
        Assert.Equal(new ControlSettings(true, false), ControlSettings.Default);
    }

    [Theory]
    [InlineData("""{"protocol":1,"survivalSound":false,"allySound":true,"test":3}""", false, true)]
    [InlineData("""{"protocol":1,"survivalSound":true}""", true, false)]
    [InlineData("""{"protocol":1,"survivalSound":false,"allySound":false}""", false, false)]
    public void Parse_Teams_SplitsTheOldFileIntoTheEnemySetting(string json, bool enemySound, bool allySound)
    {
        var settings = ControlSettings.Parse(JObject.Parse(json));

        Assert.Equal(new ControlSettings(enemySound, allySound), settings);
    }

    [Theory]
    [InlineData("""{"protocol":1,"survivalSound":true}""", true)]
    [InlineData("""{"protocol":1,"survivalSound":true,"summonerSound":false}""", false)]
    public void Parse_SummonerSound_DefaultsToOn(string json, bool expected)
    {
        var settings = ControlSettings.Parse(JObject.Parse(json));

        Assert.Equal(expected, settings!.SummonerSound);
    }

    [Theory]
    [InlineData("""{"protocol":1,"survivalSound":true}""", "off")]
    [InlineData("""{"protocol":1,"survivalSound":true,"cannonSound":"next"}""", "next")]
    [InlineData("""{"protocol":1,"survivalSound":true,"cannonSound":"spawn"}""", "spawn")]
    public void Parse_CannonSound_DefaultsToOff(string json, string expected)
    {
        var settings = ControlSettings.Parse(JObject.Parse(json));

        Assert.Equal(expected, settings!.CannonSound);
    }

    [Theory]
    [InlineData("""{"protocol":1,"survivalSound":"no"}""")]
    [InlineData("""{"protocol":1,"survivalSound":true,"cannonSound":"loud"}""")]
    [InlineData("""{"protocol":1,"survivalSound":true,"summonerSound":1}""")]
    [InlineData("""{"protocol":1,"survivalSound":true,"allySound":"yes"}""")]
    [InlineData("""{"protocol":2,"survivalSound":true}""")]
    [InlineData("""{"protocol":"1","survivalSound":true}""")]
    [InlineData("""{"survivalSound":true}""")]
    public void Parse_MalformedSettings_AreRefused(string json)
    {
        var settings = ControlSettings.Parse(JObject.Parse(json));

        Assert.Null(settings);
    }
}
