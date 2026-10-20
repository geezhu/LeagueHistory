using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The room loading log: which generation, which stage, which side, and a pseudonym for the player.
///
/// The pseudonym is the point of this one. A player is written as ten hex characters of a salted SHA-256 of their
/// identity, with a salt that is new for every instance - so a log line can be followed from one call to the next, but
/// cannot be matched against other logs from other runs, and the identity itself never appears.
/// </summary>
public class RoomLoadDiagnosticsTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new("room-diag");
    private readonly List<RoomLoadDiagnostics> _writers = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (RoomLoadDiagnostics writer in _writers)
        {
            await writer.FlushAsync();
        }

        _folder.Dispose();
    }

    private RoomLoadDiagnostics Diagnostics(string name = "room.log")
    {
        RoomLoadDiagnostics diagnostics = new(_folder.File(name));
        _writers.Add(diagnostics);
        return diagnostics;
    }

    private static async Task<string[]> LinesAsync(RoomLoadDiagnostics diagnostics)
    {
        await diagnostics.FlushAsync();
        return File.Exists(diagnostics.LogPath) ? await File.ReadAllLinesAsync(diagnostics.LogPath) : [];
    }

    /// <summary>The part of a line after <c>player=</c>, which is the pseudonym.</summary>
    private static string Player(string line)
    {
        int start = line.IndexOf("player=", StringComparison.Ordinal) + "player=".Length;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    [Fact]
    public void LogPath_IsWhatTheCallerGave()
    {
        Assert.Equal(_folder.File("room.log"), Diagnostics().LogPath);
    }

    [Fact]
    public async Task Report_WritesTheGenerationTheStageAndTheDetail()
    {
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(3, "读取名单", null, "共十人");
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains(" generation=3 stage=读取名单 ", line, StringComparison.Ordinal);
        Assert.Contains(" 共十人", line, StringComparison.Ordinal);
    }

    // ---- which side ----

    [Theory]
    [InlineData("ally/abc", "team=ally")]
    [InlineData("enemy/abc", "team=enemy")]
    [InlineData("other/abc", "team=-")]
    [InlineData("abc", "team=-")]
    public async Task Report_TheSidesAreReadFromTheKeyPrefix(string key, string expected)
    {
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "阶段", key);
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains(expected, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_WithoutAKey_BothTheSideAndThePlayerAreDashes()
    {
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "阶段");
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains("team=- player=-", line, StringComparison.Ordinal);
    }

    // ---- the pseudonym ----

    [Fact]
    public async Task Report_ThePlayerIsTenHexCharacters()
    {
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "阶段", "ally/abc");
        string player = Player(Assert.Single(await LinesAsync(diagnostics)));

        Assert.Equal(10, player.Length);
        Assert.All(player, c => Assert.True(char.IsAsciiHexDigit(c), $"'{player}' is not hexadecimal."));
    }

    [Fact]
    public async Task Report_TheSamePlayerTwice_IsTheSamePseudonym()
    {
        // The whole reason for hashing rather than redacting: a log line can still be followed.
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "第一步", "ally/abc");
        diagnostics.Report(0, "第二步", "ally/abc");
        string[] lines = await LinesAsync(diagnostics);

        Assert.Equal(Player(lines[0]), Player(lines[1]));
    }

    [Fact]
    public async Task Report_TwoDifferentPlayers_GetDifferentPseudonyms()
    {
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "阶段", "ally/abc");
        diagnostics.Report(0, "阶段", "ally/def");
        string[] lines = await LinesAsync(diagnostics);

        Assert.NotEqual(Player(lines[0]), Player(lines[1]));
    }

    [Fact]
    public async Task Report_TwoWriters_DoNotAgreeOnAPseudonym()
    {
        // Each writer salts its own hashes, so two logs cannot be joined against each other.
        // Two writers to one file would interleave, so each gets its own.
        RoomLoadDiagnostics first = Diagnostics("first.log");
        RoomLoadDiagnostics second = Diagnostics("second.log");

        first.Report(0, "阶段", "ally/abc");
        second.Report(0, "阶段", "ally/abc");

        Assert.NotEqual(
            Player(Assert.Single(await LinesAsync(first))),
            Player(Assert.Single(await LinesAsync(second)))
        );
    }

    [Fact]
    public async Task Report_TheIdentityItself_IsNotInTheFile()
    {
        // The point of the hash: the file must not name the player.
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "阶段", "ally/very-recognisable-name");
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.DoesNotContain("very-recognisable-name", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_TheSameNameOnEitherSide_GetsTheSamePseudonym()
    {
        // What is hashed is the name with its side stripped off, so the same name reads the same whichever side it is
        // on. That is deliberate: the side is already a field of its own on the line.
        RoomLoadDiagnostics diagnostics = Diagnostics();

        diagnostics.Report(0, "阶段", "ally/same");
        diagnostics.Report(0, "阶段", "enemy/same");
        string[] lines = await LinesAsync(diagnostics);

        Assert.Equal(Player(lines[0]), Player(lines[1]));
        Assert.Contains("team=ally", lines[0], StringComparison.Ordinal);
        Assert.Contains("team=enemy", lines[1], StringComparison.Ordinal);
    }
}
