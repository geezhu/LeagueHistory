using System.Net;
using System.Net.Http;
using League.Infrastructure;
using League.PrimaryElection;
using League.Tests.Support;
using Newtonsoft.Json;

namespace League.Tests.PrimaryElection;

/// <summary>
/// The champion manager behind the preselect lists: the lane lookups that name a champion's positions, and the cache
/// branch of the initialization. The expected values come from Tests/StatisticsChecks/ChampionDataChecks.cs and the
/// cache half of ChampionManagerChecks.cs.
///
/// The manager keeps its champions in a list that <c>AllChampions</c> hands out itself, so a test can fill it the way
/// the application does without any reflection. If that property ever started returning a copy, every test that fills
/// the list this way would quietly stop testing anything.
///
/// What cannot be reached from the public surface: <c>InitializeAsync</c> downloads through its own
/// <see cref="HttpClient"/> at a fixed DDragon URL, so the injected client is never used and the download's failure
/// paths (malformed JSON, a non-success status, no answer at all) are not driven here. The paths that read a cached
/// list are driven through the working directory the manager resolves its relative cache file against.
/// </summary>
public sealed class ChampionManagerTests : IDisposable
{
    private static readonly PrimaryChampion[] Champions =
    [
        Champion(1, "青钢影"),
        Champion(2, "披甲龙龟"),
        Champion(3, "九尾妖狐"),
        Champion(4, "戏命师"),
        Champion(5, "蒸汽机器人"),
    ];

    private readonly string _previousDirectory = Directory.GetCurrentDirectory();
    private readonly string _cacheFile;
    private readonly string _versionFile;
    private readonly StubHttpHandler _handler = new(Path.Combine(Path.GetTempPath(), "champion-manager-no-download"));

    public ChampionManagerTests()
    {
        // The manager resolves primary_champion_cache.json against the working directory. A folder beside the test
        // binary keeps that write off the user's own cache, and the empty client keeps a download from leaving.
        _cacheFile = Path.Combine(_previousDirectory, "champion-manager-cache-" + Guid.NewGuid().ToString("N"));
        _versionFile = Path.Combine(AppContext.BaseDirectory, "Assets", "ddragon_version.txt");
        Directory.CreateDirectory(_cacheFile);
        Directory.SetCurrentDirectory(_cacheFile);
    }

    public void Dispose()
    {
        ResetWorkingDirectory();
        Delete(_versionFile);
        _handler.Dispose();
        DeleteFolder(_cacheFile);
    }

    /// <summary>Puts the process back where it was before the constructor moved it.</summary>
    private void ResetWorkingDirectory() => Directory.SetCurrentDirectory(_previousDirectory);

    [Theory]
    [InlineData(ChampionPosition.Top, "青钢影")]
    [InlineData(ChampionPosition.Mid, "九尾妖狐")]
    [InlineData(ChampionPosition.Jungle, "披甲龙龟")]
    [InlineData(ChampionPosition.ADC, "戏命师")]
    [InlineData(ChampionPosition.Support, "蒸汽机器人")]
    public void GetChampionsByPosition_LaneWithOneChampion_ReturnsThatChampion(
        ChampionPosition position,
        string expected
    )
    {
        var manager = Manager(Champions);

        Assert.Equal(expected, Assert.Single(manager.GetChampionsByPosition(position)).Name);
    }

    [Fact]
    public void GetChampionsByPosition_LaneWithSeveralChampions_ReturnsThemInNameOrder()
    {
        var manager = Manager(Champion(1, "腕豪"), Champion(2, "德玛西亚之力"), Champion(3, "青钢影"));

        Assert.Equal(
            ["德玛西亚之力", "青钢影", "腕豪"],
            manager.GetChampionsByPosition(ChampionPosition.Top).Select(c => c.Name)
        );
    }

    [Theory]
    [InlineData(ChampionPosition.Top)]
    [InlineData(ChampionPosition.Jungle)]
    [InlineData(ChampionPosition.Support)]
    public void GetChampionsByPosition_LaneNoChampionPlays_IsEmpty(ChampionPosition position)
    {
        // 麦林炮手 plays mid and as a marksman, so the three other lanes have nobody in the list.
        var manager = Manager(Champion(907, "麦林炮手"));

        Assert.Empty(manager.GetChampionsByPosition(position));
    }

    [Fact]
    public void GetChampionsByPosition_All_KeepsEveryActiveChampionInTheOrderItWasAdded()
    {
        var manager = Manager(Champions);

        Assert.Equal(
            ["青钢影", "披甲龙龟", "九尾妖狐", "戏命师", "蒸汽机器人"],
            manager.GetChampionsByPosition(ChampionPosition.All).Select(c => c.Name)
        );
    }

    [Fact]
    public void GetChampionsByPosition_ChampionPlayedInTwoLanes_AppearsInBoth()
    {
        var manager = Manager(Champion(1, "潮汐海灵"));

        Assert.Equal("潮汐海灵", Assert.Single(manager.GetChampionsByPosition(ChampionPosition.Jungle)).Name);
        Assert.Equal("潮汐海灵", Assert.Single(manager.GetChampionsByPosition(ChampionPosition.Mid)).Name);
    }

