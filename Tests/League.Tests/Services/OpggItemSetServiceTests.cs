using System.Net;
using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The OP.GG item-set export: the most used core, the later items it does not already contain, repeated purchases,
/// the mode, map and title the client shows, and the write that verifies itself against the client. The expected
/// values come from the check in Tests/StatisticsChecks/ItemSetChecks.cs, which this class replaces. Nothing leaves
/// the process: the arammeta input is the capture under TestData/AramMeta, and the client is the stub below.
/// </summary>
public class OpggItemSetServiceTests
{
    private const string SummonerPath = "/lol-summoner/v1/current-summoner";
    private const string SetsPath = "/lol-item-sets/v1/item-sets/7/sets";
    private const string TwoCores =
        "{\"core_items\":[{\"ids\":[1,2,3],\"pick_rate\":0.2},{\"ids\":[1,2,4],\"pick_rate\":0.7}],"
        + "\"last_items\":[{\"ids\":[1]},{\"ids\":[3]},{\"ids\":[4]},{\"ids\":[4]},{\"ids\":[5]}]}";

    [Theory]
    [InlineData("{\"ids\":[1,2,3]}", new[] { 1, 2, 3 })]
    [InlineData("{\"ids\":[1,-2,0,3]}", new[] { 1, 3 })]
    [InlineData("{\"ids\":[-1,0]}", new int[0])]
    [InlineData("{\"ids\":[]}", new int[0])]
    [InlineData("{\"ids\":\"1\"}", new int[0])]
    [InlineData("{\"other\":1}", new int[0])]
    public void Ids_IdentifiersThatAreNotPositive_AreDropped(string row, int[] expected)
    {
        int[] ids = OpggItemSetService.Ids(JObject.Parse(row));

        Assert.Equal(expected, ids);
    }

    [Fact]
    public void Ids_MissingRow_ReturnsEmpty()
    {
        Assert.Empty(OpggItemSetService.Ids(null));
    }

    [Fact]
    public void Options_HigherPickRate_ComesFirst()
    {
        JObject data = Data(TwoCores);

        JObject[] options = OpggItemSetService.Options(data, "core_items");

        Assert.Equal(new[] { 1, 2, 4 }, OpggItemSetService.Ids(options[0]));
        Assert.Equal(new[] { 1, 2, 3 }, OpggItemSetService.Ids(options[1]));
    }

    [Fact]
    public void Options_RowWithoutPickRate_ComesLast()
    {
        JObject data = Data(
            "{\"core_items\":[{\"ids\":[1]},{\"ids\":[2],\"pick_rate\":0.2},{\"ids\":[3],\"pick_rate\":0.7}]}"
        );

        JObject[] options = OpggItemSetService.Options(data, "core_items");

        Assert.Equal(new[] { 3, 2, 1 }, FirstIds(options));
    }

    [Fact]
    public void Options_RowsWithoutIdentifiers_AreDropped()
    {
        JObject data = Data("{\"core_items\":[{\"ids\":[1]},{\"ids\":[]},{\"ids\":[-1]},{\"pick_rate\":1},7]}");

        JObject[] options = OpggItemSetService.Options(data, "core_items");

        Assert.Equal(new[] { 1 }, FirstIds(options));
        Assert.Single(options);
    }

    [Fact]
    public void Options_UnknownKey_ReturnsEmpty()
    {
        Assert.Empty(OpggItemSetService.Options(Data("{\"core_items\":[]}"), "boots"));
    }

    [Theory]
    [InlineData(0, new[] { 3, 5 })]
    [InlineData(1, new[] { 4, 5 })]
    public void Later_IdentifiersAlreadyInTheSelectedCore_AreExcluded(int core, int[] expected)
    {
        JObject data = Data(TwoCores);

        int[] later = OpggItemSetService.Later(data, OpggItemSetService.Options(data, "core_items")[core]);

        Assert.Equal(expected, later);
    }

    [Fact]
    public void Later_RepeatedIdentifiers_AppearOnce()
    {
        JObject data = Data("{\"last_items\":[{\"ids\":[5,5]},{\"ids\":[5]}]}");

        int[] later = OpggItemSetService.Later(data, Row(1));

        Assert.Equal(new[] { 5 }, later);
    }

    [Fact]
    public void Later_CoreWithoutIdentifiers_ExcludesNothing()
    {
        JObject data = Data("{\"last_items\":[{\"ids\":[3]},{\"ids\":[4]}]}");

        int[] later = OpggItemSetService.Later(data, Row(0));

        Assert.Equal(new[] { 3, 4 }, later);
    }

