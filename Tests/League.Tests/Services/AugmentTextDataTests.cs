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

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (Fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            string name = request.RequestUri!.AbsolutePath.EndsWith("kiwi.bin.json", StringComparison.Ordinal)
                ? "kiwi.bin.json"
                : "lol.stringtable.json";
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "CommunityDragon", "Augments", name);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(path)) }
            );
        }
    }
}
