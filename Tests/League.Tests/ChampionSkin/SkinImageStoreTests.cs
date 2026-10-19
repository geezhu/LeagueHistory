using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using League.ChampionSkin;
using League.Models;
using League.Tests.Support;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The skin image cache: which client paths are tried for a tile or a splash, what counts as a usable file, and the
/// download that writes one. The transport is a stub handler and both directories are temporary, so no request leaves
/// the process and the user's own cache is never read or written. The offered bytes come from a bitmap this test
/// paints, never from a real skin file.
/// </summary>
public class SkinImageStoreTests
{
    private const string Host = "https://127.0.0.1";

    private const string TileLocation = "/lol-game-data/assets/v1/champion-tiles/103/103000.jpg";
    private const string SplashLocation = "/lol-game-data/assets/v1/champion-splashes/103/103000.jpg";
    private const string UncentredSplashLocation =
        "/lol-game-data/assets/v1/champion-splashes/uncentered/103/103000.jpg";

    [Theory]
    [InlineData(103, 103000, "/lol-game-data/assets/v1/champion-tiles/103/103000.jpg")]
    [InlineData(1, 1000, "/lol-game-data/assets/v1/champion-tiles/1/1000.jpg")]
    public void Sources_TileWithoutItsOwnPath_UsesTheStandardLocationOfBothIds(int championId, int skinId, string path)
    {
        List<string> sources = SkinImageStore.Sources(championId, Skin(skinId), SkinImageKind.Tile);

        Assert.Equal(new[] { path }, sources);
    }

    [Fact]
    public void Sources_TileWithItsOwnPath_TriesThatPathFirstAndKeepsTheStandardFallback()
    {
        var skin = new SkinThumbItem
        {
            SkinId = 103000,
            TilePath = "assets/characters/ahri/skins/base/images/ahri_square_0.jpg",
        };

        List<string> sources = SkinImageStore.Sources(103, skin, SkinImageKind.Tile);

        Assert.Equal(
            new[]
            {
                "/assets/characters/ahri/skins/base/images/ahri_square_0.jpg",
                "/lol-game-data/assets/v1/champion-tiles/103/103000.jpg",
            },
            sources
        );
    }

    [Fact]
    public void Sources_OwnPathThatAlreadyStartsWithASlash_IsKeptWithoutASecondSlash()
    {
        var skin = new SkinThumbItem { SkinId = 103000, TilePath = "/inventory/ahri_square_0.jpg" };

        List<string> sources = SkinImageStore.Sources(103, skin, SkinImageKind.Tile);

        Assert.Equal("/inventory/ahri_square_0.jpg", sources[0]);
    }

    [Fact]
    public void Sources_TilePathOfOnlySpaces_IsIgnoredAndOnlyTheStandardLocationIsUsed()
    {
        var skin = new SkinThumbItem { SkinId = 103000, TilePath = "   " };

        List<string> sources = SkinImageStore.Sources(103, skin, SkinImageKind.Tile);

        Assert.Equal(new[] { "/lol-game-data/assets/v1/champion-tiles/103/103000.jpg" }, sources);
    }

    [Fact]
    public void Sources_OwnPathEqualToTheStandardLocationInDifferentCase_IsListedOnlyOnce()
    {
        var skin = new SkinThumbItem
        {
            SkinId = 103000,
            TilePath = "LOL-GAME-DATA/assets/v1/champion-tiles/103/103000.jpg",
        };

        List<string> sources = SkinImageStore.Sources(103, skin, SkinImageKind.Tile);

        Assert.Equal(new[] { "/LOL-GAME-DATA/assets/v1/champion-tiles/103/103000.jpg" }, sources);
    }

    [Fact]
    public void Sources_SplashWithoutItsOwnPath_TriesTheCentredPathBeforeTheUncentredOne()
    {
        List<string> sources = SkinImageStore.Sources(103, Skin(103000), SkinImageKind.Splash);

        Assert.Equal(
            new[]
            {
                "/lol-game-data/assets/v1/champion-splashes/103/103000.jpg",
                "/lol-game-data/assets/v1/champion-splashes/uncentered/103/103000.jpg",
            },
            sources
        );
    }

