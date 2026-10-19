using System.Net;
using League.Clients;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The champion mastery cache: one list per player, shared in-flight requests, a snapshot on disk, stale-while-refresh
/// and the identity, privacy and failure rules that go with them. Every request goes to a stub handler and every file
/// lives in a temporary folder, so no request leaves the process and the user's own cache directory is never touched.
/// The expected values come from the checks in Tests/StatisticsChecks/ChampionMasteryChecks.cs, which this class
/// replaces.
/// </summary>
public class ChampionMasteryServiceTests
{
    private const string Player = "player";
    private const string Scope = "platform|viewer";
    private const string WaitingMessage = "尚未取得玩家身份，等待对局名单匹配；不代表玩家隐藏了成就。";

    /// <summary>The response of the old check: a duplicated champion, an unlimited level with 64-bit points, a zero row and a row without points.</summary>
    private const string Sample = """
        [{"championId":103,"championLevel":12,"championPoints":186321,"lastPlayTime":1700000000000},
         {"championId":1,"championLevel":123,"championPoints":3000000000},
         {"championId":103,"championLevel":2,"championPoints":100},
         {"championId":2,"championLevel":0,"championPoints":0}, {"championId":4}]
        """;

    [Fact]
    public void TryNormalize_TheSampleResponse_KeepsOneRowPerChampion()
    {
        bool accepted = ChampionMasteryService.TryNormalize(JToken.Parse(Sample), out JArray data);

        Assert.True(accepted);
        Assert.Equal(3, data.Count);
        Assert.Equal(103, (int?)data[0]!["championId"]);
    }

    [Fact]
    public void TryNormalize_ADuplicatedChampion_KeepsTheHigherPointRow()
    {
        const string response = """
            [{"championId":103,"championLevel":2,"championPoints":100},
             {"championId":103,"championLevel":12,"championPoints":186321}]
            """;

        ChampionMasteryService.TryNormalize(JToken.Parse(response), out JArray data);

        JToken row = Assert.Single(data);
        Assert.Equal(103, (int?)row["championId"]);
        Assert.Equal(12, (int?)row["championLevel"]);
        Assert.Equal(186321L, (long?)row["championPoints"]);
    }

    [Fact]
    public void TryNormalize_AnUnlimitedLevelAnd64BitPoints_AreKept()
    {
        ChampionMasteryService.TryNormalize(JToken.Parse(Sample), out JArray data);

        Assert.Equal(123, (int?)data[1]!["championLevel"]);
        Assert.Equal(3000000000L, (long?)data[1]!["championPoints"]);
    }

    [Fact]
    public void TryNormalize_AChampionWithZeroPoints_IsKept()
    {
        ChampionMasteryService.TryNormalize(JToken.Parse(Sample), out JArray data);

        Assert.Equal(2, (int?)data[2]!["championId"]);
        Assert.Equal(0L, (long?)data[2]!["championPoints"]);
    }

    [Fact]
    public void TryNormalize_ARecordWithoutLevelOrPlayTime_KeepsOnlyTheRequiredFields()
    {
        const string response = """[{"championId":1,"championPoints":3000000000}]""";

        ChampionMasteryService.TryNormalize(JToken.Parse(response), out JArray data);

        var row = (JObject)data[0]!;
        Assert.Equal(2, row.Count);
        Assert.Equal(3000000000L, (long?)row["championPoints"]);
    }

    [Fact]
    public void TryNormalize_AnEmptyList_IsAcceptedAsNoStatistics()
    {
        bool accepted = ChampionMasteryService.TryNormalize(new JArray(), out JArray data);

        Assert.True(accepted);
        Assert.Empty(data);
    }

    [Fact]
    public void TryNormalize_NoValue_IsRefusedAndLeavesAnEmptyList()
    {
        bool accepted = ChampionMasteryService.TryNormalize(null, out JArray data);

        Assert.False(accepted);
        Assert.Empty(data);
    }

