using System.Net;
using System.Reflection;
using League;
using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

internal static class AssistantLifecycleChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type Assistant = typeof(FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;

    public static void Run()
    {
        ClosePendingRecommendation();
        RetireCancellationCallback();
        CloseEquipmentPreview();
        CloseMayhemDetails();
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

    private static void CloseEquipmentPreview()
    {
        using var owner = HiddenForm(new Size(320, 590));
        using var tips = new ToolTip();
        var type = typeof(FormMain).Assembly.GetType("League.Controls.AssistantEquipmentEditor")!;
        var core = JObject.Parse("{\"ids\":[99101,99102,99103]}");
        var data = new JObject
        {
            ["core_items"] = new JArray(core),
            ["last_items"] = new JArray(JObject.Parse("{\"ids\":[99104]}")),
        };
        var build = new OpggBuild(new("global", "aram", "all", "none", 103), "fixture", data, new(), []);
        using var editor = (Form)
            Activator.CreateInstance(
                type,
                new object[]
                {
                    build,
                    new[] { core },
                    0,
                    0,
                    0,
                    CancellationToken.None,
                    (Action<int, int, int>)((_, _, _) => { }),
                    "OP.GG",
                    owner,
                    tips,
                    (Func<JObject, string>)(_ => "fixture"),
                    (Action<Form>)(_ => { }),
                }
            )!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Image? lateImage = null;
        type.GetField("_itemLoader", Flags)!
            .SetValue(
                editor,
                (Func<int, Task<(Image?, string, string)>>)(
                    async _ =>
                    {
                        await release.Task;
                        lateImage = new Bitmap(8, 8);
                        return (lateImage, "fixture", "description");
                    }
                )
            );
        var work = (Task)type.GetMethod("RenderAsync", Flags)!.Invoke(editor, null)!;
        type.GetMethod("Track", Flags)!.Invoke(editor, new object[] { work });
        editor.Dispose();
        var cleanup = (Task)type.GetField("_cleanupWork", Flags)!.GetValue(editor)!;
        Check(!cleanup.IsCompleted, "Equipment preview retains delayed image work after closing.");
        release.TrySetResult();
        PumpUntil(() => cleanup.IsCompleted);
        Check(
            work.IsCompletedSuccessfully && cleanup.IsCompletedSuccessfully,
            "Closing equipment preview completes cancellation normally."
        );
        Check(
            lateImage != null && ImageDisposed(lateImage),
            "The closed equipment preview disposes late item artwork."
        );
    }

    private static void CloseMayhemDetails()
    {
        using var host = new FlowLayoutPanel();
        using var tips = new ToolTip();
        var type = typeof(FormMain).Assembly.GetType("League.Controls.AssistantMayhemBrowser")!;
        var index = AramMetaIndex.Parse(
            JObject.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta", "index.json"))
            )
        );
        var page = new AramMetaPage(index, null, true, DateTime.UtcNow);
        var release = new TaskCompletionSource<AramMetaPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var browser = (IDisposable)
            Activator.CreateInstance(
                type,
                new object[]
                {
                    page,
                    103,
                    CancellationToken.None,
                    host,
                    1,
                    0,
                    tips,
                    Color.White,
                    (Func<CancellationToken, Task<AramMetaPage>>)(_ => release.Task),
                    (Func<int[], int, CancellationToken, Task>)((_, _, _) => Task.CompletedTask),
                }
            )!;
        browser.Dispose();
        var completion = (Task)type.GetProperty("Completion")!.GetValue(browser)!;
        Check(!completion.IsCompleted, "Closed mayhem browser retains its shared detail request.");
        release.TrySetResult(page with { Detail = new JObject() });
        PumpUntil(() => completion.IsCompleted);
        Check(completion.IsCompletedSuccessfully, "Late mayhem detail is cancelled before replacing closed rows.");
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
