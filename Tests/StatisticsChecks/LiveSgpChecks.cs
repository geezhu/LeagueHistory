using League.Clients;
using League.Controls;
using League.Models;
using League.Parsers;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Opt-in integration check for the history parsers against <b>real</b> data: the SGP summary the client serves
/// for the signed-in player, and the full game the client keeps locally. Everything is a read; nothing is started,
/// sent or typed. It reports a skip when no client is running or the history service is unavailable.
/// Not part of the default suite.
/// Run: dotnet Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll --live-sgp-checks
/// </summary>
internal static class LiveSgpChecks
{
    public static async Task RunAsync()
    {
        if (!LcuProcessDiscovery.TryFind(out string port, out string token))
        {
            Console.WriteLine("SKIP: no running League client was found.");
            return;
        }

        var client = new LcuClient(port, token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        JObject summoner = JObject.Parse(await Read(client, "/lol-summoner/v1/current-summoner", timeout.Token));
        string puuid = summoner["puuid"]?.ToString() ?? "";
        if (puuid.Length == 0)
        {
            Console.WriteLine("SKIP: the client reports no signed-in player.");
            return;
        }

        var sgp = new SgpSession();
        if (!await sgp.InitSgpAsync(client.HttpClient))
        {
            Console.WriteLine("SKIP: the history service handshake did not complete.");
            return;
        }

        JArray? summaries = await sgp.SgpFetchLatestMatches(puuid, 0, 5, null, timeout.Token);
        if (summaries == null || summaries.Count == 0)
        {
            Console.WriteLine("SKIP: the history service returned no summaries to parse.");
            return;
        }

        var games = summaries.OfType<JObject>().ToArray();
        Check(games.All(summary => Game(summary)?["gameId"] != null), "Every history summary names its game");
        foreach (JObject summary in games)
        {
            JObject? document = Game(summary);
            Console.WriteLine(
                $"live: game {document?["gameId"]} with "
                    + $"{(document?["participants"] as JArray)?.Count ?? 0} participants"
            );
        }

        // The newest games are often remakes, which carry a single participant; parse the richest one so the
        // team and per-player paths are actually exercised.
        JObject richest = games
            .OrderByDescending(summary => (Game(summary)?["participants"] as JArray)?.Count ?? 0)
            .First();
        await ParseSummaryAsync(richest, puuid);
        await ParseLocalGameAsync(client, puuid, timeout.Token);

        Console.WriteLine(
            "PASS: real history data was parsed by the same code the app uses. Nothing was started, sent or typed."
        );
    }

    /// <summary>One real summary through the history parser, asserted on what must always be there.</summary>
    private static async Task ParseSummaryAsync(JObject summary, string puuid)
    {
        JObject game = (JObject)(Game(summary) ?? summary).DeepClone();
        // The parser empties the document it is given, so count and locate before it runs.
        JArray participants = game["participants"] as JArray ?? new JArray();
        int sent = participants.Count;
        JObject self =
            participants.OfType<JObject>().FirstOrDefault(p => p["puuid"]?.ToString() == puuid) ?? new JObject();
        string summonerId = self["summonerId"]?.ToString() ?? "";
        string name = self["riotIdGameName"]?.ToString() ?? "玩家";
        string tag = self["riotIdTagline"]?.ToString() ?? "0001";
        long expectedGameId = game["gameId"]?.Value<long>() ?? 0;

        var panel = (MatchListPanel)
            await new MatchParserSgp().ParseGameToPanelFromSgpAsync(game, summonerId, $"{name}#{tag}", tag, 1);
        MatchInfo info = panel.MatchInfo;

        Check(expectedGameId > 0, "The history summary carries a real game id");
        Check(info.GameId == expectedGameId, $"The parsed row keeps the game id (got {info.GameId})");
        Check(info.CurrPuuid == puuid, "The parsed row belongs to the signed-in player");
        Check(info.Mode.Length > 0, "The parsed row names a mode");
        Check(!string.IsNullOrWhiteSpace(info.ResultText), "The parsed row states a result");
        Check(info.BlueTeam.Count + info.RedTeam.Count > 0, "The parsed row lists the players of the game");

        int listed = info.BlueTeam.Count + info.RedTeam.Count;
        Console.WriteLine(
            $"live: summary row mode={info.Mode} queue={info.QueueId} result={info.ResultText} "
                + $"duration={info.DurationText} kda={info.Kills}/{info.Deaths}/{info.Assists} "
                + $"damage={info.DamageText} gold={info.GoldText} kp={info.KPPercentage} "
                + $"players={info.BlueTeam.Count}+{info.RedTeam.Count} of {sent} sent"
        );
        Check(listed == sent, $"Every participant of the summary reaches a team list (listed {listed} of {sent})");
    }

    /// <summary>
    /// The full game the client keeps for the newest game, through the same parser. The local copy nests the
    /// player's numbers under <c>stats</c>, unlike the summary, so the check reports what the parser made of it
    /// instead of demanding the summary's shape.
    /// </summary>
    private static async Task ParseLocalGameAsync(LcuClient client, string puuid, CancellationToken token)
    {
        using var history = await client.GetAsync(
            $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=3",
            token
        );
        if (!history.IsSuccessStatusCode)
        {
            Console.WriteLine("SKIP: the client did not serve the local match history list.");
            return;
        }

        var games = JObject.Parse(await history.Content.ReadAsStringAsync(token))["games"]?["games"] as JArray;
        long gameId = games?.OfType<JObject>().FirstOrDefault()?["gameId"]?.Value<long>() ?? 0;
        if (gameId == 0)
        {
            Console.WriteLine("SKIP: the local match history list is empty.");
            return;
        }

        var full = await new MatchService(client).GetFullMatchByGameIdAsync(gameId);
        if (full == null)
        {
            Console.WriteLine($"SKIP: the client has no local copy of game {gameId}.");
            return;
        }

        JObject game = (JObject)full.DeepClone();
        JObject self =
            (game["participants"] as JArray)?.OfType<JObject>().FirstOrDefault(p => p["puuid"]?.ToString() == puuid)
            ?? new JObject();
        var parsed = await new MatchParserSgp().ParseGameToPanelFromSgpAsync(
            game,
            self["summonerId"]?.ToString() ?? "",
            $"{self["riotIdGameName"]?.ToString() ?? "玩家"}#{self["riotIdTagline"]?.ToString() ?? "0001"}",
            self["riotIdTagline"]?.ToString() ?? "0001",
            1
        );
        if (parsed is not MatchListPanel panel)
        {
            Console.WriteLine(
                "SKIP: the parser declined the locally kept game. Its shape puts the player's numbers under "
                    + "stats and the identity under participantIdentities, which the SGP summary reader does not "
                    + "read, so the app never feeds it this document."
            );
            return;
        }

        MatchInfo info = panel.MatchInfo;
        Check(info.GameId == gameId, $"The parsed local game keeps its id (got {info.GameId})");
        Check(info.BlueTeam.Count + info.RedTeam.Count > 0, "The parser finds the players in a locally kept game too");

        Console.WriteLine(
            $"live: local game {gameId} kda={info.Kills}/{info.Deaths}/{info.Assists} "
                + $"items={info.Items.Length} runes={info.PrimaryRunes.Length} spells={info.SummonerSpells.Length} "
                + $"augments={info.Augments.Length} mode={info.Mode}"
        );
    }

    /// <summary>The game document of a summary: SGP wraps it in <c>json</c>, the older shape does not.</summary>
    private static JObject? Game(JObject summary) => (summary["json"] as JObject) ?? summary;

    private static async Task<string> Read(LcuClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(path, token);
        Check(response.IsSuccessStatusCode, $"The client answers {path} (got {(int)response.StatusCode})");
        return await response.Content.ReadAsStringAsync(token);
    }
}
