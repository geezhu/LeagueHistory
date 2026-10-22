using System.Net;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Reading the augments' texts for a patch. The transport serves the trimmed copies in
/// TestData/CommunityDragon/Augments in place of the mode data and the string table, and the cache goes to a folder
/// of the test's own. A text the table keeps by name and one it keeps only by hash must both be found, and a second
/// read must come from the cache without asking the network again.
/// </summary>
public class AugmentTextDataTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(),
        "league-augment-text-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private AugmentTextData Data(HttpMessageHandler handler) =>
        new(_cache, "16.21", new HttpClient(handler), "https://cdn.invalid");

    [Fact]
    public async Task LoadAsync_AnAugmentKeptByHash_HasItsNameAndFilledTooltip()
    {
        var texts = await Data(new Files()).LoadAsync(CancellationToken.None);

        Assert.Equal("将他们放倒倒倒", texts[3116].Name);
        Assert.Contains("持续5秒的效果", texts[3116].Tooltip);
    }

    [Fact]
    public async Task LoadAsync_AnAugmentKeptByName_HasItsSummary()
    {
        var texts = await Data(new Files()).LoadAsync(CancellationToken.None);

        Assert.StartsWith("在承受或造成伤害时获得层数。每10层", texts[1001].Summary);
    }

    [Fact]
    public async Task LoadAsync_ASecondRead_ComesFromTheCache()
    {
        await Data(new Files()).LoadAsync(CancellationToken.None);
        var offline = new Files { Fail = true };

        var texts = await Data(offline).LoadAsync(CancellationToken.None);

        Assert.Equal("将他们放倒倒倒", texts[3116].Name);
        Assert.Equal(0, offline.Requests);
    }

    [Fact]
    public async Task LoadAsync_Refresh_DownloadsAgainDespiteTheCache()
    {
        await Data(new Files()).LoadAsync(CancellationToken.None);
        var online = new Files();

        await Data(online).LoadAsync(CancellationToken.None, refresh: true);

        Assert.Equal(2, online.Requests);
    }

    [Fact]
    public async Task LoadAsync_ADownload_ReportsProgressUpToOne()
    {
        var reported = new List<double>();

        await Data(new Files()).LoadAsync(CancellationToken.None, progress: reported.Add);

        Assert.Equal(1, reported[^1]);
    }

    [Fact]
    public async Task LoadAsync_ADownloadWithoutALength_ReportsProgressBeforeTheEnd()
    {
        var reported = new List<double>();

        await Data(new Files { Chunked = true }).LoadAsync(CancellationToken.None, progress: reported.Add);

        Assert.Contains(reported, share => share > 0 && share < 1);
    }

    [Fact]
    public async Task LoadAsync_ADownload_RemovesTheOlderPatchesCache()
    {
        Directory.CreateDirectory(_cache);
        string older = Path.Combine(_cache, "16.20-r" + AugmentTextData.RulesVersion + ".json");
        await File.WriteAllTextAsync(older, "[]");

        await Data(new Files()).LoadAsync(CancellationToken.None);

        Assert.False(File.Exists(older));
    }

    [Fact]
    public async Task IsCached_AfterADownload_IsTrue()
    {
        var data = Data(new Files());

        await data.LoadAsync(CancellationToken.None);

        Assert.True(data.IsCached);
    }

    [Fact]
    public async Task LoadAsync_TheNetworkFails_Throws()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Data(new Files { Fail = true }).LoadAsync(CancellationToken.None)
        );
    }

    [Fact]
    public void HashedKey_AName_IsTheKeyTheStringTableFilesItUnder()
    {
        Assert.Equal("{03b9fb7aea}", AugmentTextData.HashedKey("Kiwi_Augment_TakeemDownDownDown_Name"));
    }

    private sealed class Files : HttpMessageHandler
    {
        public bool Fail { get; init; }

        // As CommunityDragon sends them: no length, and in this test large enough for the progress to move, by
        // leading whitespace the JSON reader skips.
        public bool Chunked { get; init; }

        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Requests++;
            if (Fail)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            string name = request.RequestUri!.AbsolutePath.EndsWith("kiwi.bin.json", StringComparison.Ordinal)
                ? "kiwi.bin.json"
                : "lol.stringtable.json";
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "CommunityDragon", "Augments", name);
            HttpContent content = Chunked
                ? new ByteArrayContent([
                    .. Enumerable.Repeat((byte)' ', 2_000_000),
                    .. await File.ReadAllBytesAsync(path, token),
                ])
                : new StreamContent(File.OpenRead(path));
            if (Chunked)
            {
                content.Headers.ContentLength = null;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
