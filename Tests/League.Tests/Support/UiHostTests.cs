namespace League.Tests.Support;

/// <summary>
/// The UI test host itself. These checks are what lets the rest of the interface tests be ordinary xUnit tests: they
/// pin the thread, the synchronization context and the message loop the application's own code expects.
/// </summary>
[Trait("Category", "UI")]
public class UiHostTests
{
    [Fact]
    public void Run_Body_RunsOnAnStaThreadWithAMessageLoop()
    {
        var apartment = ApartmentState.Unknown;
        var hasHandle = false;

        UiHost.Run(host =>
        {
            apartment = Thread.CurrentThread.GetApartmentState();
            hasHandle = host.IsHandleCreated;
        });

        Assert.Equal(ApartmentState.STA, apartment);
        Assert.True(hasHandle, "The host window is shown before the body runs.");
    }

    [Fact]
    public void Run_AwaitedBody_ResumesOnTheSamePumpingThread()
    {
        var before = 0;
        var after = 0;
        var pumping = false;

        UiHost.Run(async _ =>
        {
            before = Environment.CurrentManagedThreadId;
            pumping = UiHost.OnUiThread;

            await Task.Yield();

            after = Environment.CurrentManagedThreadId;
        });

        Assert.NotEqual(0, before);
        Assert.True(pumping, "The body runs with the application's own synchronization context installed.");
        Assert.Equal(before, after);
    }

    [Fact]
    public void Run_FailingBody_ReportsTheFailureHere()
    {
        var action = new Action<Form>(_ => throw new InvalidOperationException("the body failed"));

        var failure = Assert.Throws<InvalidOperationException>(() => UiHost.Run(action));

        Assert.Equal("the body failed", failure.Message);
    }
}
