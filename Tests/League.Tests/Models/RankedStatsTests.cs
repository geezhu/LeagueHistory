using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// A player's ranked record for the two queues the page shows, read out of the client's payload.
///
/// Half of it is about what the payload may omit. Win and loss counts decide whether a record exists at all, so a queue
/// that lists a tier but no results still reads as unknown rather than as zero games; the tier and division are
/// normalised, with "NONE" and "NA" meaning "nothing to show"; and a tier above diamond has no division, so none is
/// printed for it.
/// </summary>
public class RankedStatsTests
{
    private static JObject Payload(JObject? solo = null, JObject? flex = null) =>
        new()
        {
            ["queueMap"] = new JObject { ["RANKED_SOLO_5x5"] = solo, ["RANKED_FLEX_SR"] = flex },
        };

    private static JObject Queue(
        string tier = "GOLD",
        string division = "II",
        int? wins = 30,
        int? losses = 20,
        int leaguePoints = 42
    )
    {
        var queue = new JObject
        {
            ["queueType"] = "RANKED_SOLO_5x5",
            ["tier"] = tier,
            ["division"] = division,
            ["leaguePoints"] = leaguePoints,
        };
        if (wins.HasValue)
        {
            queue["wins"] = wins.Value;
        }

        if (losses.HasValue)
        {
            queue["losses"] = losses.Value;
        }

        return queue;
    }

    // ---- the two queues ----

