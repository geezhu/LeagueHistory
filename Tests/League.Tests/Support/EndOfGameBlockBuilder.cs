using Newtonsoft.Json.Linq;

namespace League.Tests.Support;

/// <summary>
/// The client's end-of-game stats block (/lol-end-of-game/v1/eog-stats-block) for a five-against-five game: the
/// local player first on team 100, then four teammates named 队友1 to 队友4, then team 200. Every player carries the
/// stats the score estimate reads, each a little higher than the player before, so every game has one best player.
/// </summary>
internal sealed class EndOfGameBlockBuilder
{
    public const string LocalPuuid = "local-puuid";
    public const long GameId = 7_000_000_001;

    private readonly Dictionary<int, string?> _names = new();
    private readonly HashSet<string> _missingStats = new();
    private readonly Dictionary<(int Player, string Stat), int> _stats = new();
    private bool _win = true;
    private int? _queueId;
    private int _gameLength = 960;

    public EndOfGameBlockBuilder Lost()
    {
        _win = false;
        return this;
    }

    public EndOfGameBlockBuilder WithQueue(int queueId)
    {
        _queueId = queueId;
        return this;
    }

    public EndOfGameBlockBuilder WithGameLength(int seconds)
    {
        _gameLength = seconds;
        return this;
    }

    /// <summary>Renames teammate 1 to 4; null leaves the name out, as for a hidden player.</summary>
    public EndOfGameBlockBuilder WithTeammateName(int teammate, string? name)
    {
        _names[teammate] = name;
        return this;
    }

    /// <summary>Sets one stat of one player: 0 is the local player, 1 to 4 the teammates, 5 to 9 the enemies.</summary>
    public EndOfGameBlockBuilder WithStat(int player, string stat, int value)
    {
        _stats[(player, stat)] = value;
        return this;
    }

    /// <summary>Leaves one stat out of every player, as an older client would.</summary>
    public EndOfGameBlockBuilder WithoutStat(string stat)
    {
        _missingStats.Add(stat);
        return this;
    }

    public JObject Build()
    {
        var block = new JObject
        {
            ["gameId"] = GameId,
            ["gameLength"] = _gameLength,
            ["localPlayer"] = new JObject { ["puuid"] = LocalPuuid, ["championId"] = 1 },
            ["teams"] = new JArray(Team(100, _win, first: 0), Team(200, !_win, first: 5)),
        };
        if (_queueId is int queueId)
        {
            block["queueId"] = queueId;
        }

        return block;
    }

    private JObject Team(int teamId, bool win, int first) =>
        new()
        {
            ["teamId"] = teamId,
            ["isWinningTeam"] = win,
            ["isPlayerTeam"] = teamId == 100,
            ["players"] = new JArray(Enumerable.Range(first, 5).Select(index => Player(teamId, index))),
        };

    private JObject Player(int teamId, int index)
    {
        var stats = new JObject
        {
            ["CHAMPIONS_KILLED"] = index + 1,
            ["NUM_DEATHS"] = 10 - index,
            ["ASSISTS"] = index * 2,
            ["GOLD_EARNED"] = 8000 + index * 500,
            ["TOTAL_DAMAGE_TAKEN"] = 15000 + index * 1000,
            ["TOTAL_DAMAGE_DEALT_TO_CHAMPIONS"] = 12000 + index * 1500,
            ["TIME_CCING_OTHERS"] = 10 + index * 3,
            ["MINIONS_KILLED"] = 20 + index,
            ["TURRETS_KILLED"] = 0,
            ["LARGEST_KILLING_SPREE"] = 2,
            ["TRIPLE_KILLS"] = 0,
            ["QUADRA_KILLS"] = 0,
            ["PENTA_KILLS"] = 0,
        };
        foreach (var ((_, stat), value) in _stats.Where(entry => entry.Key.Player == index))
        {
            stats[stat] = value;
        }

        foreach (string stat in _missingStats)
        {
            stats.Remove(stat);
        }

        var player = new JObject
        {
            ["puuid"] = index == 0 ? LocalPuuid : $"puuid-{index}",
            ["championId"] = index + 1,
            ["teamId"] = teamId,
            ["stats"] = stats,
        };
        string? name =
            index == 0 ? "本人"
            : index < 5 ? $"队友{index}"
            : $"对手{index - 4}";
        if (index is >= 1 and <= 4 && _names.TryGetValue(index, out string? renamed))
        {
            name = renamed;
        }

        if (name != null)
        {
            player["riotIdGameName"] = name;
        }

        return player;
    }
}
