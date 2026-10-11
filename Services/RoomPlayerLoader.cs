using System.Diagnostics;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed record RoomPlayerState(
    long Generation,
    string Key,
    bool Ally,
    int Column,
    JObject Player,
    PlayerMatchInfo? Info,
    string Status,
    bool CanRetry,
    long Revision
);

/// <summary>One current roster, with independently cancellable player requests and guarded UI delivery.</summary>
public sealed class RoomPlayerLoader : IDisposable
{
    private sealed class Entry
    {
        public required RoomPlayerState State;
        public readonly CancellationTokenSource Cancellation = new();
        public Task Work = Task.CompletedTask;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly SemaphoreSlim _requests = new(3);
    private readonly Func<JObject, CancellationToken, Task<PlayerMatchInfo>> _fetch;
    private readonly Func<Action, Task> _dispatch;
    private readonly Action<IReadOnlyList<RoomPlayerState>> _render;
    private readonly RoomLoadDiagnostics _log;
    private readonly TimeSpan _timeout,
        _retryDelay;
    private long _generation,
        _revision;
    private bool _disposed;
    public long Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public RoomPlayerLoader(
        Func<JObject, CancellationToken, Task<PlayerMatchInfo>> fetch,
        Func<Action, Task> dispatch,
        Action<IReadOnlyList<RoomPlayerState>> render,
        RoomLoadDiagnostics? diagnostics = null,
        TimeSpan? timeout = null,
        TimeSpan? retryDelay = null
    )
    {
        _fetch = fetch;
        _dispatch = dispatch;
        _render = render;
        _log = diagnostics ?? RoomLoadDiagnostics.Shared;
        _timeout = timeout ?? TimeSpan.FromSeconds(25);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    public long Reset()
    {
        lock (_gate)
        {
            ++_generation;
            foreach (var entry in _entries.Values)
                Retire(entry);
            _entries.Clear();
            _log.Report(_generation, "reset");
            return _generation;
        }
    }

    public async Task UpdateTeamAsync(JArray players, bool ally, long generation)
    {
        var normalized = Normalize(players);
        var start = new List<Entry>();
        bool changed = false;
        lock (_gate)
        {
            if (_disposed || generation != _generation)
                return;
            var wanted = normalized.Select((p, i) => TeamKey(ally, Identity(p, i))).ToHashSet();
            foreach (var pair in _entries.Where(p => p.Value.State.Ally == ally && !wanted.Contains(p.Key)).ToArray())
            {
                Retire(pair.Value);
                _entries.Remove(pair.Key);
                changed = true;
                _log.Report(generation, "removed", pair.Key);
            }
            for (int column = 0; column < normalized.Count; column++)
            {
                var player = (JObject)normalized[column];
                string key = TeamKey(ally, Identity(player, column));
                if (
                    _entries.TryGetValue(key, out var prior)
                    && (
                        prior.State.Player.Value<string>("__roomQueue") != player.Value<string>("__roomQueue")
                        || (prior.State.Player.Value<string>("nameVisibilityType") ?? "UNHIDDEN")
                            != (player.Value<string>("nameVisibilityType") ?? "UNHIDDEN")
                        || prior.State.Player.Value<long?>("summonerId") != player.Value<long?>("summonerId")
                    )
                )
                {
                    Retire(prior);
                    _entries.Remove(key);
                }
                if (_entries.TryGetValue(key, out var existing))
                {
                    if (existing.State.Column != column || !SameDisplay(existing.State.Player, player))
                    {
                        existing.State = existing.State with
                        {
                            Column = column,
                            Player = player,
                            Revision = ++_revision,
                        };
                        changed = true;
                    }
                    continue;
                }
                var entry = new Entry
                {
                    State = new(generation, key, ally, column, player, null, "正在读取战绩…", false, ++_revision),
                };
                _entries.Add(key, entry);
                start.Add(entry);
                changed = true;
            }
            if (changed)
                _log.Report(
                    generation,
                    "roster",
                    detail: $"team={(ally ? "ally" : "enemy")} input={players.Count} unique={normalized.Count}"
                );
        }
        if (!changed)
            return;
        try
        {
            await RenderAsync(generation);
        }
        finally
        {
            lock (_gate)
                foreach (var entry in start)
                    if (IsCurrent(entry))
                        entry.Work = Task.Run(() => LoadAsync(entry));
        }
    }

    public async Task RetryAsync(string key, long generation)
    {
        Entry? replacement;
        lock (_gate)
        {
            if (
                _disposed
                || generation != _generation
                || !_entries.TryGetValue(key, out var old)
                || !old.State.CanRetry
            )
                return;
            Retire(old);
            replacement = new Entry
            {
                State = old.State with { Status = "正在重试…", CanRetry = false, Revision = ++_revision },
            };
            _entries[key] = replacement;
        }
        try
        {
            await RenderAsync(generation);
        }
        finally
        {
            lock (_gate)
                if (IsCurrent(replacement))
                    replacement.Work = Task.Run(() => LoadAsync(replacement));
        }
    }

    private bool IsCurrent(Entry entry) =>
        !_disposed
        && entry.State.Generation == _generation
        && _entries.TryGetValue(entry.State.Key, out var current)
        && ReferenceEquals(entry, current)
        && !entry.Cancellation.IsCancellationRequested;

    private async Task LoadAsync(Entry entry)
    {
        try
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var token = entry.Cancellation.Token;
                var timer = Stopwatch.StartNew();
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(_timeout);
                bool acquired = false;
                try
                {
                    // Queue time counts towards the budget too, so a stalled first group
                    // cannot leave later players waiting indefinitely for a request slot.
                    await _requests.WaitAsync(deadline.Token);
                    acquired = true;
                    JObject player;
                    lock (_gate)
                    {
                        if (!IsCurrent(entry))
                            return;
                        player = (JObject)entry.State.Player.DeepClone();
                    }
                    _log.Report(
                        entry.State.Generation,
                        "request",
                        entry.State.Key,
                        $"column={entry.State.Column} attempt={attempt} waitMs={timer.ElapsedMilliseconds}"
                    );
                    // WaitAsync also protects UI delivery from resource loaders that do not accept cancellation.
                    var info = await _fetch(player, deadline.Token).WaitAsync(deadline.Token);
                    if (info?.Player == null || info.Player.GameName == "失败")
                        throw new InvalidOperationException("Player data unavailable");
                    string expectedPuuid = PublicPuuid(player);
                    if (
                        !string.IsNullOrEmpty(info.Player.Puuid)
                        && expectedPuuid.Length > 0
                        && info.Player.Puuid != expectedPuuid
                    )
                        throw new InvalidDataException("Player identity mismatch");
                    lock (_gate)
                    {
                        if (!IsCurrent(entry))
                            return;
                        entry.State = entry.State with
                        {
                            Info = info,
                            Status = "",
                            CanRetry = false,
                            Revision = ++_revision,
                        };
                    }
                    _log.Report(
                        entry.State.Generation,
                        "complete",
                        entry.State.Key,
                        $"ms={timer.ElapsedMilliseconds} games={info.MatchItems.Count}"
                    );
                    await RenderAsync(entry.State.Generation);
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    _log.Report(
                        entry.State.Generation,
                        "cancelled",
                        entry.State.Key,
                        $"ms={timer.ElapsedMilliseconds}"
                    );
                    return;
                }
                catch (Exception ex)
                {
                    bool timeout = deadline.IsCancellationRequested || ex is TimeoutException;
                    lock (_gate)
                    {
                        if (!IsCurrent(entry))
                            return;
                        entry.State = entry.State with
                        {
                            Status =
                                attempt < 2 ? "读取失败，稍后重试…"
                                : timeout ? "读取超时"
                                : "读取失败",
                            CanRetry = attempt == 2,
                            Revision = ++_revision,
                        };
                    }
                    _log.Report(
                        entry.State.Generation,
                        timeout ? "timeout" : "failed",
                        entry.State.Key,
                        $"attempt={attempt} ms={timer.ElapsedMilliseconds} error={ex.GetType().Name}"
                    );
                    await RenderAsync(entry.State.Generation);
                }
                finally
                {
                    if (acquired)
                        _requests.Release();
                }
                if (attempt < 2)
                    await Task.Delay(_retryDelay, token);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Report(entry.State.Generation, "cancelled", entry.State.Key);
        }
        catch (Exception ex)
        {
            _log.Report(entry.State.Generation, "delivery-failed", entry.State.Key, ex.GetType().Name);
        }
    }

