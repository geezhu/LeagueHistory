using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// Champions the Game Bar user marked for respawn reminders, as written to <c>respawn-marks.json</c>.
/// Slots index the snapshot's ten rows; heroes guard against a slot that changed champion since marking.
/// <see cref="Test"/> changes when the user turns sound on, asking for one sample chime.
/// </summary>
public sealed record RespawnMarks(string Session, int[] Slots, int[] Heroes, bool Sound, long Test)
{
    public const int MaxSlots = 10;

    public static RespawnMarks? Parse(JObject value, string session)
    {
        if (
            value["protocol"]?.Type != JTokenType.Integer
            || value["protocol"]!.ToString() != "1"
            || value["session"]?.Type != JTokenType.String
            || (string?)value["session"] != session
            || session.Length == 0
            || value["slots"] is not JArray slots
            || value["heroes"] is not JArray heroes
            || slots.Count != heroes.Count
            || slots.Count > MaxSlots
            || value["sound"]?.Type != JTokenType.Boolean
            || value["test"]?.Type != JTokenType.Integer
        )
        {
            return null;
        }

        if (
            slots.Concat(heroes).Any(token => token.Type != JTokenType.Integer)
            || slots.Values<int>().Any(slot => slot is < 0 or >= MaxSlots)
        )
        {
            return null;
        }

        return new(
            session,
            slots.Values<int>().ToArray(),
            heroes.Values<int>().ToArray(),
            (bool)value["sound"]!,
            (long)value["test"]!
        );
    }
}
