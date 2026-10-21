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

    /// <summary>A divider that must stay visible, such as between the two teams, and an off switch.</summary>
    public static readonly Color DividerStrong = Color.FromArgb(196, 206, 218);

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

    /// <summary>The two sides of a game in charts and headers, following the client's blue and red.</summary>
    public static readonly Color BlueTeam = Color.FromArgb(58, 128, 210);
    public static readonly Color RedTeam = Color.FromArgb(214, 80, 96);

    public const string FontName = "Microsoft YaHei UI";

    // Windows 11 ships Segoe Fluent Icons; Windows 10 has the older MDL2 face with the same code points.
    private static readonly Lazy<string> IconFamily = new(() =>
    {
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        return installed.Families.Any(family => family.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons"
            : "Segoe MDL2 Assets";
    });

    /// <summary>The Windows icon font for line glyphs such as the navigation rail and status cards.</summary>
    public static string IconFontName => IconFamily.Value;

    // Bahnschrift ships with Windows 10 1709 and later; its weights install as separate families.
    private const string NumberFamily = "Bahnschrift";
    private const string StrongNumberFamily = "Bahnschrift SemiBold";
    private static readonly Lazy<bool> HasNumberFamily = new(() =>
    {
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        return installed.Families.Any(family => family.Name == StrongNumberFamily);
    });

    /// <summary>
    /// A font for figures only (KDA, rates, gold, damage): even digit widths keep columns aligned. It has no Chinese
    /// glyphs, so text that mixes in Chinese stays on <see cref="FontName"/>. Falls back to that face when missing.
    /// </summary>
    public static Font NumberFont(float size, bool strong = false, GraphicsUnit unit = GraphicsUnit.Point) =>
        HasNumberFamily.Value
            ? new Font(strong ? StrongNumberFamily : NumberFamily, size, FontStyle.Regular, unit)
            : new Font(FontName, size, strong ? FontStyle.Bold : FontStyle.Regular, unit);
}
