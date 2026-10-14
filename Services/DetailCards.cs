using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// Card sections for the Game Bar detail page (DetailCards.inl). A section's layout picks stat tiles, tag chips or
/// game cards; a tile is a label, a value, a small line and a tone, with its explanation as a tooltip. Older widgets
/// show the same fields as text rows.
/// </summary>
public static class DetailCards
{
    public const string Tiles = "tiles";
    public const string Chips = "chips";
    public const string Games = "games";

    /// <summary>Tones the widget colors: good, bad, warn, info, and win, loss or remake for games.</summary>
    public static JObject Tile(
        string title,
        string value,
        string stats = "",
        string tone = "",
        string description = ""
    ) =>
        new()
        {
            ["title"] = title,
            ["value"] = value,
            ["stats"] = stats,
            ["tone"] = tone,
            ["description"] = description,
        };

    public static JObject Section(string tab, string title, string layout, IEnumerable<JObject> rows, string tip = "")
    {
        var section = OverlayChampionDetailService.Section(tab, title, rows);
        section["layout"] = layout;
        if (tip.Length > 0)
        {
            section["tip"] = tip;
        }

        return section;
    }

    /// <summary>Higher is better for us: own ahead is good, behind is bad.</summary>
    public static string Compare(double own, double enemy) =>
        own > enemy ? "good"
        : own < enemy ? "bad"
        : "";
}
