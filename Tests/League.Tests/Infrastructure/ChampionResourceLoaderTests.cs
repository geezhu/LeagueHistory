using System.Net;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Infrastructure;

/// <summary>
/// The champion catalog: one JSON file of every champion, and icons fetched through the cache service.
///
/// Unlike the rune loader, this one does not check the response - it asks for the body directly, so a failure throws
/// rather than leaving the catalog empty. That is a real difference in behaviour between two loaders in the same
/// folder, and it is pinned here.
///
/// A second load replaces the catalog instead of appending to it, because the file is the whole truth and an appended
/// catalog would double every champion.
/// </summary>
public class ChampionResourceLoaderTests : IDisposable
{
    private readonly HttpClient _client = new();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private ChampionResourceLoader Loader(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://lcu.invalid") }, new ImageCacheService(_client));

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static string ChampionsJson(params (int Id, string Alias, string Name)[] champions) =>
        new JArray(
            champions.Select(c => new JObject
            {
                ["id"] = c.Id,
                ["alias"] = c.Alias,
                ["name"] = c.Name,
                ["description"] = c.Name + "的描述",
            })
        ).ToString();

    // ---- loading ----

    [Fact]
    public async Task LoadAsync_TheFile_IsReadIntoTheCatalog()
    {
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"))));

        await loader.LoadAsync();

        Champion champion = Assert.Single(loader.Champions);
        Assert.Equal(1, champion.Id);
        Assert.Equal("Ahri", champion.Alias);
        Assert.Equal("九尾妖狐", champion.Name);
    }

    [Fact]
    public async Task LoadAsync_SeveralChampions_AreAllThere()
    {
        ChampionResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"), (2, "Zed", "影流之主")))
        );

        await loader.LoadAsync();

        Assert.Equal(2, loader.Champions.Count);
    }

    [Fact]
    public async Task LoadAsync_AFailedRequest_Throws()
    {
        // This loader asks for the body directly, so a 404 becomes an exception rather than an empty catalog.
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.NotFound, ""));

        await Assert.ThrowsAsync<HttpRequestException>(() => loader.LoadAsync());
    }

    [Fact]
    public async Task LoadAsync_Twice_ReplacesRatherThanAppends()
    {
        // The file is the whole truth; an appended catalog would list every champion twice.
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"))));

        await loader.LoadAsync();
        await loader.LoadAsync();

        Assert.Single(loader.Champions);
    }

    [Fact]
    public void AFreshLoader_HasNothing()
    {
        Assert.Empty(Loader(new Answer(HttpStatusCode.OK, "[]")).Champions);
    }

    // ---- looking one up ----

    [Fact]
    public async Task GetById_AKnownChampion_IsFound()
    {
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"))));
        await loader.LoadAsync();

        Assert.Equal("Ahri", loader.GetById(1)?.Alias);
    }

    [Fact]
    public async Task GetById_AnUnknownChampion_IsNothing()
    {
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"))));
        await loader.LoadAsync();

        Assert.Null(loader.GetById(999));
    }

    [Fact]
    public async Task GetInfoAsync_AnUnknownChampion_SaysWhichItIsLookingForAndFindsNothing()
    {
        // The widget shows this wording, so it is pinned.
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"))));
        await loader.LoadAsync();

        (System.Drawing.Image? image, string name, string description) = await loader.GetInfoAsync(999);

        Assert.Null(image);
        Assert.Equal("未知英雄", name);
        Assert.Equal("未找到该英雄的描述", description);
    }

    // ---- the icon ----

    [Fact]
    public async Task GetIconAsync_AnUnknownChampion_FetchesNothing()
    {
        // There is no path to build for a champion that is not in the catalog.
        ChampionResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, ChampionsJson((1, "Ahri", "九尾妖狐"))));
        await loader.LoadAsync();

        Assert.Null(await loader.GetIconAsync(999));
    }

    [Fact]
    public async Task GetIconAsync_BeforeAnyLoad_FetchesNothing()
    {
        Assert.Null(await Loader(new Answer(HttpStatusCode.OK, "[]")).GetIconAsync(1));
    }
}
