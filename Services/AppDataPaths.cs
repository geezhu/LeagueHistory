namespace League.Services;

/// <summary>
/// Where per-user data lives: settings, search history, logs and the shutdown plan. <c>LEAGUE_DATA_ROOT</c> moves
/// them elsewhere, which the regression checks use so they never read or write the user's own files. Caches and the
/// voice model stay in the real location; they are shared downloads, not the user's records.
/// </summary>
public static class AppDataPaths
{
    public const string RootVariable = "LEAGUE_DATA_ROOT";

    public static string LocalData =>
        Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } root
            ? root
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
