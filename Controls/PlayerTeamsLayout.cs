namespace League.Controls;

internal static class PlayerTeamsLayout
{
    public static void Show(TableLayoutPanel table, Control parent)
    {
        Control surface = table.Parent is Panel panel && panel.Name == "PlayerTeamsSurface" ? panel : table;
        if (surface.Parent != parent) parent.Controls.Add(surface);
        surface.Dock = DockStyle.Fill;
        surface.Visible = true;
        table.Visible = true;
        surface.BringToFront();
    }

    public static void Hide(TableLayoutPanel table)
    {
        if (table.Parent is Panel panel && panel.Name == "PlayerTeamsSurface") panel.Visible = false;
        table.Visible = false;
    }
    public static void Attach(TableLayoutPanel table)
    {
        var parent = table.Parent;
        if (parent == null) return;
        var host = new Panel { Name = "PlayerTeamsSurface", Bounds = table.Bounds, Anchor = table.Anchor, BackColor = Color.FromArgb(245,247,250) };
        parent.Controls.Add(host);
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 232, Padding = new Padding(8), BackColor = Color.White };
        var summaries = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        var switches = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 42, ColumnCount = 2 };
        switches.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); switches.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        var ally = new Button { Text = "友方", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        var enemy = new Button { Text = "敌方", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        ally.FlatAppearance.BorderSize = enemy.FlatAppearance.BorderSize = 0;
        switches.Controls.Add(ally,0,0); switches.Controls.Add(enemy,1,0);
        sidebar.Controls.Add(summaries); sidebar.Controls.Add(switches);
        host.Controls.Add(table); table.Dock = DockStyle.Fill; table.AutoScroll = true;
        host.Controls.Add(sidebar); host.BringToFront();
        bool showAllies = true, arranging = false;
        void Arrange()
        {
            if (arranging || table.IsDisposed) return;
            arranging = true; table.SuspendLayout(); summaries.SuspendLayout();
            try
            {
                var panels = table.Controls.Cast<Control>().ToArray();
                var selected = panels.Where(p => p.Tag is bool team && team == showAllies).ToArray();
                ally.Enabled = panels.Any(p => p.Tag is true);
                enemy.Enabled = panels.Any(p => p.Tag is false);
                ally.BackColor = showAllies ? Color.FromArgb(0,158,171) : Color.FromArgb(242,245,248);
                enemy.BackColor = !showAllies ? Color.FromArgb(0,158,171) : Color.FromArgb(242,245,248);
                ally.ForeColor = showAllies ? Color.White : Color.DimGray;
                enemy.ForeColor = !showAllies ? Color.White : Color.DimGray;
                var wanted = selected.Select(p => p.Controls.OfType<PlayerCardControl>().FirstOrDefault()?.SummaryPanel)
                    .Where(p => p != null).ToHashSet();
                foreach (Control summary in summaries.Controls.Cast<Control>().ToArray())
                    if (!wanted.Contains(summary as Panel)) summaries.Controls.Remove(summary);
                foreach (var panel in panels)
                {
                    panel.Visible = selected.Contains(panel);
                    var card = panel.Controls.OfType<PlayerCardControl>().FirstOrDefault();
                    if (card == null) continue;
                    card.SeparateSummary();
                    if (selected.Contains(panel))
                    {
                        card.SummaryPanel.Dock = DockStyle.None; card.SummaryPanel.Width = Math.Max(160,summaries.ClientSize.Width-24);
                        card.SummaryPanel.Margin = new Padding(0,0,0,8);
                        if (card.SummaryPanel.Parent != summaries) summaries.Controls.Add(card.SummaryPanel);
                    }
                }
                var positions = selected.Select(table.GetPositionFromControl).ToArray();
                for (int i=0;i<table.ColumnStyles.Count;i++)
                {
                    table.ColumnStyles[i].SizeType = SizeType.Absolute;
                    table.ColumnStyles[i].Width = positions.Any(p=>p.Column==i) ? 240 : 0;
                }
                for (int i=0;i<table.RowStyles.Count;i++)
                {
                    table.RowStyles[i].SizeType = SizeType.Absolute;
                    table.RowStyles[i].Height = positions.Any(p=>p.Row==i) ? Math.Max(260,table.ClientSize.Height-20) : 0;
                }
            }
            finally { summaries.ResumeLayout(true); table.ResumeLayout(true); arranging = false; }
        }
        ally.Click += (_,_) => { showAllies = true; Arrange(); };
        enemy.Click += (_,_) => { showAllies = false; Arrange(); };
        table.ControlAdded += (_,_) => Arrange(); table.ControlRemoved += (_,_) => Arrange();
        table.SizeChanged += (_,_) => Arrange(); summaries.SizeChanged += (_,_) => Arrange();
        Arrange();
    }
}
