using System.Net;
using System.Net.Http.Headers;
using League.Clients;
using League.Controls;
using League.Infrastructure;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Tests.Controls;

/// <summary>
/// The live match page: the poll that reads the in-game API, the participant cards it builds from the snapshot, the
/// icons it caches, the equipment panel, the manual augment records and the toolbar. Every dependency is injected —
/// the in-game client answers from a stub handler that never opens a socket, so the result is the same whether or not
/// a game is running on this machine; op.gg and arammeta answer from the stub clients the constructor accepts; and the
/// icons come from a lambda. The expected values come from the checks in Tests/StatisticsChecks/LiveMatchChecks.cs,
/// which this class replaces.
/// </summary>
[Collection(EnvironmentCollection.Name)]
[Trait("Category", "UI")]
public class LiveMatchViewTests : IDisposable
{
    private readonly ResourceLoading _previous = Globals.resLoading;

    public LiveMatchViewTests() => Globals.resLoading = Champions();

    public void Dispose() => Globals.resLoading = _previous;

    [Fact]
    public void LiveMatchClient_LiveApi_IsLoopbackReadOnly()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live);

            await screen.Rendered(() => screen.Cards.Count == 10);

            Assert.Equal(["GET https://127.0.0.1:2999/liveclientdata/allgamedata"], live.Requests);
        });
    }

    [Fact]
    public void Window_Shown_LoadsParticipantCards()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live);

            await screen.Rendered(() => screen.Cards.Count == 10);

            Assert.Equal(10, screen.Cards.Count);
        });
    }

    [Fact]
    public void Window_Shown_ShowsTheReconstructedDeathStreakOnTheCardAndTheDetails()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(Streak()));
            using var screen = CreateScreen(host, live);

            await screen.Rendered(() => screen.LabelsWith("连死 2 · 最高 2").Count > 0);

            // Once on the participant card, once in the selected hero's details.
            Assert.Equal(2, screen.LabelsWith("连死 2 · 最高 2").Count);
        });
    }

    [Theory]
    [InlineData("empty", "暂无装备")]
    [InlineData("unavailable", "装备暂不可用")]
    public void Window_EquipmentThatIsEmptyAndUnavailable_ReadsDifferently(string state, string expected)
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(Items(LiveMatchFixture.AramGame(125, 900, 1400), state)));
            using var screen = CreateScreen(host, live);

            await screen.Rendered(() => screen.LabelsWith(expected).Count == 10);

            Assert.Equal(10, screen.LabelsWith(expected).Count);
        });
    }

    [Fact]
    public void Window_APollWithTheSameSnapshot_RetainsTheCardControls()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live);
            await screen.Rendered(() => screen.Cards.Count == 10);
            var retained = screen.Cards;

            Click(screen.Refresh);
            await screen.Rendered(() => live.Polls >= 2 && screen.Cards.Count == 10);

            Assert.Equal(retained, screen.Cards);
        });
    }

    [Fact]
    public void Window_DuplicateChampions_LoadEachIconOnce()
    {
        UiHost.Run(async host =>
        {
            var icons = new FakeIcons();
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live, icons);
            await screen.Rendered(() => screen.Cards.Count == 10);

            Click(screen.Refresh);
            await screen.Rendered(() => live.Polls >= 2 && screen.Cards.Count == 10);

            // Ten Lux cards and the selected hero's portrait all read the champion once: the icon is cached by id.
            Assert.Equal([1], icons.Counts.Values.Distinct());
        });
    }

    [Fact]
    public void Window_ResizedLayout_KeepsTheIcons()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live);
            // Wait for the images themselves, not just for the icon controls: before the artwork arrives every
            // control is empty, so a snapshot taken then would compare zero against eleven.
            await screen.Rendered(() => screen.IconsWithImage() == 11);
            int before = screen.IconsWithImage();

            host.ClientSize = new Size(380, 700);
            await screen.Rendered(() => live.Polls >= 2 && screen.Cards.Count == 10);
            using var painted = new Bitmap(screen.View.Width, screen.View.Height);
            screen.View.DrawToBitmap(painted, screen.View.ClientRectangle);

            Assert.Equal(before, screen.IconsWithImage());
        });
    }

    [Fact]
    public void Window_Disconnected_ClearsPreviousPlayerCards()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live);
            await screen.Rendered(() => screen.Cards.Count == 10);
            live.Fail = true;

            Click(screen.Refresh);
            await screen.Rendered(() => live.Polls >= 2 && screen.Cards.Count == 0);

            Assert.Empty(screen.Cards);
        });
    }

    [Fact]
    public void Window_TheMayhemStatistics_FillTheManualAugmentPicker()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live, aramMeta: new FakeAramMeta());

            await screen.Rendered(() => screen.PickerReady && screen.Cards.Count == 10);

            Assert.Contains("幻影武器", screen.PickerItems);
        });
    }

    [Fact]
    public void Window_RecordingWithoutASelectedPlayer_DoesNothing()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400) with { Players = [] }));
            using var screen = CreateScreen(host, live, aramMeta: new FakeAramMeta());
            await screen.Rendered(() => screen.PickerReady);
            screen.Picker.SelectedIndex = screen.AugmentIndex("幻影武器");

            Click(screen.ButtonWith("记录已选"));

            Assert.Equal(0, screen.AugmentIconTotal);
        });
    }

    // Behaviours still to cover, every one of them blocked on the public surface rather than on the page itself: the
    // guide button (its callback is pinned by Tests/StatisticsChecks, but the mode it reads cannot be settled from
    // outside without racing the poll), switching the statistics mode, reading the statistics index once, a stale
    // rank failure, the augment records surviving and dropping with the roster, a late queue result, the owned
    // recommendation items, and clicking a recommended augment. See docs/testing-gaps-notes.md.

    [Fact]
    public void Window_LateIconCopies_AreDisposedAndNeverPublished()
    {
        UiHost.Run(async host =>
        {
            var arrived = new TaskCompletionSource<(Image?, string, string)>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var icons = new FakeIcons { Gate = arrived.Task };
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var client = new LiveMatchClient(live);
            var screen = CreateScreen(host, live, icons, client: client);
            await Wait(() => icons.Counts.Count > 0);
            screen.View.Dispose();

            // The shared icon copy cannot be cancelled, so cleanup waits for the callback it already handed out.
            // While it waits the view still holds the client it was given: asking it for the game must work.
            LiveMatchSnapshot duringCleanup = await client.GetAsync(CancellationToken.None);
            arrived.SetResult((new Bitmap(4, 4), "late", "late"));
            await Wait(() => screen.IconsWithImage() == 0);
            LiveMatchSnapshot afterCleanup = await client.GetAsync(CancellationToken.None);

            Assert.Equal(10, duringCleanup.Players.Length);
            Assert.Equal(10, afterCleanup.Players.Length);
        });
    }

    [Fact]
    public void Window_AnInjectedLiveClient_RemainsAvailableToItsCreatorAfterClose()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var client = new LiveMatchClient(live);
            var screen = CreateScreen(host, live, client: client);
            await screen.Rendered(() => screen.Cards.Count == 10);

            screen.View.Dispose();
            LiveMatchSnapshot snapshot = await client.GetAsync(CancellationToken.None);

            Assert.Equal(10, snapshot.Players.Length);
        });
    }

    [Fact]
    public void Window_ABuy_RetainsEveryOtherPlayerCard()
    {
        UiHost.Run(async host =>
        {
            var live = new FakeLive(Snapshot(LiveMatchFixture.AramGame(125, 900, 1400)));
            using var screen = CreateScreen(host, live);
            await screen.Rendered(() => screen.Cards.Count == 10);
            var before = screen.Cards.ToDictionary(card => (string)card.Tag!);

            live.Next = Snapshot(Bought());
            Click(screen.Refresh);
            await screen.Rendered(() => live.Polls >= 2 && screen.Cards.Count == 10);
            var after = screen.Cards.ToDictionary(card => (string)card.Tag!);

            Assert.Equal(10, after.Count);
            Assert.All(before.Where(pair => pair.Key != "我|0"), pair => Assert.Same(pair.Value, after[pair.Key]));
        });
    }

    // Behaviours still to cover, every one of them blocked on the public surface rather than on the page itself:
    // the guide button in a mode without a guide, switching the statistics mode, reading the statistics index once,
    // a stale rank failure, the augment records dropping with the roster, a late queue result, the owned
    // recommendation items and clicking a recommended augment. See docs/testing-gaps-notes.md.

    private static LiveMatchSnapshot Streak()
    {
        var game = LiveMatchFixture.AramGame(125, 900, 1400);
        return game with
        {
            Players = [game.Players[0] with { Kills = 0, Deaths = 2 }, .. game.Players.Skip(1)],
            Events =
            [
                new LiveMatchEvent(0, "GameStart", 0, "", ""),
                LiveMatchFixture.Kill(1, 10, "敌方1", LiveMatchFixture.SelfName),
                LiveMatchFixture.Kill(2, 20, "敌方2", LiveMatchFixture.SelfName),
            ],
        };
    }

    /// <summary>The same roster with the own player's first finished item bought.</summary>
    private static LiveMatchSnapshot Bought()
    {
        var game = LiveMatchFixture.AramGame(125, 900, 1400);
        return game with
        {
            Players =
            [
                .. game.Players.Select(player =>
                    player.Self ? player with { Items = [new LiveMatchItem(3089, "灭世者的死亡之帽", 1)] } : player
                ),
            ],
        };
    }

    private static JObject Snapshot(LiveMatchSnapshot snapshot) => LiveMatchFixture.ToJson(snapshot);

    // Every card carries its own item row, so the state has to be applied to every player: changing only the own
    // player would leave nine cards showing their ordinary items and the ten-label wait would never settle.
    private static LiveMatchSnapshot Items(LiveMatchSnapshot snapshot, string state) =>
        snapshot with
        {
            Players = [.. snapshot.Players.Select(player => player with { Items = state == "empty" ? [] : null })],
        };

    private static ResourceLoading Champions()
    {
        var loading = new ResourceLoading();
        loading.Champions.Champions.AddRange([
            new Champion
            {
                Id = 99,
                Alias = "Lux",
                Name = "拉克丝",
                Description = "",
            },
            new Champion
            {
                Id = 103,
                Alias = "Ahri",
                Name = "阿狸",
                Description = "",
            },
            new Champion
            {
                Id = 157,
                Alias = "Yasuo",
                Name = "疾风剑豪",
                Description = "",
            },
        ]);
        return loading;
    }

    private static Screen CreateScreen(
        Form host,
        FakeLive live,
        FakeIcons? icons = null,
        Func<TimeSpan>? lifetime = null,
        Func<Task<int>>? queue = null,
        Action<int, int>? guide = null,
        LiveMatchClient? client = null,
        FakeOpgg? opgg = null,
        FakeAramMeta? aramMeta = null
    ) =>
        new(
            host,
            live,
            icons ?? new FakeIcons(),
            lifetime ?? (() => TimeSpan.FromDays(1)),
            queue ?? (() => Task.FromResult(2400)),
            guide ?? ((_, _) => { }),
            client,
            opgg,
            aramMeta
        );

    private static void Click(Control control) => WindowMessages.Click(control, new Point(6, 6));

    /// <summary>One view on the host window, with every dependency stubbed.</summary>
    private sealed class Screen : IDisposable
    {
        private readonly OpggClient _opgg;
        private readonly AramMetaClient _aramMeta;

        public Screen(
            Form host,
            FakeLive live,
            FakeIcons icons,
            Func<TimeSpan> lifetime,
            Func<Task<int>> queue,
            Action<int, int> guide,
            LiveMatchClient? client,
            FakeOpgg? opgg,
            FakeAramMeta? aramMeta
        )
        {
            _opgg = new OpggClient(opgg ?? new FakeOpgg());
            _aramMeta = new AramMetaClient(aramMeta ?? new FakeAramMeta(), cacheLifetime: lifetime);
            View = new LiveMatchView(
                queue,
                guide,
                lifetime,
                client ?? new LiveMatchClient(live),
                icons.Load,
                _opgg,
                _aramMeta
            )
            {
                Dock = DockStyle.Fill,
            };
            host.Controls.Add(View);
        }

        public LiveMatchView View { get; }

        public void Dispose()
        {
            View.Dispose();
            _opgg.Dispose();
            _aramMeta.Dispose();
        }

        public List<Panel> Cards => Descendants(View).OfType<Panel>().Where(panel => panel.Tag is string).ToList();

        public List<Button> IconsOf(int id) =>
            Descendants(View)
                .OfType<Button>()
                .Where(button => button.Tag is ValueTuple<string, int> tag && tag.Item2 == id)
                .ToList();

        public List<Label> LabelsWith(string text) =>
            Descendants(View).OfType<Label>().Where(label => label.Text == text).ToList();

        public int IconsWithImage() =>
            Descendants(View).OfType<Button>().Count(button => button.BackgroundImage != null);

        public bool PickerReady => Picker.Items.Count > 0;

        /// <summary>The page's status line: the separator line that is not inside a participant card.</summary>
        public string TierStatus =>
            Descendants(View)
                .OfType<Label>()
                .Where(label => label.Text.Contains(" · ", StringComparison.Ordinal) && !InsideACard(label))
                .Select(label => label.Text)
                .FirstOrDefault()
            ?? "";

        public List<string> PickerItems => Picker.Items.Cast<object>().Select(item => item.ToString() ?? "").ToList();

        /// <summary>The picker index of one recorded augment; the statistics decide the order.</summary>
        public int AugmentIndex(string name) => PickerItems.IndexOf(name);

        public int AugmentIconTotal =>
            Descendants(View)
                .OfType<Button>()
                .Count(button => button.Tag is ValueTuple<string, int> tag && tag.Item1 == "augment");

        /// <summary>The manual augment picker: the one list box that is not the three-mode box.</summary>
        public ComboBox Picker => Descendants(View).OfType<ComboBox>().Last(box => box.Items.Count != 3);

        public Control Refresh => ButtonWith("刷新对局");

        public Control ButtonWith(string text) =>
            Descendants(View).OfType<Control>().Single(control => control.Text == text);

        /// <summary>Waits for a render that already happened to satisfy <paramref name="ready"/>.</summary>
        public Task Rendered(Func<bool> ready) => Wait(ready);

        private static IEnumerable<Control> Descendants(Control parent) =>
            parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

        private static bool InsideACard(Control control) =>
            control.Parent is { } parent && (parent.Tag is string || InsideACard(parent));
    }

    /// <summary>Waits for a positive signal, pumping the message loop the way the application does.</summary>
    private static async Task Wait(Func<bool> ready)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(ready(), "The view finished the operation within ten seconds.");
    }

    /// <summary>The in-game API on its stub: one document, loopback only, no socket.</summary>
    private sealed class FakeLive(JObject snapshot) : HttpMessageHandler
    {
        public bool Fail { get; set; }

        /// <summary>The next document to serve, for a test that changes the game between polls.</summary>
        public JObject? Next { get; set; }

        public int Polls { get; private set; }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Polls++;
            Requests.Add($"{request.Method} {request.RequestUri}");
            if (Next is { } next)
            {
                snapshot = next;
                Next = null;
            }

            return Task.FromResult(
                Fail
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(snapshot.ToString()) }
            );
        }
    }

    /// <summary>op.gg on its stub: tiers or one aram build, never a socket.</summary>
    private sealed class FakeOpgg : HttpMessageHandler
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(
                request.RequestUri!.AbsolutePath.Contains("/champions/aram/", StringComparison.Ordinal)
                    ? Json(
                        """
                        {"meta":{"version":"test"},"data":{"summary":{"id":99,"average_stats":{}},"core_items":[{"ids":[3089,3020],"win":53,"play":100,"pick_rate":0.16}]}}
                        """
                    )
                    : Json(
                        """
                        {"meta":{"version":"tiers-test"},"data":[{"id":99,"average_stats":{"win_rate":0.53,"pick_rate":0.16,"tier_data":{"tier":1,"rank":3}}}]}
                        """
                    )
            );
        }

        private static HttpResponseMessage Json(string text) =>
            new(HttpStatusCode.OK) { Content = new StringContent(text) };
    }

    /// <summary>arammeta on its stub: the captured index, validated by its entity tag.</summary>
    private sealed class FakeAramMeta : HttpMessageHandler
    {
        // The entity tag has to be unique to this class. The arammeta cache lives on disk under the shared test data
        // root, so a tag that other classes also use would let their cached document answer this class's conditional
        // request with 304, and the index would then never arrive.
        private static readonly string Tag = "\"" + AramMetaFixture.Version + "-livematch\"";
        private readonly JObject _index = AramMetaFixture.Index();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Headers.IfNoneMatch.Any(value => value.Tag == Tag))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_index.ToString()),
            };
            response.Headers.ETag = new EntityTagHeaderValue(Tag);
            return Task.FromResult(response);
        }
    }

    /// <summary>Icons from a lambda: one square per asset, counted by kind and id.</summary>
    private sealed class FakeIcons
    {
        /// <summary>Holds every answer until the test releases it.</summary>
        public Task<(Image?, string, string)>? Gate { get; set; }

        public Dictionary<(string Kind, int Id), int> Counts { get; } = [];

        public async Task<(Image? Image, string Name, string Description)> Load(string kind, int id)
        {
            Counts[(kind, id)] = Counts.GetValueOrDefault((kind, id)) + 1;
            if (Gate is { } gate)
            {
                return await gate;
            }

            using var bitmap = new Bitmap(48, 48);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(kind == "augment" ? Color.Purple : Color.Teal);
            var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            return (Image.FromStream(stream), "测试图标", "悬浮说明");
        }
    }
}
