using League.Managers;

namespace League.Tests.Managers;

/// <summary>
/// The application-wide settings entry point.
///
/// Almost all of this type is static state over the user's own settings file, which is why the injectable half is
/// checked separately as LeagueConfigStore. What is left here is the path: the one piece that says where the settings
/// live, and the one piece that has to follow the data root rather than a hard-coded folder. Reading it touches no
/// file, so it is safe to ask for in a test.
/// </summary>
public class LeagueConfigManagerTests
{
    [Fact]
    public void ConfigPath_IsTheSettingsFileUnderTheDataRoot()
    {
        // The test setup points the data root at a temporary folder, so this asserts the shape without reaching the
        // user's real files.
        string path = LeagueConfigManager.ConfigPath;

        Assert.EndsWith("LeagueConfig.json", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(Path.IsPathRooted(path), $"'{path}' is not a rooted path.");
    }

    [Fact]
    public void ConfigPath_IsTheSameEveryTime()
    {
        // It is a computed property over a static, not a fresh decision per call.
        Assert.Equal(LeagueConfigManager.ConfigPath, LeagueConfigManager.ConfigPath);
    }
}
