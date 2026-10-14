namespace League.Models;

public enum PressureLevel
{
    Low,
    Medium,
    High,
}

/// <summary>
/// What playing four against five costs against this enemy, and what a full reset is worth for this team, from
/// arammeta's capability sums. A strength is a sum over its "lacking" threshold, so 1.0 is just enough.
/// </summary>
public sealed record TeamPressure(PressureLevel Siege, PressureLevel Dive, PressureLevel SustainGap)
{
    // Initial values; the death-timing diagnostics record each verdict so they can be tuned against real games.
    private const double HighDifference = 0.3;
    private const double SelfShare = 0.4;
    private const int KnownChampions = 3;

    private static readonly string[] SelfRoleKeys = ["front", "engage", "wave", "sustain"];

    /// <summary>Capabilities this player carries for the team, such as 前排; the team loses them while dead.</summary>
    public string[] SelfRoles { get; init; } = [];

    /// <summary>Null when either team has too few champions with statistics to judge.</summary>
    public static TeamPressure? From(AramMetaIndex index, IReadOnlyList<int> own, IReadOnlyList<int> enemy, int self)
    {
        var ours = AramTeamScore.Evaluate(index, own);
        var theirs = AramTeamScore.Evaluate(index, enemy);
        if (Known(ours) < KnownChampions || Known(theirs) < KnownChampions)
        {
            return null;
        }

        // Towers fall to poke and wave clear; while one player is dead the rest defend with their own wave clear.
        double siege = Average(theirs, "poke", "wave") - Strength(ours, "wave");
        double dive = Average(theirs, "engage", "cc") - Average(ours, "front", "cc");
        var others = AramTeamScore.Evaluate(index, own.Where(id => id != self).ToArray());
        return new(Level(siege), Level(dive), SustainLevel(Strength(ours, "sustain")))
        {
            SelfRoles = SelfRoleKeys
                .Select(key => (Team: Capability(ours, key), Others: Capability(others, key)))
                .Where(pair => pair.Team is { Sum: > 0 } && 1 - pair.Others!.Sum / pair.Team.Sum >= SelfShare)
                .Select(pair => pair.Team!.Name)
                .ToArray(),
        };
    }

    private static int Known(AramTeamEvaluation evaluation) => evaluation.Team.Length - evaluation.Missing.Length;

    private static AramTeamCapability? Capability(AramTeamEvaluation evaluation, string key) =>
        evaluation.Capabilities.FirstOrDefault(capability => capability.Key == key);

    private static double Strength(AramTeamEvaluation evaluation, string key) =>
        Capability(evaluation, key) is { Threshold: > 0 } capability ? capability.Sum / capability.Threshold : 1;

    private static double Average(AramTeamEvaluation evaluation, string first, string second) =>
        (Strength(evaluation, first) + Strength(evaluation, second)) / 2;

    // Below the threshold the team lacks sustain outright; just above it, a reset still helps.
    private static PressureLevel SustainLevel(double strength) =>
        strength < 1 ? PressureLevel.High
        : strength < 1 + HighDifference ? PressureLevel.Medium
        : PressureLevel.Low;

    private static PressureLevel Level(double difference) =>
        difference >= HighDifference ? PressureLevel.High
        : difference <= -HighDifference ? PressureLevel.Low
        : PressureLevel.Medium;
}
