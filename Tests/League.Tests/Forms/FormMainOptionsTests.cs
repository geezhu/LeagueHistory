using League.Services;

namespace League.Tests.Forms;

/// <summary>
/// The switches that decide what the main window starts, added so the window can be built without touching anything
/// outside the process.
///
/// The contract is small and worth pinning because the refactor that introduced it promised not to change behaviour:
/// a window built with no arguments must start everything it used to, and each switch must control only its own part.
/// If a default ever flips, every caller that passes no options silently loses a service - and nothing in the settings
/// page would look different.
/// </summary>
public class FormMainOptionsTests
{
    /// <summary>A store that keeps the plan in memory, so no Windows task is read or written.</summary>
    private sealed class MemoryTaskStore : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Stored { get; set; }

        public int Reads { get; private set; }

        public ShutdownTaskRecord? Read()
        {
            Reads++;
            return Stored;
        }

        public void Register(string xml) => throw new NotSupportedException("The tests never register a plan.");

        public void Stop() => throw new NotSupportedException("The tests never stop a plan.");

        public void Delete() => Stored = null;

        public void SetEnabled(bool enabled) => throw new NotSupportedException("The tests never enable a plan.");
    }

    // ---- what a caller gets without asking ----

    [Fact]
    public void NewOptions_StartEverythingTheWindowUsedToStart()
    {
        FormMainOptions options = new();

        Assert.True(options.StartBackgroundServices);
        Assert.True(options.StartGameIntegration);
    }

    [Fact]
    public void NewOptions_LeaveTheShutdownPlanToWindows()
    {
        // Null means "use Task Scheduler", which is what the window did before the store was injectable.
        Assert.Null(new FormMainOptions().ShutdownTasks);
    }

    // ---- each switch is its own ----

    [Fact]
    public void TurningOffBackgroundServices_LeavesTheGameIntegrationOn()
    {
        // The two switches exist for different reasons: one keeps the app from polling, the other from registering
        // global hotkeys. Turning off one must not turn off the other.
        FormMainOptions options = new() { StartBackgroundServices = false };

        Assert.False(options.StartBackgroundServices);
        Assert.True(options.StartGameIntegration);
    }

    [Fact]
    public void TurningOffGameIntegration_LeavesTheBackgroundServicesOn()
    {
        FormMainOptions options = new() { StartGameIntegration = false };

        Assert.True(options.StartBackgroundServices);
        Assert.False(options.StartGameIntegration);
    }

    [Fact]
    public void BothSwitchesCanBeOff_WhichIsHowTheWindowIsTested()
    {
        FormMainOptions options = new() { StartBackgroundServices = false, StartGameIntegration = false };

        Assert.False(options.StartBackgroundServices);
        Assert.False(options.StartGameIntegration);
    }

    // ---- the store ----

    [Fact]
    public void AStoreCanBeInjected_AndIsKeptAsGiven()
    {
        MemoryTaskStore store = new();

        Assert.Same(store, new FormMainOptions { ShutdownTasks = store }.ShutdownTasks);
    }

    [Fact]
    public void AStoreWithNoPlan_ReadsAsNothingRatherThanFailing()
    {
        // The window asks the store on start-up; an empty answer is the ordinary case, not an error.
        MemoryTaskStore store = new();

        Assert.Null(store.Read());
        Assert.Equal(1, store.Reads);
    }

    // ---- the options are per window, not shared ----

    [Fact]
    public void TwoOptions_DoNotShareTheirSwitches()
    {
        // A second window built with different options must not see the first one's settings.
        FormMainOptions quiet = new() { StartBackgroundServices = false };
        FormMainOptions ordinary = new();

        Assert.False(quiet.StartBackgroundServices);
        Assert.True(ordinary.StartBackgroundServices);
    }

    [Fact]
    public void TwoOptions_DoNotShareTheirStore()
    {
        MemoryTaskStore first = new();
        MemoryTaskStore second = new();

        Assert.NotSame(
            new FormMainOptions { ShutdownTasks = first }.ShutdownTasks,
            new FormMainOptions { ShutdownTasks = second }.ShutdownTasks
        );
    }
}
