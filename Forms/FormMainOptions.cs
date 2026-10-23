using League.Services;

namespace League;

/// <summary>
/// What <see cref="FormMain"/> starts besides its own window. The defaults are the application's; tests switch off the
/// parts that reach outside the process (the network, the League client, global hotkeys, Task Scheduler) and still get
/// the real window with its settings pages.
/// </summary>
public sealed class FormMainOptions
{
    /// <summary>The update check, the champion-select assistant, LCU polling, the crash prompt and the hang monitor.</summary>
    public bool StartBackgroundServices { get; init; } = true;

    /// <summary>The global game hotkeys and the loading-overlay manager with its polling timers.</summary>
    public bool StartGameIntegration { get; init; } = true;

    /// <summary>Where the daily shutdown plan is read and written; <c>null</c> uses Windows Task Scheduler.</summary>
    public IShutdownTaskStore? ShutdownTasks { get; init; }

    /// <summary>What the 准备情况 page detects and how it restarts elevated; <c>null</c> uses this computer.</summary>
    public SetupEnvironment? Setup { get; init; }
}
