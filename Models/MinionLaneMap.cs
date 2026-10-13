namespace League.Models;

public enum LaneStructureKind
{
    Outer,
    Inner,
    InhibitorTurret,
    Inhibitor,
    Nexus,
}

/// <summary>A structure on a lane, as its distance along the lane path from the blue (ORDER) inhibitor.</summary>
public sealed record LaneStructure(int Team, LaneStructureKind Kind, double FromBlue)
{
    public string Label =>
        Kind switch
        {
            LaneStructureKind.Outer => "一塔",
            LaneStructureKind.Inner => "二塔",
            LaneStructureKind.InhibitorTurret => "高地塔",
            LaneStructureKind.Inhibitor => "水晶",
            _ => "门牙塔",
        };
}

/// <summary>Lane key as used in structure names: top, mid or bot.</summary>
public sealed record MinionLane(string Key, string Name, double Length, LaneStructure[] Structures)
{
    /// <summary>Turrets that must fall in order before the inhibitor, outermost first, for one team.</summary>
    public LaneStructure[] Turrets(int team) =>
        Structures
            .Where(s => s.Team == team && s.Kind is not (LaneStructureKind.Inhibitor or LaneStructureKind.Nexus))
            .OrderBy(s => team == 100 ? -s.FromBlue : s.FromBlue)
            .ToArray();

    public LaneStructure Inhibitor(int team) =>
        Structures.First(s => s.Team == team && s.Kind == LaneStructureKind.Inhibitor);

    public LaneStructure Nexus(int team) => Structures.First(s => s.Team == team && s.Kind == LaneStructureKind.Nexus);

    /// <summary>Path distance a wave of the given team walks from its own inhibitor to a structure.</summary>
    public double Distance(int team, LaneStructure target) => team == 100 ? target.FromBlue : Length - target.FromBlue;
}

/// <summary>
/// Lane paths built from BUILDING_KILL positions in 16.20 match timelines: inhibitor to inhibitor, with side
/// lanes bending at (1150, 13550) and (13550, 1150). Nexus turrets sit at their pair's midpoint behind the
/// inhibitor. Waves are assumed to start at their own inhibitor; field timing may refine both.
/// </summary>
public sealed record MinionLaneMap(MinionLane[] Lanes)
{
    public static readonly MinionLaneMap SummonersRift = new([
        Lane("top", "上路", 20093, [704, 3115, 6858], [13138, 16758, 19299], 1735, 1754),
        Lane("mid", "中路", 11904, [651, 2427, 4113], [7804, 9514, 11253], 1717, 1710),
        Lane("bot", "下路", 20251, [813, 3449, 7037], [13439, 17157, 19505], 1709, 1711),
    ]);

    public static readonly MinionLaneMap HowlingAbyss = new([
        Lane("mid", "兵线", 9149, [960, 2527], [6614, 8168], 1184, 1167),
    ]);

    /// <summary>
    /// Turret distances run from each base outward: blue lists base to outer, red lists outer to base.
    /// Three turrets per side are inhibitor, inner and outer turrets; two are inner and outer.
    /// </summary>
    private static MinionLane Lane(
        string key,
        string name,
        double length,
        double[] blue,
        double[] red,
        double blueNexus,
        double redNexus
    )
    {
        var kinds =
            blue.Length == 3
                ? new[] { LaneStructureKind.InhibitorTurret, LaneStructureKind.Inner, LaneStructureKind.Outer }
                : new[] { LaneStructureKind.Inner, LaneStructureKind.Outer };
        var structures = new List<LaneStructure>
        {
            new(100, LaneStructureKind.Nexus, -blueNexus),
            new(100, LaneStructureKind.Inhibitor, 0),
            new(200, LaneStructureKind.Inhibitor, length),
            new(200, LaneStructureKind.Nexus, length + redNexus),
        };
        structures.AddRange(blue.Select((d, i) => new LaneStructure(100, kinds[i], d)));
        structures.AddRange(red.Select((d, i) => new LaneStructure(200, kinds[kinds.Length - 1 - i], d)));
        return new(key, name, length, structures.OrderBy(s => s.FromBlue).ToArray());
    }
}
