using League.Managers;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Services;

public sealed class TeamCardDisplayService
{
    private readonly FormMain _form;
    private readonly PlayerCardManager _cards;
    private CancellationTokenSource? _gameSessionLoad;
    private string _selectionQueue = "";
    private bool _fromChampionSelect;

    public TeamCardDisplayService(FormMain form, PlayerCardManager cardManager)
    {
        _form = form;
        _cards = cardManager;
    }

    public async Task EnterChampSelectAsync()
    {
        StopGameSessionLoad();
        _cards.BeginSession();
        _fromChampionSelect = true;
        _selectionQueue = "";
        _form._cachedMyTeam = new JArray();
        _form._cachedEnemyTeam = new JArray();
        await _cards.ClearCardsAsync();
        await _cards._uiManager.DispatchAsync(() =>
        {
            _form._waitingPanel?.Dispose();
            _form._waitingPanel = null;
            League.Controls.PlayerTeamsLayout.Show(_form.tableLayoutPanel1, _form.penalGameMatchData);
        });
    }

    public async Task ShowMyTeamCards(CancellationToken token = default)
    {
        long generation = _cards.Generation;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var session = await Globals.lcuClient.GetChampSelectSession(deadline.Token);
        token.ThrowIfCancellationRequested();
        if (generation != _cards.Generation || session?["myTeam"] is not JArray team || team.Count == 0)
        {
            return;
        }

        string queue = session.Value<string>("queueId") ?? _selectionQueue;
        if (string.IsNullOrEmpty(queue) || queue == "0")
        {
            var game = await Globals.lcuClient.GetGameSession(deadline.Token);
            queue = game?["gameData"]?["queue"]?.Value<string>("id") ?? "";
        }
        token.ThrowIfCancellationRequested();
        if (generation != _cards.Generation)
        {
            return;
        }

        _selectionQueue = queue;
        _form._cachedMyTeam = RoomPlayerLoader.Normalize(team);
        // Reconcile every snapshot. The loader keeps completed and in-flight players unchanged.
        await _cards.UpdateTeamAsync(team, true, generation, queue);
    }

    public void StartGameSessionLoad()
    {
        StopGameSessionLoad();
        var cancellation = _gameSessionLoad = new CancellationTokenSource();
        bool clearCards = !_fromChampionSelect;
        long generation = clearCards ? _cards.BeginSession() : _cards.Generation;
        _fromChampionSelect = false;
        _ = LoadGameSessionAsync(generation, cancellation.Token, clearCards);
    }

    private enum RosterAttempt
    {
        // 名单还没就绪，或换了新一局，需要重试/停止。
        Unavailable,
        Stale,

        // 已显示名单，但人数不足或含选人阶段补的临时身份，继续等更完整的名单。
        Partial,
        Complete,
    }

