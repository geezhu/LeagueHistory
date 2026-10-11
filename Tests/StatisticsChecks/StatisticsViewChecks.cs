using System.Net;
using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;
using static StatisticsFixtures;

internal static class StatisticsViewChecks
{
    public static void Run()
    {
        var handler = new Handler(path =>
            path.Contains("game-timelines") ? new(HttpStatusCode.OK, Timeline().ToString())
            : path.Contains("career-stats") ? new(HttpStatusCode.NotFound, "")
            : new(HttpStatusCode.OK, new JObject { ["games"] = new JObject { ["games"] = Records() } }.ToString())
        );
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new PlayerStatisticsService(() => client);
        PrivacyAndCancellation(service);
        using var form = HiddenForm(new Size(850, 650));
        form.Show();
        TimelineSurface(form, service);
        CareerSurface(form, service);
        DeniedCareer(form);
        form.Close();
    }

    private static void PrivacyAndCancellation(PlayerStatisticsService service)
    {
        Check(
            new PlayerStatisticsService(() => null).GetAsync("/", default).GetAwaiter().GetResult().Data == null,
            "Disconnected client"
        );
        using var forbiddenClient = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden, "")))
        {
            BaseAddress = new Uri("https://localhost/"),
        };
        Check(
            new PlayerStatisticsService(() => forbiddenClient).GetAsync("/", default).GetAwaiter().GetResult().Status
                == 403,
            "Privacy status"
        );
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            service.GetAsync("/", canceled.Token).GetAwaiter().GetResult();
            throw new Exception("Cancellation swallowed");
        }
        catch (OperationCanceledException)
        {
            // The service must propagate a caller's cancelled request.
        }
    }

    private static void TimelineSurface(Form form, PlayerStatisticsService service)
    {
        var view = new TimelineView(
            new MatchInfo
            {
                GameId = 1,
                QueueId = "q_450",
                AllParticipants = Players(),
            },
            service
        )
        {
            Dock = DockStyle.Fill,
        };
        form.Controls.Add(view);
        form.Show();
        PumpUntil(() => Children(view).OfType<ListView>().Single().Items.Count == 2);
        var events = Children(view).OfType<ListView>().Single();
        Check(events.Items.Count == 2, "Timeline event rendering");
        Children(view).OfType<ComboBox>().Single().SelectedIndex = 1;
        Check(events.Items.Count == 1, "Kill filter");
        Children(view).OfType<TrackBar>().Single().Value = 2;
        using (var bitmap = new Bitmap(view.Width, view.Height))
        {
            view.DrawToBitmap(bitmap, view.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "timeline-preview.png"));
        }
        view.Dispose();
    }

    private static void CareerSurface(Form form, PlayerStatisticsService service)
    {
        var career = new CareerStatisticsView(service) { Dock = DockStyle.Fill };
        form.Controls.Add(career);
        career.LoadPlayer("self");
        PumpUntil(() => Children(career).OfType<ListView>().Single().Items.Count == 1);
        Check(Children(career).OfType<ListView>().Single().Items.Count == 1, "Career fallback mode table");
        Check(
            Children(career).OfType<Label>().Any(l => l.Text.Contains("不代表完整生涯")),
            "Fallback scope disclosure"
        );
        using (var bitmap = new Bitmap(career.Width, career.Height))
        {
            career.DrawToBitmap(bitmap, career.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "career-preview.png"));
        }
        career.Dispose();
    }

    private static void DeniedCareer(Form form)
    {
        int deniedCalls = 0;
        using var deniedClient = new HttpClient(
            new Handler(_ =>
            {
                deniedCalls++;
                return new(HttpStatusCode.Forbidden, "");
            })
        )
        {
            BaseAddress = new Uri("https://localhost/"),
        };
        var deniedView = new CareerStatisticsView(
            new PlayerStatisticsService(() => deniedClient),
            (_, _) => throw new Exception("403 must not fall back")
        );
        form.Controls.Add(deniedView);
        deniedView.LoadPlayer("self");
        PumpUntil(() => deniedCalls == 1);
        Check(deniedCalls == 1, "Denied career must not request history");
        deniedView.Dispose();
    }
}
