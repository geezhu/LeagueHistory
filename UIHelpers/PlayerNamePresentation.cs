namespace League.UIHelpers;

/// <summary>Keep the name font intact and reserve space for the system lock glyph.</summary>
public static class PlayerNamePresentation
{
    public static void Apply(LinkLabel link, string name, bool? isPublic)
    {
        link.Text = name;
        var badge = link.Controls.OfType<Label>().FirstOrDefault(c => c.Name == "PrivacyLock");
        if (badge == null)
        {
            badge = new Label
            {
                Name = "PrivacyLock", Text = "🔒", Width = 18,
                Font = new Font("Segoe UI Emoji", 9f), ForeColor = Color.FromArgb(180, 135, 40),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = false, AccessibleName = "生涯不公开"
            };
            link.Controls.Add(badge);
            void PositionBadge()
            {
                int textWidth = TextRenderer.MeasureText(link.Text, link.Font).Width;
                badge.SetBounds(Math.Max(0, Math.Min(textWidth, link.ClientSize.Width - 18)), 0, 18, link.ClientSize.Height);
            }
            link.SizeChanged += (_, _) => PositionBadge();
            link.TextChanged += (_, _) => PositionBadge();
            PositionBadge();
        }
        badge.Visible = isPublic == false;
        link.Padding = new Padding(0, 0, isPublic == false ? 18 : 0, 0);
    }
}
