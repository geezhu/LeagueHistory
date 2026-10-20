using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// Paging through a player's game history by outcome, scanning only as far as needed to fill the page.
///
/// Three details decide what a page costs and what it returns. The page number is one-based, and the target is "page
/// times size" matches seen - not "size matches on this page" - so page three costs three pages' worth of scanning.
/// The scan stops at two hundred records whatever the outcome, because this is a filter over a list the client pages
/// itself, not a whole-history search. And an empty batch means the history has run out, while a null one means the
/// fetch failed: one ends the scan quietly, the other is an error.
/// </summary>
public class OutcomeHistoryPagerTests
{
    private static JObject Game(bool win, int id) =>
        new()
        {
            ["info"] = new JObject
            {
                ["participants"] = new JArray(
                    new JObject
                    {
                        ["puuid"] = "p",
                        ["stats"] = new JObject { ["win"] = win, ["gameId"] = id },
                    }
                ),
            },
        };

    private static JObject Remake(int id) =>
        new()
        {
            ["info"] = new JObject
            {
                ["gameEndedInEarlySurrender"] = true,
                ["participants"] = new JArray(new JObject { ["puuid"] = "p" }),
            },
        };

    /// <summary>A history that hands out one slice per call and remembers how it was asked.</summary>
    private sealed class History(params JObject[] records)
    {
        public List<(int Start, int Count)> Calls { get; } = new();

        public bool ReturnNull { get; set; }

        public Task<JArray> Fetch(int start, int count)
        {
            Calls.Add((start, count));
            if (ReturnNull)
            {
                return Task.FromResult<JArray>(null!);
            }

            return Task.FromResult(new JArray(records.Skip(start).Take(count)));
        }
    }

    private static int[] Ids(JArray page) =>
        [.. page.Select(g => (int)g["info"]!["participants"]![0]!["stats"]!["gameId"]!)];

    // ---- one batch is enough ----

    [Fact]
    public async Task LoadPage_TheFirstBatchFillsThePage_IsFetchedOnce()
    {
        // Traced: the first batch is two wins, which is exactly the two matches the page wants - so one call.
        History history = new(Game(true, 1), Game(true, 2), Game(true, 3));
        OutcomeHistoryPager pager = new();

        JArray page = await pager.LoadPage("p", win: true, 1, 2, history.Fetch);

        Assert.Single(history.Calls);
        Assert.Equal([1, 2], Ids(page));
    }

    [Fact]
    public async Task LoadPage_OnlyTheWantedCount_IsAskedFor()
    {
        // Asking for more than is needed would read records the page does not show.
        History history = new(Game(true, 1), Game(true, 2), Game(true, 3));
        OutcomeHistoryPager pager = new();

        await pager.LoadPage("p", win: true, 1, 2, history.Fetch);

        Assert.Equal((0, 2), history.Calls[0]);
    }

    [Fact]
    public async Task LoadPage_Matches_IsHowManyWereFoundNotHowManyThePageHolds()
    {
        // The caller shows "found so far" beside the page, so the two numbers are different.
        History history = new(Game(true, 1), Game(true, 2), Game(true, 3), Game(true, 4));
        OutcomeHistoryPager pager = new();

        await pager.LoadPage("p", win: true, 1, 2, history.Fetch);

        Assert.Equal(2, pager.Matches);
    }

    // ---- more batches ----

    [Fact]
    public async Task LoadPage_NotEnoughInTheFirstBatch_AsksForTheRest()
    {
        // Traced: two records, one a win. The page wants two wins, so a second batch of one is asked for.
        History history = new(Game(true, 1), Game(false, 2), Game(true, 3));
        OutcomeHistoryPager pager = new();

        JArray page = await pager.LoadPage("p", win: true, 1, 2, history.Fetch);

        Assert.Equal(2, history.Calls.Count);
        Assert.Equal([1, 3], Ids(page));
        Assert.Equal(3, pager.Scanned);
    }

    [Fact]
    public async Task LoadPage_TheSecondCall_DoesNotRefetchWhatWasScanned()
    {
        // The pager keeps its records, so a second page continues where the first stopped.
        History history = new(Game(true, 1), Game(true, 2), Game(false, 3), Game(true, 4));
        OutcomeHistoryPager pager = new();
        await pager.LoadPage("p", win: true, 1, 1, history.Fetch);
        int scannedBefore = pager.Scanned;

        await pager.LoadPage("p", win: true, 2, 1, history.Fetch);

        Assert.True(pager.Scanned >= scannedBefore, "Scanning never goes backwards.");
        Assert.All(history.Calls.Skip(1), call => Assert.True(call.Start >= scannedBefore - 1));
    }

