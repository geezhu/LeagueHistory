using System.Net;
using System.Net.Http.Headers;
using League.Clients;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The arammeta client: one statistics snapshot shared by every page, a cached hero detail isolated from its
/// consumers, conditional index requests, and a disk snapshot that survives a restart. Every request goes to a stub
/// handler answering from the captured responses in TestData/AramMeta, and every file lives in a temporary folder,
/// so no request leaves the process. The expected values come from the checks in
/// Tests/StatisticsChecks/AramMetaChecks.cs, which this class replaces.
/// </summary>
public class AramMetaClientTests
{
    [Fact]
    public async Task GetAsync_TheFirstHero_ReadsTheIndexTheDetailAndValidatesTheVersion()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);

        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.Equal(3, handler.Requests);
        Assert.NotNull(page.Detail);
        Assert.False(page.Cached);
    }

    [Fact]
    public async Task GetAsync_AConsumerThatEmptiesTheDetail_CannotDamageTheCache()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);
        AramMetaPage first = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        first.Detail!.RemoveAll();

        AramMetaPage second = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.Equal(3, handler.Requests);
        Assert.True(second.Cached);
        Assert.True(second.Detail!.HasValues);
    }

    [Fact]
    public async Task GetAsync_OfflineWithoutACachedHero_Fails()
    {
        using var client = new AramMetaClient(new StubHandler { Fail = true });

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAsync_OfflineRefresh_KeepsTheCompleteCachedStatistics()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        handler.Fail = true;

        AramMetaPage fallback = await client.GetAsync(AramMetaFixture.Ahri, refresh: true, CancellationToken.None);

        Assert.True(fallback.Cached);
        Assert.True(fallback.UpdateFailed);
        Assert.True(fallback.Detail!.HasValues);
        Assert.Equal(4, handler.Requests);
    }

    [Fact]
    public async Task GetAsync_OfflineRefresh_KeepsEveryAugmentOfTheCachedHero()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        handler.Fail = true;
        AramMetaPage fallback = await client.GetAsync(AramMetaFixture.Ahri, refresh: true, CancellationToken.None);

        int rows = new[] { "kPrismatic", "kGold", "kSilver" }.Sum(category =>
            AramMetaRecommendation.Read(fallback, AramMetaFixture.Ahri, category).Length
        );

        Assert.Equal(129, rows);
    }

    [Fact]
    public async Task GetAsync_AnUnknownHero_SaysThereIsNoStatistics()
    {
        using var client = new AramMetaClient(new StubHandler());

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(999999, refresh: false, CancellationToken.None)
        );

        Assert.Equal("此英雄暂无海克斯统计，可返回英雄榜查看。", failure.Message);
    }

    [Fact]
    public async Task GetAsync_Cancelled_StopsWaitingForTheResponse()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new AramMetaClient(new StubHandler { IndexGate = gate });
        using var cancel = new CancellationTokenSource();

        Task<AramMetaPage> loading = client.GetAsync(0, refresh: false, cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
    }

    [Fact]
    public async Task GetAsync_ADisposedClient_RefusesToLoad()
    {
        var client = new AramMetaClient(new StubHandler());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetAsync_AVersionThatChangesWhileTheDetailLoads_RetriesAgainstTheNewSnapshot()
    {
        var handler = new StubHandler { IndexVersion = "old", ConfirmVersion = "new" };
        using var client = new AramMetaClient(handler);

        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.Equal("new", page.Index.Version);
        Assert.Equal(2, handler.DetailRequests);
    }

    [Fact]
    public async Task GetAsync_AVersionThatKeepsChanging_GivesUp()
    {
        var handler = new StubHandler { IndexVersion = "old", AlwaysConfirmANewVersion = true };
        using var client = new AramMetaClient(handler);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None)
        );

        Assert.Equal("数据正在更新，请稍后刷新。", failure.Message);
    }

    [Fact]
    public async Task GetAsync_ADetailWithoutEquipmentOrSpells_IsRefused()
    {
        var handler = new StubHandler { Detail = new JObject { ["nothing"] = 1 } };
        using var client = new AramMetaClient(handler);

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None)
        );

        Assert.Equal("英雄详情数据不完整。", failure.Message);
    }

    [Fact]
    public async Task GetAsync_OpeningMoreHeroes_ValidatesTheIndexWithoutDownloadingItAgain()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);

        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        await client.GetAsync(157, refresh: false, CancellationToken.None);

        Assert.Equal(1, handler.IndexBodies);
        Assert.Equal(2, handler.NotModified);
    }

    [Fact]
    public async Task GetAsync_AConditionalRefresh_StillDetectsANewVersion()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        handler.IndexVersion = "two";

        AramMetaPage changed = await client.GetAsync(AramMetaFixture.Ahri, refresh: true, CancellationToken.None);

        Assert.Equal("two", changed.Index.Version);
        Assert.Equal(2, handler.IndexBodies);
        Assert.Equal(2, handler.NotModified);
    }

    [Fact]
    public async Task GetAsync_ASlowDetail_ShowsTheSummaryFirst()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler { DetailGate = gate };
        using var client = new AramMetaClient(handler);
        AramMetaPage? preview = null;

        Task<AramMetaPage> loading = client.GetAsync(
            AramMetaFixture.Ahri,
            refresh: false,
            CancellationToken.None,
            page => preview = page
        );
        await handler.DetailStarted.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(preview);
        Assert.Null(preview.Detail);
        Assert.Contains(preview.Index.Champions, champion => champion.Id == AramMetaFixture.Ahri);
        Assert.False(loading.IsCompleted);

        gate.SetResult();
        Assert.NotNull((await loading).Detail);
    }

    [Fact]
    public async Task GetAsync_ACachedHero_RendersOnceWithoutALoadingPreview()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        int previews = 0;

        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None, _ => previews++);

        Assert.Equal(0, previews);
    }

    [Fact]
    public async Task GetAsync_TheDiskSnapshot_IsWrittenAndReusedWithoutNetwork()
    {
        using var folder = new TempFolder("arammeta-disk");
        string path = folder.File("statistics.json");
        using (var writer = new AramMetaClient(new StubHandler(), path))
        {
            await writer.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        }

        var reader = new StubHandler { Fail = true };
        using var client = new AramMetaClient(reader, path);
        AramMetaPage restored = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal(0, reader.Requests);
        Assert.True(restored.Cached);
        Assert.NotNull(restored.Detail);
    }

    [Fact]
    public async Task GetAsync_FromTheDiskSnapshot_RestoresTheHttpValidators()
    {
        using var folder = new TempFolder("arammeta-validators");
        string path = folder.File("statistics.json");
        using (var writer = new AramMetaClient(new StubHandler(), path))
        {
            await writer.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        }

        var lifetime = TimeSpan.FromHours(1);
        var reader = new StubHandler { Fail = true };
        using var client = new AramMetaClient(reader, path, () => lifetime);
        lifetime = TimeSpan.Zero;
        AramMetaPage stale = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.True(stale.UpdateFailed);
        Assert.Equal(['"' + AramMetaFixture.Version + '"'], reader.Validators);
    }

    [Fact]
    public async Task IsCurrent_AFreshPage_CanSkipCopyingTheCache()
    {
        using var client = new AramMetaClient(new StubHandler());

        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.True(client.IsCurrent(page, AramMetaFixture.Ahri));
    }

    [Fact]
    public async Task IsCurrent_AChangedLifetime_ForcesThePageToReload()
    {
        using var folder = new TempFolder("arammeta-lifetime");
        var lifetime = TimeSpan.FromHours(1);
        using var client = new AramMetaClient(new StubHandler(), folder.File("statistics.json"), () => lifetime);
        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        lifetime = TimeSpan.Zero;

        Assert.False(client.IsCurrent(page, AramMetaFixture.Ahri));
    }

    [Fact]
    public async Task IsCurrent_APageForAnotherChampion_IsNotCurrent()
    {
        using var client = new AramMetaClient(new StubHandler());
        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.False(client.IsCurrent(page, 157));
    }

    [Fact]
    public async Task IsCurrent_APageWhoseRefreshFailed_IsNotCurrent()
    {
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler);
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        handler.Fail = true;

        AramMetaPage stale = await client.GetAsync(AramMetaFixture.Ahri, refresh: true, CancellationToken.None);

        Assert.False(client.IsCurrent(stale, AramMetaFixture.Ahri));
    }

    [Fact]
    public async Task IsCurrent_ADisposedClient_IsNeverCurrent()
    {
        var client = new AramMetaClient(new StubHandler());
        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        client.Dispose();

        Assert.False(client.IsCurrent(page, AramMetaFixture.Ahri));
    }

    [Fact]
    public async Task GetAsync_AnIndexWithoutAnETag_ValidatesWithItsLastModifiedTime()
    {
        var handler = new StubHandler { UseLastModified = true };
        using var client = new AramMetaClient(handler);

        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        await client.GetAsync(157, refresh: false, CancellationToken.None);

        Assert.Equal(1, handler.IndexBodies);
        Assert.Equal(2, handler.NotModified);
    }

    [Fact]
    public async Task GetAsync_AFailedRefresh_DoesNotRenewTheStoredFetchTime()
    {
        using var folder = new TempFolder("arammeta-stale");
        var lifetime = TimeSpan.FromHours(1);
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, folder.File("statistics.json"), () => lifetime);
        AramMetaPage fresh = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        lifetime = TimeSpan.Zero;
        handler.Fail = true;

        AramMetaPage stale = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.True(stale.UpdateFailed);
        Assert.Equal(fresh.FetchedAt, stale.FetchedAt);
        Assert.Equal(4, handler.Requests);
    }

    [Fact]
    public async Task GetAsync_AManualRefresh_BypassesFreshDiskData()
    {
        using var folder = new TempFolder("arammeta-manual");
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, folder.File("statistics.json"));
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        await client.GetAsync(AramMetaFixture.Ahri, refresh: true, CancellationToken.None);

        Assert.Equal(6, handler.Requests);
    }

    [Fact]
    public async Task GetAsync_ACorruptDiskSnapshot_IsReplacedWithAValidResponse()
    {
        using var folder = new TempFolder("arammeta-corrupt");
        string path = folder.File("statistics.json");
        await File.WriteAllTextAsync(path, "broken json");
        using var client = new AramMetaClient(new StubHandler(), path);

        AramMetaPage recovered = await client.GetAsync(0, refresh: false, CancellationToken.None);
        await client.FlushAsync();

        Assert.Equal(3, recovered.Index.Champions.Length);
        Assert.Equal(1, JObject.Parse(await File.ReadAllTextAsync(path))["schema"]!.Value<int>());
    }

    [Fact]
    public async Task GetAsync_ADiskSnapshotFromTheFuture_IsIgnored()
    {
        using var folder = new TempFolder("arammeta-future");
        string path = folder.File("statistics.json");
        await WriteSnapshotAsync(path, fetched: DateTime.UtcNow.AddMinutes(5));
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, path);

        AramMetaPage page = await client.GetAsync(0, refresh: false, CancellationToken.None);

        Assert.False(page.Cached);
        Assert.NotEqual(0, handler.Requests);
    }

    [Fact]
    public async Task GetAsync_ADetailStoredUnderAnotherVersion_IsNotReused()
    {
        using var folder = new TempFolder("arammeta-olddetail");
        string path = folder.File("statistics.json");
        await WriteSnapshotAsync(path, fetched: DateTime.UtcNow, detailKey: "another-version:" + AramMetaFixture.Ahri);
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, path);

        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.False(page.Cached);
        Assert.Equal(1, handler.DetailRequests);
    }

    [Fact]
    public async Task GetAsync_ADiskSnapshotOfAnotherSchema_IsIgnored()
    {
        using var folder = new TempFolder("arammeta-schema");
        string path = folder.File("statistics.json");
        await WriteSnapshotAsync(path, fetched: DateTime.UtcNow, schema: 2);
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, path);

        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.False(page.Cached);
        Assert.Equal(1, handler.DetailRequests);
    }

    // The cache keeps at most two hundred heroes; the next one that is not in it starts it over.
    [Fact]
    public async Task GetAsync_MoreThanTwoHundredCachedHeroes_ForgetsTheOldest()
    {
        using var folder = new TempFolder("arammeta-twohundred");
        string path = folder.File("statistics.json");
        await WriteManyDetailsSnapshotAsync(path, 200);
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, path);

        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        await client.GetAsync(157, refresh: false, CancellationToken.None);

        Assert.Equal(2, handler.DetailRequests);
    }

    [Fact]
    public async Task GetAsync_ADiskThatCannotBeWritten_KeepsGoing()
    {
        using var folder = new TempFolder("arammeta-readonly");
        string path = folder.File("statistics.json");
        Directory.CreateDirectory(path);
        using var client = new AramMetaClient(new StubHandler(), path);

        AramMetaPage page = await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        await client.FlushAsync();

        Assert.NotNull(page.Detail);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFiles(folder.Root, "*.tmp"));
    }

    [Fact]
    public async Task GetAsync_TwoPagesWithTheSameSnapshot_ShareOneDownload()
    {
        using var folder = new TempFolder("arammeta-shared");
        string path = folder.File("statistics.json");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler { DetailGate = gate };
        using var first = new AramMetaClient(handler, path);
        using var second = new AramMetaClient(new StubHandler(), Path.Combine(folder.Root, ".", "statistics.json"));

        Task<AramMetaPage> loading = first.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        await handler.DetailStarted.WaitAsync(TimeSpan.FromSeconds(10));
        Task<AramMetaPage> duplicate = second.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        gate.SetResult();
        AramMetaPage[] pages = await Task.WhenAll(loading, duplicate);

        Assert.Equal(3, handler.Requests);
        Assert.True(pages[1].Cached);
    }

    [Fact]
    public async Task GetAsync_CancellingAWaitingPage_DoesNotCancelTheOther()
    {
        using var folder = new TempFolder("arammeta-waiting");
        string path = folder.File("statistics.json");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler { DetailGate = gate };
        using var first = new AramMetaClient(handler, path);
        using var second = new AramMetaClient(new StubHandler(), path);
        Task<AramMetaPage> loading = first.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        await handler.DetailStarted.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancel = new CancellationTokenSource();
        Task<AramMetaPage> waiting = second.GetAsync(157, refresh: false, cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        gate.SetResult();
        Assert.NotNull((await loading).Detail);
    }

    [Fact]
    public async Task GetAsync_ASecondHero_ReusesTheSharedIndex()
    {
        using var folder = new TempFolder("arammeta-secondhero");
        var handler = new StubHandler();
        using var client = new AramMetaClient(handler, folder.File("statistics.json"));
        await client.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        await client.GetAsync(157, refresh: false, CancellationToken.None);

        Assert.Equal(5, handler.Requests);
        Assert.Equal(1, handler.IndexBodies);
    }

    [Fact]
    public async Task Dispose_OnePageOfTwo_KeepsTheOtherPagesCache()
    {
        using var folder = new TempFolder("arammeta-onepage");
        string path = folder.File("statistics.json");
        var first = new AramMetaClient(new StubHandler(), path);
        using var second = new AramMetaClient(
            new StubHandler(),
            Path.Combine(folder.Root, "sub", "..", "statistics.json")
        );
        await first.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        first.Dispose();
        first.Dispose();
        AramMetaPage retained = await second.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);

        Assert.True(retained.Cached);
        Assert.NotNull(retained.Detail);
    }

    [Fact]
    public async Task FlushAsync_ASharedSnapshot_KeepsTheDetailsOfEveryHero()
    {
        using var folder = new TempFolder("arammeta-shareddisk");
        string path = folder.File("statistics.json");
        using (var first = new AramMetaClient(new StubHandler(), path))
        using (var second = new AramMetaClient(new StubHandler(), path))
        {
            await first.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
            await second.GetAsync(157, refresh: false, CancellationToken.None);
            await second.FlushAsync();
        }

        var disk = JObject.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(2, ((JObject)disk["details"]!).Count);
    }

    [Fact]
    public async Task GetAsync_ARestart_RestoresEverySharedHeroWithoutNetwork()
    {
        using var folder = new TempFolder("arammeta-restart");
        string path = folder.File("statistics.json");
        using (var first = new AramMetaClient(new StubHandler(), path))
        {
            await first.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
            await first.GetAsync(157, refresh: false, CancellationToken.None);
        }

        var handler = new StubHandler { Fail = true };
        using var restarted = new AramMetaClient(handler, path);
        AramMetaPage ahri = await restarted.GetAsync(AramMetaFixture.Ahri, refresh: false, CancellationToken.None);
        AramMetaPage yasuo = await restarted.GetAsync(157, refresh: false, CancellationToken.None);

        Assert.NotNull(ahri.Detail);
        Assert.NotNull(yasuo.Detail);
        Assert.Equal(0, handler.Requests);
    }

    private static async Task WriteSnapshotAsync(
        string path,
        DateTime fetched,
        string indexVersion = AramMetaFixture.Version,
        string? detailKey = null,
        int schema = 1
    )
    {
        var index = AramMetaFixture.Index();
        index["detailVersion"] = indexVersion;
        var saved = new JObject
        {
            ["schema"] = schema,
            ["fetchedUtc"] = fetched,
            ["index"] = index,
            ["details"] = new JObject
            {
                [detailKey ?? indexVersion + ":" + AramMetaFixture.Ahri] = AramMetaFixture.Detail(),
            },
        };
        await File.WriteAllTextAsync(path, saved.ToString());
    }

    /// <summary>A snapshot holding <paramref name="count"/> heroes, one of them the second fixture champion.</summary>
    private static async Task WriteManyDetailsSnapshotAsync(string path, int count)
    {
        var index = AramMetaFixture.Index();
        var details = new JObject { [AramMetaFixture.Version + ":157"] = AramMetaFixture.Detail() };
        for (int i = 1; i < count; i++)
        {
            details[AramMetaFixture.Version + ":" + (1000 + i)] = AramMetaFixture.Detail();
        }

        var saved = new JObject
        {
            ["schema"] = 1,
            ["fetchedUtc"] = DateTime.UtcNow,
            ["index"] = index,
            ["details"] = details,
        };
        await File.WriteAllTextAsync(path, saved.ToString());
    }

    /// <summary>Answers from the captured responses; a request never leaves the process.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _detailStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _confirmations;

        /// <summary>The version the index reports; changing it is what a new snapshot looks like.</summary>
        public string IndexVersion { get; set; } = AramMetaFixture.Version;

        /// <summary>Adopted after each detail request, for an index that moves on mid-load.</summary>
        public string? ConfirmVersion { get; set; }

        /// <summary>Moves the index on after every detail request, so no version is ever confirmed.</summary>
        public bool AlwaysConfirmANewVersion { get; set; }

        public JObject? Detail { get; set; }

        /// <summary>Answers without an entity tag, dating the index instead.</summary>
        public bool UseLastModified { get; set; }

        public bool Fail { get; set; }

        public TaskCompletionSource? IndexGate { get; set; }

        public TaskCompletionSource? DetailGate { get; set; }

        public int Requests { get; private set; }

        public int IndexBodies { get; private set; }

        public int NotModified { get; private set; }

        public int DetailRequests { get; private set; }

        public List<string> Validators { get; } = [];

        /// <summary>The validator the index answers with; an HTTP entity tag is quoted.</summary>
        private string Tag => "\"" + IndexVersion + "\"";

        /// <summary>The date the index answers with when it sends no entity tag.</summary>
        private DateTimeOffset LastModified => new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

        public Task DetailStarted => _detailStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Requests++;
            Validators.AddRange(request.Headers.IfNoneMatch.Select(tag => tag.Tag));
            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            return request.RequestUri!.AbsolutePath.EndsWith("tier-list.json", StringComparison.Ordinal)
                ? await IndexAsync(request, token)
                : await DetailAsync(token);
        }

        private async Task<HttpResponseMessage> IndexAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (IndexGate != null)
            {
                await IndexGate.Task.WaitAsync(token);
            }

            if (UseLastModified)
            {
                if (request.Headers.IfModifiedSince == LastModified)
                {
                    NotModified++;
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }
            }
            else if (request.Headers.IfNoneMatch.Any(value => value.Tag == Tag))
            {
                NotModified++;
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            IndexBodies++;
            JObject data = AramMetaFixture.Index();
            data["detailVersion"] = IndexVersion;
            var response = Json(data);
            if (UseLastModified)
            {
                response.Content.Headers.LastModified = LastModified;
            }
            else
            {
                response.Headers.ETag = new EntityTagHeaderValue(Tag);
            }

            return response;
        }

        private async Task<HttpResponseMessage> DetailAsync(CancellationToken token)
        {
            DetailRequests++;
            _detailStarted.TrySetResult();
            if (DetailGate != null)
            {
                await DetailGate.Task.WaitAsync(token);
            }

            if (AlwaysConfirmANewVersion)
            {
                IndexVersion = "v" + ++_confirmations;
            }
            else if (ConfirmVersion != null)
            {
                IndexVersion = ConfirmVersion;
            }

            return Json(Detail ?? AramMetaFixture.Detail());
        }

        private static HttpResponseMessage Json(JObject value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };
    }
}
