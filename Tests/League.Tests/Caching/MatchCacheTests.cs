using League.Caching;
using Newtonsoft.Json.Linq;

namespace League.Tests.Caching;

/// <summary>
/// The page cache in front of the match history: one entry per player, tag and page, and no more than five pages kept
/// for any one tag.
///
/// The cache is process-wide, so every case clears it first and the class runs on its own. Two of its rules are worth
/// stating: an empty page is not worth remembering, and a page comes back as a freshly parsed array rather than the
/// stored text, so a caller editing it cannot edit the cache.
/// </summary>
public class MatchCacheTests : IDisposable
{
    public MatchCacheTests() => MatchCache.ClearAll();

    public void Dispose()
    {
        MatchCache.ClearAll();
        GC.SuppressFinalize(this);
    }

    private static JArray Page(params int[] ids) => new(ids.Select(id => new JObject { ["gameId"] = id }));

    private static int[] Ids(JArray? page) => [.. (page ?? []).Select(row => (int)row["gameId"]!)];

    // ---- storing and reading ----

    [Fact]
    public void Get_APageThatWasAdded_ComesBack()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1, 2));

        Assert.Equal([1, 2], Ids(MatchCache.Get("p", "all", 0, 10)));
    }

    [Fact]
    public void Get_ABegIndexOrPageSizeThatDiffers_IsAnotherEntry()
    {
        // The page key is the triple, so the same player and tag can hold several pages.
        MatchCache.Add("p", "all", 0, 10, Page(1));
        MatchCache.Add("p", "all", 10, 10, Page(2));

        Assert.Equal([1], Ids(MatchCache.Get("p", "all", 0, 10)));
        Assert.Equal([2], Ids(MatchCache.Get("p", "all", 10, 10)));
    }

    [Fact]
    public void Get_NothingStored_IsNull()
    {
        Assert.Null(MatchCache.Get("p", "all", 0, 10));
    }

    [Fact]
    public void Get_AnotherPlayer_IsNotSeen()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1));

        Assert.Null(MatchCache.Get("q", "all", 0, 10));
    }

    [Fact]
    public void Get_AnotherTag_IsNotSeen()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1));

        Assert.Null(MatchCache.Get("p", "ranked", 0, 10));
    }

    [Fact]
    public void Add_TheSamePageTwice_KeepsTheLaterOne()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1));
        MatchCache.Add("p", "all", 0, 10, Page(2));

        Assert.Equal([2], Ids(MatchCache.Get("p", "all", 0, 10)));
    }

    // ---- the tag means "all" when it is missing ----

    [Fact]
    public void Add_ANullTag_IsStoredUnderAll()
    {
        // "No filter" and "the all filter" are the same request, so they share one entry.
        MatchCache.Add("p", null, 0, 10, Page(1));

        Assert.Equal([1], Ids(MatchCache.Get("p", "all", 0, 10)));
    }

    [Fact]
    public void Get_ANullTag_FindsWhatAllStored()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1));

        Assert.Equal([1], Ids(MatchCache.Get("p", null, 0, 10)));
    }

    // ---- what is not worth remembering ----

    [Fact]
    public void Add_Nothing_IsNotStored()
    {
        // Remembering an empty page would make a later real answer look cached.
        MatchCache.Add("p", "all", 0, 10, null!);

        Assert.Null(MatchCache.Get("p", "all", 0, 10));
    }

    [Fact]
    public void Add_AnEmptyPage_IsNotStored()
    {
        MatchCache.Add("p", "all", 0, 10, Page());

        Assert.Null(MatchCache.Get("p", "all", 0, 10));
    }

    // ---- the cap ----

    [Fact]
    public void Add_SixPages_KeepsOnlyFive()
    {
        // Only the last few pages are worth holding; paging forward would otherwise grow without bound.
        for (int i = 0; i < 6; i++)
        {
            MatchCache.Add("p", "all", i * 10, 10, Page(i));
        }

        Assert.Null(MatchCache.Get("p", "all", 0, 10));
        Assert.Equal([1], Ids(MatchCache.Get("p", "all", 10, 10)));
        Assert.Equal([5], Ids(MatchCache.Get("p", "all", 50, 10)));
    }

    [Fact]
    public void Add_TheCapIsPerTag()
    {
        // Two tags are two runs of pages, each with its own limit.
        for (int i = 0; i < 6; i++)
        {
            MatchCache.Add("p", "all", i * 10, 10, Page(i));
        }

        MatchCache.Add("p", "ranked", 0, 10, Page(9));

        Assert.Equal([9], Ids(MatchCache.Get("p", "ranked", 0, 10)));
    }

    [Fact]
    public void Add_TheCapIsPerPlayer()
    {
        for (int i = 0; i < 6; i++)
        {
            MatchCache.Add("p", "all", i * 10, 10, Page(i));
        }

        MatchCache.Add("q", "all", 0, 10, Page(9));

        Assert.Equal([9], Ids(MatchCache.Get("q", "all", 0, 10)));
    }

    // ---- the answer is a copy ----

    [Fact]
    public void Get_EditingWhatCameBack_DoesNotEditTheCache()
    {
        // The stored form is the text, so every read parses a fresh array.
        MatchCache.Add("p", "all", 0, 10, Page(1));
        JArray first = MatchCache.Get("p", "all", 0, 10)!;
        first.Clear();

        Assert.Equal([1], Ids(MatchCache.Get("p", "all", 0, 10)));
    }

    // ---- emptying it ----

    [Fact]
    public void ClearPlayer_ForgetsOnlyThatPlayer()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1));
        MatchCache.Add("q", "all", 0, 10, Page(2));

        MatchCache.ClearPlayer("p");

        Assert.Null(MatchCache.Get("p", "all", 0, 10));
        Assert.Equal([2], Ids(MatchCache.Get("q", "all", 0, 10)));
    }

    [Fact]
    public void ClearPlayer_APlayerThatWasNeverCached_ChangesNothing()
    {
        MatchCache.Add("q", "all", 0, 10, Page(2));

        MatchCache.ClearPlayer("absent");

        Assert.Equal([2], Ids(MatchCache.Get("q", "all", 0, 10)));
    }

    [Fact]
    public void ClearAll_ForgetsEverybody()
    {
        MatchCache.Add("p", "all", 0, 10, Page(1));
        MatchCache.Add("q", "all", 0, 10, Page(2));

        MatchCache.ClearAll();

        Assert.Null(MatchCache.Get("p", "all", 0, 10));
        Assert.Null(MatchCache.Get("q", "all", 0, 10));
    }
}
