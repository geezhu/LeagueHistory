using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Whether the champion select panel should be on screen, with a grace period for the client briefly losing its window.
///
/// The panel follows the client, and a window can disappear for a moment while the client rebuilds it - so losing it
/// is tolerated for a second and a half rather than hiding the panel immediately. Two things are never tolerated: an
/// explicit exit (not selecting, or minimised) hides at once, and a drag or a live window shows at once and clears the
/// grace, so the next loss starts its countdown from scratch.
/// </summary>
public class AssistantVisibilityTests
{
    private const long Now = 10_000;

    private static bool Show(
        AssistantVisibility visibility,
        long now,
        bool selecting = true,
        bool minimized = false,
        bool available = false,
        bool dragging = false
    ) => visibility.ShouldShow(selecting, minimized, available, dragging, now);

    // ---- what hides at once ----

    [Fact]
    public void ShouldShow_NotSelecting_HidesAtOnce()
    {
        Assert.False(Show(new AssistantVisibility(), Now, selecting: false));
    }

    [Fact]
    public void ShouldShow_Minimised_HidesAtOnce()
    {
        Assert.False(Show(new AssistantVisibility(), Now, minimized: true));
    }

    [Fact]
    public void ShouldShow_NotSelecting_ClearsTheGraceSoTheNextLossStartsAfresh()
    {
        // Traced: a loss starts the grace at 10_000; leaving select clears it; the next loss starts again at 20_000.
        AssistantVisibility visibility = new();
        Show(visibility, 10_000);

        Show(visibility, 11_000, selecting: false);

        Assert.True(Show(visibility, 20_000), "A new loss gets the whole grace again.");
    }

    // ---- what shows at once ----

    [Fact]
    public void ShouldShow_TheWindowIsThere_Shows()
    {
        Assert.True(Show(new AssistantVisibility(), Now, available: true));
    }

    [Fact]
    public void ShouldShow_Dragging_Shows()
    {
        // A drag means the user is holding the panel; it cannot be hidden from under them.
        Assert.True(Show(new AssistantVisibility(), Now, dragging: true));
    }

    [Fact]
    public void ShouldShow_TheWindowIsBack_ClearsTheGrace()
    {
        // Traced: a loss at 10_000 is still inside the grace at 11_000; the window returning at 11_500 clears it, so
        // the next loss at 12_000 has the whole second and a half again.
        AssistantVisibility visibility = new();
        Show(visibility, 10_000);

        Show(visibility, 11_500, available: true);

        Assert.True(Show(visibility, 12_000));
        Assert.False(Show(visibility, 13_600), "The second loss counts from when it started, not from before.");
    }

    // ---- the grace itself ----

    [Fact]
    public void ShouldShow_ALossJustNow_StaysVisible()
    {
        // Hiding on the first missing poll would make the panel flicker whenever the client rebuilds its window.
        Assert.True(Show(new AssistantVisibility(), Now));
    }

    [Fact]
    public void ShouldShow_ALossPartwayThroughTheGrace_StaysVisible()
    {
        AssistantVisibility visibility = new();
        Show(visibility, Now);

        Assert.True(Show(visibility, Now + 1_000));
    }

    [Fact]
    public void ShouldShow_TheGraceRunningOut_Hides()
    {
        // Traced: the loss starts at 10_000, and 1_500 later the grace is over - the bound is exclusive.
        AssistantVisibility visibility = new();
        Show(visibility, Now);

        Assert.True(Show(visibility, Now + 1_499));
        Assert.False(Show(visibility, Now + 1_500));
    }

    [Fact]
    public void ShouldShow_TheGraceIsMeasuredFromTheFirstLossNotTheLatest()
    {
        // Otherwise a client that keeps failing would keep the panel up for ever.
        // Traced: loss at 10_000, a check at 11_000 (still inside), another at 11_600 - past the original 1_500.
        AssistantVisibility visibility = new();
        Show(visibility, 10_000);
        Show(visibility, 11_000);

        Assert.False(Show(visibility, 11_600));
    }

    [Fact]
    public void ShouldShow_ALongGapWithTheWindowThere_IsJustVisible()
    {
        // Nothing about the grace applies while the window is there.
        AssistantVisibility visibility = new();
        Show(visibility, Now, available: true);

        Assert.True(Show(visibility, Now + 60_000, available: true));
    }
}
