using System.Net;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Infrastructure;

/// <summary>
/// The summoner spell catalog: one JSON file of every spell, and icons fetched through the cache service.
///
/// This loader is the third in its folder and behaves like the champion and item ones - it asks for the body
/// directly, so a failed request throws rather than leaving an empty catalog.
///
/// Its own rule is the icon path: the client sometimes writes it without a leading slash, and a relative path would be
/// resolved against the wrong root. The loader adds the slash before asking the cache, so both forms fetch the same
/// file.
/// </summary>
public class SummonerSpellResourceLoaderTests : IDisposable
{
    private readonly HttpClient _client = new();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private SummonerSpellResourceLoader Loader(HttpMessageHandler handler) =>
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
        long id,
        string? iconPath = "/x/flash.png",
        string? name = null,
        string? description = "描述"
    ) =>
        new()
        {
            ["id"] = id,
            ["name"] = name ?? "技能" + id,
            ["description"] = description == null ? null : JToken.FromObject(description),
            ["iconPath"] = iconPath == null ? null : JToken.FromObject(iconPath),
        };

    private static string SpellsJson(params JObject[] rows) => new JArray(rows.Cast<object>().ToArray()).ToString();

    // ---- loading ----

    [Fact]
    public async Task LoadAsync_TheFile_IsReadIntoTheCatalog()
    {
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, SpellsJson(Row(4))));

        await loader.LoadAsync();

        SummonerSpell spell = Assert.Single(loader.SummonerSpells);
        Assert.Equal(4, spell.Id);
        Assert.Equal("技能4", spell.Name);
        Assert.Equal("/x/flash.png", spell.IconPath);
        Assert.Equal("flash.png", spell.IconFileName);
    }

    [Fact]
    public async Task LoadAsync_TheIconFileName_IsTheLastSegmentOfThePath()
    {
        // It is what the cache is keyed by, so it is the file rather than the path.
        SummonerSpellResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, SpellsJson(Row(4, iconPath: "/deep/dir/flash.png")))
        );

        await loader.LoadAsync();

        Assert.Equal("flash.png", Assert.Single(loader.SummonerSpells).IconFileName);
    }

    [Fact]
    public async Task LoadAsync_AMissingDescription_IsAnEmptyString()
    {
        // A null in the file comes back through "dynamic" as a value whose ToString is empty, so the description
        // lands as "" rather than null - unlike the id, which is converted rather than stringified.
        SummonerSpellResourceLoader loader = Loader(
            new Answer(HttpStatusCode.OK, SpellsJson(Row(4, description: null)))
        );

        await loader.LoadAsync();

        Assert.Equal("", Assert.Single(loader.SummonerSpells).Description);
    }

    [Fact]
    public async Task LoadAsync_AMissingPath_BecomesEmptyStrings()
    {
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, SpellsJson(Row(4, iconPath: null))));

        await loader.LoadAsync();

        SummonerSpell spell = Assert.Single(loader.SummonerSpells);
        Assert.Equal("", spell.IconPath);
        Assert.Equal("", spell.IconFileName);
    }

    [Fact]
    public async Task LoadAsync_Twice_ReplacesRatherThanAppends()
    {
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, SpellsJson(Row(4))));

        await loader.LoadAsync();
        await loader.LoadAsync();

        Assert.Single(loader.SummonerSpells);
    }

    [Fact]
    public async Task LoadAsync_AFailedRequest_Throws()
    {
        // The same as the champion and item loaders, and unlike the rune one.
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.NotFound, ""));

        await Assert.ThrowsAsync<HttpRequestException>(() => loader.LoadAsync());
    }

    [Fact]
    public void AFreshLoader_HasNothing()
    {
        Assert.Empty(Loader(new Answer(HttpStatusCode.OK, "[]")).SummonerSpells);
    }

    // ---- looking one up ----

    [Fact]
    public async Task GetById_AKnownSpell_IsFound()
    {
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, SpellsJson(Row(4))));
        await loader.LoadAsync();

        Assert.Equal("技能4", loader.GetById(4)?.Name);
    }

    [Fact]
    public async Task GetById_AnUnknownSpell_IsNothing()
    {
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, SpellsJson(Row(4))));
        await loader.LoadAsync();

        Assert.Null(loader.GetById(999));
    }

    [Fact]
    public async Task GetInfoAsync_AnUnknownSpell_SaysWhichItIsLookingForAndFindsNothing()
    {
        SummonerSpellResourceLoader loader = Loader(new Answer(HttpStatusCode.OK, SpellsJson(Row(4))));
        await loader.LoadAsync();

        (System.Drawing.Image? image, string name, string description) = await loader.GetInfoAsync(999);

        Assert.Null(image);
        Assert.Equal("未知召唤师技能", name);
        Assert.Equal("未找到该技能的描述", description);
    }

    // ---- the icon path ----

    [Fact]
    public async Task GetIconAsync_AnEmptyPath_FetchesNothing()
    {
        Assert.Null(await Loader(new Answer(HttpStatusCode.OK, "[]")).GetIconAsync(""));
    }

    [Fact]
    public async Task GetIconAsync_APathWithoutAFileName_FetchesNothing()
    {
        // The file name is what the cache is keyed by.
        Assert.Null(await Loader(new Answer(HttpStatusCode.OK, "[]")).GetIconAsync("/x/"));
    }
}
