using System.Net;
using System.Reflection;
using League.Clients;
using League.Controls;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class OpggViewLifecycleChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly Views = typeof(PlayerCardControl).Assembly;

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        public bool Closed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return send(request);
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
            RankingImageRetirement();
            ChangedChampion();
            ClosePendingPage();
            RuneApplicationRetirement();
            Console.WriteLine(
                "PASS: OP.GG stale pages, cache reuse, image gate retirement and selected rune application. No live requests or writes."
            );
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance)!.SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target
            .GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(target, args);

    private static void PumpUntil(Func<bool> ready, Func<string>? state = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(5);
            Check(DateTime.UtcNow < deadline, "Timed out waiting for simulated OP.GG work. " + state?.Invoke());
        }
        Application.DoEvents();
    }

    private static JObject Fixture(string name) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opgg", name + ".json")));

    private static HttpResponseMessage Reply(string fixture, int? champion = null)
    {
        var data = Fixture(fixture);
        if (champion.HasValue)
        {
            data["data"]!["summary"]!["id"] = champion.Value;
        }
        return new(HttpStatusCode.OK) { Content = new StringContent(data.ToString()) };
    }

    private static bool Disposed(CancellationTokenSource source)
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

    private static bool Disposed(Image image)
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

    private static void Close(Control view)
    {
        view.Dispose();
        PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
        Check(Field<Task>(view, "_cleanupWork").IsCompletedSuccessfully, "Owned cleanup must finish without faults.");
    }

    private static void RankingImageRetirement()
    {
        using var view = (Control)Activator.CreateInstance(Views.GetType("League.Controls.OpggRankingsView")!)!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var copies = new List<Image>();
        int calls = 0;
        SetField(
            view,
            "_iconLoader",
            (Func<int, Task<Image?>>)(
                async _ =>
                {
                    calls++;
                    await release.Task;
                    var image = new Bitmap(8, 8);
                    copies.Add(image);
                    return image;
                }
            )
        );
        var work = Enumerable.Range(1, 5).Select(id => (Task)Invoke(view, "LoadIconAsync", id)!).ToArray();
        var source = Field<CancellationTokenSource>(view, "_lifetime");
        var gate = Field<SemaphoreSlim>(view, "_imageGate");
        try
        {
            PumpUntil(() => calls == 4);
            view.Dispose();
            Check(
                source.Token.IsCancellationRequested && !Field<Task>(view, "_cleanupWork").IsCompleted,
                "Closing cancels queued icons and retains active image workers."
            );
            Check(gate.CurrentCount == 0, "The gate remains valid while active workers own its permits.");
            release.SetResult();
            Close(view);
            Check(
                work.All(task => task.IsCompletedSuccessfully) && copies.All(Disposed),
                "Late artwork is discarded and disposed."
            );
            Check(
                calls == 4 && Disposed(source),
                "The fifth queued icon never starts and the source is eventually disposed."
            );
            try
            {
                gate.Wait(0, CancellationToken.None);
                throw new Exception("The ranking image gate was not disposed after its workers ended.");
            }
            catch (ObjectDisposedException)
            {
                // Confirm the lock is released at the final ownership boundary.
            }
        }
        finally
        {
            release.TrySetResult();
            Close(view);
            foreach (var copy in copies)
            {
                copy.Dispose();
            }
        }
    }

    private static Control Page(OpggClient client)
    {
        var type = Views.GetType("League.Controls.OpggView")!;
        var view = (Control)Activator.CreateInstance(type, PrivateInstance, null, new object[] { client }, null)!;
        SetField(
            view,
            "_resourceLoader",
            (Func<int, string, Task<(Image? image, string name, string description)>>)(
                (id, _) => Task.FromResult<(Image?, string, string)>((null, "英雄 " + id, ""))
            )
        );
        SetField(view, "_selectionClient", (Func<HttpClient?>)(() => null));
        return view;
    }

    private static Task Open(Control view, int champion) =>
        (Task)Invoke(view, "OpenQueryAsync", new OpggQuery("global", "ranked", "emerald_plus", "mid", champion))!;

    private static void ChangedChampion()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool oldStarted = false;
        using var handler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/103/"))
            {
                oldStarted = true;
                await release.Task;
            }
            return request.RequestUri.AbsolutePath.EndsWith("ranked")
                ? Reply("tiers")
                : Reply("ranked", request.RequestUri.AbsolutePath.Contains("/25/") ? 25 : 103);
        });
        using var client = new OpggClient(handler);
        using var view = Page(client);
        try
        {
            var oldWork = Open(view, 103);
            PumpUntil(() => oldStarted);
            var oldSource = Field<CancellationTokenSource>(view, "_request");
            var current = Open(view, 25);
            Check(
                oldSource.Token.IsCancellationRequested && !oldWork.IsCompleted,
                "A changed champion retires the pending old page."
            );
            release.SetResult();
            PumpUntil(
                () => current.IsCompleted && oldWork.IsCompleted && Disposed(oldSource),
                () =>
                    $"old={oldWork.Status}, current={current.Status}, retired={Disposed(oldSource)}, status={Field<Label>(view, "_status").Text}, context={SynchronizationContext.Current?.GetType().Name}"
            );
            Check(
                current.IsCompletedSuccessfully && oldWork.IsCompletedSuccessfully,
                "Canceled old pages finish without surfacing a failure."
            );
            Check(
                Field<OpggBuild?>(view, "_displayed")?.Query.ChampionId == 25,
                "Only the current champion can publish its build: " + Field<Label>(view, "_status").Text
            );
            int requests = handler.Calls;
            var cached = Open(view, 25);
            PumpUntil(() => cached.IsCompleted);
            Check(
                cached.IsCompletedSuccessfully && handler.Calls == requests,
                "Repeated query reuses the client tier/build cache."
            );
        }
        finally
        {
            release.TrySetResult();
            Close(view);
        }
    }

    private static void ClosePendingPage()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async _ =>
        {
            await release.Task;
            return Reply("tiers");
        });
        using var client = new OpggClient(handler);
        using var view = Page(client);
        try
        {
            var work = Open(view, 103);
            PumpUntil(() => handler.Calls == 1);
            var source = Field<CancellationTokenSource>(view, "_request");
            view.Dispose();
            Check(
                source.Token.IsCancellationRequested && !handler.Closed,
                "The page's HTTP client outlives its pending worker."
            );
            release.SetResult();
            Close(view);
            Check(
                work.IsCompletedSuccessfully && handler.Closed && Disposed(source),
                "Closing waits for work before disposing the owned client."
            );
        }
        finally
        {
            release.TrySetResult();
            Close(view);
        }
    }

    private static IEnumerable<Control> Children(Control root) =>
        root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child)));

    private static Control CreateRuneRecommendations(
        OpggRunes[] plans,
        CancellationToken token,
        Func<bool> canApply,
        Func<OpggRunes, CancellationToken, Task> apply,
        List<Image> copies
    ) =>
        (Control)
            Activator.CreateInstance(
                Views.GetType("League.Controls.OpggRuneRecommendations")!,
                new object[]
                {
                    plans,
                    (Func<OpggRunes, CancellationToken, Task<Image?>>)(
                        (_, _) =>
                        {
                            var image = new Bitmap(8, 8);
                            copies.Add(image);
                            return Task.FromResult<Image?>(image);
                        }
                    ),
                    (Func<RuneResourceLoader?>)(() => null),
                    canApply,
                    apply,
                    (Action<string>)(_ => throw new Exception("A canceled preview must not report a stale error.")),
                    token,
                }
            )!;

    private static void RuneApplicationRetirement()
    {
        var plans = OpggBuild.Parse(Fixture("aram"), new("global", "aram", "all", "none", 103)).Runes.Take(3).ToArray();
        Check(plans.Length >= 2, "The fixture contains multiple rune choices.");
        using var parent = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var copies = new List<Image>();
        bool allowed = false;
        int writes = 0;
        OpggRunes? applied = null;
        using var view = CreateRuneRecommendations(
            plans,
            parent.Token,
            () => allowed,
            async (plan, _) =>
            {
                writes++;
                applied = plan;
                await release.Task;
            },
            copies
        );
        try
        {
            var initial = (Task)Invoke(view, "InitializeAsync")!;
            PumpUntil(() => initial.IsCompleted);
            Check(initial.IsCompletedSuccessfully, "Rune previews initialize without a client resource catalog.");
            var apply = Field<Button>(view, "_apply");
            apply.PerformClick();
            PumpUntil(() => Field<HashSet<Task>>(view, "_work").Count == 0);
            Check(writes == 0, "A retired or unauthorized containing page cannot apply a rune scheme.");
            allowed = true;
            var choice = Children(view).OfType<Button>().Single(button => button.Tag is int index && index == 1);
            choice.PerformClick();
            PumpUntil(() => Field<int>(view, "_selected") == 1 && Field<Button>(view, "_apply").Enabled);
            apply.PerformClick();
            PumpUntil(() => writes == 1);
            Check(
                ReferenceEquals(applied, plans[1]) && !apply.Enabled && !choice.Enabled,
                "Only the selected scheme is applied while choices are locked."
            );
            allowed = false;
            view.Dispose();
            var source = Field<CancellationTokenSource>(view, "_lifetime");
            Check(
                source.Token.IsCancellationRequested && !Field<Task>(view, "_cleanupWork").IsCompleted,
                "Pending application retains its canceled lifetime."
            );
            release.SetResult();
            Close(view);
            Check(
                Disposed(source) && copies.All(Disposed) && !parent.IsCancellationRequested,
                "Component cleanup owns images and its source, not its parent."
            );
        }
        finally
        {
            release.TrySetResult();
            Close(view);
            foreach (var copy in copies)
            {
                copy.Dispose();
            }
        }
    }
}
