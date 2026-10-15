using System.Security.Cryptography;
using System.Text;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly Dictionary<string, LiveBuildOption[]> _liveBuilds = new();
    private readonly HashSet<string> _unknownStructures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deadPlayers = new(StringComparer.Ordinal);
    private readonly RespawnMarkSounds _respawnSounds = new();
    private bool _readingMarks;
    private int _respawnChecks;
    private OverlayLiveState? _liveState;
    private JArray _liveSections = new();
    private string _liveRevision = "";
    private int[]? _clientRunes;
    private double? _enemyPhysicalShare;
    private TeamPressure? _teamPressure;
    private DeathTimingAdvice? _shownDeathAdvice;
    private int? _augmentsOpenedAt;
    private bool _selfDead;
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
        _teamPressure = null;
        _shownDeathAdvice = null;
        _augmentsOpenedAt = null;
        _selfDead = false;
        _enemyShareRead = false;
        _liveRetry = 0;
        _buildRetry = 0;
        _unknownStructures.Clear();
        _deadPlayers.Clear();
        _respawnChecks = 0;
        _respawnSounds.Reset();
        _augmentSelections = [];
        _ultimateHaste = new Dictionary<string, double>();
        _liveMode = "";
        _survivalAlerts.Reset();
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

        TrackAugments(snapshot);
        _liveMode = snapshot.Mode;
        RefreshModeControls();
        var waves = MinionWaveForecaster.Calculate(snapshot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (waves != null)
        {
            waves = waves with
            {
                DeathAdvice = DeathTimingAdvisor.Evaluate(snapshot, waves, _teamPressure, _augmentsOpenedAt),
            };
        }
        ReportDeathTiming(snapshot, waves?.DeathAdvice);
        ReportUnknownStructures(waves);
        CheckRespawnFormula(snapshot);
        _ = UpdateRespawnSoundsAsync(roster, snapshot, _generation);
        _ = ReadAugmentSelectionsAsync(_generation);
        if (UseGameBar)
        {
            UpdateSurvivalSounds(roster, insight);
        }

        var augmentStacks = CountAugmentStacks(roster, snapshot);
        var effects = SumAugmentEffects(roster, augmentStacks);
        _liveState = new(insight, _liveSections, _liveRevision)
        {
            Waves = waves,
            AugmentStacks = augmentStacks,
            Health = DescribeHealth(roster, snapshot, insight, effects),
            Haste = DescribeHaste(roster, snapshot, insight, effects),
            AbilityRanks = snapshot.Active?.AbilityRanks ?? new Dictionary<string, int>(),
            Effects = effects,
            UltimateHaste = _ultimateHaste,
        };
        _ = RefreshLivePanelAsync(roster, snapshot, insight, _generation);
    }

    // Marks live in the Game Bar; the chimes play here because Windows mutes the widget's audio in the background.
    private async Task UpdateRespawnSoundsAsync(LoadingRoster roster, LiveMatchSnapshot snapshot, int generation)
    {
        if (_readingMarks || _disposed || !UseGameBar)
        {
            return;
        }

        _readingMarks = true;
        try
        {
            var marks = await _gameBar.ReadRespawnMarksAsync();
            if (_disposed || generation != _generation)
            {
                return;
            }

            var slots = GameBarBridge.Slots(roster);
            var states = (marks?.Slots ?? [])
                .Select(slot => slots[slot])
                .OfType<LoadingPlayer>()
                .Select(player =>
                    snapshot.Find(player) is { } live
                        ? new RespawnMarkState(player.Key, live.IsDead && live.RespawnTimer > 0, live.RespawnTimer)
                        : null
                )
                .OfType<RespawnMarkState>()
                .ToArray();
            _respawnSounds.Update(states, marks?.Sound ?? false, marks?.Test ?? -1);
        }
        finally
        {
            _readingMarks = false;
        }
    }

    // The death timer table is unverified in game; each new death logs the formula beside the 2999 timer.
    private void CheckRespawnFormula(LiveMatchSnapshot snapshot)
    {
        var rules = DeathTimeRules.For(snapshot.Mode, snapshot.Map);
        foreach (var player in snapshot.Players)
        {
            if (!player.IsDead || player.RespawnTimer <= 0)
            {
                _deadPlayers.Remove(player.Key);
                continue;
            }

            if (!_deadPlayers.Add(player.Key) || rules == null || _respawnChecks >= 20)
            {
                continue;
            }

            _respawnChecks++;
            double predicted = rules.Seconds(player.Level, snapshot.Time);
            ChatSendDiagnostics.Shared.Report(
                "respawn-check",
                $"复活时间核对 {snapshot.Mode} {MinionWaveForecaster.Clock(snapshot.Time)} {player.ChampionName} "
                    + $"{player.Level} 级：公式 {predicted:0.0} 秒，游戏剩余 {player.RespawnTimer:0.0} 秒"
            );
        }
    }

    // The shop, and with it the augment choice, is open on the platform at the start and whenever the player is dead.
    // A program started mid-game cannot know the earlier choices and waits for the next death.
    private void TrackAugments(LiveMatchSnapshot snapshot)
    {
        if (snapshot.Players.FirstOrDefault(p => p.Self) is { } self && (self.IsDead || snapshot.Time < 30))
        {
            _augmentsOpenedAt = self.Level;
        }
    }

    // Each own death logs the advice shown just before it, so the thresholds can be checked against real games.
    private void ReportDeathTiming(LiveMatchSnapshot snapshot, DeathTimingAdvice? advice)
    {
        bool dead = snapshot.Players.FirstOrDefault(p => p.Self)?.IsDead ?? false;
        if (dead && !_selfDead && _shownDeathAdvice is { } shown)
        {
            ChatSendDiagnostics.Shared.Report(
                "death-timing",
                $"阵亡时的送死建议 {MinionWaveForecaster.Clock(snapshot.Time)} {shown.Verdict}："
                    + string.Join("；", shown.Factors.Select(DeathTimingAdvice.Describe))
            );
        }

        _selfDead = dead;
        if (!dead)
        {
            _shownDeathAdvice = advice;
        }
    }

    // Structure names are parsed from two observed formats; a new format is logged once so the parser can follow.
    private void ReportUnknownStructures(MinionWaveForecast? waves)
    {
        foreach (string name in waves?.UnknownStructures ?? [])
        {
            if (_unknownStructures.Add(name))
            {
                ChatSendDiagnostics.Shared.Report("minion-wave", "兵线推算未识别的建筑名称：" + name);
            }
        }
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

        // The panel is built after the live state, so the player's recorded augments are already summed there.
        var options =
            self != null && _liveState?.Effects.GetValueOrDefault(self.Key) is { } effects
                ? effects.Cooldown
                : CooldownOptions.None;
        return new(
            insight,
            self != null ? insight.Players.GetValueOrDefault(self.Key) : null,
            stats,
            stats != null ? LiveDefenseAdvice.Calculate(stats, _enemyPhysicalShare) : null,
            LiveBuildAdvice.Choose(builds, owned),
            owned,
            runes,
            runeSource
        )
        {
            Cooldowns = AbilityCooldowns.Describe(
                self != null
                    ? (_cooldowns.GetValueOrDefault(self.ChampionId) ?? []).Select(c => c.ForMode(CurrentMode()))
                    : [],
                snapshot.Active?.AbilityRanks ?? new Dictionary<string, int>(),
                stats?.AbilityHaste ?? 0,
                options
            ),
            CooldownOptions = options,
        };
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
            var self = roster.Players.FirstOrDefault(p => p.Self);
            int[] allies = roster
                .Players.Where(p => p.Team == own && p.ChampionId > 0)
                .Select(p => p.ChampionId)
                .ToArray();
            _teamPressure = TeamPressure.From(index, allies, enemies, self?.ChampionId ?? 0);
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
