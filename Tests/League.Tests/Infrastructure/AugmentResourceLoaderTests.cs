using System.Net;
using League.Infrastructure;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Infrastructure;

/// <summary>
/// The Arena augment catalog, read from the client's own file.
///
/// This loader is one of the quiet ones: a failed request, a body that is not JSON and an exception while parsing all
/// leave an empty catalog rather than throwing. That is the same choice the rune loader makes and the opposite of the
/// champion and item ones, and it is pinned here so the difference is on record.
///
/// The augment ids are split into two passes because the file mixes two ranges: the ones the mode uses are 1000 and up,
/// and the smaller ids are older entries. Reading the real ones first means a duplicate id is settled in their favour.
/// </summary>
public class AugmentResourceLoaderTests : IDisposable
{
    private readonly HttpClient _client = new();

    // The image tests cache to a folder of their own, never next to the test binaries.
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(),
        "league-augment-art-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        _client.Dispose();
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private AugmentResourceLoader Loader(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://lcu.invalid") }, new ImageCacheService(_client));

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static JObject Row(
        int id,
        string? name = null,
        string? traName = null,
        string? iconPath = "/x/aug.png",
        string? rarity = "kGold",
        string? description = "描述"
    )
    {
        var row = new JObject { ["id"] = id };
        if (name != null)
        {
            row["name"] = name;
        }

        if (traName != null)
        {
            row["nameTRA"] = traName;
        }

        if (iconPath != null)
        {
            row["augmentSmallIconPath"] = iconPath;
        }

        if (rarity != null)
        {
            row["rarity"] = rarity;
        }

        if (description != null)
        {
            row["description"] = description;
        }

        return row;
    }

    private static string Json(params JObject[] rows) => new JArray(rows.Cast<object>().ToArray()).ToString();

    // ---- loading ----

    [Fact]
    public async Task LoadAsync_TheFile_IsReadIntoTheCatalog()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(1200, name: "坦克引擎"))));

        await loader.LoadAsync();

        AugmentInfo? augment = loader.GetById(1200);

        Assert.NotNull(augment);
        Assert.Equal("坦克引擎", augment.Name);
    }

    [Fact]
    public async Task LoadAsync_TheRealRange_IsReadBeforeTheOlderOne()
    {
        // Traced: the small id is listed first, but the 1000-and-up range is processed in a pass of its own first.
        AugmentResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, Json(Row(5, name: "小的"), Row(1200, name: "大的")))
        );

        await loader.LoadAsync();

        Assert.Equal("小的", loader.GetById(5)?.Name);
        Assert.Equal("大的", loader.GetById(1200)?.Name);
    }

    [Fact]
    public async Task LoadAsync_TheTranslatedName_IsPreferred()
    {
        AugmentResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, Json(Row(1200, name: "English", traName: "中文")))
        );

        await loader.LoadAsync();

        Assert.Equal("中文", loader.GetById(1200)?.Name);
    }

    [Fact]
    public async Task LoadAsync_WithoutAName_FallsBackToTheId()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(1200))));

        await loader.LoadAsync();

        Assert.Equal("Augment 1200", loader.GetById(1200)?.Name);
    }

    [Fact]
    public async Task LoadAsync_WithoutAnIcon_IsNotAdded()
    {
        // An augment with no picture is not one the panel can offer.
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(1200, iconPath: null))));

        await loader.LoadAsync();

        Assert.Null(loader.GetById(1200));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task LoadAsync_AnIdThatIsNotOne_IsSkipped(int id)
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(id, name: "无名"))));

        await loader.LoadAsync();

        Assert.Null(loader.GetById(id));
    }

    [Fact]
    public async Task LoadAsync_NoRarity_IsUnknown()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(1200, rarity: null))));

        await loader.LoadAsync();

        Assert.Equal("Unknown", loader.GetById(1200)?.Rarity);
    }

    [Fact]
    public async Task LoadAsync_SeveralAugments_AreAllThere()
    {
        AugmentResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, Json(Row(1200, name: "一"), Row(1201, name: "二"), Row(1202, name: "三")))
        );

        await loader.LoadAsync();

        Assert.Equal("二", loader.GetById(1201)?.Name);
    }

    // ---- failing quietly ----

    [Fact]
    public async Task LoadAsync_AFailedRequest_LeavesAnEmptyCatalog()
    {
        // The quiet choice, like the rune loader - not an exception like the champion and item ones.
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.NotFound, ""));

        await loader.LoadAsync();

        Assert.Null(loader.GetById(1200));
    }

    [Fact]
    public async Task LoadAsync_ABodyThatIsNotJson_IsSwallowed()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, "not json"));

        await loader.LoadAsync();

        Assert.Null(loader.GetById(1200));
    }

    [Fact]
    public async Task LoadAsync_ABodyThatIsNotAList_IsSwallowed()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, """{"id": 1200}"""));

        await loader.LoadAsync();

        Assert.Null(loader.GetById(1200));
    }

    [Fact]
    public async Task LoadAsync_Twice_ReplacesRatherThanAppends()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(1200, name: "一"))));

        await loader.LoadAsync();
        await loader.LoadAsync();

        Assert.Equal("一", loader.GetById(1200)?.Name);
    }

    [Fact]
    public void AFreshLoader_HasNothing()
    {
        Assert.Null(Loader(new Answer(HttpStatusCode.OK, "[]")).GetById(1200));
    }

    // ---- the large art ----

    [Theory]
    [InlineData(
        "/lol-game-data/assets/ASSETS/UX/Cherry/Augments/Icons/ApexInventor_small.png",
        "/lol-game-data/assets/ASSETS/UX/Cherry/Augments/Icons/ApexInventor_large.png"
    )]
    [InlineData("/x/ADAPt_SMALL.png", "/x/ADAPt_large.png")]
    public void LargeArtPath_ASmallIcon_NamesTheLargeArtBesideIt(string small, string large)
    {
        Assert.Equal(large, AugmentResourceLoader.LargeArtPath(small));
    }

    [Theory]
    [InlineData("/x/aug.png")]
    [InlineData("")]
    [InlineData(null)]
    public void LargeArtPath_NotASmallIcon_HasNoLargeArt(string? path)
    {
        Assert.Null(AugmentResourceLoader.LargeArtPath(path));
    }

    [Fact]
    public void PublicArtUrl_AClientAsset_IsTheGamePathLowerCased()
    {
        Assert.Equal(
            "https://cdn.invalid/latest/game/assets/ux/kiwi/augments/icons/takeemdowndowndown_large.png",
            AugmentResourceLoader.PublicArtUrl(
                "/lol-game-data/assets/ASSETS/UX/Kiwi/Augments/Icons/TakeEmDownDownDown_large.png",
                "https://cdn.invalid/latest/"
            )
        );
    }

    [Theory]
    [InlineData("/x/aug_large.png")]
    [InlineData(null)]
    public void PublicArtUrl_NotAClientAsset_HasNoPublicCopy(string? path)
    {
        Assert.Null(AugmentResourceLoader.PublicArtUrl(path, "https://cdn.invalid"));
    }

    [Fact]
    public async Task GetInfoAsync_ClientWithoutTheLargeArt_ReadsItFromThePublicCopy()
    {
        var art = new Images(Png(256));
        AugmentResourceLoader loader = await ArtLoader(art);

        (System.Drawing.Image? image, _, _) = await loader.GetInfoAsync(3116);

        Assert.Equal(new System.Drawing.Size(64, 64), image?.Size);
    }

    [Fact]
    public async Task GetInfoAsync_NoLargeArtAnywhere_FallsBackToTheTintedIcon()
    {
        AugmentResourceLoader loader = await ArtLoader(new Images(null));

        (System.Drawing.Image? image, _, _) = await loader.GetInfoAsync(3116);

        // The small icon is 24 pixels; the art would have come back at 64.
        Assert.Equal(new System.Drawing.Size(24, 24), image?.Size);
    }

    [Fact]
    public async Task GetInfoAsync_ArtMissingEverywhere_IsNotAskedForAgain()
    {
        var art = new Images(null);
        AugmentResourceLoader loader = await ArtLoader(art);
        await loader.GetInfoAsync(3116);

        await loader.GetInfoAsync(3116);

        Assert.Equal(1, art.Requests);
    }

    // ---- the game's descriptions ----

    [Fact]
    public async Task LoadTextsAsync_TheGameHasTheAugment_ReplacesTheHandWrittenText()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(3116, description: "手写"))));
        await loader.LoadAsync();

        await loader.LoadTextsAsync(_ => Texts(new TextFiles(fail: false)), CancellationToken.None);

        Assert.Contains("持续5秒的效果", loader.GetById(3116)?.Description);
    }

    [Fact]
    public async Task LoadTextsAsync_TheTextsCannotBeRead_KeepsTheHandWrittenText()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(3116, description: "手写"))));
        await loader.LoadAsync();

        await loader.LoadTextsAsync(_ => Texts(new TextFiles(fail: true)), CancellationToken.None);

        Assert.Equal("手写", loader.GetById(3116)?.Description);
    }

    [Fact]
    public async Task LoadTextsAsync_TheTextsAreRead_ReportsReadyWithTheirCount()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(3116))));
        await loader.LoadAsync();
        var status = new AugmentTextStatus();

        await loader.LoadTextsAsync(_ => Texts(new TextFiles(fail: false)), CancellationToken.None, status: status);

        Assert.Equal(AugmentTextState.Ready, status.State);
        Assert.True(status.Count > 0);
    }

    [Fact]
    public async Task LoadTextsAsync_TheTextsCannotBeRead_ReportsFailed()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(3116))));
        await loader.LoadAsync();
        var status = new AugmentTextStatus();

        await loader.LoadTextsAsync(_ => Texts(new TextFiles(fail: true)), CancellationToken.None, status: status);

        Assert.Equal(AugmentTextState.Failed, status.State);
    }

    [Fact]
    public async Task LoadTextsAsync_ADownload_ReportsDownloadingFirst()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, Json(Row(3116))));
        await loader.LoadAsync();
        var status = new AugmentTextStatus();
        var states = new List<AugmentTextState>();
        status.Changed += () => states.Add(status.State);

        await loader.LoadTextsAsync(_ => Texts(new TextFiles(fail: false)), CancellationToken.None, status: status);

        Assert.Equal(AugmentTextState.Downloading, states[0]);
    }

    [Theory]
    [InlineData("\"16.21.723.1234\"", "16.21")]
    [InlineData("not a version", null)]
    public async Task LoadTextsAsync_TheClientsGameVersion_ChoosesThePatch(string version, string? patch)
    {
        // The client answers the version request; every other request gets the catalog.
        AugmentResourceLoader loader = Loader(new Version(version, Json(Row(3116))));
        await loader.LoadAsync();
        string? chosen = "unset";

        await loader.LoadTextsAsync(
            asked =>
            {
                chosen = asked;
                return Texts(new TextFiles(fail: true));
            },
            CancellationToken.None
        );

        Assert.Equal(patch, chosen);
    }

    private sealed class Version(string version, string catalog) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            bool asksVersion = request.RequestUri!.AbsolutePath == "/lol-patch/v1/game-version";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(asksVersion ? version : catalog),
                }
            );
        }
    }

    private AugmentTextData Texts(HttpMessageHandler handler) =>
        new(Path.Combine(_cache, "texts"), "16.21", new HttpClient(handler), "https://cdn.invalid");

    // Serves the trimmed mode data and string table in TestData/CommunityDragon/Augments, or fails every request.
    private sealed class TextFiles(bool fail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (fail)
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

    private const string SmallIcon = "/lol-game-data/assets/ASSETS/UX/Kiwi/Augments/Icons/TakeEmDownDownDown_small.png";

    // The client has the catalog and the small icon but not the large art, as it does for most augments.
    private async Task<AugmentResourceLoader> ArtLoader(Images publicArt)
    {
        var client = new HttpClient(new Client()) { BaseAddress = new Uri("https://lcu.invalid") };
        var loader = new AugmentResourceLoader(
            client,
            new ImageCacheService(client, Path.Combine(_cache, "client")),
            new ImageCacheService(new HttpClient(publicArt), Path.Combine(_cache, "public")),
            "https://cdn.invalid/latest"
        );
        await loader.LoadAsync();
        return loader;
    }

    private static byte[] Png(int size)
    {
        using var bitmap = new System.Drawing.Bitmap(size, size);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class Client : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            var response =
                path.EndsWith("cherry-augments.json", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(Json(Row(3116, traName: "将他们放倒倒倒", iconPath: SmallIcon))),
                    }
                : path.EndsWith("_small.png", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png(24)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    // The public copy: the art when there is some, a 404 when there is none, and a count of what was asked.
    private sealed class Images(byte[]? art) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            return Task.FromResult(
                art == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(art) }
            );
        }
    }

    // ---- looking one up ----

    [Fact]
    public async Task GetInfoAsync_AnUnknownAugment_SaysWhichItIsLookingForAndFindsNothing()
    {
        AugmentResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, "[]"));
        await loader.LoadAsync();

        (System.Drawing.Image? image, string name, string description) = await loader.GetInfoAsync(1200);

        Assert.Null(image);
        Assert.Equal("Augment 1200", name);
        Assert.Equal("", description);
    }
}
