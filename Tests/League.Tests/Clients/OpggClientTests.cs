using System.Net;
using League.Clients;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The OP.GG client: which queries it accepts, where it asks for them, and how it treats what comes back.
///
/// A handler is injected, so nothing leaves the process. The rules pinned here are the ones with consequences: the
/// filter list is checked before anything is sent, a ranked query must name a lane, an answer is remembered for
/// fifteen minutes unless a refresh is asked for, a retryable status gets one more try, and a 404 or 422 is reported
/// as "no data for this combination" rather than as a broken connection.
/// </summary>
public class OpggClientTests : IDisposable
{
    private readonly TempFolder _folder = new("opggclient");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private static OpggQuery Query(
        string region = "kr",
        string mode = "ranked",
        string tier = "gold_plus",
        string position = "mid",
        int championId = 103
    ) => new(region, mode, tier, position, championId);

    /// <summary>A handler that answers from a list of statuses, then repeats the last one.</summary>
    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        private int _call;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            HttpStatusCode status = statuses[Math.Min(_call++, statuses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(Body) });
        }

        public static string Body { get; set; } = "{}";
    }

    private static string BuildBody(int championId = 103) =>
        new JObject
        {
            ["meta"] = new JObject { ["version"] = "15.1" },
            ["data"] = new JObject
            {
                ["summary"] = new JObject
                {
                    ["id"] = championId,
                    // A ranked build is read from the per-lane list; the other modes read the average instead.
                    ["average_stats"] = new JObject { ["win_rate"] = 0.5, ["pick_rate"] = 0.1 },
                    ["positions"] = new JArray(
                        new JObject
                        {
                            ["name"] = "mid",
                            ["stats"] = new JObject { ["win_rate"] = 0.5, ["pick_rate"] = 0.1 },
                        },
                        new JObject
                        {
                            ["name"] = "top",
                            ["stats"] = new JObject { ["win_rate"] = 0.4, ["pick_rate"] = 0.2 },
                        }
                    ),
                },
                ["runes"] = new JArray(),
            },
        }.ToString();

    private static string TiersBody() =>
        new JObject
        {
            ["meta"] = new JObject { ["version"] = "15.1" },
            ["data"] = new JArray(
                new JObject
                {
                    ["id"] = 103,
                    ["positions"] = new JArray(
                        new JObject
                        {
                            ["name"] = "mid",
                            ["stats"] = new JObject { ["win_rate"] = 0.5, ["rank"] = 1 },
                        }
                    ),
                }
            ),
        }.ToString();

    // ---- which queries are accepted ----

    [Theory]
    [InlineData("global")]
    [InlineData("kr")]
    [InlineData("na")]
    [InlineData("euw")]
    public void Validate_AKnownRegion_Passes(string region)
    {
        OpggClient.Validate(Query(region: region));
    }

    [Theory]
    [InlineData("br")]
    [InlineData("")]
    [InlineData("KR")]
    public void Validate_AnUnknownRegion_IsRefused(string region)
    {
        // The list is what the site has; anything else would be a 404 dressed up as a query.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            OpggClient.Validate(Query(region: region))
        );
        Assert.Equal("该模式或筛选条件暂不支持 OP.GG 攻略。", error.Message);
    }

    [Theory]
    [InlineData("ranked")]
    [InlineData("aram")]
    [InlineData("arena")]
    [InlineData("urf")]
    [InlineData("nexus_blitz")]
    public void Validate_AKnownMode_Passes(string mode)
    {
        OpggClient.Validate(Query(mode: mode));
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("")]
    [InlineData("RANKED")]
    public void Validate_AnUnknownMode_IsRefused(string mode)
    {
        Assert.Throws<InvalidOperationException>(() => OpggClient.Validate(Query(mode: mode)));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("ibsg")]
    [InlineData("gold_plus")]
    [InlineData("platinum_plus")]
    [InlineData("emerald_plus")]
    [InlineData("diamond_plus")]
    [InlineData("master")]
    [InlineData("master_plus")]
    [InlineData("grandmaster")]
    [InlineData("challenger")]
    public void Validate_AKnownTier_Passes(string tier)
    {
        OpggClient.Validate(Query(tier: tier));
    }

    [Theory]
    [InlineData("bronze")]
    [InlineData("")]
    [InlineData("GOLD_PLUS")]
    public void Validate_AnUnknownTier_IsRefused(string tier)
    {
        Assert.Throws<InvalidOperationException>(() => OpggClient.Validate(Query(tier: tier)));
    }

    [Theory]
    [InlineData("top")]
    [InlineData("jungle")]
    [InlineData("mid")]
    [InlineData("adc")]
    [InlineData("support")]
    public void Validate_AKnownLane_PassesForARankedQuery(string position)
    {
        OpggClient.Validate(Query(position: position));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("MID")]
    public void Validate_ALaneThatIsNotOneOfTheFive_IsRefusedForARankedQuery(string position)
    {
        // A ranked query always names a lane, and the name is lower case; normalising is what the caller does first.
        Assert.Throws<InvalidOperationException>(() => OpggClient.Validate(Query(position: position)));
    }

    [Theory]
    [InlineData("aram")]
    [InlineData("arena")]
    public void Validate_AModeWithoutLanes_DoesNotCareAboutThePosition(string mode)
    {
        // Only a ranked query names a lane, so a leftover position is not a reason to refuse.
        OpggClient.Validate(Query(mode: mode, position: "whatever"));
    }

    // ---- asking ----

    [Fact]
    public async Task GetBuildAsync_ARankedQuery_AsksForThatChampionInThatLaneAndTier()
    {
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);

        Assert.Contains(
            "/api/kr/champions/ranked/103/mid?tier=gold_plus",
            Assert.Single(handler.Requests).ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task GetBuildAsync_AnArenaQuery_LeavesTheLaneOutOfThePathAndIgnoresTheTier()
    {
        // Arena has no lanes, so the path has one fewer segment; and the query is normalised to the tier "all",
        // because that mode has no tiers to filter by.
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(mode: "arena", position: "none"), refresh: false, CancellationToken.None);

        string request = Assert.Single(handler.Requests).ToString();
        Assert.DoesNotContain("/none", request, StringComparison.Ordinal);
        Assert.Contains("/champions/arena/103", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBuildAsync_AChampionThatWasNotChosen_IsRefusedBeforeAnyRequest()
    {
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetBuildAsync(Query(championId: 0), refresh: false, CancellationToken.None)
        );

        Assert.Equal("请选择英雄。", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetBuildAsync_AnUnsupportedFilter_IsRefusedBeforeAnyRequest()
    {
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetBuildAsync(Query(tier: "bronze"), refresh: false, CancellationToken.None)
        );

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetBuildAsync_ALaneInCapitals_IsLowerCasedFirst()
    {
        // The query is normalised before it is validated, so a capitalised lane is not a reason to refuse.
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(position: "MID"), refresh: false, CancellationToken.None);

        Assert.Contains("/mid", Assert.Single(handler.Requests).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTiersAsync_AsksForTheChampionListOfThatModeAndTier()
    {
        var handler = new CapturingHandler(TiersBody());
        using OpggClient client = new(handler);

        (string version, OpggTier[] rows) = await client.GetTiersAsync(Query(), refresh: false, CancellationToken.None);

        Assert.Equal("15.1", version);
        Assert.Equal("mid", Assert.Single(rows).Position);
        Assert.Contains(
            "/api/kr/champions/ranked?tier=gold_plus",
            Assert.Single(handler.Requests).ToString(),
            StringComparison.Ordinal
        );
    }

    // ---- the cache ----

    [Fact]
    public async Task GetBuildAsync_TheSameQueryTwice_AsksOnce()
    {
        // An answer is remembered for fifteen minutes, so paging through champions does not hammer the site.
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);
        await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetBuildAsync_ARefresh_AsksAgain()
    {
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);
        await client.GetBuildAsync(Query(), refresh: true, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBuildAsync_ADifferentLane_IsADifferentAnswer()
    {
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(position: "mid"), refresh: false, CancellationToken.None);
        await client.GetBuildAsync(Query(position: "top"), refresh: false, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBuildAsync_EachAnswer_IsACopyTheCallerCannotRuin()
    {
        // The cached document is handed out as a clone, so a caller editing it does not edit the cache.
        var handler = new CapturingHandler(BuildBody());
        using OpggClient client = new(handler);

        OpggBuild first = await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);
        ((JObject)first.Stats!)["win_rate"] = 0.99;
        OpggBuild second = await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);

        Assert.NotEqual(0.99, (double)second.Stats!["win_rate"]!, 3);
    }

    // ---- what comes back ----

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task GetBuildAsync_NoDataForThisCombination_IsReportedAsSuch(HttpStatusCode status)
    {
        // A 404 here is not a broken connection: it means the site has nothing for this champion.
        var handler = new SequenceHandler(status);
        SequenceHandler.Body = BuildBody();
        using OpggClient client = new(handler);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetBuildAsync(Query(), refresh: false, CancellationToken.None)
        );

        Assert.Equal("当前英雄、模式或分路暂无可用数据。", error.Message);
    }

    [Fact]
    public async Task GetBuildAsync_AServerError_IsRetriedOnce()
    {
        // The site fails transiently; one more try costs a second and saves a page that would otherwise be blank.
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        SequenceHandler.Body = BuildBody();
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBuildAsync_TooManyRequests_IsAlsoRetriedOnce()
    {
        var handler = new SequenceHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        SequenceHandler.Body = BuildBody();
        using OpggClient client = new(handler);

        await client.GetBuildAsync(Query(), refresh: false, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetBuildAsync_AFailureTwice_RaisesTheRequestFailure()
    {
        // Two tries and no luck: the caller is told the site could not be reached, not that there is no data.
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError);
        SequenceHandler.Body = BuildBody();
        using OpggClient client = new(handler);

        // The second try falls through to the plain status check, so the message is that one rather than the
        // "could not reach the site" wording, which only a caller that never got a response would see.
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetBuildAsync(Query(), refresh: false, CancellationToken.None)
        );

        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>Answers every request with one body and keeps the addresses.</summary>
    private sealed class CapturingHandler(string json) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
