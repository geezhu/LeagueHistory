using League.Tests.Support;
using League.UIHelpers;
using Newtonsoft.Json.Linq;

namespace League.Tests.UIHelpers;

/// <summary>
/// Which folder layouts count as a League installation, which launcher inside one is used, and the small config file
/// that remembers a choice. Every fixture is written under a temporary folder and the helper is always built with its
/// own config path, so neither the user's own LOLPathConfig.json nor a client installed on this machine is read. The
/// rules come from the checks in Tests/StatisticsChecks/LauncherInstallationChecks.cs.
/// StartValidatedLauncher is deliberately not covered: it hands the path to Process.Start, which would open the real
/// client.
/// </summary>
public class LOLHelperTests
{
    private const string ClientExecutable = @"LeagueClient\LeagueClient.exe";
    private const string ClientUxExecutable = @"LeagueClient\LeagueClientUx.exe";
    private const string StartupRunner = @"Launcher\startup_runner.exe";
    private const string LauncherClient = @"Launcher\Client.exe";
    private const string TclsClient = @"TCLS\Client.exe";
    private const string WeGameLauncher = @"WeGameLauncher\launcher.exe";
    private const string LauncherWeGame = @"Launcher\WeGameLauncher\launcher.exe";
    private const string ConfigName = "LOLPathConfig.json";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void ResolveInstallation_NoUsablePath_ReturnsNull(string? path)
    {
        LolInstallation? installation = LOLHelper.ResolveInstallation(path);

        Assert.Null(installation);
    }

    [Fact]
    public void ResolveInstallation_DirectoryThatDoesNotExist_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-missing");

        LolInstallation? installation = LOLHelper.ResolveInstallation(Path.Combine(folder.Root, "not-installed"));