    [Fact]
    public void FromJson_AlwaysYieldsBothQueues()
    {
        // The page has two slots, so both are always there even when one has nothing in it.
        Dictionary<string, RankedStats> stats = RankedStats.FromJson(Payload());

        Assert.Equal(["单双排", "灵活组排"], stats.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void FromJson_AnEmptyPayload_LeavesBothUnranked()
    {
        Dictionary<string, RankedStats> stats = RankedStats.FromJson(new JObject());

        Assert.All(stats.Values, s => Assert.Equal("未定级", s.Tier));
        Assert.All(stats.Values, s => Assert.True(s.IsUnknown));
    }

    [Fact]
    public void FromJson_EachQueue_IsReadFromItsOwnEntry()
    {
        Dictionary<string, RankedStats> stats = RankedStats.FromJson(
            Payload(solo: Queue("GOLD"), flex: Queue("SILVER", "IV"))
        );

        Assert.Equal("GOLD", stats["单双排"].Tier);
        Assert.Equal("SILVER", stats["灵活组排"].Tier);
    }

    // ---- whether there is a record ----

    [Fact]
    public void IsUnknown_AQueueWithBothCounts_IsKnown()
    {
        Assert.False(RankedStats.FromJson(Payload(solo: Queue()))["单双排"].IsUnknown);
    }

    [Fact]
    public void IsUnknown_AQueueWithOnlyWins_IsUnknown()
    {
        // A record needs both halves: one number alone cannot give a win rate.
        Assert.True(RankedStats.FromJson(Payload(solo: Queue(losses: null)))["单双排"].IsUnknown);
    }

    [Fact]
    public void HasWins_AQueueWithOnlyWins_StillReportsThem()
    {
        // Wins are shown on their own when they are there, which is why this flag is separate from the record's.
        RankedStats stats = RankedStats.FromJson(Payload(solo: Queue(losses: null)))["单双排"];

        Assert.True(stats.HasWins);
        Assert.Equal("30 场", stats.WinsDisplay);
    }

    [Fact]
    public void HasWins_AQueueWithOnlyLosses_DoesNotReportWins()
    {
        RankedStats stats = RankedStats.FromJson(Payload(solo: Queue(wins: null)))["单双排"];

        Assert.False(stats.HasWins);
        Assert.Equal("未知", stats.WinsDisplay);
    }

    // ---- the totals ----

    [Fact]
    public void TotalGames_AKnownRecord_IsWinsAndLosses()
    {
        Assert.Equal(50, RankedStats.FromJson(Payload(solo: Queue(wins: 30, losses: 20)))["单双排"].TotalGames);
    }

    [Fact]
    public void TotalGames_AnUnknownRecord_IsZero()
    {
        Assert.Equal(0, RankedStats.FromJson(Payload(solo: Queue(losses: null)))["单双排"].TotalGames);
    }

    [Fact]
    public void WinRate_AKnownRecord_IsTheShareOfWins()
    {
        Assert.Equal(60, RankedStats.FromJson(Payload(solo: Queue(wins: 30, losses: 20)))["单双排"].WinRate, 3);
    }

    [Fact]
    public void WinRate_IsRoundedToTwoDecimals()
    {
        // Two wins in three is 66.666…, which the page shows as 66.67.
        Assert.Equal(66.67, RankedStats.FromJson(Payload(solo: Queue(wins: 2, losses: 1)))["单双排"].WinRate, 3);
    }

    [Fact]
    public void WinRate_NoGamesAtAll_IsZero()
    {
        // A record of no games is known but has no rate; dividing by it would be a mistake.
        Assert.Equal(0, RankedStats.FromJson(Payload(solo: Queue(wins: 0, losses: 0)))["单双排"].WinRate);
    }

    [Fact]
    public void WinRate_NoLosses_IsOneHundred()
    {
        // Five wins and no losses is every game won, not "no games": the total is what decides.
        Assert.Equal(100, RankedStats.FromJson(Payload(solo: Queue(wins: 5, losses: 0)))["单双排"].WinRate, 3);
    }

    // ---- the wording ----

    [Fact]
    public void WinRateDisplay_AKnownRecord_NamesTheRateAsAPercentage()
    {
        // A whole-number rate keeps this free of a decimal separator, which follows the machine's language.
        Assert.Equal("60%", RankedStats.FromJson(Payload(solo: Queue(wins: 30, losses: 20)))["单双排"].WinRateDisplay);
    }

    [Fact]
    public void WinRateDisplay_AnUnknownRecord_IsADash()
    {
        Assert.Equal("—", RankedStats.FromJson(Payload(solo: Queue(losses: null)))["单双排"].WinRateDisplay);
    }

    [Fact]
    public void TotalGamesDisplay_AKnownRecord_CountsThem()
    {
        Assert.Equal(
            "50 场",
            RankedStats.FromJson(Payload(solo: Queue(wins: 30, losses: 20)))["单双排"].TotalGamesDisplay
        );
    }

    [Fact]
    public void TotalGamesDisplay_AnUnknownRecord_SaysUnknown()
    {
        Assert.Equal("未知", RankedStats.FromJson(Payload(solo: Queue(losses: null)))["单双排"].TotalGamesDisplay);
    }

    [Fact]
    public void LossesDisplay_ReportsTheLossCount()
    {
        Assert.Equal("20 场", RankedStats.FromJson(Payload(solo: Queue(wins: 30, losses: 20)))["单双排"].LossesDisplay);
    }

    // ---- the tier ----

    [Theory]
    [InlineData("IRON", "坚韧黑铁 IV")]
    [InlineData("BRONZE", "英勇黄铜 IV")]
    [InlineData("SILVER", "不屈白银 IV")]
    [InlineData("GOLD", "荣耀黄金 IV")]
    [InlineData("PLATINUM", "华贵铂金 IV")]
    [InlineData("EMERALD", "流光翡翠 IV")]
    [InlineData("DIAMOND", "璀璨钻石 IV")]
    public void FormattedTier_ADivisionTier_NamesBothInChinese(string tier, string expected)
    {
        Assert.Equal(expected, RankedStats.FromJson(Payload(solo: Queue(tier, "IV")))["单双排"].FormattedTier);
    }

    [Theory]
    [InlineData("MASTER", "超凡大师")]
    [InlineData("GRANDMASTER", "傲世宗师")]
    [InlineData("CHALLENGER", "最强王者")]
    public void FormattedTier_ATierWithoutDivisions_NamesOnlyTheTier(string tier, string expected)
    {
        // Master and above have no divisions, so one is not printed even if the payload carries something.
        Assert.Equal(expected, RankedStats.FromJson(Payload(solo: Queue(tier, "I")))["单双排"].FormattedTier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NONE")]
    public void FormattedTier_NothingToShow_IsUnranked(string tier)
    {
        Assert.Equal("未定级", RankedStats.FromJson(Payload(solo: Queue(tier, "II")))["单双排"].FormattedTier);
    }

    [Fact]
    public void FormattedTier_ATierThePageDoesNotKnow_IsUnknown()
    {
        Assert.Equal(
            "未知 II",
            RankedStats.FromJson(Payload(solo: Queue("SOMETHING_NEW", "II")))["单双排"].FormattedTier
        );
    }

    [Theory]
    [InlineData("NA", "")]
    [InlineData("", "")]
    public void FormattedTier_ADivisionThePayloadCannotGive_IsLeftOut(string division, string tail)
    {
        // "NA" and an empty division both mean there is none to print.
        Assert.Equal(
            ("荣耀黄金" + (tail.Length == 0 ? " " : " " + tail)).TrimEnd(),
            RankedStats.FromJson(Payload(solo: Queue("GOLD", division)))["单双排"].FormattedTier.TrimEnd()
        );
    }

    // ---- what the page calls a past tier ----

    [Fact]
    public void HighestTierDisplay_ATierFromAPastSeason_IsNamedTheSameWay()
    {
        JObject queue = Queue();
        queue["highestTier"] = "PLATINUM";
        queue["highestDivision"] = "I";

        Assert.Equal("华贵铂金 I", RankedStats.FromJson(Payload(solo: queue))["单双排"].HighestTierDisplay);
    }

    [Fact]
    public void HighestTierDisplay_NoPastTier_IsADash()
    {
        Assert.Equal("—", RankedStats.FromJson(Payload(solo: Queue()))["单双排"].HighestTierDisplay);
    }

    [Fact]
    public void PreviousSeasonDisplay_ATierFromLastSeason_IsNamedTheSameWay()
    {
        JObject queue = Queue();
        queue["previousSeasonEndTier"] = "DIAMOND";
        queue["previousSeasonEndDivision"] = "III";

        Assert.Equal("璀璨钻石 III", RankedStats.FromJson(Payload(solo: queue))["单双排"].PreviousSeasonDisplay);
    }

    [Fact]
    public void HighestTierOrder_ATierWithADivision_IsItsPlaceInTheLadder()
    {
        // Iron IV is (0 + 1) * 10 + 1; the order only compares past tiers with each other.
        JObject queue = Queue();
        queue["highestTier"] = "IRON";
        queue["highestDivision"] = "IV";

        Assert.Equal(11, RankedStats.FromJson(Payload(solo: queue))["单双排"].HighestTierOrder);
    }

    [Fact]
    public void HighestTierOrder_Challenger_IsTheTopOfIt()
    {
        JObject queue = Queue();
        queue["highestTier"] = "CHALLENGER";
        queue["highestDivision"] = "";

        Assert.Equal(100, RankedStats.FromJson(Payload(solo: queue))["单双排"].HighestTierOrder);
    }

    [Fact]
    public void HighestTierOrder_NoPastTier_IsZero()
    {
        Assert.Equal(0, RankedStats.FromJson(Payload(solo: Queue()))["单双排"].HighestTierOrder);
    }

    // ---- the league points ----

    [Fact]
    public void LeaguePoints_AreReadAsTheyAre()
    {
        Assert.Equal(42, RankedStats.FromJson(Payload(solo: Queue(leaguePoints: 42)))["单双排"].LeaguePoints);
    }

    [Fact]
    public void QueueType_IsReadFromThePayload()
    {
        Assert.Equal("RANKED_SOLO_5x5", RankedStats.FromJson(Payload(solo: Queue()))["单双排"].QueueType);
    }
}
