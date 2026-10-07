using League.Clients;
using League.Models;
using League.Networking;

namespace League.Services;

public static class OverlayPartyAnalysis
{
    // Two groups per side at most. Colors remain readable against the dark widget background.
    private static readonly int[] Colors = [0xff8d9e, 0x82baff, 0xcba2ff, 0xffce7a];

    public static IReadOnlyDictionary<string, OverlayPartyMarker> ForRoster(LoadingRoster? roster, IEnumerable<PlayerMatchInfo> cached)
    {
        var result = new Dictionary<string, OverlayPartyMarker>();
        if (roster == null) return result;
        var infos = cached.Where(c => c?.Player != null).ToArray();
        static string Identity(LoadingPlayer p) => LoadingRosterIdentityService.HasPuuid(p.Puuid) ? "p:" + p.Puuid
            : p.SummonerId > 0 ? "s:" + p.SummonerId : "";
        var identities = roster.Players.Select(Identity).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        foreach (var team in roster.Players.Where(p => p.Team is 100 or 200).GroupBy(p => p.Team))
        {
            var entries = new List<(LoadingPlayer Player, HashSet<string> Matches)>();
            foreach (var p in team.Take(5))
            {
                string identity = Identity(p);
                if (p.IdentityHidden || identity.Length == 0 || identities[identity] != 1) continue;
                var matches = infos.Where(c => LoadingRosterIdentityService.HasPuuid(p.Puuid) ? c.Player.Puuid == p.Puuid
                    : c.Player.SummonerId == p.SummonerId).Take(2).ToArray();
                if (matches.Length != 1) continue;
                // A PUUID entry and a summoner-ID-only entry must not count the same account twice.
                if (roster.Players.Count(q => !q.IdentityHidden && (LoadingRosterIdentityService.HasPuuid(q.Puuid)
                    ? matches[0].Player.Puuid == q.Puuid : q.SummonerId > 0 && matches[0].Player.SummonerId == q.SummonerId)) != 1) continue;
                entries.Add((p, matches[0].MatchKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.Ordinal)));
            }
            int[] parents = Enumerable.Range(0, entries.Count).ToArray();
            int Root(int i) => parents[i] == i ? i : parents[i] = Root(parents[i]);
            for (int i = 0; i < entries.Count; i++)
            for (int j = i + 1; j < entries.Count; j++)
                if (entries[i].Matches.Intersect(entries[j].Matches).Take(PartyDetector.DefaultMatchThreshold).Count() >= PartyDetector.DefaultMatchThreshold)
                    parents[Root(j)] = Root(i);
            int color = team.Key == 100 ? 0 : 2;
            foreach (var group in Enumerable.Range(0, entries.Count).GroupBy(Root).Where(g => g.Count() >= 2))
            {
                var marker = new OverlayPartyMarker(group.Count(), Colors[color++]);
                foreach (int index in group) result[entries[index].Player.Key] = marker;
            }
        }
        return result;
    }
}
