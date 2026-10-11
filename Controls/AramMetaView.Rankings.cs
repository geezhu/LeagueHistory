using League.Models;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private Font? _rankingBold;
    private double _maximumWinRate;

    internal static Color TierColor(string tier) =>
        tier switch
        {
            "OP" => Color.FromArgb(139, 83, 187),
            "T1" => Color.FromArgb(0, 145, 153),
            "T2" => Color.FromArgb(58, 119, 185),
            "T4" => Color.FromArgb(185, 112, 24),
            "T5" => Color.FromArgb(193, 70, 82),
            _ => Color.FromArgb(113, 130, 146),
        };

    private void ConfigureRankings()
    {
        _rankingBold = new Font(Font, FontStyle.Bold);
        _grid.EnableHeadersVisualStyles = false;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.GridColor = Color.FromArgb(235, 240, 245);
        _grid.ColumnHeadersHeight = 42;
        _grid.RowTemplate.Height = 56;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(239, 245, 249),
            ForeColor = Color.FromArgb(91, 111, 128),
            Font = _rankingBold,
            Padding = new Padding(10, 0, 10, 0),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(38, 58, 75);
        _grid.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 253);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 242, 244);
        _grid.Cursor = Cursors.Hand;
        _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns[0].Width = 82;
        _grid.Columns[0].HeaderText = "#";
        _grid.Columns[1].FillWeight = 160;
        _grid.Columns[1].DefaultCellStyle.Font = _rankingBold;
        _grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns[2].Width = 76;
        _grid.Columns[3].MinimumWidth = 100;
        _grid.Columns[4].MinimumWidth = 125;
        _grid.Columns[4].FillWeight = 125;
        _grid.Columns[5].MinimumWidth = 90;
        _grid.Columns[5].DefaultCellStyle.ForeColor = Color.FromArgb(107, 124, 139);
        _grid.Columns[6].DisplayIndex = 5;
        _grid.Columns[6].HeaderCell.ToolTipText =
            "英雄场次 ÷ 样本总对局数。总对局数按全部英雄场次之和 ÷ 10 计算；仅代表 arammeta 的完整 5v5 样本。缺失或不一致时显示 —。";
        _grid.Columns[4].HeaderCell.ToolTipText =
            "数字为真实调整后胜率；条形长度相对当前筛选结果的最高胜率，最高者为满格。";
        _grid.CellPainting += PaintRankingCell;
        _status.ForeColor = Color.FromArgb(105, 124, 141);
    }

    private void PaintRankingCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].Tag is not AramMetaChampion champion || e.Graphics == null)
        {
            return;
        }

        if (e.ColumnIndex is not (0 or 2 or 4))
        {
            return;
        }

        e.PaintBackground(e.ClipBounds, true);
        var bounds = e.CellBounds;
        if (e.ColumnIndex == 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                (e.RowIndex + 1).ToString(),
                Font,
                new Rectangle(bounds.X + 4, bounds.Y, 25, bounds.Height),
                Color.FromArgb(130, 147, 162),
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter
            );
            if (e.Value is Image avatar)
            {
                e.Graphics.DrawImage(avatar, new Rectangle(bounds.X + 36, bounds.Y + (bounds.Height - 34) / 2, 34, 34));
            }
        }
        else if (e.ColumnIndex == 2)
        {
            Color ink = TierColor(champion.Tier);
            var badge = new Rectangle(bounds.X + 10, bounds.Y + (bounds.Height - 26) / 2, 42, 26);
            using var brush = new SolidBrush(Color.FromArgb(24, ink));
            e.Graphics.FillRectangle(brush, badge);
            TextRenderer.DrawText(
                e.Graphics,
                champion.Tier,
                _rankingBold,
                badge,
                ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter
            );
        }
        else
        {
            Color ink = champion.WinRate >= .5 ? Color.FromArgb(0, 144, 151) : Color.FromArgb(105, 124, 141);
            TextRenderer.DrawText(
                e.Graphics,
                Rate(champion.WinRate),
                _rankingBold,
                new Rectangle(bounds.X + 10, bounds.Y + 7, bounds.Width - 20, 24),
                ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            );
            if (champion.WinRate is double rate)
            {
                var track = new Rectangle(bounds.X + 10, bounds.Bottom - 13, Math.Min(110, bounds.Width - 24), 3);
                using var background = new SolidBrush(Color.FromArgb(227, 236, 241));
                e.Graphics.FillRectangle(background, track);
                using var fill = new SolidBrush(ink);
                e.Graphics.FillRectangle(
                    fill,
                    new Rectangle(
                        track.X,
                        track.Y,
                        (int)Math.Round(track.Width * AramMetaIndex.RelativeRate(rate, _maximumWinRate)),
                        track.Height
                    )
                );
            }
        }
        e.Handled = true;
    }
}
