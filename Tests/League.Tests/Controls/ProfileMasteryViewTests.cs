using System.Net;
using System.Text;
using League.Clients;
using League.Controls;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The client's champion achievements: the five top champions as posters, the ownership heading, the "all" button,
/// and the status line while they load. The statistics service talks to stub responses keyed by URL, and the
/// champion pictures come from an injected loader, so nothing is requested over the network.
/// </summary>
[Trait("Category", "UI")]
public class ProfileMasteryViewTests
{
    [Fact]
    public void OwnershipText_BeforeAnyPlayer_ShowsThePlaceholder()
    {
        UiHost.Run(host =>
        {
            var handler = new MasteryHandler(Routes());
            using var client = Client(handler);
            using var view = View(host, client);

            Assert.Equal("— / —", view.OwnershipText);
            Assert.Equal("英雄成就", Heading(view).Text);
            Assert.Equal("连接客户端后加载英雄成就。", Status(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_MasteryRecords_ShowsTheFiveTopChampionsAndTheOwnership()
    {
        UiHost.Run(async host =>
        {
            JObject? announced = null;
            var handler = new MasteryHandler(
                Routes(
                    profile: () => Profile(7),
                    mastery: () =>
                        Json(
                            Mastery(
                                MasteryRecord(1, 6000),
                                MasteryRecord(2, 5000),
                                MasteryRecord(3, 4000),
                                MasteryRecord(4, 3000),
                                MasteryRecord(5, 2000),
                                MasteryRecord(6, 1000),
                                MasteryRecord(7, 0),
                                MasteryRecord(-1, 9000)
                            )
                        ),
                    catalog: () => Json(Catalog(1, 2, 3)),
                    inventory: () => Json(Inventory((1, true), (2, false), (3, true)))
                )
            );
            using var client = Client(handler);
            using var view = View(host, client);
            view.ProfileLoaded += summoner => announced = summoner;

            view.LoadPlayer("self");
            await WaitFor(() => CardCount(view) == 5 && view.OwnershipText.StartsWith("拥有"));

            Assert.Equal("拥有 2 / 3", view.OwnershipText);
            Assert.Equal("英雄成就  ·  拥有 2 / 3", Heading(view).Text);
            Assert.Equal($"英雄1\n1 级英雄成就\n{6000L:N0} 成就点数", CardTexts(view)[0]);
            Assert.True(All(view).Enabled, "Seven records are offered to the full list.");
            Assert.Empty(Status(view).Text);
            Assert.Equal(7, (int?)announced?["summonerId"]);
        });
    }

    [Fact]
    public void Click_All_AnnouncesEveryRecordItHas()
    {
        UiHost.Run(async host =>
        {
            var handler = new MasteryHandler(
                Routes(mastery: () =>
                    Json(Mastery(MasteryRecord(1, 6000), MasteryRecord(2, 1000), MasteryRecord(3, 0)))
                )
            );
            using var client = Client(handler);
            using var view = View(host, client);
            JObject[]? announced = null;
            view.AllRequested += records => announced = records;
            view.LoadPlayer("self");
            await WaitFor(() => All(view).Enabled);

            WindowMessages.Click(All(view), new Point(4, 4));

            Assert.Equal(3, announced?.Length);
        });
    }

    [Fact]
    public void Click_Refresh_AsksForTheAchievementsAgain()
    {
        UiHost.Run(async host =>
        {
            var handler = new MasteryHandler(Routes(mastery: () => Json(Mastery(MasteryRecord(1, 6000)))));
            using var client = Client(handler);
            using var view = View(host, client);
            view.LoadPlayer("self");
            await WaitFor(() => CardCount(view) == 1);

            WindowMessages.Click(Refresh(view), new Point(4, 4));
            await WaitFor(() => handler.Count("champion-mastery") == 2 && CardCount(view) == 1);

            Assert.Equal(2, handler.Count("champion-mastery"));
            Assert.Equal(1, CardCount(view));
            Assert.Equal("英雄成就  ·  成就英雄 1 / —", Heading(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_EmptyMastery_SaysThereAreNoRecords()
    {
        UiHost.Run(async host =>
        {
            var handler = new MasteryHandler(Routes(mastery: () => Json(new JArray())));
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("self");
            await WaitFor(() => !Status(view).Text.StartsWith("正在加载"));

            Assert.Equal("暂无英雄成就记录。", Status(view).Text);
            Assert.Equal("成就英雄 0 / —", view.OwnershipText);
            Assert.False(All(view).Enabled);
            Assert.Equal(0, CardCount(view));
        });
    }

    [Fact]
    public void LoadPlayer_MasteryRejected_ReportsWhatTheClientSaid()
    {
        UiHost.Run(async host =>
        {
            var handler = new MasteryHandler(Routes(mastery: () => Json(new JObject(), HttpStatusCode.Forbidden)));
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("self");
            await WaitFor(() => !Status(view).Text.StartsWith("正在加载"));

            Assert.Equal("当前玩家资料不允许访问。", Status(view).Text);
            Assert.Equal("英雄总数 —", view.OwnershipText);
        });
    }

    [Fact]
    public void LoadPlayer_MasteryWithoutRecords_SaysItCannotBeFetched()
    {
        UiHost.Run(async host =>
        {
            var handler = new MasteryHandler(Routes(mastery: () => Json(new JObject())));
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("self");
            await WaitFor(() => !Status(view).Text.StartsWith("正在加载"));

            Assert.Equal("暂时无法获取英雄成就。", Status(view).Text);
        });
    }

    [Fact]
    public void LoadPlayer_SecondPlayer_KeepsTheSecondPlayersCardsAndDropsTheRetiredOnes()
    {
        UiHost.Run(async host =>
        {
            var gate = new TaskCompletionSource();
            var handler = new MasteryHandler(
                uri =>
                    uri.AbsolutePath.Contains("player-1")
                        ? Json(Mastery(MasteryRecord(1, 1111)))
                        : Json(Mastery(MasteryRecord(2, 2222))),
                index => index == 0 ? gate.Task : null
            );
            using var client = Client(handler);
            using var view = View(host, client);

            view.LoadPlayer("player-1");
            view.LoadPlayer("player-2");
            await WaitFor(() => CardCount(view) == 1);

            gate.SetResult();
            Application.DoEvents();
            await Task.Yield();

            Assert.Equal(1, CardCount(view));
            Assert.Contains($"{2222L:N0} 成就点数", CardTexts(view)[0]);
        });
    }

    [Fact]
    public void Dispose_BeforeLoading_StopsTheViewFromAskingForAnything()
    {
        UiHost.Run(host =>
        {
            var handler = new MasteryHandler(Routes());
            using var client = Client(handler);
            var view = View(host, client);

            view.Dispose();
            view.LoadPlayer("self");

            Assert.Empty(handler.Paths);
        });
    }

    [Fact]
    public void SelectAll_RecordsWithPoints_DropsThePlaceholderAndKeepsTheHighestPerChampion()
    {
        var data = new JArray(
            MasteryRecord(1, 100),
            MasteryRecord(1, 300),
            MasteryRecord(-1, 900),
            MasteryRecord(2, -1),
            new JObject { ["championId"] = 3, ["championPoints"] = "many" },
            MasteryRecord(4, 0)
        );

        var records = ProfileMasteryView.SelectAll(data);

        Assert.Equal([1, 4], records.Select(record => (int)record["championId"]!));
        Assert.Equal(300, (int)records[0]["championPoints"]!);
    }

    [Fact]
    public void SelectTop_RecordsWithPoints_KeepsTheFiveHighest()
    {
        var data = new JArray(
            MasteryRecord(1, 10),
            MasteryRecord(2, 50),
            MasteryRecord(3, 20),
            MasteryRecord(4, 40),
            MasteryRecord(5, 30),
            MasteryRecord(6, 0),
            MasteryRecord(7, 25)
        );

        var top = ProfileMasteryView.SelectTop(data);

        Assert.Equal([2, 4, 5, 7, 3], top.Select(record => (int)record["championId"]!));
    }

    [Fact]
    public void StandardChampions_Catalog_DropsThePlaceholderAndTheVariantEntries()
    {
        var data = new JArray(
            new JObject { ["id"] = -1 },
            new JObject { ["id"] = 1 },
            new JObject { ["id"] = 1 },
            new JObject { ["id"] = 60001 },
            new JObject { ["id"] = "2" }
        );

        var champions = ProfileMasteryView.StandardChampions(data);

        Assert.Equal([1], champions.Select(champion => (int)champion["id"]!));
    }

    private static Label Status(ProfileMasteryView view) =>
        view.Controls.OfType<Label>().Single(label => label.Dock == DockStyle.Top && label.Height == 26);

    // The header is the panel that carries the two buttons; the card table is its own control type.
    private static Panel Header(ProfileMasteryView view) =>
        view.Controls.OfType<Panel>().Single(panel => panel.Controls.OfType<Button>().Any());

    private static Label Heading(ProfileMasteryView view) => Header(view).Controls.OfType<Label>().Single();

    private static Button Refresh(ProfileMasteryView view) => HeaderButton(view, "刷新");

    private static Button All(ProfileMasteryView view) => HeaderButton(view, "全部 ›");

    private static Button HeaderButton(ProfileMasteryView view, string text) =>
        Header(view).Controls.OfType<Button>().Single(button => button.Text == text);

    private static TableLayoutPanel Cards(ProfileMasteryView view) => view.Controls.OfType<TableLayoutPanel>().Single();

    private static int CardCount(ProfileMasteryView view) => Cards(view).Controls.OfType<Panel>().Count();

    private static string[] CardTexts(ProfileMasteryView view) =>
        [
            .. Cards(view)
                .Controls.OfType<Panel>()
                .Select(card => card.Controls.OfType<PictureBox>().Single().Controls.OfType<Label>().Single().Text),
        ];

    private static JObject MasteryRecord(int championId, long points) =>
        new()
        {
            ["championId"] = championId,
            ["championPoints"] = points,
            ["championLevel"] = 1,
        };

    private static JArray Mastery(params JObject[] records) => new(records.Cast<object>().ToArray());

    private static JArray Catalog(params int[] ids) =>
        new(ids.Select(id => (object)new JObject { ["id"] = id }).ToArray());

    private static JArray Inventory(params (int Id, bool Owned)[] champions) =>
        new(
            champions
                .Select(champion =>
                    (object)
                        new JObject
                        {
                            ["id"] = champion.Id,
                            ["ownership"] = new JObject { ["owned"] = champion.Owned },
                        }
                )
                .ToArray()
        );

    // The four endpoints the view reads, each answered by URL with a fresh response; anything else is a 404.
    private static Func<Uri, HttpResponseMessage> Routes(
        Func<HttpResponseMessage>? profile = null,
        Func<HttpResponseMessage>? mastery = null,
        Func<HttpResponseMessage>? catalog = null,
        Func<HttpResponseMessage>? inventory = null
    ) =>
        uri =>
            uri.AbsolutePath.Contains("summoners-by-puuid") ? (profile ?? Missing)()
            : uri.AbsolutePath.Contains("champion-mastery") ? (mastery ?? Missing)()
            : uri.AbsolutePath.Contains("champion-summary") ? (catalog ?? Missing)()
            : uri.AbsolutePath.Contains("champions-minimal") ? (inventory ?? Missing)()
            : Missing();

    private static HttpResponseMessage Missing() => Json(new JObject(), HttpStatusCode.NotFound);

    private static HttpResponseMessage Profile(int summonerId) => Json(new JObject { ["summonerId"] = summonerId });

    private static HttpResponseMessage Json(JToken body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://stub.test/") };

    private static ProfileMasteryView View(Form host, HttpClient client)
    {
        var view = new ProfileMasteryView(
            new PlayerStatisticsService(() => client),
            championId => Task.FromResult<(Image?, string, string)>((null, $"英雄{championId}", "说明"))
        )
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

    /// <summary>Answers by URL, records every path, and can hold the nth request until the test lets it go.</summary>
    private sealed class MasteryHandler(Func<Uri, HttpResponseMessage> answer, Func<int, Task?>? hold = null)
        : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        public int Count(string part) => Paths.Count(path => path.Contains(part));

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
