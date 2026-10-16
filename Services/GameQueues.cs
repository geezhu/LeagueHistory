using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// The queue a game's statistics come from. Custom games have no matchmaking queue (-1 in the client), so the game
/// mode decides: KIWI is ARAM: Mayhem (2400, as is Mayhem's other queue 3270), ARAM is 450, and so on.
/// </summary>
public static class GameQueues
{
    /// <summary>The queue for statistics, from the client's game data (<c>gameData</c> of the gameflow session).</summary>
    public static int Effective(JToken? gameData)
    {
        var queue = gameData?["queue"];
        return PlayerChampionTier.NormalizeQueue((int?)queue?["id"] ?? 0, (string?)queue?["gameMode"] ?? "");
    }

    /// <summary>
    /// The queue for a player card's mode win rate. A custom Summoner's Rift game matches no queue players are
    /// ranked in, so it keeps the client's value and the card shows all recent games, as before.
    /// </summary>
    public static string ForHistory(JToken? gameData)
    {
        var queue = gameData?["queue"];
        int id = (int?)queue?["id"] ?? 0;
        string mode = (string?)queue?["gameMode"] ?? "";
        if (id <= 0 && mode.Equals("CLASSIC", StringComparison.OrdinalIgnoreCase))
        {
            return id.ToString();
        }

        int effective = Effective(gameData);
        return effective == 0 ? "" : effective.ToString();
    }
}
