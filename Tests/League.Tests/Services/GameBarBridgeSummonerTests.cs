using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The ally summoner spells on the bridge's rows, and the timer file the widget writes back.
///
/// Two things are pinned. A spell carries a countdown only when the widget is timing it - the enemies' spells are
/// listed with their icons but no timer, and a spell whose cooldown is zero is not "ready at zero seconds", it is not
/// timed at all. And the icon file only holds the spells that some seat actually has, so a spell nobody picked does not
/// travel with the snapshot.
/// </summary>
public class GameBarBridgeSummonerTests : IDisposable
{
    private readonly TempFolder _folder = new("bridge-summoners");

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

    private static PlayerSummoner Spell(int slot, double cooldown, double? started) =>
        new(slot, new SummonerSpellInfo("SummonerFlash", "闪现", cooldown, "flash.png"), cooldown, started);

    /// <summary>A live state with the players' spells and two icons, one of them for a spell nobody has.</summary>
    private static OverlayLiveState Live() =>
        new(
            new LiveMatchInsight(
                100,
                new LiveTeamInsight(0, 0, 0, 0, 0),
                new LiveTeamInsight(0, 0, 0, 0, 0),
                new Dictionary<string, LivePlayerInsight>
                {
                    ["self-key"] = new(0, 1, 2, 3, null, 0),
                    ["enemy-key"] = new(0, 1, 2, 3, null, 0),
                },
                [],
                ""
            ),
            [],
            ""
        )
        {
            Summoners = new Dictionary<string, PlayerSummoner[]>
            {
                ["self-key"] = [Spell(0, 300, 0), Spell(1, 0, null)],
                ["enemy-key"] = [Spell(0, 0, null)],
            },
            SummonerIcons = new Dictionary<string, string>
            {
                ["SummonerFlash"] = "/icons/flash.png",
                ["SummonerHeal"] = "/icons/heal.png",
            },
        };

    private (GameBarBridge Bridge, JObject Snapshot) Publish()
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
            live: Live()
        );
        // The bridge writes asynchronously. Disposing completes the channel and Completion waits for the loop to
        // drain, so reading after the dispose sees the final file.
        bridge.Dispose();
        bridge.Completion.Wait(TimeSpan.FromSeconds(10));
        string path = Directory.GetFiles(_folder.Root, "*.json").First();
        return (bridge, JObject.Parse(File.ReadAllText(path)));
    }

    private static JObject Row(JObject snapshot, int slot) => (JObject)snapshot["players"]![slot]!;

    // ---- the rows ----

    [Fact]
    public void Publish_ATimedSpell_CarriesItsCountdownAndStart()
    {
        // Traced: the self seat's first spell is timed at 300 seconds from 0, so the row carries both.
        (GameBarBridge bridge, JObject snapshot) = Publish();
        using (bridge)
        {
            JObject spell = (JObject)Row(snapshot, 0)["summoners"]![0]!;

            Assert.True((bool)spell["timed"]!);
            Assert.Equal(300, (double)spell["cooldown"]!, 3);
            Assert.Equal(0, (double)spell["started"]!, 3);
        }
    }

    [Fact]
    public void Publish_ASpellWithNoCooldown_CarriesNoCountdown()
    {
        // Zero means "not timed", which is different from "back at zero seconds".
        (GameBarBridge bridge, JObject snapshot) = Publish();
        using (bridge)
        {
            JObject spell = (JObject)Row(snapshot, 0)["summoners"]![1]!;

            Assert.False((bool)spell["timed"]!);
            Assert.Null(spell["cooldown"]);
            Assert.Null(spell["started"]);
        }
    }

    [Fact]
    public void Publish_AnEnemysSpell_IsListedButNeverTimed()
    {
        // The widget has nothing to time an enemy's spell with, so the row reports it as untimed however it is set.
        (GameBarBridge bridge, JObject snapshot) = Publish();
        using (bridge)
        {
            JObject spell = (JObject)Row(snapshot, 5)["summoners"]![0]!;

            Assert.Equal("闪现", (string?)spell["name"]);
            Assert.False((bool)spell["timed"]!);
        }
    }

    [Fact]
    public void Publish_TheIcons_CoverOnlyTheSpellsSomeSeatHas()
    {
        // A spell nobody picked does not travel with the snapshot.
        (GameBarBridge bridge, JObject snapshot) = Publish();
        using (bridge)
        {
            JObject icons = (JObject)snapshot["summonerIcons"]!;

            Assert.Contains("SummonerFlash", icons.Properties().Select(p => p.Name));
            Assert.DoesNotContain("SummonerHeal", icons.Properties().Select(p => p.Name));
        }
    }

    // ---- the timer file ----

    [Fact]
    public async Task ReadSummonerTimersAsync_BeforeAnythingWasPublished_IsNothing()
    {
        // The session is what the file is keyed by; without one there is no file to read.
        using GameBarBridge bridge = new(_folder.Root);

        Assert.Null(await bridge.ReadSummonerTimersAsync());
    }

    [Fact]
    public async Task ReadSummonerTimersAsync_NoFile_YieldsNothing()
    {
        (GameBarBridge bridge, _) = Publish();

        Assert.Null(await bridge.ReadSummonerTimersAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadSummonerTimersAsync_AFileThatIsTooLarge_IsRefused()
    {
        // The size bound keeps a runaway file from being parsed; eight kilobytes is the writer's own limit.
        (GameBarBridge bridge, _) = Publish();
        string path = Path.Combine(_folder.Root, "summoner-timers.json");
        await File.WriteAllTextAsync(path, new string('x', 9000));

        Assert.Null(await bridge.ReadSummonerTimersAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadSummonerTimersAsync_AFileThatIsNotJson_IsRefusedInsteadOfThrowing()
    {
        // The widget replaces the file atomically, so a read during the swap must be harmless.
        (GameBarBridge bridge, _) = Publish();
        await File.WriteAllTextAsync(Path.Combine(_folder.Root, "summoner-timers.json"), "{ not json ");

        Assert.Null(await bridge.ReadSummonerTimersAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadSummonerTimersAsync_AFileForAnotherSession_IsRefused()
    {
        // The file names the session it belongs to; one from an earlier game must not be read as this one's.
        (GameBarBridge bridge, _) = Publish();
        await File.WriteAllTextAsync(
            Path.Combine(_folder.Root, "summoner-timers.json"),
            """{"protocol":1,"session":"other","timers":[]}"""
        );

        Assert.Null(await bridge.ReadSummonerTimersAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadSummonerTimersAsync_AfterBeingDisposed_IsNothing()
    {
        (GameBarBridge bridge, _) = Publish();
        bridge.Dispose();

        Assert.Null(await bridge.ReadSummonerTimersAsync());
    }
}
