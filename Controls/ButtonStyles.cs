namespace League.Controls;

/// <summary>
/// The light pages' button looks, so a page picks a role instead of mixing its own fills and borders.
/// </summary>
internal static class ButtonStyles
{
    /// <summary>Everyday actions such as refresh, filter or paging: a quiet fill without a border.</summary>
    public static void Secondary(Button button)
    {
        Flat(button);
        button.BackColor = Theme.ControlFill;
        button.ForeColor = Theme.Heading;
        button.FlatAppearance.MouseOverBackColor = Theme.AccentSoftHover;
        button.FlatAppearance.MouseDownBackColor = Theme.AccentSoft;
    }

    /// <summary>Links that look like text until hovered, such as "全部 ›".</summary>
    public static void Ghost(Button button)
    {
        Flat(button);
        button.BackColor = Color.Transparent;
        button.ForeColor = Theme.Accent;
        button.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = Theme.AccentSoftHover;
    }

    private static void Flat(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
    }
}
