using System.Net;
using System.Reflection;
using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class HistoryPresentationChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Handler(Func<int, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token) =>
            response(++Calls);
    }

    public static void Run()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        TimelineRendering();
        CareerFallback();
        RetiredRequests();
        Console.WriteLine(
            "PASS: timeline filtering and missing frames, career fallback/privacy and stale request retirement."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, args);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static Form Host(Control view)
    {
        var host = new Form
        {
            ClientSize = new Size(850, 650),
            ShowInTaskbar = false,
            Location = new Point(-30000, -30000),
            StartPosition = FormStartPosition.Manual,
        };
        view.Dock = DockStyle.Fill;
        host.Controls.Add(view);
        host.Show();
        Application.DoEvents();
        return host;
    }

    private static HttpResponseMessage Reply(JToken data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(data.ToString()) };

    private static HttpClient Client(Handler handler) => new(handler) { BaseAddress = new Uri("https://mock.test/") };

    private static MatchInfo Match() =>
        new()
        {
            GameId = 1,
            QueueId = "q_450",
            AllParticipants = new()
            {
                new JObject
                {
                    ["participantId"] = 1,
                    ["teamId"] = 200,
                    ["riotIdGameName"] = "红队玩家",
                },
                new JObject
                {
                    ["participantId"] = 7,
                    ["teamId"] = 100,
                    ["riotIdGameName"] = "蓝队玩家",
                },
            },
        };

    private static JObject Timeline() =>
        JObject.Parse(
            """
            {"gameId":1,"frames":[
              {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
              {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]},
              {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
              {"timestamp":180000,"participantFrames":{"1":{"totalGold":3200},"7":{"totalGold":3400}},"events":[]}
            ]}
            """
        );

    private static JArray Records(string puuid) =>
        JArray.Parse(
            $$$"""
            [{"gameId":1,"gameCreation":1,"queueId":450,"gameDuration":1200,
              "participants":[{"puuid":"{{{puuid}}}","teamId":100,"championId":1,
                "stats":{"win":true,"kills":2,"deaths":1,"assists":3,"totalDamageDealtToChampions":10000,"goldEarned":9000,"pentaKills":0}}]}]
            """
        );

    private static void TimelineRendering()
    {
        using var client = Client(new Handler(_ => Task.FromResult(Reply(Timeline()))));
        using var match = Match();
        using var view = new TimelineView(match, new PlayerStatisticsService(() => client));
        using var host = Host(view);
        var events = Field<ListView>(view, "_events");
        var time = Field<TrackBar>(view, "_time");
        Check(events.Items.Count == 2 && time.Maximum == 3, "Timeline must render all events and frames");
        Field<ComboBox>(view, "_filter").SelectedIndex = 1;
        Check(
            events.Items.Count == 1 && events.Items[0].SubItems[1].Text == "蓝队玩家",
            "Kill filters must retain participant mapping"
        );
        time.Value = 2;
        Check(
            Field<Label>(view, "_status").Text.Contains("数据缺失"),
            "A missing frame must remain a gap rather than zero gold"
        );
        using var image = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(image, view.ClientRectangle);
        image.Save(Path.Combine(AppContext.BaseDirectory, "timeline-quality.png"));
        var wrong = Timeline();
        wrong["gameId"] = 99;
        using var wrongClient = Client(new Handler(_ => Task.FromResult(Reply(wrong))));
        using var wrongView = new TimelineView(match, new PlayerStatisticsService(() => wrongClient));
        using var wrongHost = Host(wrongView);
        Check(
            Field<Label>(wrongView, "_status").Text.Contains("不匹配")
                && Field<ListView>(wrongView, "_events").Items.Count == 0,
            "A timeline from another match must not be shown"
        );
    }

    private static void CareerFallback()
    {
        using var client = Client(
            new Handler(call =>
                Task.FromResult(
                    call == 1
                        ? Reply(new JObject(), HttpStatusCode.NotFound)
                        : Reply(new JObject { ["games"] = Records("self") })
                )
            )
        );
        using var view = new CareerStatisticsView(new PlayerStatisticsService(() => client));
        using var host = Host(view);
        view.LoadPlayer("self");
        Application.DoEvents();
        Check(Field<ListView>(view, "_modes").Items.Count == 1, "Career fallback must retain the mode summary");
        Check(
            Field<Label>(view, "_status").Text.Contains("不代表完整生涯"),
            "A history sample must disclose its scope"
        );
        Check(
            Field<Label>(view, "_summary").Text.Contains("KDA 5.00"),
            "Career summary must preserve the ratio of totals"
        );
        var denied = new Handler(_ => Task.FromResult(Reply(new JObject(), HttpStatusCode.Forbidden)));
        using var deniedClient = Client(denied);
        using var deniedView = new CareerStatisticsView(
            new PlayerStatisticsService(() => deniedClient),
            (_, _) => throw new Exception("Private career records must not fall back")
        );
        using var deniedHost = Host(deniedView);
        deniedView.LoadPlayer("hidden");
        Application.DoEvents();
        Check(
            denied.Calls == 1 && Field<ListView>(deniedView, "_modes").Items.Count == 0,
            "Privacy rejection must not request history or fabricate statistics"
        );
    }

    private static void RetiredRequests()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(new Handler(call => call == 1 ? delayed.Task : Task.FromResult(Reply(Timeline()))));
        using var match = Match();
        using var view = new TimelineView(match, new PlayerStatisticsService(() => client));
        using var host = Host(view);
        var retired = Field<CancellationTokenSource>(view, "_request");
        Invoke(view, "LoadTimeline");
        Check(
            retired.Token.IsCancellationRequested,
            "A retry must cancel the retired request without disposing its unfinished worker"
        );
        Check(Field<ListView>(view, "_events").Items.Count == 2, "The replacement request must publish independently");
        var wrong = Timeline();
        wrong["gameId"] = 99;
        delayed.SetResult(Reply(wrong));
        PumpUntil(() => Disposed(retired));
        Check(
            !Field<Label>(view, "_status").Text.Contains("不匹配"),
            "A cancelled late response must not replace the new timeline"
        );
        CareerRetirement();
    }

    private static void CareerRetirement()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(
            new Handler(call => call == 1 ? delayed.Task : Task.FromResult(Reply(Records("new"))))
        );
        using var view = new CareerStatisticsView(new PlayerStatisticsService(() => client));
        using var host = Host(view);
        view.LoadPlayer("old");
        var retired = Field<CancellationTokenSource>(view, "_request");
        view.LoadPlayer("new");
        Check(retired.Token.IsCancellationRequested, "Switching players must retire the old career request");
        delayed.SetResult(Reply(new JObject(), HttpStatusCode.Forbidden));
        PumpUntil(() => Disposed(retired));
        Check(
            Field<Label>(view, "_summary").Text.Contains("KDA 5.00"),
            "A retired private response must not clear the replacement player's summary"
        );
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
            // Reading the token checks that retirement released the source after request completion.
            return true;
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Check(condition(), "Retired request did not finish and release its source");
    }
}
