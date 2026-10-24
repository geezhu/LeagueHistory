using League.Caching;
using Newtonsoft.Json.Linq;

namespace League.Tests.Caching;

/// <summary>
/// The match history cache: one run of games per player and filter, newest first, cut into pages on demand. New games
/// join at the head when a refresh is asked for, older ones at the tail when a page past the run is asked for, and
/// nothing already held is fetched again.
///
/// The cache is process-wide, so every case clears it first. The server is a list of game ids, newest first, that
/// records every range it was asked for.
/// </summary>
public class MatchCacheTests : IDisposable
{
    private readonly FakeServer _server = new();

    public MatchCacheTests() => MatchCache.ClearAll();

    public void Dispose()
    {
        MatchCache.ClearAll();
        GC.SuppressFinalize(this);
    }

    private Task<JArray?> PageAsync(int begIndex, int pageSize, bool refresh = false, string? tag = "all") =>
        MatchCache.GetPageAsync("p", tag, begIndex, pageSize, refresh, _server.FetchAsync);

    private static int[] Ids(JArray? page) => [.. (page ?? []).Select(row => (int)row["json"]!["gameId"]!)];

    // ---- reading pages ----

    [Fact]
    public async Task GetPageAsync_FirstPage_ReturnsTheNewestGames()
    {
        _server.Hold(30);

        JArray? page = await PageAsync(0, 3);

        Assert.Equal([30, 29, 28], Ids(page));
    }