    [Theory]
    [InlineData("{\"errorCode\":\"NOT_FOUND\"}")]
    [InlineData("\"plain text\"")]
    [InlineData("[1,2]")]
    [InlineData("[{\"championId\":4}]")]
    [InlineData("[{\"championId\":0,\"championPoints\":10}]")]
    [InlineData("[{\"championId\":-1,\"championPoints\":10}]")]
    [InlineData("[{\"championId\":2147483648,\"championPoints\":10}]")]
    [InlineData("[{\"championId\":1,\"championPoints\":-1}]")]
    [InlineData("[{\"championId\":1,\"championPoints\":\"many\"}]")]
    [InlineData("[{\"championId\":1,\"championPoints\":10.5}]")]
    public void TryNormalize_AResponseWithoutUsableRecords_IsRefusedAndLeavesAnEmptyList(string response)
    {
        bool accepted = ChampionMasteryService.TryNormalize(JToken.Parse(response), out JArray data);

        Assert.False(accepted);
        Assert.Empty(data);
    }

    [Theory]
    [InlineData("[{\"championId\":1,\"championPoints\":10,\"championLevel\":-1}]", "championLevel")]
    [InlineData("[{\"championId\":1,\"championPoints\":10,\"championLevel\":2147483648}]", "championLevel")]
    [InlineData("[{\"championId\":1,\"championPoints\":10,\"lastPlayTime\":0}]", "lastPlayTime")]
    [InlineData("[{\"championId\":1,\"championPoints\":10,\"lastPlayTime\":253402300800000}]", "lastPlayTime")]
    public void TryNormalize_AnOptionalValueOutsideItsRange_IsOmitted(string response, string field)
    {
        ChampionMasteryService.TryNormalize(JToken.Parse(response), out JArray data);

        var row = (JObject)data[0]!;
        Assert.Equal(2, row.Count);
        Assert.Null(row[field]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetAsync_AnIdentityThatIsNotPublic_ReportsTheWaitingMessageWithoutARequest(string puuid)
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-identity");

        ChampionMasteryPage page = await Service(() => client, folder.Root).GetAsync(puuid, CancellationToken.None);

        Assert.Equal(WaitingMessage, page.Message);
        Assert.Null(page.Data);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task GetAsync_APuuidThatIsARealGuid_IsQueriedLikeAnyOtherIdentity()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-guid");

        ChampionMasteryPage page = await Service(() => client, folder.Root)
            .GetAsync("1f2c4c8e-0b1d-4b1e-9a4f-3c2b1a0d9e8f", CancellationToken.None);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(3, page.Data?.Count);
    }

    [Fact]
    public async Task GetAsync_ACancelledToken_StopsBeforeAnyRequest()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-cancelled");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(() => client, folder.Root).GetAsync(Player, cancellation.Token)
        );

        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task GetAsync_TheFirstRead_QueriesTheLcuMasteryPathOfThePlayer()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-path");

        await Service(() => client, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Equal("/lol-champion-mastery/v1/player/champion-mastery", stub.LastRequest?.AbsolutePath);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task GetAsync_TheSampleResponse_ExposesTheSelectedChampionsMastery()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-values");

        ChampionMasteryPage page = await Service(() => client, folder.Root).GetAsync(Player, CancellationToken.None);

        PlayerChampionMastery fox = page.ForChampion(103);
        Assert.Equal(12, fox.Level);
        Assert.Equal(186321L, fox.Points);
        Assert.False(fox.Stale);
    }

    [Fact]
    public async Task GetAsync_TheSelectedChampion_ShowsCompactPointsAndTheExactTooltip()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-text");

        ChampionMasteryPage page = await Service(() => client, folder.Root).GetAsync(Player, CancellationToken.None);

