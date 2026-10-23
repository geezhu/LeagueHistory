namespace League.Clients;

/// <summary>
/// Where match history lives on the servers Riot runs. A Tencent client's own configuration names its history service
/// (<c>PlayerPreferences.ServiceEndpoint</c>), but on Riot's servers that entry is the player-preferences service,
/// which refuses history requests; their history sits on regional hosts shared by several servers. The table follows
/// LeagueAkari's built-in server list (MIT, see Assets/ThirdParty/LeagueAkari-License.txt), dated 2026-07-18; only the
/// Japanese server was checked against a live client. It is tried first; the client's own player platform edge
/// covers regions missing here (EUN1, ME1) and a host that has moved since.
/// </summary>
public static class SgpServers
{
    public const string Tencent = "TENCENT";

    private const string NorthEastAsia = "https://apne1-red.pp.sgp.pvp.net";
    private const string SouthEastAsia = "https://apse1-red.pp.sgp.pvp.net";
    private const string UsWest = "https://usw2-red.pp.sgp.pvp.net";
    private const string EuropeCentral = "https://euc1-red.pp.sgp.pvp.net";

    private static readonly Dictionary<string, string> MatchHistoryHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JP"] = NorthEastAsia,
        ["KR"] = NorthEastAsia,
        ["TW2"] = SouthEastAsia,
        ["SG2"] = SouthEastAsia,
        ["PH2"] = SouthEastAsia,
        ["VN2"] = SouthEastAsia,
        ["TH2"] = SouthEastAsia,
        ["OC1"] = SouthEastAsia,
        ["NA1"] = UsWest,
        ["BR1"] = UsWest,
        ["LA1"] = UsWest,
        ["LA2"] = UsWest,
        ["PBE"] = UsWest,
        ["EUW"] = EuropeCentral,
        ["RU"] = EuropeCentral,
        ["TR1"] = EuropeCentral,
    };

    // Some clients report the short or platform form of a region; the table uses one name for each.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NA"] = "NA1",
        ["BR"] = "BR1",
        ["TR"] = "TR1",
        ["LAN"] = "LA1",
        ["LAS"] = "LA2",
        ["OCE"] = "OC1",
        ["EUW1"] = "EUW",
        ["JP1"] = "JP",
    };

    /// <summary>The region the client reports, in the table's spelling.</summary>
    public static string RegionId(string region)
    {
        string trimmed = region.Trim().ToUpperInvariant();
        return Aliases.TryGetValue(trimmed, out string? alias) ? alias : trimmed;
    }

    /// <summary>The history host of a Riot-run region, or <c>null</c> for Tencent and for regions not in the table.</summary>
    public static string? MatchHistoryHost(string region) =>
        MatchHistoryHosts.TryGetValue(RegionId(region), out string? host) ? host : null;
}
