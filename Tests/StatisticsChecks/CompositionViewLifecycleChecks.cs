using System.Net;
using System.Reflection;
using League.Clients;
using League.Controls;
using League.Models;

internal static class CompositionViewLifecycleChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Calls;
        public bool Closed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return send(request, token);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    public static void Run()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try
        {
            RetiredRoster();
            ClosingDuringArtwork();
            Console.WriteLine(
                "PASS: composition stale rosters, cancellation callbacks, borrowed clients and late artwork cleanup."
            );
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static T Field<T>(ChampionCompositionView view, string name) =>
        (T)typeof(ChampionCompositionView).GetField(name, PrivateInstance)!.GetValue(view)!;

    private static void SetField(ChampionCompositionView view, string name, object value) =>
        typeof(ChampionCompositionView).GetField(name, PrivateInstance)!.SetValue(view, value);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(5);
            Check(DateTime.UtcNow < deadline, "Timed out waiting for simulated composition work.");
        }
        Application.DoEvents();
    }

    private static string CacheDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "composition-lifecycle-" + Guid.NewGuid().ToString("N"));

    private static HttpResponseMessage AnalysisReply(string alias = "lux") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "ChampionAnalysis", alias + ".json")
                )
            ),
        };

    private static ChampionCompositionView View(ChampionAnalysisClient client)
    {
        var view = new ChampionCompositionView(
            id => id == 99 ? ("拉克丝", "Lux") : ("莫甘娜", "Morgana"),
            () => TimeSpan.FromDays(1),
            client
        );
        SetField(view, "_portraitLoader", (Func<int, Task<Image?>>)(_ => Task.FromResult<Image?>(null)));
        SetField(view, "_skillClient", (Func<HttpClient?>)(() => null));
        return view;
    }

    private static ChampionSelectRoster Roster(int id) => new([new(id, 0, true, false)], [], false);

    private static bool SourceDisposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static void CloseAndRemoveCache(ChampionCompositionView view, string directory)
    {
        view.Dispose();
        PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
        Check(
            Field<Task>(view, "_cleanupWork").IsCompletedSuccessfully,
            "Retired composition work must finish cleanly."
        );
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private static void RetiredRoster()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callbackStarted = new ManualResetEventSlim();
        using var callbackRelease = new ManualResetEventSlim();
        using var handler = new Handler(
            async (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/lux."))
                {
                    // Keep an old transport alive after cancellation to exercise late completion.
                    await release.Task;
                }
                return AnalysisReply(request.RequestUri!.AbsolutePath.Contains("morgana") ? "morgana" : "lux");
            }
        );
        var directory = CacheDirectory();
        using var client = new ChampionAnalysisClient(handler, directory);
        using var view = View(client);
        var snapshots = Field<Dictionary<int, ChampionAnalysisSnapshot>>(view, "_snapshots");
        try
        {
            view.SetRoster(Roster(99));
            PumpUntil(() => handler.Calls == 1);
            var oldWork = Field<HashSet<Task>>(view, "_pendingLoads").Single();
            var oldSource = Field<CancellationTokenSource>(view, "_request");
            using var registration = oldSource.Token.Register(() =>
            {
                callbackStarted.Set();
                // This intentionally slow callback must run away from the UI.
                callbackRelease.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
            });
            view.SetRoster(Roster(25));
            PumpUntil(() => callbackStarted.IsSet && snapshots.ContainsKey(25));
            Check(oldSource.Token.IsCancellationRequested, "Roster changes cancel the old request.");
            release.SetResult();
            PumpUntil(() => oldWork.IsCompleted);
            Check(!SourceDisposed(oldSource), "Cancellation source must outlive its pending callback.");
            callbackRelease.Set();
            PumpUntil(() => SourceDisposed(oldSource));
            Check(!snapshots.ContainsKey(99), "An old response must not publish into the new roster.");
            Check(
                !Field<Dictionary<int, string>>(view, "_errors").ContainsKey(99),
                "Canceled work is not a read error."
            );
            Check(!handler.Closed, "The view borrows its analysis client.");
        }
        finally
        {
            release.TrySetResult();
            callbackRelease.Set();
            CloseAndRemoveCache(view, directory);
        }
    }

    private static void ClosingDuringArtwork()
    {
        var portrait = new TaskCompletionSource<Image?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var skillRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((_, _) => Task.FromResult(AnalysisReply()));
        using var skillHandler = new Handler(
            async (_, _) =>
            {
                await skillRelease.Task;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":99}") };
            }
        );
        using var skillHttp = new HttpClient(skillHandler) { BaseAddress = new Uri("https://fixture.test/") };
        var directory = CacheDirectory();
        using var client = new ChampionAnalysisClient(handler, directory);
        using var view = View(client);
        using var lateImage = new Bitmap(8, 8);
        SetField(view, "_portraitLoader", (Func<int, Task<Image?>>)(_ => portrait.Task));
        SetField(view, "_skillClient", (Func<HttpClient?>)(() => skillHttp));
        try
        {
            view.SetRoster(Roster(99));
            PumpUntil(() => skillHandler.Calls == 1);
            var source = Field<CancellationTokenSource>(view, "_viewLifetime");
            view.Dispose();
            Check(source.Token.IsCancellationRequested, "Closing cancels skill HTTP requests.");
            Check(!Field<Task>(view, "_cleanupWork").IsCompleted, "Pending portraits retain their lifetime.");
            portrait.SetResult(lateImage);
            skillRelease.SetResult();
            PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
            Check(SourceDisposed(source), "View lifetime is disposed only after portrait and skill work.");
            Check(ImageDisposed(lateImage), "The view disposes a late owned portrait.");
            Check(
                Field<Dictionary<int, Image>>(view, "_portraits").Count == 0,
                "Closed views do not cache late portraits."
            );
            Check(
                Field<Dictionary<int, DateTime>>(view, "_skillRetryAfter").Count == 0,
                "Closing does not schedule a skill retry."
            );
            Check(!skillHandler.Closed && !handler.Closed, "Borrowed clients remain available to their owner.");
        }
        finally
        {
            portrait.TrySetResult(lateImage);
            skillRelease.TrySetResult();
            CloseAndRemoveCache(view, directory);
        }
    }

    private static bool ImageDisposed(Image image)
    {
        try
        {
            _ = image.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
