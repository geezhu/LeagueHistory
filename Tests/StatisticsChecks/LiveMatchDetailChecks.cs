using League.Clients;
using League.Models;
using League.Parsers;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Opt-in integration check for the paths that only have data after a game: the client's own copy of the match,
/// the history parser reading it, and the history service listing the same game. Everything is a read.
/// Not part of the default suite.
/// Run: dotnet Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll --live-match-detail-checks
/// </summary>
internal static class LiveMatchDetailChecks
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

        using var list = await client.GetAsync(
            $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=3",
            timeout.Token
        );
        if (!list.IsSuccessStatusCode)
        {
            Console.WriteLine("SKIP: the client did not serve the match history list.");
            return;
        }

        var games = JObject.Parse(await list.Content.ReadAsStringAsync(timeout.Token))["games"]?["games"] as JArray;
        if (games == null || games.Count == 0)
        {
            Console.WriteLine("SKIP: the match history list is empty.");
            return;
        }

        long gameId = games[0]["gameId"]?.Value<long>() ?? 0;
        Check(gameId > 0, "The newest history entry names its game");

        var full = await new MatchService(client).GetFullMatchByGameIdAsync(gameId);
        if (full == null)
        {
            Console.WriteLine($"SKIP: the client keeps no local copy of game {gameId}.");
            return;
        }

        Check(full["gameId"]?.Value<long>() == gameId, $"The local copy is the game that was asked for (got {gameId})");
        var participants = full["participants"] as JArray ?? new JArray();
        Check(participants.Count > 1, "The local copy lists the players of the game");
        Check(full["participantIdentities"] != null, "The local copy carries the player identities");

        JObject first = participants.OfType<JObject>().FirstOrDefault() ?? new JObject();
        Console.WriteLine(
            $"live: game {gameId} participants={participants.Count} "
                + $"flatKills={first["kills"] != null} nestedStatsKills={first["stats"]?["kills"] != null} "
                + $"duration={full["gameDuration"]} mode={full["gameMode"]} queue={full["queueId"]} "
                + $"teams={(full["teams"] as JArray)?.Count ?? 0}"
        );

        var parsed = await new MatchDataParser().ParsePlayerMatchInfo(puuid, games, timeout.Token);
        Console.WriteLine(
            $"live: the history parser made {parsed.RecentMatches.Count} rows, "
                + $"{parsed.WinHistory.Count} results and {parsed.MatchItems.Count} list rows from the local list"
        );

        await CrossCheckAsync(client, puuid, gameId, timeout.Token);

        Console.WriteLine(
            "PASS: the client served the finished game, its local copy and the history list, and the parsers read "
                + "them. Nothing was started, sent or typed."
        );
    }

    /// <summary>Asks the history service whether it lists the same game the client keeps locally.</summary>
    private static async Task CrossCheckAsync(LcuClient client, string puuid, long gameId, CancellationToken token)
    {
        var sgp = new SgpSession();
        if (!await sgp.InitSgpAsync(client.HttpClient))
        {
            Console.WriteLine("SKIP: the history service handshake did not complete, so no cross check was made.");
            return;
        }

        JArray? summaries = await sgp.SgpFetchLatestMatches(puuid, 0, 5, null, token);
        if (summaries == null)
        {
            Console.WriteLine("SKIP: the history query did not answer, so no cross check was made.");
            return;
        }

        bool present = summaries
            .OfType<JObject>()
            .Any(summary => ((summary["json"] as JObject) ?? summary)["gameId"]?.Value<long>() == gameId);
        Console.WriteLine($"live: the history service lists {summaries.Count} summaries, this game present={present}");
    }

    private static async Task<string> Read(LcuClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(path, token);
        Check(response.IsSuccessStatusCode, $"The client answers {path} (got {(int)response.StatusCode})");
        return await response.Content.ReadAsStringAsync(token);
    }
}
