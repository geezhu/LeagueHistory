using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>In-game facts for the overlay: scoreboard insight, plus the active player's panel once loaded.</summary>
/// <param name="Revision">Changes only when the panel content changes, so the widget skips identical redraws.</param>
public sealed record OverlayLiveState(LiveMatchInsight Insight, JArray Sections, string Revision)
{
    /// <summary>Minion wave estimate; null for modes without lane minions.</summary>
    public MinionWaveForecast? Waves { get; init; }
}
