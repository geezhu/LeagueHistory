namespace League.PrimaryElection
{
    /// <summary>One preselection list: its config key, the name shown and the lane filter that suits it.</summary>
    public sealed record PreliminaryListInfo(string Key, string Name, ChampionPosition Lane);

    /// <summary>
    /// The preselection lists and which one champion select uses. Ranked and normal games use the list for the
    /// assigned position, bench modes (ARAM, Nexus Blitz, ARURF) the mode list; an empty or missing list falls back
    /// to the general one, which is also where lists saved before the split live.
    /// </summary>
    public static class PreliminaryLists
    {
        public const string General = "general";
        public const string Aram = "aram";

        public static readonly IReadOnlyList<PreliminaryListInfo> All =
        [
            new(General, "通用", ChampionPosition.All),
            new("top", "上单", ChampionPosition.Top),
            new("jungle", "打野", ChampionPosition.Jungle),
            new("middle", "中单", ChampionPosition.Mid),
            new("bottom", "下路", ChampionPosition.ADC),
            new("utility", "辅助", ChampionPosition.Support),
            new(Aram, "乱斗", ChampionPosition.All),
        ];

        /// <summary>Queues where champions are assigned and swapped from the bench.</summary>
        public static bool IsBenchQueue(int queueId) => queueId is 450 or 2400 or 900;

        public static string NameOf(string key) => All.FirstOrDefault(l => l.Key == key)?.Name ?? key;

        /// <summary>The stored list for the key, created when missing; the general list is <c>Heroes</c>.</summary>
        public static List<PreliminaryHero> Get(PreliminaryConfig config, string key)
        {
            if (key == General)
            {
                return config.Heroes ??= new();
            }

            config.PositionHeroes ??= new();
            if (!config.PositionHeroes.TryGetValue(key, out var list) || list == null)
            {
                list = new();
                config.PositionHeroes[key] = list;
            }

            return list;
        }

        /// <summary>
        /// The list champion select uses, in priority order, with its key for the log. The assigned position is the
        /// client's <c>assignedPosition</c> (top, jungle, middle, bottom, utility; empty in blind pick).
        /// </summary>
        public static (string Key, List<PreliminaryHero> Heroes) Resolve(
            PreliminaryConfig? config,
            int queueId,
            string? assignedPosition
        )
        {
            if (config == null)
            {
                return (General, new());
            }

            string? own =
                IsBenchQueue(queueId) ? Aram
                : All.Any(l => l.Key == assignedPosition && l.Key is not (General or Aram)) ? assignedPosition
                : null;
            if (own != null && Sorted(config.PositionHeroes?.GetValueOrDefault(own)) is { Count: > 0 } specific)
            {
                return (own, specific);
            }

            return (General, Sorted(config.Heroes));
        }

        /// <summary>Whether any list has a champion; an empty configuration needs no champion select work.</summary>
        public static bool AnyHeroes(PreliminaryConfig? config) =>
            config != null
            && ((config.Heroes?.Count ?? 0) > 0 || (config.PositionHeroes?.Values.Any(l => l?.Count > 0) ?? false));

        private static List<PreliminaryHero> Sorted(List<PreliminaryHero>? heroes) =>
            heroes?.OrderBy(h => h.Priority).ToList() ?? new();
    }
}
