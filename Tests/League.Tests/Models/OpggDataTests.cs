using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The OP.GG data the tier and rune pages are built from: which mode a queue belongs to, which position a champion is
/// shown at, and the parsing of the two payloads.
///
/// Two rules drive most of it. A query is normalised before it goes out - only a ranked query keeps a position, and
/// arena has no tiers - and a rune page is dropped unless it is complete (two different pages, nine positive ids),
/// because a half-read page cannot be applied.
/// </summary>
public class OpggDataTests
{
    private static OpggQuery Query(string mode = "ranked", string position = "mid", string tier = "gold") =>
        new("kr", mode, tier, position, ChampionId: 103);

    private static OpggTier Tier(string position, double? pickRate = 0.05, int? rank = 1, int championId = 103) =>
        new(championId, position, 0.5, pickRate, 0.1, 1, rank);

    // ---- which mode a queue is ----

    [Theory]
    [InlineData(400)]
    [InlineData(420)]
    [InlineData(430)]
    [InlineData(440)]
    [InlineData(480)]
    [InlineData(490)]
    public void ModeForQueue_ASummonersRiftQueue_IsRanked(int queue)
    {
        Assert.Equal("ranked", OpggQuery.ModeForQueue(queue));
    }

    [Theory]
    [InlineData(450, "aram")]
    [InlineData(1700, "arena")]
    [InlineData(1710, "arena")]
    [InlineData(900, "urf")]
    [InlineData(1900, "urf")]
    [InlineData(1200, "nexus_blitz")]
    [InlineData(1300, "nexus_blitz")]
    public void ModeForQueue_AnotherModeQueue_IsThatMode(int queue, string expected)
    {
        Assert.Equal(expected, OpggQuery.ModeForQueue(queue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(-1)]
    public void ModeForQueue_AQueueWithNoMode_IsNull(int queue)
    {
        Assert.Null(OpggQuery.ModeForQueue(queue));
    }

    [Fact]
    public void ModeForFollowing_AQueueWithNoMode_FallsBackToRanked()
    {
        // A follow request still has to name a mode, and ranked is the one that always exists.
        Assert.Equal("ranked", OpggQuery.ModeForFollowing(9999));
        Assert.Equal("aram", OpggQuery.ModeForFollowing(450));
    }

    // ---- normalising a query ----

    [Fact]
    public void Normalize_ARankedQuery_LowerCasesThePosition()
    {
        Assert.Equal("mid", Query(position: "MID").Normalize().Position);
    }

    [Fact]
    public void Normalize_AnyOtherMode_HasNoPosition()
    {
        // Positions only exist for ranked; the other modes show one list per champion.
        Assert.Equal("none", Query(mode: "aram", position: "mid").Normalize().Position);
    }

    [Fact]
    public void Normalize_AnArenaQuery_HasNoTier()
    {
        // Arena has no tiers to filter by, so the tier is neutralised rather than sent.
        Assert.Equal("all", Query(mode: "arena", tier: "gold").Normalize().Tier);
    }

    [Fact]
    public void Normalize_AQueryThatNeedsNoChange_KeepsItsTier()
    {
        Assert.Equal("gold", Query().Normalize().Tier);
    }

    // ---- which position a champion is shown at ----

    [Fact]
    public void PositionForChampion_TheAssignedPosition_IsPreferred()
    {
        // The position the player was assigned decides it, whatever the pick rates say.
        OpggTier[] rows = [Tier("top", pickRate: 0.9), Tier("mid", pickRate: 0.1), Tier("jungle", pickRate: 0.5)];

        Assert.Equal("mid", OpggQuery.PositionForChampion(rows, 103, "MID"));
    }

    [Fact]
    public void PositionForChampion_WithoutAnAssignedPosition_TheMostPickedOneWins()
    {
        OpggTier[] rows = [Tier("top", pickRate: 0.1), Tier("jungle", pickRate: 0.5), Tier("mid", pickRate: 0.3)];

        Assert.Equal("jungle", OpggQuery.PositionForChampion(rows, 103, null));
    }

    [Fact]
    public void PositionForChampion_EqualPickRates_AreBrokenByTheBetterRank()
    {
        // Rank 1 is better than rank 2, so the smaller number is the tie-breaker.
        OpggTier[] rows = [Tier("top", pickRate: 0.5, rank: 2), Tier("mid", pickRate: 0.5, rank: 1)];

        Assert.Equal("mid", OpggQuery.PositionForChampion(rows, 103, null));
    }

    [Fact]
    public void PositionForChampion_AMissingPickRate_CountsAsLowest()
    {
        // A position with no pick rate must not win over one that has any.
        OpggTier[] rows = [Tier("top", pickRate: null), Tier("mid", pickRate: 0.01)];

        Assert.Equal("mid", OpggQuery.PositionForChampion(rows, 103, null));
    }

    [Fact]
    public void PositionForChampion_AMissingRank_SortsLast()
    {
        OpggTier[] rows = [Tier("top", pickRate: 0.5, rank: null), Tier("mid", pickRate: 0.5, rank: 9)];

        Assert.Equal("mid", OpggQuery.PositionForChampion(rows, 103, null));
    }

    [Fact]
    public void PositionForChampion_AnotherChampion_IsIgnored()
    {
        // Rows for other champions share the list, so the id has to match too.
        OpggTier[] rows = [Tier("top", championId: 999), Tier("mid")];

        Assert.Equal("mid", OpggQuery.PositionForChampion(rows, 103, null));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("unknown")]
    public void PositionForChampion_APositionThatIsNotOneOfTheFive_IsIgnored(string position)
    {
        // The five lanes are what the page has; anything else is not a lane a champion is shown at.
        Assert.Null(OpggQuery.PositionForChampion([Tier(position)], 103, null));
    }

    [Fact]
    public void PositionForChampion_NoRowsAtAll_IsNull()
    {
        Assert.Null(OpggQuery.PositionForChampion([], 103, "mid"));
    }

    [Fact]
    public void PositionForChampion_ThePositionIsReturnedInLowerCase()
    {
        Assert.Equal("mid", OpggQuery.PositionForChampion([Tier("MID")], 103, null));
    }

    // ---- the ratio ----

    [Theory]
    [InlineData(5, 10, 0.5)]
    [InlineData(0, 10, 0.0)]
    [InlineData(10, 10, 1.0)]
    public void Ratio_ADenominatorAboveZero_Divides(double numerator, double denominator, double expected)
    {
        double? ratio = OpggBuild.Ratio(numerator, denominator);
        Assert.NotNull(ratio);
        Assert.Equal(expected, ratio.Value, 3);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    [InlineData(0, 0)]
    public void Ratio_ADenominatorOfZero_TellsNothingInsteadOfDividingByIt(double numerator, double denominator)
    {
        Assert.Null(OpggBuild.Ratio(numerator, denominator));
    }

    [Fact]
    public void Ratio_AMissingDenominator_IsNull()
    {
        Assert.Null(OpggBuild.Ratio(5, null));
    }

    // ---- parsing a build ----

    private static JObject Build(params (int Primary, int Secondary, int Perks)[] pages)
    {
        var raw = new JObject
        {
            ["meta"] = new JObject { ["version"] = "15.1" },
            ["data"] = new JObject
            {
                ["summary"] = new JObject
                {
                    ["id"] = 103,
                    ["average_stats"] = new JObject { ["win_rate"] = 0.5 },
                },
                ["runes"] = new JArray(
                    pages.Select(p => new JObject
                    {
                        ["primary_page_id"] = p.Primary,
                        ["secondary_page_id"] = p.Secondary,
                        ["primary_rune_ids"] = new JArray(Enumerable.Repeat(1, p.Perks - 5).Cast<object>().ToArray()),
                        ["secondary_rune_ids"] = new JArray(Enumerable.Repeat(2, 2).Cast<object>().ToArray()),
                        ["stat_mod_ids"] = new JArray(Enumerable.Repeat(3, 3).Cast<object>().ToArray()),
                        ["play"] = 10,
                        ["win"] = 5,
                    })
                ),
            },
        };
        return raw;
    }

    [Fact]
    public void Parse_AWellFormedBuild_KeepsTheVersionTheStatsAndTheRunes()
    {
        OpggBuild build = OpggBuild.Parse(Build((8000, 8100, 9)), Query(mode: "aram"));

        Assert.Equal("15.1", build.Version);
        Assert.NotNull(build.Stats);
        Assert.Single(build.Runes);
        Assert.NotNull(build.Runes[0].WinRate);
        Assert.Equal(0.5, build.Runes[0].WinRate!.Value, 3);
    }

    [Fact]
    public void Parse_AMisplacedChampionId_IsRefused()
    {
        // The id in the payload is what ties it to the query; a different one is a different champion's build.
        JObject raw = Build((8000, 8100, 9));
        ((JObject)raw["data"]!)["summary"]!["id"] = 999;

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => OpggBuild.Parse(raw, Query()));
        Assert.Equal("该英雄在当前筛选条件下暂无攻略。", error.Message);
    }

    [Fact]
    public void Parse_WithoutADataObject_IsRefused()
    {
        Assert.Throws<InvalidDataException>(() => OpggBuild.Parse(new JObject(), Query()));
    }

    [Fact]
    public void Parse_WithoutStatsForThePosition_IsRefused()
    {
        var raw = new JObject { ["data"] = new JObject { ["summary"] = new JObject { ["id"] = 103 } } };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            OpggBuild.Parse(raw, Query(mode: "aram"))
        );
        Assert.Equal("该英雄在当前分路暂无统计，请更换分路。", error.Message);
    }

    [Fact]
    public void Parse_WithoutAVersion_SaysItIsUnknown()
    {
        JObject raw = Build((8000, 8100, 9));
        raw.Remove("meta");

        Assert.Equal("未知", OpggBuild.Parse(raw, Query(mode: "aram")).Version);
    }

    [Fact]
    public void Parse_ARunePageWithTheSameTwoPagesTwice_IsDropped()
    {
        // A rune page needs two different trees; the same one twice is not a page that can be applied.
        Assert.Empty(OpggBuild.Parse(Build((8000, 8000, 9)), Query(mode: "aram")).Runes);
    }

    [Fact]
    public void Parse_ARunePageWithoutTwoDifferentPageIds_IsDropped()
    {
        Assert.Empty(OpggBuild.Parse(Build((0, 8100, 9)), Query(mode: "aram")).Runes);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public void Parse_ARunePageWithTheWrongNumberOfRunes_IsDropped(int perks)
    {
        // Nine ids: four primary, two secondary, three stat shards.
        Assert.Empty(OpggBuild.Parse(Build((8000, 8100, perks)), Query(mode: "aram")).Runes);
    }

    [Fact]
    public void Parse_APageWithAZeroRuneId_IsDropped()
    {
        // A zero is an empty slot, not a rune.
        JObject raw = Build((8000, 8100, 9));
        ((JObject)((JArray)((JObject)raw["data"]!)["runes"]!)[0]!)["stat_mod_ids"] = new JArray(3, 0, 3);

        Assert.Empty(OpggBuild.Parse(raw, Query(mode: "aram")).Runes);
    }

    // ---- parsing the tiers ----

    private static JObject Tiers(params (int Champion, string Position, double? Rank)[] rows) =>
        new()
        {
            ["data"] = new JArray(
                rows.Select(r => new JObject
                {
                    ["id"] = r.Champion,
                    ["average_stats"] = new JObject
                    {
                        ["win_rate"] = 0.5,
                        ["pick_rate"] = 0.1,
                        ["tier"] = 1,
                        ["rank"] = r.Rank,
                    },
                    ["positions"] = new JArray(
                        new JObject
                        {
                            ["name"] = r.Position,
                            ["stats"] = new JObject
                            {
                                ["win_rate"] = 0.5,
                                ["pick_rate"] = 0.1,
                                ["tier"] = 1,
                                ["rank"] = r.Rank,
                            },
                            ["counters"] = new JArray(
                                new JObject { ["champion_id"] = 1 },
                                new JObject { ["champion_id"] = 2 },
                                new JObject { ["champion_id"] = 3 },
                                new JObject { ["champion_id"] = 4 }
                            ),
                        }
                    ),
                })
            ),
        };

    [Fact]
    public void ParseTiers_ARankedPayload_YieldsOneRowPerPosition()
    {
        OpggTier[] tiers = OpggBuild.ParseTiers(Tiers((103, "mid", 1), (103, "top", 2)), "ranked");

        Assert.Equal(["mid", "top"], tiers.Select(t => t.Position));
    }

    [Fact]
    public void ParseTiers_TheRowsAreOrderedByRank()
    {
        // A missing rank sorts last, so the ranked rows come first.
        OpggTier[] tiers = OpggBuild.ParseTiers(Tiers((103, "mid", null), (104, "mid", 1)), "ranked");

        Assert.Equal([104, 103], tiers.Select(t => t.ChampionId));
    }

    [Fact]
    public void ParseTiers_OnlyTheFirstThreeCountersAreKept()
    {
        Assert.Equal([1, 2, 3], Assert.Single(OpggBuild.ParseTiers(Tiers((103, "mid", 1)), "ranked")).Counters);
    }

    [Fact]
    public void ParseTiers_AnEmptyChampionId_IsDropped()
    {
        Assert.Empty(OpggBuild.ParseTiers(Tiers((0, "mid", 1)), "ranked"));
    }

    [Fact]
    public void ParseTiers_AModeWithoutPositions_UsesTheAverageStatsOnce()
    {
        // Only ranked has per-position rows; the other modes show one row per champion under "none".
        OpggTier[] tiers = OpggBuild.ParseTiers(Tiers((103, "mid", 1)), "aram");

        Assert.Equal("none", Assert.Single(tiers).Position);
    }

    [Fact]
    public void ParseTiers_APayloadWithoutADataArray_IsRefused()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            OpggBuild.ParseTiers(new JObject(), "ranked")
        );
        Assert.Equal("暂无英雄梯队数据。", error.Message);
    }

    [Fact]
    public void ParseTiers_APositionWithoutStats_IsSkipped()
    {
        var raw = new JObject
        {
            ["data"] = new JArray(
                new JObject { ["id"] = 103, ["positions"] = new JArray(new JObject { ["name"] = "mid" }) }
            ),
        };

        Assert.Empty(OpggBuild.ParseTiers(raw, "ranked"));
    }
}
