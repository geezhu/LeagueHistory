namespace League.Models;

/// <summary>One finished game as the end-of-game panel shows it, read from the client's end-of-game stats.</summary>
public sealed record EndOfGameSummary(
    long GameId,
    string PlayerName,
    bool Win,
    TimeSpan Duration,
    int ChampionId,
    EndOfGameKda Kda,
    OwnMatchScore? Score,
    IReadOnlyList<string> Honors,
    IReadOnlyList<EndOfGameTeammate> Teammates
);

public readonly record struct EndOfGameKda(int Kills, int Deaths, int Assists)
{
    public override string ToString() => $"{Kills}/{Deaths}/{Assists}";
}

/// <summary>
/// A player on the local player's team, the local player excluded. Score is null outside the modelled queues; honors
/// are the keys of WeGame's honor icons this player earned.
/// </summary>
public sealed record EndOfGameTeammate(
    string Name,
    int ChampionId,
    EndOfGameKda Kda,
    OwnMatchScore? Score,
    IReadOnlyList<string> Honors
)
{
    /// <summary>A hidden or unmatched name cannot be addressed in chat, so its row has nothing to send.</summary>
    public bool CanBeNamed => !string.IsNullOrWhiteSpace(Name);
}
