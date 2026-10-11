using System.Net;
using System.Reflection;
using League;
using League.Clients;
using static StatisticsCheckSupport;

internal static class AssistantLifecycleChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type Assistant = typeof(FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;

    public static void Run()
    {
        ClosePendingRecommendation();
        RetireCancellationCallback();
        Console.WriteLine(
            "PASS: assistant closing waits for recommendation work and cancellation callbacks before cleanup. No live requests."
        );
    }

    private static T Field<T>(object target, string name) => (T)Assistant.GetField(name, Flags)!.GetValue(target)!;

    private static void Set(object target, string name, object value) =>
        Assistant.GetField(name, Flags)!.SetValue(target, value);

    private static Task Invoke(object target, string name, params object[] args) =>
        (Task)Assistant.GetMethod(name, Flags)!.Invoke(target, args)!;

    private static Form Create(FormMain owner)
    {
        var assistant = (Form)Activator.CreateInstance(Assistant, new object[] { owner })!;
        Field<System.Windows.Forms.Timer>(assistant, "_timer").Stop();
        Field<ComboBox>(assistant, "_mode").SelectedIndex = 2;
        return assistant;
    }

    private static void ClosePendingRecommendation()
    {
        using var owner = new FormMain();
        using var assistant = Create(owner);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new WaitingHandler(release.Task);
        Field<OpggClient>(assistant, "_opgg").Dispose();
        Set(assistant, "_opgg", new OpggClient(handler));
        Set(assistant, "_hero", 103);
        var work = Invoke(assistant, "LoadHeroAsync");
        PumpUntil(() => handler.Started);
        var source = Field<CancellationTokenSource>(assistant, "_load");
        try
        {
            assistant.Dispose();
            Check(
                source.IsCancellationRequested && !Field<Task>(assistant, "_cleanupWork").IsCompleted,
                "Closing cancels the recommendation and keeps cleanup pending while its handler is active."
            );
            Check(!handler.Closed, "The recommendation client stays alive until pending work ends.");
            release.TrySetResult();
            PumpUntil(() => Field<Task>(assistant, "_cleanupWork").IsCompleted);
            Check(
                work.IsCompletedSuccessfully && handler.Closed,
                "Late cancellation ends normally before the client is disposed."
            );
            Check(
                Field<Task>(assistant, "_cleanupWork").IsCompletedSuccessfully,
                "Assistant cleanup must finish without faults."
            );
            Check(Disposed(source), "The retired hero cancellation source is eventually disposed.");
        }
        finally
        {
            release.TrySetResult();
            assistant.Dispose();
            PumpUntil(() => Field<Task>(assistant, "_cleanupWork").IsCompleted);
        }
    }

    private static void RetireCancellationCallback()
    {
        using var owner = new FormMain();
        using var assistant = Create(owner);
        var source = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = source.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });
        Set(assistant, "_load", source);
        try
        {
            assistant.Dispose();
            PumpUntil(() => entered.Task.IsCompleted);
            Check(
                !Field<Task>(assistant, "_cleanupWork").IsCompleted && !Disposed(source),
                "Closing returns without blocking the UI and retains the source while its callback runs."
            );
            release.TrySetResult();
            PumpUntil(() => Field<Task>(assistant, "_cleanupWork").IsCompleted);
            Check(Disposed(source), "Cancellation callbacks finish before source disposal.");
        }
        finally
        {
            release.TrySetResult();
            assistant.Dispose();
            PumpUntil(() => Field<Task>(assistant, "_cleanupWork").IsCompleted);
        }
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

    private sealed class WaitingHandler(Task release) : HttpMessageHandler
    {
        public bool Started { get; private set; }
        public bool Closed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Started = true;
            await release;
            token.ThrowIfCancellationRequested();
            return new(HttpStatusCode.ServiceUnavailable);
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }
}
