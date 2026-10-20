using System.Net;
using League.Infrastructure;
using League.Models;
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

    public void Dispose()
    {
        _client.Dispose();
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
