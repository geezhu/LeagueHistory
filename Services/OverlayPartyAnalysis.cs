using League.Clients;
using League.Models;
using League.Networking;

namespace League.Services;

public static class OverlayPartyAnalysis
{
    // Two groups per side at most. Colors remain readable against the dark widget background.
    private static readonly int[] Colors = [0xff8d9e, 0x82baff, 0xcba2ff, 0xffce7a];

    public static IReadOnlyDictionary<string, OverlayPartyMarker> ForRoster(
        LoadingRoster? roster,
        IEnumerable<PlayerMatchInfo> cached
    )
    {
        var result = new Dictionary<string, OverlayPartyMarker>();
        if (roster == null)
        {
            return result;
        }

        var infos = cached.Where(c => c?.Player != null).ToArray();
        foreach (var team in roster.Players.Where(p => p.Team is 100 or 200).GroupBy(p => p.Team))
        {
            var entries = EligiblePlayers(roster, team, infos);
            int[] parents = Enumerable.Range(0, entries.Count).ToArray();
            int Root(int i) => parents[i] == i ? i : parents[i] = Root(parents[i]);
            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = i + 1; j < entries.Count; j++)
                {
                    if (
                        entries[i]
                            .Matches.Intersect(entries[j].Matches)
                            .Take(PartyDetector.DefaultMatchThreshold)
                            .Count() >= PartyDetector.DefaultMatchThreshold
                    )
                    {
                        parents[Root(j)] = Root(i);
                    }
                }
            }

            int color = team.Key == 100 ? 0 : 2;
            foreach (var group in Enumerable.Range(0, entries.Count).GroupBy(Root).Where(g => g.Count() >= 2))
            {
                var marker = new OverlayPartyMarker(group.Count(), Colors[color++]);
                foreach (int index in group)
                {
                    result[entries[index].Player.Key] = marker;
                }
            }
        }
        return result;
    }

    private static List<(LoadingPlayer Player, HashSet<string> Matches)> EligiblePlayers(
        LoadingRoster roster,
        IEnumerable<LoadingPlayer> team,
        PlayerMatchInfo[] infos
    )
    {
        var entries = new List<(LoadingPlayer Player, HashSet<string> Matches)>();
        foreach (var p in team.Take(5))
        {
            if (OverlayPlayerRecords.Find(roster, p, infos) is { } info)
            {
                entries.Add(
                    (p, info.MatchKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.Ordinal))
                );
            }
        }
        return entries;
    }
}