    [Fact]
    public void Sources_SplashWithItsOwnPath_TriesThatPathBeforeBothStandardFallbacks()
    {
        var skin = new SkinThumbItem { SkinId = 99001, SplashPath = "lol-game-data/assets/x.jpg" };

        List<string> sources = SkinImageStore.Sources(99, skin, SkinImageKind.Splash);

        Assert.Equal(
            new[]
            {
                "/lol-game-data/assets/x.jpg",
                "/lol-game-data/assets/v1/champion-splashes/99/99001.jpg",
                "/lol-game-data/assets/v1/champion-splashes/uncentered/99/99001.jpg",
            },
            sources
        );
    }

    [Fact]
    public void Sources_SplashOfASkinThatNamesOnlyItsTile_IgnoresTheTilePath()
    {
        var skin = new SkinThumbItem { SkinId = 103000, TilePath = "/inventory/ahri_square_0.jpg" };

        List<string> sources = SkinImageStore.Sources(103, skin, SkinImageKind.Splash);

        Assert.Equal(
            new[]
            {
                "/lol-game-data/assets/v1/champion-splashes/103/103000.jpg",
                "/lol-game-data/assets/v1/champion-splashes/uncentered/103/103000.jpg",
            },
            sources
        );
    }

    [Fact]
    public void FilePath_Tile_IsTheSkinIdInsideTheInjectedTileDirectory()
    {
        using var tiles = new TempFolder("league-skin-tile-path");
        using var splashes = new TempFolder("league-skin-tile-path-splashes");
        var store = new SkinImageStore(() => null, tiles.Root, splashes.Root);

        string path = store.FilePath(103000, SkinImageKind.Tile);

        Assert.Equal(Path.Combine(tiles.Root, "103000.jpg"), path);
    }

    [Fact]
    public void FilePath_Splash_IsTheSkinIdInsideTheInjectedSplashDirectory()
    {
        using var tiles = new TempFolder("league-skin-splash-path");
        using var splashes = new TempFolder("league-skin-splash-path-splashes");
        var store = new SkinImageStore(() => null, tiles.Root, splashes.Root);

        string path = store.FilePath(103000, SkinImageKind.Splash);

        Assert.Equal(Path.Combine(splashes.Root, "103000.jpg"), path);
    }

