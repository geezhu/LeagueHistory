namespace League.Controls;

/// <summary>
/// One entry of the main navigation rail, set as a <see cref="TabPage.Tag"/>: a glyph from the Windows icon font and a
/// short label for the labelled rail. The page's own text stays the full title shown in the tooltip.
/// </summary>
public sealed record RailIcon(string Glyph, string Label);
