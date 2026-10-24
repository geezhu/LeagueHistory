using System.Text;
using League.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

public enum PostGameSendResult
{
    Sent,

    /// <summary>The post-game lobby is gone, for example after the stats screen was skipped.</summary>
    NoConversation,
    Failed,
}

/// <summary>
/// Reads the finished game from the client's end-of-game stats and sends the panel's lines to the post-game chat.
/// The stats block can lag the end of the game by a moment; the match history may not have the game yet at all.
/// </summary>
public sealed class EndOfGameSummaryService
{
    private const string StatsBlockPath = "/lol-end-of-game/v1/eog-stats-block";
    private const string ConversationsPath = "/lol-chat/v1/conversations";
    private const string PostGameType = "postGame";

    // The match record names the score estimate and the honor icons read, with the upper-case end-of-game name of
    // each. The stats block carries both spellings for most stats, but the multi-kills only as match record names
    // (seen on a live client on 2026-10-24), so the match record name is read first.
    private static readonly (string Stat, string Field)[] RecordStats =
    [
        ("CHAMPIONS_KILLED", "kills"),
        ("NUM_DEATHS", "deaths"),
        ("ASSISTS", "assists"),
        ("GOLD_EARNED", "goldEarned"),
        ("TOTAL_DAMAGE_TAKEN", "totalDamageTaken"),
        ("TOTAL_DAMAGE_DEALT_TO_CHAMPIONS", "totalDamageDealtToChampions"),
        ("TIME_CCING_OTHERS", "timeCCingOthers"),
        ("MINIONS_KILLED", "totalMinionsKilled"),
        ("TURRETS_KILLED", "turretKills"),
        ("LARGEST_KILLING_SPREE", "largestKillingSpree"),
        ("TRIPLE_KILLS", "tripleKills"),
        ("QUADRA_KILLS", "quadraKills"),
        ("PENTA_KILLS", "pentaKills"),
    ];

    private static readonly TimeSpan ConversationPoll = TimeSpan.FromSeconds(1);

