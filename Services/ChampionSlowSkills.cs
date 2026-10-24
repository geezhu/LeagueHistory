using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>An ability's strongest enemy slow at its top rank, in percent, and its cast range.</summary>
public sealed record SlowSkill(string Key, int Percent, int Range)
{
    /// <summary>How long one slow lasts at the top rank, in seconds; 0 when the data names no duration.</summary>
    public double Duration { get; init; }

    /// <summary>A zone that outlasts the slow (a wall, a trail) and may keep slowing; 0 when there is none.</summary>
    public double Zone { get; init; }

    /// <summary>The slow at each rank from the first; the fields above are the strongest of them.</summary>
    public SlowRank[] Ranks { get; init; } = [];
}

/// <summary>A slow at one ability rank: percent, seconds one slow lasts and a longer zone (0 when none).</summary>
public sealed record SlowRank(int Percent, double Duration, double Zone);

/// <summary>The slow a champion of a given level can have, the rank it is read at (from 1) and why, in words.</summary>
public sealed record SlowAtLevel(SlowRank Slow, int Rank, string Basis);

/// <summary>
/// Slows from CommunityDragon's extraction of the game's champion data
/// (game/data/characters/{alias}/{alias}.bin.json): spell DataValues named like "SlowAmount" at the ability's own
/// ranks (index 0 is unlearned), read from
/// the ability's spell and its sub-spells. Self slows, attack-speed slows, resistances and ratios are not enemy
/// slows, and only abilities whose Chinese description (<see cref="ChineseChampionData"/>) mentions 减速 count.
/// </summary>
public static class ChampionSlowSkills
{
    /// <summary>The page lists ranged slows of at least this strength first; melee and weaker slows wait.</summary>
    public const int StrongPercent = 40;

    public const int RangedReach = 600;

    // Global or map-wide "ranges" mark a cast anywhere, not a reach.
    private const double GlobalRange = 5000;

    private static readonly string[] Skip =
    [
        "self",
        "attack",
        "resist",
        "ratio",
        "multiplier",
        "duration",
        "time",
        "decay",
        "radius",
        "range",
        "count",
        "stack",
    ];

    // Duration names: the slow's own, a lingering zone's, then a general debuff duration when the slow has none.
    private static readonly string[] TimeWords = ["duration", "time", "lifetime"];
    private static readonly string[] ZoneWords =
    [
        "zone",
        "field",
        "cloud",
        "trail",
        "ground",
        "area",
        "pillar",
        "wall",
        "linger",
    ];
    private static readonly HashSet<string> GeneralDurations =
    [
        "debuffduration",
        "ccduration",
        "rccduration",
        "duration",
        "wduration",
        "baseduration",
    ];

    // Longer values are lifetimes of objects or recasts, not the slow a target feels.
    private const double LongestDuration = 30;

    public static bool IsRangedStrong(SlowSkill slow) => slow.Percent >= StrongPercent && slow.Range >= RangedReach;

    /// <summary>
    /// The highest rank a champion of this level can have put into the ability; 0 before it can be learned. A
    /// basic ability gains a rank every two levels (1, 3, 5, …), Udyr's six-rank abilities included. A three-rank
    /// ultimate opens at 6, 11 and 16; a four-rank one (Karma, Jayce, Elise, Nidalee) starts at level 1.
    /// </summary>
    public static int RankAt(string key, int ranks, int level)
    {
        if (key != "R" || ranks > 4)
        {
            return Math.Clamp((level + 1) / 2, 0, ranks);
        }

        int[] opens = ranks == 4 ? [1, 6, 11, 16] : [6, 11, 16];
        return Math.Min(ranks, opens.Count(open => level >= open));
    }

    /// <summary>The level an ability's first rank opens at.</summary>
    public static int FirstLevel(string key, int ranks) => key == "R" && ranks is >= 1 and < 4 ? 6 : 1;

    /// <summary>
    /// The slow at the highest rank the champion's level allows: how the ranks were spent is unknown, so this is
    /// the most it can be now. An unknown level (0, before 2999 reports one) falls back to the top rank.
    /// </summary>
    public static SlowAtLevel? AtLevel(ControlSkill skill, int level)
    {
        if (skill.Slow is not int top)
        {
            return null;
        }

        var ranks = skill.SlowRanks;
        if (ranks.Length == 0 || ranks.Any(rank => rank.Percent == 0))
        {
            return new(new(top, skill.SlowDuration, skill.SlowZone), Math.Max(1, ranks.Length), "按满级数值");
        }

        if (level <= 0)
        {
            return new(ranks[^1], ranks.Length, $"等级未知，按满级 {ranks.Length}/{ranks.Length} 级");
        }

        int reached = RankAt(skill.Key, ranks.Length, level);
        return reached > 0
            ? new(ranks[reached - 1], reached, $"{level} 级时该技能最高 {reached}/{ranks.Length} 级")
            : new(ranks[0], 1, $"{level} 级还不能学（{FirstLevel(skill.Key, ranks.Length)} 级可学），按 1 级显示");
    }

