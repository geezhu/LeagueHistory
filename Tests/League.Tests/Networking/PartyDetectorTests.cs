using System.Drawing;
using League.Models;
using League.Networking;

namespace League.Tests.Networking;

/// <summary>
/// Detect writes the result onto the objects it was handed: the players' own PartyId, the shared sorted GroupKey and
/// the colour on each PlayerInfo, or nulls and Color.Black when the player stays alone. It returns void and keeps no
/// public state, so every assertion below reads the players back. The expected values come from the PremadeGroups
/// check in Tests/StatisticsChecks/UtilityLogicChecks.cs, which this class replaces.
/// </summary>
public class PartyDetectorTests
{
    [Fact]
    public void DefaultMatchThreshold_TwoSharedGames()
    {
        int threshold = PartyDetector.DefaultMatchThreshold;

        Assert.Equal(2, threshold);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    public void Detect_SharedMatchesAtOrAboveTheThreshold_GroupsBothPlayers(int sharedGames, int threshold)
    {
        var first = PlayersWith("甲", sharedGames, 3);
        var second = PlayersWith("乙", sharedGames, 0);
        Assert.Equal(sharedGames, first.Matches.Intersect(second.Matches).Count());

        new PartyDetector(threshold).Detect([first, second]);

        string expected = sharedGames >= threshold ? "甲_乙" : null;
        Assert.Equal(expected, first.PartyId);
        Assert.Equal(expected, second.PartyId);
        Assert.Equal(expected, first.Player.GroupKey);
        Assert.Equal(expected, second.Player.GroupKey);
        Assert.Equal(first.Player.NameColor, second.Player.NameColor);
    }

    [Fact]
    public void Detect_TwoSharedMatchesAtTheDefaultThreshold_GroupsBothPlayers()
    {
        var first = PlayersWith("甲", 2, 3);
        var second = PlayersWith("乙", 2, 0);

        new PartyDetector().Detect([first, second]);

        Assert.Equal("甲_乙", first.PartyId);
        Assert.Equal("甲_乙", second.PartyId);
        Assert.Equal("甲_乙", first.Player.GroupKey);
        Assert.Equal("甲_乙", second.Player.GroupKey);
        Assert.Equal(first.Player.NameColor, second.Player.NameColor);
        Assert.NotEqual(Color.Black, first.Player.NameColor);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void Detect_OneSharedMatchBelowTheThreshold_LeavesBothAlone(int sharedGames, int threshold)
    {
        var first = PlayersWith("甲", sharedGames, 2);
        var second = PlayersWith("乙", sharedGames, 0);
        Assert.Equal(sharedGames, first.Matches.Intersect(second.Matches).Count());

        new PartyDetector(threshold).Detect([first, second]);

        Assert.Null(first.PartyId);
        Assert.Null(second.PartyId);
        Assert.Null(first.Player.GroupKey);
        Assert.Null(second.Player.GroupKey);
        Assert.Equal(Color.Black, first.Player.NameColor);
        Assert.Equal(Color.Black, second.Player.NameColor);
    }

    [Fact]
    public void Detect_EmptyRoster_LeavesTheDetectorUsableForALaterScan()
    {
        var first = Player("甲", "m1", "m2");
        var second = Player("乙", "m1", "m2");
        var detector = new PartyDetector();

        detector.Detect([]);
        detector.Detect([first, second]);

        Assert.Equal("甲_乙", first.PartyId);
        Assert.Equal("甲_乙", second.PartyId);
    }

    [Fact]
    public void Detect_NoPlayers_Returns()
    {
        new PartyDetector().Detect([]);
    }

    [Fact]
    public void Detect_SinglePlayer_LeavesThemUnmarked()
    {
        var only = Player("甲", "m1", "m2", "m3");

        new PartyDetector().Detect([only]);

        Assert.Null(only.PartyId);
        Assert.Null(only.Player.GroupKey);
        Assert.Equal(Color.Black, only.Player.NameColor);
    }

    [Fact]
    public void Detect_PlayersWithNoSharedHistory_LeavesBothUnmarked()
    {
        var first = Player("甲", "m1", "m2");
        var alone = Player("丁", "z1");

        new PartyDetector().Detect([first, alone]);

        Assert.Null(first.PartyId);
        Assert.Null(alone.PartyId);
        Assert.Null(first.Player.GroupKey);
        Assert.Null(alone.Player.GroupKey);
        Assert.Equal(Color.Black, first.Player.NameColor);
        Assert.Equal(Color.Black, alone.Player.NameColor);
    }

    [Fact]
    public void Detect_PremadeAndLoner_NamesTheGroupByEveryMember()
    {
        var first = Player("甲", "m1", "m2", "m3");
        var second = Player("乙", "m1", "m2");
        var third = Player("丙", "m1");

        new PartyDetector().Detect([first, second, third]);

        Assert.Equal("甲_乙", first.Player.GroupKey);
        Assert.Equal("甲_乙", second.Player.GroupKey);
        Assert.Null(third.Player.GroupKey);
        Assert.Equal(first.Player.NameColor, second.Player.NameColor);
        Assert.Equal(Color.Black, third.Player.NameColor);
    }

    [Fact]
    public void Detect_OneSharedGame_IsBelowTheDefaultThreshold()
    {
        var first = Player("甲", "m1", "m2", "m3");
        var third = Player("丙", "m1");

        new PartyDetector().Detect([first, third]);

        Assert.Null(first.PartyId);
        Assert.Null(third.PartyId);
        Assert.Null(first.Player.GroupKey);
        Assert.Equal(Color.Black, first.Player.NameColor);
        Assert.Equal(Color.Black, third.Player.NameColor);
    }

    [Fact]
    public void Detect_ThresholdOfOne_GroupsPlayersWhoSharedASingleGame()
    {
        var first = Player("甲", "m1", "m2", "m3");
        var third = Player("丙", "m1");

        new PartyDetector(1).Detect([first, third]);

        Assert.Equal("丙_甲", first.PartyId);
        Assert.Equal("丙_甲", third.PartyId);
        Assert.Equal("丙_甲", first.Player.GroupKey);
        Assert.Equal("丙_甲", third.Player.GroupKey);
        Assert.Equal(first.Player.NameColor, third.Player.NameColor);
        Assert.NotEqual(Color.Black, first.Player.NameColor);
    }

    [Fact]
    public void Detect_SecondScanNoLongerGroupsAPlayer_ClearsTheMarkItLeft()
    {
        var first = Player("甲", "m1", "m2");
        var second = Player("乙", "m1", "m2");
        var alone = Player("丁", "z1");
        var detector = new PartyDetector();

        detector.Detect([first, second, alone]);
        detector.Detect([first, alone]);

        Assert.Null(first.PartyId);
        Assert.Null(first.Player.GroupKey);
        Assert.Equal(Color.Black, first.Player.NameColor);
    }

    [Fact]
    public void Detect_PlayerWithoutAPuuid_IsLeftOutOfTheGroup()
    {
        var first = Player("甲", "m1", "m2", "m3");
        var anonymous = Player("无名", "m1", "m2", "m3");
        anonymous.Player.Puuid = "";

        new PartyDetector().Detect([first, anonymous]);

        Assert.Null(first.PartyId);
        Assert.Null(anonymous.PartyId);
        Assert.Null(anonymous.Player.GroupKey);
        Assert.Equal(Color.Black, anonymous.Player.NameColor);
    }

    [Fact]
    public void Detect_TwoPremades_TellsThemApartByColour()
    {
        var first = Player("甲", "m1", "m2");
        var second = Player("乙", "m1", "m2");
        var otherPair = Player("戊", "y1", "y2");
        var otherMate = Player("己", "y1", "y2");

        new PartyDetector().Detect([first, second, otherPair, otherMate]);

        Assert.NotEqual(Color.Black, first.Player.NameColor);
        Assert.Equal(first.Player.NameColor, second.Player.NameColor);
        Assert.NotEqual(first.Player.NameColor, otherPair.Player.NameColor);
        Assert.Equal(otherPair.Player.NameColor, otherMate.Player.NameColor);
    }

    // Detect counts a.Matches.Intersect(b.Matches), so the two players built from the same sharedGames value share
    // exactly that many keys and no more: the "s" keys are common, the rest belong to one player only. The first line
    // of every threshold theory pins that overlap, because a helper that reused the same match list for both players
    // would make every one of them pass for the wrong reason.
    private static PlayerMatchInfo PlayersWith(string gameName, int sharedGames, int ownGames)
    {
        var player = new PlayerMatchInfo
        {
            Player = new PlayerInfo { GameName = gameName, Puuid = $"puuid-{gameName}" },
        };

        for (int i = 0; i < sharedGames; i++)
        {
            player.MatchKeys.Add($"s{i}");
        }

        for (int i = 0; i < ownGames; i++)
        {
            player.MatchKeys.Add($"{gameName}-p{i}");
        }

        return player;
    }

    private static PlayerMatchInfo Player(string gameName, params string[] matchKeys)
    {
        var player = new PlayerMatchInfo
        {
            Player = new PlayerInfo { GameName = gameName, Puuid = $"puuid-{gameName}" },
        };

        player.MatchKeys.AddRange(matchKeys);
        return player;
    }
}
