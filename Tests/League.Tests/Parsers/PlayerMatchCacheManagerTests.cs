using League.Models;
using League.Parsers;

namespace League.Tests.Parsers;

/// <summary>
/// The per-player, per-filter cache of match history used while the room is being assembled.
///
/// It is an ordinary dictionary with one guard: a summoner id of zero is not a player, so a history filed under it is
/// dropped rather than stored. That matters because zero is what a hidden or not-yet-loaded seat reports, and an entry
/// there would be found again by whichever such seat was asked about next.
/// </summary>
public class PlayerMatchCacheManagerTests
{
    private static PlayerMatchInfo Info() => new() { Player = new PlayerInfo { SummonerId = 7 } };

    [Fact]
    public void TryGetPlayerMatch_NothingCached_IsFalse()
    {
        Assert.False(new PlayerMatchCacheManager().TryGetPlayerMatch(7, "all", out _));
    }

    [Fact]
    public void CacheThenGet_TheSamePlayerAndFilter_HandsItBack()
    {
        PlayerMatchCacheManager cache = new();
        PlayerMatchInfo info = Info();

        cache.CachePlayerMatch(7, "all", info);

        Assert.True(cache.TryGetPlayerMatch(7, "all", out PlayerMatchInfo found));
        Assert.Same(info, found);
    }

    [Fact]
    public void CacheThenGet_AnotherFilter_IsAnotherEntry()
    {
        // The filter is part of the key: the same player has a history per filter.
        PlayerMatchCacheManager cache = new();
        cache.CachePlayerMatch(7, "all", Info());

        Assert.False(cache.TryGetPlayerMatch(7, "ranked", out _));
    }

    [Fact]
    public void CacheThenGet_AnotherPlayer_IsAnotherEntry()
    {
        PlayerMatchCacheManager cache = new();
        cache.CachePlayerMatch(7, "all", Info());

        Assert.False(cache.TryGetPlayerMatch(8, "all", out _));
    }

    [Fact]
    public void CacheTheSameKeyTwice_KeepsTheLaterOne()
    {
        PlayerMatchCacheManager cache = new();
        PlayerMatchInfo first = Info();
        PlayerMatchInfo second = Info();

        cache.CachePlayerMatch(7, "all", first);
        cache.CachePlayerMatch(7, "all", second);

        Assert.True(cache.TryGetPlayerMatch(7, "all", out PlayerMatchInfo found));
        Assert.Same(second, found);
    }

    [Fact]
    public void Cache_ASummonerIdOfZero_IsNotStored()
    {
        // Zero is what a hidden or not-yet-loaded seat reports; filing a history under it would give it to the next
        // such seat that asked.
        PlayerMatchCacheManager cache = new();

        cache.CachePlayerMatch(0, "all", Info());

        Assert.False(cache.TryGetPlayerMatch(0, "all", out _));
    }

    [Fact]
    public void Cache_ANegativeSummonerId_IsStored()
    {
        // Only zero is guarded; a negative id is nonsense but not the one value the guard is about, so it is kept.
        PlayerMatchCacheManager cache = new();

        cache.CachePlayerMatch(-1, "all", Info());

        Assert.True(cache.TryGetPlayerMatch(-1, "all", out _));
    }

    [Fact]
    public void ClearCache_ForgetsEverything()
    {
        PlayerMatchCacheManager cache = new();
        cache.CachePlayerMatch(7, "all", Info());
        cache.CachePlayerMatch(8, "ranked", Info());

        cache.ClearCache();

        Assert.False(cache.TryGetPlayerMatch(7, "all", out _));
        Assert.False(cache.TryGetPlayerMatch(8, "ranked", out _));
    }

    [Fact]
    public void ClearCache_OnAnEmptyCache_IsSafe()
    {
        new PlayerMatchCacheManager().ClearCache();
    }
}
