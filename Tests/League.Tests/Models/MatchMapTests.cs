using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The map a finished game was played on.
///
/// Two shapes have to be read: the payload may wrap the game in "json" and "info" or not, so both are peeled off
/// defensively. And the Howling Abyss has skins of its own - the bridge - which come from the mode mutators rather
/// than the map id. That list needs three separate answers: no list at all is a random map (the mode rotated one in),
/// a list without a recognised override is the plain abyss, and a recognised override is that bridge.
/// </summary>
public class MatchMapTests
{
    private static JObject Entry(JToken? mapId, JArray? mutators = null)
    {
        var game = new JObject();
        if (mapId != null)
        {
            game["mapId"] = mapId;
        }

        if (mutators != null)
        {
            game["gameModeMutators"] = mutators;
        }

        return game;
    }

    // ---- the shapes ----

    [Fact]
    public void GetName_NoPayload_IsUnknown()
    {
        Assert.Equal("未知地图", MatchMap.GetName(null));
    }

    [Fact]
    public void GetName_NoMapId_IsUnknown()
    {
        Assert.Equal("未知地图", MatchMap.GetName(new JObject()));
    }

    [Fact]
    public void GetName_AMapIdThatIsNotANumber_IsUnknown()
    {
        // The id is a number in every payload that has one; text is not a map.
        Assert.Equal("未知地图", MatchMap.GetName(Entry("11")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetName_EitherWrapping_ReadsTheSameMap(bool wrapped)
    {
        JObject game = Entry(11);
        var payload = wrapped ? new JObject { ["json"] = new JObject { ["info"] = game } } : game;

        Assert.Equal("召唤师峡谷", MatchMap.GetName(payload));
    }

    // ---- the maps by id ----

    [Theory]
    [InlineData(11, "召唤师峡谷")]
    [InlineData(21, "极限闪击")]
    [InlineData(22, "云顶之弈")]
    [InlineData(30, "斗魂竞技场")]
    [InlineData(33, "无尽狂潮")]
    [InlineData(35, "班德尔之森")]
    [InlineData(453, "经典召唤师峡谷")]
    public void GetName_AKnownMap_IsItsName(int mapId, string expected)
    {
        Assert.Equal(expected, MatchMap.GetName(Entry(mapId)));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetName_AnUnknownMap_KeepsItsNumber(int mapId)
    {
        // A wrong name would be worse than the number: the reader can at least look the number up.
        Assert.Equal($"地图 {mapId}", MatchMap.GetName(Entry(mapId)));
    }

    // ---- the abyss and its bridges ----

    [Fact]
    public void GetName_TheAbyssWithoutAMutatorList_IsARandomMap()
    {
        // No list means the mode rotated a map in; naming one of the bridges would be a guess.
        Assert.Equal("随机地图", MatchMap.GetName(Entry(12)));
    }

    [Fact]
    public void GetName_TheAbyssWithAnEmptyList_IsThePlainAbyss()
    {
        // An explicit list with no override means the client's base map for that mode.
        Assert.Equal("嚎哭深渊", MatchMap.GetName(Entry(12, [])));
    }

    [Fact]
    public void GetName_TheAbyssWithAnUnknownMutator_IsThePlainAbyss()
    {
        Assert.Equal("嚎哭深渊", MatchMap.GetName(Entry(12, ["SomethingElse"])));
    }

    [Theory]
    [InlineData("MapSkin_Map12_Bloom", "莲华栈桥")]
    [InlineData("MapSkin_HA_Bilgewater", "屠夫之桥")]
    [InlineData("MapSkin_HA_Crepe", "进步之桥")]
    public void GetName_AKnownBridgeMutator_NamesTheBridge(string mutator, string expected)
    {
        Assert.Equal(expected, MatchMap.GetName(Entry(12, [mutator])));
    }

    [Theory]
    [InlineData("mapskin_map12_bloom")]
    [InlineData("MAPSKIN_MAP12_BLOOM")]
    public void GetName_TheMutatorIsMatchedWhateverItsCase(string mutator)
    {
        // The client's spelling is not something to rely on.
        Assert.Equal("莲华栈桥", MatchMap.GetName(Entry(12, [mutator])));
    }

    [Fact]
    public void GetName_SeveralMutatorsOneOfThemABridge_NamesTheBridge()
    {
        // The list carries everything that changed, so one recognisable entry is enough.
        Assert.Equal("屠夫之桥", MatchMap.GetName(Entry(12, ["SomethingElse", "MapSkin_HA_Bilgewater"])));
    }

    [Fact]
    public void GetName_AMutatorThatIsNotText_IsSkipped()
    {
        // The list is untyped, so a number in it says nothing about the map.
        Assert.Equal("屠夫之桥", MatchMap.GetName(Entry(12, [123, "MapSkin_HA_Bilgewater"])));
    }

    [Fact]
    public void GetName_BridgeMutatorsOnAnotherMap_AreNotConsulted()
    {
        // The mutators only matter for the abyss; the Rift is the Rift whatever the mode changed.
        Assert.Equal("召唤师峡谷", MatchMap.GetName(Entry(11, ["MapSkin_HA_Bilgewater"])));
    }
}
