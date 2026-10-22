namespace League.Controls;

/// <summary>
/// A guide's headline figure: a small muted caption over a large number, the way the OP.GG and ARAM guides open. The
/// caller owns the figure font, since every metric in a header shares one.
/// </summary>
internal static class SummaryMetric
{
    public static Control Create(string caption, string value, Color color, Font figure)
    {
        var metric = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 6, 28, 0),
        };
        metric.Controls.Add(
            new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = Padding.Empty,
            }
        );
        metric.Controls.Add(
            new Label
            {
                Text = value,
                AutoSize = true,
                ForeColor = color,
                Font = figure,
                Margin = new Padding(0, 2, 0, 0),
            }
        );
        return metric;
    }
}