    private readonly Func<HttpClient?> _client;
    private readonly TimeSpan _conversationWait;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="conversationWait">
    /// How long a send waits for the post-game chat to open. The panel opens as the game ends, while the honor screen
    /// can keep the client away from the end-of-game lobby, and its chat, for most of a minute.
    /// </param>
    public EndOfGameSummaryService(
        Func<HttpClient?> client,
        TimeSpan? conversationWait = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        _client = client;
        _conversationWait = conversationWait ?? TimeSpan.FromSeconds(90);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>The finished game, or null while the client has no stats block or it lacks the local player.</summary>
    public async Task<EndOfGameSummary?> GetAsync(int fallbackQueueId, CancellationToken token)
    {
        var client = _client();
        if (client == null)
        {
            return null;
        }

        using var response = await client.GetAsync(StatsBlockPath, token);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(token);
        if (JToken.Parse(json) is not JObject block)
        {
            return null;
        }

        var summary = Parse(block, fallbackQueueId);
        if (summary != null)
        {
            LogStatNames(block);
        }

        return summary;
    }

    // Temporary: the stat names below were written from memory; this records which the client really sends, by name
    // only (no values, names or ids). Remove once a real game has confirmed them.
    private static void LogStatNames(JObject block)
    {
        var stats = (block["teams"] as JArray)
            ?.SelectMany(team => team["players"] as JArray ?? [])
            .Select(player => player["stats"] as JObject)
            .FirstOrDefault(value => value != null);
        var names = stats?.Properties().Select(property => property.Name).ToHashSet() ?? [];
        string missing = string.Join(
            ", ",
            RecordStats
                .Where(entry => !names.Contains(entry.Field) && !names.Contains(entry.Stat))
                .Select(entry => entry.Field)
        );
        AppLog.Shared.Info(
            LogCategory.Game,
            $"[结算字段·临时] 缺少：{(missing.Length > 0 ? missing : "无")}；全部统计项：{string.Join(", ", names.Order())}"
        );
    }

    /// <summary>
    /// The summary of a stats block. The queue comes from the block when it carries one, otherwise from the caller;
    /// scores exist only for the queues the estimate models.
    /// </summary>
    public static EndOfGameSummary? Parse(
        JObject block,
        int fallbackQueueId,
        AramMayhemScoreEstimator? estimator = null
    )
    {
        string? localPuuid = (string?)block["localPlayer"]?["puuid"];
        var teams = (block["teams"] as JArray)?.OfType<JObject>().ToArray() ?? [];
        var players = teams
            .SelectMany(team =>
                (team["players"] as JArray)?.OfType<JObject>().Select(player => (Team: team, Player: player)) ?? []
            )
            .ToArray();
        int local = Array.FindIndex(players, entry => (string?)entry.Player["puuid"] == localPuuid);
        if (
            string.IsNullOrEmpty(localPuuid)
            || local < 0
            || players[local].Team["isWinningTeam"]?.Type != JTokenType.Boolean
        )
        {
            return null;
        }

        var ownTeam = players[local].Team;
        int queueId = block["queueId"]?.Type == JTokenType.Integer ? block.Value<int>("queueId") : fallbackQueueId;
        var participants = players.Select(entry => RecordParticipant(entry.Team, entry.Player)).ToArray();
        double[]? estimates = (estimator ?? AramMayhemScoreEstimator.Shared).Estimate(participants, queueId);
        OwnMatchScore? Score(int index) =>
            estimates == null
                ? null
                : new OwnMatchScore(estimates[index], AramMayhemScoreEstimator.Honour(participants, estimates, index));

        // Honor icons compare each player with the whole game, both teams included.
        var everyone = participants.ToList();
        IReadOnlyList<string> Honors(int index) =>
            UIHelpers.WeGameHonorIcons.Select(participants[index], everyone).Select(honor => honor.Key).ToArray();

        // As in WeGame, a teammate with the team's MVP or SVP comes right after the local player; the rest keep
        // the client's order. OrderBy is stable.
        var teammates = Enumerable
            .Range(0, players.Length)
            .Where(index => index != local && players[index].Team == ownTeam)
            .Select(index => new EndOfGameTeammate(
                Name(players[index].Player),
                (int?)players[index].Player["championId"] ?? 0,
                Kda(players[index].Player),
                Score(index),
                Honors(index)
            ))
            .OrderBy(teammate => teammate.Score?.Honour is MatchHonour.Mvp or MatchHonour.Svp ? 0 : 1)
            .ToArray();
        return new EndOfGameSummary(
            (long?)block["gameId"] ?? 0,
            Name(players[local].Player),
            ownTeam.Value<bool>("isWinningTeam"),
            TimeSpan.FromSeconds((int?)block["gameLength"] ?? 0),
            (int?)players[local].Player["championId"] ?? 0,
            Kda(players[local].Player),
            Score(local),
            Honors(local),
            teammates
        );
    }

    /// <summary>
    /// Sends one line to the post-game lobby chat, never to any other conversation; waits for that chat to open
    /// when the client is still on the honor screen.
    /// </summary>
    public async Task<PostGameSendResult> SendAsync(string message, CancellationToken token)
    {
        var client = _client();
        if (client == null)
        {
            return PostGameSendResult.NoConversation;
        }

        var (listed, id) = await FindPostGameConversationAsync(client, token);
        for (var waited = TimeSpan.Zero; listed && id == null && waited < _conversationWait; waited += ConversationPoll)
        {
            await _delay(ConversationPoll, token);
            (listed, id) = await FindPostGameConversationAsync(client, token);
        }

        if (!listed)
        {
            return PostGameSendResult.Failed;
        }

        if (id == null)
        {
            return PostGameSendResult.NoConversation;
        }

        using var content = new StringContent(
            JsonConvert.SerializeObject(new { body = message }),
            Encoding.UTF8,
            "application/json"
        );
        using var sendResponse = await client.PostAsync(
            $"{ConversationsPath}/{Uri.EscapeDataString(id)}/messages",
            content,
            token
        );
        return sendResponse.IsSuccessStatusCode ? PostGameSendResult.Sent : PostGameSendResult.Failed;
    }

    /// <summary>Listed is false when the client refused the conversation list, which waiting does not fix.</summary>
    private static async Task<(bool Listed, string? Id)> FindPostGameConversationAsync(
        HttpClient client,
        CancellationToken token
    )
    {
        using var response = await client.GetAsync(ConversationsPath, token);
        if (!response.IsSuccessStatusCode)
        {
            return (false, null);
        }

        // Matched on the type alone: the name fallbacks of ChatService would also take a champion select lobby.
        string? id = JToken.Parse(await response.Content.ReadAsStringAsync(token)) is JArray conversations
            ? conversations
                .OfType<JObject>()
                .Where(c => string.Equals((string?)c["type"], PostGameType, StringComparison.OrdinalIgnoreCase))
                .Select(c => (string?)c["id"])
                .FirstOrDefault(value => !string.IsNullOrEmpty(value))
            : null;
        return (true, id);
    }

    private static string Name(JObject player) =>
        new[] { "riotIdGameName", "gameName", "summonerName" }
            .Select(key => ((string?)player[key])?.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value))
        ?? "";

    private static EndOfGameKda Kda(JObject player) =>
        new(Stat(player, "CHAMPIONS_KILLED"), Stat(player, "NUM_DEATHS"), Stat(player, "ASSISTS"));

    private static int Stat(JObject player, string key) => (int?)player["stats"]?[key] ?? 0;

    // A participant shaped like a match record, with a stat left out when the block lacks it: the estimate then
    // declines the whole game, and the honor icons that stat would award, instead of counting it as zero.
    private static JObject RecordParticipant(JObject team, JObject player)
    {
        var participant = new JObject
        {
            ["teamId"] = player["teamId"] ?? team["teamId"],
            ["championId"] = player["championId"],
            ["win"] = team["isWinningTeam"],
        };
        foreach (var (stat, field) in RecordStats)
        {
            var value = new[] { player["stats"]?[field], player["stats"]?[stat] }.FirstOrDefault(token =>
                token?.Type == JTokenType.Integer
            );
            if (value != null)
            {
                participant[field] = value;
            }
        }

        return participant;
    }
}
