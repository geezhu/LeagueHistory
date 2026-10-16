using System.Net;
using System.Reflection;
using System.Text;
using League.Clients;
using League.PrimaryElection;
using static StatisticsCheckSupport;

/// <summary>
/// The LCU services that talk to the client over HTTP: chat, match history, replays and champion select.
/// A recording handler answers in place of the client, so nothing here needs a running League client.
/// </summary>
internal static class LcuClientServiceChecks
{
    public static async Task RunAsync()
    {
        await ChatChecks();
        await MatchChecks();
        await ReplayChecks();
        await ChampionSelectChecks();
        Console.WriteLine(
            "PASS: chat conversation lookup and send, paged and latest match history, full match, replay download "
                + "and watch, champion select preselect, bench swap and skin, with no client and no game input."
        );
    }

    private static async Task ChatChecks()
    {
        var sent = new Recorder(_ => Json("{}"));
        Check(await new ChatService(Client(sent)).SendMessageAsync("c1", "你好"), "A chat message is sent");
        Check(
            sent.Paths.Single() == "/lol-chat/v1/conversations/c1/messages",
            "The message goes to the named conversation"
        );

        var refused = new Recorder(_ => Json("{}", HttpStatusCode.InternalServerError));
        Check(
            !await new ChatService(Client(refused)).SendMessageAsync("c1", "你好"),
            "A refused message reports failure"
        );

        var custom = new Recorder(_ => Json("""[{"id":"c9","type":"customGame"}]"""));
        Check(
            await new ChatService(Client(custom)).FindConversationIdAsync("game", 1, 0) == "c9",
            "A game request accepts a custom game conversation"
        );

        var byStatus = new Recorder(_ => Json("""[{"id":"a","type":"other"},{"id":"b","gameStatus":"champSelect"}]"""));
        Check(
            await new ChatService(Client(byStatus)).FindConversationIdAsync("game", 1, 0) == "b",
            "A conversation is matched by game status as well as by type"
        );

        var active = new Recorder(_ => Json("""[{"id":"live","state":"active"}]"""));
        Check(
            await new ChatService(Client(active)).FindConversationIdAsync("ingame", 1, 0) == "live",
            "A running game is accepted by its session state"
        );

        var byName = new Recorder(_ => Json("""[{"id":"rift","name":"召唤师峡谷 排位"}]"""));
        Check(
            await new ChatService(Client(byName)).FindConversationIdAsync("game", 1, 0) == "rift",
            "A named game conversation is accepted without a type"
        );

        var withoutId = new Recorder(_ => Json("""[{"type":"game"}]"""));
        Check(
            await new ChatService(Client(withoutId)).FindConversationIdAsync("game", 1, 0) == null,
            "A conversation without an id is skipped"
        );

        var scene = new Recorder(_ => Json("""[{"id":"x","type":"someQueue"}]"""));
        Check(
            await new ChatService(Client(scene)).FindConversationIdAsync("someQueue", 1, 0) == "x",
            "An unknown scene matches its own type"
        );

        var empty = new Recorder(_ => Json("[]"));
        Check(
            await new ChatService(Client(empty)).FindConversationIdAsync("game", 3, 0) == null
                && empty.Paths.Count == 3,
            "An empty conversation list is retried and then gives up"
        );

        var lost = new Recorder(_ => Json("{}", HttpStatusCode.NotFound));
        Check(
            await new ChatService(Client(lost)).FindConversationIdAsync("game", 3, 0) == null && lost.Paths.Count == 1,
            "A refused conversation list is not retried"
        );

        var broken = new Recorder(_ => Json("not json"));
        Check(
            await new ChatService(Client(broken)).FindConversationIdAsync("game", 2, 0) == null
                && broken.Paths.Count == 2,
            "A malformed conversation list is retried and then gives up"
        );
    }

