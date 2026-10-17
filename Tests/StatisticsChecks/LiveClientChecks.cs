using System.Net;
using League.Clients;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Opt-in integration checks against a <b>running</b> League client. Everything here is a read: no game is started,
/// no chat is sent and no input is typed. When no client is found the whole check reports a skip and exits, so it
/// is safe to run at any time. It is not part of the default suite.
/// Run: dotnet Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll --live-client-checks
/// </summary>
internal static class LiveClientChecks
{
    public static async Task RunAsync()
    {
        if (!LcuProcessDiscovery.TryFind(out string port, out string token))
        {
            Console.WriteLine("SKIP: no running League client was found, so the live client checks were not run.");
            return;
        }

        Console.WriteLine($"live: talking to the client on port {port}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var client = new LcuClient(port, token);

        string puuid = await ClientReadsAsync(client, timeout.Token);
        await HistoryServiceAsync(client, puuid);
    }

    /// <summary>The plain client reads; returns the signed-in player's puuid for the history query.</summary>
    private static async Task<string> ClientReadsAsync(LcuClient client, CancellationToken token)
    {
        string phase = JToken.Parse(await Read(client, "/lol-gameflow/v1/gameflow-phase", token)).ToString();
        Check(phase.Length > 0, "The client reports a gameflow phase");

        JObject summoner = JObject.Parse(await Read(client, "/lol-summoner/v1/current-summoner", token));
        Check(summoner["puuid"]?.ToString() is { Length: > 0 }, "The client reports the signed-in player's identity");
        Check(summoner["gameName"] != null || summoner["displayName"] != null, "The signed-in player has a name");

        foreach (string path in new[] { "/lol-gameflow/v1/session", "/lol-ranked/v1/current-ranked-stats" })
        {
            using var response = await client.GetAsync(path, token);
            Check((int)response.StatusCode is 200 or 404, $"The client answers {path} with data or a plain not-found");
        }

        string puuid = summoner["puuid"]!.ToString();
        JArray? history = null;
        using (
            var response = await client.GetAsync(
                $"/lol-match-history/v1/products/lol/{puuid}/matches?begIndex=0&endIndex=4",
                token
            )
        )
        {
            Check(
                (int)response.StatusCode is 200 or 404,
                "The match history list answers with data or a plain not-found"
            );
            if (response.IsSuccessStatusCode)
            {
                history = JObject.Parse(await response.Content.ReadAsStringAsync(token))["games"]?["games"] as JArray;
            }
        }

        Check(history != null, "The client serves the signed-in player's recent games over LCU");
        Check(
            history!.OfType<JObject>().All(game => game["gameId"] != null || game["json"] != null),
            "Every LCU history entry names its game"
        );
        return puuid;
    }

    /// <summary>The history service handshake and one real summary read.</summary>
    private static async Task HistoryServiceAsync(LcuClient client, string puuid)
    {
        var sgp = new SgpSession();
        if (!await sgp.InitSgpAsync(client.HttpClient))
        {
            Console.WriteLine("SKIP: the history service handshake did not complete, so the SGP checks stopped here.");
            return;
        }

        Check(sgp.DataScope.Length > 0, "The history service reports the address it uses");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        JArray? summaries = await sgp.SgpFetchLatestMatches(puuid, 0, 3, null, timeout.Token);
        if (summaries == null)
        {
            Console.WriteLine("SKIP: the history query did not answer, so the SGP summary shape was not checked.");
            return;
        }

        Check(summaries.Count > 0, "The history service returns recent games for the signed-in player");
        Check(
            summaries.OfType<JObject>().All(summary => Game(summary)?["gameId"] != null),
            "Every history summary carries a game id"
        );
        Check(
            summaries.OfType<JObject>().Any(summary => (Game(summary)?["participants"] as JArray)?.Count > 0),
            "At least one history summary carries its participants"
        );

        Console.WriteLine(
            "PASS: the running client answered the gameflow, summoner, ranked and history reads, and the history "
                + "service returned real summaries. Nothing was started, sent or typed."
        );
    }

    /// <summary>The game document of a summary: SGP wraps it in <c>json</c>, the older LCU shape does not.</summary>
    private static JObject? Game(JObject summary) => (summary["json"] as JObject) ?? summary;

    private static async Task<string> Read(LcuClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(path, token);
        Check(response.StatusCode == HttpStatusCode.OK, $"The client answers {path} (got {(int)response.StatusCode})");
        return await response.Content.ReadAsStringAsync(token);
    }
}