        PlayerChampionMastery fox = page.ForChampion(103);
        Assert.Contains("18.6万", fox.Text);
        Assert.Contains("186,321", fox.Description);
    }

    [Fact]
    public async Task GetAsync_ASecondReadWithinTheLifetime_IsServedFromTheCachedList()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-memory");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);

        ChampionMasteryPage second = await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(3, second.Data?.Count);
        Assert.False(second.Stale);
    }

    [Fact]
    public async Task GetAsync_AConsumerThatEmptiesTheList_CannotDamageTheCachedCopy()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-copy");
        ChampionMasteryService service = Service(() => client, folder.Root);
        ChampionMasteryPage first = await service.GetAsync(Player, CancellationToken.None);
        first.Data!.RemoveAll();

        ChampionMasteryPage second = await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(186321L, second.ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_AListOlderThanTheLifetime_StaysVisibleWhileItRefreshes()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-expired");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        service.CacheLifetime = TimeSpan.Zero;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        ChampionMasteryPage stale = await service.GetAsync(Player, CancellationToken.None);

        Assert.True(stale.Stale);
        Assert.Equal(186321L, stale.ForChampion(103).Points);

        // An expired list is returned without waiting for the refresh. Joining that refresh as a forced one keeps it
        // from outliving the test.
        Task<ChampionMasteryPage> pending = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();
        await pending;
    }

    [Fact]
    public async Task GetAsync_AManualRefresh_QueriesAgainDespiteTheFreshCache()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-manual");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);

        ChampionMasteryPage refreshed = await service.GetAsync(Player, CancellationToken.None, refresh: true);

        Assert.Equal(2, stub.Calls);
        Assert.False(refreshed.Stale);
    }

    [Fact]
    public async Task GetAsync_AnotherPlatformScope_DoesNotShareThePlayersList()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-scope");
        string scope = "platform-A|viewer";
        var service = new ChampionMasteryService(() => client, () => scope, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        scope = "platform-B|viewer";

        await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task GetAsync_TheScopeChangingWhileTheDiskLoads_RefusesTheOldPlayersList()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-scope-change");
        var scopes = new Queue<string>(["platform-A|viewer", "platform-B|viewer"]);
        var service = new ChampionMasteryService(() => client, scopes.Dequeue, folder.Root);

        ChampionMasteryPage page = await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal("客户端账号或区服已变化，请重新查询。", page.Message);
        Assert.Null(page.Data);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task GetAsync_TheClientReconnectingDuringTheFetch_RefusesTheOldClientsList()
    {
        using var folder = new TempFolder("mastery-reconnect");
        var stub = new MasteryStub();
        using var first = Client(stub);
        using var second = Client(new MasteryStub());
        var clients = new Queue<HttpClient?>([first, second]);
        var service = new ChampionMasteryService(clients.Dequeue, () => Scope, folder.Root);

        ChampionMasteryPage page = await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal("客户端账号或连接已变化，请重新查询。", page.Message);
        Assert.Null(page.Data);
        Assert.Empty(Directory.GetFiles(folder.Root, "*.json"));
    }

    [Fact]
    public async Task GetAsync_TwoConcurrentRefreshesOfOnePlayer_ShareASingleRequest()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-shared");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        Task<ChampionMasteryPage> first = service.GetAsync(Player, CancellationToken.None, refresh: true);
        Task<ChampionMasteryPage> second = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, stub.Calls);

        gate.SetResult();
        ChampionMasteryPage[] pages = await Task.WhenAll(first, second);

        Assert.Equal(2, stub.Calls);
        Assert.Equal(186321L, pages[0].ForChampion(103).Points);
        Assert.Equal(186321L, pages[1].ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_ACancelledReader_KeepsTheSharedRequestRunning()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-waiter");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;
        using var cancellation = new CancellationTokenSource();

        Task<ChampionMasteryPage> waiting = service.GetAsync(Player, cancellation.Token, refresh: true);
        Task<ChampionMasteryPage> shared = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        gate.SetResult();
        ChampionMasteryPage page = await shared;

        Assert.Equal(2, stub.Calls);
        Assert.Equal(186321L, page.ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_ARestartedService_ReadsTheListFromTheSnapshotWithoutARequest()
    {
        using var folder = new TempFolder("mastery-restart");
        using (var writer = Client(new MasteryStub()))
        {
            await Service(() => writer, folder.Root).GetAsync(Player, CancellationToken.None);
        }

        var stub = new MasteryStub();
        using var reader = Client(stub);
        ChampionMasteryPage page = await Service(() => reader, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Equal(0, stub.Calls);
        Assert.Equal(186321L, page.ForChampion(103).Points);
        Assert.False(page.Stale);
    }

    [Fact]
    public async Task GetAsync_TheWrittenSnapshot_HoldsOneNormalizedFileWithoutTheAccountIdentity()
    {
        using var folder = new TempFolder("mastery-snapshot");
        using var client = Client(new MasteryStub());

        await Service(() => client, folder.Root).GetAsync(Player, CancellationToken.None);

        string[] files = Directory.GetFiles(folder.Root, "*.json");
        string text = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Equal(1, JObject.Parse(text)["schema"]?.Value<int>());
        Assert.DoesNotContain(Player, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"schema\":2,\"champions\":[{\"championId\":103,\"championPoints\":1}]}")]
    [InlineData("{\"schema\":1,\"champions\":[{\"championId\":4}]}")]
    [InlineData("{\"schema\":1,\"champions\":[{\"championId\":103,\"championPoints\":1}]}")]
    [InlineData("{\"schema\":1,\"fetchedAt\":0,\"champions\":[{\"championId\":103,\"championPoints\":1}]}")]
    public async Task GetAsync_ADiskSnapshotThatCannotBeTrusted_IsReplacedByAClientResponse(string snapshot)
    {
        using var folder = new TempFolder("mastery-untrusted");
        using (var writer = Client(new MasteryStub()))
        {
            await Service(() => writer, folder.Root).GetAsync(Player, CancellationToken.None);
        }

        await WriteSnapshotAsync(folder, snapshot);
        var stub = new MasteryStub();
        using var reader = Client(stub);

        ChampionMasteryPage page = await Service(() => reader, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(186321L, page.ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_ADiskSnapshotFromTheFuture_IsReplacedByAClientResponse()
    {
        using var folder = new TempFolder("mastery-future");
        using (var writer = Client(new MasteryStub()))
        {
            await Service(() => writer, folder.Root).GetAsync(Player, CancellationToken.None);
        }

        var snapshot = new JObject
        {
            ["schema"] = 1,
            ["fetchedAt"] = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds(),
            ["champions"] = new JArray(new JObject { ["championId"] = 103, ["championPoints"] = 1 }),
        };
        await WriteSnapshotAsync(folder, snapshot.ToString());
        var stub = new MasteryStub();
        using var reader = Client(stub);

        ChampionMasteryPage page = await Service(() => reader, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(186321L, page.ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_ADiskSnapshotLargerThanAMegabyte_IsReplacedByAClientResponse()
    {
        using var folder = new TempFolder("mastery-oversize");
        using (var writer = Client(new MasteryStub()))
        {
            await Service(() => writer, folder.Root).GetAsync(Player, CancellationToken.None);
        }

        await WriteSnapshotAsync(folder, new string('x', (1024 * 1024) + 1));
        var stub = new MasteryStub();
        using var reader = Client(stub);

        ChampionMasteryPage page = await Service(() => reader, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Equal(1, stub.Calls);
        Assert.Equal(186321L, page.ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_AFailedRefresh_KeepsTheOldListWithAStaleStatus()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-stale");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        service.CacheLifetime = TimeSpan.Zero;
        stub.Status = HttpStatusCode.ServiceUnavailable;
        stub.Body = "";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        ChampionMasteryPage stale = await service.GetAsync(Player, CancellationToken.None);
        Task<ChampionMasteryPage> pending = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();
        ChampionMasteryPage failed = await pending;

        Assert.True(stale.Stale);
        Assert.True(failed.Stale);
        Assert.Equal(186321L, failed.ForChampion(103).Points);
        Assert.NotEmpty(failed.Message);
    }

    [Fact]
    public async Task GetAsync_AFailedRefresh_DoesNotRepeatAtEveryPoll()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-backoff");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        service.CacheLifetime = TimeSpan.Zero;
        stub.Status = HttpStatusCode.ServiceUnavailable;
        stub.Body = "";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        await service.GetAsync(Player, CancellationToken.None);
        Task<ChampionMasteryPage> pending = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();
        await pending;

        ChampionMasteryPage polled = await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal(2, stub.Calls);
        Assert.True(polled.Stale);
        Assert.Equal(186321L, polled.ForChampion(103).Points);
    }

    [Fact]
    public async Task GetAsync_TheFirstReadFailing_ReportsNoStatistics()
    {
        var stub = new MasteryStub { Status = HttpStatusCode.ServiceUnavailable, Body = "" };
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-cold-failure");

        ChampionMasteryPage page = await Service(() => client, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Null(page.Data);
        Assert.Equal("数据暂时不可用（HTTP 503），可以重试。", page.Message);
    }

    [Fact]
    public async Task GetAsync_AFailedFirstRead_DoesNotAskAgainWhileTheBackoffHolds()
    {
        var stub = new MasteryStub { Status = HttpStatusCode.ServiceUnavailable, Body = "" };
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-cold-backoff");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);

        await service.GetAsync(Player, CancellationToken.None);

        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task GetAsync_APrivacyDenial_ReportsNoDataWithTheForbiddenMessage()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-denied");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        stub.Status = HttpStatusCode.Forbidden;
        stub.Body = "";

        ChampionMasteryPage denied = await service.GetAsync(Player, CancellationToken.None, refresh: true);

        Assert.Null(denied.Data);
        Assert.Equal("当前玩家资料不允许访问。", denied.Message);
    }

    [Fact]
    public async Task GetAsync_APrivacyDenial_DeletesTheSnapshot()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-denied-disk");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        stub.Status = HttpStatusCode.Forbidden;
        stub.Body = "";

        await service.GetAsync(Player, CancellationToken.None, refresh: true);

        Assert.Empty(Directory.GetFiles(folder.Root, "*.json"));
    }

    [Fact]
    public async Task GetAsync_WithoutAConnectedClient_ReportsTheConnectionMessage()
    {
        using var folder = new TempFolder("mastery-offline");

        ChampionMasteryPage page = await Service(() => null, folder.Root).GetAsync(Player, CancellationToken.None);

        Assert.Null(page.Data);
        Assert.Equal("客户端尚未连接，请登录后重试。", page.Message);
        Assert.Empty(Directory.GetFiles(folder.Root, "*.json"));
    }

    [Fact]
    public async Task Invalidate_AFreshList_StaysVisibleAsStaleUntilTheRefreshArrives()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-invalidate");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        service.Invalidate(Player);
        ChampionMasteryPage stale = await service.GetAsync(Player, CancellationToken.None);

        Assert.True(stale.Stale);
        Assert.Equal(186321L, stale.ForChampion(103).Points);

        // The invalidated list is returned before the refresh finishes. Joining that refresh as a forced one keeps it
        // from outliving the test.
        Task<ChampionMasteryPage> pending = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();
        ChampionMasteryPage refreshed = await pending;

        Assert.False(refreshed.Stale);
    }

    [Fact]
    public async Task Invalidate_AFreshList_MakesTheNextReadQueryTheClientAgain()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-invalidate-read");
        ChampionMasteryService service = Service(() => client, folder.Root);
        await service.GetAsync(Player, CancellationToken.None);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        service.Invalidate(Player);
        await service.GetAsync(Player, CancellationToken.None);
        Task<ChampionMasteryPage> pending = service.GetAsync(Player, CancellationToken.None, refresh: true);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult();
        ChampionMasteryPage refreshed = await pending;

        Assert.Equal(2, stub.Calls);
        Assert.False(refreshed.Stale);
    }

    [Fact]
    public async Task Invalidate_DuringTheFirstRequest_DiscardsTheLateResult()
    {
        var stub = new MasteryStub();
        using var client = Client(stub);
        using var folder = new TempFolder("mastery-late");
        ChampionMasteryService service = Service(() => client, folder.Root);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Gate = gate;

        Task<ChampionMasteryPage> loading = service.GetAsync(Player, CancellationToken.None);
        await stub.Waiting.WaitAsync(TimeSpan.FromSeconds(10));
        service.Invalidate(Player);
        gate.SetResult();

        ChampionMasteryPage page = await loading;

        Assert.Null(page.Data);
        Assert.Equal("英雄成就已更新，请重新查询。", page.Message);
    }

    private static ChampionMasteryService Service(Func<HttpClient?> client, string directory) =>
        new(client, () => Scope, directory);

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://test.invalid/") };

    /// <summary>The one snapshot the service wrote into the temporary folder.</summary>
    private static string Snapshot(string directory) => Directory.GetFiles(directory, "*.json").Single();

    private static async Task WriteSnapshotAsync(TempFolder folder, string content) =>
        await File.WriteAllTextAsync(Snapshot(folder.Root), content);

    /// <summary>Answers every mastery request with the prepared response and counts the calls; nothing leaves the process.</summary>
    private sealed class MasteryStub : HttpMessageHandler
    {
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public string Body { get; set; } = Sample;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>Held open to keep one request in flight while the test inspects the cache.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public Uri? LastRequest { get; private set; }

        /// <summary>Completed once a request waits on <see cref="Gate"/>.</summary>
        public Task Waiting => _waiting.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Interlocked.Increment(ref _calls);
            LastRequest = request.RequestUri;
            if (Gate != null)
            {
                _waiting.TrySetResult();
                await Gate.Task.WaitAsync(token);
            }

            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }
}
