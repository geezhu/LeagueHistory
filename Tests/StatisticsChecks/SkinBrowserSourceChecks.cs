using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using League;
using League.ChampionSkin;
using League.Clients;
using League.Infrastructure;
using League.Models;
using League.PrimaryElection;
using static StatisticsCheckSupport;

/// <summary>
/// The skin browser's client-backed source against a stub client: the champion list, the account's owned skins, the
/// icon copy, filling a skin's missing paths from the champion data, and decoding a downloaded file. Every request is
/// answered by a fake handler and every folder is temporary, so no live client, no network and no real cache is
/// touched.
/// </summary>
internal static class SkinBrowserSourceChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Summoner = "/lol-summoner/v1/current-summoner";
    private const string Inventory = "/lol-champions/v1/inventories/12345/skins-minimal";
    private const string Champion99 = "/lol-game-data/assets/v1/champions/99.json";
    private const string Champion86 = "/lol-game-data/assets/v1/champions/86.json";
    private const string Champion1 = "/lol-game-data/assets/v1/champions/1.json";
    private const string Icon157 = "/lol-game-data/assets/v1/champion-icons/157.png";
    private const string OwnSplash = "/lol-game-data/assets/v1/champion-splashes/99/99001.jpg";
    private const string DataSplash = "/lol-game-data/assets/v1/champion-splashes/99/99002.jpg";

    private static readonly FieldInfo LcuHttpClient = typeof(LcuClient).GetField("_httpClient", Flags)!;
    private static readonly FieldInfo LcuSessionClient = typeof(LcuSession).GetField("_lcuClient", Flags)!;
    private static readonly MethodInfo InitializeServices = typeof(LcuSession).GetMethod("InitializeServices", Flags)!;

    public static async Task RunAsync()
    {
        string root = Temporary();
        var previousSession = FormMain.Globals.lcuClient;
        var previousResources = FormMain.Globals.resLoading;
        try
        {
            await WithoutClientAsync(root);
            await WithClientAsync(root);
            await FailuresAsync(root);
        }
        finally
        {
            FormMain.Globals.lcuClient = previousSession;
            FormMain.Globals.resLoading = previousResources;
            Cleanup(root);
        }

        Console.WriteLine(
            "PASS: the skin browser's client source lists champions, reads owned skins, copies icons, fills a skin's "
                + "paths from the champion data and decodes its files against a stub client. No live client or network."
        );
    }

    // ---------------------------------------------------------------- no client

    private static async Task WithoutClientAsync(string root)
    {
        FormMain.Globals.lcuClient = new LcuSession();
        FormMain.Globals.resLoading = Resources(root, new Stub(Refusing), Champions());
        var source = Source(root);

        var models = await source.LoadAsync(new Dictionary<int, int>(), CancellationToken.None);
        Check(
            models.Count == 4 && models.All(m => m.Skins.Count == 1 && m.Skins[0].IsDefault),
            "Without a client every champion is listed with only its default skin"
        );

        var skin = new SkinThumbItem { SkinId = 99000 };
        Check(
            await source.LoadSkinAsync(models[0], skin, SkinImageKind.Tile, CancellationToken.None) == null,
            "Without a client an uncached skin has no art"
        );
        Check(skin.TilePath == null, "Without the champion data a skin keeps its missing paths");
        Check(
            await source.LoadIconAsync(models[0], CancellationToken.None) == null,
            "An icon the shared resource cache does not know is missing"
        );

        bool stopped = false;
        try
        {
            await source.LoadIconAsync(models[0], new CancellationToken(canceled: true));
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        Check(stopped, "A cancelled icon request stops before loading anything");
    }

    // ---------------------------------------------------------------- with a client

    private static async Task WithClientAsync(string root)
    {
        var stub = new Stub(Route);
        FormMain.Globals.lcuClient = StubSession(stub);
        FormMain.Globals.resLoading = Resources(root, stub, Champions());
        var source = Source(root);

        var models = await source.LoadAsync(new Dictionary<int, int> { [99] = 99001 }, CancellationToken.None);
        var lux = models.Single(m => m.ChampionId == 99);
        Check(lux.IsOwned && lux.PreferredSkinId == 99001, "The account's owned skins and its preference are reported");
        Check(
            models.Single(m => m.ChampionId == 1).Skins.Single().IsDefault,
            "A champion without an inventory entry keeps only the default skin"
        );
        Check(
            models.All(m => m.ChampionId != 86 || m.Skins.Any(s => s.IsDefault)),
            "An owned default skin comes from the inventory"
        );

        await OwnPathsAsync(source, lux, stub);
        await ChampionDataAsync(source, models, stub);
        await IconsAsync(source, root, stub, models);
    }

    private static async Task OwnPathsAsync(LcuSkinBrowserSource source, ChampionSkinCardModel lux, Stub stub)
    {
        var owned = lux.Skins.Single(s => s.SkinId == 99001);
        Check(owned.SplashPath == OwnSplash && owned.TilePath == null, "The inventory's own splash path is kept");

        using var splash = await source.LoadSkinAsync(lux, owned, SkinImageKind.Splash, CancellationToken.None);
        Check(splash != null, "A skin with its own path is downloaded and decoded");
        Check(stub.Requested(OwnSplash), "The path the inventory reported is the one requested");

        // The same skin has no tile path, so its tile comes from the champion data.
        using var tile = await source.LoadSkinAsync(lux, owned, SkinImageKind.Tile, CancellationToken.None);
        Check(tile != null, "The same skin's tile falls back to the standard location");
    }

    private static async Task ChampionDataAsync(
        LcuSkinBrowserSource source,
        List<ChampionSkinCardModel> models,
        Stub stub
    )
    {
        var lux = models.Single(m => m.ChampionId == 99);
        int reads = stub.Count(Champion99);
        Check(reads == 1, "A champion's data is read once, when one of its skins first needs its paths");

        var first = new SkinThumbItem { SkinId = 99002 };
        using (var image = await source.LoadSkinAsync(lux, first, SkinImageKind.Splash, CancellationToken.None))
        {
            Check(image != null, "A skin without paths is downloaded from the art the champion data names");
        }

        Check(first.SplashPath == DataSplash, "A skin without paths takes them from the champion data");
        Check(stub.Count(Champion99) == reads, "The champion data already read is reused");

        var unknown = new SkinThumbItem { SkinId = 99003 };
        using (await source.LoadSkinAsync(lux, unknown, SkinImageKind.Tile, CancellationToken.None))
        {
            Check(stub.Count(Champion99) == reads, "Another skin of the same champion reuses the champion data");
        }

        Check(unknown.TilePath == null, "A skin the champion data does not name keeps its missing paths");

        var garen = models.Single(m => m.ChampionId == 86);
        int missing = stub.Count(Champion86);
        var orphan = new SkinThumbItem { SkinId = 86001 };
        using (var image = await source.LoadSkinAsync(garen, orphan, SkinImageKind.Tile, CancellationToken.None))
        {
            Check(image != null, "A champion whose data is refused still gets the standard tile");
        }

        using (
            await source.LoadSkinAsync(
                garen,
                new SkinThumbItem { SkinId = 86002 },
                SkinImageKind.Tile,
                CancellationToken.None
            )
        )
        {
            Check(stub.Count(Champion86) == missing + 1, "A champion whose data failed is not read again");
        }

        var annie = models.Single(m => m.ChampionId == 1);
        using (
            var image = await source.LoadSkinAsync(
                annie,
                new SkinThumbItem { SkinId = 1001 },
                SkinImageKind.Tile,
                CancellationToken.None
            )
        )
        {
            Check(image != null, "A champion whose data request breaks still gets the standard tile");
        }
    }

    private static async Task IconsAsync(
        LcuSkinBrowserSource source,
        string root,
        Stub stub,
        List<ChampionSkinCardModel> models
    )
    {
        string folder = Path.Combine(root, "champion");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "A2Lux.png"), Png(48));

        using (var cached = await source.LoadIconAsync(models.Single(m => m.ChampionId == 99), CancellationToken.None))
        {
            Check(cached != null, "A champion icon already in the shared cache is returned");
        }

        Check(stub.Count(Icon157) == 0, "A cached icon is not downloaded");
        using (
            var downloaded = await source.LoadIconAsync(models.Single(m => m.ChampionId == 157), CancellationToken.None)
        )
        {
            Check(downloaded != null && stub.Count(Icon157) == 1, "An icon that is not cached is downloaded once");
        }

        Check(
            SkinImageStore.IsValid(Path.Combine(folder, "A2Yasuo.png")),
            "The downloaded icon is kept in the shared cache folder"
        );

        // A cached file that is the right size but not an image.
        Directory.CreateDirectory(Path.Combine(root, "tiles"));
        await File.WriteAllBytesAsync(Path.Combine(root, "tiles", "77001.jpg"), new byte[512]);
        using (
            var broken = await source.LoadSkinAsync(
                models.Single(m => m.ChampionId == 99),
                new SkinThumbItem { SkinId = 77001 },
                SkinImageKind.Tile,
                CancellationToken.None
            )
        )
        {
            Check(broken == null, "A cached file that is not an image yields no art");
        }
    }

    // ---------------------------------------------------------------- failures

    private static async Task FailuresAsync(string root)
    {
        await ExpectOwnedAsync(
            root,
            new Stub(_ => Json("""{"summonerId":12345,"displayName":"stub"}""")),
            "A summoner without a puuid counts as not ready"
        );
        await ExpectOwnedAsync(
            root,
            new Stub(path =>
                path == Summoner
                    ? Json("""{"puuid":"p","summonerId":12345}""")
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
            ),
            "An inventory the client refuses leaves only the default skins"
        );
        await ExpectOwnedAsync(
            root,
            new Stub(path =>
                path == Summoner ? Json("""{"puuid":"p","summonerId":12345}""") : Json("not an inventory")
            ),
            "An inventory that is not JSON leaves only the default skins"
        );
        await ExpectOwnedAsync(
            root,
            new Stub(path =>
                path == Summoner
                    ? throw new HttpRequestException("no summoner")
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
            ),
            "A summoner request that breaks leaves only the default skins"
        );
    }

    private static async Task ExpectOwnedAsync(string root, Stub stub, string message)
    {
        FormMain.Globals.lcuClient = StubSession(stub);
        FormMain.Globals.resLoading = Resources(root, stub, Champions());
        var models = await Source(root).LoadAsync(new Dictionary<int, int>(), CancellationToken.None);
        Check(models.Count == 4 && models.All(m => !m.IsOwned && m.Skins.Count == 1), message);
    }

    // ---------------------------------------------------------------- wiring

    private static LcuSkinBrowserSource Source(string root)
    {
        string tiles = Path.Combine(root, "tiles");
        string splashes = Path.Combine(root, "splashes");
        Directory.CreateDirectory(tiles);
        Directory.CreateDirectory(splashes);
        var source = new LcuSkinBrowserSource();
        Set(source, "_images", new SkinImageStore(() => FormMain.Globals.lcuClient.Client, tiles, splashes));
        return source;
    }

    /// <summary>A resource cache over the stub client whose folders are the temporary ones.</summary>
    private static ResourceLoading Resources(string root, Stub stub, List<PrimaryChampion> champions)
    {
        var resources = new ResourceLoading();
        var cache = new ImageCacheService(Client(stub));
        Set(cache, "_baseCacheDir", root);
        foreach (var champion in champions)
        {
            resources.Champions.Champions.Add(
                new Champion
                {
                    Id = champion.Id,
                    Name = champion.Name,
                    Alias = champion.Alias,
                    Description = string.Empty,
                }
            );
        }

        Set(resources.Champions, "_imageCache", cache);
        var manager = new ChampionManager(Client(stub), resources);
        Set(manager, "_isInitialized", true);
        Set(manager, "_allChampions", champions);
        Set(resources, "_championManager", manager);
        return resources;
    }

    private static LcuSession StubSession(Stub stub)
    {
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose();
        LcuHttpClient.SetValue(client, Client(stub));
        var session = new LcuSession();
        LcuSessionClient.SetValue(session, client);
        InitializeServices.Invoke(session, null);
        return session;
    }

    private static HttpClient Client(Stub stub) => new(stub) { BaseAddress = new Uri("https://127.0.0.1/") };

    private static List<PrimaryChampion> Champions() =>
        [
            Primary(99, "A2Lux", "光辉女郎", "Lux"),
            Primary(1, "A2Annie", "黑暗之女", "Annie"),
            Primary(86, "A2Garen", "德玛西亚之力", "Garen"),
            Primary(157, "A2Yasuo", "疾风剑豪", "Yasuo"),
        ];

    private static PrimaryChampion Primary(int id, string name, string title, string alias) =>
        new()
        {
            Id = id,
            Name = name,
            Title = title,
            Alias = alias,
        };

    private static void Set(object owner, string name, object? value) =>
        owner.GetType().GetField(name, Flags)!.SetValue(owner, value);

    // ---------------------------------------------------------------- the stub client

    private static HttpResponseMessage Route(string path) =>
        path switch
        {
            Summoner => Json("""{"puuid":"stub-puuid","summonerId":12345}"""),
            Inventory => Json(InventoryBody),
            Champion99 => Json(ChampionBody),
            Champion86 => new HttpResponseMessage(HttpStatusCode.NotFound),
            Champion1 => throw new HttpRequestException("no champion data"),
            _ when path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => Bytes(Png(48)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

    private static HttpResponseMessage Refusing(string path) => new(HttpStatusCode.NotFound);

    private const string InventoryBody = """
        [{"id":99001,"name":"测试皮肤","championId":99,"isBase":false,
          "splashPath":"/lol-game-data/assets/v1/champion-splashes/99/99001.jpg","ownership":{"owned":true}},
         {"id":86000,"name":"默认","championId":86,"isBase":true,"ownership":{"owned":true}},
         {"id":55000,"name":"未拥有","championId":55,"ownership":{"owned":false}}]
        """;

    private const string ChampionBody = """
        {"skins":[{"id":99002,
                   "splashPath":"/lol-game-data/assets/v1/champion-splashes/99/99002.jpg",
                   "tilePath":"/lol-game-data/assets/v1/champion-tiles/99/99002.jpg"}]}
        """;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>A small but real PNG, above the image store's minimum size.</summary>
    private static byte[] Png(int size)
    {
        using var bitmap = new Bitmap(size, size);
        var random = new Random(size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class Stub(Func<string, HttpResponseMessage> route) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public bool Requested(string path) => _requests.Contains(path);

        public int Count(string path) => _requests.Count(requested => requested == path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _requests.Enqueue(request.RequestUri!.AbsolutePath);
            return Task.FromResult(route(request.RequestUri.AbsolutePath));
        }
    }

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), $"league-skin-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Skin browser source check left its temporary folder: " + path);
        }
    }
}
