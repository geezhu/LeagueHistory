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
            return;
        string queue = session.Value<string>("queueId") ?? _selectionQueue;
        if (string.IsNullOrEmpty(queue) || queue == "0")
        {
            var game = await Globals.lcuClient.GetGameSession(deadline.Token);
            queue = game?["gameData"]?["queue"]?.Value<string>("id") ?? "";
        }
        token.ThrowIfCancellationRequested();
        if (generation != _cards.Generation)
            return;
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

    private async Task LoadGameSessionAsync(long generation, CancellationToken token, bool clearCards)
    {
        bool hadRoster = false;
        try
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
                    return;
                _form._waitingPanel?.Dispose();
                _form._waitingPanel = null;
                League.Controls.PlayerTeamsLayout.Show(_form.tableLayoutPanel1, _form.penalGameMatchData);
            });
            // Team data may not be ready at the first InProgress transition. Retry the roster,
            // without making the game-phase monitor await player histories or slow LCU calls.
            for (int attempt = 0; attempt < 5 && generation == _cards.Generation; attempt++)
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    var summoner = await Globals.lcuClient.GetCurrentSummoner(deadline.Token);
                    var session = await Globals.lcuClient.GetGameSession(deadline.Token);
                    token.ThrowIfCancellationRequested();
                    if (generation != _cards.Generation)
                        return;
                    string puuid = summoner?.Value<string>("puuid") ?? "";
                    var data = session?["gameData"];
                    var one = data?["teamOne"] as JArray;
                    var two = data?["teamTwo"] as JArray;
                    if (puuid.Length > 0 && one != null && two != null)
                    {
                        bool inOne = one.Any(p => p.Value<string>("puuid") == puuid);
                        bool inTwo = two.Any(p => p.Value<string>("puuid") == puuid);
                        if (inOne != inTwo)
                        {
                            string queue = data?["queue"]?.Value<string>("id") ?? "";
                            Globals.CurrGameMod = queue;
                            var resolved = RoomPlayerLoader.ResolveGameTeams((JObject)data!);
                            var ours = inOne ? resolved.One : resolved.Two;
                            var theirs = inOne ? resolved.Two : resolved.One;
                            // Known identities must never appear on both sides.
                            var ourIds = ours.Select(p => p.Value<long?>("summonerId") ?? 0)
                                .Where(id => id > 0)
                                .ToHashSet();
                            var ourPuuids = ours.Select(RoomPlayerLoader.PublicPuuid)
                                .Where(p => p.Length > 0)
                                .ToHashSet();
                            theirs = new JArray(
                                theirs
                                    .Where(p =>
                                        !ourIds.Contains(p.Value<long?>("summonerId") ?? 0)
                                        && !ourPuuids.Contains(RoomPlayerLoader.PublicPuuid(p))
                                    )
                                    .Select(p => p.DeepClone())
                            );
                            int supplemented = ours.Concat(theirs)
                                .Count(p => p.Value<bool?>("__roomSelectionOnly") == true);
                            RoomLoadDiagnostics.Shared.Report(
                                generation,
                                "session-roster",
                                detail: $"one={one.Count} two={two.Count} selections={(data?["playerChampionSelections"] as JArray)?.Count ?? 0} ally={ours.Count} enemy={theirs.Count} supplemented={supplemented}"
                            );
                            _form._cachedMyTeam = ours;
                            _form._cachedEnemyTeam = theirs;
                            await _cards.UpdateTeamAsync(ours, true, generation, queue);
                            await _cards.UpdateTeamAsync(theirs, false, generation, queue);
                            hadRoster = true;
                            // Allow a later complete team list to replace a provisional hidden profile.
                            if (ours.Count == 5 && theirs.Count == 5 && supplemented == 0)
                                return;
                        }
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
