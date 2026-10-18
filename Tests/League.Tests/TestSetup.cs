using System.Runtime.CompilerServices;
using League.Services;

namespace League.Tests;

/// <summary>Process-wide setup. A module initializer runs before the first test and before any window exists.</summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Settings, search history and logs go to a throwaway folder, never the user's own files. The old harness
        // does the same from Program.Main; without it a test that logs would append to the real
        // %LocalAppData%\League\Logs. Tests that need the real location can still ask AppDataPaths for it.
        string root = Path.Combine(Path.GetTempPath(), "league-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppDataPaths.RootVariable, root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Delete(root);

        // Rows and buttons are laid out in device pixels, so a scaled display would move every measured coordinate.
        // This must happen before the first window, which is why it is not a fixture.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
    }

    private static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A log still open by a background writer is left in the temp folder.
            Console.WriteLine("Test data folder kept: " + folder);
        }
    }
}
