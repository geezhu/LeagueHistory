using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Which game mode a game is playing, under the name the champion data uses for its overrides.
///
/// The live mode wins when the game reports one, because it is what the game is actually running; but "CLASSIC" is
/// special - it is the client saying "no special mode", so the queue decides instead. An empty answer means the usual
/// rules apply, which is also what a queue the table does not know gives.
/// </summary>
public class GameModesTests
{
    [Theory]
    [InlineData(100, "aram")]
    [InlineData(450, "aram")]
    [InlineData(2400, "kiwi")]
    [InlineData(3270, "kiwi")]
    [InlineData(900, "urf")]
    [InlineData(1010, "urf")]
    [InlineData(1900, "urf")]
    [InlineData(1700, "cherry")]
    [InlineData(1710, "cherry")]
    [InlineData(480, "swiftplay")]
    [InlineData(490, "swiftplay")]
    public void FromQueue_AKnownQueue_IsItsMode(int queue, string expected)
    {
        Assert.Equal(expected, GameModes.FromQueue(queue));
    }

    [Theory]
    [InlineData(420)]
    [InlineData(440)]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromQueue_AnOrdinaryQueue_HasNoSpecialMode(int queue)
    {
        // Empty means the usual rules apply.
        Assert.Equal("", GameModes.FromQueue(queue));
    }

    // ---- the live mode wins, except for one word ----

    [Theory]
    [InlineData("KIWI", "kiwi")]
    [InlineData("ARAM", "aram")]
    [InlineData("Cherry", "cherry")]
    public void Of_ALiveMode_IsUsedAndLowerCased(string liveMode, string expected)
    {
        // The game is what is actually running, so it wins over the queue.
        Assert.Equal(expected, GameModes.Of(liveMode, 420));
    }

    [Fact]
    public void Of_ALiveModeWithPadding_IsTrimmed()
    {
        Assert.Equal("aram", GameModes.Of("  ARAM  ", 420));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_NoLiveMode_FallsBackToTheQueue(string? liveMode)
    {
        // Before a game runs the client does not report a mode, so the queue is all there is.
        Assert.Equal("aram", GameModes.Of(liveMode, 450));
    }

    [Theory]
    [InlineData("CLASSIC")]
    [InlineData("classic")]
    [InlineData("Classic")]
    public void Of_TheWordClassic_FallsBackToTheQueue(string liveMode)
    {
        // "CLASSIC" is the client saying there is no special mode, so the queue decides after all.
        Assert.Equal("aram", GameModes.Of(liveMode, 450));
        Assert.Equal("kiwi", GameModes.Of(liveMode, 2400));
    }

    [Fact]
    public void Of_ClassicOnAnOrdinaryQueue_HasNoSpecialMode()
    {
        Assert.Equal("", GameModes.Of("CLASSIC", 420));
    }

    [Fact]
    public void Of_ALiveModeOnAnOrdinaryQueue_StillUsesTheLiveMode()
    {
        // The mode the game reports is not a guess to be second-guessed by the queue.
        Assert.Equal("urf", GameModes.Of("URF", 420));
    }
}
