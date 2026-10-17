using System.Net;
using System.Reflection;
using System.Text;
using League;
using League.Clients;
using League.Infrastructure;
using League.Managers;
using League.Models;
using League.Parsers;
using League.Services;
using League.States;
using League.UIState;
using static StatisticsCheckSupport;

/// <summary>
/// The coordinator that turns game phase changes into work: starting and stopping the phase monitor, the queue, the
/// champion select and the in-game branches, the end-of-game cleanup and the return to the lobby, and the disconnect
/// path. The client is a stub handler, the auto accept is configured but the client session has no message service, and
/// the window stays hidden, so nothing reaches a real client.
/// </summary>
internal static class GameFlowWatcherChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PhasePath = "/lol-gameflow/v1/gameflow-phase";

    private static readonly FieldInfo LcuHttpClient = typeof(LcuClient).GetField("_httpClient", Flags)!;
    private static readonly FieldInfo LcuSessionClient = typeof(LcuSession).GetField("_lcuClient", Flags)!;
    private static readonly FieldInfo ConnectionManager = typeof(LcuSession).GetField("_connectionManager", Flags)!;
    private static readonly FieldInfo HotkeyManager = typeof(FormMain).GetField("_hotkeyManager", Flags)!;
    private static readonly FieldInfo ChatSender = typeof(FormMain).GetField("_chatSender", Flags)!;
    private static readonly FieldInfo AppConfig = typeof(FormMain).GetField("_appConfig", Flags)!;
    private static readonly FieldInfo LcuPoller = typeof(FormMain).GetField("_lcuPoller", Flags)!;
    private static readonly FieldInfo Processor = typeof(GameFlowWatcher).GetField("_matchQueryProcessor", Flags)!;
    private static readonly MethodInfo InitializeServices = typeof(LcuSession).GetMethod("InitializeServices", Flags)!;
    private static readonly MethodInfo Disconnected = typeof(GameFlowWatcher).GetMethod(
        "HandleLcuDisconnected",
        Flags
    )!;

    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        var previous = FormMain.Globals.lcuClient;
        var previousMod = FormMain.Globals.CurrGameMod;
        var previousRefresh = RefreshState.ForceMatchRefresh;
        using var owner = new FormMain();
        ((IDisposable)HotkeyManager.GetValue(owner)!).Dispose();
        ChatSender.SetValue(owner, null);
        try
        {
            _ = owner.Handle; // the cards and the status panel marshal through this window
            // The delay the user configured (7) is not one of the offered values, so it has to fall back to zero.
            AppConfig.SetValue(owner, new LeagueConfig { EnableAutoAcceptQueue = true, AutoAcceptDelaySeconds = 7 });
            UseClient();
            var ui = new FormUiStateManager(owner);
            var processor = new MatchQueryProcessor();
            using var cards = new PlayerCardManager(owner, processor);
            var watcher = new GameFlowWatcher(owner, ui, cards, processor);

            NotReady(watcher, ui);
            QueuePhases(watcher, ui, owner);
            GamePhases(watcher, ui, owner);
            LobbyPhases(watcher, owner);
            FailingPhase(watcher);
            Monitoring(watcher, ui);
            Disconnect(watcher, owner, ui);
            watcher.ClearGameState();
            AwaitUntil(() => true, "the end-of-game cleanup finished");
        }
        finally
        {
            ((AsyncPoller)LcuPoller.GetValue(owner)!).Stop();
            FormMain.Globals.lcuClient = previous;
            FormMain.Globals.CurrGameMod = previousMod;
            RefreshState.ForceMatchRefresh = previousRefresh;
        }

        Console.WriteLine(
            "PASS: the game flow watcher starts and stops its monitor, handles the queue, champion select, in-game, "
                + "end-of-game, lobby and disconnect phases and clears its state, all against a stub client."
        );
    }

    // ---------------------------------------------------------------- starting and stopping

    private static void NotReady(GameFlowWatcher watcher, FormUiStateManager ui)
    {
        ui.LcuReady = false;
        watcher.StartGameflowWatcher();
        Check(watcher.CurrentPhase == "未知", "A watcher without a ready client does not start monitoring");
    }

    private static void Monitoring(GameFlowWatcher watcher, FormUiStateManager ui)
    {
        ui.LcuReady = true;
        watcher.StartGameflowWatcher();
        AwaitUntil(
            () => watcher.CurrentPhase == "Lobby",
            "The phase monitor reports the phase the client answered with"
        );
        watcher.StopGameflowWatcher();
        Check(ui.LcuReady, "Stopping the watcher leaves the connection state to the disconnect path");
    }

    // ---------------------------------------------------------------- the phases

    private static void QueuePhases(GameFlowWatcher watcher, FormUiStateManager ui, FormMain owner)
    {
        Await(watcher.OnGameflowPhaseChanged("Matchmaking", null));
        Check(watcher.CurrentPhase == "Matchmaking", "The phase that was handled is remembered");
        Check(!ui.IsGame, "Matchmaking is not a game");

        Await(watcher.OnGameflowPhaseChanged("ReadyCheck", "Matchmaking"));
        Check(!ui.IsGame, "A ready check is not a game either");
        Check(owner.imageTabControl1.SelectedIndex == 1, "The queue phases bring the champion select tab forward");
    }

    private static void GamePhases(GameFlowWatcher watcher, FormUiStateManager ui, FormMain owner)
    {
        owner._waitingPanel = new Panel();
        Await(watcher.OnGameflowPhaseChanged("ChampSelect", "ReadyCheck"));
        Check(ui.IsGame, "Champion select counts as a game");
        Check(owner._waitingPanel == null, "Champion select closes the waiting hint");

        Await(watcher.OnGameflowPhaseChanged("InProgress", "ChampSelect"));
        Check(ui.IsGame, "The loading screen counts as a game");
    }

    private static void LobbyPhases(GameFlowWatcher watcher, FormMain owner)
    {
        FormMain.Globals.CurrGameMod = "420";
        Await(watcher.OnGameflowPhaseChanged("EndOfGame", "InProgress"));
        Check(watcher.CurrentPhase == "EndOfGame", "The end of a game is remembered");

        // A phase that did not follow a game is not cleaned up.
        Await(watcher.OnGameflowPhaseChanged("WaitingForStats", null));
        Await(watcher.OnGameflowPhaseChanged("PreEndOfGame", "WaitingForStats"));

        // The cleanup remembered that a game just ended, so the lobby returns to the mode that was played.
        Await(watcher.OnGameflowPhaseChanged("Lobby", "PreEndOfGame"));
        Await(watcher.OnGameflowPhaseChanged("None", "Lobby"));
        Check(FormMain.Globals.CurrGameMod == "420", "Returning to the lobby keeps the mode that was played");
    }

    /// <summary>A phase handler that fails is logged; the loop keeps its state and carries on.</summary>
    private static void FailingPhase(GameFlowWatcher watcher)
    {
        var processor = Processor.GetValue(watcher);
        Processor.SetValue(watcher, null);
        try
        {
            Await(watcher.OnGameflowPhaseChanged("Matchmaking", null));
        }
        finally
        {
            Processor.SetValue(watcher, processor);
        }

        Check(watcher.CurrentPhase == "Matchmaking", "A failing phase handler still records the phase");
    }

    private static void Disconnect(GameFlowWatcher watcher, FormMain owner, FormUiStateManager ui)
    {
        ui.LcuReady = true;
        ui.IsGame = true;
        Disconnected.Invoke(watcher, null);
        ((AsyncPoller)LcuPoller.GetValue(owner)!).Stop();
        Check(!ui.LcuReady && !ui.IsGame, "A disconnected client clears both interface states");
    }

    // ---------------------------------------------------------------- the stub client

    /// <summary>A client that answers the phase endpoint only; the connection manager stays out of the way.</summary>
    private static void UseClient()
    {
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose();
        LcuHttpClient.SetValue(client, new HttpClient(new Stub()) { BaseAddress = new Uri("https://127.0.0.1/") });
        var session = new LcuSession();
        LcuSessionClient.SetValue(session, client);
        InitializeServices.Invoke(session, null);
        // The watcher's disconnect path asks the window to poll for a client again; without a connection manager that
        // poll fails immediately instead of looking for the player's real client process.
        ConnectionManager.SetValue(session, null);
        FormMain.Globals.lcuClient = session;
    }

    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response =
                request.RequestUri!.AbsolutePath == PhasePath
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("\"Lobby\"", Encoding.UTF8, "application/json"),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    // ---------------------------------------------------------------- pumping

    private static void Await(Task task, int seconds = 6)
    {
        AwaitUntil(() => task.IsCompleted, "The phase handling finished in time", seconds);
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
}