    private static async Task MatchChecks()
    {
        var paged = new Recorder(_ => Json("""{"games":{"games":[{"gameId":1},{"gameId":2}]}}"""));
        var service = new MatchService(Client(paged));
        Check(service.GetAverageResponseTime() == 0, "Without a request the average response time is zero");
        Check(
            (await service.FetchMatchesWithRetryAsync("puuid", 0, 20))?.Count == 2,
            "The paged history returns the games"
        );
        Check(
            paged.Paths.Single().Contains("begIndex=0&endIndex=20", StringComparison.Ordinal),
            "The paged history asks for the requested range"
        );

        var preheat = new MatchService(Client(new Recorder(_ => Json("""{"games":{"games":[]}}"""))));
        Check(
            await preheat.FetchMatchesWithRetryAsync("puuid", 0, 20, isPreheat: true) is { Count: 0 },
            "A preheat request still returns the games"
        );
        Check(preheat.GetAverageResponseTime() == 0, "A preheat request is not timed");

        var noGames = new Recorder(_ => Json("""{"games":{}}"""));
        Check(
            await new MatchService(Client(noGames)).FetchMatchesWithRetryAsync("puuid", 0, 20) == null,
            "A response without games returns nothing"
        );
        Check(noGames.Paths.Count == 1, "A successful response is never retried");

        var refused = new Recorder(_ => Json("{}", HttpStatusCode.InternalServerError));
        Check(
            await new MatchService(Client(refused)).FetchMatchesWithRetryAsync("puuid", 0, 20) == null,
            "A refused paged request gives up"
        );
        Check(refused.Paths.Count == 3, "A refused paged request is retried twice");

        var refusedPreheat = new Recorder(_ => Json("{}", HttpStatusCode.InternalServerError));
        Check(
            await new MatchService(Client(refusedPreheat)).FetchMatchesWithRetryAsync("puuid", 0, 20, isPreheat: true)
                == null,
            "A refused preheat gives up"
        );
        Check(refusedPreheat.Paths.Count == 1, "A preheat request is not retried");

        var latest = new Recorder(_ => Json("""{"games":{"games":[{"gameId":3}]}}"""));
        Check(
            (await new MatchService(Client(latest)).FetchLatestMatchesAsync("puuid"))?.Count == 1,
            "The latest history returns the games"
        );

        var latestRefused = new Recorder(_ => Json("{}", HttpStatusCode.InternalServerError));
        Check(
            await new MatchService(Client(latestRefused)).FetchLatestMatchesAsync("puuid") == null,
            "A refused latest request gives up after its retries"
        );
        Check(latestRefused.Paths.Count == 2, "The latest history is tried twice");

        var full = new Recorder(_ => Json("""{"gameId":7}"""));
        var match = await new MatchService(Client(full)).GetFullMatchByGameIdAsync(7);
        Check(match?["gameId"]?.ToObject<long>() == 7, "A full match is returned");
        var missing = new Recorder(_ => Json("{}", HttpStatusCode.NotFound));
        Check(
            await new MatchService(Client(missing)).GetFullMatchByGameIdAsync(7) == null,
            "A missing full match returns nothing"
        );
        var broken = new Recorder(_ => Json("not json"));
        Check(
            await new MatchService(Client(broken)).GetFullMatchByGameIdAsync(7) == null,
            "A malformed full match returns nothing"
        );
    }

