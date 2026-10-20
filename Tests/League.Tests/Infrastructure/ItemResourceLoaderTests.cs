using System.Net;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Infrastructure;

/// <summary>
/// The item catalog: one JSON file of every item, and icons fetched through the cache service.
///
/// Not every entry in the file is an item worth showing. The client's list carries rows with no icon - placeholders,
/// removed items and internal ids - and those are skipped rather than added with a blank picture. A price that is
/// missing becomes zero, which is what free and unsellable items both report.
/// </summary>
public class ItemResourceLoaderTests : IDisposable
{
    private readonly HttpClient _client = new();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private ItemResourceLoader Loader(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://lcu.invalid") }, new ImageCacheService(_client));

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static JObject Row(int id, string? iconPath = "/x/icon.png", int? price = 300) =>
        new()
        {
            ["id"] = id,
            ["name"] = "装备" + id,
            ["description"] = "装备" + id + "的描述",
            ["iconPath"] = iconPath == null ? null : JToken.FromObject(iconPath),
            ["priceTotal"] = price == null ? null : JToken.FromObject(price),
        };

    private static string ItemsJson(params JObject[] rows) => new JArray(rows.Cast<object>().ToArray()).ToString();

    // ---- loading ----

    [Fact]
    public async Task LoadAsync_TheFile_IsReadIntoTheCatalog()
    {
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ItemsJson(Row(1001, price: 300))));

        await loader.LoadAsync();

        Item item = Assert.Single(loader.Items);
        Assert.Equal(1001, item.Id);
        Assert.Equal("装备1001", item.Name);
        Assert.Equal(300, item.PriceTotal);
        Assert.Equal("/x/icon.png", item.IconFileName);
    }

    [Fact]
    public async Task LoadAsync_AnItemWithoutAnIcon_IsSkipped()
    {
        // Those rows are placeholders, removed items and internal ids; a blank picture is not worth showing.
        ItemResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, ItemsJson(Row(1001, iconPath: ""), Row(1002)))
        );

        await loader.LoadAsync();

        Assert.Equal([1002], loader.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task LoadAsync_AnItemWithNoPrice_IsFreeRatherThanUnknown()
    {
        // Free and unsellable items both report nothing; zero is what the panel shows.
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ItemsJson(Row(1001, price: null))));

        await loader.LoadAsync();

        Assert.Equal(0, Assert.Single(loader.Items).PriceTotal);
    }

    [Fact]
    public async Task LoadAsync_Twice_ReplacesRatherThanAppends()
    {
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ItemsJson(Row(1001))));

        await loader.LoadAsync();
        await loader.LoadAsync();

        Assert.Single(loader.Items);
    }

    [Fact]
    public async Task LoadAsync_AFailedRequest_Throws()
    {
        // This loader asks for the body directly, so a 404 is an exception rather than an empty catalog.
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.NotFound, ""));

        await Assert.ThrowsAsync<HttpRequestException>(() => loader.LoadAsync());
    }

    [Fact]
    public void AFreshLoader_HasNothing()
    {
        Assert.Empty(Loader(new Answer(HttpStatusCode.OK, "[]")).Items);
    }

    // ---- looking one up ----

    [Fact]
    public async Task GetById_AKnownItem_IsFound()
    {
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ItemsJson(Row(1001))));
        await loader.LoadAsync();

        Assert.Equal("装备1001", loader.GetById(1001)?.Name);
    }

    [Fact]
    public async Task GetById_AnUnknownItem_IsNothing()
    {
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ItemsJson(Row(1001))));
        await loader.LoadAsync();

        Assert.Null(loader.GetById(999));
    }

    [Fact]
    public async Task GetInfoAsync_AnUnknownItem_SaysWhichItIsLookingForAndFindsNothing()
    {
        ItemResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ItemsJson(Row(1001))));
        await loader.LoadAsync();

        (System.Drawing.Image? image, string name, string description) = await loader.GetInfoAsync(999);

        Assert.Null(image);
        Assert.Equal("未知装备", name);
        Assert.Equal("未找到该装备的描述", description);
    }

    // ---- the icon ----

    [Fact]
    public async Task GetIconAsync_PathWithoutAFileName_FetchesNothing()
    {
        // The file name is what the cache is keyed by, so a path ending in a separator is not a file.
        Assert.Null(await Loader(new Answer(HttpStatusCode.OK, "[]")).GetIconAsync("/x/"));
    }

    [Fact]
    public async Task GetIconAsync_NoPathAtAll_FetchesNothing()
    {
        Assert.Null(await Loader(new Answer(HttpStatusCode.OK, "[]")).GetIconAsync(""));
    }
}
