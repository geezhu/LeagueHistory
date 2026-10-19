using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The "连死/本局最高" line: how many times each selected seat has died in a row and its worst run this game.
///
/// A seat counts as known only when both numbers are there and the highest is not below the current one, so a record
/// with a missing highest - or one claiming a highest smaller than the current run - reads as 未知 rather than
/// printing a contradiction. A seat whose identity is hidden never gets a record at all, so it reads 未知 too.
/// </summary>
public class OverlayDeathStreakChatMessageTests
{
    private static LoadingPlayer Seat(
        string key,
        int championId,
        int team = 100,
        bool self = false,
        bool identityHidden = false
    ) =>
        new(
            Key: key,
            Name: key,
            Puuid: $"puuid-{key}",
            SummonerId: 0,
            ChampionId: championId,
            Team: team,
            Self: self,
            IdentityHidden: identityHidden
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

    private static LiveDeathStreak Streak(int? current, int? highest, bool complete = true) =>
        new(current, highest, complete, "记录");

    private static string Champion(int championId) => $"英雄{championId}";

    private const string Prefix = "【召唤师峡谷·我方】连死/本局最高：";

    // ---- the two refusals ----

    [Fact]
    public void Build_NoSeatIsSelected_IsRefused()
    {
        // An empty selection comes from the shared chooser, so this is the same refusal the tier message gives.
        LoadingRoster roster = Roster(Seat("a", 11));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayDeathStreakChatMessage.Build(roster, Request(), new Dictionary<string, LiveDeathStreak>(), Champion)
        );

        Assert.Equal("未发送：无法确定所选阵营或英雄，请等待名单更新。", error.Message);
    }