    private static async Task ReplayChecks()
    {
        var ready = new Recorder(_ => Json("""{"state":"watch","downloadProgress":100}"""));
        Check(await new ReplayService(Client(ready)).DownloadReplayAsync(5), "A finished replay needs no download");
        Check(
            ready.Paths.Single() == "/lol-replays/v1/metadata/5",
            "A finished replay is decided from its metadata alone"
        );

        var refused = new Recorder(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/metadata/5", StringComparison.Ordinal)
                ? Json("{}", HttpStatusCode.NotFound)
                : Json("{}", HttpStatusCode.InternalServerError)
        );
        Check(!await new ReplayService(Client(refused)).DownloadReplayAsync(5), "A refused download reports failure");
        Check(
            refused.Paths.Count == 2 && refused.Paths[1] == "/lol-replays/v1/rofls/5/download",
            "A missing replay is downloaded once"
        );

        var watch = new Recorder(_ => Json("{}"));
        Check(await new ReplayService(Client(watch)).PlayReplayAsync(5), "A replay starts");
        Check(watch.Paths.Single() == "/lol-replays/v1/rofls/5/watch", "The replay is started at the watch endpoint");

        var watchRefused = new Recorder(_ => Json("{}", HttpStatusCode.InternalServerError));
        Check(
            !await new ReplayService(Client(watchRefused)).PlayReplayAsync(5),
            "A refused replay start reports failure"
        );
    }

    private static async Task ChampionSelectChecks()
    {
        await PreselectChecks();
        await BenchSwapChecks();
        await SkinChecks();
    }

    private static async Task PreselectChecks()
    {
        var (noSession, _) = Picker(null);
        Check(!await noSession.AutoDeclareIntentAsync([Hero(1, 1)]), "Without a session nothing is preselected");

        var (noActions, _) = Picker("{}");
        Check(!await noActions.AutoDeclareIntentAsync([Hero(1, 1)]), "A session without actions preselects nothing");

        var (otherPick, _) = Picker(OtherPick);
        Check(!await otherPick.AutoDeclareIntentAsync([Hero(1, 1)]), "Another player's pick is not ours to declare");

        var (zeroAction, _) = Picker(ZeroAction);
        Check(!await zeroAction.AutoDeclareIntentAsync([Hero(1, 1)]), "An action without an id is not patched");

        var (taken, _) = Picker(
            """
            {"localPlayerCellId":0,"bans":{"myTeamBans":[7],"theirTeamBans":[8]},
             "myTeam":[{"cellId":0,"championId":9}],
             "actions":[[{"id":3,"actorCellId":0,"type":"pick","completed":false,"championId":0}]]}
            """
        );
        Check(
            !await taken.AutoDeclareIntentAsync([Hero(7, 1), Hero(8, 2), Hero(9, 3)]),
            "Heroes that are banned or already picked are skipped"
        );

        var (hovered, _) = Picker(Hovered);
        Check(!await hovered.AutoDeclareIntentAsync([Hero(42, 1)]), "A hero already hovered is not declared again");

        var (free, freeRecorder) = Picker(MyTurn);
        Check(await free.AutoDeclareIntentAsync([Hero(42, 1), Hero(43, 2)]), "The first available hero is declared");
        Check(
            freeRecorder.Paths.Last() == "/lol-champ-select/v1/session/actions/3",
            "The declaration patches our own action"
        );

        var (refused, _) = Picker(MyTurn, HttpStatusCode.InternalServerError);
        Check(!await refused.AutoDeclareIntentAsync([Hero(42, 1)]), "A refused declaration reports failure");

        var (empty, emptyRecorder) = Picker(MyTurn);
        Check(!await empty.AutoDeclareIntentAsync([]), "An empty priority list is not declared");
        Check(!await empty.AutoDeclareIntentAsync(null!), "A missing priority list is not declared");
        Check(emptyRecorder.Paths.Count == 0, "A list without heroes sends no request");
    }

