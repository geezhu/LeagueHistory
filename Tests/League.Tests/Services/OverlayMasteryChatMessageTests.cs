using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The mastery message the Game Bar sends for one side: each player's name and, where it can be had, their mastery.
///
/// Two rules are this type's own. A mastery that belongs to a different champion than the seat currently holds is
/// dropped rather than printed, because the seat may have been reused since the mastery was fetched - showing the old
/// number would attribute one player's progress to another. And an empty side is refused before anything is built,
/// because the reason is different from "no mastery data yet" and the message says so.
/// </summary>
public class OverlayMasteryChatMessageTests
{
    private static LoadingPlayer Player(int team, int championId, bool self = false, string? key = null) =>
        new(
            Key: key ?? $"p{team}-{championId}",
            Name: $"名字{championId}",
            Puuid: $"puuid-{team}-{championId}",
            SummonerId: championId,
            ChampionId: championId,
            Team: team,
            Self: self
        );

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game", 420, "CLASSIC", players);

    private static OverlayChatRequest Request(string target, int slot = -1, int championId = 0) =>
        new(
            Id: "i",
            Session: "s",
            Target: target,
            Slot: slot,
            ChampionId: championId,
            CreatedAt: 1,
            Dismissed: false,
            Cancelled: false
        );

    private static PlayerChampionMastery Mastery(int championId, int level = 5, long points = 12000) =>
        new(championId, level, points, null, Available: true);

    private static readonly Func<int, string> Champion = id => $"英雄{id}";

    // ---- a side that cannot be worked out ----

    [Fact]
    public void Build_ASideThatCannotBeWorkedOut_IsRefused()
    {
        // The reason is not "no mastery yet": the side itself is unknown, and the message says which.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayMasteryChatMessage.Build(
                Roster(),
                Request("single", slot: 99),
                new Dictionary<string, PlayerChampionMastery>(),
                Champion
            )
        );

        Assert.Equal("未发送：无法确定所选阵营或英雄，请等待名单更新。", error.Message);
    }

    // ---- which side ----

    [Fact]
    public void Build_TheOwnSide_NamesItAsSuch()
    {
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(100, 12));
        Dictionary<string, PlayerChampionMastery> masteries = new() { ["p100-11"] = Mastery(11) };

        IReadOnlyList<string> lines = OverlayMasteryChatMessage.Build(roster, Request("own"), masteries, Champion);

        Assert.Contains("我方", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TheEnemySide_NamesItAsSuch()
    {
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(200, 21));
        Dictionary<string, PlayerChampionMastery> masteries = new() { ["p200-21"] = Mastery(21) };

        IReadOnlyList<string> lines = OverlayMasteryChatMessage.Build(roster, Request("enemy"), masteries, Champion);

        Assert.Contains("对方", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AnotherTarget_IsJustCalledTheChampion()
    {
        // "single" resolves to one seat, which is neither side as a whole.
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(200, 21));
        Dictionary<string, PlayerChampionMastery> masteries = new()
        {
            ["p100-11"] = Mastery(11),
            ["p200-21"] = Mastery(21),
        };

        IReadOnlyList<string> lines = OverlayMasteryChatMessage.Build(
            roster,
            Request("single", slot: 0, championId: 11),
            masteries,
            Champion
        );

        Assert.Contains("英雄", Assert.Single(lines), StringComparison.Ordinal);
    }

    // ---- which players ----

    [Fact]
    public void Build_EveryPlayerOfTheSideWithAMastery_IsNamed()
    {
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(100, 12));
        Dictionary<string, PlayerChampionMastery> masteries = new()
        {
            ["p100-11"] = Mastery(11),
            ["p100-12"] = Mastery(12),
        };

        IReadOnlyList<string> lines = OverlayMasteryChatMessage.Build(roster, Request("own"), masteries, Champion);

        string text = string.Join("\n", lines);
        Assert.Contains("英雄11", text, StringComparison.Ordinal);
        Assert.Contains("英雄12", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TheOtherSidesPlayers_AreNotNamed()
    {
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(200, 21));
        Dictionary<string, PlayerChampionMastery> masteries = new()
        {
            ["p100-11"] = Mastery(11),
            ["p200-21"] = Mastery(21),
        };

        string text = string.Join("\n", OverlayMasteryChatMessage.Build(roster, Request("own"), masteries, Champion));

        Assert.DoesNotContain("英雄21", text, StringComparison.Ordinal);
    }

    // ---- the mastery that does not belong to the seat ----

    [Fact]
    public void Build_AMasteryForAnotherChampion_IsDropped()
    {
        // The seat may have been reused since the mastery was fetched; printing the old number would credit one
        // player with another's progress.
        LoadingRoster roster = Roster(Player(100, 11, self: true));
        Dictionary<string, PlayerChampionMastery> stale = new() { ["p100-11"] = Mastery(99) };

        // With the only mastery dropped there is nothing to send: a message where every seat is unavailable is
        // refused rather than printed.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayMasteryChatMessage.Build(roster, Request("own"), stale, Champion)
        );

        Assert.Contains("尚未就绪", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OneStaleMasteryAmongGoodOnes_ListsThatSeatWithoutALevel()
    {
        // The stale one is dropped, but the seat is still one of the side - so it is listed as unavailable rather
        // than vanishing from a message that is meant to name everybody.
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(100, 12));
        Dictionary<string, PlayerChampionMastery> masteries = new()
        {
            ["p100-11"] = Mastery(99),
            ["p100-12"] = Mastery(12),
        };

        string text = string.Join("\n", OverlayMasteryChatMessage.Build(roster, Request("own"), masteries, Champion));

        Assert.Contains("英雄11 暂不可用", text, StringComparison.Ordinal);
        Assert.Contains("英雄12 5级", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NoMasteryAtAll_IsRefusedByTheMessageItself()
    {
        // The side is known but there is nothing to say about it: a message where every seat is unavailable is
        // refused rather than printed.
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(100, 12));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayMasteryChatMessage.Build(
                roster,
                Request("own"),
                new Dictionary<string, PlayerChampionMastery>(),
                Champion
            )
        );

        Assert.Contains("尚未就绪", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AnUnavailableMastery_CountsAsNoMastery()
    {
        // "Unavailable" is not a level of zero; it leaves nothing to send.
        LoadingRoster roster = Roster(Player(100, 11, self: true));
        Dictionary<string, PlayerChampionMastery> masteries = new()
        {
            ["p100-11"] = PlayerChampionMastery.Unavailable(11),
        };

        Assert.Throws<InvalidOperationException>(() =>
            OverlayMasteryChatMessage.Build(roster, Request("own"), masteries, Champion)
        );
    }

    [Fact]
    public void Build_TheChampionNameComesFromTheFunction_NotTheSeat()
    {
        // The caller owns how a champion is written; the seat only carries its id.
        LoadingRoster roster = Roster(Player(100, 11, self: true));
        Dictionary<string, PlayerChampionMastery> masteries = new() { ["p100-11"] = Mastery(11) };

        IReadOnlyList<string> lines = OverlayMasteryChatMessage.Build(
            roster,
            Request("own"),
            masteries,
            _ => "自定义名字"
        );

        Assert.Contains("自定义名字", Assert.Single(lines), StringComparison.Ordinal);
    }
}