        Assert.Null(installation);
    }

    [Fact]
    public void ResolveInstallation_FolderThatIsNotAnInstallation_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-not-a-game");
        string readme = Fixture(folder, @"SomeGame\readme.txt");

        LolInstallation? installation = LOLHelper.ResolveInstallation(readme);

        Assert.Null(installation);
    }

    /// <summary>A path the framework itself refuses to turn into a directory; the helper reports it instead of throwing.</summary>
    [Fact]
    public void ResolveInstallation_PathWithANullCharacter_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-bad-path");

        LolInstallation? installation = LOLHelper.ResolveInstallation(Path.Combine(folder.Root, "League\0broken"));

        Assert.Null(installation);
    }

    [Fact]
    public void ResolveInstallation_ClientExecutableOfAVerifiedInstall_ReturnsTheRootAndBothLaunchers()
    {
        using var folder = new TempFolder("lol-helper-both");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);

        LolInstallation? installation = LOLHelper.ResolveInstallation(Path.Combine(root, ClientUxExecutable));

        AssertResolved(installation, root, Path.Combine(root, StartupRunner), Path.Combine(root, WeGameLauncher));
    }

    [Fact]
    public void ResolveInstallation_InstallationRootFolder_IsResolvedLikeAFileInsideIt()
    {
        using var folder = new TempFolder("lol-helper-root");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner);

        LolInstallation? installation = LOLHelper.ResolveInstallation(root);

        AssertResolved(installation, root, Path.Combine(root, StartupRunner), null);
    }

    [Fact]
    public void ResolveInstallation_LauncherExecutable_ResolvesTheInstallationAroundIt()
    {
        using var folder = new TempFolder("lol-helper-launcher-path");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner);
        string launcher = Path.Combine(root, StartupRunner);

        LolInstallation? installation = LOLHelper.ResolveInstallation(launcher);

        AssertResolved(installation, root, launcher, null);
    }

    [Fact]
    public void ResolveInstallation_OfficialLauncherWithoutTheStartupRunner_UsesTheClientExecutable()
    {
        using var folder = new TempFolder("lol-helper-official-client");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, LauncherClient);

        LolInstallation? installation = LOLHelper.ResolveInstallation(root);

        AssertResolved(installation, root, Path.Combine(root, LauncherClient), null);
    }

    [Fact]
    public void ResolveInstallation_OfficialLauncherUnderTcls_IsUsedWhenTheLauncherFolderHasNone()
    {
        using var folder = new TempFolder("lol-helper-tcls");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, TclsClient);

        LolInstallation? installation = LOLHelper.ResolveInstallation(root);

        AssertResolved(installation, root, Path.Combine(root, TclsClient), null);
    }

    [Fact]
    public void ResolveInstallation_SeveralOfficialLaunchers_UsesTheStartupRunner()
    {
        using var folder = new TempFolder("lol-helper-official-order");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, LauncherClient, TclsClient);

        LolInstallation? installation = LOLHelper.ResolveInstallation(root);

        Assert.NotNull(installation);
        Assert.Equal(Path.Combine(root, StartupRunner), installation.OfficialPath);
    }

    [Fact]
    public void ResolveInstallation_WeGameLauncherInsideTheLauncherFolder_IsUsedWhenTheTopLevelOneIsAbsent()
    {
        using var folder = new TempFolder("lol-helper-wegame-nested");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, LauncherWeGame);

        LolInstallation? installation = LOLHelper.ResolveInstallation(root);

        AssertResolved(installation, root, null, Path.Combine(root, LauncherWeGame));
    }

    [Fact]
    public void ResolveInstallation_SeveralWeGameLaunchers_UsesTheTopLevelOne()
    {
        using var folder = new TempFolder("lol-helper-wegame-order");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, WeGameLauncher, LauncherWeGame);

        LolInstallation? installation = LOLHelper.ResolveInstallation(root);

        Assert.NotNull(installation);
        Assert.Equal(Path.Combine(root, WeGameLauncher), installation.WeGamePath);
    }

    [Fact]
    public void ResolveInstallation_ClientFolderWithoutTheUxExecutable_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-no-ux");
        string root = Install(folder, ClientExecutable, StartupRunner);

        Assert.Null(LOLHelper.ResolveInstallation(root));
    }

    [Fact]
    public void ResolveInstallation_ClientFolderWithoutTheClientExecutable_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-no-client");
        string root = Install(folder, ClientUxExecutable, StartupRunner);

        Assert.Null(LOLHelper.ResolveInstallation(root));
    }

    [Fact]
    public void ResolveInstallation_VerifiedClientWithoutAnyLauncher_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-no-launcher");
        string root = Install(folder, ClientExecutable, ClientUxExecutable);

        Assert.Null(LOLHelper.ResolveInstallation(root));
    }

    [Theory]
    [InlineData(@"Launcher\startup_runner.exe")]
    [InlineData(@"Launcher\WeGameLauncher\launcher.exe")]
    public void ResolveInstallation_AnotherGamesLauncherFolder_ReturnsNull(string launcher)
    {
        using var folder = new TempFolder("lol-helper-another-game");
        string path = Fixture(folder, Path.Combine("Delta Force", launcher));

        Assert.Null(LOLHelper.ResolveInstallation(path));
    }

    [Fact]
    public void ResolveInstallation_PathThreeFoldersBelowTheRoot_IsStillResolved()
    {
        using var folder = new TempFolder("lol-helper-three-deep");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner);
        string deep = Directory.CreateDirectory(Path.Combine(root, "Logs", "LeagueClient", "Config")).FullName;

        LolInstallation? installation = LOLHelper.ResolveInstallation(deep);

        Assert.NotNull(installation);
        Assert.Equal(root, installation.Root);
    }

    [Fact]
    public void ResolveInstallation_PathFourFoldersBelowTheRoot_IsNotFound()
    {
        using var folder = new TempFolder("lol-helper-four-deep");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner);
        string deep = Directory
            .CreateDirectory(Path.Combine(root, "Logs", "LeagueClient", "Config", "Nested"))
            .FullName;

        Assert.Null(LOLHelper.ResolveInstallation(deep));
    }

    [Fact]
    public void SaveInstallation_VerifiedInstall_ReturnsTheResolvedFields()
    {
        using var folder = new TempFolder("lol-helper-save");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        var helper = new LOLHelper(folder.File(ConfigName));

        LolInstallation saved = helper.SaveInstallation(root);

        AssertResolved(saved, root, Path.Combine(root, StartupRunner), Path.Combine(root, WeGameLauncher));
    }

    [Fact]
    public void SaveInstallation_FolderThatIsNotAnInstallation_ThrowsWithoutWritingAConfig()
    {
        using var folder = new TempFolder("lol-helper-save-refused");
        string configPath = folder.File(ConfigName);
        var helper = new LOLHelper(configPath);

        Assert.Throws<InvalidOperationException>(() => helper.SaveInstallation(folder.Root));
        Assert.False(File.Exists(configPath));
    }

    [Fact]
    public void ReadSavedInstallation_AfterSaveInstallation_ReadsBackTheSavedFields()
    {
        using var folder = new TempFolder("lol-helper-round-trip");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        var helper = new LOLHelper(folder.File(ConfigName));
        LolInstallation saved = helper.SaveInstallation(root);

        LolInstallation? restored = helper.ReadSavedInstallation();

        AssertResolved(restored, saved.Root, saved.OfficialPath, saved.WeGamePath);
    }

    [Fact]
    public void ReadSavedInstallation_ConfigThatDoesNotExist_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-no-config");
        var helper = new LOLHelper(folder.File(ConfigName));

        Assert.Null(helper.ReadSavedInstallation());
    }

    [Fact]
    public void ReadSavedInstallation_EmptyConfig_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-empty-config");
        string configPath = folder.File(ConfigName);
        var helper = new LOLHelper(configPath);
        File.WriteAllText(configPath, string.Empty);

        Assert.Null(helper.ReadSavedInstallation());
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{")]
    [InlineData("[1, 2]")]
    public void ReadSavedInstallation_ConfigThatIsNotAJsonObject_ReturnsNull(string content)
    {
        using var folder = new TempFolder("lol-helper-broken-config");
        string configPath = folder.File(ConfigName);
        var helper = new LOLHelper(configPath);
        File.WriteAllText(configPath, content);

        Assert.Null(helper.ReadSavedInstallation());
    }

    [Fact]
    public void ReadSavedInstallation_InstallRootField_IsAcceptedEvenThoughTheRootIsNotALauncher()
    {
        using var folder = new TempFolder("lol-helper-install-root");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        string configPath = folder.File(ConfigName);
        SaveConfig(configPath, ("InstallRoot", root));
        var helper = new LOLHelper(configPath);

        LolInstallation? installation = helper.ReadSavedInstallation();

        AssertResolved(installation, root, Path.Combine(root, StartupRunner), Path.Combine(root, WeGameLauncher));
    }

    [Fact]
    public void ReadSavedInstallation_OfficialPathField_IsAccepted()
    {
        using var folder = new TempFolder("lol-helper-official-field");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        string configPath = folder.File(ConfigName);
        SaveConfig(configPath, ("OfficialPath", Path.Combine(root, StartupRunner)));
        var helper = new LOLHelper(configPath);

        LolInstallation? installation = helper.ReadSavedInstallation();

        AssertResolved(installation, root, Path.Combine(root, StartupRunner), Path.Combine(root, WeGameLauncher));
    }

    [Fact]
    public void ReadSavedInstallation_WeGamePathField_IsAccepted()
    {
        using var folder = new TempFolder("lol-helper-wegame-field");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        string configPath = folder.File(ConfigName);
        SaveConfig(configPath, ("WeGamePath", Path.Combine(root, WeGameLauncher)));
        var helper = new LOLHelper(configPath);

        LolInstallation? installation = helper.ReadSavedInstallation();

        AssertResolved(installation, root, Path.Combine(root, StartupRunner), Path.Combine(root, WeGameLauncher));
    }

    [Fact]
    public void ReadSavedInstallation_LauncherFieldPointingAtTheClientExecutable_IsIgnored()
    {
        using var folder = new TempFolder("lol-helper-client-as-launcher");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        string configPath = folder.File(ConfigName);
        SaveConfig(configPath, ("OfficialPath", Path.Combine(root, ClientUxExecutable)));
        var helper = new LOLHelper(configPath);

        Assert.Null(helper.ReadSavedInstallation());
    }

    [Fact]
    public void ReadSavedInstallation_InstallRootOfAnotherGame_FallsThroughToTheLauncherField()
    {
        using var folder = new TempFolder("lol-helper-wrong-root");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner);
        string anotherGame = Fixture(folder, @"Delta Force\Launcher\startup_runner.exe");
        string configPath = folder.File(ConfigName);
        SaveConfig(configPath, ("InstallRoot", anotherGame), ("OfficialPath", Path.Combine(root, StartupRunner)));
        var helper = new LOLHelper(configPath);

        LolInstallation? installation = helper.ReadSavedInstallation();

        AssertResolved(installation, root, Path.Combine(root, StartupRunner), null);
    }

    [Fact]
    public void ReadSavedInstallation_LastLOLPathOfAnotherGame_IsIgnored()
    {
        using var folder = new TempFolder("lol-helper-wrong-legacy");
        string anotherGame = Fixture(folder, @"Delta Force\Launcher\startup_runner.exe");
        string configPath = folder.File(ConfigName);
        SaveConfig(configPath, ("LastLOLPath", anotherGame));
        var helper = new LOLHelper(configPath);

        Assert.Null(helper.ReadSavedInstallation());
    }

    [Fact]
    public void ReadSavedInstallation_ClientExecutableDeletedAfterTheSave_ReturnsNull()
    {
        using var folder = new TempFolder("lol-helper-moved");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        var helper = new LOLHelper(folder.File(ConfigName));
        helper.SaveInstallation(root);
        File.Delete(Path.Combine(root, ClientUxExecutable));

        Assert.Null(helper.ReadSavedInstallation());
    }

    /// <summary>
    /// A save pins the detection cache for thirty seconds, which is what keeps the process, config and registry scan
    /// below out of the assertion: the detected installation is the one that was just saved.
    /// </summary>
    [Fact]
    public async Task DetectAsync_AfterASave_AnswersWithTheSavedInstallation()
    {
        using var folder = new TempFolder("lol-helper-detect");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner, WeGameLauncher);
        var helper = new LOLHelper(folder.File(ConfigName));
        helper.SaveInstallation(root);

        LolInstallation? detected = await helper.DetectAsync();

        AssertResolved(detected, root, Path.Combine(root, StartupRunner), Path.Combine(root, WeGameLauncher));
    }

    [Fact]
    public async Task DetectAsync_CalledTwice_ReusesTheDetectionItAlreadyHas()
    {
        using var folder = new TempFolder("lol-helper-detect-cache");
        string root = Install(folder, ClientExecutable, ClientUxExecutable, StartupRunner);
        var helper = new LOLHelper(folder.File(ConfigName));
        helper.SaveInstallation(root);

        Task<LolInstallation?> first = helper.DetectAsync();
        Task<LolInstallation?> second = helper.DetectAsync();

        Assert.Same(first, second);
        Assert.Equal(root, (await second)?.Root);
    }

    /// <summary>
    /// The three fields the resolution rule fills in, compared one by one: comparing the record as a whole would turn
    /// every property added to it into a failure that is not a rule change.
    /// </summary>
    private static void AssertResolved(LolInstallation? installation, string root, string? official, string? wegame)
    {
        Assert.NotNull(installation);
        Assert.Equal(root, installation.Root);
        Assert.Equal(official, installation.OfficialPath);
        Assert.Equal(wegame, installation.WeGamePath);
    }

    /// <summary>Creates the named files below a new <c>League</c> folder and returns that folder.</summary>
    private static string Install(TempFolder folder, params string[] relatives)
    {
        string root = Path.GetFullPath(Path.Combine(folder.Root, "League"));
        foreach (string relative in relatives)
        {
            Place(root, relative);
        }

        return root;
    }

    /// <summary>Creates one fixture file below the temporary folder and returns the path it was written to.</summary>
    private static string Fixture(TempFolder folder, string relative) => Place(folder.Root, relative);

    /// <summary>Creates a file of fixture content, plus its folders; only its existence matters to the helper.</summary>
    private static string Place(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return path;
    }

    /// <summary>Writes a config file whose fields hold the given values, the way the launcher panel writes one.</summary>
    private static void SaveConfig(string configPath, params (string Field, string Value)[] fields)
    {
        var json = new JObject();
        foreach (var (field, value) in fields)
        {
            json[field] = value;
        }

        File.WriteAllText(configPath, json.ToString());
    }
}
