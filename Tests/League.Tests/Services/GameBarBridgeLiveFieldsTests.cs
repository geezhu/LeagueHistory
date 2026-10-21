using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// What the bridge adds to a row once the game is running: the player's health lines, the survival items they hold and
/// the ultimate haste the augment page collected.
///
/// The survival list is the interesting one. The game reuses the same items under prefixed ids in the arena modes
/// (223157 and 773157 are both Zhonya's), so what counts is the last four digits; an item the list does not know is
/// dropped rather than passed on with an empty effect.
/// </summary>
public class GameBarBridgeLiveFieldsTests : IDisposable
{
    private readonly TempFolder _folder = new("bridge-live");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private static LoadingPlayer Player(int championId, bool self, string key) =>
        new(
            Key: key,
            Name: key,
            Puuid: $"puuid-{key}",
            SummonerId: championId,
            ChampionId: championId,
            Team: self ? 200 : 100,
            Self: self
        );

    private static LoadingRoster Roster() =>
        new("123", 2400, "KIWI", [Player(2, self: true, "self-key"), Player(1, self: false, "enemy-key")]);

    private static LivePlayerInsight Insight(int itemGold = 12_345, params LiveMatchItem[] items) =>
        new(itemGold, 3, 2, 7, 0.75, 12.5) { Items = items };

    /// <summary>A live state carrying one insight for the self seat, filled in by the caller.</summary>
    private static OverlayLiveState Live(LivePlayerInsight insight) =>
        new(
            new LiveMatchInsight(
                300,
                new LiveTeamInsight(5, 0, 0, 0, 0),
                new LiveTeamInsight(2, 0, 0, 0, 0),
                new Dictionary<string, LivePlayerInsight> { ["self-key"] = insight },
                [],
                ""
            ),
            [],
            ""
        );

    private JObject Publish(OverlayLiveState live)
    {
        using var icon = new Bitmap(8, 8);
        GameBarBridge bridge = new(_folder.Root);
        bridge.Publish(
            Roster(),
            new Dictionary<string, PlayerChampionTier>(),
            new Dictionary<int, Image> { [1] = icon, [2] = icon },
            champion: id => "英雄" + id,
            details: _ => "",
            session: "match-1",
            status: "当前对局",
            enabled: true,
            active: true,
            show: true,
            live: live
        );
        // Disposing completes the writer's channel; waiting before that would sit out the timeout.
        bridge.Dispose();
        bridge.Completion.Wait(TimeSpan.FromSeconds(10));
        return JObject.Parse(File.ReadAllText(Directory.GetFiles(_folder.Root, "*.json").First()));
    }

    private static JObject Row(JObject snapshot, int slot) => (JObject)snapshot["players"]![slot]!;

    // ---- the player's own numbers ----

    [Fact]
    public void Publish_APlayersHealth_IsWrittenAsItsLineAndDetail()
    {
        // The player's own health is real, not an estimate; the card shows the short line and explains it on hover.
        OverlayLiveState live = Live(Insight()) with
        {
            Health = new Dictionary<string, PlayerHealth> { ["self-key"] = new("2 460", "护甲 88 · 魔抗 52") },
        };

        JObject row = Row(Publish(live), 0);

        Assert.Equal("2 460", (string?)row["health"]);
        Assert.Equal("护甲 88 · 魔抗 52", (string?)row["healthDetails"]);
    }

    [Fact]
    public void Publish_ASeatWithNoHealthEntry_HasNoHealthFields()
    {
        // The other seats have no health line until the game reports one.
        JObject row = Row(Publish(Live(Insight())), 0);

        Assert.Null(row["health"]);
        Assert.Null(row["healthDetails"]);
    }

    // ---- the survival items ----

    [Fact]
    public void Publish_ASurvivalItem_IsListedWithItsEffectAndRule()
    {
        // 3157 is Zhonya's: the card names the effect and what it does.
        JObject row = Row(Publish(Live(Insight(items: new LiveMatchItem(3157, "中娅沙漏", 1)))), 0);

        JObject item = (JObject)((JArray)row["survival"]!)[0]!;
        Assert.Equal("中娅沙漏", (string?)item["name"]);
        Assert.Equal("凝滞", (string?)item["effect"]);
        Assert.NotEmpty((string)item["rule"]!);
    }

    [Fact]
    public void Publish_APrefixedSurvivalItem_IsTheSameItem()
    {
        // The arena modes hand out the same item under 223157; the last four digits are what identify it.
        JObject row = Row(Publish(Live(Insight(items: new LiveMatchItem(223157, "中娅沙漏", 1)))), 0);

        Assert.Single((JArray)row["survival"]!);
    }

    [Fact]
    public void Publish_AnItemThatCannotSaveYou_IsNotListed()
    {
        // The list is about surviving, so an ordinary item is left out rather than passed on with an empty effect.
        JObject row = Row(Publish(Live(Insight(items: new LiveMatchItem(1001, "靴子", 1)))), 0);

        Assert.Empty((JArray)row["survival"]!);
    }

    [Fact]
    public void Publish_TheSameSurvivalItemTwice_IsListedOnce()
    {
        // Two of the same item (a stack) is one effect to know about, not two.
        JObject row = Row(
            Publish(
                Live(Insight(items: [new LiveMatchItem(3157, "中娅沙漏", 1), new LiveMatchItem(3157, "中娅沙漏", 2)]))
            ),
            0
        );

        Assert.Single((JArray)row["survival"]!);
    }

    // ---- the ultimate haste ----

    [Fact]
    public void Publish_UltimateHasteEntries_AreWrittenByPlayer()
    {
        // The augment page collected these; the widget reads them by player key.
        OverlayLiveState live = Live(Insight()) with
        {
            UltimateHaste = new Dictionary<string, double> { ["self-key"] = 25, ["enemy-key"] = 10 },
        };

        JObject haste = (JObject)Publish(live)["ultimateHaste"]!;

        Assert.Equal(25, (double)haste["self-key"]!, 3);
        Assert.Equal(10, (double)haste["enemy-key"]!, 3);
    }

    [Fact]
    public void Publish_NoUltimateHaste_IsAnEmptyObject()
    {
        // Absent is an empty map rather than a missing key, so the widget does not have to check.
        Assert.Empty((JObject)Publish(Live(Insight()))["ultimateHaste"]!);
    }
}
