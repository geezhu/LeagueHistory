using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record LiveMatchItem(int Id, string Name, int Count)
{
    // 2999 reports the item's own price, which for a finished item excludes its components.
    public int Price { get; init; }
}

public sealed record LiveMatchPlayer(
    string Key,
    string Name,
    string ChampionAlias,
    string ChampionName,
    string Team,
    bool Self,
    int Level,
    LiveMatchItem[]? Items
)
{
    public string[] EventAliases { get; init; } = [];
    public string RawChampionName { get; init; } = "";
    public int? Kills { get; init; }
    public int? Deaths { get; init; }
    public int? Assists { get; init; }
    public bool IsBot { get; init; }
    public bool IsDead { get; init; }
    public double RespawnTimer { get; init; }
}

public sealed record LiveMatchSnapshot(string Mode, int Map, double Time, LiveMatchPlayer[] Players)
{
    public LiveMatchEvent[]? Events { get; init; }
    public LiveActivePlayer? Active { get; init; }
    public IReadOnlyDictionary<string, LiveDeathStreak> DeathStreaks { get; init; } =
        new Dictionary<string, LiveDeathStreak>();

    public static LiveMatchSnapshot Parse(JObject data)
    {
        if (data["allPlayers"] is not JArray rows || data["gameData"] is not JObject game)
        {
            throw new InvalidDataException("游戏数据不完整。");
        }
        var active = data["activePlayer"] as JObject;
        string? activeId = (string?)active?["riotId"];
        string? activeName = (string?)active?["summonerName"];
        var players = rows.OfType<JObject>()
            .Select((row, index) => ReadPlayer(row, index, activeId, activeName))
            .ToArray();
        var snapshot = new LiveMatchSnapshot(
            (string?)game["gameMode"] ?? "未知",
            (int?)game["mapNumber"] ?? 0,
            (double?)game["gameTime"] ?? 0,
            players
        )
        {
            Events = ReadEvents(data),
            Active = LiveActivePlayer.Parse(active),
        };
        return snapshot with { DeathStreaks = LiveDeathStreakAnalysis.Calculate(snapshot) };
    }

    private static LiveMatchPlayer ReadPlayer(JObject row, int index, string? activeId, string? activeName)
    {
        string name = (string?)row["riotId"] ?? (string?)row["summonerName"] ?? $"玩家 {index + 1}";
        bool bot = (bool?)row["isBot"] ?? false;
        if (bot && (string.IsNullOrWhiteSpace(name) || name.Trim() == "#"))
        {
            name = (string?)row["summonerName"] ?? $"机器人 {index + 1}";
        }

        bool self = !string.IsNullOrWhiteSpace(activeId)
            ? (string?)row["riotId"] == activeId
            : !string.IsNullOrWhiteSpace(activeName) && (string?)row["summonerName"] == activeName;
        string raw = (string?)row["rawChampionName"] ?? "";
        string alias = LiveChampionIdentity.ReadAlias(raw);
        var items = (row["items"] as JArray)
            ?.OfType<JObject>()
            .Where(i => (int?)i["itemID"] > 0)
            .Select(i => new LiveMatchItem(
                (int)i["itemID"]!,
                (string?)i["displayName"] ?? "未知装备",
                Math.Max(1, (int?)i["count"] ?? 1)
            )
            {
                Price = Math.Max(0, (int?)i["price"] ?? 0),
            })
            .ToArray();
        static int? Score(JToken? value) =>
            value?.Type == JTokenType.Integer && int.TryParse(value.ToString(), out int count) && count >= 0
                ? count
                : null;
        string? riot = (string?)row["riotId"];
        string? gameName = (string?)row["riotIdGameName"];
        if (string.IsNullOrWhiteSpace(gameName) && riot?.IndexOf('#') is > 0)
        {
            gameName = riot[..riot.IndexOf('#')];
        }

        return new LiveMatchPlayer(
            name + "|" + index,
            name,
            alias,
            (string?)row["championName"] ?? alias,
            (string?)row["team"] ?? "UNKNOWN",
            self,
            (int?)row["level"] ?? 0,
            items
        )
        {
            EventAliases = new[] { riot, (string?)row["summonerName"], gameName }
                .Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim() != "#")
                .Select(n => n!)
                .Distinct()
                .ToArray(),
            Kills = Score(row["scores"]?["kills"]),
            Deaths = Score(row["scores"]?["deaths"]),
            Assists = Score(row["scores"]?["assists"]),
            IsBot = bot,
            IsDead = (bool?)row["isDead"] ?? false,
            RespawnTimer = Math.Max(0, (double?)row["respawnTimer"] ?? 0),
            RawChampionName = raw,
        };
    }

    private static LiveMatchEvent[]? ReadEvents(JObject data)
    {
        var events = new List<LiveMatchEvent>();
        bool valid = data["events"]?["Events"] is JArray array && array.All(value => value is JObject);
        foreach (var row in (data["events"]?["Events"] as JArray ?? new()).OfType<JObject>())
        {
            string eventName = (string?)row["EventName"] ?? "";
            if (eventName is not ("ChampionKill" or "GameStart" or "Multikill" or "Ace" or "GameEnd"))
            {
                continue;
            }

            if (
                !double.TryParse(
                    row["EventTime"]?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double time
                )
                || !double.IsFinite(time)
                || time < 0
                || eventName == "ChampionKill" && string.IsNullOrWhiteSpace((string?)row["VictimName"])
            )
            {
                valid = false;
                continue;
            }
            long? eventId =
                row["EventID"]?.Type == JTokenType.Integer
                && long.TryParse(row["EventID"]!.ToString(), out long id)
                && id >= 0
                    ? id
                    : null;
            events.Add(
                new(eventId, eventName, time, (string?)row["KillerName"] ?? "", (string?)row["VictimName"] ?? "")
                {
                    Assisters = (row["Assisters"] as JArray ?? new())
                        .Values<string>()
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!)
                        .ToArray(),
                    Streak = (int?)row["KillStreak"] ?? 0,
                    Team = (string?)row["AcingTeam"] ?? "",
                    Result = (string?)row["Result"] ?? "",
                }
            );
        }
        return valid ? events.ToArray() : null;
    }

    /// <summary>Event names that identify exactly one player; duplicated names are left out rather than guessed.</summary>
    public IReadOnlyDictionary<string, string> EventAliasMap() =>
        Players
            .Where(p => p.Name.Trim() != "#")
            .SelectMany(p => p.EventAliases.Distinct().Select(name => (name, p.Key)))
            .Where(p => !string.IsNullOrWhiteSpace(p.name) && p.name.Trim() != "#")
            .GroupBy(p => p.name, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Key).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

    public static int TeamId(string team) =>
        team switch
        {
            "ORDER" => 100,
            "CHAOS" => 200,
            _ => 0,
        };

    /// <summary>The live row for a client roster entry, when name and team identify exactly one.</summary>
    public LiveMatchPlayer? Find(LoadingPlayer player)
    {
        if (player.IdentityHidden)
        {
            return null;
        }

        var matches = Players.Where(l => l.Name == player.Name && TeamId(l.Team) == player.Team).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public string TeamLabel(LiveMatchPlayer player)
    {
        string? own = Players.FirstOrDefault(p => p.Self)?.Team;
        return own == null || own == "UNKNOWN" || player.Team == "UNKNOWN" ? player.Team
            : player.Team == own ? "我方"
            : "对方";
    }
}
