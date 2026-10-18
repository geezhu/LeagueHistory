using System.Net;
using System.Text;
using League.Clients;
using League.Controls;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The career page: the summary line, the per-mode table and the status line above them, fed by the career endpoint
/// and by the recent-history fallback. The statistics service talks to a stub handler, so nothing is requested over
/// the network and the responses are whatever the test says they are.
/// </summary>
[Trait("Category", "UI")]
public class CareerStatisticsViewTests
{
    [Fact]
    public void LoadPlayer_BlankPuuid_DoesNotAskForAnything()
    {
        UiHost.Run(host =>
        {
            var handler = new CareerHandler(_ => Json(Records("self")));
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer(" ");

            Assert.Empty(handler.Paths);
            Assert.Equal("连接客户端后加载统计。", Status(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_CareerRecords_ShowsTheSummaryAndTheModeRow()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(_ => Json(Records("self")));
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("self");
            await WaitFor(() => Modes(view).Items.Count > 0);

            Assert.Equal(["/lol-career-stats/v1/summoner-games/self"], handler.Paths);
            Assert.Contains("生涯接口返回 1 条记录", Status(view).Text);
            Assert.Contains("1 场", Summary(view).Text);
            Assert.Contains("KDA 5.00", Summary(view).Text);
            Assert.Equal(["极地大乱斗"], ModeRows(view));
        });
    }

    [Fact]
    public void LoadPlayer_HistoryFallback_DisclosesThatItCoversOnlyRecentGames()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(uri =>
                IsCareer(uri) ? Json(new JObject(), HttpStatusCode.NotFound) : Json(Records("self"))
            );
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("self");
            await WaitFor(() => Modes(view).Items.Count > 0);

            Assert.Equal(2, handler.Paths.Count);
            Assert.Contains("不代表完整生涯", Status(view).Text);
            Assert.Contains("最近 1 条历史记录", Status(view).Text);
            Assert.Contains("KDA 5.00", Summary(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_PrivacyRejection_DoesNotAskForHistory()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(_ => Json(new JObject(), HttpStatusCode.Forbidden));
            using var client = Client(handler);
            using var view = View(host, client, HistoryThatMustNotBeUsed);

            view.LoadPlayer("hidden");
            await WaitFor(() => !Status(view).Text.StartsWith("正在读取"));

            Assert.Single(handler.Paths);
            Assert.Contains("不允许访问", Status(view).Text);
            Assert.Empty(Modes(view).Items);
        });
    }

    [Fact]
    public void LoadPlayer_RecordsWithNothingRecognizable_SaysSoWithoutInventingStatistics()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(_ => Json(new JObject { ["games"] = new JArray(new JObject()) }));
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("self");
            await WaitFor(() => !Status(view).Text.StartsWith("正在读取"));

            Assert.Contains("1 条数据不完整", Status(view).Text);
            Assert.Equal("暂无可统计的比赛", Summary(view).Text);
            Assert.Empty(Modes(view).Items);
        });
    }

    [Fact]
    public void LoadPlayer_StatisticsUnavailable_ReportsWhatTheClientSaid()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(_ => Json(new JObject(), HttpStatusCode.InternalServerError));
            using var client = Client(handler);
            using var view = View(host, client, (_, _) => Task.FromResult<JArray?>(null));

            view.LoadPlayer("self");
            await WaitFor(() => !Status(view).Text.StartsWith("正在读取"));

            Assert.Contains("数据暂时不可用（HTTP 500）", Status(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_EmptyAnswers_FallThroughToTheRecentHistoryLoader()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(_ => Json(new JObject()));
            using var client = Client(handler);
            int loaded = 0;
            using var view = View(
                host,
                client,
                (_, _) =>
                {
                    loaded++;
                    return Task.FromResult<JArray?>(null);
                }
            );

            view.LoadPlayer("self");
            await WaitFor(() => !Status(view).Text.StartsWith("正在读取"));

            Assert.Equal(2, handler.Paths.Count);
            Assert.Equal(1, loaded);
            Assert.Equal("接口未返回可识别的统计记录，可以稍后重试。", Status(view).Text);
        });
    }

    [Fact]
    public void Click_Refresh_AsksForTheSamePlayerAgain()
    {
        UiHost.Run(async host =>
        {
            var handler = new CareerHandler(_ => Json(Records("self")));
            using var client = Client(handler);
            using var view = View(host, client);
            view.LoadPlayer("self");
            await WaitFor(() => Modes(view).Items.Count > 0);

            WindowMessages.Click(Refresh(view), new Point(4, 4));
            await WaitFor(() => handler.Paths.Count == 2);

            Assert.All(handler.Paths, path => Assert.Equal("/lol-career-stats/v1/summoner-games/self", path));
            Assert.Contains("生涯接口返回 1 条记录", Status(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_SecondPlayer_KeepsTheSecondResultAndDropsTheRetiredOne()
    {
        UiHost.Run(async host =>
        {
            var gate = new TaskCompletionSource();
            var handler = new CareerHandler(
                uri => Json(Records(uri.AbsolutePath.EndsWith("player-2") ? "player-2" : "player-1", games: 2)),
                index => index == 0 ? gate.Task : null
            );
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("player-1");
            view.LoadPlayer("player-2");
            await WaitFor(() => Summary(view).Text.Contains("2 场"));

            // The retired player's answer arrives afterwards; it must not replace the one on screen.
            gate.SetResult();
            Application.DoEvents();
            await Task.Yield();

            Assert.Equal(2, handler.Paths.Count);
            Assert.Contains("2 场", Summary(view).Text);
        });
    }

    [Fact]
    public void Dispose_DuringALoad_PublishesNothing()
    {
        UiHost.Run(async host =>
        {
            var gate = new TaskCompletionSource();
            var handler = new CareerHandler(_ => Json(Records("self")), index => index == 0 ? gate.Task : null);
            using var client = Client(handler);
            var view = View(host, client);
            var status = Status(view);
            var modes = Modes(view);

            view.LoadPlayer("self");
            view.Dispose();
            gate.SetResult();
            Application.DoEvents();
            await Task.Yield();

            Assert.Empty(status.Text);
            Assert.Empty(modes.Items);

            // The same answer does reach a view that is still open, so the empty line above is the drop.
            using var live = View(host, client);
            live.LoadPlayer("self");
            await WaitFor(() => Modes(live).Items.Count > 0);

            Assert.Contains("生涯接口返回 1 条记录", Status(live).Text);
        });
    }

    // The view lays its two lines out as top-docked labels: the 48-pixel one is the status, the 66-pixel one the summary.
    private static Label Status(CareerStatisticsView view) =>
        view.Controls.OfType<Label>().Single(label => label.Dock == DockStyle.Top && label.Height == 48);

    private static Label Summary(CareerStatisticsView view) =>
        view.Controls.OfType<Label>().Single(label => label.Dock == DockStyle.Top && label.Height == 66);

    private static ListView Modes(CareerStatisticsView view) => view.Controls.OfType<ListView>().Single();

    private static Button Refresh(CareerStatisticsView view) =>
        view.Controls.OfType<Panel>().Single().Controls.OfType<Button>().Single();

    private static string[] ModeRows(CareerStatisticsView view) =>
        [.. Modes(view).Items.Cast<ListViewItem>().Select(item => item.Text)];

    private static Task<JArray?> HistoryThatMustNotBeUsed(string puuid, CancellationToken token) =>
        throw new InvalidOperationException("历史记录不该被请求");

    private static bool IsCareer(Uri uri) => uri.AbsolutePath.Contains("career-stats");

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://stub.test/") };

    private static HttpResponseMessage Json(JToken body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };

    private static CareerStatisticsView View(
        Form host,
        HttpClient client,
        Func<string, CancellationToken, Task<JArray?>>? historyLoader = null
    )
    {
        var view = new CareerStatisticsView(new PlayerStatisticsService(() => client), historyLoader)
        {
            Dock = DockStyle.Fill,
        };
        host.Controls.Add(view);
        return view;
    }

    private static async Task WaitFor(Func<bool> ready)
    {
        for (int attempt = 0; attempt < 400 && !ready(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    /// <summary>One game per record, in one mode, with the numbers the career paragraphs are built from.</summary>
    private static JObject Records(string puuid, int games = 1) =>
        new()
        {
            ["games"] = new JArray(
                Enumerable
                    .Range(1, games)
                    .Select(id => new JObject
                    {
                        ["gameId"] = id,
                        ["gameCreation"] = id,
                        ["queueId"] = 450,
                        ["gameDuration"] = 1200,
                        ["participants"] = new JArray(
                            new JObject
                            {
                                ["puuid"] = puuid,
                                ["teamId"] = 100,
                                ["championId"] = 1,
                                ["stats"] = new JObject
                                {
                                    ["win"] = true,
                                    ["kills"] = 2,
                                    ["deaths"] = 1,
                                    ["assists"] = 3,
                                    ["totalDamageDealtToChampions"] = 10000,
                                    ["goldEarned"] = 9000,
                                    ["pentaKills"] = 0,
                                },
                            }
                        ),
                    })
            ),
        };

    /// <summary>Answers by URL, records every path asked for, and can hold the nth request until the test lets it go.</summary>
    private sealed class CareerHandler(Func<Uri, HttpResponseMessage> answer, Func<int, Task?>? hold = null)
        : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            int index = Paths.Count;
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (hold?.Invoke(index) is { } gate)
            {
                await gate;
            }

            return answer(request.RequestUri);
        }
    }
}
