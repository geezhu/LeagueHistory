using League.Models;
using League.Parsers;
using League.Networking;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

public sealed class PlayerCardManager : IDisposable
{
    private readonly MatchQueryProcessor _matchQueryProcessor;
    private readonly PlayerCardCache _cache = new();
    public readonly PlayerCardUIManager _uiManager;
    public readonly PlayerCardFactory _factory = new();
    private readonly RoomPlayerLoader _loader;
    public long Generation => _loader.Generation;

    public PlayerCardManager(FormMain form, MatchQueryProcessor matchQueryProcessor)
    {
        _matchQueryProcessor = matchQueryProcessor;
        _uiManager = new PlayerCardUIManager(form);
        matchQueryProcessor.SetPlayerCardManager(this);
        _loader = new RoomPlayerLoader(FetchAsync, _uiManager.DispatchAsync, states =>
        {
            foreach (var team in states.GroupBy(s => s.Ally))
                new PartyDetector().Detect(team.Where(s => !string.IsNullOrEmpty(s.Info?.Player?.Puuid)).Select(s => s.Info!).ToList());
            _uiManager.Render(states);
            _cache.Replace(states.Where(s => s.Info?.Player?.SummonerId > 0)
                .Select(s => s.Info!.CopyForChampion(s.Player.Value<int?>("championId") ?? 0)));
        });
        _uiManager.RetryRequested = (key, generation) => _ = RetryAsync(key, generation);
        form.Disposed += (_, _) => Dispose();
    }

    private Task<PlayerMatchInfo> FetchAsync(JObject player, CancellationToken token)
    {
        long generation = Generation;
        string key = RoomPlayerLoader.Identity(player, 0);
        return _matchQueryProcessor.FetchPlayerMatchInfoAsync(player, token, player.Value<string>("__roomQueue"),
            stage => RoomLoadDiagnostics.Shared.Report(generation, "fetch-" + stage, key));
    }

    private async Task RetryAsync(string key, long generation)
    {
        try { await _loader.RetryAsync(key, generation); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { RoomLoadDiagnostics.Shared.Report(generation, "retry-failed", key, ex.GetType().Name); }
    }

    public Task UpdateTeamAsync(JArray players, bool ally, long generation, string queueId)
    {
        var snapshot = (JArray)players.DeepClone();
        foreach (var player in snapshot.OfType<JObject>()) player["__roomQueue"] = queueId;
        return _loader.UpdateTeamAsync(snapshot, ally, generation);
    }
    public Task ClearCardsAsync() => _uiManager.DispatchAsync(() => _uiManager.Render(Array.Empty<RoomPlayerState>()));
    public long BeginSession()
    {
        long generation = _loader.Reset();
        _cache.ClearGameState();
        return generation;
    }
    public void CancelPendingLoads() => _loader.Reset();
    public PlayerMatchInfo CreateFailedPlayerInfo(long sid, int champion) => _factory.CreateFailedPlayerInfo(sid, champion);
    public Dictionary<long, PlayerMatchInfo> GetAllCachedPlayerInfos() => _cache.GetAllCachedPlayerInfos();
    public void ClearAllCaches() { _matchQueryProcessor.ClearPlayerMatchCache(); _cache.ClearAll(); }
    public void ClearGameState() { CancelPendingLoads(); _cache.ClearGameState(); }
    public void Dispose() => _loader.Dispose();
}
