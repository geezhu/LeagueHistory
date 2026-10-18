using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The arammeta statistics document: the three champions and their rates, the tier the adjusted win rate falls in,
/// and the derived match count and pick rate. The source counts all ten participants in every valid match, which is
/// why the count is the games total over ten. The expected values come from the checks in
/// Tests/StatisticsChecks/AramMetaChecks.cs, which this class replaces.
/// </summary>
public class AramMetaIndexTests
{
    [Fact]
    public void Parse_TheCapturedIndex_ReadsItsPatchVersionAndChampions()
    {
        AramMetaIndex index = AramMetaIndex.Parse(AramMetaFixture.Index());

        Assert.Equal(AramMetaFixture.Patch, index.Patch);
        Assert.Equal(AramMetaFixture.Version, index.Version);
        Assert.Equal(new[] { 103, 157, 887 }, index.Champions.Select(champion => champion.Id).Order());
    }

    [Fact]
    public void Parse_TheAdjustedAndTheRawWinRate_AreKeptApart()
    {
        AramMetaIndex index = AramMetaIndex.Parse(AramMetaFixture.Index());

        AramMetaChampion ahri = index.Champions.Single(champion => champion.Id == AramMetaFixture.Ahri);
        Assert.Equal(0.5277, ahri.WinRate);
        Assert.Equal(0.5278, ahri.RawWinRate);
    }

    [Fact]
    public void Parse_AChampion_CarriesItsNamesTagsAndGameCount()
    {
        AramMetaIndex index = AramMetaIndex.Parse(AramMetaFixture.Index());

        AramMetaChampion ahri = index.Champions.Single(champion => champion.Id == AramMetaFixture.Ahri);
        Assert.Equal("阿璃", ahri.Name);
        Assert.Equal("Ahri", ahri.Alias);
        Assert.Equal(new[] { "Mage" }, ahri.Roles);
        Assert.Equal(55200L, ahri.Games);
    }

    [Theory]
    [InlineData(0.55, "OP")]
    [InlineData(0.54, "T1")]
    [InlineData(0.52, "T1")]
    [InlineData(0.51, "T2")]
    [InlineData(0.50, "T2")]
    [InlineData(0.49, "T3")]
    [InlineData(0.48, "T3")]
    [InlineData(0.47, "T4")]
    [InlineData(0.46, "T4")]
    [InlineData(0.45, "T5")]
    [InlineData(0.0, "T5")]
    public void Tier_AdjustedWinRate_FallsIntoItsBand(double? winRate, string expected)
    {
        Assert.Equal(expected, Champion(AramMetaFixture.Ahri, 100, winRate).Tier);
    }

