using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// <c>LEAGUE_DATA_ROOT</c> moves the per-user files (settings, search history, logs, the shutdown plan) somewhere
/// else; without it the real local application data folder is used. The variable is process-wide, so the previous
/// value is captured in the constructor and restored on dispose.
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class AppDataPathsTests : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable(AppDataPaths.RootVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppDataPaths.RootVariable, _previous);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LocalData_NoOverride_UsesLocalApplicationData()
    {
        Environment.SetEnvironmentVariable(AppDataPaths.RootVariable, null);

        string path = AppDataPaths.LocalData;

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path);
    }

    [Fact]
    public void LocalData_Override_UsesTheVariable()
    {
        Environment.SetEnvironmentVariable(AppDataPaths.RootVariable, @"C:\league-checks");

        string path = AppDataPaths.LocalData;

        Assert.Equal(@"C:\league-checks", path);
    }

    [Fact]
    public void LocalData_EmptyOverride_UsesLocalApplicationData()
    {
        Environment.SetEnvironmentVariable(AppDataPaths.RootVariable, string.Empty);

        string path = AppDataPaths.LocalData;

        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path);
    }
}
