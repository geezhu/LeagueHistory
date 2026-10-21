namespace League.Controls;

/// <summary>
/// The main window's light palette. Blue marks what can be clicked or is selected; green and red
/// only ever mean a win or a loss, so the accent never reads as a result.
/// </summary>
internal static class Theme
{
    /// <summary>Page background behind cards.</summary>
    public static readonly Color Canvas = Color.FromArgb(244, 247, 251);

    /// <summary>Cards, lists and other raised content.</summary>
    public static readonly Color Surface = Color.White;

    /// <summary>Quiet fills inside a card, such as an inactive option.</summary>
    public static readonly Color SurfaceSubtle = Color.FromArgb(247, 250, 252);

    /// <summary>Thin borders and row dividers.</summary>
    public static readonly Color Divider = Color.FromArgb(225, 231, 239);

    /// <summary>Body text.</summary>
    public static readonly Color Ink = Color.FromArgb(36, 48, 65);

    /// <summary>Titles and section headings.</summary>
    public static readonly Color Heading = Color.FromArgb(36, 63, 98);

    /// <summary>Secondary text: dates, notes and idle tabs.</summary>
    public static readonly Color Muted = Color.FromArgb(111, 123, 139);

    /// <summary>Links, the selection bar and filled buttons with white text.</summary>
    public static readonly Color Accent = Color.FromArgb(46, 103, 190);

    /// <summary>Hover and pressed fills of a primary button.</summary>
    public static readonly Color AccentHover = Color.FromArgb(38, 90, 170);
    public static readonly Color AccentPressed = Color.FromArgb(31, 77, 148);

    /// <summary>Background of a selected row or the profile header.</summary>
    public static readonly Color AccentSoft = Color.FromArgb(232, 241, 253);

    /// <summary>Fill of a secondary button.</summary>
    public static readonly Color ControlFill = Color.FromArgb(232, 239, 248);

    /// <summary>Hover over a soft button.</summary>
    public static readonly Color AccentSoftHover = Color.FromArgb(225, 237, 253);

    public static readonly Color Win = Color.FromArgb(38, 139, 113);
    public static readonly Color Loss = Color.FromArgb(191, 81, 91);

    /// <summary>The navigation rail and title bar framing the light pages.</summary>
    public static readonly Color Rail = Color.FromArgb(24, 32, 44);
    public static readonly Color RailHover = Color.FromArgb(36, 47, 62);
    public static readonly Color RailSelected = Color.FromArgb(44, 57, 75);
    public static readonly Color RailText = Color.FromArgb(226, 232, 240);

    public const string FontName = "Microsoft YaHei UI";
}
