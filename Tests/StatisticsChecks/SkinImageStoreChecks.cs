using System.Net;
using League.ChampionSkin;
using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// The skin image cache: which client paths are tried for a tile or a splash, what counts as a usable file, and
/// the download that writes one. The transport is a fake handler and the folders are temporary, so no client and
/// no real cache is touched.
/// </summary>
internal static class SkinImageStoreChecks
{
    public static async Task RunAsync()
    {
        SourceChecks();
        await ValidityChecks();
        await StorageChecks();
        Console.WriteLine(
            "PASS: the skin image source order, the usable-file rule, and the download, reuse, offline and refusal "
                + "paths of the tile and splash caches."
        );
    }

    private static void SourceChecks()
    {
        var skin = new SkinThumbItem { SkinId = 103000, Name = "默认" };
        Check(
            SkinImageStore
                .Sources(103, skin, SkinImageKind.Tile)
                .SequenceEqual(["/lol-game-data/assets/v1/champion-tiles/103/103000.jpg"]),
            "A tile without its own path uses the standard location"
        );

        skin.TilePath = "assets/characters/ahri/skins/base/images/ahri_square_0.jpg";
        var tile = SkinImageStore.Sources(103, skin, SkinImageKind.Tile);
        Check(
            tile[0] == "/assets/characters/ahri/skins/base/images/ahri_square_0.jpg",
            "The path the inventory reports is tried first, with a leading slash"
        );
        Check(tile.Count == 2, "The standard tile location is kept as the fallback");

        Check(
            SkinImageStore
                .Sources(103, skin, SkinImageKind.Splash)
                .SequenceEqual([
                    "/lol-game-data/assets/v1/champion-splashes/103/103000.jpg",
                    "/lol-game-data/assets/v1/champion-splashes/uncentered/103/103000.jpg",
                ]),
            "Splash art tries the centred path before the uncentred one"
        );
    }

    private static async Task ValidityChecks()
    {
        string folder = Temporary("valid");
        try
        {
            Check(!SkinImageStore.IsValid(Path.Combine(folder, "missing.jpg")), "A missing file is not a usable image");

            string small = Path.Combine(folder, "small.jpg");
            await File.WriteAllBytesAsync(small, new byte[100]);
            Check(!SkinImageStore.IsValid(small), "A file below the minimum size is not a usable image");

            string good = Path.Combine(folder, "good.jpg");
            await File.WriteAllBytesAsync(good, Jpeg());
            Check(SkinImageStore.IsValid(good), "A real image above the minimum size is usable");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    private static async Task StorageChecks()
    {
        string tiles = Temporary("tiles");
        string splashes = Temporary("splashes");
        try
        {
            var serving = new Serving(Jpeg(), HttpStatusCode.OK);
            var store = new SkinImageStore(() => Client(serving), tiles, splashes);
            var skin = new SkinThumbItem { SkinId = 103000 };

            string? tile = await store.EnsureAsync(103, skin, SkinImageKind.Tile, CancellationToken.None);
            Check(tile == Path.Combine(tiles, "103000.jpg"), "A tile is cached in the tile folder under the skin id");
            Check(SkinImageStore.IsValid(tile!), "The downloaded tile is a usable image");

            int requests = serving.Requests;
            string? again = await store.EnsureAsync(103, skin, SkinImageKind.Tile, CancellationToken.None);
            Check(again == tile, "A second request answers with the cached file");
            Check(serving.Requests == requests, "A cached skin is not downloaded again");

            string? splash = await store.EnsureAsync(103, skin, SkinImageKind.Splash, CancellationToken.None);
            Check(splash == Path.Combine(splashes, "103000.jpg"), "Splash art is cached in its own folder");

            var offline = new SkinImageStore(() => null, tiles, splashes);
            Check(
                await offline.EnsureAsync(103, skin, SkinImageKind.Tile, CancellationToken.None) == tile,
                "Without a client the cached tile is still served"
            );
            Check(
                await offline.EnsureAsync(
                    103,
                    new SkinThumbItem { SkinId = 999000 },
                    SkinImageKind.Tile,
                    CancellationToken.None
                ) == null,
                "Without a client an uncached skin stays missing"
            );

            var refusing = new SkinImageStore(
                () => Client(new Serving(Jpeg(), HttpStatusCode.NotFound)),
                tiles,
                splashes
            );
            Check(
                await refusing.EnsureAsync(
                    103,
                    new SkinThumbItem { SkinId = 555000 },
                    SkinImageKind.Tile,
                    CancellationToken.None
                ) == null,
                "A skin every source refuses is not cached"
            );
            Check(
                !SkinImageStore.IsValid(Path.Combine(tiles, "555000.jpg")),
                "A refused download leaves no file behind"
            );
        }
        finally
        {
            Cleanup(tiles);
            Cleanup(splashes);
        }
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://127.0.0.1/") };

    /// <summary>A small but real JPEG, above the store's minimum size.</summary>
    private static byte[] Jpeg()
    {
        using var bitmap = new Bitmap(64, 64);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.CornflowerBlue);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private static string Temporary(string kind)
    {
        string path = Path.Combine(Path.GetTempPath(), $"league-skins-{kind}-{Guid.NewGuid():N}");
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
            Console.WriteLine("Skin image check left its temporary folder: " + path);
        }
    }

    private sealed class Serving(byte[] body, HttpStatusCode status) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}
