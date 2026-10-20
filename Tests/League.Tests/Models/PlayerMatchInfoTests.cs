using League.Models;

namespace League.Tests.Models;

/// <summary>
/// A player's row copied for a different champion: the same player, the same games, the same icons - only which
/// champion it is claimed to be for changes.
///
/// That shape is the point. The copy is shallow everywhere except the player record, so the match list, the keys and
/// the win history stay shared with the original. Copying them would double the memory for a row that differs in one
/// integer, and would also break the caller's assumption that filling the original's list fills the copy's.
/// </summary>
public class PlayerMatchInfoTests : IDisposable
{
    private readonly List<PlayerMatchInfo> _made = [];

    public void Dispose()
    {
        foreach (PlayerMatchInfo info in _made)
        {
            info.HeroIcons?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private PlayerMatchInfo Info(int championId = 11)
    {
        PlayerMatchInfo info = new()
        {
            Player = new PlayerInfo
            {
                ChampionId = championId,
                GameName = "名字",
                Puuid = "puuid",
                SummonerId = 7,
                SoloRank = "黄金 I",
            },
            MatchKeys = ["a", "b"],
            WinHistory = [true, false],
            RecentMatches =
            [
                new MatchStat
                {
                    Kills = 1,
                    Deaths = 2,
                    Assists = 3,
                },
            ],
            PartyId = "party",
            IsFromCache = true,
        };
        _made.Add(info);
        return info;
    }

    // ---- what changes ----

    [Fact]
    public void CopyForChampion_APlayerForAnotherChampion_HasThatChampion()
    {
        PlayerMatchInfo copy = Info(championId: 11).CopyForChampion(22);
        _made.Add(copy);

        Assert.Equal(22, copy.Player.ChampionId);
    }

    [Fact]
    public void CopyForChampion_TheOriginalKeepsItsChampion()
    {
        // The whole reason for copying: the row already in the list must not change.
        PlayerMatchInfo original = Info(championId: 11);

        original.CopyForChampion(22);

        Assert.Equal(11, original.Player.ChampionId);
    }

    [Fact]
    public void CopyForChampion_TheCopyIsAnotherInstance()
    {
        PlayerMatchInfo original = Info();
        PlayerMatchInfo copy = original.CopyForChampion(22);
        _made.Add(copy);

        Assert.NotSame(original, copy);
        Assert.NotSame(original.Player, copy.Player);
    }

    [Fact]
    public void CopyForChampion_OnlyTheChampionDiffers()
    {
        // Everything the panel shows about the person travels with the copy.
        PlayerMatchInfo copy = Info().CopyForChampion(22);
        _made.Add(copy);

        Assert.Equal("名字", copy.Player.GameName);
        Assert.Equal("puuid", copy.Player.Puuid);
        Assert.Equal(7, copy.Player.SummonerId);
        Assert.Equal("黄金 I", copy.Player.SoloRank);
    }

    // ---- what is shared ----

    [Fact]
    public void CopyForChampion_TheMatchKeys_AreTheSameList()
    {
        // Copying them would double the memory for a row that differs in one integer.
        PlayerMatchInfo original = Info();
        PlayerMatchInfo copy = original.CopyForChampion(22);
        _made.Add(copy);

        Assert.Same(original.MatchKeys, copy.MatchKeys);
    }

    [Fact]
    public void CopyForChampion_TheWinHistory_IsTheSameList()
    {
        PlayerMatchInfo original = Info();
        PlayerMatchInfo copy = original.CopyForChampion(22);
        _made.Add(copy);

        Assert.Same(original.WinHistory, copy.WinHistory);
    }

    [Fact]
    public void CopyForChampion_TheMatchItems_AreTheSameList()
    {
        PlayerMatchInfo original = Info();
        PlayerMatchInfo copy = original.CopyForChampion(22);
        _made.Add(copy);

        Assert.Same(original.MatchItems, copy.MatchItems);
    }

    [Fact]
    public void CopyForChampion_TheIcons_AreTheSameImageList()
    {
        // One image list per row would hold duplicate bitmaps for every champion a player was copied for.
        PlayerMatchInfo original = Info();
        PlayerMatchInfo copy = original.CopyForChampion(22);
        _made.Add(copy);

        Assert.Same(original.HeroIcons, copy.HeroIcons);
    }

    [Fact]
    public void CopyForChampion_FillingTheOriginal_FillsTheCopy()
    {
        // The consequence of the sharing: a caller that adds a key to the original must not be surprised that the
        // copy has it too.
        PlayerMatchInfo original = Info();
        PlayerMatchInfo copy = original.CopyForChampion(22);
        _made.Add(copy);

        original.MatchKeys.Add("c");

        Assert.Contains("c", copy.MatchKeys);
    }

    [Fact]
    public void CopyForChampion_TheOtherFlags_AreCarriedOver()
    {
        PlayerMatchInfo copy = Info().CopyForChampion(22);
        _made.Add(copy);

        Assert.Equal("party", copy.PartyId);
        Assert.True(copy.IsFromCache);
        Assert.Single(copy.RecentMatches);
    }

    // ---- the rest of the bag ----

    [Fact]
    public void Matches_IsTheSameKeysAsMatchKeys()
    {
        // The read-only view is the storage itself, not a snapshot of it.
        PlayerMatchInfo info = Info();

        Assert.Equal(["a", "b"], info.Matches);
        Assert.Same(info.MatchKeys, info.Matches as List<string>);
    }

    [Fact]
    public void ANewPlayerMatchInfo_StartsWithEmptyCollections()
    {
        // A row is filled in as the games arrive, so it has to be usable before any do.
        PlayerMatchInfo info = new();

        Assert.Empty(info.MatchKeys);
        Assert.Empty(info.WinHistory);
        Assert.Empty(info.RecentMatches);
        Assert.Empty(info.MatchItems);
        Assert.NotNull(info.HeroIcons);
        Assert.NotNull(info.Behavior);
        Assert.False(info.IsFromCache);
    }
}
