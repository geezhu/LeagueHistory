using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The Game Bar detail page's card sections: a stat tile, a section that holds them, and the comparison tone.
///
/// A tile always carries all five fields, even when four of them are empty - the widget reads them by name, so a
/// missing key would be a different thing from an empty one. A tip is only written when there is one, because an empty
/// tooltip and no tooltip look different when the pointer rests on the card.
/// </summary>
public class DetailCardsTests
{
    private static JObject Row(string value = "值") => new() { ["value"] = value };

    // ---- the three layouts ----

    [Fact]
    public void TheLayoutNames_AreTheOnesTheWidgetKnows()
    {
        Assert.Equal("tiles", DetailCards.Tiles);
        Assert.Equal("chips", DetailCards.Chips);
        Assert.Equal("games", DetailCards.Games);
    }

    // ---- a tile ----

    [Fact]
    public void Tile_AlwaysHasAllFiveFields()
    {
        // The widget reads them by name, so an empty one is still a field rather than a missing key.
        JObject tile = DetailCards.Tile("胜率", "52.3%");

        Assert.Equal(["title", "value", "stats", "tone", "description"], tile.Properties().Select(p => p.Name));
        Assert.Equal("", (string?)tile["stats"]);
        Assert.Equal("", (string?)tile["tone"]);
        Assert.Equal("", (string?)tile["description"]);
    }

    [Fact]
    public void Tile_CarriesWhatItWasGiven()
    {
        JObject tile = DetailCards.Tile("胜率", "52.3%", "样本 1,234 场", "good", "来自近三十天");

        Assert.Equal("胜率", (string?)tile["title"]);
        Assert.Equal("52.3%", (string?)tile["value"]);
        Assert.Equal("样本 1,234 场", (string?)tile["stats"]);
        Assert.Equal("good", (string?)tile["tone"]);
        Assert.Equal("来自近三十天", (string?)tile["description"]);
    }

    // ---- a section ----

    [Fact]
    public void Section_HasTheTabTheTitleAndTheRows()
    {
        JObject section = DetailCards.Section("stats", "属性", DetailCards.Tiles, [Row("一"), Row("二")]);

        Assert.Equal("stats", (string?)section["tab"]);
        Assert.Equal("属性", (string?)section["title"]);
        Assert.Equal(2, ((JArray)section["rows"]!).Count);
        Assert.Equal("tiles", (string?)section["layout"]);
    }

    [Fact]
    public void Section_WithoutATip_LeavesTheKeyOut()
    {
        // An empty tooltip and no tooltip are different things when the pointer rests on the card.
        Assert.False(DetailCards.Section("stats", "属性", DetailCards.Chips, []).ContainsKey("tip"));
    }

    [Fact]
    public void Section_WithATip_WritesIt()
    {
        JObject section = DetailCards.Section("stats", "属性", DetailCards.Chips, [], "说明一下");

        Assert.Equal("说明一下", (string?)section["tip"]);
    }

    [Fact]
    public void Section_WithoutRows_HasAnEmptyArray()
    {
        // The widget still has a section to draw; it simply has nothing in it.
        JObject section = DetailCards.Section("stats", "属性", DetailCards.Games, []);

        Assert.Equal(0, ((JArray)section["rows"]!).Count);
    }

    // ---- the comparison tone ----

    [Theory]
    [InlineData(10, 5, "good")]
    [InlineData(5, 10, "bad")]
    [InlineData(5, 5, "")]
    public void Compare_HigherForUs_IsGoodAndLowerIsBad(double own, double enemy, string expected)
    {
        // The tone is what colours the tile: better than the opponent is good, worse is bad, equal says nothing.
        Assert.Equal(expected, DetailCards.Compare(own, enemy));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(-5, -5)]
    public void Compare_Equal_IsNoToneAtAll(double value, double enemy)
    {
        Assert.Equal("", DetailCards.Compare(value, enemy));
    }
}
