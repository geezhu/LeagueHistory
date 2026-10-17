using System.Reflection;
using League.Infrastructure;
using League.PrimaryElection;
using Newtonsoft.Json;
using static StatisticsCheckSupport;

/// <summary>
/// The champion manager behind the preselect lists: the local DDragon version file, the versioned cache on disk, the
/// position mapping report and the icon lookup. Both paths point at temporary files, so the user's cache, the real
/// Assets folder and the DDragon service are never touched.
/// </summary>
internal static class ChampionManagerChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo CacheFile = typeof(ChampionManager).GetField("_cacheFilePath", Flags)!;
    private static readonly FieldInfo VersionFile = typeof(ChampionManager).GetField("_versionFilePath", Flags)!;
    private static readonly FieldInfo ChampionsField = typeof(ChampionManager).GetField("_allChampions", Flags)!;
    private static readonly MethodInfo ReadVersion = typeof(ChampionManager).GetMethod("ReadLocalVersion", Flags)!;
    private static readonly MethodInfo ValidateMapping = typeof(ChampionManager).GetMethod(
        "ValidatePositionMapping",
        Flags
    )!;
    private static readonly MethodInfo CacheLoad = typeof(ChampionManager).GetMethod("TryLoadFromCacheAsync", Flags)!;
    private static readonly MethodInfo CacheSave = typeof(ChampionManager).GetMethod("SaveToCacheAsync", Flags)!;

    public static void Run() => Task.Run(RunAsync).GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        string root = Temporary();
        try
        {
            await VersionAsync(root);
            await CacheReadAsync(root);
            await CacheWriteAsync(root);
            await InitializationAsync(root);
            await MappingAndIconAsync(root);
        }
        finally
        {
            Cleanup(root);
        }

        Console.WriteLine(
            "PASS: the champion manager reads its local version file, accepts and rejects its versioned cache, writes "
                + "one it can and survives one it cannot, reports the position mapping and looks up an icon. No "
                + "download and no real cache file."
        );
    }

    // ---------------------------------------------------------------- the local version file

    private static async Task VersionAsync(string root)
    {
        var manager = Manager(root, "version");
        Check(Version(manager) == "", "Without a version file the local version is empty");

        string path = Path.Combine(root, "version", "ddragon_version.txt");
        await File.WriteAllTextAsync(path, "  15.24.1 \r\n");
        Check(Version(manager) == "15.24.1", "The local version is trimmed");

        CheckLockedVersionFile(manager, path);
    }

    /// <summary>Holds the file open so the read fails the way a file in use does.</summary>
    private static void CheckLockedVersionFile(ChampionManager manager, string path)
    {
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Check(Version(manager) == "", "A version file that cannot be read falls back to an empty version");
    }

    // ---------------------------------------------------------------- reading the cache

    private static async Task CacheReadAsync(string root)
    {
        var manager = Manager(root, "read");
        string path = Path.Combine(root, "read", "primary_champion_cache.json");
        Check(!await Load(manager, "15.24.1"), "A cache file that was never written cannot be used");

        await File.WriteAllTextAsync(path, "this is not json");
        Check(!await Load(manager, "15.24.1"), "A cache file that is not JSON is ignored");
        Check(manager.AllChampions.Count == 0, "A cache that cannot be parsed publishes nothing");

        await WriteCacheAsync(path, "15.24.1");
        Check(!await Load(manager, "15.24.1"), "A cache file without champions is ignored");

        await WriteCacheAsync(path, "15.24.1", Champion(1, "腕豪"));
        Check(!await Load(manager, "9.9.9"), "A cache from another data version is refreshed");
        Check(manager.AllChampions.Count == 0, "A cache from another version is not published");

        Check(await Load(manager, "15.24.1"), "A cache with the local version is used");
        Check(
            manager.AllChampions.Count == 1 && manager.AllChampions[0].Name == "腕豪",
            "The cached champions are published"
        );

        await WriteCacheAsync(path, "AbC", Champion(2, "武器大师"));
        Check(await Load(manager, "aBc"), "The version comparison ignores case");
        Check(manager.AllChampions[0].Id == 2, "The second cached list replaces the first");
    }

    // ---------------------------------------------------------------- writing the cache

    private static async Task CacheWriteAsync(string root)
    {
        var manager = Manager(root, "write");
        ChampionsField.SetValue(manager, new List<PrimaryChampion> { Champion(7, "武器大师") });
        await Save(manager, "15.24.1");

        string path = Path.Combine(root, "write", "primary_champion_cache.json");
        var saved = JsonConvert.DeserializeObject<PrimaryChampionCacheDto>(await File.ReadAllTextAsync(path))!;
        Check(
            saved.DataVersion == "15.24.1" && saved.Champions.Count == 1 && saved.Champions[0].Id == 7,
            "Saving writes the data version and the champion list"
        );

        CacheFile.SetValue(manager, Path.Combine(root, "write", "absent", "cache.json"));
        await Save(manager, "15.24.1");
        Check(
            !Directory.Exists(Path.Combine(root, "write", "absent")),
            "A cache that cannot be written is logged instead of thrown"
        );
    }

    // ---------------------------------------------------------------- the whole initialization

    private static async Task InitializationAsync(string root)
    {
        var manager = Manager(root, "init");
        await File.WriteAllTextAsync(Path.Combine(root, "init", "ddragon_version.txt"), "15.24.1");
        await WriteCacheAsync(
            Path.Combine(root, "init", "primary_champion_cache.json"),
            "15.24.1",
            Champion(1, "腕豪"),
            Champion(2, "武器大师")
        );

        int loaded = 0;
        manager.ChampionsLoaded += (_, _) => loaded++;
        await manager.InitializeAsync();
        Check(manager.IsInitialized, "A cache with the local version initializes the manager");
        Check(
            manager.AllChampions.Count == 2 && manager.AllChampions[0].Name == "腕豪",
            "The champions come from the cache without a download"
        );
        Check(loaded == 1, "The champions loaded event is raised once");

        await manager.InitializeAsync();
        Check(loaded == 1 && manager.AllChampions.Count == 2, "An initialized manager does not load a second time");
    }

    // ---------------------------------------------------------------- mapping and icons

    private static async Task MappingAndIconAsync(string root)
    {
        var manager = Manager(root, "mapping");
        ChampionsField.SetValue(
            manager,
            new List<PrimaryChampion> { Champion(1, "腕豪"), Champion(2, "不在位置表里的英雄") }
        );
        ValidateMapping.Invoke(manager, null);
        Check(manager.GetChampionById(1)?.Name == "腕豪", "The mapping report leaves the champions in place");

        Check(manager.GetChampionById(999) == null, "An unknown champion id finds nothing");
        Check(await manager.GetChampionIconAsync(1) == null, "An icon the resource table does not know is missing");
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>A manager whose version and cache files both live in the given temporary folder.</summary>
    private static ChampionManager Manager(string root, string name)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        var manager = new ChampionManager(new HttpClient(), new ResourceLoading());
        CacheFile.SetValue(manager, Path.Combine(folder, "primary_champion_cache.json"));
        VersionFile.SetValue(manager, Path.Combine(folder, "ddragon_version.txt"));
        return manager;
    }

    private static async Task WriteCacheAsync(string path, string version, params PrimaryChampion[] champions) =>
        await File.WriteAllTextAsync(
            path,
            JsonConvert.SerializeObject(
                new PrimaryChampionCacheDto
                {
                    DataVersion = version,
                    CacheTime = DateTime.Now,
                    Champions = champions.ToList(),
                }
            )
        );

    private static PrimaryChampion Champion(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Alias = name,
            IsActive = true,
        };

    private static string Version(ChampionManager manager) => (string)ReadVersion.Invoke(manager, null)!;

    private static async Task<bool> Load(ChampionManager manager, string version) =>
        await (Task<bool>)CacheLoad.Invoke(manager, [version])!;

    private static async Task Save(ChampionManager manager, string version) =>
        await (Task)CacheSave.Invoke(manager, [version])!;

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "league-champion-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException e)
        {
            Console.WriteLine("Test folder kept: " + path + " (" + e.GetType().Name + ")");
        }
    }
}
