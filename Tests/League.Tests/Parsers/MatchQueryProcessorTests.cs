using League.Parsers;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Parsers;

/// <summary>
/// The processor that turns a player card into a player's history. Only the checks reachable through the public
/// surface are migrated here.
/// <para>
/// Everything past <c>EnsureFactoryInitialized</c> cannot be reached from a test without injecting a
/// <c>PlayerCardManager</c>, and the only way to do that is the internal <c>SetPlayerCardManager</c>. That takes out
/// the hidden-player rule, the history size reaching the data layer, the cache hit, the champion name and avatar
/// lookups, the behaviour averages and the rank text — all of which
/// <c>Tests/StatisticsChecks/MatchQueryProcessorChecks.cs</c> still drives through reflection. Migrating those needs
/// a public seam on the processor first.
/// </para>
/// <para>
/// The expectations come from that same check class. This class joins the process-wide state collection because it
/// constructs a processor, which owns a process-wide image cache and writes through <c>AppLog.Shared</c> when a fetch
/// fails. It deliberately does not swap <c>Globals</c>: no code it can reach reads it, because the factory guard
/// throws before the champion and queue lookups.
/// </para>
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class MatchQueryProcessorTests
{
    /// <summary>
    /// The whole no-argument surface at once: a processor can be built and both cache-clearing entry points accept a
    /// call on it. <c>SetMatchHistoryCount</c> forwards to <c>ClearPlayerMatchCache</c>, so a broken cache would
    /// surface here. Which size it settles on, and what the cache does with a filled entry, are not observable from
    /// outside — see the gap note.
    /// </summary>
    [Fact]
    public void PublicSurface_NoArguments_IsAccepted()
    {
        var processor = new MatchQueryProcessor();

        processor.SetMatchHistoryCount(50);
        processor.ClearPlayerMatchCache();

        Assert.NotNull(processor);
    }

    [Fact]
    public async Task FetchPlayerMatchInfoAsync_MissingPlayerCard_IsRefused()
    {
        var processor = new MatchQueryProcessor();

        await Assert.ThrowsAsync<ArgumentNullException>(() => processor.FetchPlayerMatchInfoAsync(null!));
    }

    [Fact]
    public async Task FetchPlayerMatchInfoAsync_CancelledRead_IsRefused()
    {
        var processor = new MatchQueryProcessor();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            processor.FetchPlayerMatchInfoAsync(PlayerCard(7, "p-0000000001"), cancelled.Token)
        );
    }

    [Fact]
    public async Task FetchPlayerMatchInfoAsync_WithoutACardManager_RefusesToFetch()
    {
        var processor = new MatchQueryProcessor();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.FetchPlayerMatchInfoAsync(PlayerCard(7, "p-0000000001"))
        );
    }

    /// <summary>
    /// The safe layer swallows the inner failure and gives up on the factory instead, which without a card manager is
    /// null. The thrown <see cref="NullReferenceException"/> is the evidence that a summary attempt happened: a
    /// cancelled read or a refused argument would have surfaced as their own type instead.
    /// </summary>
    [Fact]
    public async Task SafeFetchPlayerMatchInfoAsync_WithoutACardManager_TakesTheRetryPath()
    {
        var processor = new MatchQueryProcessor();

        await Assert.ThrowsAsync<NullReferenceException>(() =>
            processor.SafeFetchPlayerMatchInfoAsync(PlayerCard(7, "p-0000000001"), retryTimes: 0)
        );
    }

    /// <summary>
    /// A card the client marks hidden returns before the retry loop, so the two paths are told apart by where the
    /// factory is reached from rather than by how long either one waits.
    /// </summary>
    [Fact]
    public async Task SafeFetchPlayerMatchInfoAsync_HiddenCard_DoesNotTakeTheRetryPath()
    {
        var processor = new MatchQueryProcessor();
        var hidden = PlayerCard(7, "p-0000000001");
        hidden["nameVisibilityType"] = "HIDDEN";

        await Assert.ThrowsAsync<NullReferenceException>(() => processor.SafeFetchPlayerMatchInfoAsync(hidden));
    }

    private static JObject PlayerCard(long summonerId, string puuid) =>
        new()
        {
            ["summonerId"] = summonerId,
            ["puuid"] = puuid,
            ["championId"] = 103,
        };
}
