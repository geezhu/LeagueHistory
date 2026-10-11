using League.Models;
using League.Services;

namespace League.Controls;

public sealed class LoadingOverlayView : UserControl
{
    private const int PlayersPerTeam = 5;
    private readonly Font _viewFont = new("Microsoft YaHei UI", 9);
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        Padding = new Padding(12, 5, 12, 0),
    };
    private readonly TableLayoutPanel _teams = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = PlayersPerTeam + 1,
        Padding = new Padding(10, 0, 10, 0),
    };
    private readonly Label _details = new()
    {
        Dock = DockStyle.Bottom,
        Height = 104,
        Padding = new Padding(16, 10, 16, 8),
        BackColor = Color.FromArgb(12, 37, 48),
    };

    private sealed record PlayerRow(
        Panel Card,
        PictureBox Icon,
        Label Name,
        Label Champion,
        Label Rank,
        Label Rates,
        Label Mastery
    );

    private readonly List<PlayerRow> _rows = new();
    private readonly ToolTip _masteryTips = new() { ShowAlways = true };
    private readonly Func<int, string> _champion;
    private readonly Func<LoadingPlayer, string> _playerDetails;
    private LoadingRoster? _roster;
    private IReadOnlyDictionary<string, PlayerChampionTier> _ranks = new Dictionary<string, PlayerChampionTier>();
    private string? _selected;
    private bool _closing;

    public LoadingOverlayView(Func<int, string> champion, Func<LoadingPlayer, string> details)
    {
        _champion = champion;
        _playerDetails = details;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.FromArgb(211, 229, 233);
        Font = _viewFont;
        ConfigureTeamColumns();
        Controls.Add(_teams);
        Controls.Add(_details);
        Controls.Add(_status);
        _details.Text = "点击玩家卡片，查看已有的段位、近期战绩与统计来源。";
    }

    private void ConfigureTeamColumns()
    {
        _teams.ColumnStyles.Add(new(SizeType.Percent, 50));
        _teams.ColumnStyles.Add(new(SizeType.Percent, 50));
        _teams.RowStyles.Add(new(SizeType.Absolute, 30));
        for (int row = 0; row < PlayersPerTeam; row++)
        {
            _teams.RowStyles.Add(new(SizeType.Percent, 100f / PlayersPerTeam));
        }
        for (int side = 0; side < 2; side++)
        {
            _teams.Controls.Add(
                new Label
                {
                    Text = side == 0 ? "我方" : "对方",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = side == 0 ? Color.Turquoise : Color.SandyBrown,
                    Padding = new Padding(8, 0, 0, 0),
                },
                side,
                0
            );
            for (int row = 0; row < PlayersPerTeam; row++)
            {
                var playerRow = CreatePlayerRow();
                _teams.Controls.Add(playerRow.Card, side, row + 1);
                _rows.Add(playerRow);
            }
        }
    }

    private PlayerRow CreatePlayerRow()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            Padding = new Padding(8),
            BackColor = Color.FromArgb(17, 46, 58),
            Cursor = Cursors.Hand,
        };
        var icon = new PictureBox
        {
            Dock = DockStyle.Left,
            Width = 48,
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        var rank = new Label
        {
            Dock = DockStyle.Right,
            Width = 42,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var text = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };
        var name = RowLabel(DockStyle.Top, 20);
        var hero = RowLabel(DockStyle.Top, 20, Color.FromArgb(232, 199, 121));
        var rates = RowLabel(DockStyle.Fill, 0, Color.FromArgb(83, 202, 201));
        var mastery = RowLabel(DockStyle.Bottom, 18, Color.FromArgb(208, 185, 130));
        text.Controls.Add(rates);
        text.Controls.Add(mastery);
        text.Controls.Add(hero);
        text.Controls.Add(name);
        card.Controls.Add(text);
        card.Controls.Add(rank);
        card.Controls.Add(icon);
        foreach (Control target in new Control[] { card, icon, rank, text, name, hero, rates, mastery })
        {
            target.Click += (_, _) => SelectPlayer(card);
        }
        return new(card, icon, name, hero, rank, rates, mastery);
    }

    private static Label RowLabel(DockStyle dock, int height, Color? color = null) =>
        new()
        {
            Dock = dock,
            Height = height,
            AutoEllipsis = true,
            ForeColor = color ?? Color.FromArgb(211, 229, 233),
        };

    private void SelectPlayer(Control card)
    {
        if (!_closing && card.Tag is LoadingPlayer player)
        {
            _selected = player.Key;
            ShowDetails(player);
        }
    }

    public void UpdateRoster(
        LoadingRoster? roster,
        IReadOnlyDictionary<string, PlayerChampionTier> ranks,
        IReadOnlyDictionary<int, Image> icons,
        string status,
        IReadOnlyDictionary<string, PlayerChampionMastery>? masteries = null
    )
    {
        if (_closing || IsDisposed)
        {
            return;
        }
        _roster = roster;
        _ranks = ranks;
        _status.Text = status;
        SuspendLayout();
        try
        {
            int own = roster?.Players.FirstOrDefault(player => player.Self)?.Team ?? 100;
            int[] teams = own == 200 ? [200, 100] : [100, 200];
            for (int side = 0; side < teams.Length; side++)
            {
                _teams.GetControlFromPosition(side, 0)!.Text =
                    roster?.TeamLabel(teams[side]) ?? (side == 0 ? "我方" : "对方");
                var players =
                    roster?.Players.Where(player => player.Team == teams[side]).Take(PlayersPerTeam).ToArray() ?? [];
                for (int row = 0; row < PlayersPerTeam; row++)
                {
                    UpdatePlayerRow(
                        _rows[side * PlayersPerTeam + row],
                        players.ElementAtOrDefault(row),
                        icons,
                        masteries
                    );
                }
            }
            RefreshSelectedDetails();
        }
        finally
        {
            ResumeLayout();
        }
    }

    private void UpdatePlayerRow(
        PlayerRow row,
        LoadingPlayer? player,
        IReadOnlyDictionary<int, Image> icons,
        IReadOnlyDictionary<string, PlayerChampionMastery>? masteries
    )
    {
        row.Card.Tag = player;
        // Icons are borrowed from the overlay owner's cache, including when a roster replaces these rows.
        row.Icon.Image = player != null ? icons.GetValueOrDefault(player.ChampionId) : null;
        row.Name.Text =
            player == null ? "等待玩家数据"
            : string.IsNullOrWhiteSpace(player.Name) ? "玩家名称尚未提供"
            : player.Name + (player.Self ? " · 自己" : "");
        row.Champion.Text = player?.ChampionId > 0 ? _champion(player.ChampionId) : "英雄待确认";
        var rank = player != null ? _ranks.GetValueOrDefault(player.Key) : null;
        row.Rank.Text = rank?.Tier ?? "—";
        row.Rank.ForeColor = AramMetaView.TierColor(rank?.Tier ?? "—");
        row.Rates.Text =
            player == null ? ""
            : rank == null ? "统计读取中…"
            : rank.Rate + " · 选 " + rank.PickRate;
        var mastery = player != null ? masteries?.GetValueOrDefault(player.Key) : null;
        if (mastery?.ChampionId != player?.ChampionId)
        {
            mastery = null;
        }
        row.Mastery.Text = player?.ChampionId > 0 ? mastery?.Text ?? "成就读取中…" : "";
        _masteryTips.SetToolTip(row.Mastery, mastery?.Description ?? "");
    }

    private void RefreshSelectedDetails()
    {
        var selected = _roster?.Players.FirstOrDefault(player => player.Key == _selected);
        if (selected != null)
        {
            ShowDetails(selected);
            return;
        }
        _selected = null;
        string source = _ranks.Values.FirstOrDefault()?.Note.Replace("\n", " · ") ?? "英雄统计尚未读取。";
        _details.Text = "点击玩家卡片，查看已有的段位与近期战绩。胜率和选用率属于英雄模式统计。\n" + source;
    }

    private void ShowDetails(LoadingPlayer player)
    {
        string source = _ranks.GetValueOrDefault(player.Key)?.Note ?? "英雄统计尚未读取。";
        _details.Text =
            (string.IsNullOrWhiteSpace(player.Name) ? _champion(player.ChampionId) : player.Name)
            + " · "
            + _champion(player.ChampionId)
            + "\n"
            + _playerDetails(player)
            + "\n"
            + source.Replace("\n", " · ");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _masteryTips.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _viewFont.Dispose();
        }
    }
}