    [Fact]
    public async Task GetPageAsync_ThePageAgain_AsksNothingMore()
    {
        _server.Hold(30);
        await PageAsync(0, 10);

        await PageAsync(0, 10);

        Assert.Equal(["0+10"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_ASmallerPageSize_IsCutFromTheRunWithoutAsking()
    {
        // A page size that follows the window height must not throw away what is held.
        _server.Hold(30);
        await PageAsync(0, 20);

        JArray? page = await PageAsync(8, 8);

        Assert.Equal([22, 21, 20, 19, 18, 17, 16, 15], Ids(page));
        Assert.Equal(["0+20"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_ThePageAfterTheRun_AsksOnlyForTheMissingTail()
    {
        _server.Hold(30);
        await PageAsync(0, 10);

        JArray? page = await PageAsync(10, 10);

        Assert.Equal([20, 19, 18, 17, 16, 15, 14, 13, 12, 11], Ids(page));
        Assert.Equal(["0+10", "9+11"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_PastTheLastGame_AsksNoMoreOnceTheServerRanShort()
    {
        _server.Hold(5);
        await PageAsync(0, 10);

        JArray? page = await PageAsync(10, 10);

        Assert.Empty(Ids(page));
        Assert.Equal(["0+10"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_PastTheLimit_AsksForNoGameBeyond200()
    {
        // SGP keeps the last 200 games; asking further only wastes a request.
        _server.Hold(300);

        await PageAsync(190, 20);

        Assert.Equal(["0+200"], _server.Asked);
    }

    // ---- new games ----

    [Fact]
    public async Task GetPageAsync_RefreshAfterANewGame_PutsItFirst()
    {
        _server.Hold(30);
        await PageAsync(0, 5);
        _server.Play(31);

        JArray? page = await PageAsync(0, 5, refresh: true);

        Assert.Equal([31, 30, 29, 28, 27], Ids(page));
    }

    [Fact]
    public async Task GetPageAsync_RefreshAfterANewGame_KeepsTheOlderPagesHeld()
    {
        // Only the head is asked for; the games behind it stay where they were.
        _server.Hold(30);
        await PageAsync(0, 20);
        _server.Play(31);
        await PageAsync(0, 5, refresh: true);

        JArray? page = await PageAsync(15, 5);

        Assert.Equal([16, 15, 14, 13, 12], Ids(page));
        Assert.Equal(["0+20", "0+5"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_RefreshThatMeetsNoHeldGame_StartsTheRunAgain()
    {
        // More new games than the refresh asked for leaves a gap of unknown size, so nothing older can be trusted.
        _server.Hold(30);
        await PageAsync(0, 5);
        _server.Play(31, 32, 33, 34, 35, 36);

        JArray? page = await PageAsync(5, 5);
        JArray? refreshed = await PageAsync(0, 5, refresh: true);

        Assert.Equal([25, 24, 23, 22, 21], Ids(page));
        Assert.Equal([36, 35, 34, 33, 32], Ids(refreshed));
    }

    [Fact]
    public async Task GetPageAsync_NewGamesBeforeTheNextPage_ShowsNoGameTwice()
    {
        // Without a refresh the server's positions moved by two. The tail is asked for from the last held game, which
        // turns up two places later, so only what follows it is taken.
        _server.Hold(30);
        await PageAsync(0, 5);
        _server.Play(31, 32);

        JArray? page = await PageAsync(5, 5);

        Assert.Equal([25, 24, 23, 22, 21], Ids(page));
        Assert.Equal(["0+5", "4+6", "9+3"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_RefreshOnOneFilter_ChecksTheOtherFiltersHeadsToo()
    {
        _server.Hold(30);
        await PageAsync(0, 5, tag: "q_2400");
        await PageAsync(0, 5);
        _server.Play(31);
        await PageAsync(0, 5, refresh: true);

        JArray? page = await PageAsync(0, 5, tag: "q_2400");

        Assert.Equal([31, 30, 29, 28, 27], Ids(page));
    }

    // ---- what is kept apart ----

    [Fact]
    public async Task GetPageAsync_AnotherFilter_IsAnotherRun()
    {
        _server.Hold(30);
        await PageAsync(0, 5);

        await PageAsync(0, 5, tag: "q_2400");

        Assert.Equal(["0+5", "0+5"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_ANullFilter_SharesTheAllRun()
    {
        // "No filter" and "the all filter" are the same request.
        _server.Hold(30);
        await PageAsync(0, 5);

        await PageAsync(0, 5, tag: null);

        Assert.Equal(["0+5"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_AnotherPlayer_IsAnotherRun()
    {
        _server.Hold(30);
        await PageAsync(0, 5);

        await MatchCache.GetPageAsync("q", "all", 0, 5, false, _server.FetchAsync);

        Assert.Equal(["0+5", "0+5"], _server.Asked);
    }

    // ---- failures and empty answers ----

    [Fact]
    public async Task GetPageAsync_ServerUnavailable_ReturnsNull()
    {
        _server.Hold(30);
        _server.Down = true;

        JArray? page = await PageAsync(0, 5);

        Assert.Null(page);
    }

    [Fact]
    public async Task GetPageAsync_RefreshThatFailed_ChecksTheHeadNextTime()
    {
        _server.Hold(30);
        await PageAsync(0, 5);
        _server.Down = true;
        await PageAsync(0, 5, refresh: true);
        _server.Down = false;

        await PageAsync(0, 5);

        Assert.Equal(["0+5", "0+5", "0+5"], _server.Asked);
    }

    [Fact]
    public async Task GetPageAsync_NoGamesAtAll_IsNotRemembered()
    {
        // Remembering an empty answer would make a later real answer look cached.
        await PageAsync(0, 5);
        _server.Hold(3);

        JArray? page = await PageAsync(0, 5);

        Assert.Equal([3, 2, 1], Ids(page));
    }

    // ---- the answer is a copy ----

    [Fact]
    public async Task GetPageAsync_EditingWhatCameBack_DoesNotEditTheCache()
    {
        _server.Hold(30);
        JArray first = (await PageAsync(0, 5))!;
        first.Clear();

        JArray? again = await PageAsync(0, 5);

        Assert.Equal([30, 29, 28, 27, 26], Ids(again));
    }

    // ---- emptying it ----

    [Fact]
    public async Task ClearAll_ForgetsEverything()
    {
        _server.Hold(30);
        await PageAsync(0, 5);

        MatchCache.ClearAll();
        await PageAsync(0, 5);

        Assert.Equal(["0+5", "0+5"], _server.Asked);
    }

    /// <summary>The match history service: game ids newest first, served as SGP wraps them.</summary>
    private sealed class FakeServer
    {
        private readonly List<int> _games = [];

        public List<string> Asked { get; } = [];

        public bool Down { get; set; }

        /// <summary>Games 1 to count, the highest newest.</summary>
        public void Hold(int count) => _games.AddRange(Enumerable.Range(1, count).Reverse());

        /// <summary>Games finished since, in the order they were played.</summary>
        public void Play(params int[] ids) => _games.InsertRange(0, ids.Reverse());

        public Task<JArray?> FetchAsync(int start, int count)
        {
            Asked.Add($"{start}+{count}");
            if (Down)
            {
                return Task.FromResult<JArray?>(null);
            }

            IEnumerable<JObject> rows = _games
                .Skip(start)
                .Take(count)
                .Select(id => new JObject { ["json"] = new JObject { ["gameId"] = id } });
            return Task.FromResult<JArray?>(new JArray(rows));
        }
    }
}
