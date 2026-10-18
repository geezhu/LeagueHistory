using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The Game Bar bridge turns a roster into the one snapshot.json the widget reads, and describes the widget's own
/// heartbeat in settings. A temporary directory is the whole injection: no client, no named pipe and no window are
/// involved. This class replaces the checks in Tests/StatisticsChecks/GameBarBridgeChecks.cs.
/// </summary>
public class GameBarBridgeTests : IDisposable
{
    private const long Now = 100000;
    private const string Disconnected = "组件未连接 · 按 Win＋G 打开「League 对局速览」";
    private const string NoValidData = "组件已打开，但尚未读到有效数据";
    private const string PictureNotLoaded = "组件画面尚未加载，请关闭后重新打开";
    private const string NotPinned = "已连接，尚未固定 · 点图钉可留在游戏上方";
    private const string Waiting = "已连接并固定 · 等待对局";
    private const string Connected = "已连接并固定";

    private readonly TempFolder _folder = new("league-gamebar");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void DescribeWidgetState_NoWidget_ReportsDisconnected()
    {
        string status = GameBarBridge.DescribeWidgetState(null, Now);

        Assert.Equal(Disconnected, status);
    }

    [Theory]
    [InlineData(100, Connected)]
    [InlineData(14999, Connected)]
    [InlineData(15000, Disconnected)]
    [InlineData(20000, Disconnected)]
    [InlineData(-1, Disconnected)]
    public void DescribeWidgetState_HeartbeatAge_DecidesWhetherTheWidgetIsConnected(long age, string expected)
    {
        JObject state = Heartbeat();
        state["updatedAt"] = Now - age;

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(0, 560)]
    [InlineData(820, 0)]
    [InlineData(0, 0)]
    public void DescribeWidgetState_WidgetWithoutASize_ReportsThePictureIsNotLoaded(double width, double height)
    {
        JObject state = Heartbeat();
        state["width"] = width;
        state["height"] = height;

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal(PictureNotLoaded, status);
    }

    [Theory]
    [InlineData(true, true, Connected)]
    [InlineData(true, false, Waiting)]
    [InlineData(false, true, NotPinned)]
    [InlineData(false, false, NotPinned)]
    public void DescribeWidgetState_PinnedAndActive_ReportTheWidgetState(bool pinned, bool active, string expected)
    {
        JObject state = Heartbeat();
        state["pinned"] = pinned;
        state["active"] = active;

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void DescribeWidgetState_ReadErrorWithoutAConnection_ReportsNoValidData()
    {
        JObject state = Heartbeat();
        state.Remove("connected");
        state["readError"] = "read failed";

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal(NoValidData, status);
    }

    [Fact]
    public void DescribeWidgetState_TransientReadErrorOnAFreshSnapshot_KeepsTheConnection()
    {
        JObject state = Heartbeat();
        state["readError"] = "read failed";

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal(Connected, status);
    }

    [Fact]
    public void DescribeWidgetState_StoppedPublisher_ReportsNoValidData()
    {
        JObject state = Heartbeat();
        state["connected"] = false;

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal(NoValidData, status);
    }

    [Fact]
    public void DescribeWidgetState_WidgetReportedError_ReturnsItVerbatim()
    {
        JObject state = Heartbeat();
        state["error"] = "组件需要更新";

        string status = GameBarBridge.DescribeWidgetState(state, Now);

        Assert.Equal("组件需要更新", status);
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(200, 100)]
    public void Slots_OwnTeamIsAlwaysFirst_FillsFiveSlotsPerSide(int ownTeam, int enemyTeam)
    {
        LoadingPlayer?[] slots = GameBarBridge.Slots(Roster(ownTeam, enemyTeam));

        Assert.Equal(10, slots.Length);
        Assert.Equal("self-key", slots[0]?.Key);
        Assert.Equal("enemy-key", slots[5]?.Key);
        Assert.Equal(8, slots.Count(slot => slot is null));
    }

    [Fact]
    public async Task Publish_OwnRedTeam_IsPlacedFirst()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal("我方", (string?)snapshot["teams"]![0]);
        Assert.Equal("Self · 自己", (string)Row(snapshot, 0)["name"]!);
    }

