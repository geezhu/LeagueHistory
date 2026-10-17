using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using League;
using League.Clients;
using League.Infrastructure;
using League.Managers;
using League.Parsers;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The service that fills the team cards: entering champion select, the statistics queue taken from the client's
/// snapshot, reading the loading screen roster once and the layout and failure paths around it. The client is a stub
/// handler, the champion catalog is empty and the window stays hidden, so no real client, network or player file is
/// touched.
/// </summary>
internal static class TeamCardDisplayChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string SummonerPath = "/lol-summoner/v1/current-summoner";
    private const string ChampSelectPath = "/lol-champ-select/v1/session";
    private const string SessionPath = "/lol-gameflow/v1/session";
    private const string Me = "check-player-1";

    private static readonly FieldInfo LcuHttpClient = typeof(LcuClient).GetField("_httpClient", Flags)!;
    private static readonly FieldInfo LcuSessionClient = typeof(LcuSession).GetField("_lcuClient", Flags)!;
    private static readonly FieldInfo HotkeyManager = typeof(FormMain).GetField("_hotkeyManager", Flags)!;
    private static readonly FieldInfo ChatSender = typeof(FormMain).GetField("_chatSender", Flags)!;
    private static readonly FieldInfo FromChampSelect = PrivateField("_fromChampionSelect");
    private static readonly FieldInfo SelectionQueue = PrivateField("_selectionQueue");
    private static readonly MethodInfo InitializeServices = typeof(LcuSession).GetMethod("InitializeServices", Flags)!;
    private static readonly MethodInfo RosterOnce = PrivateMethod("TryLoadRosterOnceAsync");
    private static readonly MethodInfo TeamsLayout = PrivateMethod("ShowTeamsLayoutAsync");
    private static readonly MethodInfo GameSessionLoad = PrivateMethod("LoadGameSessionAsync");

    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        var previousSession = FormMain.Globals.lcuClient;
        var previousResources = FormMain.Globals.resLoading;
        var previousMod = FormMain.Globals.CurrGameMod;
        using var owner = new FormMain();
        ((IDisposable)HotkeyManager.GetValue(owner)!).Dispose();
        ChatSender.SetValue(owner, null);
        try
        {
            _ = owner.Handle; // the cards marshal every render through this window
            // An empty champion catalog keeps the conflicting-roster path deterministic even when a game is running.
            FormMain.Globals.resLoading = new ResourceLoading();
            using var cards = new PlayerCardManager(owner, new MatchQueryProcessor());
            var service = new TeamCardDisplayService(owner, cards);
            ChampSelectEntry(owner, service);
            MyTeamCards(owner, service);
            RosterBranches(owner, cards, service);
            LoadingScreen(owner, cards, service);
            Snapshots(owner, service);
        }
        finally
        {
            FormMain.Globals.lcuClient = previousSession;
            FormMain.Globals.resLoading = previousResources;
            FormMain.Globals.CurrGameMod = previousMod;
        }

        Console.WriteLine(
            "PASS: the team cards enter champion select, resolve the statistics queue from the client snapshot, read "
                + "the loading screen roster and stop on a stale or failing one, all against a stub client."
        );
    }

    // ---------------------------------------------------------------- champion select

    private static void ChampSelectEntry(FormMain owner, TeamCardDisplayService service)
    {
        UseClient(_ => Refused());
        owner._waitingPanel = new Panel();
        owner._cachedMyTeam = new JArray(new JObject());
        Await(service.EnterChampSelectAsync());

        Check(owner._waitingPanel == null, "Entering champion select closes the waiting hint");
        Check(
            owner._cachedMyTeam!.Count == 0 && owner._cachedEnemyTeam!.Count == 0,
            "Entering champion select drops the previous roster"
        );
        Check(
            (bool)FromChampSelect.GetValue(service)!,
            "The roster that follows comes from champion select until the game starts"
        );
    }

    private static void MyTeamCards(FormMain owner, TeamCardDisplayService service)
    {
        UseClient(_ => Refused());
        Await(service.ShowMyTeamCards());
        Check(owner._cachedMyTeam!.Count == 0, "A client without a champion select session shows no cards");

        UseClient(path => path == ChampSelectPath ? Json("""{"queueId":420}""") : Refused());
        Await(service.ShowMyTeamCards());
        Check(owner._cachedMyTeam!.Count == 0, "An empty team list shows no cards");

        var stub = UseClient(path => path == ChampSelectPath ? Json(ChampSelectBody("420", 5)) : Refused());
        Await(service.ShowMyTeamCards());
        Check(owner._cachedMyTeam!.Count == 5, "The champion select team fills the ally snapshot");
        Check(Selection(service) == "420", "A queue with its own history filter keeps the client's value");
        Check(stub.Count(ChampSelectPath) == 1, "The champion select session is read once per snapshot");

        UseClient(path =>
            path switch
            {
                ChampSelectPath => Json(ChampSelectBody("3270", 5)),
                SessionPath => Json(GameBody(2400, "CLASSIC", 5, 5)),
                _ => Refused(),
            }
        );
        Await(service.ShowMyTeamCards());
        Check(Selection(service) == "2400", "A queue without a history filter is resolved from the game mode");

        UseClient(path =>
            path switch
            {
                ChampSelectPath => Json(ChampSelectBody("900", 5)),
                SessionPath => Json(GameBody(0, "", 5, 5)),
                _ => Refused(),
            }
        );
        Await(service.ShowMyTeamCards());
        Check(Selection(service) == "900", "A session that resolves no filter keeps the client's queue");

        UseClient(path =>
            path switch
            {
                ChampSelectPath => Json(ChampSelectBody("900", 5)),
                SessionPath => Json(GameBody(2400, "CLASSIC", 5, 5)),
                _ => Refused(),
            }
        );
        Await(service.ShowMyTeamCards());
        Check(Selection(service) == "900", "A later snapshot of the same champion select reuses the resolved queue");
    }

    // ---------------------------------------------------------------- the loading screen roster

    private static void RosterBranches(FormMain owner, PlayerCardManager cards, TeamCardDisplayService service)
    {
        long generation = cards.Generation;

        UseClient(_ => Refused());
        Check(Attempt(service, generation - 1) == "Stale", "A roster read for an older session is dropped");

        UseClient(path => path == SummonerPath ? Json("""{"summonerId":770000}""") : Refused());
        Check(
            Attempt(service, generation) == "Unavailable",
            "A summoner without a puuid leaves the roster unavailable"
        );

        UseClient(path =>
            path switch
            {
                SummonerPath => Json(SummonerBody(Me)),
                SessionPath => Json("""{"gameData":{}}"""),
                _ => Refused(),
            }
        );
        Check(
            Attempt(service, generation) == "Unavailable",
            "A session without both team lists leaves the roster unavailable"
        );

        UseClient(path =>
            path switch
            {
                SummonerPath => Json(SummonerBody("check-player-99")),
                SessionPath => Json(GameBody(420, "CLASSIC", 5, 5)),
                _ => Refused(),
            }
        );
        Check(
            Attempt(service, generation) == "Unavailable",
            "A summoner on neither team leaves the roster unavailable"
        );

        // A leftover roster puts the summoner on both sides; the live game decides, and no live client answers here.
        owner._cachedMyTeam = new JArray(new JObject());
        UseClient(path =>
            path switch
            {
                SummonerPath => Json(SummonerBody(Me)),
                SessionPath => Json(GameBody(420, "CLASSIC", 5, 5, teamTwoFrom: 1)),
                _ => Refused(),
            }
        );
        Check(Attempt(service, generation) == "Unavailable", "A roster with the summoner on both sides is not shown");
        Check(owner._cachedMyTeam!.Count == 1, "A discarded roster leaves the previous snapshot alone");

        UseClient(path =>
            path switch
            {
                SummonerPath => Json(SummonerBody(Me)),
                SessionPath => Json(GameBody(420, "CLASSIC", 5, 5)),
                _ => Refused(),
            }
        );
        Check(Attempt(service, generation) == "Complete", "Two full teams without a provisional identity are complete");
        Check(owner._cachedMyTeam!.Count == 5 && owner._cachedEnemyTeam!.Count == 5, "Both sides reach the snapshot");
        Check(
            FormMain.Globals.CurrGameMod == "420",
            "The client's own queue is remembered for the return to the lobby"
        );

        UseClient(path =>
            path switch
            {
                SummonerPath => Json(SummonerBody(Me)),
                SessionPath => Json(GameBody(420, "CLASSIC", 5, 4)),
                _ => Refused(),
            }
        );
        Check(
            Attempt(service, generation) == "Partial",
            "A team completed from the champion selections stays provisional"
        );
    }

    private static void LoadingScreen(FormMain owner, PlayerCardManager cards, TeamCardDisplayService service)
    {
        owner.lastChampSelectSnapshotString = "champion select snapshot";
        owner._waitingPanel = new Panel();
        service.ClearSnapshots();
        Check(owner.lastChampSelectSnapshotString.Length == 0, "Clearing the snapshots drops the champion select text");

        var stub = UseClient(path =>
            path switch
            {
                SummonerPath => Json(SummonerBody(Me)),
                SessionPath => Json(GameBody(420, "CLASSIC", 5, 5)),
                _ => Refused(),
            }
        );
        owner._cachedMyTeam = new JArray();
        owner._cachedEnemyTeam = new JArray();
        service.StartGameSessionLoad();
        AwaitUntil(() => owner._cachedMyTeam?.Count == 5, "The loading screen roster arrives");
        Check(owner._cachedEnemyTeam!.Count == 5, "The loading screen shows both teams");
        Check(owner._waitingPanel == null, "The loading screen closes the waiting hint");
        Check(stub.Count(SummonerPath) > 0, "The loading screen reads the current summoner");

        Layout(owner, cards, service);
        FailedRoster(cards, service);
        StaleRoster(owner, cards, service);
    }

    private static void Layout(FormMain owner, PlayerCardManager cards, TeamCardDisplayService service)
    {
        UseClient(_ => Refused());
        owner._waitingPanel = new Panel();
        owner._cachedMyTeam = new JArray(new JObject());
        Await(LayoutTask(service, cards.Generation - 1, false));
        Check(owner._waitingPanel != null, "A stale generation does not lay out the roster");
        Check(owner._cachedMyTeam!.Count == 1, "A stale layout leaves the snapshot alone");

        owner._waitingPanel = new Panel();
        Await(LayoutTask(service, cards.Generation, true));
        Check(owner._waitingPanel == null, "The layout request closes the waiting hint and shows the teams");
        Check(owner._cachedMyTeam!.Count == 0, "A cleared layout drops the roster");
    }

    private static void FailedRoster(PlayerCardManager cards, TeamCardDisplayService service)
    {
        var previous = FormMain.Globals.lcuClient;
        using (var cancellation = new CancellationTokenSource())
        {
            // A session without its services: the first roster read throws instead of answering.
            FormMain.Globals.lcuClient = new LcuSession();
            var task = GameSessionTask(service, cards.Generation, cancellation.Token, false);
            Await(cancellation.CancelAsync());
            Await(task);
        }
        FormMain.Globals.lcuClient = previous;
        Check(true, "A failing roster read stops the load instead of retrying forever");
    }

    private static void StaleRoster(FormMain owner, PlayerCardManager cards, TeamCardDisplayService service)
    {
        UseClient(_ => Refused());
        owner._cachedMyTeam = new JArray(new JObject());
        Await(GameSessionTask(service, cards.Generation - 1, CancellationToken.None, false));
        Check(owner._cachedMyTeam!.Count == 1, "A load for an older session never touches the roster");
    }

    private static void Snapshots(FormMain owner, TeamCardDisplayService service)
    {
        owner.lastChampSelectSnapshotString = "champion select";
        service.ClearSnapshots();
        Check(owner.lastChampSelectSnapshotString.Length == 0, "Clearing stops the running load and the snapshot text");
    }

    // ---------------------------------------------------------------- fixtures

    private static string ChampSelectBody(string queue, int players) =>
        "{\"queueId\":" + queue + ",\"myTeam\":[" + string.Join(",", Players(1, players)) + "]}";

    private static string GameBody(int queue, string mode, int inOne, int inTwo, int teamTwoFrom = 6) =>
        "{\"gameData\":{\"queue\":{\"id\":"
        + queue
        + ",\"gameMode\":\""
        + mode
        + "\",\"numPlayersPerTeam\":5},\"teamOne\":["
        + string.Join(",", Players(1, inOne))
        + "],\"teamTwo\":["
        + string.Join(",", Players(teamTwoFrom, inTwo))
        + "],\"playerChampionSelections\":["
        + string.Join(",", Selections())
        + "]}}";

    private static IEnumerable<string> Players(int from, int count) =>
        Enumerable.Range(from, count).Select(i => Player(i));

    private static string Player(int i) =>
        "{\"summonerId\":"
        + (770000 + i)
        + ",\"puuid\":\"check-player-"
        + i
        + "\",\"championId\":"
        + (100 + i)
        + ",\"nameVisibilityType\":\"HIDDEN\"}";

    // Every mode the main window lists has one champion per player, so the selections name the whole lobby.
    private static IEnumerable<string> Selections() =>
        Enumerable.Range(1, 10).Select(i => "{\"puuid\":\"check-player-" + i + "\",\"championId\":" + (100 + i) + "}");

    private static string SummonerBody(string puuid) => "{\"puuid\":\"" + puuid + "\",\"summonerId\":770001}";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Refused() => new(HttpStatusCode.NotFound);

    /// <summary>Points the application's client at a stub handler and returns it for request inspection.</summary>
    private static Stub UseClient(Func<string, HttpResponseMessage> route)
    {
        var stub = new Stub(route);
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose();
        LcuHttpClient.SetValue(client, new HttpClient(stub) { BaseAddress = new Uri("https://127.0.0.1/") });
        var session = new LcuSession();
        LcuSessionClient.SetValue(session, client);
        InitializeServices.Invoke(session, null);
        FormMain.Globals.lcuClient = session;
        return stub;
    }

    private static string Attempt(TeamCardDisplayService service, long generation)
    {
        var task = (Task)RosterOnce.Invoke(service, [generation, CancellationToken.None])!;
        Await(task, 10);
        return task.GetType().GetProperty("Result")!.GetValue(task)!.ToString()!;
    }

    private static Task LayoutTask(TeamCardDisplayService service, long generation, bool clearCards) =>
        (Task)TeamsLayout.Invoke(service, [generation, CancellationToken.None, clearCards])!;

    private static Task GameSessionTask(
        TeamCardDisplayService service,
        long generation,
        CancellationToken token,
        bool clearCards
    ) => (Task)GameSessionLoad.Invoke(service, [generation, token, clearCards])!;

    private static string Selection(TeamCardDisplayService service) => (string)SelectionQueue.GetValue(service)!;

    private static FieldInfo PrivateField(string name) => typeof(TeamCardDisplayService).GetField(name, Flags)!;

    private static MethodInfo PrivateMethod(string name) => typeof(TeamCardDisplayService).GetMethod(name, Flags)!;

    // ---------------------------------------------------------------- pumping and reflection helpers

    /// <summary>Runs the message loop until the work finishes, so UI-affine continuations can be delivered.</summary>
    private static void Await(Task task, int seconds = 6)
    {
        AwaitUntil(() => task.IsCompleted, "The team card work finished in time", seconds);
        task.GetAwaiter().GetResult();
    }

    private static void AwaitUntil(Func<bool> ready, string message, int seconds = 6)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }

        Check(ready(), message);
    }

    /// <summary>
    /// Answers every route from one table. The cancellation token is deliberately ignored: the service's own
    /// cancellation checks are what these checks exercise.
    /// </summary>
    private sealed class Stub(Func<string, HttpResponseMessage> route) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public int Count(string path) => _requests.Count(requested => requested == path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            _requests.Enqueue(path);
            return Task.FromResult(route(path));
        }
    }
}
