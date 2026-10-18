using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// Character facts Data Dragon leaves out, read from the champion's record in CommunityDragon's extraction of the
/// game data (Characters/{alias}/CharacterRecords/Root). Zero means the record does not say.
/// </summary>
public sealed record ChampionRecord
{
    public static readonly ChampionRecord None = new();

    // The game's defaults for fields the extraction leaves out because they equal them.
    private const double DefaultWindup = 0.3;
    private const double DefaultGameplayRadius = 65;

    /// <summary>What bonus attack speed is multiplied by; most champions use their base attack speed.</summary>
    public double AttackSpeedRatio { get; init; }

    /// <summary>The share of an attack spent before it lands (0.2 for 20%).</summary>
    public double AttackWindup { get; init; }

    public double AcquisitionRange { get; init; }

    public double SelectionRadius { get; init; }

    public double GameplayRadius { get; init; }

    public double PathfindingRadius { get; init; }

    public static ChampionRecord Parse(JObject bin, string alias)
    {
        if (bin[$"Characters/{alias}/CharacterRecords/Root"] is not JObject root)
        {
            return None;
        }

        double Number(JToken? token) =>
            token?.Type is JTokenType.Integer or JTokenType.Float
            && (double)token is var value
            && double.IsFinite(value)
                ? Math.Max(0, value)
                : 0;
        var attack = root["basicAttack"] as JObject;
        // Champions with their own attack timing list the cast and total times; the rest offset the default share.
        double cast = Number(attack?["mAttackCastTime"]),
            total = Number(attack?["mAttackTotalTime"]);
        double windup =
            cast > 0 && total > 0 ? cast / total
            : attack?["mAttackDelayCastOffsetPercent"]?.Type is JTokenType.Float or JTokenType.Integer
                ? DefaultWindup + (double)attack["mAttackDelayCastOffsetPercent"]!
            : DefaultWindup;
        double gameplay = Number(root["overrideGameplayCollisionRadius"]);
        return new()
        {
            AttackSpeedRatio = Number(root["attackSpeedRatioModifiable"]?["baseValue"]),
            AttackWindup = Math.Clamp(windup, 0, 1),
            AcquisitionRange = Number(root["acquisitionRange"]),
            SelectionRadius = Number(root["selectionRadius"]),
            GameplayRadius = gameplay > 0 ? gameplay : DefaultGameplayRadius,
            PathfindingRadius = Number(root["pathfindingCollisionRadius"]),
        };
    }
}
