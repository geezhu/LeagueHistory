using System.Runtime.CompilerServices;

namespace League.Tests;

/// <summary>Process-wide setup. A module initializer runs before the first test and before any window exists.</summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Rows and buttons are laid out in device pixels, so a scaled display would move every measured coordinate.
        // This must happen before the first window, which is why it is not a fixture.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
    }
}
