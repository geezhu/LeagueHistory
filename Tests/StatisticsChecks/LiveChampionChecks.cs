using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class LiveChampionChecks
{
    private static void Check(bool value, string note)
    {
        if (!value)
        {
            throw new Exception(note);
        }
    }

    private static readonly Champion[] Heroes =
    [
        new()
        {
            Id = 147,
            Alias = "Seraphine",
            Name = "星籁歌姬",
        },
        new()
        {
            Id = 25,
            Alias = "Morgana",
            Name = "堕落天使",
        },
    ];

    private static LiveMatchSnapshot Sample(string raw, string name = "星籁歌姬") =>
        LiveMatchSnapshot.Parse(
            new JObject
            {
                ["activePlayer"] = new JObject { ["riotId"] = "Self#CN" },
                ["gameData"] = new JObject
                {
                    ["gameMode"] = "ARAM",
                    ["mapNumber"] = 12,
                    ["gameTime"] = 100,
                },
                ["allPlayers"] = new JArray(
                    new JObject
                    {
                        ["riotId"] = "Self#CN",
                        ["championName"] = name,
                        ["rawChampionName"] = raw,
                        ["team"] = "ORDER",
                        ["items"] = new JArray(),
                    }
                ),
            }
        );

    public static void Run()
    {
        RawChampionKeys();
        ExactResourceNames();
        var roster = RosterFallback();
        Task.Run(() => CheckBridge(roster)).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: Seraphine raw formats, exact name fallback, LCU pick retention, ambiguity, hidden identities, Game Bar detail selection and tier chat. No game input sent."
        );
    }

    private static void RawChampionKeys()
    {
        foreach (
            string raw in new[]
            {
                "game_character_displayname_Seraphine",
                "Character_Seraphine_Name",
                "character_seraphine_name",
                " Character_Seraphine_Name ",
            }
        )
        {
            var player = Sample(raw, "萨勒芬妮").Players.Single();
            Check(
                player.ChampionAlias.Equals("Seraphine", StringComparison.OrdinalIgnoreCase)
                    && LiveChampionIdentity.Resolve(player, Heroes)?.Id == 147,
                "Both raw formats resolve the correct hero even with a different localized name"
            );
            Check(player.RawChampionName == raw, "Original hero key remains available for bounded diagnostics");
        }
        Check(
            LiveChampionIdentity.ReadAlias("Character_MonkeyKing_Name") == "MonkeyKing",
            "Internal multiword aliases are preserved"
        );
        foreach (
            string raw in new[]
            {
                "Character__Name",
                "Character_Seraphine",
                "Seraphine_Name",
                "game_character_displayname_",
                "Character_Seraphine_Name_extra",
                "Character_Seraphine\nMorgana_Name",
            }
        )
        {
            Check(LiveChampionIdentity.ReadAlias(raw) == "", "Malformed keys must not use substring guesses");
        }
    }

    private static void ExactResourceNames()
    {
        Check(
            LiveChampionIdentity.Resolve(Sample("changed-key").Players.Single(), Heroes)?.Id == 147,
            "Exact localized resource names recover omitted or changed keys"
        );
        Check(
            LiveChampionIdentity.Resolve(Sample("", "Seraphine").Players.Single(), Heroes)?.Id == 147,
            "Exact English display names are supported"
        );
        Check(
            LiveChampionIdentity.Resolve(Sample("", "星籁").Players.Single(), Heroes) == null,
            "Partial names never guess a hero"
        );
        Check(
            LiveChampionIdentity.Resolve(
                Sample("", "星籁歌姬").Players.Single(),
                Heroes.Append(
                    new Champion
                    {
                        Id = 999,
                        Alias = "Other",
                        Name = "星籁歌姬",
                    }
                )
            ) == null,
            "Ambiguous resource names remain unknown"
        );
    }

    private static LoadingRoster RosterFallback()
    {
        var roster = new LoadingRoster(
            "match",
            450,
            "ARAM",
            [new("account", "Self#CN", "private-id", 1, 147, 100, true)]
        );
        int Resolve(LiveMatchPlayer player) => LiveChampionIdentity.Resolve(player, Heroes)?.Id ?? 0;
        var live = Sample("Character_Seraphine_Name");
        var merged = roster.FromLive(live, Resolve);
        Check(
            merged.Players.Single() is { ChampionId: 147, Puuid: "private-id" },
            "Live name mapping retains the exact LCU identity and correct champion ID"
        );
        var missing = Sample("unknown", "unrecognized hero");
        Check(
            roster.FromLive(missing, _ => 0).Players.Single().ChampionId == 147,
            "Temporary missing resource keys cannot erase a confirmed LCU pick"
        );
        InvalidRosterFallbacks(roster, live, missing);
        return merged;
    }

    private static void InvalidRosterFallbacks(LoadingRoster roster, LiveMatchSnapshot live, LiveMatchSnapshot missing)
    {
        int Resolve(LiveMatchPlayer player) => LiveChampionIdentity.Resolve(player, Heroes)?.Id ?? 0;
        Check(
            (roster with { Players = [roster.Players[0] with { Team = 200 }] })
                .FromLive(missing, _ => 0)
                .Players.Single()
                .ChampionId == 0,
            "LCU fallback cannot cross teams"
        );
        Check(
            (roster with { Players = [roster.Players[0] with { Name = "Other#CN" }] })
                .FromLive(missing, _ => 0)
                .Players.Single()
                is { ChampionId: 0, Puuid: "" },
            "A different player cannot inherit a pick by roster position"
        );
        Check(
            (roster with { Players = [roster.Players[0], roster.Players[0] with { Key = "duplicate" }] })
                .FromLive(missing, _ => 0)
                .Players.Single()
                .ChampionId == 0,
            "Ambiguous account matching cannot supply a fallback pick"
        );
        Check(
            (roster with { Players = [roster.Players[0] with { Puuid = "", SummonerId = 0 }] })
                .FromLive(missing, _ => 0)
                .Players.Single()
                .ChampionId == 0,
            "An unconfirmed account cannot supply a fallback pick"
        );
        Check(
            (roster with { Players = [roster.Players[0] with { ChampionId = 25 }] })
                .FromLive(live, Resolve)
                .Players.Single()
                .ChampionId == 147,
            "Recognized live champion takes precedence over an outdated LCU pick"
        );
        var hidden = missing with { Players = [missing.Players[0] with { Name = "#" }] };
        Check(
            roster.FromLive(hidden, _ => 0).Players.Single() is { ChampionId: 0, IdentityHidden: true, Puuid: "" },
            "Hidden identities cannot inherit another player's pick"
        );
    }

    private static async Task CheckBridge(LoadingRoster roster)
    {
        string folder = Path.Combine(Path.GetTempPath(), "League-live-champion-" + Guid.NewGuid().ToString("N"));
        var bridge = new GameBarBridge(folder);
        try
        {
            bridge.Publish(
                roster,
                new Dictionary<string, PlayerChampionTier>
                {
                    { "account", new("T1", "胜率 53%", "test", 450, "none") },
                },
                new Dictionary<int, Image>(),
                id => Heroes.Single(c => c.Id == id).Name,
                _ => "cached",
                "match",
                "test",
                true,
                true,
                true,
                new Dictionary<string, PlayerChampionMastery> { { "account", new(147, 12, 100000, null, true) } }
            );
            string file = Path.Combine(folder, "snapshot.json");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(file) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
            Check(File.Exists(file), "Game Bar snapshot is published");
            var snapshot = JObject.Parse(await File.ReadAllTextAsync(file));
            Check(
                (int?)snapshot["players"]![0]!["championId"] == 147
                    && (string?)snapshot["players"]![0]!["champion"] == "星籁歌姬"
                    && ((string?)snapshot["players"]![0]!["mastery"])?.Contains("12级") == true,
                "Game Bar receives a real champion ID and mastery rather than hero pending"
            );
            var request = new JObject
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString(),
                ["session"] = snapshot["session"],
                ["slot"] = 0,
                ["championId"] = 147,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "detail-request.json"), request.ToString());
            Check(
                (await bridge.ReadDetailRequestAsync())?.ChampionId == 147,
                "The recovered Seraphine card can request details through the existing protocol"
            );
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    public static async Task RunLiveAsync()
    {
        if (!LcuProcessDiscovery.TryFind(out string port, out string token))
        {
            Console.WriteLine("LCU connection not readable.");
            return;
        }
        using var client = new LcuClient(port, token).HttpClient;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var catalog =
            JsonConvert.DeserializeObject<Champion[]>(
                await client.GetStringAsync("/lol-game-data/assets/v1/champion-summary.json", timeout.Token)
            ) ?? [];
        var probe = Sample("Character_Seraphine_Name", "萨勒芬妮").Players.Single();
        Check(
            LiveChampionIdentity.Resolve(probe, catalog)?.Id == 147,
            "Seraphine's alternate key resolves against the actual client's current resource catalog"
        );
        Console.WriteLine($"LCU champion catalog={catalog.Length}; alternate Seraphine key resolved=147.");
        using var liveClient = new LiveMatchClient();
        var live = await liveClient.GetAsync(timeout.Token);
        Console.WriteLine(
            $"Live players={live.Players.Length}; recognized={live.Players.Count(p => LiveChampionIdentity.Resolve(p, catalog) != null)}; Seraphine in current game={live.Players.Any(p => LiveChampionIdentity.Resolve(p, catalog)?.Id == 147)}."
        );
    }
}