    [Fact]
    public void Tier_WithoutAWinRate_SaysNothing()
    {
        Assert.Equal("—", Champion(AramMetaFixture.Ahri, 100, null).Tier);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.5)]
    public void Rate_ARateBetweenZeroAndOne_IsKept(double value, double expected)
    {
        Assert.Equal(expected, AramMetaIndex.Rate(new JValue(value)));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2)]
    public void Rate_AValueOutsideZeroToOne_IsNotARate(double value)
    {
        Assert.Null(AramMetaIndex.Rate(new JValue(value)));
    }

    [Fact]
    public void Rate_MissingOrNotANumber_HasNoRate()
    {
        Assert.Null(AramMetaIndex.Rate(null));
        Assert.Null(AramMetaIndex.Rate(new JValue(double.NaN)));
    }

    [Fact]
    public void SampleMatches_TenParticipantsPerMatch_CountsTheMatches()
    {
        AramMetaIndex index = Index(Population(20, 50));

        Assert.Equal(100L, index.SampleMatches);
    }

    [Fact]
    public void SampleMatches_NoChampions_HasNoMatchCount()
    {
        Assert.Null(Index().SampleMatches);
    }

    [Fact]
    public void SampleMatches_AChampionWithoutGames_HasNoMatchCount()
    {
        var champions = Population(20, 50);
        champions[4] = Champion(champions[4].Id, null);

        Assert.Null(Index(champions).SampleMatches);
    }

    [Fact]
    public void SampleMatches_NegativeGames_HasNoMatchCount()
    {
        var champions = Population(20, 50);
        champions[4] = Champion(champions[4].Id, -1);

        Assert.Null(Index(champions).SampleMatches);
    }

    [Fact]
    public void SampleMatches_NoGamesAtAll_HasNoMatchCount()
    {
        Assert.Null(Index(Population(20, 0)).SampleMatches);
    }

    [Fact]
    public void SampleMatches_ATotalThatIsNotWholeMatches_HasNoMatchCount()
    {
        var champions = Population(20, 50);
        champions[4] = Champion(champions[4].Id, 51);

        Assert.Null(Index(champions).SampleMatches);
    }

    [Fact]
    public void PickRate_UsesTheMatchCount_NotTheShareOfParticipantSlots()
    {
        AramMetaIndex index = Index(Population(20, 50));

        Assert.Equal(0.5, index.PickRate(index.Champions[0]));
    }

    [Fact]
    public void PickRate_WithoutAMatchCount_HasNoPickRate()
    {
        AramMetaIndex index = Index(Champion(AramMetaFixture.Ahri, null));

        Assert.Null(index.PickRate(index.Champions[0]));
    }

    [Fact]
    public void PickRate_MoreGamesThanTheSample_HasNoPickRate()
    {
        AramMetaIndex index = Index(Population(20, 50));
        AramMetaChampion outsider = Champion(999, 500);

        Assert.Null(index.PickRate(outsider));
    }

    [Theory]
    [InlineData(0.6, 0.6, 1)]
    [InlineData(0.3, 0.6, 0.5)]
    [InlineData(0.0, 0.0, 0)]
    [InlineData(null, 1.0, 0)]
    [InlineData(-1.0, 1.0, 0)]
    [InlineData(2.0, 1.0, 1)]
    [InlineData(0.5, 0.0, 0)]
    public void RelativeRate_WinRateBars_NormalizeToTheHighestShownRate(double? value, double maximum, double expected)
    {
        Assert.Equal(expected, AramMetaIndex.RelativeRate(value, maximum));
    }

    [Fact]
    public void Parse_WithoutChampions_RefusesTheDocument()
    {
        var raw = AramMetaFixture.Index();
        raw.Remove("champs");

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => AramMetaIndex.Parse(raw));
        Assert.Equal("arammeta 数据结构不完整。", failure.Message);
    }

    [Fact]
    public void Parse_WithoutADetailVersion_RefusesTheDocument()
    {
        var raw = AramMetaFixture.Index();
        raw["detailVersion"] = " ";

        Assert.Throws<InvalidDataException>(() => AramMetaIndex.Parse(raw));
    }

    [Fact]
    public void Parse_RowsThatAreNotChampions_AreSkipped()
    {
        var raw = new JObject
        {
            ["detailVersion"] = "v1",
            ["champs"] = new JObject
            {
                ["103"] = new JObject { ["name"] = "Ahri" },
                ["notes"] = "not a champion",
                ["104"] = new JArray(),
            },
        };

        AramMetaIndex index = AramMetaIndex.Parse(raw);

        Assert.Equal(new[] { 103 }, index.Champions.Select(champion => champion.Id));
    }

    [Fact]
    public void Parse_AName_FallsBackFromChineseToEnglishToTheKey()
    {
        var raw = new JObject
        {
            ["detailVersion"] = "v1",
            ["champs"] = new JObject
            {
                ["103"] = new JObject { ["name"] = "Ahri" },
                ["104"] = new JObject(),
            },
        };

        AramMetaIndex index = AramMetaIndex.Parse(raw);

        Assert.Equal("Ahri", index.Champions.Single(champion => champion.Id == 103).Name);
        Assert.Equal("104", index.Champions.Single(champion => champion.Id == 104).Name);
    }

    [Fact]
    public void Parse_WithoutAliasOrTags_UsesAnEmptyAliasAndNoRoles()
    {
        var raw = new JObject
        {
            ["detailVersion"] = "v1",
            ["champs"] = new JObject { ["103"] = new JObject { ["name"] = "阿璃" } },
        };

        AramMetaIndex index = AramMetaIndex.Parse(raw);

        Assert.Equal(string.Empty, index.Champions[0].Alias);
        Assert.Empty(index.Champions[0].Roles);
    }

    [Fact]
    public void Parse_WithoutAugmentMetadata_HasAnEmptyTable()
    {
        var raw = new JObject
        {
            ["detailVersion"] = "v1",
            ["champs"] = new JObject { ["103"] = new JObject { ["name"] = "阿璃" } },
        };

        AramMetaIndex index = AramMetaIndex.Parse(raw);

        Assert.Empty(index.Augments);
    }

    [Fact]
    public void Parse_WithoutAPatchPrefix_SaysUnknown()
    {
        var raw = new JObject
        {
            ["detailVersion"] = "v1",
            ["champs"] = new JObject { ["103"] = new JObject { ["name"] = "阿璃" } },
        };

        Assert.Equal("未知", AramMetaIndex.Parse(raw).Patch);
    }

    private static AramMetaChampion[] Population(int count, long games) =>
        [.. Enumerable.Range(1, count).Select(id => Champion(id, games))];

    private static AramMetaIndex Index(params AramMetaChampion[] champions) =>
        new(AramMetaFixture.Patch, "v1", champions, [], new JObject());

    private static AramMetaChampion Champion(int id, long? games, double? winRate = 0.5) =>
        new(id, $"英雄{id}", $"Alias{id}", [], winRate, winRate, games, new JObject());
}
