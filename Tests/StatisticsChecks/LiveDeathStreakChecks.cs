using League.Models;
using Newtonsoft.Json.Linq;

internal static class LiveDeathStreakChecks
{
    private static void Check(bool value, string note)
    {
        if (!value)
            throw new Exception(note);
    }

    public static JObject Fixture()
    {
        JObject Player(string name, string team, int kills, int deaths) =>
            new()
            {
                ["riotId"] = name + "#CN",
                ["summonerName"] = "legacy-" + name,
                ["team"] = team,
                ["rawChampionName"] = "game_character_displayname_Lux",
                ["championName"] = "拉克丝",
                ["level"] = 12,
                ["items"] = new JArray(),
                ["scores"] = new JObject { ["kills"] = kills, ["deaths"] = deaths },
            };
        JObject Kill(int id, string killer, string victim) =>
            new()
            {
                ["EventID"] = id,
                ["EventName"] = "ChampionKill",
                ["EventTime"] = id * 10,
                ["KillerName"] = killer,
                ["VictimName"] = victim,
            };
        var assist = Kill(3, "legacy-B", "legacy-C");
        assist["Assisters"] = new JArray("legacy-A");
        var events = new JArray(
            new JObject
            {
                ["EventID"] = 0,
                ["EventName"] = "GameStart",
                ["EventTime"] = 0,
            },
            Kill(1, "legacy-B", "legacy-A"),
            Kill(2, "Turret_T1", "legacy-A"),
            assist,
            Kill(4, "legacy-B", "legacy-A"),
            Kill(5, "legacy-A", "legacy-B"),
            Kill(6, "legacy-B", "legacy-A"),
            Kill(7, "legacy-A", "legacy-C"),
            Kill(8, "legacy-B", "legacy-A"),
            Kill(9, "legacy-B", "legacy-A")
        );
        return new JObject
        {
            ["activePlayer"] = new JObject { ["riotId"] = "A#CN" },
            ["gameData"] = new JObject
            {
                ["gameMode"] = "ARAM",
                ["mapNumber"] = 12,
                ["gameTime"] = 120,
            },
            ["allPlayers"] = new JArray(
                Player("A", "ORDER", 2, 6),
                Player("B", "CHAOS", 6, 1),
                Player("C", "CHAOS", 0, 2)
            ),
            ["events"] = new JObject { ["Events"] = events },
        };
    }

    private static LiveDeathStreak Streak(LiveMatchSnapshot snapshot, int index) =>
        snapshot.DeathStreaks[snapshot.Players[index].Key];

