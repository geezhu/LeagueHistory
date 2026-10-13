using System.Security.Cryptography;
using System.Text;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly Dictionary<string, LiveBuildOption[]> _liveBuilds = new();
    private OverlayLiveState? _liveState;
    private JArray _liveSections = new();
    private string _liveRevision = "";
    private int[]? _clientRunes;
    private double? _enemyPhysicalShare;
    private bool _liveBusy,
        _enemyShareRead;
    private long _liveRetry,
        _buildRetry;

    private void ResetLive()
    {
        _liveState = null;
        _liveSections = new JArray();
        _liveRevision = "";
        _clientRunes = null;
        _enemyPhysicalShare = null;
        _enemyShareRead = false;
        _liveRetry = 0;
        _buildRetry = 0;
    }

    private static int ItemValue(LiveMatchItem item) =>
        FormMain.Globals.resLoading?.GetItemById(item.Id)?.PriceTotal is int total and > 0 ? total : item.Price;

    private void UpdateLive(LoadingRoster roster, LiveMatchSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            // Keep the last scoreboard through a missed poll; a new match resets it in ApplyRoster.
            return;
        }

        var insight = LiveMatchInsight.Calculate(roster, snapshot, ItemValue);
        if (insight == null)
        {
            _liveState = null;
            return;
        }

        _liveState = new(insight, _liveSections, _liveRevision);
        _ = RefreshLivePanelAsync(roster, snapshot, insight, _generation);
    }

    private async Task RefreshLivePanelAsync(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight,
        int generation
    )
    {
        if (_liveBusy || _disposed)
        {
            return;
        }

        _liveBusy = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var input = await ReadLivePanelInputAsync(roster, snapshot, insight, timeout.Token);
            var data = new JObject
            {
                ["sections"] = LivePanelSections.Build(
                    input,
                    id => FormMain.Globals.resLoading?.GetItemById(id)?.Name ?? $"装备 {id}"
                ),
            };
            // Images are cached by the detail service; the revision covers text only, so a redraw follows real changes.
            string revision = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString(Newtonsoft.Json.Formatting.None)))
            )[..16];
            await _championDetails.DescribeIconsAsync(data, timeout.Token);
            if (_disposed || generation != _generation)
            {
                return;
            }

            _liveSections = (JArray)data["sections"]!;
            _liveRevision = revision;
            if (_liveState != null)
            {
                _liveState = _liveState with { Sections = _liveSections, Revision = revision };
            }

            SchedulePaint();
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a slow source; the next poll rebuilds the panel.
        }
        catch (Exception e)
        {
            // A fire-and-forget refresh has no caller to rethrow to; record it and keep the last panel.
            ChatSendDiagnostics.Shared.Report("overlay-live", e.GetType().Name + " " + e.Message);
        }
        finally
        {
            _liveBusy = false;
        }
    }

    private async Task<LivePanelInput> ReadLivePanelInputAsync(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight,
        CancellationToken token
    )
    {
        var self = roster.Players.FirstOrDefault(p => p.Self);
        var live = self == null ? snapshot.Players.FirstOrDefault(p => p.Self) : snapshot.Find(self);
        int[] owned = (live?.Items ?? []).Select(i => i.Id).ToArray();
        var stats = snapshot.Active?.Stats;
        await ReadEnemyShareAsync(roster, token);
        var builds = self is { ChampionId: > 0 } ? await ReadBuildsAsync(self.ChampionId, roster.Queue, token) : [];
        int[] runes = snapshot.Active?.Runes ?? [];
        string runeSource = "来自游戏内数据。";
        if (runes.Length == 0)
        {
            runes = await ReadClientRunesAsync(token);
            runeSource = "本模式游戏内不提供符文，显示客户端当前符文页。";
        }

        return new(
            insight,
            self != null ? insight.Players.GetValueOrDefault(self.Key) : null,
            stats,
            stats != null ? LiveDefenseAdvice.Calculate(stats, _enemyPhysicalShare) : null,
            LiveBuildAdvice.Choose(builds, owned),
            owned,
            runes,
            runeSource
        );
    }

    private async Task ReadEnemyShareAsync(LoadingRoster roster, CancellationToken token)
    {
        int own = roster.Players.FirstOrDefault(p => p.Self)?.Team ?? 0;
        int[] enemies = roster
            .Players.Where(p => p.Team != own && p.ChampionId > 0)
            .Select(p => p.ChampionId)
            .ToArray();
        if (_enemyShareRead || own == 0 || enemies.Length == 0 || Environment.TickCount64 < _liveRetry)
        {
            return;
        }

        try
        {
            var index = await _championDetails.LoadAramMetaIndexAsync(token);
            var evaluation = AramTeamScore.Evaluate(index, enemies);
            _enemyPhysicalShare = evaluation.Missing.Length < enemies.Length ? evaluation.AdShare : null;
            _enemyShareRead = true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
        {
            // Statistics are optional here; survivability still shows without the damage mix.
            _liveRetry = Environment.TickCount64 + 60000;
        }
    }

    private async Task<LiveBuildOption[]> ReadBuildsAsync(int hero, int queue, CancellationToken token)
    {
        string key = $"{hero}:{queue}";
        if (_liveBuilds.TryGetValue(key, out var cached) || Environment.TickCount64 < _buildRetry)
        {
            return cached ?? [];
        }

        try
        {
            var builds = await _championDetails.LoadBuildOptionsAsync(hero, queue, "", token);
            _liveBuilds[key] = builds;
            return builds;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
        {
            // Retried a minute later; the rest of the panel does not wait for recommendations.
            _buildRetry = Environment.TickCount64 + 60000;
            return [];
        }
    }

    private async Task<int[]> ReadClientRunesAsync(CancellationToken token)
    {
        if (_clientRunes != null || FormMain.Globals.lcuClient?.Client is not { } client)
        {
            return _clientRunes ?? [];
        }

        try
        {
            var page = JObject.Parse(await client.GetStringAsync("/lol-perks/v1/currentpage", token));
            _clientRunes = (page["selectedPerkIds"] as JArray ?? new()).Values<int>().Where(id => id > 0).ToArray();
        }
        catch (Exception e) when (e is HttpRequestException or Newtonsoft.Json.JsonException)
        {
            // The client page is a fallback; leave the rune section out until it can be read.
        }

        return _clientRunes ?? [];
    }
}