    public static SlowSkill[] Parse(JObject bin, JObject champion)
    {
        string alias = (string?)champion["alias"] ?? "";
        var scripts =
            (bin[$"Characters/{alias}/CharacterRecords/Root"]?["spellNames"] as JArray)?.Values<string>().ToArray()
            ?? [];
        var abilities = (champion["spells"] as JArray ?? new())
            .OfType<JObject>()
            .GroupBy(s => ((string?)s["spellKey"] ?? "").ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var result = new List<SlowSkill>();
        for (int i = 0; i < Math.Min(4, scripts.Length); i++)
        {
            string key = "QWER"[i].ToString();
            string script = scripts[i] ?? "";
            if (
                script.Length == 0
                || !abilities.TryGetValue(key, out var ability)
                || !((string?)ability["description"] + (string?)ability["dynamicDescription"]).Contains("减速")
            )
            {
                continue;
            }

            // The ability's spell and its sub-spells ("ZiggsE" and "ZiggsEMine") share the ability path.
            var spells = bin.Properties()
                .Where(p =>
                    p.Name.Contains("/" + script, StringComparison.OrdinalIgnoreCase) && p.Value["mSpell"] is JObject
                )
                .Select(p =>
                    (
                        Main: p.Name.EndsWith("/" + script, StringComparison.OrdinalIgnoreCase),
                        Spell: (JObject)p.Value["mSpell"]!
                    )
                )
                .ToArray();
            int ranks = Ranks(spells.Where(s => s.Main).Select(s => s.Spell).FirstOrDefault(), key);
            int percent = spells.Select(s => Slow(s.Spell, ranks)).DefaultIfEmpty(0).Max();
            int reach = spells.Where(s => s.Main).Select(s => Reach(s.Spell, ranks)).DefaultIfEmpty(0).Max();
            if (reach == 0)
            {
                reach = spells.Select(s => Reach(s.Spell, ranks)).DefaultIfEmpty(0).Max();
            }

            if (percent > 0)
            {
                var values = spells.SelectMany(s => Values(s.Spell, ranks)).Where(v => v.Ranks.Length > 0).ToArray();
                var (duration, zone) = Timing(values, Top);
                result.Add(
                    new(key, percent, reach)
                    {
                        Duration = duration,
                        Zone = zone,
                        Ranks = Enumerable.Range(0, ranks).Select(rank => AtRank(values, rank)).ToArray(),
                    }
                );
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// The slows under each game mode whose value overrides change them (Teemo R slows 20% in ARAM), by lower-case
    /// mode; modes that leave the slows as they are are left out.
    /// </summary>
    public static Dictionary<string, SlowSkill[]> ParseModes(JObject bin, JObject champion, SlowSkill[] slows)
    {
        string usual = JsonConvert.SerializeObject(slows);
        var result = new Dictionary<string, SlowSkill[]>();
        foreach (string mode in OverrideModes(bin))
        {
            var moded = Parse(ModeView(bin, mode), champion);
            if (JsonConvert.SerializeObject(moded) != usual)
            {
                result[mode.ToLowerInvariant()] = moded;
            }
        }

        return result;
    }

    private static IEnumerable<string> OverrideModes(JObject bin) =>
        bin.Properties()
            .Select(p => (p.Value as JObject)?["mSpell"]?["DataValuesModeOverride"])
            .OfType<JObject>()
            .SelectMany(overrides => overrides.Properties().Select(mode => mode.Name))
            .Where(mode => mode != "__type" && !mode.StartsWith('{'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // A copy of the game data with each spell's values replaced by the mode's, so the usual parsing reads that mode.
    private static JObject ModeView(JObject bin, string mode)
    {
        var view = (JObject)bin.DeepClone();
        foreach (var spell in view.Properties().Select(p => (p.Value as JObject)?["mSpell"]).OfType<JObject>())
        {
            if (
                spell["DataValuesModeOverride"]?[mode]?["SpellDataValues"] is not JArray overrides
                || spell["DataValues"] is not JArray values
            )
            {
                continue;
            }

            foreach (var value in overrides.OfType<JObject>())
            {
                var usual = values.OfType<JObject>().FirstOrDefault(v => (string?)v["name"] == (string?)value["name"]);
                if (usual != null)
                {
                    usual["values"] = value["values"]?.DeepClone();
                }
                else
                {
                    values.Add(value.DeepClone());
                }
            }
        }

        return view;
    }

    /// <summary>Adds the ranged strong slows to the control skills, in ability order.</summary>
    public static ControlSkill[] Merge(ControlSkill[] controls, IEnumerable<SlowSkill> slows, JObject champion)
    {
        var skills = controls.ToList();
        foreach (var slow in slows.Where(IsRangedStrong))
        {
            int index = skills.FindIndex(skill => skill.Key == slow.Key);
            if (index >= 0)
            {
                skills[index] = skills[index] with
                {
                    Slow = slow.Percent,
                    Range = slow.Range,
                    SlowDuration = slow.Duration,
                    SlowZone = slow.Zone,
                    SlowRanks = slow.Ranks,
                };
                continue;
            }

            var spell = (champion["spells"] as JArray ?? new())
                .OfType<JObject>()
                .FirstOrDefault(s => ((string?)s["spellKey"] ?? "").ToUpperInvariant() == slow.Key);
            skills.Add(
                new(slow.Key, (string?)spell?["name"] ?? slow.Key, [], ChampionControlSkills.Summary(spell))
                {
                    Slow = slow.Percent,
                    Range = slow.Range,
                    SlowDuration = slow.Duration,
                    SlowZone = slow.Zone,
                    SlowRanks = slow.Ranks,
                }
            );
        }

        string[] order = ["被动", "Q", "W", "E", "R"];
        return skills.OrderBy(skill => Array.IndexOf(order, skill.Key)).ToArray();
    }

    // Values past the last rank are unreachable: reading five ranks of an ultimate overstated its slow. The tooltip
    // data counts the ranks (3 for most ultimates, 4 for Karma's, 6 for Udyr's); the client's maxLevel is always 0.
    public static int Ranks(JObject? spell, string key) =>
        spell?.SelectToken("mClientData.mTooltipData.mLists.LevelUp.levelCount") is { Type: JTokenType.Integer } count
        && (int)count is >= 1 and <= 6
            ? (int)count
        : key == "R" ? 3
        : 5;

    private static int Slow(JObject spell, int levels) => Slow(Values(spell, levels), Top);

    // Picks a value from an ability's ranks: the strongest, or the one at a given rank.
    private static double Top(double[] ranks) => ranks.Max(Math.Abs);

    private static SlowRank AtRank((string Name, double[] Ranks)[] values, int rank)
    {
        double Pick(double[] ranks) => Math.Abs(ranks[Math.Min(rank, ranks.Length - 1)]);
        var (duration, zone) = Timing(values, Pick);
        return new(Slow(values, Pick), duration, zone);
    }

    private static int Slow(IEnumerable<(string Name, double[] Ranks)> values, Func<double[], double> pick)
    {
        double best = 0;
        foreach (var (name, ranks) in values)
        {
            // Movement speed modifiers are slows only when negative; positive ones are hastes.
            bool slow =
                name.Contains("slow")
                || ((name.Contains("movespeed") || name.Contains("msmod")) && ranks.Any(v => v < 0));
            if (!slow || ranks.Length == 0 || Skip.Any(name.Contains))
            {
                continue;
            }

            double top = pick(ranks);
            double percent = top <= 1 ? top * 100 : top;
            if (percent is >= 5 and <= 100)
            {
                best = Math.Max(best, percent);
            }
        }

        return (int)Math.Round(best);
    }

    /// <summary>
    /// The slow's own duration decides. A zone counts only when the ability has no slow duration; when it outlasts
    /// the slow it is kept apart, because standing in it may slow again.
    /// </summary>
    private static (double Duration, double Zone) Timing(
        (string Name, double[] Ranks)[] values,
        Func<double[], double> pick
    )
    {
        double Longest(Func<string, bool> match) =>
            values
                .Where(v => match(v.Name))
                .Select(v => pick(v.Ranks))
                .Where(top => top is > 0 and <= LongestDuration)
                .DefaultIfEmpty(0)
                .Max();
        bool Timed(string name) => TimeWords.Any(name.Contains);
        double own = Longest(name =>
            (name.Contains("slow") || name.Contains("chill"))
            && Timed(name)
            && !name.Contains("rate")
            && !name.Contains("multiplier")
            && !name.Contains("ticks")
        );
        if (own == 0)
        {
            own = Longest(GeneralDurations.Contains);
        }

        double zone = Longest(name =>
            ZoneWords.Any(name.Contains) && Timed(name) && !name.Contains("slow") && !name.Contains("exhaust")
        );
        return own > 0 ? (own, zone > own ? zone : 0) : (zone, 0);
    }

    private static IEnumerable<(string Name, double[] Ranks)> Values(JObject spell, int levels) =>
        (spell["DataValues"] as JArray ?? new())
            .OfType<JObject>()
            .Select(value =>
                (
                    ((string?)value["name"] ?? "").ToLowerInvariant(),
                    (value["values"] as JArray ?? new())
                        .Skip(1)
                        .Take(levels)
                        .Select(v => v.Type is JTokenType.Float or JTokenType.Integer ? (double)v : 0)
                        .ToArray()
                )
            );

    private static int Reach(JObject spell, int levels) =>
        (int)
            (spell["castRange"] as JArray ?? new())
                .Skip(1)
                .Take(levels)
                .Select(v => v.Type is JTokenType.Float or JTokenType.Integer ? (double)v : 0)
                .Where(v => v < GlobalRange)
                .DefaultIfEmpty(0)
                .Max();
}
