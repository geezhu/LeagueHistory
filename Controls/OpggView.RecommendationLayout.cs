using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class OpggView
{
    private async Task RenderMatchupsAsync(OpggBuild build, CancellationToken token)
    {
        var section = Section("对局分析");
        var opponents = (build.Data["counters"] as JArray ?? new())
            .OfType<JObject>()
            .Where(c =>
                (int?)c["champion_id"] > 0
                && (long?)c["play"] > 0
                && OpggBuild.Ratio(c["win"], c["play"]) is >= 0 and <= 1
            )
            .ToArray();
        if (opponents.Length == 0)
        {
            section.Controls.Add(TextLabel("暂无对局统计"));
            return;
        }
        var layout = Inline(section);
        // The two groups share the card's width half and half, and follow it when the page is resized.
        layout.SizeChanged += (_, _) => FitMatchupColumns(layout);
        var groups = new[]
        {
            (
                Title: "优势对局 · 胜率 ≥ 50%",
                Rows: opponents
                    .Where(c => OpggBuild.Ratio(c["win"], c["play"]) >= 0.5)
                    .OrderByDescending(c => OpggBuild.Ratio(c["win"], c["play"]))
                    .ToArray(),
                Color: Theme.Win
            ),
            (
                Title: "劣势对局 · 胜率 < 50%",
                Rows: opponents
                    .Where(c => OpggBuild.Ratio(c["win"], c["play"]) < 0.5)
                    .OrderBy(c => OpggBuild.Ratio(c["win"], c["play"]))
                    .ToArray(),
                Color: Theme.Loss
            ),
        };
        foreach (var group in groups)
        {
            await RenderMatchupGroupAsync(layout, group.Title, group.Rows, group.Color, token);
        }
        FitMatchupColumns(layout);
    }

    private const int MatchupsShown = 8;
    private const int MatchupGap = 24;

    private static void FitMatchupColumns(Control layout)
    {
        int width = Math.Max(240, (layout.ClientSize.Width - MatchupGap) / 2);
        foreach (Control column in layout.Controls)
        {
            column.MinimumSize = new Size(width, 0);
            column.MaximumSize = new Size(width, 0);
            foreach (var row in column.Controls.OfType<MatchupRow>())
            {
                row.Width = width;
            }
        }
    }

    // Each side lists its first eight; the rest stay built but hidden behind a "show all" button, so a long guide
    // does not end in fifty rows.
    private async Task RenderMatchupGroupAsync(
        Control layout,
        string title,
        JObject[] opponents,
        Color color,
        CancellationToken token
    )
    {
        var column = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, layout.Controls.Count == 0 ? MatchupGap : 0, 0),
        };
        layout.Controls.Add(column);
        column.Controls.Add(
            new Label
            {
                Text = $"{title}（{opponents.Length}）",
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = new Padding(2, 0, 0, 6),
            }
        );
        if (opponents.Length == 0)
        {
            column.Controls.Add(TextLabel("暂无符合条件的对局"));
        }
        var rows = new List<Control>();
        foreach (var opponent in opponents)
        {
            token.ThrowIfCancellationRequested();
            var row = await AddMatchupRowAsync(column, opponent, color, token);
            row.Visible = rows.Count < MatchupsShown;
            rows.Add(row);
        }
        if (rows.Count > MatchupsShown)
        {
            column.Controls.Add(MatchupToggle(rows));
        }
    }

    private Button MatchupToggle(List<Control> rows)
    {
        var toggle = new Button
        {
            Text = $"显示全部 {rows.Count} 个 ˅",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };
        ButtonStyles.Ghost(toggle);
        bool expanded = false;
        toggle.Click += (_, _) =>
        {
            expanded = !expanded;
            foreach (var row in rows.Skip(MatchupsShown))
            {
                row.Visible = expanded;
            }
            toggle.Text = expanded ? "收起 ˄" : $"显示全部 {rows.Count} 个 ˅";
        };
        return toggle;
    }

    private async Task<Control> AddMatchupRowAsync(
        Control column,
        JObject opponent,
        Color color,
        CancellationToken token
    )
    {
        int id = (int)opponent["champion_id"]!;
        long games = (long)opponent["play"]!;
        double rate = OpggBuild.Ratio(opponent["win"], opponent["play"]) ?? 0.5;
        var row = new MatchupRow(ChampionName(id), rate, games, color)
        {
            Width = Math.Max(240, column.Width),
            Margin = Padding.Empty,
            Tag = id,
        };
        column.Controls.Add(row);
        var iconHost = new FlowLayoutPanel
        {
            Bounds = new Rectangle(0, 1, 36, 36),
            Margin = Padding.Empty,
            WrapContents = false,
        };
        row.Controls.Add(iconHost);
        _tips.SetToolTip(row, $"{ChampionName(id)} · {games:N0} 场 · 当前攻略英雄对阵它的胜率 {rate:P1}");
        await AddIconAsync(iconHost, id, "champion", token);
        return row;
    }

    /// <summary>
    /// One opponent: icon, name, a bar for how far the win rate sits from 50% (a 15-point lead fills it), the rate and
    /// the games. The bar lets 53% and 58% read as different at a glance.
    /// </summary>
    private sealed class MatchupRow : Panel
    {
        private const double FullLead = 0.15;
        private readonly string _name;
        private readonly double _rate;
        private readonly long _games;
        private readonly Color _color;

        public MatchupRow(string name, double rate, long games, Color color)
        {
            _name = name;
            _rate = rate;
            _games = games;
            _color = color;
            DoubleBuffered = true;
            Height = 38;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            const int nameLeft = 44,
                nameWidth = 96,
                rateWidth = 52,
                gamesWidth = 64;
            TextRenderer.DrawText(
                g,
                _name,
                Font,
                new Rectangle(nameLeft, 0, nameWidth, Height),
                Theme.Ink,
                flags | TextFormatFlags.EndEllipsis
            );
            int games = Width - gamesWidth;
            int rate = games - rateWidth - 8;
            int barLeft = nameLeft + nameWidth + 8;
            int barWidth = Math.Max(0, rate - 12 - barLeft);
            if (barWidth > 0)
            {
                var track = new Rectangle(barLeft, Height / 2 - 3, barWidth, 6);
                using var trackBrush = new SolidBrush(Theme.ControlFill);
                g.FillRectangle(trackBrush, track);
                double lead = Math.Clamp(Math.Abs(_rate - 0.5) / FullLead, 0, 1);
                using var fill = new SolidBrush(_color);
                g.FillRectangle(fill, track.X, track.Y, Math.Max(2, (int)(track.Width * lead)), track.Height);
            }
            using var strong = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                $"{_rate:P1}",
                strong,
                new Rectangle(rate, 0, rateWidth, Height),
                _color,
                flags | TextFormatFlags.Right
            );
            TextRenderer.DrawText(
                g,
                $"{_games:N0} 场",
                Font,
                new Rectangle(games, 0, gamesWidth, Height),
                Theme.Muted,
                flags | TextFormatFlags.Right
            );
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }
    }

    private Control RecommendationStats(JToken data)
    {
        var cell = new OpggStatCell(
            OpggBuild.Ratio(data["win"], data["play"]),
            (long?)data["play"],
            (double?)data["pick_rate"]
        );
        _tips.SetToolTip(cell, "胜率按 50% 分色；条形为选用率");
        return cell;
    }

    private void RenderSkills(OpggBuild build, Control host)
    {
        var section = Section("技能加点", host);
        var layout = Inline(section);
        var left = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        layout.Controls.Add(left);
        var mastery = build.Data["skill_masteries"]?.First;
        var skill = build.Data["skills"]?.First;
        void Chips(JToken? sequence, bool priority)
        {
            var values = (sequence as JArray)?.Values<string>().ToArray() ?? Array.Empty<string>();
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            left.Controls.Add(row);
            if (values.Length == 0)
            {
                row.Controls.Add(TextLabel("暂无数据"));
                return;
            }
            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                int size = priority ? 32 : 22;
                var color = value switch
                {
                    "Q" => Color.FromArgb(29, 146, 220),
                    "W" => Color.FromArgb(0, 167, 154),
                    "E" => Color.FromArgb(225, 147, 44),
                    _ => Color.FromArgb(91, 105, 206),
                };
                var chip = new Label
                {
                    Text = value,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(size, size),
                    Margin = new Padding(1, 2, 1, 2),
                    ForeColor = value == "R" ? Color.White : color,
                    BackColor = value == "R" ? color : Color.FromArgb(239, 246, 250),
                };
                chip.Paint += (_, e) =>
                {
                    using var pen = new Pen(color);
                    e.Graphics.DrawRectangle(pen, 0, 0, chip.Width - 1, chip.Height - 1);
                };
                _tips.SetToolTip(chip, priority ? "技能升级优先级" : $"第 {i + 1} 级：{value}");
                row.Controls.Add(chip);
                if (priority && i < values.Length - 1)
                {
                    row.Controls.Add(
                        new Label
                        {
                            Text = "›",
                            Size = new Size(24, 32),
                            TextAlign = ContentAlignment.MiddleCenter,
                            ForeColor = Color.Gray,
                            Margin = new Padding(2),
                        }
                    );
                }
            }
        }
        Chips(mastery?["ids"], true);
        Chips(skill?["order"], false);
        if (skill != null)
        {
            var stats = RecommendationStats(skill);
            stats.Margin = new Padding(12, 8, 0, 0);
            layout.Controls.Add(stats);
        }
    }

    private async Task RenderStartingEquipmentAsync(OpggBuild build, Control host, CancellationToken token)
    {
        var section = Section("出门装与鞋子", host);
        var layout = Inline(section);
        foreach (var (key, title) in new[] { ("starter_items", "出门装"), ("boots", "鞋子") })
        {
            var column = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 12, 0),
            };
            layout.Controls.Add(column);
            column.Controls.Add(
                new Label
                {
                    Text = title,
                    AutoSize = true,
                    Margin = new Padding(2, 0, 0, 4),
                    ForeColor = Color.SlateGray,
                }
            );
            var options = (build.Data[key] as JArray ?? new()).OfType<JObject>().Take(3).ToArray();
            if (options.Length == 0)
            {
                column.Controls.Add(TextLabel("暂无数据"));
            }

            foreach (var option in options)
            {
                var ids = (option["ids"] as JArray ?? new()).Values<int>().ToArray();
                // Keep the three data columns together; only the entire section may wrap.
                int iconWidth = Math.Max(108, ids.Length * 36);
                var row = new Panel
                {
                    Size = new Size(iconWidth + 12 + OpggStatCell.CellWidth, 44),
                    Margin = Padding.Empty,
                };
                column.Controls.Add(row);
                var icons = new FlowLayoutPanel
                {
                    Location = new Point(0, 4),
                    Size = new Size(iconWidth, 36),
                    WrapContents = false,
                    Margin = Padding.Empty,
                };
                row.Controls.Add(icons);
                foreach (var id in ids)
                {
                    await AddIconAsync(icons, id, "item", token);
                }

                token.ThrowIfCancellationRequested();
                var stats = RecommendationStats(option);
                stats.Location = new Point(iconWidth + 12, 2);
                row.Controls.Add(stats);
            }
        }
    }
}
