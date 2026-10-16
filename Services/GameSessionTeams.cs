using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// Cleans the client's game session when it carries players from an earlier game (the user then appears on both
/// teams): only players whose champion is in the live game stay. Champions are unique within a game in every mode the
/// main window lists, so they tell the current players apart without names.
/// </summary>
public static class GameSessionTeams
{
    public static JObject KeepLive(JObject gameData, IReadOnlySet<int> liveChampions)
    {
        var copy = (JObject)gameData.DeepClone();
        foreach (string field in new[] { "teamOne", "teamTwo", "playerChampionSelections" })
        {
            copy[field] = new JArray(
                (copy[field] as JArray ?? new()).Where(p => liveChampions.Contains((int?)p["championId"] ?? 0))
            );
        }

        return copy;
    }
}
