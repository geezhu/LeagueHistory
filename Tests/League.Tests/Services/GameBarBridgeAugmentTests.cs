using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The augment data the bridge hands the widget: the catalog the widget's menu offers, the counted stacks, and the two
/// files the widget writes back.
///
/// The catalog is sent every time and comes straight from <c>AugmentCatalog</c>, so it is checked for the fields the
/// widget reads rather than for any particular augment. The stacks come from the snapshot and carry both ends of the
/// count - the anchor the widget resumes its corrections from, and the stacks counted to now - because the widget
/// keeps its own corrections and must be able to tell them apart from the rebuilt number.
/// </summary>
public class GameBarBridgeAugmentTests : IDisposable
{
    private readonly TempFolder _folder = new("bridge-augments");

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

    private JObject Publish(Action<GameBarBridge>? configure = null)
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
            show: true
        );
        configure?.Invoke(bridge);
        bridge.Dispose();
        bridge.Completion.Wait(TimeSpan.FromSeconds(10));
        return JObject.Parse(File.ReadAllText(Directory.GetFiles(_folder.Root, "*.json").First()));
    }

    // ---- the catalog the widget's menu reads ----

    [Fact]
    public void Publish_TheAugmentCatalog_IsSentEveryTime()
    {
        // The widget builds its menu from this list, so it has to travel with every snapshot.
        JArray catalog = (JArray)Publish()["augmentCatalog"]!;

        Assert.NotEmpty(catalog);
    }

    [Fact]
    public void Publish_EachCatalogEntry_CarriesTheFieldsTheWidgetReads()
    {
        // Traced: the first augment's entry has the machine name, the shown name, the kind, the effect and the
        // search aids; the choices array is present even when the augment has none.
        JObject entry = (JObject)((JArray)Publish()["augmentCatalog"]!)[0]!;

        Assert.Equal(
            ["augment", "name", "kind", "effect", "pinyin", "tags", "choices"],
            entry.Properties().Select(p => p.Name)
        );
        Assert.NotEmpty((string)entry["augment"]!);
        Assert.NotEmpty((string)entry["name"]!);
        Assert.Equal(JTokenType.Array, entry["tags"]!.Type);
        Assert.Equal(JTokenType.Array, entry["choices"]!.Type);
    }

    [Fact]
    public void Publish_TheCatalogCoversEveryTrackedAugment()
    {
        // A missing augment would be one the widget could not offer.
        Assert.Equal(AugmentCatalog.All.Length, ((JArray)Publish()["augmentCatalog"]!).Count);
    }

    // ---- the counted stacks ----

    [Fact]
    public void Publish_ACountedStack_CarriesItsAnchorAndItsCount()
    {
        // Both ends travel: the anchor is where the widget resumes its own corrections from, and stacks is the number
        // counted to now. The widget keeps them apart, so the row names them separately.
        JObject stack = FirstStack(Stack(TrackedAugment.InfiniteRecursion));

        Assert.Equal("self-key", (string?)stack["player"]);
        Assert.Equal("InfiniteRecursion", (string?)stack["augment"]);
        Assert.Equal("无限循环往复", (string?)stack["name"]);
        Assert.Equal(100, (double)stack["time"]!, 3);
        Assert.Equal(3, (int)stack["start"]!);
        Assert.Equal(5, (int)stack["stacks"]!);
    }

    [Fact]
    public void Publish_AFewDeaths_MakeTheCountApproximate()
    {
        // Deaths take stacks away, and the bridge cannot always see how many; the row says so rather than pretending
        // the number is exact.
        JObject stack = FirstStack(Stack(TrackedAugment.SoulEater));

        Assert.True((bool)stack["approximate"]!);
    }

    [Fact]
    public void Publish_AnAugmentDeathCannotTake_KeepsTheCountExact()
    {
        // Infinite Recursion keeps its stacks when you die, so a death does not make the count a guess.
        JObject stack = FirstStack(Stack(TrackedAugment.InfiniteRecursion));

        Assert.False((bool)stack["approximate"]!);
    }

    [Fact]
    public void Publish_NoDeaths_IsExactWhateverTheAugment()
    {
        // Without a death there is nothing to have lost track of.
        JObject stack = FirstStack(Stack(TrackedAugment.SoulEater, deaths: 0));

        Assert.False((bool)stack["approximate"]!);
    }

    [Fact]
    public void Publish_ATakedownsAugment_ExplainsWhereTheCountCameFrom()
    {
        // The reader needs the anchor, the kills and the deaths to check the number themselves.
        JObject stack = FirstStack(Stack(TrackedAugment.InfiniteRecursion));

        string note = (string)stack["note"]!;
        Assert.StartsWith("起点 3 层，之后参与击杀 5 次、死亡 1 次。", note, StringComparison.Ordinal);
        Assert.EndsWith("据此推算", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_AManualAugment_ExplainsWhatItDoesInstead()
    {
        // A manual augment is not counted from kills, so the note describes the effect and then adds the state's note.
        JObject stack = FirstStack(Stack(TrackedAugment.SoulEater));

        Assert.Equal(AugmentCatalog.Get(TrackedAugment.SoulEater).Effect + "。据此推算", (string?)stack["note"]);
    }

    /// <summary>One stack: anchored at 100 seconds on 3 layers, now counted to 5.</summary>
    private static LiveAugmentStack Stack(TrackedAugment augment, int deaths = 1) =>
        new(
            new AugmentSelection("self-key", augment, new AugmentStackAnchor(100, 3)),
            new AugmentStackState(5, 5, deaths, true, "据此推算")
        );

    /// <summary>Publishes one stack and hands back the first entry of the resulting list.</summary>
    private JObject FirstStack(LiveAugmentStack stack)
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
            live: new OverlayLiveState(
                new LiveMatchInsight(
                    300,
                    new LiveTeamInsight(0, 0, 0, 0, 0),
                    new LiveTeamInsight(0, 0, 0, 0, 0),
                    new Dictionary<string, LivePlayerInsight>(),
                    [],
                    ""
                ),
                [],
                ""
            )
            {
                AugmentStacks = [stack],
            }
        );
        bridge.Dispose();
        bridge.Completion.Wait(TimeSpan.FromSeconds(10));
        JObject snapshot = JObject.Parse(File.ReadAllText(Directory.GetFiles(_folder.Root, "*.json").First()));
        return (JObject)((JArray)snapshot["augmentStacks"]!)[0]!;
    }

    // ---- the file readers ----

    [Fact]
    public async Task ReadAugmentSelectionsAsync_BeforeAnythingWasPublished_IsNothing()
    {
        using GameBarBridge bridge = new(_folder.Root);

        Assert.Null(await bridge.ReadAugmentSelectionsAsync());
    }

    [Fact]
    public async Task ReadAugmentSelectionsAsync_AFileTooLarge_IsRefused()
    {
        GameBarBridge bridge = new(_folder.Root);
        await File.WriteAllTextAsync(Path.Combine(_folder.Root, "augment-stacks.json"), new string('x', 5000));

        Assert.Null(await bridge.ReadAugmentSelectionsAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadAugmentSelectionsAsync_AFileThatIsNotJson_IsRefused()
    {
        GameBarBridge bridge = new(_folder.Root);
        await File.WriteAllTextAsync(Path.Combine(_folder.Root, "augment-stacks.json"), "{ not json ");

        Assert.Null(await bridge.ReadAugmentSelectionsAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadUltimateHasteAsync_BeforeAnythingWasPublished_IsNothing()
    {
        using GameBarBridge bridge = new(_folder.Root);

        Assert.Null(await bridge.ReadUltimateHasteAsync());
    }

    [Fact]
    public async Task ReadUltimateHasteAsync_AFileTooLarge_IsRefused()
    {
        // This one's limit is twice the stacks file's, because it carries one number per player per augment.
        GameBarBridge bridge = new(_folder.Root);
        await File.WriteAllTextAsync(Path.Combine(_folder.Root, "ultimate-haste.json"), new string('x', 9000));

        Assert.Null(await bridge.ReadUltimateHasteAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task ReadUltimateHasteAsync_AFileThatIsNotJson_IsRefused()
    {
        GameBarBridge bridge = new(_folder.Root);
        await File.WriteAllTextAsync(Path.Combine(_folder.Root, "ultimate-haste.json"), "{ not json ");

        Assert.Null(await bridge.ReadUltimateHasteAsync());
        bridge.Dispose();
    }

    [Fact]
    public async Task TheReaders_AfterBeingDisposed_AreNothing()
    {
        GameBarBridge bridge = new(_folder.Root);
        bridge.Dispose();

        Assert.Null(await bridge.ReadAugmentSelectionsAsync());
        Assert.Null(await bridge.ReadUltimateHasteAsync());
    }
}