    private static void Retire(Entry entry)
    {
        entry.Cancellation.Cancel();
        _ = entry.Work.ContinueWith(_ => entry.Cancellation.Dispose(), TaskScheduler.Default);
    }

    private Task RenderAsync(long generation) =>
        _dispatch(() =>
        {
            lock (_gate)
            {
                if (_disposed || generation != _generation)
                    return;
                _render(
                    _entries.Values.Select(e => e.State).OrderByDescending(s => s.Ally).ThenBy(s => s.Column).ToArray()
                );
            }
        });

    public static JArray Normalize(JArray players)
    {
        var result = new JArray();
        var puuids = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<long>();
        var cells = new HashSet<int>();
        foreach (var player in players.OfType<JObject>())
        {
            string puuid = PublicPuuid(player);
            long id = player.Value<long?>("summonerId") ?? 0;
            int? cell = player.Value<int?>("cellId");
            if (
                (puuid.Length > 0 && puuids.Contains(puuid))
                || (id > 0 && ids.Contains(id))
                || (puuid.Length == 0 && id == 0 && cell.HasValue && !cells.Add(cell.Value))
            )
                continue;
            if (puuid.Length > 0)
                puuids.Add(puuid);
            if (id > 0)
                ids.Add(id);
            result.Add(player.DeepClone());
            if (result.Count == 5)
                break;
        }
        return result;
    }

