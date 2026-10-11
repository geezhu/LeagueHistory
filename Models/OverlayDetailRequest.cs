using Newtonsoft.Json.Linq;

namespace League.Models;

// Only a current roster slot can request data; no URLs, paths or account IDs cross this channel.
public sealed record OverlayDetailRequest(string Id, string Session, int Slot, int ChampionId)
{
    public static OverlayDetailRequest? Parse(JObject value, string session, int[] heroes, long now)
    {
        if (
            value["protocol"]?.Type != JTokenType.Integer
            || value["protocol"]!.ToString() != "1"
            || value["session"]?.Type != JTokenType.String
            || (string?)value["session"] != session
            || session.Length == 0
        )
            return null;
        if (
            value["id"]?.Type != JTokenType.String
            || value["updatedAt"]?.Type != JTokenType.Integer
            || value["slot"]?.Type != JTokenType.Integer
            || value["championId"]?.Type != JTokenType.Integer
        )
            return null;
        string id = (string?)value["id"] ?? "";
        if (
            !long.TryParse(value["updatedAt"]!.ToString(), out long stamp)
            || stamp < 0
            || stamp > now
            || !int.TryParse(value["slot"]!.ToString(), out int slot)
            || !int.TryParse(value["championId"]!.ToString(), out int hero)
        )
            return null;
        long age = now - stamp;
        if (!Guid.TryParse(id, out _) || age < 0 || age > 30000)
            return null;
        if (slot == -1 && hero == 0)
            return new(id, session, slot, hero);
        return slot >= 0 && slot < heroes.Length && hero > 0 && heroes[slot] == hero
            ? new(id, session, slot, hero)
            : null;
    }
}
