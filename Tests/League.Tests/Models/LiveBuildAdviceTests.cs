using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Which build to recommend: the one closest to what the player already owns, and the first item in it they are
/// missing.
///
/// The ordering is the whole rule. More items in common comes first, so the advice follows the path already taken
/// rather than sending them down an unfamiliar one; ties keep the order the source gave, so the recommendation is
/// stable between polls. An option that names no items is not a build and is left out.
/// </summary>
public class LiveBuildAdviceTests
{
    private static LiveBuildOption Option(params int[] ids) => new(ids, 0.5, 0.1, 100);

    // ---- nothing to recommend ----

    [Fact]
    public void Choose_NoOptions_RecommendsNothing()
    {
        Assert.Null(LiveBuildAdvice.Choose([], [1, 2]));
    }

    [Fact]
    public void Choose_OnlyOptionsWithoutItems_RecommendsNothing()
    {
        // An option that names no items is not a build, whatever else it carries.
        Assert.Null(LiveBuildAdvice.Choose([Option()], [1, 2]));
    }

    // ---- which one wins ----

    [Fact]
    public void Choose_TheBuildWithMostItemsInCommon_Wins()
    {
        // The advice follows the path already taken.
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(9), Option(1, 2)], [1, 2]);

        Assert.Equal([1, 2], advice?.Build.Ids);
    }

    [Fact]
    public void Choose_ATie_KeepsTheOrderTheSourceGave()
    {
        // Two builds equally close: the first one the source offered is the recommendation, so polls agree.
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(1, 2), Option(1, 2, 3)], [1, 2]);

        Assert.Equal(2, advice?.Owned);
        Assert.Equal([1, 2], advice?.Build.Ids);
    }

    [Fact]
    public void Choose_NothingInCommon_StillRecommendsTheFirstBuild()
    {
        // Some advice is better than none; the first item is what they would buy next.
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(7, 8)], [1, 2]);

        Assert.Equal(0, advice?.Owned);
        Assert.Equal(7, advice?.Next);
    }

    // ---- how many are owned ----

    [Fact]
    public void Choose_CountsOnlyTheItemsOnesOwned()
    {
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(1, 2, 3)], [3, 1, 99]);

        Assert.Equal(2, advice?.Owned);
    }

    [Fact]
    public void Choose_AnOwnedItemThatIsNotInTheBuild_DoesNotCount()
    {
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(1)], [2, 3]);

        Assert.Equal(0, advice?.Owned);
    }

    // ---- what comes next ----

    [Fact]
    public void Choose_TheNextItem_IsTheFirstOneNotOwned()
    {
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(1, 2, 3)], [1]);

        Assert.Equal(2, advice?.Next);
    }

    [Fact]
    public void Choose_TheBuildIsAlreadyComplete_HasNoNextItem()
    {
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(1, 2)], [1, 2]);

        Assert.Null(advice?.Next);
    }

    [Fact]
    public void Choose_EveryItemIsMissing_StartsAtTheFirst()
    {
        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([Option(5, 6, 7)], []);

        Assert.Equal(5, advice?.Next);
    }

    [Fact]
    public void Choose_TheBuildItselfIsHandedBackUnchanged()
    {
        // The recommendation is the source's own object, so its rates travel with it.
        LiveBuildOption option = new([1, 2, 3], 0.62, 0.14, 4800);

        LiveBuildAdvice? advice = LiveBuildAdvice.Choose([option], [1]);

        Assert.Same(option, advice?.Build);
    }
}
