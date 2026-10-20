using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Data Dragon's champion stats, kept per client version in a folder of their own.
///
/// The reason it is a file rather than a request is that Data Dragon is a web service and the panel needs the numbers
/// the moment a game starts; so a version is fetched once and read back for as long as the client stays on it.
///
/// Only the cached path is covered here. The fetching path calls the real ddragon host and then writes the file
/// atomically - and there is no seam for the request, so triggering it would mean talking to the network. What is
/// covered is what the program does on every run after the first: read the file, parse it, and refuse garbage.
/// </summary>
public class ChampionBaseStatsDataTests : IDisposable
{
    private const string Version = "16.20.1";

    private readonly TempFolder _folder = new("ddragon");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Folder() => _folder.File("cache-" + Guid.NewGuid().ToString("N"));

    private static string ChampionList(params (int Id, double Health)[] champions) =>
        new JObject
        {
            ["data"] = new JObject(
                champions
                    .Select(c => new JProperty(
                        "Champion" + c.Id,
                        new JObject
                        {
                            ["key"] = c.Id.ToString(),
                            ["stats"] = new JObject
                            {
                                ["hp"] = c.Health,
                                ["hpperlevel"] = 90,
                                ["armor"] = 30,
                                ["armorperlevel"] = 4,
                                ["spellblock"] = 32,
                                ["spellblockperlevel"] = 1.3,
                            },
                        }
                    ))
                    .ToArray()
            ),
        }.ToString();

    /// <summary>Writes the cache file the loader looks for, then hands back a loader pointed at that folder.</summary>
    private ChampionBaseStatsData Cached(string json, string version = Version)
    {
        string folder = Folder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, version + ".json"), json);
        return new ChampionBaseStatsData(folder, version);
    }

    // ---- reading the cache ----

    [Fact]
    public async Task LoadAsync_TheCachedFile_IsReadWithoutAskingAnywhere()
    {
        ChampionBaseStatsData data = Cached(ChampionList((1, 600)));

        Dictionary<int, ChampionBaseStats> stats = await data.LoadAsync(CancellationToken.None);

        Assert.Equal(600, stats[1].Health, 3);
    }

    [Fact]
    public async Task LoadAsync_EveryChampionInTheFile_IsThere()
    {
        ChampionBaseStatsData data = Cached(ChampionList((1, 600), (2, 500), (3, 700)));

        Dictionary<int, ChampionBaseStats> stats = await data.LoadAsync(CancellationToken.None);

        Assert.Equal(3, stats.Count);
        Assert.Equal(500, stats[2].Health, 3);
    }

    [Fact]
    public async Task LoadAsync_TheOtherNumbers_AreReadToo()
    {
        // Health is not the only field the estimate needs; the per-level growth and the resistances matter as much.
        ChampionBaseStatsData data = Cached(ChampionList((1, 600)));

        ChampionBaseStats stats = (await data.LoadAsync(CancellationToken.None))[1];

        Assert.Equal(90, stats.HealthPerLevel, 3);
        Assert.Equal(30, stats.Armor, 3);
        Assert.Equal(4, stats.ArmorPerLevel, 3);
        Assert.Equal(32, stats.MagicResist, 3);
    }

    [Fact]
    public async Task LoadAsync_EachVersion_HasItsOwnFile()
    {
        // The client changes version and the old file is not stale for the new one; it is simply not the one read.
        ChampionBaseStatsData data = Cached(ChampionList((1, 600)), version: "16.21.1");

        Dictionary<int, ChampionBaseStats> stats = await data.LoadAsync(CancellationToken.None);

        Assert.Equal(600, stats[1].Health, 3);
    }

    [Fact]
    public async Task LoadAsync_AnEntryWithoutAKey_IsLeftOut()
    {
        // The key is the champion id; without it there is nothing to look the entry up by.
        var list = JObject.Parse(ChampionList((1, 600)));
        ((JObject)list["data"]!).Add("Broken", new JObject { ["stats"] = new JObject { ["hp"] = 1 } });
        ChampionBaseStatsData data = Cached(list.ToString());

        Dictionary<int, ChampionBaseStats> stats = await data.LoadAsync(CancellationToken.None);

        Assert.Single(stats);
    }

    [Fact]
    public async Task LoadAsync_AnEntryWithoutStats_IsLeftOut()
    {
        var list = JObject.Parse(ChampionList((1, 600)));
        ((JObject)list["data"]!).Add("Broken", new JObject { ["key"] = "99" });
        ChampionBaseStatsData data = Cached(list.ToString());

        Assert.Single(await data.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_AnEmptyFile_IsAnEmptyCatalog()
    {
        // No champions is a valid answer; the panel simply has no estimate to show.
        ChampionBaseStatsData data = Cached(new JObject().ToString());

        Assert.Empty(await data.LoadAsync(CancellationToken.None));
    }

    // ---- garbage in the cache ----

    [Fact]
    public async Task LoadAsync_ACacheFileThatIsNotJson_Throws()
    {
        // Nothing guards the parse, so a half-written file is an exception rather than a silent empty catalog.
        ChampionBaseStatsData data = Cached("not json at all");

        await Assert.ThrowsAnyAsync<Newtonsoft.Json.JsonException>(() => data.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_ACacheFileThatIsAList_Throws()
    {
        ChampionBaseStatsData data = Cached("[1, 2, 3]");

        await Assert.ThrowsAnyAsync<Newtonsoft.Json.JsonException>(() => data.LoadAsync(CancellationToken.None));
    }

    // ---- what the estimate does with it ----

    [Fact]
    public void Grown_AtLevelOne_IsTheBaseValue()
    {
        // Traced: no steps, so the growth term is zero and the base value stands.
        Assert.Equal(600, ChampionBaseStats.Grown(600, 90, 1), 3);
    }

    [Fact]
    public void Grown_AtLevelEleven_AddsTheCurvedGrowth()
    {
        // Traced: ten steps, so 600 + 90 × 10 × (0.7025 + 0.175) = 600 + 789.75.
        Assert.Equal(600 + 90 * 10 * (0.7025 + 0.0175 * 10), ChampionBaseStats.Grown(600, 90, 11), 3);
    }

    [Fact]
    public void Grown_BelowLevelOne_IsTheBaseValueToo()
    {
        // A level that is not a level must not subtract health.
        Assert.Equal(600, ChampionBaseStats.Grown(600, 90, 0), 3);
        Assert.Equal(600, ChampionBaseStats.Grown(600, 90, -5), 3);
    }
}