    public static async Task RunLiveAsync()
    {
        using var client = new League.Clients.LiveMatchClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            var snapshot = await client.GetAsync(timeout.Token);
            Console.WriteLine(
                $"Live players={snapshot.Players.Length}; kill events={snapshot.Events?.Count(e => e.Name == "ChampionKill")}; game time={snapshot.Time:F1}"
            );
            Console.WriteLine(
                $"Death streaks complete={snapshot.DeathStreaks.Values.Count(s => s.Complete)}; recorded={snapshot.DeathStreaks.Values.Count(s => s.Current.HasValue && !s.Complete)}; unknown={snapshot.DeathStreaks.Values.Count(s => !s.Current.HasValue)}"
            );
            foreach (
                var group in snapshot.DeathStreaks.Values.Where(s => !s.Current.HasValue).GroupBy(s => s.Description)
            )
                Console.WriteLine($"Unknown reason: {group.Key} count={group.Count()}");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine("2999 is not available; live verification needs an active game.");
        }
    }

    public static void Run()
    {
        var data = Fixture();
        var snapshot = LiveMatchSnapshot.Parse(data);
        Check(
            Streak(snapshot, 0) is { Current: 2, Highest: 3, Complete: true },
            "Deaths accumulate, a kill resets current but not maximum, and assists do not reset"
        );
        Check(
            Streak(snapshot, 1) is { Current: 0, Highest: 1, Complete: true }
                && Streak(snapshot, 2) is { Current: 2, Highest: 2, Complete: true },
            "Same-champion players retain their own counters"
        );
        var bare = (JObject)data.DeepClone();
        foreach (var ev in ((JArray)bare["events"]!["Events"]!).OfType<JObject>())
        foreach (string field in new[] { "KillerName", "VictimName" })
            if (((string?)ev[field])?.StartsWith("legacy-") == true)
                ev[field] = ((string)ev[field]!)[7..];
        Check(
            Streak(LiveMatchSnapshot.Parse(bare), 0) is { Current: 2, Highest: 3, Complete: true },
            "Actual events using bare Riot game names match unambiguously"
        );
        var bareDuplicate = (JObject)bare.DeepClone();
        bareDuplicate["allPlayers"]![2]!["riotId"] = "A#OTHER";
        Check(
            Streak(LiveMatchSnapshot.Parse(bareDuplicate), 0).Current == null,
            "Equal game names with different tags remain ambiguous for untagged events"
        );
        var bot = (JObject)data.DeepClone();
        bot["allPlayers"]![0]!["riotId"] = "#";
        bot["allPlayers"]![0]!["isBot"] = true;
        Check(
            Streak(LiveMatchSnapshot.Parse(bot), 0) is { Current: 2, Highest: 3, Complete: true },
            "Bots without Riot IDs use their explicit unique summoner names, rather than being treated as hidden humans"
        );
        for (int i = 0; i < 10; i++)
            Check(
                Streak(LiveMatchSnapshot.Parse(data), 0).Current == 2,
                "Repeated polling must not accumulate the same events"
            );
        var duplicate = (JObject)data.DeepClone();
        ((JArray)duplicate["events"]!["Events"]!).Add(duplicate["events"]!["Events"]![9]!.DeepClone());
        Check(Streak(LiveMatchSnapshot.Parse(duplicate), 0).Current == 2, "Repeated events are deduplicated");
        var reordered = (JObject)data.DeepClone();
        reordered["events"]!["Events"] = new JArray(((JArray)reordered["events"]!["Events"]!).Reverse());
        Check(
            Streak(LiveMatchSnapshot.Parse(reordered), 0) == Streak(snapshot, 0),
            "Events are ordered by game time before reconstruction"
        );
        var selfDeath = (JObject)data.DeepClone();
        ((JArray)selfDeath["events"]!["Events"]!).Add(
            new JObject
            {
                ["EventID"] = 10,
                ["EventName"] = "ChampionKill",
                ["EventTime"] = 100,
                ["KillerName"] = "legacy-A",
                ["VictimName"] = "legacy-A",
            }
        );
        selfDeath["allPlayers"]![0]!["scores"]!["deaths"] = 7;
        Check(
            Streak(LiveMatchSnapshot.Parse(selfDeath), 0) is { Current: 3, Highest: 3 },
            "Self and environment deaths count without being mistaken for a reset kill"
        );
        selfDeath["events"]!["Events"]![10]!["VictimName"] = "A#CN";
        Check(
            Streak(LiveMatchSnapshot.Parse(selfDeath), 0) is { Current: 3, Highest: 3 },
            "Legacy and Riot-ID aliases of the same player cannot turn self-death into a kill reset"
        );
        var partial = (JObject)data.DeepClone();
        partial["events"]!["Events"] = new JArray(
            partial["events"]!["Events"]![8]!.DeepClone(),
            partial["events"]!["Events"]![9]!.DeepClone()
        );
        Check(
            Streak(LiveMatchSnapshot.Parse(partial), 0) is { Current: 2, Complete: false }
                && Streak(LiveMatchSnapshot.Parse(partial), 0).Text.EndsWith('*'),
            "Truncated history is explicitly marked as recorded events"
        );
        var missing = (JObject)data.DeepClone();
        missing.Remove("events");
        Check(
            Streak(LiveMatchSnapshot.Parse(missing), 0).Current == null,
            "No event history is unknown, never a fabricated zero"
        );
        var noPlayer = (JObject)data.DeepClone();
        noPlayer["events"]!["Events"] = new JArray(new JObject { ["EventName"] = "GameStart", ["EventTime"] = 0 });
        Check(
            Streak(LiveMatchSnapshot.Parse(noPlayer), 0).Current == null,
            "Missing a player's known deaths cannot be reported as zero"
        );
        var noStart = (JObject)data.DeepClone();
        ((JArray)noStart["events"]!["Events"]!).RemoveAt(0);
        Check(
            Streak(LiveMatchSnapshot.Parse(noStart), 0).Complete,
            "Matching kill/death totals can verify complete per-player history without the start event"
        );
        var hidden = (JObject)data.DeepClone();
        hidden["allPlayers"]![0]!["riotId"] = "#";
        Check(
            Streak(LiveMatchSnapshot.Parse(hidden), 0).Current == null,
            "A hidden player cannot be mapped by champion or by a masked name"
        );
        var ambiguous = (JObject)data.DeepClone();
        ambiguous["allPlayers"]![2]!["summonerName"] = "legacy-A";
        Check(
            Streak(LiveMatchSnapshot.Parse(ambiguous), 0).Current == null,
            "Ambiguous event names are not assigned to an arbitrary player"
        );
        var malformed = (JObject)data.DeepClone();
        malformed["events"]!["Events"]![1]!["EventTime"] = -1;
        Check(
            Streak(LiveMatchSnapshot.Parse(malformed), 0).Current == null,
            "Malformed kill history leaves the roster usable but statistics unavailable"
        );
        var lag = (JObject)data.DeepClone();
        lag["allPlayers"]![0]!["scores"]!["deaths"] = 5;
        Check(
            Streak(LiveMatchSnapshot.Parse(lag), 0).Current == null,
            "A scoreboard behind the events waits for synchronization"
        );
        var next = Fixture();
        next["gameData"]!["gameTime"] = 1;
        next["events"]!["Events"] = new JArray(
            new JObject
            {
                ["EventID"] = 0,
                ["EventName"] = "GameStart",
                ["EventTime"] = 0,
            }
        );
        foreach (var player in (JArray)next["allPlayers"]!)
        {
            player["scores"]!["kills"] = 0;
            player["scores"]!["deaths"] = 0;
        }
        Check(
            Streak(LiveMatchSnapshot.Parse(next), 0) is { Current: 0, Highest: 0, Complete: true },
            "A new match resets both current and highest without retained state"
        );
        var roster = new LoadingRoster(
            "game",
            450,
            "ARAM",
            [
                new("account-A", "A#CN", "private", 1, 99, 100, true),
                new("account-B", "B#CN", "private", 2, 99, 200, false),
                new("hidden", "隐藏玩家", "", 0, 99, 200, false, true),
            ]
        );
        var mapped = LiveDeathStreakAnalysis.ForRoster(roster, snapshot);
        Check(
            mapped["account-A"].Current == 2 && mapped["account-B"].Current == 0 && mapped["hidden"].Current == null,
            "Live events map to the confirmed roster names and sides, with hidden slots unknown"
        );
        Check(
            LiveDeathStreakAnalysis
                .ForRoster(roster with { Players = [roster.Players[0] with { Team = 200 }] }, snapshot)["account-A"]
                .Current == null,
            "A mismatched team cannot reuse another roster's event statistics"
        );
        Console.WriteLine(
            "PASS: death streak reset, assists, execution, highest, late-open recovery, duplicates, sorting, partial history, hidden identities, score verification and new-match reset."
        );
    }
}
