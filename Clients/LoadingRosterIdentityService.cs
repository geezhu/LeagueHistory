using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Clients;

/// <summary>Resolve public account metadata before merging legacy LCU names with full Live Client Riot IDs.</summary>
public sealed class LoadingRosterIdentityService(Func<HttpClient?> client)
{
    private sealed record Identity(string Puuid, long SummonerId, string Name);

    private sealed class Entry
    {
        public Task<Identity?>? Request;
        public Identity? Value;
        public long RetryAt,
            Access;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly SemaphoreSlim _network = new(3);
    private HttpClient? _connection;

    public static bool HasPuuid(string value) =>
        !string.IsNullOrWhiteSpace(value) && (!Guid.TryParse(value, out var id) || id != Guid.Empty);

    public async Task<LoadingRoster> MergeAsync(
        LoadingRoster roster,
        JObject flow,
        string own,
        LiveMatchSnapshot live,
        Func<LiveMatchPlayer, int> champion,
        CancellationToken token
    )
    {
        var candidates = roster.Players.ToList();
        bool missingPublic =
            live.Players.Count(p => p.Name.Trim() != "#")
            > candidates.Count(p => HasPuuid(p.Puuid) || p.SummonerId > 0);
        foreach (
            var pick in (
                missingPublic ? flow["gameData"]?["playerChampionSelections"] as JArray ?? new() : new JArray()
            ).OfType<JObject>()
        )
        {
            string puuid = (string?)pick["puuid"] ?? "";
            if (!HasPuuid(puuid) || candidates.Any(p => p.Puuid == puuid))
                continue;
            candidates.Add(
                new(puuid, "", puuid, (long?)pick["summonerId"] ?? 0, (int?)pick["championId"] ?? 0, 0, puuid == own)
            );
        }
        var connection = client();
        var resolved = await Task.WhenAll(
                candidates.Select(async player =>
                {
                    if (!HasPuuid(player.Puuid) && player.SummonerId <= 0)
                        return player;
                    // Already confirmed full Riot IDs need no extra request.
                    if (
                        HasPuuid(player.Puuid)
                        && player.Name.Contains('#')
                        && live.Players.Count(p => p.Name == player.Name) == 1
                    )
                        return player;
                    var identity = await Resolve(player, roster.GameId + "|" + own, connection, token)
                        .ConfigureAwait(false);
                    return identity == null
                        ? player
                        : player with
                        {
                            Puuid = identity.Puuid,
                            SummonerId = identity.SummonerId,
                            Name = identity.Name.Length > 0 ? identity.Name : player.Name,
                            Self = player.Self || identity.Puuid == own,
                        };
                })
            )
            .ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(connection, client()))
            return (roster with { Players = [] }).FromLive(live, champion);
        return (roster with { Players = resolved }).FromLive(live, champion);
    }

    private async Task<Identity?> Resolve(
        LoadingPlayer player,
        string own,
        HttpClient? connection,
        CancellationToken token
    )
    {
        if (connection == null)
            return null;
        string key = own + "|" + (HasPuuid(player.Puuid) ? player.Puuid : "id:" + player.SummonerId);
        Task<Identity?> request;
        lock (_gate)
        {
            if (!ReferenceEquals(_connection, connection))
            {
                _connection = connection;
                _entries.Clear();
            }
            if (!_entries.TryGetValue(key, out var entry))
                _entries[key] = entry = new();
            entry.Access = Environment.TickCount64;
            if (entry.RetryAt > Environment.TickCount64)
                return entry.Value;
            request = entry.Request is { IsCompleted: false }
                ? entry.Request
                : entry.Request = Fetch(player, entry, connection);
            if (_entries.Count > 256)
                foreach (
                    string old in _entries
                        .Where(p => p.Key != key && p.Value.Request?.IsCompleted != false)
                        .OrderBy(p => p.Value.Access)
                        .Take(_entries.Count - 256)
                        .Select(p => p.Key)
                        .ToArray()
                )
                    _entries.Remove(old);
        }
        return await request.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<Identity?> Fetch(LoadingPlayer player, Entry entry, HttpClient connection)
    {
        await _network.WaitAsync().ConfigureAwait(false);
        Identity? identity = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string path =
                player.SummonerId > 0
                    ? "/lol-summoner/v1/summoners/" + player.SummonerId
                    : "/lol-summoner/v2/summoners/puuid/" + Uri.EscapeDataString(player.Puuid);
            var response = await new PlayerStatisticsService(() => connection)
                .GetAsync(path, timeout.Token)
                .ConfigureAwait(false);
            if (response.Data is JObject row)
            {
                string puuid = (string?)row["puuid"] ?? "";
                long id = (long?)row["summonerId"] ?? 0;
                if (
                    HasPuuid(puuid)
                    && (!HasPuuid(player.Puuid) || player.Puuid == puuid)
                    && (player.SummonerId <= 0 || player.SummonerId == id)
                )
                {
                    string name = (string?)row["gameName"] ?? "",
                        tag = (string?)row["tagLine"] ?? "";
                    if (name.Length > 0 && tag.Length > 0)
                        name += "#" + tag;
                    else
                        name = (string?)row["displayName"] ?? "";
                    identity = new(puuid, id, name);
                }
            }
        }
        catch (Exception e)
            when (e
                    is OperationCanceledException
                        or HttpRequestException
                        or Newtonsoft.Json.JsonException
                        or InvalidOperationException
            ) { }
        finally
        {
            _network.Release();
        }
        lock (_gate)
        {
            entry.Value = identity;
            entry.RetryAt = Environment.TickCount64 + (identity?.Name.Contains('#') == true ? 20 * 60 * 1000 : 60000);
        }
        return identity;
    }
}
