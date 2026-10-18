using System.Net;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json;

namespace League.Tests.Services;

/// <summary>The string table download, against a stub host serving the fixture's excerpt.</summary>
public sealed class SpellTextDataTests : IDisposable
{
    private const string Host = "https://cdragon.test";
    private readonly TempFolder _folder = new("spell-text");
    private readonly StubHttpHandler _handler = new(CommunityDragonFixture.AnnieStringTablePath);

    public void Dispose()
    {
        _handler.Dispose();
        _folder.Dispose();
    }

    private SpellTextData Data(HttpMessageHandler handler) =>
        new(_folder.Root, "16.20", new HttpClient(handler, disposeHandler: false), Host);

    [Fact]
    public async Task ReadTooltipsAsync_StringTable_KeepsOnlySpellTooltips()
    {
        await using var reader = new JsonTextReader(File.OpenText(CommunityDragonFixture.AnnieStringTablePath));

        var texts = await SpellTextData.ReadTooltipsAsync(reader, CancellationToken.None);

        Assert.Equal(
            [
                "spell_anniee_tooltip",
                "spell_anniepassive_tooltip",
                "spell_annieq_tooltip",
                "spell_annier_tooltip",
                "spell_anniew_tooltip",
            ],
            texts.Keys.Order()
        );
    }

    [Theory]
    [InlineData("spell_annieq_tooltip", true)]
    [InlineData("game_character_passivedescription_vayne", true)]
    [InlineData("generatedtip_spell_annieq_tooltip", false)]
    [InlineData("tft_item_tooltip", false)]
    [InlineData("spell_annieq_name", false)]
    public void Keeps_Key_KeepsSpellTooltipsOnly(string key, bool kept)
    {
        var result = SpellTextData.Keeps(key);

        Assert.Equal(kept, result);
    }

    [Fact]
    public async Task LoadAsync_FirstRead_AsksForThePatchesChineseTable()
    {
        await Data(_handler).LoadAsync(CancellationToken.None);

        Assert.Equal(
            $"{Host}/16.20/game/zh_cn/data/menu/en_us/lol.stringtable.json",
            Assert.Single(_handler.Requests).ToString()
        );
    }

    [Fact]
    public async Task LoadAsync_SecondRead_UsesTheCacheWithoutARequest()
    {
        await Data(_handler).LoadAsync(CancellationToken.None);
        using var second = new StubHttpHandler(CommunityDragonFixture.AnnieStringTablePath);

        var texts = await Data(second).LoadAsync(CancellationToken.None);

        Assert.Equal((5, 0), (texts.Count, second.Requests.Count));
    }

    [Fact]
    public async Task LoadAsync_ServerError_ThrowsAndCachesNothing()
    {
        using var failing = new StubHttpHandler(CommunityDragonFixture.AnnieStringTablePath, HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(() => Data(failing).LoadAsync(CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_folder.Root));
    }

    [Fact]
    public async Task LoadAsync_Cancelled_ThrowsAndCachesNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Data(_handler).LoadAsync(cancelled.Token));

        Assert.Empty(Directory.GetFiles(_folder.Root));
    }
}
