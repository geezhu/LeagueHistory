using League.Controls;
using League.Managers;
using League.Models;

namespace League.Tests.Managers;

/// <summary>
/// The cache behind the player cards: the match history per summoner, the current champion per summoner, and the card
/// control itself.
///
/// Most members refuse a summoner id of zero or less, because that is what a hidden or not-yet-loaded player looks
/// like; the one exception is the current champion, which may be recorded for such a player as long as a champion is
/// known. Reading the whole cache hands back a copy, so a caller cannot edit the cache through it.
/// </summary>
public class PlayerCardCacheTests
{
    private static PlayerMatchInfo Info(long summonerId) =>
        new()
        {
            Player = new PlayerInfo { SummonerId = summonerId, Puuid = "puuid-" + summonerId },
        };

    // ---- the history cache ----

    [Fact]
    public void AddOrUpdateCache_AKnownSummoner_IsStored()
    {
        PlayerCardCache cache = new();
        PlayerMatchInfo info = Info(7);

        cache.AddOrUpdateCache(7, info);

        Assert.Same(info, Assert.Single(cache.GetAllCachedPlayerInfos()).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddOrUpdateCache_AnIdThatCannotBeAPlayer_IsIgnored(long summonerId)
    {
        // Zero and negative are what a hidden or not-yet-loaded player looks like, so there is nobody to file this
        // history under.
        PlayerCardCache cache = new();

        cache.AddOrUpdateCache(summonerId, Info(summonerId));

        Assert.Empty(cache.GetAllCachedPlayerInfos());
    }

    [Fact]
    public void AddOrUpdateCache_TheSameSummonerTwice_KeepsTheSecond()
    {
        PlayerCardCache cache = new();
        PlayerMatchInfo first = Info(7);
        PlayerMatchInfo second = Info(7);

        cache.AddOrUpdateCache(7, first);
        cache.AddOrUpdateCache(7, second);

        Assert.Same(second, Assert.Single(cache.GetAllCachedPlayerInfos()).Value);
    }

    [Fact]
    public void GetAllCachedPlayerInfos_HandsBackACopy()
    {
        // A caller editing what it read must not be editing the cache.
        PlayerCardCache cache = new();
        cache.AddOrUpdateCache(7, Info(7));

        cache.GetAllCachedPlayerInfos().Clear();

        Assert.Single(cache.GetAllCachedPlayerInfos());
    }

    [Fact]
    public void Replace_DropsWhatWasThereAndKeysByThePlayersOwnId()
    {
        // The id comes from each player rather than from the caller, so the result is filed correctly even when the
        // cache was previously holding someone else.
        PlayerCardCache cache = new();
        cache.AddOrUpdateCache(1, Info(1));

        cache.Replace([Info(8), Info(9)]);

        Assert.Equal([8L, 9L], cache.GetAllCachedPlayerInfos().Keys.Order());
    }

    [Fact]
    public void Replace_Nothing_LeavesTheCacheEmpty()
    {
        PlayerCardCache cache = new();
        cache.AddOrUpdateCache(1, Info(1));

        cache.Replace([]);

        Assert.Empty(cache.GetAllCachedPlayerInfos());
    }

    // ---- the current champion ----

    [Fact]
    public void GetCurrentChampion_ASummonerThatWasNeverRecorded_ReadsAsZero()
    {
        Assert.Equal(0, new PlayerCardCache().GetCurrentChampion(7));
    }

    [Fact]
    public void UpdateCurrentChampion_AKnownSummoner_IsRemembered()
    {
        PlayerCardCache cache = new();

        cache.UpdateCurrentChampion(7, 22, 3);

        Assert.Equal(22, cache.GetCurrentChampion(7));
    }

    [Fact]
    public void UpdateCurrentChampion_ASummonerWithNoIdButAKnownChampion_IsStillRemembered()
    {
        // A hidden player has no summoner id to speak of, but the champion is known and the card still needs it -
        // this is the one member that does not insist on a positive id.
        PlayerCardCache cache = new();

        cache.UpdateCurrentChampion(0, 22, 3);

        Assert.Equal(22, cache.GetCurrentChampion(0));
    }

    [Fact]
    public void UpdateCurrentChampion_NeitherAnIdNorAChampion_IsIgnored()
    {
        PlayerCardCache cache = new();

        cache.UpdateCurrentChampion(0, 0, 3);

        Assert.Equal(0, cache.GetCurrentChampion(0));
    }

    [Fact]
    public void UpdateCurrentChampion_ReplacesTheChampionForThatSummoner()
    {
        PlayerCardCache cache = new();
        cache.UpdateCurrentChampion(7, 22, 3);

        cache.UpdateCurrentChampion(7, 33, 3);

        Assert.Equal(33, cache.GetCurrentChampion(7));
    }

    // ---- the card controls ----

    [Fact]
    [Trait("Category", "UI")]
    public void TryGetCard_NothingRegistered_IsFalse()
    {
        PlayerCardCache cache = new();

        Assert.False(cache.TryGetCard(7, out _));
    }

    [Fact]
    [Trait("Category", "UI")]
    public void RegisterCard_ThenTryGetCard_HandsBackTheSameControl()
    {
        PlayerCardCache cache = new();
        using PlayerCardControl card = new();

        cache.RegisterCard(7, card);

        Assert.True(cache.TryGetCard(7, out PlayerCardControl? found));
        Assert.Same(card, found);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("Category", "UI")]
    public void RegisterCard_AnIdThatCannotBeAPlayer_IsIgnored(long summonerId)
    {
        PlayerCardCache cache = new();
        using PlayerCardControl card = new();

        cache.RegisterCard(summonerId, card);

        Assert.False(cache.TryGetCard(summonerId, out _));
    }

    // ---- clearing ----

    [Fact]
    [Trait("Category", "UI")]
    public void ClearAll_EmptiesEverything()
    {
        PlayerCardCache cache = new();
        using PlayerCardControl card = new();
        cache.AddOrUpdateCache(7, Info(7));
        cache.UpdateCurrentChampion(7, 22, 3);
        cache.RegisterCard(7, card);

        cache.ClearAll();

        Assert.Empty(cache.GetAllCachedPlayerInfos());
        Assert.Equal(0, cache.GetCurrentChampion(7));
        Assert.False(cache.TryGetCard(7, out _));
    }

    [Fact]
    [Trait("Category", "UI")]
    public void ClearGameState_EmptiesTheSameThreeThings()
    {
        // The two clears differ only in a field nothing ever writes, so from the outside they do the same.
        PlayerCardCache cache = new();
        using PlayerCardControl card = new();
        cache.AddOrUpdateCache(7, Info(7));
        cache.UpdateCurrentChampion(7, 22, 3);
        cache.RegisterCard(7, card);

        cache.ClearGameState();

        Assert.Empty(cache.GetAllCachedPlayerInfos());
        Assert.Equal(0, cache.GetCurrentChampion(7));
        Assert.False(cache.TryGetCard(7, out _));
    }

    [Fact]
    public void ClearAll_OnAnEmptyCache_IsSafe()
    {
        PlayerCardCache cache = new();

        cache.ClearAll();
        cache.ClearGameState();

        Assert.Empty(cache.GetAllCachedPlayerInfos());
        Assert.Equal(0, cache.GetCurrentChampion(7));
    }
}