    public static JArray WithChampionSelections(JArray players, JArray selections)
    {
        var result = Normalize(players);
        foreach (var player in result.OfType<JObject>())
        {
            string puuid = PublicPuuid(player);
            if (puuid.Length == 0)
                continue;
            var selected = selections.FirstOrDefault(s => PublicPuuid(s) == puuid);
            if (selected?.Value<int?>("championId") is > 0 and var champion)
                player["championId"] = champion;
        }
        return result;
    }

    public static (JArray One, JArray Two) ResolveGameTeams(JObject gameData)
    {
        var selections = gameData["playerChampionSelections"] as JArray ?? new();
        var rawOne = gameData["teamOne"] as JArray ?? new();
        var rawTwo = gameData["teamTwo"] as JArray ?? new();
        var one = WithChampionSelections(rawOne, selections);
        var two = WithChampionSelections(rawTwo, selections);
        // Some private profiles are absent from teamOne/teamTwo but still occur in selections.
        // Only a complete five-player team plus ten distinct selections proves the complement.
        // Partial teams, duplicate identities and smaller/custom rosters are not enough evidence.
        if (
            gameData["queue"]?.Value<int?>("numPlayersPerTeam") != 5
            || selections.Count != 10
            || selections.OfType<JObject>().Count() != 10
            || rawOne.Count > 5
            || rawTwo.Count > 5
        )
            return (one, two);
        var selectedIds = selections.Select(PublicPuuid).ToArray();
        if (
            selectedIds.Any(string.IsNullOrEmpty)
            || selectedIds.Distinct(StringComparer.Ordinal).Count() != 10
            || selections.Any(p => p.Value<int?>("championId") is not > 0)
        )
            return (one, two);
        var oneIds = one.Select(PublicPuuid).ToArray();
        var twoIds = two.Select(PublicPuuid).ToArray();
        var knownIds = oneIds.Concat(twoIds).ToArray();
        if (
            knownIds.Any(p => !selectedIds.Contains(p, StringComparer.Ordinal))
            || knownIds.Distinct(StringComparer.Ordinal).Count() != knownIds.Length
        )
            return (one, two);
        JArray? incomplete =
            one.Count == 5 && two.Count < 5 ? two
            : two.Count == 5 && one.Count < 5 ? one
            : null;
        if (incomplete == null)
            return (one, two);
        foreach (
            var pick in selections
                .OfType<JObject>()
                .Where(p => !knownIds.Contains(PublicPuuid(p), StringComparer.Ordinal))
        )
        {
            incomplete.Add(
                new JObject
                {
                    ["puuid"] = PublicPuuid(pick),
                    ["championId"] = pick.Value<int>("championId"),
                    ["summonerId"] = 0,
                    ["nameVisibilityType"] = "HIDDEN",
                    ["__roomSelectionOnly"] = true,
                }
            );
        }
        return (one, two);
    }

    private static bool SameDisplay(JObject first, JObject second) =>
        new[]
        {
            "summonerId",
            "puuid",
            "championId",
            "assignedPosition",
            "selectedPosition",
            "nameVisibilityType",
            "__roomQueue",
            "__roomSelectionOnly",
        }.All(key => JToken.DeepEquals(first[key], second[key]));

    public static string PublicPuuid(JToken player)
    {
        string value = player.Value<string>("puuid") ?? "";
        return
            string.IsNullOrWhiteSpace(value)
            || value == "00000000-0000-0000-0000-000000000000"
            || value.StartsWith("OBFUSCATED_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("HIDDEN_", StringComparison.OrdinalIgnoreCase)
            ? ""
            : value;
    }

    public static string Identity(JToken player, int column)
    {
        string puuid = PublicPuuid(player);
        long id = player.Value<long?>("summonerId") ?? 0;
        return puuid.Length > 0 ? "p:" + puuid
            : id > 0 ? "s:" + id
            : "cell:" + (player.Value<int?>("cellId") ?? column);
    }

    private static string TeamKey(bool ally, string identity) => (ally ? "ally/" : "enemy/") + identity;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            Reset();
            _disposed = true;
        }
    }
}
