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
            badge = new League.Controls.IdentityIcon
            {
                Name = "PrivacyLock", Width = 18,
                Font = new Font("Segoe UI Emoji", 9f), ForeColor = Color.FromArgb(180, 135, 40),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = false, AccessibleName = "生涯不公开"
            };
            link.Controls.Add(badge);
            void PositionBadge()
            {
                int width = Math.Max(18, link.Font.Height + 4);
                int textWidth = TextRenderer.MeasureText(link.Text, link.Font).Width;
                badge.SetBounds(Math.Max(0, Math.Min(textWidth, link.ClientSize.Width - width)), 0, width, link.ClientSize.Height);
            }
            link.SizeChanged += (_, _) => PositionBadge();
            link.TextChanged += (_, _) => PositionBadge();
            link.FontChanged += (_, _) => PositionBadge();
            PositionBadge();
        }
        badge.Visible = isPublic == false;
        var previousFont = badge.Font;
        badge.Font = new Font(link.Font.FontFamily, link.Font.SizeInPoints);
        previousFont.Dispose();
        link.Padding = new Padding(0, 0, isPublic == false ? Math.Max(18, link.Font.Height + 4) : 0, 0);
    }
}
