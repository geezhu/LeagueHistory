using System.Net;
using System.Reflection;
using League.Clients;
using League.Controls;
using Newtonsoft.Json.Linq;

internal static class ProfilePresentationChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int FirstChampion = 1000001;
    private const int SecondChampion = 1000002;

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request);
    }

    public static void Run()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        try
        {
            SummaryAndGallery();
            LatePlayerIcons();
            CloseFromProfileNotification();
            Console.WriteLine(
                "PASS: profile mastery counts, navigation, visible gallery batching, stale icons and notification disposal."
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

    private static IEnumerable<Control> Children(Control root) =>
        root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child)));

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(5);
            Check(DateTime.UtcNow < deadline, "Timed out waiting for simulated profile work.");
        }
        Application.DoEvents();
    }

    private static HttpResponseMessage Reply(JToken value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };

    private static JObject Mastery(int id, int points = 80000) =>
        new()
        {
            ["championId"] = id,
            ["championPoints"] = points,
            ["championLevel"] = 9,
        };

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new Handler(send)) { BaseAddress = new Uri("https://fixture.test/") };

    private static Task<HttpResponseMessage> ProfileReply(HttpRequestMessage request, JArray mastery) =>
        Task.FromResult(
            Reply(
                request.RequestUri!.AbsolutePath.Contains("champion-mastery") ? mastery
                : request.RequestUri.AbsolutePath.Contains("champion-summary") ? JArray.Parse("[{\"id\":1},{\"id\":2}]")
                : request.RequestUri.AbsolutePath.Contains("champions-minimal")
                    ? JArray.Parse(
                        "[{\"id\":1,\"ownership\":{\"owned\":true}},{\"id\":2,\"ownership\":{\"owned\":false}}]"
                    )
                : JObject.Parse("{\"summonerId\":7}")
            )
        );

    private static void Close(ProfileMasteryView view)
    {
        view.Dispose();
        PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
        Check(Field<Task>(view, "_cleanupWork").IsCompletedSuccessfully, "Profile work should finish cleanly.");
    }

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

    private static void SummaryAndGallery()
    {
        Check(
            League.FormMain.Globals.resLoading.GetChampionById(FirstChampion) == null
                && League.FormMain.Globals.resLoading.GetChampionById(SecondChampion) == null,
            "Synthetic champions avoid public artwork requests."
        );
        var records = new JArray(Mastery(FirstChampion), Mastery(SecondChampion, 20000));
        using var client = Client(request => ProfileReply(request, records));
        using var borrowedIcon = new Bitmap(16, 16);
        using var view = new ProfileMasteryView(
            new PlayerStatisticsService(() => client),
            id => Task.FromResult<(Image?, string, string)>((borrowedIcon, "测试英雄 " + id, ""))
        );
        try
        {
            view.LoadPlayer("sample");
            PumpUntil(() => Children(view).OfType<PictureBox>().Count() == 2 && view.OwnershipText == "拥有 1 / 2");
            Check(
                Children(view).OfType<Label>().Any(label => label.Text.Contains("80,000")),
                "Mastery points stay visible."
            );
            Check(
                Children(view).OfType<PictureBox>().All(poster => !ReferenceEquals(poster.Image, borrowedIcon)),
                "Posters own image copies, not the provider's bitmap."
            );
            JObject[]? selected = null;
            view.AllRequested += value => selected = value;
            Children(view).OfType<Button>().Single(button => button.Text == "全部 ›").PerformClick();
            Check(selected?.Length == 2, "All mastery navigation reuses the loaded records.");
            GalleryBatching();
        }
        finally
        {
            Close(view);
        }
        Check(borrowedIcon.Width == 16, "Closing the profile must retain a borrowed provider image.");
    }

    private static void GalleryBatching()
    {
        var type = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.MasteryGalleryView")!;
        var records = Enumerable.Range(0, 12).Select(_ => Mastery(FirstChampion)).ToArray();
        using var gallery = (Control)Activator.CreateInstance(type, new object[] { records, "拥有 1 / 2" })!;
        using var host = new Form
        {
            ClientSize = new Size(720, 900),
            ShowInTaskbar = false,
            Location = new Point(-30000, -30000),
            StartPosition = FormStartPosition.Manual,
        };
        gallery.Dock = DockStyle.Fill;
        host.Controls.Add(gallery);
        host.Show();
        var timer = Field<System.Windows.Forms.Timer>(gallery, "_loadTimer");
        timer.Stop();
        var pending = Field<object>(gallery, "_pending");
        int Count() => (int)pending.GetType().GetProperty("Count")!.GetValue(pending)!;
        Check(
            Children(gallery).OfType<PictureBox>().Count() == 12 && Count() == 12,
            "Gallery displays all loaded records."
        );
        var work = (Task)type.GetMethod("LoadVisiblePostersAsync", PrivateInstance)!.Invoke(gallery, null)!;
        PumpUntil(() => work.IsCompleted);
        timer.Stop();
        Check(work.IsCompletedSuccessfully && Count() == 8, "Gallery requests only four visible posters per batch.");
        gallery.Dispose();
        Check(!timer.Enabled, "Closing the gallery stops scheduled artwork.");
    }

    private static void LatePlayerIcons()
    {
        var release = new TaskCompletionSource<(Image?, string, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var icon = new Bitmap(16, 16);
        using var client = Client(request =>
            ProfileReply(
                request,
                new JArray(Mastery(request.RequestUri!.AbsolutePath.Contains("/old/") ? FirstChampion : SecondChampion))
            )
        );
        using var view = new ProfileMasteryView(
            new PlayerStatisticsService(() => client),
            id =>
                id == FirstChampion ? release.Task : Task.FromResult<(Image?, string, string)>((icon, "新玩家英雄", ""))
        );
        try
        {
            view.LoadPlayer("old");
            PumpUntil(() => view.Height == 360);
            var oldSource = Field<CancellationTokenSource>(view, "_request");
            view.LoadPlayer("new");
            PumpUntil(() => Children(view).OfType<Label>().Any(label => label.Text.Contains("新玩家英雄")));
            Check(oldSource.Token.IsCancellationRequested, "Changing players cancels the old request.");
            release.SetResult((icon, "旧玩家英雄", ""));
            PumpUntil(() => SourceDisposed(oldSource));
            Check(
                !Children(view).OfType<Label>().Any(label => label.Text.Contains("旧玩家英雄")),
                "A late icon cannot add an old card."
            );
            Check(
                Children(view).OfType<PictureBox>().Count() == 1 && icon.Width == 16,
                "The new player retains one owned copy."
            );
        }
        finally
        {
            release.TrySetResult((icon, "旧玩家英雄", ""));
            Close(view);
        }
    }

    private static void CloseFromProfileNotification()
    {
        int masteryReads = 0;
        using var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("champion-mastery"))
            {
                masteryReads++;
            }
            return ProfileReply(request, new JArray(Mastery(FirstChampion)));
        });
        using var view = new ProfileMasteryView(
            new PlayerStatisticsService(() => client),
            _ => Task.FromResult<(Image?, string, string)>((null, "", ""))
        );
        view.ProfileLoaded += _ => view.Dispose();
        view.LoadPlayer("closing");
        var source = Field<CancellationTokenSource>(view, "_request");
        PumpUntil(() => view.IsDisposed);
        Close(view);
        Check(
            SourceDisposed(source) && masteryReads == 0,
            "Notification disposal cancels the already registered mastery worker."
        );
    }
}
