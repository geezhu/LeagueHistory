using System.Net;
using System.Reflection;
using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class LiveMatchChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Sample = """
        {"activePlayer":{"riotId":"Self#CN"},"gameData":{"gameMode":"ARAM","mapNumber":12,"gameTime":125},"allPlayers":[
        {"riotId":"Enemy#CN","rawChampionName":"game_character_displayname_Lux","championName":"拉克丝","team":"ORDER","level":8,"scores":{"kills":3,"deaths":1},"items":[{"itemID":3089,"displayName":"灭世者的死亡之帽","count":1}]},
        {"riotId":"Self#CN","rawChampionName":"game_character_displayname_Morgana","championName":"莫甘娜","team":"CHAOS","level":9,"scores":{"kills":1,"deaths":3},"items":[],"runes":{"keystone":{"id":8112}}},
        {"riotId":"Ally#CN","rawChampionName":"game_character_displayname_Lux","championName":"拉克丝","team":"CHAOS","level":8,"scores":{"kills":0,"deaths":0}}
        ],"events":{"Events":[{"EventID":0,"EventName":"GameStart","EventTime":0},
        {"EventID":1,"EventName":"ChampionKill","EventTime":10,"KillerName":"Enemy#CN","VictimName":"Self#CN"},
        {"EventID":2,"EventName":"ChampionKill","EventTime":20,"KillerName":"Enemy#CN","VictimName":"Self#CN"},
        {"EventID":3,"EventName":"ChampionKill","EventTime":30,"KillerName":"Self#CN","VictimName":"Enemy#CN"},
        {"EventID":4,"EventName":"ChampionKill","EventTime":40,"KillerName":"Enemy#CN","VictimName":"Self#CN"}]}}
        """;

    public static void Run()
    {
        SnapshotIdentity();
        WinFormsCheckHost.Run(() =>
        {
            using var champions = new ChampionFixtures();
            CardsAndRecommendations();
            LateAssetsAndBorrowedClients();
            ClosedPollingAndStaleRanks();
            CancelledRecommendations();
            InteractivePaths();
        });
        Console.WriteLine(
            "PASS: live match identity, retained cards, cached icons, equipment, responsive layout, cancellation and owned cleanup."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Field<T>(LiveMatchView view, string name) =>
        (T)typeof(LiveMatchView).GetField(name, PrivateInstance)!.GetValue(view)!;

    private static void SetField(LiveMatchView view, string name, object? value) =>
        typeof(LiveMatchView).GetField(name, PrivateInstance)!.SetValue(view, value);

    private static object? Invoke(LiveMatchView view, string name, params object[] args) =>
        typeof(LiveMatchView).GetMethod(name, PrivateInstance)!.Invoke(view, args);

    private static IEnumerable<Control> Children(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child)));

    private static Panel[] Cards(LiveMatchView view) =>
        Children(view).OfType<Panel>().Where(card => card.Tag is string).ToArray();

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(ready(), "The simulated operation completed before its deadline.");
    }

    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Handler : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Check(
                request.Method == HttpMethod.Get
                    && request.RequestUri!.ToString() == "https://127.0.0.1:2999/liveclientdata/allgamedata",
                "Live API is loopback read-only."
            );
            return Task.FromResult(
                Fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response(Sample)
            );
        }
    }

    private sealed class Recommendations : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Read { get; set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Check(
                request.Method == HttpMethod.Get && request.RequestUri!.Host == "lol-api-champion.op.gg",
                "Recommendation checks remain in the simulated handler."
            );
            if (Read != null)
            {
                return Read(request, token);
            }
            int champion = request
                .RequestUri!.Segments.Select(segment => int.TryParse(segment.Trim('/'), out int id) ? id : 0)
                .FirstOrDefault(id => id > 0);
            var data = JObject.Parse(
                """
                {"meta":{"version":"test"},"data":{"summary":{"id":25,"average_stats":{}},"core_items":[{"ids":[3089,3020],"win":53,"play":100,"pick_rate":0.16}]}}
                """
            );
            data["data"]!["summary"]!["id"] = champion;
            return Task.FromResult(Response(data.ToString()));
        }
    }

    private static HttpResponseMessage Response(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private sealed class ChampionFixtures : IDisposable
    {
        private readonly Champion[] _champions =
        [
            new()
            {
                Id = 99,
                Alias = "Lux",
                Name = "拉克丝",
                Description = "",
            },
            new()
            {
                Id = 25,
                Alias = "Morgana",
                Name = "莫甘娜",
                Description = "",
            },
            new()
            {
                Id = 147,
                Alias = "Seraphine",
                Name = "星籁歌姬",
                Description = "",
            },
        ];

        public ChampionFixtures()
        {
            League.FormMain.Globals.resLoading.Champions.Champions.AddRange(_champions);
        }

        public void Dispose()
        {
            foreach (var champion in _champions)
            {
                League.FormMain.Globals.resLoading.Champions.Champions.Remove(champion);
            }
        }
    }

    private static LiveMatchView CreateView(
        LiveMatchClient client,
        Func<string, int, Task<(Image?, string, string)>> icons,
        OpggClient opgg,
        Func<Task<int>>? queue = null
    )
    {
        var view = new LiveMatchView(
            queue ?? (() => Task.FromResult(0)),
            (_, _) => throw new Exception("Guide must not open automatically."),
            () => TimeSpan.FromDays(1),
            client,
            icons,
            opgg
        );
        SetField(
            view,
            "_artworkLoader",
            (Func<int, CancellationToken, Task<Image?>>)((_, _) => Task.FromResult<Image?>(null))
        );
        return view;
    }

    private static void SnapshotIdentity()
    {
        var snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Sample));
        Check(
            snapshot.TeamLabel(snapshot.Players[0]) == "对方" && snapshot.TeamLabel(snapshot.Players[1]) == "我方",
            "Team determined by self, not ORDER."
        );
        Check(
            snapshot.Players[0].ChampionAlias == "Lux" && snapshot.Players[0].Items![0].Id == 3089,
            "Raw champion alias and current item IDs."
        );
        Check(
            snapshot.Players[1].Items is { Length: 0 } && snapshot.Players[2].Items == null,
            "Empty equipment differs from unavailable."
        );
        Check(
            snapshot.Players.Length == 3 && snapshot.Players[0].Key != snapshot.Players[2].Key,
            "Duplicate champions remain independent players."
        );
        Check(
            snapshot.DeathStreaks[snapshot.Players[1].Key] is { Current: 1, Highest: 2, Complete: true },
            "Current and highest death streak reconstruct from a midgame snapshot."
        );
        var unknown = JObject.Parse(Sample);
        unknown.Remove("activePlayer");
        var neutral = LiveMatchSnapshot.Parse(unknown);
        Check(
            neutral.Players.All(player => !player.Self) && neutral.TeamLabel(neutral.Players[0]) == "ORDER",
            "Unknown self must not assign friend/enemy."
        );
        try
        {
            LiveMatchSnapshot.Parse(new JObject());
            throw new Exception("Malformed snapshot accepted.");
        }
        catch (InvalidDataException)
        {
            // Missing game metadata is the expected parse failure.
        }
    }

    private static void CardsAndRecommendations()
    {
        var handler = new Handler();
        using var client = new LiveMatchClient(handler);
        using var opgg = new OpggClient(new Recommendations());
        using var form = new Form { ClientSize = new Size(1100, 740) };
        var calls = new Dictionary<(string, int), int>();
        using var view = CreateView(
            client,
            (kind, id) =>
            {
                calls[(kind, id)] = calls.GetValueOrDefault((kind, id)) + 1;
                var image = new Bitmap(48, 48);
                using var graphics = Graphics.FromImage(image);
                graphics.Clear(kind == "augment" ? Color.Purple : Color.Teal);
                graphics.DrawEllipse(Pens.Gold, 5, 5, 36, 36);
                return Task.FromResult<(Image?, string, string)>((image, "测试图标", "悬浮说明"));
            },
            opgg
        );
        form.Controls.Add(view);
        form.Show();
        PumpUntil(() =>
            Cards(view).Length == 3 && Children(view).OfType<Button>().Any(button => button.BackgroundImage != null)
        );
        Check(handler.Calls >= 1, "Live page loads participant cards.");
        Check(
            Children(view).OfType<Label>().Any(label => label.Text == "连死 1 · 最高 2"),
            "Live page and selected hero details show the reconstructed death streak."
        );
        PumpUntil(() =>
            Children(view)
                .OfType<Button>()
                .Any(button => button.AccessibleName?.Contains("死亡之帽") == true && button.BackgroundImage != null)
        );
        SetField(view, "_currentQueue", 450);
        Complete((Task)Invoke(view, "LoadRecommendationsAsync")!);
        SetField(view, "_recommendationMode", 450);
        Invoke(view, "Render");
        var retained = Cards(view)[0];
        Invoke(view, "Render");
        Check(!retained.IsDisposed, "Unchanged poll retains card controls to prevent flashing.");
        RenderRecommendations(view);
        EquipmentOwnership(view);
        Capture(form, "live-match.png");
        form.ClientSize = new Size(650, 700);
        Application.DoEvents();
        Capture(form, "live-match-narrow.png");
        Check(
            Children(view).OfType<Button>().Any(button => button.BackgroundImage != null),
            "Icons survive responsive layout."
        );
        AlternateChampion(view);
        PumpUntil(() => calls.ContainsKey(("champion", 147)));
        Check(
            calls.Values.All(count => count == 1),
            "Each icon is loaded once across duplicate champions and resizing."
        );
        handler.Fail = true;
        Complete((Task)Invoke(view, "PollAsync")!);
        Check(Cards(view).Length == 0, "Disconnect clears previous player cards.");
        view.Dispose();
        Complete(Field<Task>(view, "_cleanupWork"));
    }

    private static void RenderRecommendations(LiveMatchView view)
    {
        var builds = new List<(int[] Ids, double? Win, double? Pick)>
        {
            (new[] { 3089, 3020 }, .53, .16),
            (new[] { 3089, 3157 }, .52, .11),
        };
        var augments = new List<(string Title, AramMetaRecommendation[] Rows)>
        {
            (
                "棱彩强化",
                new[]
                {
                    new AramMetaRecommendation(new[] { 2095 }, "测试强化", "强化说明", .56, .03, 100, new JObject()),
                    new AramMetaRecommendation(Array.Empty<int>(), "无效强化", "", null, null, 0, new JObject()),
                }
            ),
        };
        Invoke(view, "RenderRecommendations", builds, augments);
        PumpUntil(() =>
            Children(view)
                .OfType<Button>()
                .Where(button => button.Tag is ValueTuple<string, int>)
                .All(button => button.BackgroundImage != null)
        );
        var icon = Children(Field<FlowLayoutPanel>(view, "_recommendations"))
            .OfType<Button>()
            .Single(button => button.Tag is ValueTuple<string, int> tag && tag.Item1 == "augment");
        typeof(Control).GetMethod("OnClick", PrivateInstance)!.Invoke(icon, new object[] { EventArgs.Empty });
        var manual = Field<Dictionary<string, List<int>>>(view, "_manualAugments");
        Check(
            manual[Field<string>(view, "_selectedKey")].SequenceEqual(new[] { 2095 }),
            "Valid recommended augments record once and empty IDs never create a broken icon."
        );
    }

    private static void EquipmentOwnership(LiveMatchView view)
    {
        var before = Cards(view).ToDictionary(card => (string)card.Tag!);
        var host = Field<FlowLayoutPanel>(view, "_recommendations");
        var recommendation = host.Controls[0];
        var purchased = JObject.Parse(Sample);
        purchased["allPlayers"]![1]!["items"] = new JArray(new JObject { ["itemID"] = 3089, ["count"] = 1 });
        var snapshot = LiveMatchSnapshot.Parse(purchased);
        SetField(view, "_snapshot", snapshot);
        Invoke(view, "Render");
        var after = Cards(view).ToDictionary(card => (string)card.Tag!);
        string selfKey = snapshot.Players.Single(player => player.Self).Key;
        Check(
            before.Where(pair => pair.Key != selfKey).All(pair => ReferenceEquals(pair.Value, after[pair.Key])),
            "Buying an item retains all other player cards."
        );
        Check(ReferenceEquals(recommendation, host.Controls[0]), "Buying an item does not rebuild recommendations.");
        Check(
            Children(host)
                .OfType<Button>()
                .Where(button => button.Tag is ValueTuple<string, int> tag && tag.Item2 == 3089)
                .All(button => button.FlatAppearance.BorderSize == 2),
            "Owned recommendation items update immediately."
        );
    }

    private static void Capture(Form form, string name)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(AppContext.BaseDirectory, name));
    }

    private static void AlternateChampion(LiveMatchView view)
    {
        var alternate = JObject.Parse(Sample);
        alternate["allPlayers"]![1]!["rawChampionName"] = "Character_Seraphine_Name";
        alternate["allPlayers"]![1]!["championName"] = "星籁歌姬";
        SetField(view, "_currentQueue", 0);
        SetField(view, "_snapshot", LiveMatchSnapshot.Parse(alternate));
        Invoke(view, "Render");
        Check(
            Children(view).OfType<Label>().Any(label => label.Text == "星籁歌姬"),
            "Seraphine's alternate key resolves into the actual champion."
        );
    }

    private static void LateAssetsAndBorrowedClients()
    {
        using var client = new LiveMatchClient(new Handler());
        using var opgg = new OpggClient(new Recommendations());
        using var form = new Form();
        var icon = Completion<(Image?, string, string)>();
        var artwork = Completion<Image?>();
        int calls = 0;
        int artworkCalls = 0;
        using var view = CreateView(
            client,
            (_, _) =>
            {
                calls++;
                return icon.Task;
            },
            opgg
        );
        SetField(
            view,
            "_artworkLoader",
            (Func<int, CancellationToken, Task<Image?>>)(
                (_, _) =>
                {
                    artworkCalls++;
                    return artwork.Task;
                }
            )
        );
        form.Controls.Add(view);
        form.Show();
        PumpUntil(() => calls > 0 && artworkCalls > 0 && Cards(view).Length == 3);
        view.Dispose();
        var cleanup = Field<Task>(view, "_cleanupWork");
        Check(!cleanup.IsCompleted, "View cleanup waits for uncancellable shared asset copies.");
        using var iconImage = new Bitmap(4, 4);
        using var artworkImage = new Bitmap(4, 4);
        icon.SetResult((iconImage, "late", "late"));
        artwork.SetResult(artworkImage);
        Complete(cleanup);
        Check(
            Disposed(iconImage) && Disposed(artworkImage),
            "Late icon and artwork copies are disposed, never published."
        );
        var snapshot = client.GetAsync(CancellationToken.None);
        Complete(snapshot);
        Check(
            snapshot.Result.Players.Length == 3,
            "An injected live client remains available to its creator after close."
        );
        Complete(opgg.GetBuildAsync(new("global", "aram", "all", "none", 25), false, CancellationToken.None));
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

    private static void ClosedPollingAndStaleRanks()
    {
        using var client = new LiveMatchClient(new Handler());
        var queue = Completion<int>();
        var response = Completion<HttpResponseMessage>();
        var handler = new Recommendations { Read = (_, _) => response.Task };
        using var opgg = new OpggClient(handler);
        using var form = new Form();
        using var view = CreateView(
            client,
            (_, _) => Task.FromResult<(Image?, string, string)>((null, "", "")),
            opgg,
            () => queue.Task
        );
        form.Controls.Add(view);
        form.Show();
        PumpUntil(() => Field<bool>(view, "_busy"));
        view.Dispose();
        var cleanup = Field<Task>(view, "_cleanupWork");
        Check(!cleanup.IsCompleted, "Closing waits for the client queue callback already in flight.");
        queue.SetResult(450);
        Complete(cleanup);
        Check(Cards(view).Length == 0, "A late queue result cannot rebuild closed controls.");
        using var ranks = CreateView(client, (_, _) => Task.FromResult<(Image?, string, string)>((null, "", "")), opgg);
        SetField(ranks, "_snapshot", LiveMatchSnapshot.Parse(JObject.Parse(Sample)));
        SetField(ranks, "_currentQueue", 450);
        Invoke(ranks, "RefreshRanks", 450);
        PumpUntil(() => handler.Calls > 0);
        SetField(ranks, "_currentQueue", 0);
        SetField(ranks, "_rankStatus", "current mode");
        response.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Complete(Field<Task>(ranks, "_rankLoad"));
        Check(
            Field<string>(ranks, "_rankStatus") == "current mode",
            "An old rank failure cannot replace the new mode status."
        );
        ranks.Dispose();
        Complete(Field<Task>(ranks, "_cleanupWork"));
    }

    private static void InteractivePaths()
    {
        using var client = new LiveMatchClient(new Handler());
        using var opgg = new OpggClient(new Recommendations());
        using var am = new AramMetaClient(new TierIndex());
        using var form = new Form { ClientSize = new Size(1100, 740) };
        var guided = new List<(int Id, int Queue)>();
        using var view = new LiveMatchView(
            () => Task.FromResult(2400),
            (id, queue) => guided.Add((id, queue)),
            () => TimeSpan.FromDays(1),
            client,
            (_, _) => Task.FromResult<(Image?, string, string)>((null, "", "")),
            opgg,
            am
        );
        SetField(
            view,
            "_artworkLoader",
            (Func<int, CancellationToken, Task<Image?>>)((_, _) => Task.FromResult<Image?>(null))
        );
        form.Controls.Add(view);
        form.Show();
        PumpUntil(() => Cards(view).Length == 3);
        PumpUntil(() => Field<string>(view, "_rankStatus").Contains("arammeta"));
        Check(
            ((System.Collections.ICollection)Field<object>(view, "_ranks")).Count > 0,
            "The mayhem statistics fill the live ranks."
        );
        Check(
            Field<ComboBox>(view, "_augment").Items.Count > 0,
            "The mayhem statistics fill the manual augment picker."
        );
        Toolbar(view, guided);
        ManualAugments(view);
        SnapshotChange(view);
        Invoke(view, "RefreshRanks", 0);
        Invoke(view, "RefreshRanks", 2400);
        view.Dispose();
        Complete(Field<Task>(view, "_cleanupWork"));
    }

    private static void Toolbar(LiveMatchView view, List<(int Id, int Queue)> guided)
    {
        Field<ComboBox>(view, "_mode").SelectedIndex = 1;
        Click(Find(view, "刷新对局"));
        PumpUntil(() => Cards(view).Length == 3);

        string self = Field<string>(view, "_selectedKey");
        var other = Cards(view).Select(card => (string)card.Tag!).First(key => key != self);
        Click(Cards(view).First(card => (string)card.Tag! == other));
        PumpUntil(() => Field<string>(view, "_selectedKey") == other);

        Click(Find(view, "所选英雄完整攻略"));
        Check(
            guided.Count == 1 && guided[0].Queue == 2400 && guided[0].Id > 0,
            "The guide opens for the selected champion in the current mode."
        );
        SetField(view, "_currentQueue", 0);
        Click(Find(view, "所选英雄完整攻略"));
        Check(guided.Count == 1, "A mode without a guide opens nothing.");
        SetField(view, "_currentQueue", 2400);
    }

    private static void ManualAugments(LiveMatchView view)
    {
        var manual = Field<Dictionary<string, List<int>>>(view, "_manualAugments");
        string selected = Field<string>(view, "_selectedKey");
        var picker = Field<ComboBox>(view, "_augment");
        picker.SelectedIndex = 0;
        Click(Find(view, "记录已选"));
        Check(
            manual.TryGetValue(selected, out var ids) && ids.Count == 1,
            "The picker records the chosen augment for the selected player."
        );
        Click(Find(view, "清除记录"));
        Check(!manual.ContainsKey(selected), "Clearing drops the recorded augments.");

        SetField(view, "_selectedKey", "player-that-left");
        Click(Find(view, "记录已选"));
        Check(!manual.ContainsKey("player-that-left"), "Recording without a selected player does nothing.");
        SetField(view, "_selectedKey", selected);
        Click(Find(view, "记录已选"));
    }

    private static void SnapshotChange(LiveMatchView view)
    {
        var manual = Field<Dictionary<string, List<int>>>(view, "_manualAugments");
        Check(manual.Count > 0, "A recorded augment is kept while the roster is the same.");
        var changed = JObject.Parse(Sample);
        changed["allPlayers"]![0]!["riotId"] = "Other#CN";
        Invoke(view, "ApplySnapshot", LiveMatchSnapshot.Parse(changed), 2400);
        Check(manual.Count == 0, "A different roster drops the manual augment records.");
        Invoke(view, "ApplySnapshot", LiveMatchSnapshot.Parse(changed), 450);
        Check(
            Field<string>(view, "_rankStatus").Length == 0,
            "Switching the statistics mode clears the previous tier status."
        );
    }

    private static Button Find(LiveMatchView view, string text) =>
        Children(view).OfType<Button>().First(button => button.Text == text);

    private static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", PrivateInstance)!.Invoke(control, new object[] { EventArgs.Empty });

    private sealed class TierIndex : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Check(
                request.RequestUri!.AbsolutePath.EndsWith("tier-list.json", StringComparison.Ordinal),
                "The live page reads the complete statistics index once."
            );
            string body = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta", "index.json"),
                token
            );
            return Response(body);
        }
    }

    private static void CancelledRecommendations()
    {
        using var client = new LiveMatchClient(new Handler());
        var response = Completion<HttpResponseMessage>();
        var handler = new Recommendations { Read = (_, _) => response.Task };
        using var opgg = new OpggClient(handler);
        using var view = CreateView(client, (_, _) => Task.FromResult<(Image?, string, string)>((null, "", "")), opgg);
        var snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Sample));
        SetField(view, "_snapshot", snapshot);
        SetField(view, "_selectedKey", snapshot.Players.Single(player => player.Self).Key);
        SetField(view, "_currentQueue", 450);
        var old = (Task)Invoke(view, "LoadRecommendationsAsync")!;
        PumpUntil(() => handler.Calls > 0);
        SetField(view, "_currentQueue", 0);
        Complete((Task)Invoke(view, "LoadRecommendationsAsync")!);
        response.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Complete(old);
        Check(
            Field<FlowLayoutPanel>(view, "_recommendations").Controls[0].Text == "选择已识别的英雄查看推荐。",
            "Cancelled recommendation failures leave the newer selection message intact."
        );
        view.Dispose();
        Complete(Field<Task>(view, "_cleanupWork"));
    }
}
