using System.Text;
using System.Text.Json;
using League.Managers;
using League.Models;
using League.Tests.Support;

namespace League.Tests.Managers;

/// <summary>
/// The settings file, and the bookkeeping that lets it survive an upgrade: the program used to keep it beside the
/// executable, so a missing file is looked for in the launch directories and moved to the shared one.
///
/// The move leaves the old file alone and only copies it once, and it validates before copying - so a damaged old file
/// is reported rather than becoming the new one. Writes go through a temporary file, so an interrupted save cannot
/// leave half a settings file behind.
/// </summary>
public class LeagueConfigStoreTests : IDisposable
{
    private readonly TempFolder _folder = new("config");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Settings_ => Path.Combine(_folder.Root, "settings");

    private string Legacy_ => Path.Combine(_folder.Root, "legacy");

    private LeagueConfigStore Store(params string[] legacy) => new(Settings_, legacy);

    // ---- where the file lives ----

    [Fact]
    public void ConfigPath_IsTheFileInTheSettingsDirectory()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(Settings_, "LeagueConfig.json")), Store().ConfigPath);
    }

    // ---- the round trip ----

    [Fact]
    public void Load_NoFileAnywhere_IsTheDefaultSettings()
    {
        LeagueConfig config = Store().Load();

        Assert.True(config.EnableChampionSelectAssistant);
        Assert.Equal(LeagueConfig.DefaultGameBarWaveOpacity, config.GameBarWaveOpacity);
    }

    [Fact]
    public void Save_ThenLoad_ReadsBackWhatWasSaved()
    {
        LeagueConfigStore store = Store();
        store.Save(new LeagueConfig { GameBarWaveOpacity = 55, AramMetaCacheMinutes = 7 });

        LeagueConfig reloaded = store.Load();

        Assert.Equal(55, reloaded.GameBarWaveOpacity);
        Assert.Equal(7, reloaded.AramMetaCacheMinutes);
    }

    [Fact]
    public void Save_TheDirectoryDoesNotExist_IsCreated()
    {
        LeagueConfigStore store = Store();

        store.Save(new LeagueConfig());

        Assert.True(File.Exists(store.ConfigPath));
    }

    [Fact]
    public void Save_ThenLoadFromAnotherInstance_SeesTheSameFile()
    {
        // A second instance stands in for the next start of the program.
        Store().Save(new LeagueConfig { AramMetaCacheMinutes = 11 });

        Assert.Equal(11, Store().Load().AramMetaCacheMinutes);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        // The write goes through a temporary file; a stray one would mean the move did not finish.
        LeagueConfigStore store = Store();
        store.Save(new LeagueConfig());

        string[] files = Directory.GetFiles(Path.GetDirectoryName(store.ConfigPath)!);
        Assert.Equal(["LeagueConfig.json"], files.Select(Path.GetFileName));
    }

    [Fact]
    public void Save_WritesUtf8WithoutAByteOrderMark()
    {
        // A BOM in a JSON file is legal but surprises every reader; the writer leaves it out.
        LeagueConfigStore store = Store();
        store.Save(new LeagueConfig());

        byte[] bytes = File.ReadAllBytes(store.ConfigPath);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "The file starts with a byte order mark."
        );
    }

    [Fact]
    public void Save_IsIndentedSoTheFileCanBeReadByAPerson()
    {
        LeagueConfigStore store = Store();
        store.Save(new LeagueConfig());

        Assert.Contains('\n', File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Save_Nothing_IsRefused()
    {
        // A null settings object would deserialize to nothing; refusing it here says so at the call site.
        Assert.Throws<ArgumentNullException>(() => Store().Save(null!));
    }

    // ---- reading a damaged file ----

    [Fact]
    public void Load_AFileThatHoldsNull_IsReportedRatherThanTreatedAsDefaults()
    {
        // Silently falling back to defaults would throw the user's settings away.
        LeagueConfigStore store = Store();
        Directory.CreateDirectory(Path.GetDirectoryName(store.ConfigPath)!);
        File.WriteAllText(store.ConfigPath, "null");

        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => store.Load());
    }

    [Fact]
    public void Load_AMalformedFile_IsReported()
    {
        LeagueConfigStore store = Store();
        Directory.CreateDirectory(Path.GetDirectoryName(store.ConfigPath)!);
        File.WriteAllText(store.ConfigPath, "{ not json ");

        Assert.ThrowsAny<Exception>(() => store.Load());
    }

    // ---- moving an old file forward ----

    [Fact]
    public void Load_AnOldFileBesideTheExecutable_IsCopiedToTheSharedPlace()
    {
        Directory.CreateDirectory(Legacy_);
        File.WriteAllText(
            Path.Combine(Legacy_, "LeagueConfig.json"),
            JsonSerializer.Serialize(new LeagueConfig { GameBarWaveOpacity = 33 })
        );

        LeagueConfig config = Store(Legacy_).Load();

        Assert.Equal(33, config.GameBarWaveOpacity);
        Assert.True(
            File.Exists(Path.Combine(Settings_, "LeagueConfig.json")),
            "The settings were moved to the shared place."
        );
    }

    [Fact]
    public void Load_AnOldFile_IsLeftWhereItWas()
    {
        // The old file is the only copy until the new one is written, so it is not removed.
        Directory.CreateDirectory(Legacy_);
        string legacy = Path.Combine(Legacy_, "LeagueConfig.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new LeagueConfig { GameBarWaveOpacity = 33 }));

        Store(Legacy_).Load();

        Assert.True(File.Exists(legacy), "The old file is left intact.");
    }

    [Fact]
    public void Load_AnOldFile_IsOnlyMovedOnce()
    {
        // Once the shared file exists the old one is never looked at again, so changing it has no effect.
        Directory.CreateDirectory(Legacy_);
        string legacy = Path.Combine(Legacy_, "LeagueConfig.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new LeagueConfig { GameBarWaveOpacity = 33 }));
        LeagueConfigStore store = Store(Legacy_);
        store.Load();

        File.WriteAllText(legacy, JsonSerializer.Serialize(new LeagueConfig { GameBarWaveOpacity = 77 }));

        Assert.Equal(33, store.Load().GameBarWaveOpacity);
    }

    [Fact]
    public void Load_AnOldFileThatIsDamaged_IsReportedInsteadOfBecomingTheNewOne()
    {
        // Validating first is what keeps a broken old file from replacing a good shared one.
        Directory.CreateDirectory(Legacy_);
        File.WriteAllText(Path.Combine(Legacy_, "LeagueConfig.json"), "null");

        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => Store(Legacy_).Load());
        Assert.False(File.Exists(Path.Combine(Settings_, "LeagueConfig.json")), "Nothing was moved.");
    }

    [Fact]
    public void Load_TheSharedFileWinsOverAnOldOne()
    {
        // A program that has already been upgraded must not go looking for the old location.
        Store().Save(new LeagueConfig { GameBarWaveOpacity = 55 });
        Directory.CreateDirectory(Legacy_);
        File.WriteAllText(
            Path.Combine(Legacy_, "LeagueConfig.json"),
            JsonSerializer.Serialize(new LeagueConfig { GameBarWaveOpacity = 33 })
        );

        Assert.Equal(55, Store(Legacy_).Load().GameBarWaveOpacity);
    }

    [Fact]
    public void Load_NoOldFileInAnyOfTheDirectories_IsTheDefault()
    {
        // Several launch directories are given because the program may be started from any of them.
        Assert.Equal(
            LeagueConfig.DefaultGameBarWaveOpacity,
            Store(Path.Combine(_folder.Root, "absent-one"), Path.Combine(_folder.Root, "absent-two"))
                .Load()
                .GameBarWaveOpacity
        );
    }

    [Fact]
    public void Load_TheSecondOfTwoOldFiles_IsUsedWhenTheFirstIsAbsent()
    {
        string second = Path.Combine(_folder.Root, "second");
        Directory.CreateDirectory(second);
        File.WriteAllText(
            Path.Combine(second, "LeagueConfig.json"),
            JsonSerializer.Serialize(new LeagueConfig { GameBarWaveOpacity = 44 })
        );

        Assert.Equal(44, Store(Path.Combine(_folder.Root, "absent"), second).Load().GameBarWaveOpacity);
    }
}