    [Theory]
    [InlineData(ChampionPosition.All, 1)]
    [InlineData(ChampionPosition.Top, 0)]
    [InlineData(ChampionPosition.Mid, 0)]
    [InlineData(ChampionPosition.Jungle, 1)]
    [InlineData(ChampionPosition.ADC, 0)]
    [InlineData(ChampionPosition.Support, 0)]
    public void GetChampionsByPosition_InactiveChampion_IsLeftOut(ChampionPosition position, int expected)
    {
        // 腕豪 is a top laner and a support, 披甲龙龟 jungles, and the first one is retired.
        var manager = Manager(Champion(1, "腕豪", active: false), Champion(2, "披甲龙龟"));

        Assert.Equal(expected, manager.GetChampionsByPosition(position).Count);
    }

    [Theory]
    [InlineData(ChampionPosition.Top)]
    [InlineData(ChampionPosition.Mid)]
    [InlineData(ChampionPosition.Jungle)]
    [InlineData(ChampionPosition.ADC)]
    [InlineData(ChampionPosition.Support)]
    public void GetChampionsByPosition_ChampionInNoLane_IsLeftOutOfThatLane(ChampionPosition position)
    {
        // All 172 champions DDragon serves are in the mapping table, so the name nobody knows is the case that is left.
        var manager = Manager(Champion(999, "不存在的英雄"));

        Assert.Empty(manager.GetChampionsByPosition(position));
    }

    [Fact]
    public void GetChampionsByPosition_NoChampions_IsEmpty()
    {
        var manager = Manager();

        Assert.Empty(manager.GetChampionsByPosition(ChampionPosition.All));
    }

    [Fact]
    public void GetChampionById_KnownId_FindsTheChampion()
    {
        var manager = Manager(Champions);

        Assert.Equal("九尾妖狐", manager.GetChampionById(3)?.Name);
    }

    [Fact]
    public void GetChampionById_UnknownId_FindsNothing()
    {
        var manager = Manager(Champions);

        Assert.Null(manager.GetChampionById(999));
    }

    [Fact]
    public void AllChampions_TheListTheManagerUses_IsTheOneItPublishes()
    {
        var manager = Manager();

        manager.AllChampions.Add(Champion(42, "迅捷斥候"));

        Assert.Equal("迅捷斥候", Assert.Single(manager.GetChampionsByPosition(ChampionPosition.Support)).Name);
    }

    [Fact]
    public async Task GetChampionIconAsync_IdTheResourceTableDoesNotKnow_IsMissing()
    {
        var manager = Manager(Champions);

        Assert.Null(await manager.GetChampionIconAsync(999));
    }

    [Fact]
    public async Task InitializeAsync_MatchingCache_PublishesCachedChampionsWithoutADownload()
    {
        await WriteVersionFileAsync("15.24.1");
        await WriteCacheAsync("15.24.1", Champion(1, "腕豪"), Champion(2, "武器大师"));
        var manager = Manager();
        int loaded = 0;
        manager.ChampionsLoaded += (_, _) => loaded++;

        await manager.InitializeAsync();

        Assert.Equal((true, 2, 1), (manager.IsInitialized, manager.AllChampions.Count, loaded));
        Assert.Equal("腕豪", manager.AllChampions[0].Name);
    }

    [Fact]
    public async Task InitializeAsync_SecondCall_DoesNotLoadAgain()
    {
        await WriteVersionFileAsync("15.24.1");
        await WriteCacheAsync("15.24.1", Champion(1, "腕豪"), Champion(2, "武器大师"));
        var manager = Manager();
        int loaded = 0;
        manager.ChampionsLoaded += (_, _) => loaded++;
        await manager.InitializeAsync();

        await manager.InitializeAsync();

        Assert.Equal((2, 1), (manager.AllChampions.Count, loaded));
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// A manager whose relative cache file lands in the test's own folder. The client answers every request with a
    /// file that does not exist, so an accidental download fails at once instead of reaching the network.
    /// </summary>
    private ChampionManager Manager(params PrimaryChampion[] champions)
    {
        var manager = new ChampionManager(new HttpClient(_handler), new ResourceLoading());
        foreach (var champion in champions)
        {
            manager.AllChampions.Add(champion);
        }
        return manager;
    }

    private Task WriteCacheAsync(string version, params PrimaryChampion[] champions) =>
        File.WriteAllTextAsync(
            Path.Combine(_cacheFile, "primary_champion_cache.json"),
            JsonConvert.SerializeObject(
                new PrimaryChampionCacheDto
                {
                    DataVersion = version,
                    CacheTime = DateTime.Now,
                    Champions = champions.ToList(),
                }
            )
        );

    private Task WriteVersionFileAsync(string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_versionFile)!);
        return File.WriteAllTextAsync(_versionFile, version);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Removes the test's own folder; a file some other part of the process still holds is left behind.</summary>
    private static void DeleteFolder(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Test folder kept: " + path + " (" + e.GetType().Name + ")");
        }
    }

    private static PrimaryChampion Champion(int id, string name, bool active = true) =>
        new()
        {
            Id = id,
            Name = name,
            Alias = name,
            IsActive = active,
        };
}
