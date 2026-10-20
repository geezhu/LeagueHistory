using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Whether the window in front of the user is the game's own: the foreground window's process id, and the process name
/// the client reports for it.
///
/// All three have to agree. An id of zero means there is no foreground window to speak of, the ids have to be the same
/// process, and the name has to be the game's - matched without regard to case, because the client is not consistent
/// about it.
/// </summary>
public class GameWindowIdentityTests
{
    private const string Game = "League of Legends";

    [Fact]
    public void Matches_TheGameInFront_IsTheGame()
    {
        Assert.True(GameWindowIdentity.Matches(1234, 1234, Game));
    }

    [Fact]
    public void Matches_TheNameIsMatchedWhateverItsCase()
    {
        Assert.True(GameWindowIdentity.Matches(1234, 1234, "league of legends"));
        Assert.True(GameWindowIdentity.Matches(1234, 1234, "LEAGUE OF LEGENDS"));
    }

    [Fact]
    public void Matches_NoForegroundWindow_IsNotTheGame()
    {
        // An id of zero is what the system reports when nothing has the focus.
        Assert.False(GameWindowIdentity.Matches(0, 0, Game));
    }

    [Fact]
    public void Matches_AnotherProcessInFront_IsNotTheGame()
    {
        Assert.False(GameWindowIdentity.Matches(1234, 999, Game));
    }

    [Fact]
    public void Matches_AnotherProgramWithTheSameId_IsNotTheGame()
    {
        // Two processes can share an id across time; the name is what settles it.
        Assert.False(GameWindowIdentity.Matches(1234, 1234, "LeagueClientUx"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("League of Legends.exe")]
    [InlineData("League of Legend")]
    public void Matches_ANameThatIsNotExactlyTheGames_IsNotTheGame(string name)
    {
        // The comparison is of the whole name, not of a prefix.
        Assert.False(GameWindowIdentity.Matches(1234, 1234, name));
    }

    [Fact]
    public void Matches_ANegativeProcessId_IsNeverTheForegroundWindow()
    {
        // The foreground id is unsigned, so a negative process id cannot be equal to it however it is read.
        Assert.False(GameWindowIdentity.Matches(uint.MaxValue, -1, Game));
    }
}