    [Fact]
    public async Task Publish_MissingSlots_StayOnTheCorrectTeam()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal(10, snapshot["players"]!.Count());
        Assert.Empty(Row(snapshot, 1));
        Assert.Empty(Row(snapshot, 2));
        Assert.Empty(Row(snapshot, 3));
        Assert.Empty(Row(snapshot, 4));
        Assert.Empty(Row(snapshot, 6));
        Assert.Empty(Row(snapshot, 9));
    }

    [Fact]
    public async Task Publish_Enemy_RemainsInTheOtherColumn()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal("Enemy", (string)Row(snapshot, 5)["name"]!);
    }

    [Fact]
    public async Task Publish_PlayerMastery_IsIndependentOnBothTeams()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.StartsWith("成就 12级", (string)Row(snapshot, 0)["mastery"]!);
        Assert.StartsWith("成就 5级", (string)Row(snapshot, 5)["mastery"]!);
    }

    [Fact]
    public async Task Publish_AvailableDeathStreak_CrossesTheBridge()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal(3, (int)Row(snapshot, 0)["currentDeathStreak"]!);
        Assert.Equal(5, (int)Row(snapshot, 0)["maxDeathStreak"]!);
        Assert.True((bool)Row(snapshot, 0)["deathStreakComplete"]!, "the event history covers the whole match");
        Assert.Equal("连死 3 · 最高 5", (string)Row(snapshot, 0)["deathStreak"]!);
    }

    [Fact]
    public async Task Publish_UnavailableDeathStreak_StaysUnknownInsteadOfZero()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal(JTokenType.Null, Row(snapshot, 5)["currentDeathStreak"]!.Type);
        Assert.Equal(JTokenType.Null, Row(snapshot, 5)["maxDeathStreak"]!.Type);
    }

    [Fact]
    public async Task Publish_PartyMarker_CrossesTheBridge()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal(5, (int)Row(snapshot, 0)["partySize"]!);
        Assert.Equal(0xcba2ff, (int)Row(snapshot, 0)["partyColor"]!);
        Assert.Equal("5人", (string)Row(snapshot, 0)["party"]!);
        Assert.StartsWith("疑似5人组", (string)Row(snapshot, 0)["partyDetails"]!);
    }

    [Fact]
    public async Task Publish_MissingHistory_LeavesThePlayerUnmarked()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal(0, (int)Row(snapshot, 5)["partySize"]!);
        Assert.Equal("", (string)Row(snapshot, 5)["party"]!);
    }

    [Fact]
    public async Task Publish_TierWinRateAndPickRate_CrossTheBridge()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal("T4", (string)Row(snapshot, 0)["tier"]!);
        Assert.Equal("胜 48.5% · 选用率 7.1%", (string)Row(snapshot, 0)["stats"]!);
    }

    [Fact]
    public async Task Publish_ChampionIcon_IsSizedForTheWidget()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        using var bytes = new MemoryStream(Convert.FromBase64String((string)Row(snapshot, 0)["icon"]!));
        using var decoded = Image.FromStream(bytes);

        Assert.Equal(48, decoded.Width);
        Assert.Equal(48, decoded.Height);
    }

    [Fact]
    public async Task Publish_AccountIdentifiers_AreNotExported()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.DoesNotContain("puuid", snapshot.ToString());
    }

    [Fact]
    public async Task Publish_KeyboardCommands_AreNeverPublished()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());

        Assert.Equal("main", (string)snapshot["shortcutOwner"]!);
        Assert.Equal("", (string)snapshot["shortcut"]!);
        Assert.Null(snapshot["toggleSequence"]);
        Assert.Null(snapshot["toggleAt"]);
    }

    [Fact]
    public async Task Publish_SessionAndCommand_ShareOneInstanceIdentity()
    {
        JObject snapshot = await PublishSnapshotAsync(RosterPublish());
        string session = (string)snapshot["session"]!;

        Assert.EndsWith(":match-1", session);
        Assert.Equal(session.Split(':')[0], ((string)snapshot["command"]!).Split(':')[0]);
    }

    /// <summary>A widget that is open, pinned and showing a match, as the widget itself reports it.</summary>
    private static JObject Heartbeat() =>
        new()
        {
            ["updatedAt"] = Now - 100,
            ["connected"] = true,
            ["pinned"] = true,
            ["active"] = true,
            ["shortcut"] = "PrtSc",
            ["width"] = 820,
            ["height"] = 560,
        };

    private static JObject Row(JObject snapshot, int slot) => (JObject)snapshot["players"]![slot]!;

    private static LoadingRoster Roster(int ownTeam, int enemyTeam) =>
        new(
            "123",
            2400,
            "KIWI",
            [
                new("enemy-key", "Enemy", "private-puuid", 123, 1, enemyTeam, false),
                new("self-key", "Self", "secret-puuid", 456, 2, ownTeam, true),
            ]
        );

    private static Dictionary<string, PlayerChampionTier> Ranks() =>
        new() { ["self-key"] = new("T4", "胜 48.5%", "arammeta · 调整后胜率", 2400, "none") { PickRate = "7.1%" } };

    private static Dictionary<string, PlayerChampionMastery> Masteries() =>
        new() { ["self-key"] = new(2, 12, 186321, null, true), ["enemy-key"] = new(1, 5, 2000, null, true) };

    private static Dictionary<string, LiveDeathStreak> DeathStreaks() =>
        new() { ["self-key"] = new(3, 5, true, "完整历史"), ["enemy-key"] = LiveDeathStreak.Unavailable() };

    private static Dictionary<string, OverlayPartyMarker> Parties() => new() { ["self-key"] = new(5, 0xcba2ff) };

    private static Action<GameBarBridge> RosterPublish() => bridge => PublishRoster(bridge);

    private static void PublishRoster(GameBarBridge bridge)
    {
        using var icon = new Bitmap(32, 32);
        bridge.Publish(
            Roster(ownTeam: 200, enemyTeam: 100),
            Ranks(),
            new Dictionary<int, Image> { [2] = icon },
            champion: id => "英雄 " + id,
            details: _ => "近期 20 场，12 胜",
            session: "match-1",
            status: "当前对局",
            enabled: true,
            active: true,
            show: true,
            masteries: Masteries(),
            deathStreaks: DeathStreaks(),
            parties: Parties()
        );
    }

    private async Task<JObject> PublishSnapshotAsync(Action<GameBarBridge> publish)
    {
        var (snapshot, _) = await PublishAsync(publish);
        return snapshot;
    }

    /// <summary>
    /// Publishing is asynchronous; the writer only starts on a pool thread. Disposing completes the channel and
    /// <see cref="GameBarBridge.Completion"/> then waits for the loop to drain it, so the file on disk is final and
    /// the test needs no polling.
    /// </summary>
    private async Task<(JObject Snapshot, string Status)> PublishAsync(Action<GameBarBridge> publish)
    {
        var bridge = new GameBarBridge(_folder.Root);
        publish(bridge);
        bridge.Dispose();
        await bridge.Completion;

        string written = await File.ReadAllTextAsync(_folder.File("snapshot.json"));

        return (JObject.Parse(written), bridge.Status);
    }
}
