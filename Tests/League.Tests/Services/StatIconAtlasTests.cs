using System.Drawing.Imaging;
using System.Net;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Cutting the game's stats panel atlas into one icon per stat. The transport serves a generated atlas in place of
/// CommunityDragon's, and the downloaded copy goes to a folder of the test's own.
/// </summary>
public class StatIconAtlasTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(),
        "league-stat-icons-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private StatIconAtlas Atlas(HttpMessageHandler handler) =>
        new(_cache, new HttpClient(handler), "https://cdn.invalid/statspanel_atlas.png");

    private static byte[] Png(int size)
    {
        using var image = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(image);
        // The critical chance cell, red so the cut can be told from the rest.
        graphics.FillRectangle(Brushes.Red, 233, 489, StatIconAtlas.IconSize, StatIconAtlas.IconSize);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap Decode(string base64) => new(new MemoryStream(Convert.FromBase64String(base64)));

    [Fact]
    public void Cut_TheAtlas_GivesEachIconAtItsCell()
    {
        using var atlas = Decode(Convert.ToBase64String(Png(StatIconAtlas.AtlasSize)));

        using var crit = Decode(StatIconAtlas.Cut(atlas)["crit"]);

        Assert.Equal(Color.Red.ToArgb(), crit.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void Cut_TheAtlas_GivesTwentyPixelIcons()
    {
        using var atlas = new Bitmap(StatIconAtlas.AtlasSize, StatIconAtlas.AtlasSize);

        using var icon = Decode(StatIconAtlas.Cut(atlas)["ad"]);

        Assert.Equal(StatIconAtlas.IconSize, icon.Width);
    }

    [Fact]
    public void Cut_AnAtlasOfAnotherSize_GivesNoIcons()
    {
        using var atlas = new Bitmap(256, 256);

        Assert.Empty(StatIconAtlas.Cut(atlas));
    }

    [Fact]
    public async Task LoadAsync_ASecondInstance_ReadsTheDownloadedCopy()
    {
        await Atlas(new Answer(HttpStatusCode.OK, Png(StatIconAtlas.AtlasSize))).LoadAsync(CancellationToken.None);
        var offline = new Answer(HttpStatusCode.ServiceUnavailable, []);

        var icons = await Atlas(offline).LoadAsync(CancellationToken.None);

        Assert.Contains("crit", icons.Keys);
        Assert.Equal(0, offline.Requests);
    }

    [Fact]
    public async Task LoadAsync_TheDownloadFails_GivesNoIcons()
    {
        var icons = await Atlas(new Answer(HttpStatusCode.ServiceUnavailable, [])).LoadAsync(CancellationToken.None);

        Assert.Empty(icons);
    }

    [Fact]
    public async Task LoadAsync_AfterAFailure_AsksAgain()
    {
        var answer = new Answer(HttpStatusCode.ServiceUnavailable, []);
        var atlas = Atlas(answer);
        await atlas.LoadAsync(CancellationToken.None);

        await atlas.LoadAsync(CancellationToken.None);

        Assert.Equal(2, answer.Requests);
    }

    [Fact]
    public async Task LoadAsync_ADamagedCopy_IsDownloadedAgain()
    {
        Directory.CreateDirectory(_cache);
        await File.WriteAllTextAsync(Path.Combine(_cache, "statspanel_atlas.png"), "not an image");
        await Atlas(new Answer(HttpStatusCode.OK, [])).LoadAsync(CancellationToken.None);

        var icons = await Atlas(new Answer(HttpStatusCode.OK, Png(StatIconAtlas.AtlasSize)))
            .LoadAsync(CancellationToken.None);

        Assert.Contains("crit", icons.Keys);
    }

    private sealed class Answer(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}
