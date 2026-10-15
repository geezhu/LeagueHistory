using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// An augment recorded on the augment page for a roster player (empty for the widget's own player, as widgets before
/// per-player augments wrote it). Takedown augments count from the anchor's time and stacks; for the others the
/// anchor's stacks are the count the user keeps, and fixed augments have none.
/// </summary>
public sealed record AugmentSelection(string Player, TrackedAugment Augment, AugmentStackAnchor Anchor)
{
    /// <summary>The augment's choice key (an ability, a form, a resistance); empty for its first choice.</summary>
    public string Choice { get; init; } = "";
}

/// <summary>
/// Recorded augments as the widget writes them to <c>augment-stacks.json</c>. 2999 does not report augments, so
/// the user adds each one by hand, for any player, and may set or correct the stacks; each change is a new anchor.
/// </summary>
public static class AugmentSelections
{
    public const int MaxStacks = 999;

    private const int MaxPlayers = 10;
    private const int MaxPlayerKey = 256;

    public static AugmentSelection[]? Parse(JObject value, string session)
    {
        if (
            value["protocol"]?.Type != JTokenType.Integer
            || value["protocol"]!.ToString() != "1"
            || value["session"]?.Type != JTokenType.String
            || (string?)value["session"] != session
            || session.Length == 0
            || value["augments"] is not JArray augments
            || augments.Count > MaxPlayers * Enum.GetValues<TrackedAugment>().Length
        )
        {
            return null;
        }

        var result = new List<AugmentSelection>();
        foreach (var entry in augments)
        {
            if (
                entry is not JObject row
                || row["player"] is { Type: not JTokenType.String }
                || ((string?)row["player"] ?? "").Length > MaxPlayerKey
                || row["augment"]?.Type != JTokenType.String
                || !Enum.GetNames<TrackedAugment>().Contains((string)row["augment"]!)
                || !Enum.TryParse((string)row["augment"]!, out TrackedAugment augment)
                || row["time"]?.Type is not (JTokenType.Integer or JTokenType.Float)
                || row["stacks"]?.Type != JTokenType.Integer
                || row["choice"] is { Type: not JTokenType.String }
                || !ValidChoice(augment, (string?)row["choice"] ?? "")
                || result.Any(selection =>
                    selection.Augment == augment && selection.Player == ((string?)row["player"] ?? "")
                )
            )
            {
                return null;
            }

            double time = (double)row["time"]!;
            int stacks = (int)row["stacks"]!;
            if (!double.IsFinite(time) || time < 0 || stacks is < 0 or > MaxStacks)
            {
                return null;
            }

            result.Add(
                new((string?)row["player"] ?? "", augment, new(time, stacks)) { Choice = (string?)row["choice"] ?? "" }
            );
        }

        return result.ToArray();
    }

    private static bool ValidChoice(TrackedAugment augment, string choice) =>
        choice.Length == 0 || AugmentCatalog.Get(augment).Choices.Any(c => c.Key == choice);
}
