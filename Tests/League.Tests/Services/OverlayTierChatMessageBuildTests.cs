using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The tier message for one side: one chat line per statistics version, naming each champion with its lane, its tier
/// and its rates.
///
/// Two of its rules are about refusing. An empty selection has no side to describe, and a side whose statistics have
/// not arrived has nothing to say - and those are different problems with different wording, so they are told apart.
/// The third rule is the interesting one: players are grouped by the version their statistics came from, and each
/// version gets its own line, because rates from two different patches must never appear side by side as if comparable.
/// </summary>
public class OverlayTierChatMessageBuildTests
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

    private static OverlayChatRequest Request(string target = "own") =>
        new(
            Id: "i",
            Session: "s",
            Target: target,
            Slot: -1,
            ChampionId: 0,
            CreatedAt: 1,
            Dismissed: false,
            Cancelled: false
        );

    private static PlayerChampionTier Rank(
        string tier = "S+",
        string position = "none",
        double? winRate = 0.5234,
        double? pickRate = 0.0876,
        string version = "15.1",
        bool reference = false
    ) =>
        new(tier, "52.3%", "", 420, position)
        {
            WinRateValue = winRate,
            PickRateValue = pickRate,
            Version = version,
            ReferencePosition = reference,
        };

    private static string Name(int id) => "英雄" + id;

    private static Dictionary<string, PlayerChampionTier> Ranks(
        params (string Key, PlayerChampionTier Rank)[] entries
    ) => entries.ToDictionary(e => e.Key, e => e.Rank, StringComparer.Ordinal);

    // ---- the two refusals ----

    [Fact]
    public void Build_ASideThatCannotBeWorkedOut_IsRefused()
    {
        // No self seat, so there is no side to describe.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayTierChatMessage.Build(Roster(Player(100, 1)), Request(), Ranks(), Name)
        );

        Assert.Equal("未发送：无法确定所选阵营或英雄，请等待名单更新。", error.Message);
    }

    [Fact]
    public void Build_ASideWhoseStatisticsHaveNotArrived_IsRefusedWithItsOwnReason()
    {
        // The side is known but no player has a win rate yet; the user is told to wait, not that the side is unknown.
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayTierChatMessage.Build(roster, Request(), Ranks(("p100-1", Rank(winRate: null))), Name)
        );

        Assert.Equal("未发送：英雄统计尚未就绪或当前模式暂无统计，请稍后重试。", error.Message);
    }

    [Fact]
    public void Build_OnePlayerWithRates_IsEnoughToSend()
    {
        // The refusal is about the whole side having nothing; one usable row is enough.
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(100, 2));
        var ranks = Ranks(("p100-1", Rank()), ("p100-2", Rank(winRate: null)));

        IReadOnlyList<string> lines = OverlayTierChatMessage.Build(roster, Request(), ranks, Name);

        Assert.NotEmpty(lines);
    }

    // ---- the line ----

    [Fact]
    public void Build_TheLineNamesTheSideTheChampionsAndTheirTier()
    {
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        string line = Assert.Single(
            OverlayTierChatMessage.Build(roster, Request("own"), Ranks(("p100-1", Rank())), Name)
        );

        Assert.Contains("我方", line, StringComparison.Ordinal);
        Assert.Contains("英雄1", line, StringComparison.Ordinal);
        Assert.Contains("S+", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("top", "上路")]
    [InlineData("jungle", "打野")]
    [InlineData("mid", "中路")]
    [InlineData("adc", "下路")]
    [InlineData("support", "辅助")]
    public void Build_ALane_IsWrittenInChineseAfterTheChampion(string position, string expected)
    {
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        Assert.Contains(
            "（" + expected + "）",
            Assert.Single(
                OverlayTierChatMessage.Build(roster, Request(), Ranks(("p100-1", Rank(position: position))), Name)
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_AReferenceLane_IsMarkedAsSuch()
    {
        // The rates came from a neighbouring lane, so the name says so rather than passing them off as this lane's.
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        Assert.Contains(
            "（参考上路）",
            Assert.Single(
                OverlayTierChatMessage.Build(
                    roster,
                    Request(),
                    Ranks(("p100-1", Rank(position: "top", reference: true))),
                    Name
                )
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_NoLane_AddsNoParenthesis()
    {
        // "none" is the model's word for "no lane"; it is the one value that suppresses the suffix.
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        Assert.DoesNotContain(
            "（",
            Assert.Single(
                OverlayTierChatMessage.Build(roster, Request(), Ranks(("p100-1", Rank(position: "none"))), Name)
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_AnEmptyLane_WritesEmptyParentheses()
    {
        // The guard only knows the word "none", so an empty lane produces a pair of empty brackets. That is what the
        // code does today and is pinned rather than fixed here: the caller is expected to say "none".
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        Assert.Contains(
            "（）",
            Assert.Single(OverlayTierChatMessage.Build(roster, Request(), Ranks(("p100-1", Rank(position: ""))), Name)),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_AMissingTier_EndsUpAsADash()
    {
        // The overlay's own normalisation turns the dash into "暂无梯队", and the renderer turns "暂无梯队" back into
        // the dash - so a champion without a tier is drawn as a dash either way, and that net result is what is pinned.
        LoadingRoster roster = Roster(Player(100, 1, self: true));

        Assert.Contains(
            "—",
            Assert.Single(OverlayTierChatMessage.Build(roster, Request(), Ranks(("p100-1", Rank(tier: "—"))), Name)),
            StringComparison.Ordinal
        );
    }

    // ---- one line per statistics version ----

    [Fact]
    public void Build_TwoVersions_GetOneLineEach()
    {
        // Rates from two patches are not comparable, so they never share a line.
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(100, 2));
        var ranks = Ranks(("p100-1", Rank(version: "15.1")), ("p100-2", Rank(version: "15.2")));

        IReadOnlyList<string> lines = OverlayTierChatMessage.Build(roster, Request(), ranks, Name);

        Assert.Equal(2, lines.Count);
        Assert.Contains("15.1", lines[0], StringComparison.Ordinal);
        Assert.Contains("15.2", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OneVersion_IsOneLine()
    {
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(100, 2));
        var ranks = Ranks(("p100-1", Rank(version: "15.1")), ("p100-2", Rank(version: "15.1")));

        Assert.Single(OverlayTierChatMessage.Build(roster, Request(), ranks, Name));
    }

    // ---- where the numbers came from ----

    [Fact]
    public void Build_OnTheHowlingAbyss_NamesArammetaAsTheSource()
    {
        // Mayhem's rates are arammeta's, and the line has to say which site they came from.
        LoadingRoster mayhem = new("game", 2400, "KIWI", [Player(100, 1, self: true)]);

        Assert.Contains(
            "arammeta",
            Assert.Single(OverlayTierChatMessage.Build(mayhem, Request(), Ranks(("p100-1", Rank())), Name)),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_OnTheRift_NamesOpggAsTheSource()
    {
        LoadingRoster rift = Roster(Player(100, 1, self: true));

        Assert.Contains(
            "OP.GG",
            Assert.Single(OverlayTierChatMessage.Build(rift, Request(), Ranks(("p100-1", Rank())), Name)),
            StringComparison.Ordinal
        );
    }
}
