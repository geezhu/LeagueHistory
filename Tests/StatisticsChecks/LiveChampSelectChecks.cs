using System.Net;
using System.Reflection;
using League.Clients;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Opt-in integration check for a <b>running</b> champion select. Everything here is read-only: it never picks,
/// bans, swaps or changes a skin, so it cannot alter the player's selection. It reports a skip when the client is
/// not in champion select. Not part of the default suite.
/// Run: dotnet Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll --live-champ-select-checks
/// </summary>
internal static class LiveChampSelectChecks
{
    public static async Task RunAsync()
    {
        if (!LcuProcessDiscovery.TryFind(out string port, out string token))
        {
            Console.WriteLine("SKIP: no running League client was found.");
            return;
        }

        var client = new LcuClient(port, token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await client.GetAsync("/lol-champ-select/v1/session", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine("SKIP: the client is not in champion select, so there is nothing to read.");
            return;
        }

        Check(response.IsSuccessStatusCode, "The client serves the champion select session");
        var session = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));

        Check(session["localPlayerCellId"] != null, "The session says which cell is the player's own");
        Check(session["actions"] is JArray, "The session lists the pick and ban actions");
        Check(session["myTeam"] is JArray team && team.Count > 0, "The session lists the player's own team");
        Check(session["queueId"]?.Value<int>() is > 0, "The session names the queue it was created for");

        int queueId = session["queueId"]!.Value<int>();
        Console.WriteLine($"live: champion select queue {queueId}");

        JToken? pick = (JToken?)FindPick.Invoke(null, [session]);
        Console.WriteLine(
            pick == null ? "live: no pending pick action of our own" : $"live: pending pick action {pick["id"]}"
        );

        int current = (int)MyChampion.Invoke(null, [session])!;
        var bench = (HashSet<int>)BenchChampions.Invoke(null, [session])!;
        Console.WriteLine($"live: our champion id is {current}, {bench.Count} champions on the bench");
        Check(bench.All(id => id > 0), "Every bench champion has a real champion id");

        // The same session through the service the assistant uses, and the queue resolution on real game data.
        var parsed = await new GameflowService(client).GetChampSelectSessionAsync(timeout.Token);
        Check(parsed != null && parsed["actions"] != null, "The gameflow service parses the champion select session");

        using var flow = await client.GetAsync("/lol-gameflow/v1/session", timeout.Token);
        if (flow.IsSuccessStatusCode)
        {
            var gameData = JObject.Parse(await flow.Content.ReadAsStringAsync(timeout.Token))["gameData"] as JObject;
            if (gameData != null)
            {
                Check(
                    GameQueues.ForHistory(gameData).Length > 0,
                    "A running champion select resolves to a history queue"
                );
                Console.WriteLine(
                    $"live: gameflow queue id {gameData["queue"]?["id"]} mode {gameData["queue"]?["gameMode"]} "
                        + $"=> statistics queue {GameQueues.Effective(gameData)}, history filter {GameQueues.ForHistory(gameData)}"
                );
                if (gameData["queue"]?["id"]?.Value<int>() is int flowQueue && flowQueue != queueId)
                {
                    Console.WriteLine(
                        $"live: NOTE champion select reports queue {queueId} while gameflow reports {flowQueue}"
                    );
                }
            }
        }

        Console.WriteLine(
            "PASS: the live champion select session was read and matched the shape the assistant and the history "
                + "filter expect. Nothing was picked, banned, swapped or sent."
        );
    }

    private static readonly MethodInfo FindPick = typeof(ChampionSelectService).GetMethod(
        "FindMyPendingPick",
        BindingFlags.Static | BindingFlags.NonPublic
    )!;

    private static readonly MethodInfo MyChampion = typeof(ChampionSelectService).GetMethod(
        "GetMyChampionId",
        BindingFlags.Static | BindingFlags.NonPublic
    )!;

    private static readonly MethodInfo BenchChampions = typeof(ChampionSelectService).GetMethod(
        "GetBenchChampionIds",
        BindingFlags.Static | BindingFlags.NonPublic
    )!;
}