    [Fact]
    public void Later_MissingLastItems_ReturnsEmpty()
    {
        Assert.Empty(OpggItemSetService.Later(Data("{}"), Row(1)));
    }

    [Theory]
    [InlineData("opgg")]
    [InlineData("OPGG")]
    [InlineData("AramMeta")]
    [InlineData("")]
    public void Create_SourceOutsideTheAllowedPair_Throws(string source)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null, source)
        );

        Assert.Equal("source", error.ParamName);
    }

    [Theory]
    [InlineData("{\"ids\":[]}")]
    [InlineData("{\"ids\":[0,-3]}")]
    [InlineData("{}")]
    public void Create_CoreWithoutUsableIdentifiers_Throws(string core)
    {
        Assert.Throws<InvalidDataException>(() =>
            OpggItemSetService.Create(Build(), "Ahri", JObject.Parse(core), null, null)
        );
    }

    [Fact]
    public void Create_RepeatedStarterPurchase_IsCountedOnce()
    {
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1, 2, 3), Row(1056, 2003, 2003), Row(3020));

        Assert.Equal(new[] { 1056, 2003 }, BlockIds(itemSet, 0));
        Assert.Equal(new[] { 1, 2 }, BlockCounts(itemSet, 0));
    }

    [Fact]
    public void Create_StarterBootsCoreAndLater_AreOrderedBlocks()
    {
        JObject data = Data(TwoCores);
        JObject core = OpggItemSetService.Options(data, "core_items")[0];

        JObject itemSet = OpggItemSetService.Create(Build(data), "Ahri", core, Row(1056), Row(3020));

        Assert.Equal(new[] { "出门装", "鞋子", "核心装备", "后续可选装备" }, BlockTypes(itemSet));
        Assert.Equal(new[] { 3, 5 }, BlockIds(itemSet, 3));
    }

    [Fact]
    public void Create_WithoutStarterAndBoots_KeepsOnlyTheCoreBlocks()
    {
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        Assert.Equal(new[] { "核心装备" }, BlockTypes(itemSet));
    }

    [Fact]
    public void Create_AnotherCoreForTheSameQuery_KeepsTheUid()
    {
        JObject data = Data(TwoCores);
        JObject[] options = OpggItemSetService.Options(data, "core_items");

        JObject itemSet = OpggItemSetService.Create(Build(data), "Ahri", options[0], null, null);
        JObject alternate = OpggItemSetService.Create(Build(data), "Ahri", options[1], null, null);

        Assert.Equal((string?)itemSet["uid"], (string?)alternate["uid"]);
    }

    [Fact]
    public void Create_AnotherMode_UsesAnotherUidAndMap()
    {
        JObject ranked = OpggItemSetService.Create(Build(mode: "ranked"), "Ahri", Row(1), null, null);
        JObject aram = OpggItemSetService.Create(Build(mode: "aram"), "Ahri", Row(1), null, null);

        Assert.NotEqual((string?)ranked["uid"], (string?)aram["uid"]);
        Assert.Equal(new[] { 12 }, Maps(aram));
    }

    [Fact]
    public void Create_AramMetaSource_UsesAnotherUidThanOpgg()
    {
        JObject opgg = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);
        JObject arammeta = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null, "arammeta");

        Assert.NotEqual((string?)opgg["uid"], (string?)arammeta["uid"]);
    }

    [Theory]
    [InlineData("ranked", new[] { 11 })]
    [InlineData("urf", new[] { 11 })]
    [InlineData("aram", new[] { 12 })]
    [InlineData("mayhem", new[] { 12 })]
    [InlineData("nexus_blitz", new[] { 21 })]
    [InlineData("arena", new[] { 30 })]
    [InlineData("swiftplay", new int[0])]
    public void Create_Mode_SelectsTheClientMap(string mode, int[] expected)
    {
        JObject itemSet = OpggItemSetService.Create(Build(mode: mode), "Ahri", Row(1), null, null);

        Assert.Equal(expected, Maps(itemSet));
    }

    [Theory]
    [InlineData("top", "[OP.GG] Ahri·峡谷·上路")]
    [InlineData("jungle", "[OP.GG] Ahri·峡谷·打野")]
    [InlineData("mid", "[OP.GG] Ahri·峡谷·中路")]
    [InlineData("adc", "[OP.GG] Ahri·峡谷·下路")]
    [InlineData("support", "[OP.GG] Ahri·峡谷·辅助")]
    [InlineData("none", "[OP.GG] Ahri·峡谷")]
    public void Create_RankedMode_NamesTheLane(string position, string title)
    {
        JObject itemSet = OpggItemSetService.Create(
            Build(mode: "ranked", position: position),
            "Ahri",
            Row(1),
            null,
            null
        );

        Assert.Equal(title, (string?)itemSet["title"]);
    }

    [Theory]
    [InlineData("aram", "[OP.GG] Ahri·大乱斗")]
    [InlineData("mayhem", "[OP.GG] Ahri·海克斯大乱斗")]
    [InlineData("urf", "[OP.GG] Ahri·无限火力")]
    [InlineData("arena", "[OP.GG] Ahri·竞技场")]
    [InlineData("nexus_blitz", "[OP.GG] Ahri·极限闪击")]
    [InlineData("swiftplay", "[OP.GG] Ahri·swiftplay")]
    public void Create_ModeOtherThanRanked_DropsTheLane(string mode, string title)
    {
        JObject itemSet = OpggItemSetService.Create(Build(mode: mode, position: "mid"), "Ahri", Row(1), null, null);

        Assert.Equal(title, (string?)itemSet["title"]);
    }

    [Fact]
    public void Create_AramMetaSource_NamesTheSetForItsSource()
    {
        JObject itemSet = OpggItemSetService.Create(Build(mode: "mayhem"), "Ahri", Row(1), null, null, "arammeta");

        Assert.Equal("[arammeta] Ahri·海克斯大乱斗", (string?)itemSet["title"]);
    }

    [Fact]
    public void FromAramMeta_DetailNotLoaded_Throws()
    {
        var page = new AramMetaPage(AramMetaIndex.Parse(AramMetaFixture.Index()), null, false, DateTime.UtcNow);

        Assert.Throws<InvalidDataException>(() => OpggItemSetService.FromAramMeta(page, AramMetaFixture.Ahri));
    }

    [Fact]
    public void FromAramMeta_Capture_DescribesAMayhemBuild()
    {
        OpggBuild build = OpggItemSetService.FromAramMeta(Page(), AramMetaFixture.Ahri);

        Assert.Equal("global", build.Query.Region);
        Assert.Equal("mayhem", build.Query.Mode);
        Assert.Equal("all", build.Query.Tier);
        Assert.Equal("", build.Query.Position);
        Assert.Equal(AramMetaFixture.Ahri, build.Query.ChampionId);
        Assert.Equal(AramMetaFixture.Patch, build.Version);
        Assert.Empty(build.Stats);
        Assert.Empty(build.Runes);
    }

    [Theory]
    [InlineData("items", "core_items")]
    [InlineData("singleItems", "last_items")]
    [InlineData("boots", "boots")]
    public void FromAramMeta_Capture_MapsEveryCategoryToItsOptionKey(string category, string key)
    {
        AramMetaPage page = Page();

        OpggBuild build = OpggItemSetService.FromAramMeta(page, AramMetaFixture.Ahri);

        int expected = AramMetaRecommendation.Read(page, AramMetaFixture.Ahri, category).Length;
        Assert.Equal(expected, ((JArray)build.Data[key]!).Count);
        Assert.NotEmpty(OpggItemSetService.Options(build.Data, key));
    }

    [Fact]
    public void Create_AramMetaBuild_KeepsSourceMapAndCoreWithoutStarter()
    {
        OpggBuild build = OpggItemSetService.FromAramMeta(Page(), AramMetaFixture.Ahri);
        JObject core = OpggItemSetService.Options(build.Data, "core_items")[0];

        JObject itemSet = OpggItemSetService.Create(
            build,
            "Ahri",
            core,
            null,
            OpggItemSetService.Options(build.Data, "boots")[0],
            "arammeta"
        );

        Assert.Equal("[arammeta] Ahri·海克斯大乱斗", (string?)itemSet["title"]);
        Assert.Equal(new[] { 12 }, Maps(itemSet));
        Assert.Equal(new[] { "鞋子", "核心装备", "后续可选装备" }, BlockTypes(itemSet));
        Assert.Empty(OpggItemSetService.Later(build.Data, core).Intersect(OpggItemSetService.Ids(core)));
    }

    [Fact]
    public async Task SaveAsync_VerifiedWrite_ReadsTheAccountWritesAndConfirms()
    {
        using var server = new ItemSetServer();
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None);

        Assert.Equal(
            new[] { ("GET", SummonerPath), ("GET", SetsPath), ("PUT", SetsPath), ("GET", SetsPath) },
            server.Requests
        );
        Assert.Equal(new[] { 1 }, BlockIds(Set(server, 1), 0));
    }

    [Fact]
    public async Task SaveAsync_VerifiedWrite_KeepsPersonalSetsAndAccountMetadata()
    {
        using var server = new ItemSetServer();
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None);

        Assert.Equal(2, Sets(server).Length);
        Assert.Equal("personal", (string?)Sets(server)[0]["uid"]);
        Assert.Equal(123, (int?)Sets(server)[0]["custom"]);
        Assert.Equal(123, (int?)server.Document["accountId"]);
        Assert.Equal((string?)itemSet["uid"], (string?)Sets(server)[1]["uid"]);
    }

    [Fact]
    public async Task SaveAsync_SameUidWrittenTwice_ReplacesInsteadOfAccumulating()
    {
        using var server = new ItemSetServer();
        JObject first = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);
        JObject second = OpggItemSetService.Create(Build(), "Ahri", Row(2), null, null);

        await OpggItemSetService.SaveAsync(server.Client, first, CancellationToken.None);
        await OpggItemSetService.SaveAsync(server.Client, second, CancellationToken.None);

        JObject stored = Assert.Single(Sets(server), row => (string?)row["uid"] == (string?)first["uid"]);
        Assert.Equal(2, Sets(server).Length);
        Assert.Equal(new[] { 2 }, BlockIds(stored, 0));
    }

    [Fact]
    public async Task SaveAsync_ExistingSetOfTheSameSourceFamily_IsReplaced()
    {
        JObject itemSet = OpggItemSetService.Create(Build(mode: "mayhem"), "Ahri", Row(1), null, null, "arammeta");
        using var server = new ItemSetServer(
            sets:
            [
                new JObject
                {
                    ["uid"] = itemSet["uid"],
                    ["title"] = "[arammeta] Ahri·海克斯大乱斗",
                    ["blocks"] = new JArray(),
                },
            ]
        );

        await OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None);

        Assert.Equal(2, Sets(server).Length);
        Assert.Equal(new[] { 1 }, BlockIds(Set(server, 1), 0));
    }

    [Fact]
    public async Task SaveAsync_ExistingSetOfAnotherSource_ThrowsAndLeavesItAlone()
    {
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);
        using var server = new ItemSetServer(
            sets:
            [
                new JObject
                {
                    ["uid"] = itemSet["uid"],
                    ["title"] = "My set",
                    ["blocks"] = new JArray(),
                },
            ]
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None)
        );

        Assert.Equal(0, server.Writes);
        Assert.Equal("My set", (string?)Set(server, 1)["title"]);
    }

    [Fact]
    public async Task SaveAsync_DocumentWithoutItemSets_ThrowsBeforeWriting()
    {
        using var server = new ItemSetServer(malformed: true);
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None)
        );

        Assert.Equal(0, server.Writes);
        Assert.Single(Sets(server));
    }

    [Fact]
    public async Task SaveAsync_SummonerWithoutIdentity_ThrowsBeforeWriting()
    {
        using var server = new ItemSetServer(summonerId: 0);
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None)
        );

        Assert.Equal(new[] { ("GET", SummonerPath) }, server.Requests);
    }

    [Fact]
    public async Task SaveAsync_RejectedWrite_ThrowsWithoutPublishingTheSet()
    {
        using var server = new ItemSetServer(rejected: true);
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None)
        );

        Assert.Equal(1, server.Writes);
        Assert.Single(Sets(server));
    }

    [Fact]
    public async Task SaveAsync_WriteTheClientNeverConfirms_Throws()
    {
        using var server = new ItemSetServer(confirmed: false);
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, CancellationToken.None)
        );

        Assert.Equal(1, server.Writes);
    }

    [Fact]
    public async Task SaveAsync_TokenAlreadyCancelled_ThrowsBeforeAnyRequest()
    {
        using var cancelled = new CancellationTokenSource();
        using var server = new ItemSetServer();
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, cancelled.Token)
        );

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task SaveAsync_CancelledAfterTheRead_PublishesNothingAndReleasesTheGate()
    {
        using var cancelled = new CancellationTokenSource();
        using var server = new ItemSetServer(cancelAfterRead: cancelled);
        JObject itemSet = OpggItemSetService.Create(Build(), "Ahri", Row(1), null, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpggItemSetService.SaveAsync(server.Client, itemSet, cancelled.Token)
        );

        Assert.Equal(0, server.Writes);
        Assert.Single(Sets(server));

        // The cancelled call must have released the gate; the bound only keeps a leak from hanging the run.
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await OpggItemSetService.SaveAsync(server.Client, itemSet, bound.Token);

        Assert.Equal(1, server.Writes);
    }

    private static JObject Data(string json) => JObject.Parse(json);

    private static JObject Row(params int[] ids) => new() { ["ids"] = new JArray(ids) };

    private static OpggBuild Build(JObject? data = null, string mode = "ranked", string position = "mid") =>
        new(
            new OpggQuery("global", mode, "emerald_plus", position, 103),
            "16.17",
            data ?? new JObject(),
            new JObject(),
            []
        );

    private static AramMetaPage Page() =>
        new(AramMetaIndex.Parse(AramMetaFixture.Index()), AramMetaFixture.Detail(), false, DateTime.UtcNow);

    private static JObject[] Sets(ItemSetServer server) =>
        ((JArray)server.Document["itemSets"]!).OfType<JObject>().ToArray();

    private static JObject Set(ItemSetServer server, int index) => Sets(server)[index];

    private static int[] FirstIds(JObject[] options) => options.Select(row => OpggItemSetService.Ids(row)[0]).ToArray();

    private static string[] BlockTypes(JObject itemSet) =>
        ((JArray)itemSet["blocks"]!).Select(block => (string)block["type"]!).ToArray();

    private static int[] BlockIds(JObject itemSet, int block) =>
        ((JArray)itemSet["blocks"]![block]!["items"]!).Select(item => (int)item["id"]!).ToArray();

    private static int[] BlockCounts(JObject itemSet, int block) =>
        ((JArray)itemSet["blocks"]![block]!["items"]!).Select(item => (int)item["count"]!).ToArray();

    private static int[] Maps(JObject itemSet) =>
        ((JArray)itemSet["associatedMaps"]!).Select(map => (int)map).ToArray();

    /// <summary>
    /// The client's item-set service kept in memory: one personal set, the summoner identity, and the writes the
    /// client accepted. <c>confirmed</c> false makes the client keep its old document, so a write is never confirmed;
    /// <c>cancelAfterRead</c> is cancelled right after the document is read, which stops the write that follows.
    /// </summary>
    private sealed class ItemSetServer : IDisposable
    {
        private const string Personal = "{\"uid\":\"personal\",\"title\":\"My set\",\"custom\":123,\"blocks\":[]}";

        private readonly long _summonerId;
        private readonly bool _malformed;
        private readonly bool _rejected;
        private readonly bool _confirmed;
        private readonly CancellationTokenSource? _cancelAfterRead;

        public ItemSetServer(
            long summonerId = 7,
            bool malformed = false,
            bool rejected = false,
            bool confirmed = true,
            CancellationTokenSource? cancelAfterRead = null,
            params JObject[] sets
        )
        {
            _summonerId = summonerId;
            _malformed = malformed;
            _rejected = rejected;
            _confirmed = confirmed;
            _cancelAfterRead = cancelAfterRead;
            Document = new JObject
            {
                ["accountId"] = 123,
                ["timestamp"] = 5,
                ["itemSets"] = new JArray(sets.Prepend(JObject.Parse(Personal)).Select(row => row.DeepClone())),
            };
            Client = new HttpClient(new Handler(SendAsync)) { BaseAddress = new Uri("https://test.invalid") };
        }

        public JObject Document { get; private set; }

        public HttpClient Client { get; }

        public List<(string Method, string Path)> Requests { get; } = new();

        public int Writes { get; private set; }

        public void Dispose() => Client.Dispose();

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method.Method, path));
            if (path == SummonerPath)
            {
                return Json(new JObject { ["summonerId"] = _summonerId });
            }

            if (path != SetsPath)
            {
                return new(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Get)
            {
                if (_cancelAfterRead != null)
                {
                    await _cancelAfterRead.CancelAsync();
                }

                return Json(_malformed ? new JObject() : Document);
            }

            if (request.Method != HttpMethod.Put)
            {
                return new(HttpStatusCode.MethodNotAllowed);
            }

            Writes++;
            string payload = await request.Content!.ReadAsStringAsync(token);
            if (_rejected)
            {
                return new(HttpStatusCode.Forbidden);
            }

            if (_confirmed)
            {
                Document = JObject.Parse(payload);
            }

            return Json(new JObject());
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            return await send(request, token);
        }
    }

    private static HttpResponseMessage Json(JObject value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };
}
