using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>In-game facts for the overlay: scoreboard insight, plus the active player's panel once loaded.</summary>
/// <param name="Revision">Changes only when the panel content changes, so the widget skips identical redraws.</param>
public sealed record OverlayLiveState(LiveMatchInsight Insight, JArray Sections, string Revision)
{
    /// <summary>Minion wave estimate; null for modes without lane minions.</summary>
    public MinionWaveForecast? Waves { get; init; }

    /// <summary>The self player's stacking augments added in the Game Bar, counted up to this snapshot.</summary>
    public LiveAugmentStack[] AugmentStacks { get; init; } = [];

    /// <summary>Health lines by roster player key: real stats for the player, estimates for everyone else.</summary>
    public IReadOnlyDictionary<string, PlayerHealth> Health { get; init; } = new Dictionary<string, PlayerHealth>();

    /// <summary>Ability haste by roster player key: 2999's value for the player, estimates for everyone else.</summary>
    public IReadOnlyDictionary<string, PlayerHaste> Haste { get; init; } = new Dictionary<string, PlayerHaste>();

    /// <summary>Stats by roster player key: 2999's panel for the player, estimates for everyone else.</summary>
    public IReadOnlyDictionary<string, ChampionStatSheet> Sheets { get; init; } =
        new Dictionary<string, ChampionStatSheet>();

    /// <summary>The active player's ability ranks from 2999.</summary>
    public IReadOnlyDictionary<string, int> AbilityRanks { get; init; } = new Dictionary<string, int>();

    /// <summary>What each player's recorded augments add up to, by roster player key.</summary>
    public IReadOnlyDictionary<string, AugmentEffects> Effects { get; init; } =
        new Dictionary<string, AugmentEffects>();

    /// <summary>Each player's summoner spells by roster player key: timed for allies, icons only for enemies.</summary>
    public IReadOnlyDictionary<string, PlayerSummoner[]> Summoners { get; init; } =
        new Dictionary<string, PlayerSummoner[]>();

    /// <summary>The game time of this poll and when it was read (Unix milliseconds), for the widget's timers.</summary>
    public double GameTime { get; init; }

    public long GameTimeAt { get; init; }

    /// <summary>Summoner spell icons as Base64 PNG, by internal name.</summary>
    public IReadOnlyDictionary<string, string> SummonerIcons { get; init; } = new Dictionary<string, string>();

    /// <summary>Ultimate haste entered by hand on the augment page, by roster player key.</summary>
    public IReadOnlyDictionary<string, double> UltimateHaste { get; init; } = new Dictionary<string, double>();
}

public sealed record LiveAugmentStack(AugmentSelection Selection, AugmentStackState State);
