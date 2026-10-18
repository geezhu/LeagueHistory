using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The survival items' own sounds: which item has one, reading it from the cache folder, and refusing anything the
/// Wiki answers with that is not an Ogg file. Nothing here plays audio or uses the network; the expected values come
/// from the checks in Tests/StatisticsChecks/SurvivalSoundChecks.cs.
/// </summary>
public class ItemSoundLibraryTests
{
    private static readonly int[] Tracked =
    [
        3157,
        2420,
        3026,
        3139,
        3140,
        3102,
        3814,
        4632,
        3053,
        6673,
        3156,
        3155,
        3193,
    ];

    [Fact]
    public void Files_EverySurvivalItem_HasASound()
    {
        var tracked = SurvivalItems.Find([.. Tracked.Select(id => new LiveMatchItem(id, "", 1))]);

        Assert.DoesNotContain(tracked, item => !ItemSoundLibrary.Files.ContainsKey(SurvivalItems.BaseId(item.Id)));
    }

    [Fact]
    public async Task LoadAsync_CachedSound_ReturnsTheCachedFile()
    {
        using var folder = new TempFolder("league-item-sounds");
        byte[] wave = "RIFF-cached"u8.ToArray();
        await File.WriteAllBytesAsync(folder.File("Guardian_Angel_passive_SFX.wav"), wave);
        var library = new ItemSoundLibrary(folder.Root);

        byte[]? sound = await library.LoadAsync(223026, CancellationToken.None);

        Assert.Equal(wave, sound);
    }

    [Fact]
    public async Task LoadAsync_ItemWithoutASound_ReturnsNull()
    {
        using var folder = new TempFolder("league-item-sounds");
        var library = new ItemSoundLibrary(folder.Root);

        byte[]? sound = await library.LoadAsync(1001, CancellationToken.None);

        Assert.Null(sound);
    }

    [Fact]
    public void ToWave_PageInsteadOfAnOggFile_Throws()
    {
        byte[] page = "<html>please wait</html>"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => ItemSoundLibrary.ToWave(page));
    }

    [Fact]
    public void ToWave_EmptyResponse_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ItemSoundLibrary.ToWave([]));
    }
}
