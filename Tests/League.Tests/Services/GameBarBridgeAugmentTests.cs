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

    private static LiveAugmentStack Stack(TrackedAugment augment, string player = "self-key") =>
        new(
            new AugmentSelection(player, augment, new AugmentStackAnchor(100, 3)),
            new AugmentStackState(5, 4, 1, true, "据此推算")
        );

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