    [Fact]
    public void Build_NoSeatHasAUsableRecord_IsRefused()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OverlayDeathStreakChatMessage.Build(roster, Request(), new Dictionary<string, LiveDeathStreak>(), Champion)
        );

        Assert.Equal("未发送：本局连死记录尚未就绪或无法匹配玩家，请稍后重试。", error.Message);
    }

    [Fact]
    public void Build_OneUsableRecordAmongUnknownOnes_IsEnoughToSend()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true), Seat("b", 12));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["b"] = Streak(1, 2) },
            Champion
        );

        Assert.Single(lines);
        Assert.Contains("英雄12 1/2", lines[0], StringComparison.Ordinal);
    }

    // ---- what makes a record usable ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 5)]
    [InlineData(5, 5)]
    public void Build_ACurrentRunAtOrBelowTheHighest_IsUsable(int current, int highest)
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(current, highest) },
            Champion
        );

        Assert.Contains($"英雄11 {current}/{highest}", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(3, null)]
    [InlineData(null, null)]
    [InlineData(-1, 5)]
    [InlineData(6, 5)]
    public void Build_AContradictoryOrMissingRecord_ReadsAsUnknown(int? current, int? highest)
    {
        // A highest below the current run is a contradiction, and a missing number is not a run either; both read
        // as 未知 rather than printing something that cannot be true.
        LoadingRoster roster = Roster(Seat("a", 11, self: true), Seat("b", 12));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(current, highest), ["b"] = Streak(1, 2) },
            Champion
        );

        Assert.Contains("英雄11 未知", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ASeatWithNoRecordAtAll_ReadsAsUnknown()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true), Seat("b", 12));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["b"] = Streak(1, 2) },
            Champion
        );

        Assert.Contains("英雄11 未知", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ASeatWhoseIdentityIsHidden_ReadsAsUnknownEvenWithARecord()
    {
        // The hidden seat is still listed - it is a seat on the side - but it is never matched to a record.
        LoadingRoster roster = Roster(Seat("a", 11, self: true), Seat("b", 12, identityHidden: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2), ["b"] = Streak(3, 4) },
            Champion
        );

        Assert.Contains("英雄12 未知", Assert.Single(lines), StringComparison.Ordinal);
    }

    // ---- the item text ----

    [Fact]
    public void Build_ARunStillBeingRecorded_IsMarkedAsSuch()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(3, 4, complete: false) },
            Champion
        );

        Assert.Contains("英雄11 3/4（已记录）", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AFinishedRun_CarriesNoMarker()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(3, 4, complete: true) },
            Champion
        );

        Assert.Contains("英雄11 3/4", Assert.Single(lines), StringComparison.Ordinal);
        Assert.DoesNotContain("已记录", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SeveralSeats_AreJoinedByTheEnumeratingComma()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true), Seat("b", 12));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2), ["b"] = Streak(3, 4) },
            Champion
        );

        Assert.Equal(Prefix + "英雄11 1/2、英雄12 3/4", Assert.Single(lines));
    }

    // ---- the header ----

    [Theory]
    [InlineData("own", "我方")]
    [InlineData("enemy", "对方")]
    public void Build_TheHeaderNamesTheSideTheRequestAskedFor(string target, string group)
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true), Seat("b", 12, team: 200));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(target),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2), ["b"] = Streak(1, 2) },
            Champion
        );

        Assert.StartsWith($"【召唤师峡谷·{group}】连死/本局最高：", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TheHeaderNamesTheMapFromTheQueue()
    {
        // The queue decides the map name, so the same seats under a different queue read differently.
        LoadingRoster roster = new("game", 450, "CLASSIC", [Seat("a", 11, self: true)]);

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2) },
            Champion
        );

        Assert.StartsWith("【极地大乱斗·我方】", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3270, "CLASSIC", "海克斯大乱斗")]
    [InlineData(420, "CLASSIC", "召唤师峡谷")]
    [InlineData(9999, "CLASSIC", "召唤师峡谷")]
    [InlineData(9999, "SOMETHING_NEW", "当前模式")]
    public void Build_TheMapNameComesFromTheNormalizedQueue(int queue, string mode, string map)
    {
        // The queue is normalized first, and an unrecognised queue falls back to whatever the client's mode string
        // means - so 9999 under CLASSIC is a Summoner's Rift game, and only an unrecognised mode as well leaves the
        // queue as it is and lands on the generic name.
        LoadingRoster roster = new("game", queue, mode, [Seat("a", 11, self: true)]);

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2) },
            Champion
        );

        Assert.StartsWith($"【{map}·我方】", Assert.Single(lines), StringComparison.Ordinal);
    }

    // ---- the length limit ----

    [Fact]
    public void Build_ALongChampionName_IsCutSoTheEntryStillFits()
    {
        // The name is trimmed to leave room for the prefix and the numbers, so a long name cannot push the first
        // entry past the chat limit.
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(123, 456) },
            _ => new string('长', 300)
        );

        string line = Assert.Single(lines);
        Assert.True(line.Length <= 180, $"The line is {line.Length} characters, which is over the 180 limit.");
        Assert.Contains("…", line, StringComparison.Ordinal);
        Assert.Contains("123/456", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AShortName_IsNotTouched()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2) },
            _ => "短名"
        );

        Assert.Contains("短名 1/2", Assert.Single(lines), StringComparison.Ordinal);
        Assert.DoesNotContain("…", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AChampionNameWithNewlines_IsFlattened()
    {
        LoadingRoster roster = Roster(Seat("a", 11, self: true));

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            roster,
            Request(),
            new Dictionary<string, LiveDeathStreak> { ["a"] = Streak(1, 2) },
            _ => "带\r\n换行"
        );

        Assert.Contains("带  换行 1/2", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MoreSeatsThanOneLineHolds_WrapOntoAnotherLineThatRepeatsTheHeader()
    {
        var seats = new List<LoadingPlayer> { Seat("a", 1, self: true) };
        for (int i = 2; i <= 5; i++)
        {
            seats.Add(Seat($"s{i}", i));
        }

        var records = new Dictionary<string, LiveDeathStreak>();
        foreach (LoadingPlayer seat in seats)
        {
            records[seat.Key] = Streak(3, 4);
        }

        IReadOnlyList<string> lines = OverlayDeathStreakChatMessage.Build(
            Roster([.. seats]),
            Request(),
            records,
            _ => new string('名', 40)
        );

        Assert.True(lines.Count > 1, "Five long entries should have been wrapped onto more than one line.");
        Assert.All(lines, line => Assert.StartsWith(Prefix, line, StringComparison.Ordinal));
    }
}
