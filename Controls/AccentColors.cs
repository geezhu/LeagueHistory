namespace League.Controls;

/// <summary>Teal accent for the dark champion-select assistant and voice windows; light pages use <see cref="Theme.Accent"/>.</summary>
internal static class AccentColors
{
    /// <summary>Accent text, links and indicator lines.</summary>
    public static readonly Color Accent = Color.FromArgb(0, 143, 153);

    /// <summary>Filled controls with white text; darker than <see cref="Accent"/> to keep text contrast readable.</summary>
    public static readonly Color Fill = Color.FromArgb(0, 126, 139);
}