    // ---- the page number ----

    [Fact]
    public async Task LoadPage_TheSecondPage_SkipsTheFirstPagesMatches()
    {
        // The page number is one-based: page two skips one page's worth of matches.
        History history = new(Game(true, 1), Game(true, 2), Game(true, 3));
        OutcomeHistoryPager pager = new();

        JArray page = await pager.LoadPage("p", win: true, page: 2, size: 1, history.Fetch);

        Assert.Equal([2], Ids(page));
    }

    [Fact]
    public async Task LoadPage_TheSecondPage_WantsTwiceAsManyMatchesSeen()
    {
        // The target is "page times size" seen, not "size on this page".
        History history = new(Game(true, 1), Game(true, 2));
        OutcomeHistoryPager pager = new();

        await pager.LoadPage("p", win: true, page: 2, size: 1, history.Fetch);

        Assert.Equal(2, pager.Matches);
    }

    // ---- running out ----

    [Fact]
    public async Task LoadPage_AnEmptyBatch_EndsTheScanQuietly()
    {
        // The history has run out; that is not an error.
        History history = new(Game(true, 1));
        OutcomeHistoryPager pager = new();

        JArray page = await pager.LoadPage("p", win: true, 1, 5, history.Fetch);

        Assert.True(pager.Exhausted);
        Assert.Equal([1], Ids(page));
    }

    [Fact]
    public async Task LoadPage_OnceExhausted_LaterCallsDoNotFetch()
    {
        // Nothing more can arrive, so asking again would be a wasted round trip.
        History history = new(Game(true, 1));
        OutcomeHistoryPager pager = new();
        await pager.LoadPage("p", win: true, 1, 5, history.Fetch);
        int calls = history.Calls.Count;

        await pager.LoadPage("p", win: false, 1, 5, history.Fetch);

        Assert.Equal(calls, history.Calls.Count);
    }

    [Fact]
    public async Task LoadPage_AFailedFetch_IsAnErrorRatherThanAnEnd()
    {
        // "Could not fetch" and "nothing left" are different, and the message says which.
        History history = new() { ReturnNull = true };
        OutcomeHistoryPager pager = new();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pager.LoadPage("p", win: true, 1, 5, history.Fetch)
        );

        Assert.Equal("获取筛选记录失败，请重试", error.Message);
    }

    // ---- the two-hundred cap ----

    [Fact]
    public async Task LoadPage_TheTwoHundredthRecord_EndsTheScan()
    {
        // The pager is a filter over a paged list, not a search of the whole history.
        History history = new([.. Enumerable.Range(0, 400).Select(i => Game(win: false, i))]);
        OutcomeHistoryPager pager = new();

        await pager.LoadPage("p", win: true, 1, 5, history.Fetch);

        Assert.True(pager.Exhausted);
        Assert.Equal(200, pager.Scanned);
    }

    [Fact]
    public async Task LoadPage_ReachingTheCap_NeverAsksBeyondIt()
    {
        History history = new([.. Enumerable.Range(0, 400).Select(i => Game(win: false, i))]);
        OutcomeHistoryPager pager = new();

        await pager.LoadPage("p", win: true, 1, 5, history.Fetch);

        Assert.All(history.Calls, call => Assert.True(call.Start + call.Count <= 200));
    }

    // ---- what counts ----

    [Fact]
    public async Task LoadPage_ARemake_CountsAsNeitherOutcome()
    {
        // A remake is not a win or a loss, so it fills neither page.
        History history = new(Remake(1), Game(true, 2));
        OutcomeHistoryPager pager = new();

        JArray page = await pager.LoadPage("p", win: true, 1, 5, history.Fetch);

        Assert.Equal([2], Ids(page));
    }

    [Fact]
    public async Task LoadPage_AnEmptyHistory_IsExhaustedAtOnce()
    {
        History history = new();
        OutcomeHistoryPager pager = new();

        JArray page = await pager.LoadPage("p", win: true, 1, 5, history.Fetch);

        Assert.True(pager.Exhausted);
        Assert.Empty(page);
        Assert.Equal(0, pager.Matches);
    }
}
