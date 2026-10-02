using League.Clients;
using Newtonsoft.Json.Linq;

internal static class MasteryLiveDiagnostics
{
    public static async Task Run()
    {
        if (!LcuProcessDiscovery.TryFind(out string port, out string token))
        { Console.WriteLine("Client connection not readable."); return; }
        var lcu = new LcuClient(port, token);
        using var client = lcu.HttpClient;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var api = new PlayerStatisticsService(() => client);
        var phase = await api.GetAsync("/lol-gameflow/v1/gameflow-phase", timeout.Token);
        var flow = await api.GetAsync("/lol-gameflow/v1/session", timeout.Token);
        Console.WriteLine($"Phase: {phase.Data}; flow HTTP {flow.Status}");
        var game = flow.Data?["gameData"];
        if (game == null) return;
        foreach (string team in new[] { "teamOne", "teamTwo", "playerChampionSelections" })
        {
            int index = 0;
            foreach (var row in (game[team] as JArray ?? new()).OfType<JObject>())
            {
                string puuid = (string?)row["puuid"] ?? "";
                Console.WriteLine($"{team}[{index++}]: fields={string.Join(',', row.Properties().Select(p => p.Name))}; puuid={puuid.Length > 0}; zero={puuid == Guid.Empty.ToString()}; id={(long?)row["summonerId"] > 0}; name={!string.IsNullOrWhiteSpace((string?)row["gameName"])}; tag={!string.IsNullOrWhiteSpace((string?)row["tagLine"])}; champion={row["championId"]}");
            }
        }
        var current = await api.GetAsync("/lol-summoner/v1/current-summoner", timeout.Token);
        using var liveClient = new LiveMatchClient();
        var live = await liveClient.GetAsync(timeout.Token);
        var rows = new[] { "teamOne", "teamTwo" }.SelectMany(k => (game[k] as JArray ?? new()).OfType<JObject>()).ToArray();
        Console.WriteLine($"Live players={live.Players.Length}; exact roster name matches={live.Players.Count(p => rows.Any(r => (string?)r["summonerName"] == p.Name))}");
        var before = League.Models.LoadingRoster.Parse((JObject)flow.Data!, (string?)current.Data?["puuid"] ?? "");
        var merged = await new LoadingRosterIdentityService(() => client).MergeAsync(before, (JObject)flow.Data!,
            (string?)current.Data?["puuid"] ?? "", live, _ => 1, timeout.Token);
        Console.WriteLine($"Merged players={merged.Players.Length}; confirmed PUUIDs={merged.Players.Count(p => LoadingRosterIdentityService.HasPuuid(p.Puuid))}");
        int available = 0, unavailable = 0;
        foreach (var player in merged.Players.Where(p => !p.IdentityHidden && LoadingRosterIdentityService.HasPuuid(p.Puuid)))
        {
            var mastery = await api.GetAsync("/lol-champion-mastery/v1/" + Uri.EscapeDataString(player.Puuid) + "/champion-mastery", timeout.Token);
            if (mastery.Data is JArray) available++; else unavailable++;
        }
        Console.WriteLine($"Mastery lists available={available}; unavailable={unavailable}; hidden skipped={merged.Players.Count(p => p.IdentityHidden)}");
    }
}
