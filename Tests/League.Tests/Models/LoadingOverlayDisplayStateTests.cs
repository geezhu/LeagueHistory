using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Whether the loading overlay is wanted, independent of statistics readiness and of the game briefly losing focus.
///
/// Three flags carry that intent, and the interesting part is how they survive each other: dismissing hides the
/// overlay for the rest of that game and no longer, a preview stays up even while the game is not at a displayable
/// phase, and a manual request clears the dismissal while a manual preview does not.
/// </summary>
public class LoadingOverlayDisplayStateTests
{
    private const string Displayable = "GameStart";
    private const string NotDisplayable = "ChampSelect";

    // ---- the phases the overlay can appear at ----

    [Theory]
    [InlineData("GameStart")]
    [InlineData("InProgress")]
    [InlineData("Reconnect")]
    public void ObservePhase_ADisplayablePhase_AsksForTheOverlay(string phase)
    {
        LoadingOverlayDisplayState state = new();

        state.ObservePhase(phase);

        Assert.True(state.Requested, $"{phase} is a phase the overlay belongs in.");
    }

    [Theory]
    [InlineData("ChampSelect")]
    [InlineData("EndOfGame")]
    [InlineData("WaitingForStats")]
    [InlineData("")]
    public void ObservePhase_APhaseThatIsNotDisplayable_StopsAsking(string phase)
    {
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);

        state.ObservePhase(phase);

        Assert.False(state.Requested, $"{phase} is not a phase the overlay belongs in.");
    }

    // ---- manual requests ----

    [Fact]
    public void Request_AsksForTheOverlayAndClearsTheDismissal()
    {
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);
        state.Dismiss();

        state.Request();

        Assert.True(state.Requested);
        Assert.False(state.Suppressed, "Opening it by hand overrides the earlier dismissal.");
    }

    // ---- dismissing ----

    [Fact]
    public void Dismiss_StopsAskingAndRecordsWhy()
    {
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);

        state.Dismiss();

        Assert.False(state.Requested);
        Assert.True(state.Suppressed);
    }

    [Fact]
    public void ObservePhase_AfterADismissal_DoesNotAskAgainInTheSameGame()
    {
        // The game keeps reporting its phase while the player is playing, and each report must respect the dismissal.
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);
        state.Dismiss();

        state.ObservePhase(Displayable);
        state.ObservePhase(Displayable);

        Assert.False(state.Requested);
    }

    [Fact]
    public void ObservePhase_ANewGame_ClearsTheDismissal()
    {
        // Leaving a displayable phase and coming back is a new game, so the dismissal only lasted the one it was made in.
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);
        state.Dismiss();

        state.ObservePhase(NotDisplayable);
        state.ObservePhase(Displayable);

        Assert.True(state.Requested);
        Assert.False(state.Suppressed);
    }

    [Fact]
    public void ObservePhase_AReturnToTheSameGameAfterADismissal_StillRespectsIt()
    {
        // Staying inside displayable phases is the same game; only leaving them starts a new one.
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);
        state.Dismiss();

        state.ObservePhase("InProgress");

        Assert.False(state.Requested);
    }

    // ---- previewing ----

    [Fact]
    public void RequestPreview_AsksForTheOverlayAndMarksItAsAPreview()
    {
        LoadingOverlayDisplayState state = new();

        state.RequestPreview();

        Assert.True(state.Requested);
        Assert.True(state.Preview);
    }

    [Fact]
    public void ObservePhase_APhaseThatIsNotDisplayable_LeavesAPreviewAlone()
    {
        // A preview is shown on purpose while looking at the window, so leaving a game must not tear it down.
        LoadingOverlayDisplayState state = new();
        state.RequestPreview();

        state.ObservePhase(NotDisplayable);

        Assert.True(state.Requested);
        Assert.True(state.Preview);
    }

    [Fact]
    public void ObservePhase_ADisplayablePhaseAfterAPreview_TurnsItIntoTheRealThing()
    {
        // The game's own overlay replaces the preview rather than sitting next to it.
        LoadingOverlayDisplayState state = new();
        state.RequestPreview();

        state.ObservePhase(Displayable);

        Assert.True(state.Requested);
        Assert.False(state.Preview);
    }

    [Fact]
    public void RequestPreview_DoesNotClearADismissal_WhileRequestDoes()
    {
        // The asymmetry is deliberate: previewing is a look at the overlay, not a decision to bring it back.
        LoadingOverlayDisplayState first = new();
        first.ObservePhase(Displayable);
        first.Dismiss();
        first.RequestPreview();
        Assert.True(first.Suppressed);

        LoadingOverlayDisplayState second = new();
        second.ObservePhase(Displayable);
        second.Dismiss();
        second.Request();
        Assert.False(second.Suppressed);
    }

    [Fact]
    public void Request_AfterAPreview_ClearsThePreviewFlag()
    {
        LoadingOverlayDisplayState state = new();
        state.RequestPreview();

        state.Request();

        Assert.False(state.Preview);
    }

    [Fact]
    public void Dismiss_AfterAPreview_StopsIt()
    {
        LoadingOverlayDisplayState state = new();
        state.RequestPreview();

        state.Dismiss();

        Assert.False(state.Requested);
        Assert.False(state.Preview);
    }

    // ---- resetting ----

    [Fact]
    public void Reset_ClearsEverything()
    {
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);
        state.Dismiss();
        state.RequestPreview();

        state.Reset();

        Assert.False(state.Requested);
        Assert.False(state.Preview);
        Assert.False(state.Suppressed);
    }

    [Fact]
    public void Reset_ForgetsThatAGameWasInProgress()
    {
        // After a reset the next displayable phase counts as a new game, so an automatic report asks again.
        LoadingOverlayDisplayState state = new();
        state.ObservePhase(Displayable);
        state.Dismiss();
        state.Reset();

        state.ObservePhase(Displayable);

        Assert.True(state.Requested);
        Assert.False(state.Suppressed);
    }

    [Fact]
    public void AFreshState_AsksForNothing()
    {
        LoadingOverlayDisplayState state = new();

        Assert.False(state.Requested);
        Assert.False(state.Preview);
        Assert.False(state.Suppressed);
    }
}
