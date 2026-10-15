using System.Media;
using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // Summoner spells on the Game Bar cards: allies' are timed by hand (the user clicks when a spell is used) and chime
    // when back; enemies' only show which spells they have. Riot's rules forbid tracking enemy summoner cooldowns,
    // manual timers included, so enemies never get a cooldown here.
    private readonly SummonerSpellData _summonerData = new();
    private readonly SummonerTimerAlerts _summonerAlerts = new();
    private readonly Dictionary<string, string> _summonerIcons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _summonerIconRequests = new(StringComparer.Ordinal);
    private Dictionary<string, SummonerSpellInfo>? _summonerSpells;

    // Base cooldowns a mode changes; until they load (or when CommunityDragon fails) the spells' own are used.
    private Dictionary<string, Dictionary<string, double>>? _summonerModes;
    private SummonerTimer[] _summonerTimers = [];
    private bool _loadingSummoners,
        _loadingSummonerModes,
        _readingSummonerTimers;
    private long _summonerRetry,
        _summonerModesRetry;
    private SoundPlayer? _summonerPlayer;

    private Dictionary<string, PlayerSummoner[]> DescribeSummoners(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight
    )
    {
        _ = LoadSummonerSpellsAsync();
        _ = LoadSummonerModesAsync();
        _ = ReadSummonerTimersAsync(_generation);
        string mode = CurrentMode();
        var result = new Dictionary<string, PlayerSummoner[]>();
        int own = roster.Players.FirstOrDefault(p => p.Self)?.Team ?? 0;
        if (_summonerSpells == null || own == 0)
        {
            return result;
        }

        foreach (var player in roster.Players.Where(p => p.ChampionId > 0))
        {
            if (insight.Players.GetValueOrDefault(player.Key) is not { } live)
            {
                continue;
            }

            bool ally = player.Team == own;
            double haste = SummonerSpells.Haste(live.Items, player.Self ? snapshot.Active?.Runes ?? [] : []);
            result[player.Key] = live
                .SummonerSpells.Select(
                    (id, slot) =>
                        _summonerSpells.GetValueOrDefault(id) is { } spell
                            ? new PlayerSummoner(
                                slot,
                                spell,
                                ally ? SummonerSpells.Cooldown(spell, haste, mode, _summonerModes) : 0,
                                ally
                                    ? _summonerTimers
                                        .FirstOrDefault(t => t.Player == player.Key && t.Slot == slot)
                                        ?.Time
                                    : null
                            )
                            : null
                )
                .OfType<PlayerSummoner>()
                .ToArray();
            foreach (var summoner in result[player.Key])
            {
                _ = LoadSummonerIconAsync(summoner.Spell);
            }
        }

        AnnounceSummoners(roster, snapshot, result, own);
        return result;
    }

    private void AnnounceSummoners(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        Dictionary<string, PlayerSummoner[]> summoners,
        int own
    )
    {
        var timers = summoners.SelectMany(entry =>
            entry.Value.Select(summoner => new SummonerTimerState(entry.Key, summoner))
        );
        var ready = _summonerAlerts.Update(timers, snapshot.Time);
        if (ready.Length == 0 || !_controlSettings.SummonerSound)
        {
            return;
        }

        foreach (var timer in ready)
        {
            var player = roster.Players.First(p => p.Key == timer.Key);
            ChatSendDiagnostics.Shared.Report(
                "summoner-ready",
                $"召唤师技能转好：{(player.Team == own ? "我方" : "敌方")} {Champion(player.ChampionId)} {timer.Summoner.Spell.Name}"
            );
        }

        // SoundPlayer.Play returns at once and plays on its own thread; several spells back at once chime once.
        _summonerPlayer?.Dispose();
        _summonerPlayer = new SoundPlayer(new MemoryStream(RespawnMarkSounds.SummonerReadyWave.Value));
        try
        {
            _summonerPlayer.Play();
        }
        catch (InvalidOperationException e)
        {
            // No audio device: the countdown on the card still shows the spell is back.
            ChatSendDiagnostics.Shared.Report("summoner-ready", "提示音无法播放：" + e.Message);
        }
    }

    private async Task ReadSummonerTimersAsync(int generation)
    {
        if (_readingSummonerTimers || _disposed || !UseGameBar)
        {
            return;
        }

        _readingSummonerTimers = true;
        try
        {
            var timers = await _gameBar.ReadSummonerTimersAsync();
            // A missing or half-written file keeps the last timers; the widget rewrites it on every click.
            if (timers != null && !_disposed && generation == _generation)
            {
                _summonerTimers = timers;
            }
        }
        finally
        {
            _readingSummonerTimers = false;
        }
    }

    private async Task LoadSummonerSpellsAsync()
    {
        if (_summonerSpells != null || _loadingSummoners || _disposed || Environment.TickCount64 < _summonerRetry)
        {
            return;
        }

        _loadingSummoners = true;
        try
        {
            var spells = await Task.Run(() => _summonerData.LoadAsync(_life.Token));
            if (spells.Count > 0)
            {
                _summonerSpells = spells;
            }
            else
            {
                _summonerRetry = Environment.TickCount64 + 60_000;
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Shutting down; nothing to keep.
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or TaskCanceledException
                        or IOException
                        or UnauthorizedAccessException
                        or Newtonsoft.Json.JsonException
            )
        {
            // The cards show no summoner spells until the next try, a minute later.
            _summonerRetry = Environment.TickCount64 + 60_000;
            ChatSendDiagnostics.Shared.Report("summoner-spells", e.GetType().Name + " " + e.Message);
        }
        finally
        {
            _loadingSummoners = false;
        }
    }

    private async Task LoadSummonerModesAsync()
    {
        if (
            _summonerModes != null
            || _loadingSummonerModes
            || _disposed
            || Environment.TickCount64 < _summonerModesRetry
        )
        {
            return;
        }

        _loadingSummonerModes = true;
        try
        {
            _summonerModes = await Task.Run(() => _summonerData.LoadModesAsync(_life.Token));
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Shutting down; nothing to keep.
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or TaskCanceledException
                        or IOException
                        or UnauthorizedAccessException
                        or Newtonsoft.Json.JsonException
            )
        {
            // Timers keep the spells' own cooldowns (Exhaust 240 instead of 290 in ARAM) until the next try.
            _summonerModesRetry = Environment.TickCount64 + 5 * 60_000;
            ChatSendDiagnostics.Shared.Report("summoner-modes", e.GetType().Name + " " + e.Message);
        }
        finally
        {
            _loadingSummonerModes = false;
        }
    }

    private async Task LoadSummonerIconAsync(SummonerSpellInfo spell)
    {
        if (_disposed || _summonerIcons.ContainsKey(spell.Id) || !_summonerIconRequests.Add(spell.Id))
        {
            return;
        }

        try
        {
            string icon = await Task.Run(() => _summonerData.IconAsync(spell, _life.Token));
            if (!_disposed && icon.Length > 0)
            {
                _summonerIcons[spell.Id] = icon;
                SchedulePaint();
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Shutting down; nothing to show.
        }
        catch (Exception e)
            when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            // The card shows the spell's name instead; the icon is tried again in the next game.
            ChatSendDiagnostics.Shared.Report("summoner-icon", $"{spell.Id} {e.GetType().Name} {e.Message}");
        }
        finally
        {
            _summonerIconRequests.Remove(spell.Id);
        }
    }
}
