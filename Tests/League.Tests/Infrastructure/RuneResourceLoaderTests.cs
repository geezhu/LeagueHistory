using System.Net;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Infrastructure;

/// <summary>
/// The rune data: one JSON file of runes, an optional second one of the trees they sit in, and images fetched through
/// the cache service.
///
/// The two files fail independently, and that is the point. Without the rune file there is nothing to show at all; the
/// tree file is only what groups them, so a failure there leaves the runes usable - the source says so in a comment
/// and the try/catch is what makes it true.
/// </summary>
public class RuneResourceLoaderTests : IDisposable
{
    private readonly HttpClient _client = new();

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private RuneResourceLoader Loader(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://lcu.invalid") }, new ImageCacheService(_client));

    /// <summary>Answers each request from the next body in turn, repeating the last.</summary>
    private sealed class Bodies(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        private int _call;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            (HttpStatusCode status, string body) = answers[Math.Min(_call++, answers.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static string RunesJson(params (int Id, string Name)[] runes) =>
        new JArray(
            runes.Select(r => new JObject
            {
                ["id"] = r.Id,
                ["name"] = r.Name,
                ["longDesc"] = r.Name + "的描述",
                ["iconPath"] = "/x/" + r.Id + ".png",
            })
        ).ToString();

    private static string StylesJson(params (int Id, string Name)[] styles) =>
        new JObject
        {
            ["styles"] = new JArray(
                styles.Select(s => new JObject
                {
                    ["id"] = s.Id,
                    ["name"] = s.Name,
                    ["iconPath"] = "/y/" + s.Id + ".png",
                    ["slots"] = new JArray(
                        new JObject { ["perks"] = new JArray(1, 2, 3) },
                        new JObject { ["perks"] = new JArray(4) }
                    ),
                })
            ),
        }.ToString();

    // ---- reading the trees ----

    [Fact]
    public void ParseStyles_EachTree_KeepsItsNameIconAndSlots()
    {
        Dictionary<int, RuneResourceLoader.RuneStyle> styles = RuneResourceLoader.ParseStyles(
            JObject.Parse(StylesJson((8000, "精密")))
        );

        RuneResourceLoader.RuneStyle style = styles[8000];
        Assert.Equal("精密", style.Name);
        Assert.Equal("/y/8000.png", style.IconPath);
        Assert.Equal(2, style.Slots.Length);
        Assert.Equal([1, 2, 3], style.Slots[0]);
        Assert.Equal([4], style.Slots[1]);
    }

    [Fact]
    public void ParseStyles_ATreeWithoutAnId_IsLeftOut()
    {
        // Without an id there is nothing to look the tree up by.
        Assert.Empty(RuneResourceLoader.ParseStyles(JObject.Parse(StylesJson((0, "无编号")))));
    }

    [Fact]
    public void ParseStyles_ATreeWithoutANameOrIcon_GetsEmptyStrings()
    {
        var data = new JObject { ["styles"] = new JArray(new JObject { ["id"] = 8000 }) };

        RuneResourceLoader.RuneStyle style = RuneResourceLoader.ParseStyles(data)[8000];
        Assert.Equal("", style.Name);
        Assert.Equal("", style.IconPath);
    }

    [Fact]
    public void ParseStyles_ATreeWithoutSlots_HasNone()
    {
        var data = new JObject { ["styles"] = new JArray(new JObject { ["id"] = 8000 }) };

        Assert.Empty(RuneResourceLoader.ParseStyles(data)[8000].Slots);
    }

    [Fact]
    public void ParseStyles_SeveralTrees_AreAllThere()
    {
        Dictionary<int, RuneResourceLoader.RuneStyle> styles = RuneResourceLoader.ParseStyles(
            JObject.Parse(StylesJson((8000, "精密"), (8100, "主宰")))
        );

        Assert.Equal(2, styles.Count);
        Assert.Equal("主宰", styles[8100].Name);
    }

    [Fact]
    public void ParseStyles_NoStylesAtAll_IsEmpty()
    {
        Assert.Empty(RuneResourceLoader.ParseStyles(new JObject()));
    }

    [Fact]
    public void ParseStyles_TheSameIdTwice_IsRefused()
    {
        // The result is keyed by id, so two trees with one id cannot both be kept - better to say so than to pick.
        Assert.ThrowsAny<ArgumentException>(() =>
            RuneResourceLoader.ParseStyles(JObject.Parse(StylesJson((8000, "精密"), (8000, "重复"))))
        );
    }

    // ---- loading ----

    [Fact]
    public async Task LoadAsync_BothFiles_LoadTheRunesAndTheTrees()
    {
        Bodies handler = new(
            (HttpStatusCode.OK, RunesJson((1, "电刑"))),
            (HttpStatusCode.OK, StylesJson((8000, "精密")))
        );
        RuneResourceLoader loader = Loader(handler);

        await loader.LoadAsync();

        Assert.Single(loader.Runes);
        Assert.Single(loader.Styles);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task LoadAsync_TheRuneFileFails_LeavesBothEmpty()
    {
        // Without the runes there is nothing to show, so the second file is not even asked for.
        Bodies handler = new((HttpStatusCode.NotFound, ""));
        RuneResourceLoader loader = Loader(handler);

        await loader.LoadAsync();

        Assert.Empty(loader.Runes);
        Assert.Empty(loader.Styles);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_TheTreeFileFails_StillLeavesTheRunesUsable()
    {
        // The trees only group the runes; a failure there must not cost the runes themselves.
        Bodies handler = new((HttpStatusCode.OK, RunesJson((1, "电刑"))), (HttpStatusCode.NotFound, ""));
        RuneResourceLoader loader = Loader(handler);

        await loader.LoadAsync();

        Assert.Single(loader.Runes);
        Assert.Empty(loader.Styles);
    }

    [Fact]
    public async Task LoadAsync_TheTreeFileIsNotJson_StillLeavesTheRunesUsable()
    {
        // The failure is caught around the whole tree step, parsing included.
        Bodies handler = new((HttpStatusCode.OK, RunesJson((1, "电刑"))), (HttpStatusCode.OK, "{ not json "));
        RuneResourceLoader loader = Loader(handler);

        await loader.LoadAsync();

        Assert.Single(loader.Runes);
        Assert.Empty(loader.Styles);
    }

    // ---- looking one up ----

    [Fact]
    public async Task GetById_ALoadedRune_IsFound()
    {
        RuneResourceLoader loader = Loader(
            new Bodies((HttpStatusCode.OK, RunesJson((1, "电刑"))), (HttpStatusCode.OK, "{}"))
        );
        await loader.LoadAsync();

        Assert.Equal("电刑", loader.GetById(1)?.name);
    }

    [Fact]
    public async Task GetById_AnUnknownRune_IsNothing()
    {
        RuneResourceLoader loader = Loader(
            new Bodies((HttpStatusCode.OK, RunesJson((1, "电刑"))), (HttpStatusCode.OK, "{}"))
        );
        await loader.LoadAsync();

        Assert.Null(loader.GetById(999));
    }

    [Fact]
    public async Task GetInfoAsync_AnUnknownRune_SaysWhichItIsLookingForAndFindsNothing()
    {
        // The widget shows this wording, so it is pinned rather than left to chance.
        RuneResourceLoader loader = Loader(
            new Bodies((HttpStatusCode.OK, RunesJson((1, "电刑"))), (HttpStatusCode.OK, "{}"))
        );
        await loader.LoadAsync();

        (System.Drawing.Image? image, string name, string description) = await loader.GetInfoAsync(999);

        Assert.Null(image);
        Assert.Equal("未知符文", name);
        Assert.Equal("未找到该符文描述", description);
    }

    [Fact]
    public async Task GetInfoAsync_ALoadedRune_NamesItAndItsDescription()
    {
        RuneResourceLoader loader = Loader(
            new Bodies((HttpStatusCode.OK, RunesJson((1, "电刑"))), (HttpStatusCode.OK, "{}"))
        );
        await loader.LoadAsync();

        (_, string name, string description) = await loader.GetInfoAsync(1);

        Assert.Equal("电刑", name);
        Assert.Equal("电刑的描述", description);
    }

    // ---- the icon path ----

    [Fact]
    public async Task GetIconAsync_NoPath_FetchesNothing()
    {
        // A rune without an icon is not an error; there is simply nothing to fetch.
        RuneResourceLoader loader = Loader(new Bodies((HttpStatusCode.NotFound, "")));

        Assert.Null(await loader.GetIconAsync(""));
    }

    [Fact]
    public async Task GetIconAsync_APathThatNamesNoFile_FetchesNothing()
    {
        // The file name is what the cache is keyed by, so a path ending in a separator is not a file.
        RuneResourceLoader loader = Loader(new Bodies((HttpStatusCode.NotFound, "")));

        Assert.Null(await loader.GetIconAsync("/lol-game-data/assets/v1/rune-icons/"));
    }

    // ---- what it starts with ----

    [Fact]
    public void AFreshLoader_HasNeitherRunesNorTrees()
    {
        RuneResourceLoader loader = Loader(new Bodies((HttpStatusCode.NotFound, "")));

        Assert.Empty(loader.Runes);
        Assert.Empty(loader.Styles);
    }
}