    private async Task LoadGameSessionAsync(long generation, CancellationToken token, bool clearCards)
    {
        bool hadRoster = false;
        try
        {
            await ShowTeamsLayoutAsync(generation, token, clearCards);
            // Team data may not be ready at the first InProgress transition. Retry the roster,
            // without making the game-phase monitor await player histories or slow LCU calls.
            for (int attempt = 0; attempt < 5 && generation == _cards.Generation; attempt++)
            {
                try
                {
                    var result = await TryLoadRosterOnceAsync(generation, token);
                    if (result == RosterAttempt.Stale)
                    {
                        return;
                    }

                    hadRoster |= result is RosterAttempt.Partial or RosterAttempt.Complete;
                    // Allow a later complete team list to replace a provisional hidden profile.
                    if (result == RosterAttempt.Complete)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    RoomLoadDiagnostics.Shared.Report(generation, "session-timeout");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RoomLoadDiagnostics.Shared.Report(generation, "session-failed", detail: ex.GetType().Name);
                }
                await Task.Delay(1500, token);
            }
            RoomLoadDiagnostics.Shared.Report(generation, hadRoster ? "roster-ready" : "roster-unavailable");
        }
        // StopGameSessionLoad cancelled this load for a newer session or a cleared snapshot.
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RoomLoadDiagnostics.Shared.Report(generation, "session-display-failed", detail: ex.GetType().Name);
        }
    }

    private async Task ShowTeamsLayoutAsync(long generation, CancellationToken token, bool clearCards)
    {
        if (clearCards)
        {
            _form._cachedMyTeam = new JArray();
            _form._cachedEnemyTeam = new JArray();
            await _cards.ClearCardsAsync();
        }
        await _cards._uiManager.DispatchAsync(() =>
        {
            if (token.IsCancellationRequested || generation != _cards.Generation)
            {
                return;
            }

            _form._waitingPanel?.Dispose();
            _form._waitingPanel = null;
            League.Controls.PlayerTeamsLayout.Show(_form.tableLayoutPanel1, _form.penalGameMatchData);
        });
    }

    /// <summary>
    /// 读一次对局会话；只有能确定自己在哪一队时才显示双方名单。单次读取最多等 8 秒。
    /// </summary>
    private async Task<RosterAttempt> TryLoadRosterOnceAsync(long generation, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var summoner = await Globals.lcuClient.GetCurrentSummoner(deadline.Token);
        var session = await Globals.lcuClient.GetGameSession(deadline.Token);
        token.ThrowIfCancellationRequested();
        if (generation != _cards.Generation)
        {
            return RosterAttempt.Stale;
        }

        string puuid = summoner?.Value<string>("puuid") ?? "";
        var data = session?["gameData"];
        var one = data?["teamOne"] as JArray;
        var two = data?["teamTwo"] as JArray;
        if (puuid.Length == 0 || one == null || two == null)
        {
            return RosterAttempt.Unavailable;
        }

        bool inOne = one.Any(p => p.Value<string>("puuid") == puuid);
        bool inTwo = two.Any(p => p.Value<string>("puuid") == puuid);
        if (inOne && inTwo)
        {
            // Leftovers from an earlier game put the user on both sides; the live game tells which players are real.
            data = await KeepLivePlayersAsync((JObject)data!, generation, deadline.Token);
            one = data?["teamOne"] as JArray ?? new();
            two = data?["teamTwo"] as JArray ?? new();
            inOne = one.Any(p => p.Value<string>("puuid") == puuid);
            inTwo = two.Any(p => p.Value<string>("puuid") == puuid);
        }

        if (inOne == inTwo)
        {
            return RosterAttempt.Unavailable;
        }

        string queue = data?["queue"]?.Value<string>("id") ?? "";
        Globals.CurrGameMod = queue;
        AppLog.Shared.State(LogCategory.Game, "game-queue", $"对局队列 {queue}，双方 {one.Count}+{two.Count} 人");
        var resolved = RoomPlayerLoader.ResolveGameTeams((JObject)data!);
        var ours = inOne ? resolved.One : resolved.Two;
        var theirs = WithoutAllies(ours, inOne ? resolved.Two : resolved.One);
        int supplemented = ours.Concat(theirs).Count(p => p.Value<bool?>("__roomSelectionOnly") == true);
        RoomLoadDiagnostics.Shared.Report(
            generation,
            "session-roster",
            detail: $"one={one.Count} two={two.Count} selections={(data?["playerChampionSelections"] as JArray)?.Count ?? 0} ally={ours.Count} enemy={theirs.Count} supplemented={supplemented}"
        );
        _form._cachedMyTeam = ours;
        _form._cachedEnemyTeam = theirs;
        await _cards.UpdateTeamAsync(ours, true, generation, queue);
        await _cards.UpdateTeamAsync(theirs, false, generation, queue);
        return ours.Count == 5 && theirs.Count == 5 && supplemented == 0
            ? RosterAttempt.Complete
            : RosterAttempt.Partial;
    }

    /// <summary>The session with only the players whose champions are in the live game; null when 2999 is unavailable.</summary>
    private static async Task<JObject?> KeepLivePlayersAsync(JObject data, long generation, CancellationToken token)
    {
        HashSet<int> champions;
        try
        {
            using var live = new Clients.LiveMatchClient();
            var snapshot = await live.GetAsync(token);
            var known = Globals.resLoading?.Champions.Champions ?? [];
            champions = snapshot
                .Players.Select(p => Models.LiveChampionIdentity.Resolve(p, known)?.Id ?? 0)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch (Exception e)
            when (e is HttpRequestException or Newtonsoft.Json.JsonException
                || (e is OperationCanceledException && !token.IsCancellationRequested)
            )
        {
            AppLog.Shared.Warn(LogCategory.Game, "对局玩家：客户端名单含上一局残留，游戏内名单读取失败", e);
            return null;
        }

        RoomLoadDiagnostics.Shared.Report(
            generation,
            "session-conflict",
            detail: $"one={(data["teamOne"] as JArray)?.Count ?? 0} two={(data["teamTwo"] as JArray)?.Count ?? 0} live={champions.Count}"
        );
        return champions.Count > 0 ? GameSessionTeams.KeepLive(data, champions) : null;
    }

    // Known identities must never appear on both sides.
    private static JArray WithoutAllies(JArray ours, JArray theirs)
    {
        var ourIds = ours.Select(p => p.Value<long?>("summonerId") ?? 0).Where(id => id > 0).ToHashSet();
        var ourPuuids = ours.Select(RoomPlayerLoader.PublicPuuid).Where(p => p.Length > 0).ToHashSet();
        return new JArray(
            theirs
                .Where(p =>
                    !ourIds.Contains(p.Value<long?>("summonerId") ?? 0)
                    && !ourPuuids.Contains(RoomPlayerLoader.PublicPuuid(p))
                )
                .Select(p => p.DeepClone())
        );
    }

    private void StopGameSessionLoad()
    {
        _gameSessionLoad?.Cancel();
        _gameSessionLoad?.Dispose();
        _gameSessionLoad = null;
    }

    public void ClearSnapshots()
    {
        StopGameSessionLoad();
        _cards.CancelPendingLoads();
        _fromChampionSelect = false;
        _form.lastChampSelectSnapshotString = "";
    }
}
