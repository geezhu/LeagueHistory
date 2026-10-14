using System.Text.RegularExpressions;

namespace League.Models;

/// <summary>
/// Destroyed lane structures rebuilt from 2999 events. Lane turrets fall outermost first, so a team and lane
/// identify the next standing turret without its exact position. Two name formats are known:
/// <c>Turret_T1_L_03_A</c> (L top, R bot, C mid; C_06 / C_07 are the top / bot inhibitor turrets; C_01 / C_02
/// nexus) and <c>Turret_T1_L1_P3_x</c> (L0 bot, L1 mid, L2 top; positions above 3 are nexus turrets). The digit
/// format also names the team: <c>Turret_TChaos_L1_P3_x</c> and <c>Inhib_TOrder_L1_P1_x</c>, seen on the Howling
/// Abyss, where P3 and P2 fall before the inhibitor and P4 and P5 after it. Names matching neither are returned as
/// unknown instead of being guessed.
/// </summary>
public sealed partial class LaneStructureState
{
    private readonly Dictionary<(int Team, string Lane), int> _turrets = new();
    private readonly Dictionary<(int Team, string Lane), double?> _inhibitors = new();
    private readonly Dictionary<int, int> _nexusTurrets = new();

    public List<string> Unknown { get; } = new();

    public int TurretsDown(int team, string lane) => _turrets.GetValueOrDefault((team, lane));

    /// <summary>The game time the inhibitor fell, or null while it stands.</summary>
    public double? InhibitorDownSince(int team, string lane) => _inhibitors.GetValueOrDefault((team, lane));

    public int InhibitorsDown(int team) => _inhibitors.Count(i => i.Key.Team == team && i.Value.HasValue);

    public int NexusTurretsDown(int team) => _nexusTurrets.GetValueOrDefault(team);

    public static LaneStructureState From(IEnumerable<LiveMatchEvent> events, bool singleLane)
    {
        var state = new LaneStructureState();
        foreach (var e in events.OrderBy(e => e.Time))
        {
            switch (e.Name)
            {
                case "TurretKilled":
                    state.ApplyTurret(e.Structure, singleLane);
                    break;
                case "InhibKilled":
                    state.ApplyInhibitor(e.Structure, singleLane, e.Time);
                    break;
                case "InhibRespawned":
                    state.ApplyInhibitor(e.Structure, singleLane, null);
                    break;
            }
        }
        return state;
    }

    private void ApplyTurret(string name, bool singleLane)
    {
        if (ParseTurret(name, singleLane) is not { } turret)
        {
            Unknown.Add(name);
            return;
        }

        if (turret.Nexus)
        {
            _nexusTurrets[turret.Team] = NexusTurretsDown(turret.Team) + 1;
        }
        else
        {
            _turrets[(turret.Team, turret.Lane)] = TurretsDown(turret.Team, turret.Lane) + 1;
        }
    }

    private void ApplyInhibitor(string name, bool singleLane, double? time)
    {
        var match = InhibitorName().Match(name);
        if (!match.Success)
        {
            Unknown.Add(name);
            return;
        }

        int team = Team(match);
        string lane = singleLane ? "mid" : Lane(match.Groups["lane"].Value);
        if (lane.Length == 0)
        {
            Unknown.Add(name);
            return;
        }
        _inhibitors[(team, lane)] = time;
    }

    public static (int Team, string Lane, bool Nexus)? ParseTurret(string name, bool singleLane)
    {
        if (LetterTurret().Match(name) is { Success: true } letter)
        {
            int index = int.Parse(letter.Groups["index"].Value);
            string side = letter.Groups["lane"].Value;
            bool nexus = side == "C" && index <= 2;
            string lane = side switch
            {
                "L" => "top",
                "R" => "bot",
                _ when index == 6 => "top",
                _ when index == 7 => "bot",
                _ => "mid",
            };
            return (Team(letter), singleLane ? "mid" : lane, nexus);
        }

        if (DigitTurret().Match(name) is { Success: true } digit)
        {
            bool nexus = int.Parse(digit.Groups["position"].Value) > 3;
            string lane = Lane(digit.Groups["lane"].Value);
            return lane.Length == 0 ? null : (Team(digit), singleLane ? "mid" : lane, nexus);
        }

        return null;
    }

    private static int Team(Match match) => match.Groups["team"].Value is "1" or "Order" ? 100 : 200;

    private static string Lane(string value) =>
        value switch
        {
            "L" or "2" => "top",
            "C" or "1" => "mid",
            "R" or "0" => "bot",
            _ => "",
        };

    [GeneratedRegex(@"^Turret_T(?<team>[12])_(?<lane>[LCR])_(?<index>\d{2})")]
    private static partial Regex LetterTurret();

    [GeneratedRegex(@"^Turret_T(?<team>[12]|Order|Chaos)_L(?<lane>\d)_P(?<position>\d)")]
    private static partial Regex DigitTurret();

    // Barracks_T1_L1 / Barracks_T2_C1 / Barracks_T1_R1, or Inhib_T1_L0_P1_x / Inhib_TOrder_L1_P1_x in the digit format.
    [GeneratedRegex(@"^(?:Barracks|Inhib)_T(?<team>[12]|Order|Chaos)_(?:(?<lane>[LCR])1|L(?<lane>\d))")]
    private static partial Regex InhibitorName();
}
