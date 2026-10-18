using League.PrimaryElection;
using League.Tests.Support;
using static League.FormMain;

namespace League.Tests.PrimaryElection;

/// <summary>
/// The preselection source that reads the shared resource loader. It needs a running client to answer anything, so
/// the tests here pin the two things that hold without one: an icon request that is already cancelled, and the
/// failure a caller sees when the client was never initialised.
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class LcuPreliminarySourceTests : IDisposable
{
    private readonly League.Infrastructure.ResourceLoading _previousResources = Globals.resLoading;

    public LcuPreliminarySourceTests()
    {
        Globals.resLoading = new League.Infrastructure.ResourceLoading();
    }

    public void Dispose() => Globals.resLoading = _previousResources;

    [Fact]
    public async Task LoadIconAsync_Cancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LcuPreliminarySource().LoadIconAsync(103, cancelled.Token)
        );
    }

    [Fact]
    public async Task LoadChampionsAsync_WithoutAClient_Fails()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LcuPreliminarySource().LoadChampionsAsync(CancellationToken.None)
        );
    }
}
