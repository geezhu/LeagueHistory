using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The recommendations arammeta offers for one champion: an equipment or spell row carries the item ids to draw, an
/// augment row takes its name and text from the augment table, and rows without a usable id are dropped. The
/// expected values come from the checks in Tests/StatisticsChecks/AramMetaChecks.cs, which this class replaces.
/// </summary>
public class AramMetaRecommendationTests
{
    [Fact]
    public void Read_AnEquipmentCategory_ExpandsARowIntoTheItemsItsBuildUses()
    {
        AramMetaPage page = Page(AramMetaFixture.Detail());

        AramMetaRecommendation[] rows = AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "items");

        Assert.Equal(new[] { 6655, 4645 }, rows[0].Ids);
        Assert.Equal("盧登回音 + 黯影之炎", rows[0].Name);
        Assert.Equal(0.544, rows[0].WinRate);
        Assert.Equal(0.1408, rows[0].PickRate);
        Assert.Equal(7779L, rows[0].Games);
    }

    [Fact]
    public void Read_ASpellCategory_ReadsEverySpellInThePairOnce()
    {
        AramMetaPage page = Page(AramMetaFixture.Detail());

        AramMetaRecommendation[] rows = AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "spells");

        Assert.Equal(new[] { 4, 6 }, rows[0].Ids);
        Assert.Equal("閃現 + 鬼步", rows[0].Name);
    }

    [Fact]
    public void Read_AnAugmentCategory_TakesTheNameAndTextFromTheAugmentTable()
    {
        AramMetaPage page = Page(AramMetaFixture.Detail());
        JObject metadata = (JObject)page.Index.Augments["1388"]!;

        AramMetaRecommendation[] rows = AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "kPrismatic");

        Assert.Equal(new[] { 1388 }, rows[0].Ids);
        Assert.Equal((string?)metadata["name_zh"], rows[0].Name);
        Assert.Equal((string?)metadata["desc_zh"], rows[0].Description);
    }

    // The cached hero pool keeps every statistical row, so it holds more than the three the overview shows.
    [Fact]
    public void Read_TheAugmentCategories_ReadEveryRowOfThePool()
    {
        AramMetaPage page = Page(AramMetaFixture.Detail());

        int rows = new[] { "kPrismatic", "kGold", "kSilver" }.Sum(category =>
            AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, category).Length
        );

        Assert.Equal(129, rows);
    }

    [Fact]
    public void Read_WithoutAPool_FallsBackToTheOverviewOfTheChampionRow()
    {
        AramMetaPage page = Page(AramMetaFixture.Json("103"));
        AramMetaChampion champion = page.Index.Champions.Single(row => row.Id == AramMetaFixture.Ahri);
        var expected = (int)champion.Data["top"]!["kPrismatic"]![0]!["id"]!;

        AramMetaRecommendation[] rows = AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "kPrismatic");

        Assert.Equal(new[] { expected }, rows[0].Ids);
    }

    [Fact]
    public void Read_AChampionTheIndexDoesNotHave_HasNoAugmentRows()
    {
        AramMetaPage page = Page(AramMetaFixture.Detail());

        Assert.Empty(AramMetaRecommendation.Read(page, 999, "kPrismatic"));
    }

    [Theory]
    [InlineData("""{"items":{"top":[{"items":[{"id":5}]}]}}""", "5")]
    [InlineData("""{"items":{"top":[{"items":[{"id":5,"ic":0}]}]}}""", "5")]
    [InlineData("""{"items":{"top":[{"items":[{"id":5,"ic":50}]}]}}""", "5,5,5,5,5,5,5,5,5,5")]
    [InlineData("""{"items":{"top":[{"items":[{"id":0},{"id":7}]}]}}""", "7")]
    public void Read_AnItemRow_RepeatsAnItemAsOftenAsItsBuildRowSays(string detail, string expected)
    {
        AramMetaPage page = Page(JObject.Parse(detail));

        AramMetaRecommendation[] rows = AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "items");

        Assert.Equal(expected, string.Join(",", rows[0].Ids));
    }

    [Fact]
    public void Read_AnEquipmentRowWithoutItems_IsLeftOut()
    {
        AramMetaPage page = Page(JObject.Parse("""{"items":{"top":[{"id":5,"items":[]}]}}"""));

        Assert.Empty(AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "items"));
    }

    [Fact]
    public void Read_WithoutADetail_HasNoEquipmentRows()
    {
        AramMetaPage page = Page(null);

        Assert.Empty(AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, "items"));
    }

    [Fact]
    public void SpellHighlights_TheHighestPickRateAndWinRate_AreChosenSeparately()
    {
        AramMetaRecommendation[] rows =
        [
            Recommendation([4, 7], 0.51, 0.8, 100),
            Recommendation([4, 21], 0.6, 0.2, 20),
            Recommendation([4], 1, 1, 10),
        ];

        (string Title, AramMetaRecommendation? Entry)[] highlights = AramMetaRecommendation.SpellHighlights(rows);

        Assert.Equal(new[] { "使用率最高", "胜率最高" }, highlights.Select(highlight => highlight.Title));
        Assert.Same(rows[0], highlights[0].Entry);
        Assert.Same(rows[1], highlights[1].Entry);
    }

    [Fact]
    public void SpellHighlights_AMissingRate_DoesNotFabricateARecommendation()
    {
        AramMetaRecommendation[] rows = [Recommendation([4, 7], null, null, 100)];

        (string Title, AramMetaRecommendation? Entry)[] highlights = AramMetaRecommendation.SpellHighlights(rows);

        Assert.All(highlights, highlight => Assert.Null(highlight.Entry));
    }

    [Fact]
    public void SpellHighlights_EqualRates_AreBrokenByGames()
    {
        AramMetaRecommendation[] rows = [Recommendation([4, 7], 0.5, 0.5, 10), Recommendation([4, 21], 0.5, 0.5, 900)];

        (string Title, AramMetaRecommendation? Entry)[] highlights = AramMetaRecommendation.SpellHighlights(rows);

        Assert.Same(rows[1], highlights[0].Entry);
        Assert.Same(rows[1], highlights[1].Entry);
    }

    [Fact]
    public void Sort_ByWinRateOrPickRate_PutsTheHighestFirst()
    {
        AramMetaRecommendation[] rows = [Recommendation([1], 0.4, 0.9, 1), Recommendation([2], 0.6, 0.1, 1)];

        Assert.Equal(new[] { 2, 1 }, AramMetaRecommendation.Sort(rows, 1).Select(row => row.Ids[0]));
        Assert.Equal(new[] { 1, 2 }, AramMetaRecommendation.Sort(rows, 2).Select(row => row.Ids[0]));
    }

    [Fact]
    public void Sort_NoSort_KeepsTheSourceOrder()
    {
        AramMetaRecommendation[] rows = [Recommendation([1], 0.4, 0.9, 1), Recommendation([2], 0.6, 0.1, 1)];

        Assert.Equal(new[] { 1, 2 }, AramMetaRecommendation.Sort(rows, 0).Select(row => row.Ids[0]));
    }

    [Fact]
    public void ToOption_CarriesTheIdsPlayCountRatesAndWins()
    {
        AramMetaRecommendation row = Recommendation([4, 7], 0.5, 0.25, 200);

        JObject option = row.ToOption();

        Assert.Equal(new[] { 4, 7 }, option["ids"]!.Values<int>());
        Assert.Equal(200L, option["play"]!.Value<long>());
        Assert.Equal(0.5, option["win_rate"]!.Value<double>());
        Assert.Equal(100.0, option["win"]!.Value<double>());
        Assert.Equal(0.25, option["pick_rate"]!.Value<double>());
    }

    [Fact]
    public void ToOption_WithoutRates_LeavesTheWinCountOut()
    {
        JObject option = Recommendation([4], null, null, 200).ToOption();

        Assert.Equal(JTokenType.Null, option["win"]!.Type);
    }

    private static AramMetaPage Page(JObject? detail) =>
        new(AramMetaIndex.Parse(AramMetaFixture.Index()), detail, false, DateTime.UtcNow);

    private static AramMetaRecommendation Recommendation(int[] ids, double? winRate, double? pickRate, long games) =>
        new(ids, "recommendation " + ids[0], "", winRate, pickRate, games, new JObject());
}
