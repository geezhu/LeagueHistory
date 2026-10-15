using League.Models;

namespace League.Services;

/// <summary>A timed ally summoner spell on one poll: whose, and its state.</summary>
public sealed record SummonerTimerState(string Key, PlayerSummoner Summoner);

/// <summary>
/// Announces the user's ally summoner timers once each when they come back: a timer counts once per start, so a
/// restarted timer can announce again, and one already past when first seen (a program started mid-game) is quiet.
/// </summary>
public sealed class SummonerTimerAlerts
{
    private readonly Dictionary<(string Key, int Slot), (double Started, bool Ready)> _timers = new();

    /// <summary>The timers that came back since the last poll.</summary>
    public SummonerTimerState[] Update(IEnumerable<SummonerTimerState> timers, double gameTime)
    {
        var ready = new List<SummonerTimerState>();
        var seen = new HashSet<(string, int)>();
        foreach (var timer in timers)
        {
            if (timer.Summoner.Started is not double started || timer.Summoner.ReadyAt is not double readyAt)
            {
                continue;
            }

            var slot = (timer.Key, timer.Summoner.Slot);
            seen.Add(slot);
            bool now = gameTime >= readyAt;
            if (!_timers.TryGetValue(slot, out var before) || before.Started != started)
            {
                _timers[slot] = (started, now);
                continue;
            }

            if (now && !before.Ready)
            {
                ready.Add(timer);
            }

            _timers[slot] = (started, now || before.Ready);
        }

        foreach (var gone in _timers.Keys.Where(slot => !seen.Contains(slot)).ToArray())
        {
            _timers.Remove(gone);
        }

        return ready.ToArray();
    }

    public void Reset() => _timers.Clear();
}