    [Fact]
    public void IsValid_MissingFile_IsFalse()
    {
        using var folder = new TempFolder("league-skin-missing");

        Assert.False(SkinImageStore.IsValid(folder.File("103000.jpg")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public async Task IsValid_FileBelowTheMinimumSize_IsFalse(int length)
    {
        using var folder = new TempFolder("league-skin-too-small");
        string path = folder.File("103000.jpg");
        await File.WriteAllBytesAsync(path, new byte[length]);

        Assert.False(SkinImageStore.IsValid(path));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(65536)]
    public async Task IsValid_FileAtOrAboveTheMinimumSizeOfNonImageBytes_IsTrue(int length)
    {
        using var folder = new TempFolder("league-skin-large-enough");
        string path = folder.File("103000.jpg");
        await File.WriteAllBytesAsync(path, new byte[length]);

        // The rule is existence plus size; nothing here is a JPEG, and no signature or decode check is made.
        Assert.True(SkinImageStore.IsValid(path), "The usable-file rule is existence plus a minimum size");
    }

    [Fact]
    public async Task EnsureAsync_CachedTileLargeEnough_ServesItWithoutAskingTheClient()
    {
        using var tiles = new TempFolder("league-skin-cached");
        using var splashes = new TempFolder("league-skin-cached-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, new byte[4096]));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);
        string cached = Path.Combine(tiles.Root, "103000.jpg");
        await File.WriteAllBytesAsync(cached, new byte[4096]);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(cached, path);
        Assert.Empty(transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_UncachedSkinWithoutAClient_ReturnsNull()
    {
        using var tiles = new TempFolder("league-skin-offline");
        using var splashes = new TempFolder("league-skin-offline-splashes");
        var store = new SkinImageStore(() => null, tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(999000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Null(path);
    }

    [Fact]
    public async Task EnsureAsync_TileWithoutItsOwnPath_DownloadsTheStandardLocation()
    {
        using var tiles = new TempFolder("league-skin-tile-download");
        using var splashes = new TempFolder("league-skin-tile-download-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 64)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(Path.Combine(tiles.Root, "103000.jpg"), path);
        Assert.Equal(new[] { Host + TileLocation }, transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_TileDownloaded_IsAUsableImageAndLeavesNoTemporaryFile()
    {
        using var tiles = new TempFolder("league-skin-tile-written");
        using var splashes = new TempFolder("league-skin-tile-written-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 64)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.True(SkinImageStore.IsValid(path!), "The downloaded tile is a usable image");
        Assert.Empty(Directory.EnumerateFiles(tiles.Root, "*.tmp"));
    }

    [Fact]
    public async Task EnsureAsync_TileRequestedTwice_IsDownloadedOnlyOnce()
    {
        using var tiles = new TempFolder("league-skin-tile-reuse");
        using var splashes = new TempFolder("league-skin-tile-reuse-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 64)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? first = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);
        string? second = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_TwoRequestsForTheSameSkinAtOnce_DownloadItOnce()
    {
        using var tiles = new TempFolder("league-skin-tile-concurrent");
        using var splashes = new TempFolder("league-skin-tile-concurrent-splashes");
        var transport = new GatedTransport(Jpeg(64, 64));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        Task<string?> first = store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);
        await transport.Entered.Task;
        Task<string?> second = store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);
        transport.Release();

        string expected = Path.Combine(tiles.Root, "103000.jpg");
        Assert.Equal(new string?[] { expected, expected }, await Task.WhenAll(first, second));
        Assert.Equal(1, transport.Requests);
    }

    [Fact]
    public async Task EnsureAsync_CachedFileBelowTheMinimumSize_IsDownloadedAgain()
    {
        using var tiles = new TempFolder("league-skin-tile-stale");
        using var splashes = new TempFolder("league-skin-tile-stale-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 64)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);
        string path = Path.Combine(tiles.Root, "103000.jpg");
        await File.WriteAllBytesAsync(path, new byte[255]);

        string? result = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(path, result);
        Assert.True(SkinImageStore.IsValid(path), "A file below the minimum size is replaced by a real download");
        Assert.Single(transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_FirstCandidateRefused_FallsBackToTheStandardLocation()
    {
        using var tiles = new TempFolder("league-skin-tile-fallback");
        using var splashes = new TempFolder("league-skin-tile-fallback-splashes");
        var transport = new StubTransport(url =>
            url.Contains("/champion-tiles/", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, Jpeg(64, 64))
                : (HttpStatusCode.NotFound, [])
        );
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);
        var skin = new SkinThumbItem { SkinId = 103000, TilePath = "assets/characters/ahri/missing.jpg" };

        string? path = await store.EnsureAsync(103, skin, SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(Path.Combine(tiles.Root, "103000.jpg"), path);
        Assert.Equal(new[] { Host + "/assets/characters/ahri/missing.jpg", Host + TileLocation }, transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_CandidateBelowTheMinimumSize_FallsBackToTheStandardLocation()
    {
        using var tiles = new TempFolder("league-skin-tile-short");
        using var splashes = new TempFolder("league-skin-tile-short-splashes");
        var transport = new StubTransport(url =>
            url.Contains("/champion-tiles/", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, Jpeg(64, 64))
                : (HttpStatusCode.OK, new byte[255])
        );
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);
        var skin = new SkinThumbItem { SkinId = 103000, TilePath = "assets/characters/ahri/short.jpg" };

        string? path = await store.EnsureAsync(103, skin, SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(Path.Combine(tiles.Root, "103000.jpg"), path);
        Assert.Equal(new[] { Host + "/assets/characters/ahri/short.jpg", Host + TileLocation }, transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_EveryCandidateRefused_ReturnsNullAndWritesNothing()
    {
        using var tiles = new TempFolder("league-skin-tile-refused");
        using var splashes = new TempFolder("league-skin-tile-refused-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.NotFound, []));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(555000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Null(path);
        Assert.Empty(Directory.EnumerateFiles(tiles.Root));
    }

    [Fact]
    public async Task EnsureAsync_BodyThatIsNotADecodableImage_ReturnsNullAndLeavesNoPartialFile()
    {
        using var tiles = new TempFolder("league-skin-tile-undecodable");
        using var splashes = new TempFolder("league-skin-tile-undecodable-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, new byte[4096]));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Null(path);
        Assert.Empty(Directory.EnumerateFiles(tiles.Root));
    }

    [Fact]
    public async Task EnsureAsync_TileLargerThanTheCap_IsWrittenAtTwoHundredAndFiftySixOnTheLongEdge()
    {
        using var tiles = new TempFolder("league-skin-tile-cap");
        using var splashes = new TempFolder("league-skin-tile-cap-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(512, 512)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(new Size(256, 256), PixelSize(path!));
    }

    [Fact]
    public async Task EnsureAsync_TileSmallerThanTheCap_IsWrittenAtItsOwnSize()
    {
        using var tiles = new TempFolder("league-skin-tile-small");
        using var splashes = new TempFolder("league-skin-tile-small-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 48)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, CancellationToken.None);

        Assert.Equal(new Size(64, 48), PixelSize(path!));
    }

    [Fact]
    public async Task EnsureAsync_SplashWithoutItsOwnPath_DownloadsTheCentredLocationIntoTheSplashDirectory()
    {
        using var tiles = new TempFolder("league-skin-splash-download");
        using var splashes = new TempFolder("league-skin-splash-download-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 64)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Splash, CancellationToken.None);

        Assert.Equal(Path.Combine(splashes.Root, "103000.jpg"), path);
        Assert.Equal(new[] { Host + SplashLocation }, transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_CentredSplashRefused_FallsBackToTheUncentredLocation()
    {
        using var tiles = new TempFolder("league-skin-splash-fallback");
        using var splashes = new TempFolder("league-skin-splash-fallback-splashes");
        var transport = new StubTransport(url =>
            url.EndsWith("/uncentered/103/103000.jpg", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, Jpeg(64, 64))
                : (HttpStatusCode.NotFound, [])
        );
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Splash, CancellationToken.None);

        Assert.Equal(Path.Combine(splashes.Root, "103000.jpg"), path);
        Assert.Equal(new[] { Host + SplashLocation, Host + UncentredSplashLocation }, transport.Urls);
    }

    [Fact]
    public async Task EnsureAsync_SplashLargerThanTheCap_IsWrittenUnderTheCapWithItsAspectRatio()
    {
        using var tiles = new TempFolder("league-skin-splash-cap");
        using var splashes = new TempFolder("league-skin-splash-cap-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(1920, 1080)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        string? path = await store.EnsureAsync(103, Skin(103000), SkinImageKind.Splash, CancellationToken.None);

        Assert.Equal(new Size(1280, 720), PixelSize(path!));
    }

    [Fact]
    public async Task EnsureAsync_CancelledBeforeTheFirstRequest_ThrowsWithoutWritingAnything()
    {
        using var tiles = new TempFolder("league-skin-tile-cancelled");
        using var splashes = new TempFolder("league-skin-tile-cancelled-splashes");
        var transport = new StubTransport(_ => (HttpStatusCode.OK, Jpeg(64, 64)));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, new CancellationToken(canceled: true))
        );

        Assert.Empty(transport.Urls);
        Assert.Empty(Directory.EnumerateFiles(tiles.Root));
    }

    [Fact]
    public async Task EnsureAsync_CancelledWhileTheDownloadIsInFlight_ThrowsAndLeavesNoPartialFile()
    {
        using var tiles = new TempFolder("league-skin-tile-aborted");
        using var splashes = new TempFolder("league-skin-tile-aborted-splashes");
        using var cancellation = new CancellationTokenSource();
        var transport = new GatedTransport(Jpeg(64, 64));
        var store = new SkinImageStore(() => Client(transport), tiles.Root, splashes.Root);

        Task<string?> download = store.EnsureAsync(103, Skin(103000), SkinImageKind.Tile, cancellation.Token);
        await transport.Entered.Task;
        await cancellation.CancelAsync();
        transport.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);

        Assert.Empty(Directory.EnumerateFiles(tiles.Root));
    }

    private static SkinThumbItem Skin(int skinId) => new() { SkinId = skinId };

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri(Host) };

    /// <summary>The pixel size of a file the store wrote; a fresh stream keeps the file unlocked.</summary>
    private static Size PixelSize(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var image = Image.FromStream(stream);
        return image.Size;
    }

    /// <summary>A small but real JPEG the store can decode, scale and write.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.CornflowerBlue);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    /// <summary>Answers every request from one function and records the URLs; no request leaves the process.</summary>
    private sealed class StubTransport(Func<string, (HttpStatusCode Status, byte[] Body)> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            Urls.Add(url);
            var (status, body) = reply(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    /// <summary>Holds the first answer until the test releases it, so a second request can overtake it.</summary>
    private sealed class GatedTransport(byte[] body) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Requests { get; private set; }

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Requests++;
            Entered.TrySetResult();
            await _gate.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        }
    }
}
