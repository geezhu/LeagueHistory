using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Cleaning a game session that still carries players from an earlier game - the user then appears on both teams.
///
/// The tool is the live game's own champion list: champions are unique within a game in every mode the window lists,
/// so a seat whose champion is not in the live game cannot be one of its players. The session is copied rather than
/// edited, because the caller may still need what the client sent.
/// </summary>
public class GameSessionTeamsTests
{
    private static JObject Seat(int championId) => new() { ["championId"] = championId };

    private static JObject Session(params int[] champions) =>
        new()
        {
            ["teamOne"] = new JArray(champions.Select(Seat)),
            ["teamTwo"] = new JArray(champions.Select(Seat)),
            ["playerChampionSelections"] = new JArray(champions.Select(Seat)),
        };

    private static int[] Champions(JToken? field) =>
        [.. ((JArray?)field ?? []).Select(seat => (int)seat["championId"]!)];

    [Fact]
    public void KeepLive_OnlyTheLiveChampions_Stay()
    {
        // A champion in the live game is the only way a seat can belong to it.
        JObject kept = GameSessionTeams.KeepLive(Session(1, 2, 3), new HashSet<int> { 1, 3 });

        Assert.Equal([1, 3], Champions(kept["teamOne"]));
    }

    [Fact]
    public void KeepLive_AllThreeFieldsAreCleaned()
    {
        // Leaving one behind would keep the stale seat somewhere else.
        JObject kept = GameSessionTeams.KeepLive(Session(1, 2), new HashSet<int> { 2 });

        Assert.Equal([2], Champions(kept["teamOne"]));
        Assert.Equal([2], Champions(kept["teamTwo"]));
        Assert.Equal([2], Champions(kept["playerChampionSelections"]));
    }

    [Fact]
    public void KeepLive_NothingLive_LeavesEmptyLists()
    {
        JObject kept = GameSessionTeams.KeepLive(Session(1, 2), new HashSet<int>());

        Assert.Empty(Champions(kept["teamOne"]));
    }

    [Fact]
    public void KeepLive_ASeatWithoutAChampion_IsDropped()
    {
        // Zero is not a champion, so it is not in the live set and a seat reporting it is dropped.
        var session = new JObject { ["teamOne"] = new JArray(Seat(0), Seat(1)) };

        Assert.Equal([1], Champions(GameSessionTeams.KeepLive(session, new HashSet<int> { 1 })["teamOne"]));
    }

    [Fact]
    public void KeepLive_TheOriginalSession_IsNotTouched()
    {
        // The caller may still need what the client sent; the cleaning is on a copy.
        JObject session = Session(1, 2);

        JObject kept = GameSessionTeams.KeepLive(session, new HashSet<int> { 1 });

        Assert.Equal([1, 2], Champions(session["teamOne"]));
        Assert.Equal([1], Champions(kept["teamOne"]));
        Assert.NotSame(session, kept);
    }

    [Fact]
    public void KeepLive_AnotherField_IsCarriedOverUnchanged()
    {
        // Only the three player lists are cleaned; the rest of the session is what the client said.
        var session = Session(1);
        session["gameId"] = 12345;

        Assert.Equal(12345, (int?)GameSessionTeams.KeepLive(session, new HashSet<int>())["gameId"]);
    }

    [Fact]
    public void KeepLive_AMissingField_BecomesAnEmptyList()
    {
        // The result always has all three lists, so a caller does not have to check.
        JObject kept = GameSessionTeams.KeepLive(new JObject(), new HashSet<int> { 1 });

        Assert.Empty(Champions(kept["teamOne"]));
        Assert.Empty(Champions(kept["teamTwo"]));
        Assert.Empty(Champions(kept["playerChampionSelections"]));
    }
}
