using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// Whether a game was a remake: someone left before the game really began, so it is not a win or a loss for anyone.
///
/// The flag is looked for in four places, because the shape of the payload differs between the endpoints the window
/// reads. It has to be a real boolean — the string "true" is not the flag, and treating it as one would hide a game.
/// </summary>
public class MatchResultTests
{
    private static JObject Flagged() => new() { ["gameEndedInEarlySurrender"] = true };

    [Fact]
    public void IsRemake_NoFlagAnywhere_IsNotARemake()
    {
        Assert.False(MatchResult.IsRemake(new JObject()));
    }

    [Fact]
    public void IsRemake_NoEntryAtAll_IsNotARemake()
    {
        Assert.False(MatchResult.IsRemake(null));
    }

    [Fact]
    public void IsRemake_TheFlagOnTheEntryItself_Counts()
    {
        Assert.True(MatchResult.IsRemake(Flagged()));
    }

    [Fact]
    public void IsRemake_TheFlagUnderInfo_Counts()
    {
        // The endpoint that wraps the game in "info" is the usual one.
        Assert.True(MatchResult.IsRemake(new JObject { ["info"] = Flagged() }));
    }

    [Fact]
    public void IsRemake_TheFlagUnderJsonAndInfo_Counts()
    {
        Assert.True(MatchResult.IsRemake(new JObject { ["json"] = new JObject { ["info"] = Flagged() } }));
    }

    [Fact]
    public void IsRemake_TheFlagUnderTheGamesOwnStats_Counts()
    {
        Assert.True(MatchResult.IsRemake(new JObject { ["info"] = new JObject { ["stats"] = Flagged() } }));
    }

    [Fact]
    public void IsRemake_TheFlagOnAnyParticipant_Counts()
    {
        // Some payloads carry the flag only per player.
        var entry = new JObject
        {
            ["info"] = new JObject { ["participants"] = new JArray(new JObject { ["stats"] = Flagged() }) },
        };

        Assert.True(MatchResult.IsRemake(entry));
    }

    [Fact]
    public void IsRemake_TheFlagOnAPlayerPassedSeparately_Counts()
    {
        // The window also asks about a list of players it already has, without them being in the entry.
        Assert.True(MatchResult.IsRemake(new JObject(), [new JObject { ["stats"] = Flagged() }]));
    }

    [Fact]
    public void IsRemake_TheFlagOnAPlayerThatIsNotUnderStats_Counts()
    {
        Assert.True(MatchResult.IsRemake(new JObject(), [Flagged()]));
    }

    [Theory]
    [InlineData("true")]
    [InlineData(1)]
    [InlineData(false)]
    public void IsRemake_AFlagThatIsNotTrue_IsNotARemake(object value)
    {
        // The flag is a boolean; the string "true" is not the flag, and reading it as one would hide a real game.
        var entry = new JObject { ["gameEndedInEarlySurrender"] = JToken.FromObject(value) };

        Assert.False(MatchResult.IsRemake(entry));
    }

    [Fact]
    public void IsRemake_SomePlayersWithTheFlagAndSomeWithout_IsARemake()
    {
        // One early surrender is enough: the game did not really happen.
        var entry = new JObject
        {
            ["info"] = new JObject
            {
                ["participants"] = new JArray(new JObject(), new JObject { ["stats"] = Flagged() }),
            },
        };

        Assert.True(MatchResult.IsRemake(entry));
    }

    [Fact]
    public void RemakeColor_IsTheGreyTheListDrawsRemakesWith()
    {
        Assert.Equal(System.Drawing.Color.FromArgb(111, 123, 139), MatchResult.RemakeColor);
    }
}