    private static async Task BenchSwapChecks()
    {
        var (rift, riftRecorder) = Picker(
            """{"queueId":420,"localPlayerCellId":0,"myTeam":[{"cellId":0,"championId":10}],"benchChampions":[{"championId":20}]}"""
        );
        await rift.AutoSwapToHighestPriorityAsync([Hero(10, 5), Hero(20, 2)]);
        Check(riftRecorder.Paths.Count == 1, "A mode without a bench never swaps");

        var (swap, swapRecorder) = Picker(Bench);
        await swap.AutoSwapToHighestPriorityAsync([Hero(10, 5), Hero(20, 2)]);
        Check(
            swapRecorder.Paths.Last() == "/lol-champ-select/v1/session/bench/swap/20",
            "A better hero on the bench is taken"
        );

        var (keep, keepRecorder) = Picker(Bench);
        await keep.AutoSwapToHighestPriorityAsync([Hero(10, 2), Hero(20, 5)]);
        Check(keepRecorder.Paths.Count == 1, "A worse hero on the bench is left alone");

        var (none, noneRecorder) = Picker("""{"queueId":450,"localPlayerCellId":0,"myTeam":[],"benchChampions":[]}""");
        await none.AutoSwapToHighestPriorityAsync([Hero(10, 1)]);
        Check(noneRecorder.Paths.Count == 1, "Without a champion of our own there is nothing to swap");
    }

    private static async Task SkinChecks()
    {
        var (skin, skinRecorder) = Picker("{}");
        Check(!await skin.TryApplyPreferredSkinAsync(0, 1001), "A missing champion has no skin to apply");
        Check(!await skin.TryApplyPreferredSkinAsync(42, 0), "A missing skin is not applied");
        Check(!await skin.TryApplyPreferredSkinAsync(42, 1000), "A default skin is not applied");
        Check(!await skin.TryApplyPreferredSkinAsync(42, 1001, 1001), "A skin already applied is not applied again");
        Check(skinRecorder.Paths.Count == 0, "Rejected skins send no request");
        Check(await skin.TryApplyPreferredSkinAsync(42, 1001), "A chosen skin is applied");
        Check(
            skinRecorder.Paths.Single() == "/lol-champ-select/v1/session/my-selection",
            "The skin patches the champion selection"
        );

        var (refused, _) = Picker("{}", HttpStatusCode.InternalServerError);
        Check(!await refused.TryApplyPreferredSkinAsync(42, 1001), "A refused skin reports failure");
    }

    private static PreliminaryHero Hero(int championId, int priority) =>
        new()
        {
            ChampionId = championId,
            ChampionName = $"英雄{championId}",
            Priority = priority,
        };

    private const string OtherPick =
        """{"localPlayerCellId":0,"actions":[[{"id":1,"actorCellId":1,"type":"pick","completed":false}]]}""";

    private const string ZeroAction =
        """{"localPlayerCellId":0,"actions":[[{"id":0,"actorCellId":0,"type":"pick","completed":false}]]}""";

    private const string MyTurn =
        """{"localPlayerCellId":0,"actions":[[{"id":3,"actorCellId":0,"type":"pick","completed":false,"championId":0}]]}""";

    private const string Hovered =
        """{"localPlayerCellId":0,"actions":[[{"id":3,"actorCellId":0,"type":"pick","completed":false,"championId":42}]]}""";

    private const string Bench = """
        {"queueId":450,"localPlayerCellId":0,"myTeam":[{"cellId":0,"championId":10}],
         "benchChampions":[{"championId":20}]}
        """;

    /// <summary>The champion select session answers the session endpoint; every other call returns <paramref name="status"/>.</summary>
    private static (ChampionSelectService Service, Recorder Recorder) Picker(
        string? session,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        var recorder = new Recorder(request =>
            request.RequestUri!.AbsolutePath == "/lol-champ-select/v1/session"
                ? session == null
                    ? Json("{}", HttpStatusCode.NotFound)
                    : Json(session)
                : Json("{}", status)
        );
        var client = Client(recorder);
        return (new ChampionSelectService(client, new GameflowService(client)), recorder);
    }

    private static readonly FieldInfo HttpClientField = typeof(LcuClient).GetField(
        "_httpClient",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    /// <summary>An LcuClient whose transport is the recording handler, so no client has to be running.</summary>
    private static LcuClient Client(Recorder recorder)
    {
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose(); // Replaced below by the recording transport.
        HttpClientField.SetValue(client, new HttpClient(recorder) { BaseAddress = new Uri("https://127.0.0.1/") });
        return client;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Recorder(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(reply(request));
        }
    }
}
