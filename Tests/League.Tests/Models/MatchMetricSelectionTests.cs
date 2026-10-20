using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Which metrics the match tools show: a set of keys capped at six.
///
/// Two rules matter. A selection is atomic - a group that would not fit is refused whole rather than partly applied,
/// because picking three of five children silently is worse than picking none. And choosing a whole group while the
/// cap is already reached replaces the selection instead of failing, since a group is a different way of looking at
/// the same six slots.
/// </summary>
public class MatchMetricSelectionTests
{
    private static readonly string[] AllSix = ["a", "b", "c", "d", "e", "f"];

    private static MatchMetricSelection Filled(params string[] keys)
    {
        MatchMetricSelection selection = new();
        Assert.True(selection.Set(keys, selected: true));
        return selection;
    }

    // ---- the basics ----

    [Fact]
    public void ANewSelection_IsEmpty()
    {
        MatchMetricSelection selection = new();

        Assert.Equal(0, selection.Count);
        Assert.False(selection.Contains("a"));
    }

    [Fact]
    public void TheCap_IsSix()
    {
        Assert.Equal(6, MatchMetricSelection.Limit);
    }

    [Fact]
    public void Set_AddsAndRemovesSingleKeys()
    {
        MatchMetricSelection selection = new();

        Assert.True(selection.Set(["a"], selected: true));
        Assert.True(selection.Contains("a"));

        Assert.True(selection.Set(["a"], selected: false));
        Assert.False(selection.Contains("a"));
    }

    [Fact]
    public void Set_RemovingSomethingThatIsNotThere_IsHarmless()
    {
        MatchMetricSelection selection = new();

        Assert.True(selection.Set(["x"], selected: false));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Set_TheSameKeyTwice_CountsOnce()
    {
        // The keys are a set, so a repeated one is not a second slot.
        MatchMetricSelection selection = Filled("a", "a", "a");

        Assert.Equal(1, selection.Count);
    }

    // ---- the cap ----

    [Fact]
    public void Set_AFullSelection_AcceptsNoMore()
    {
        MatchMetricSelection selection = Filled(AllSix);

        Assert.False(selection.Set(["g"], selected: true));
        Assert.Equal(6, selection.Count);
        Assert.False(selection.Contains("g"));
    }

    [Fact]
    public void Set_AGroupThatDoesNotFit_IsRefusedWhole()
    {
        // Applying only the ones that fit would leave the user with a selection they did not ask for.
        MatchMetricSelection selection = Filled("a", "b", "c", "d", "e");

        Assert.False(selection.Set(["f", "g"], selected: true));
        Assert.Equal(5, selection.Count);
        Assert.False(selection.Contains("f"));
        Assert.False(selection.Contains("g"));
    }

    [Fact]
    public void Set_AGroupThatExactlyFits_IsApplied()
    {
        MatchMetricSelection selection = Filled("a", "b", "c", "d");

        Assert.True(selection.Set(["e", "f"], selected: true));
        Assert.Equal(6, selection.Count);
    }

    [Fact]
    public void Set_KeysThatAreAlreadySelected_DoNotCountAgainstTheCapTwice()
    {
        // The check is on the union, so re-selecting what is already there is not a reason to refuse.
        MatchMetricSelection selection = Filled("a", "b", "c", "d", "e");

        Assert.True(selection.Set(["a", "f"], selected: true));
        Assert.Equal(6, selection.Count);
    }

    // ---- whole-group selections ----

    [Fact]
    public void SetGroup_BelowTheCap_AddsTheGroup()
    {
        MatchMetricSelection selection = Filled("a");

        Assert.True(selection.SetGroup(["b", "c"], selected: true));
        Assert.Equal(3, selection.Count);
    }

    [Fact]
    public void SetGroup_AtTheCap_ReplacesTheSelection()
    {
        // A group is a different way of looking at the same slots, so it takes over rather than failing.
        MatchMetricSelection selection = Filled(AllSix);

        Assert.True(selection.SetGroup(["x", "y"], selected: true));

        Assert.Equal(2, selection.Count);
        Assert.True(selection.Contains("x"));
        Assert.False(selection.Contains("a"));
    }

    [Fact]
    public void SetGroup_AGroupBiggerThanTheCap_IsRefusedAtTheCap()
    {
        // It cannot replace the selection, and it certainly cannot be added to it.
        MatchMetricSelection selection = Filled(AllSix);

        Assert.False(selection.SetGroup(["g", "h", "i", "j", "k", "l", "m"], selected: true));
        Assert.Equal(6, selection.Count);
        Assert.True(selection.Contains("a"));
    }

    [Fact]
    public void SetGroup_BelowTheCap_AGroupThatDoesNotFit_IsStillRefusedWhole()
    {
        // The replace only happens when the cap is already reached; otherwise this is an ordinary selection.
        MatchMetricSelection selection = Filled("a", "b", "c", "d", "e");

        Assert.False(selection.SetGroup(["f", "g"], selected: true));
        Assert.Equal(5, selection.Count);
        Assert.False(selection.Contains("f"));
    }

    [Fact]
    public void SetGroup_Deselecting_RemovesTheGroup()
    {
        MatchMetricSelection selection = Filled(AllSix);

        Assert.True(selection.SetGroup(["a", "b"], selected: false));

        Assert.Equal(4, selection.Count);
        Assert.False(selection.Contains("a"));
    }

    [Fact]
    public void SetGroup_Deselecting_IsAllowedWhateverTheCount()
    {
        // Removing never has to check the cap.
        MatchMetricSelection selection = Filled(AllSix);

        Assert.True(selection.SetGroup(AllSix, selected: false));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void SetGroup_DuplicateKeysInTheGroup_CountOnce()
    {
        MatchMetricSelection selection = new();

        Assert.True(selection.SetGroup(["a", "a", "b"], selected: true));

        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public void SetGroup_AnEmptyGroup_ChangesNothing()
    {
        MatchMetricSelection selection = Filled("a");

        Assert.True(selection.SetGroup([], selected: true));
        Assert.Equal(1, selection.Count);
    }
}
