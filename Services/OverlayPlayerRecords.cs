using League.Clients;
using League.Models;

namespace League.Services;

/// <summary>
/// Joins roster seats to the main window's cached player history. A seat gets a history only when its account is
/// unique in the roster and matches exactly one cache entry, so a history is never shown for the wrong player.
/// </summary>
public static class OverlayPlayerRecords
{
    public static PlayerMatchInfo? Find(LoadingRoster roster, LoadingPlayer player, IEnumerable<PlayerMatchInfo> cached)
    {
        string identity = Identity(player);
        if (player.IdentityHidden || identity.Length == 0 || roster.Players.Count(p => Identity(p) == identity) != 1)
        {
            return null;
        }

        var matches = cached
            .Where(c =>
                c?.Player != null
                && (
                    LoadingRosterIdentityService.HasPuuid(player.Puuid)
                        ? c.Player.Puuid == player.Puuid
                        : c.Player.SummonerId == player.SummonerId
                )
            )
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return null;
        }

        // A PUUID entry and a summoner-ID-only entry must not count the same account twice.
        var info = matches[0];
        int owners = roster.Players.Count(q =>
            !q.IdentityHidden
            && (
                LoadingRosterIdentityService.HasPuuid(q.Puuid)
                    ? info.Player.Puuid == q.Puuid
                    : q.SummonerId > 0 && info.Player.SummonerId == q.SummonerId
            )
        );
        return owners == 1 ? info : null;
    }

    private static string Identity(LoadingPlayer p) =>
        LoadingRosterIdentityService.HasPuuid(p.Puuid) ? "p:" + p.Puuid
        : p.SummonerId > 0 ? "s:" + p.SummonerId
        : "";
}
