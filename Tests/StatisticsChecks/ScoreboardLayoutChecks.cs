using System.Reflection;
using League.Controls;
using League.Models;
using League.UIState;
using Newtonsoft.Json.Linq;

internal static class ScoreboardLayoutChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        RowContents();
        ResizeAndMetricPages();
        PlayerCardLayout();
        DetailNavigation();
        TeamSorting();
        BanSlots();
        RequestRetirement();
        StatisticsTable();
        ChartSelectionAndRendering();
        Console.WriteLine(
            "PASS: scoreboard values, metric pages, player cards, lazy tabs, team sorting, bans and request retirement."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, args);

    private static JObject Player(int kills, int assists) =>
        new()
        {
            ["kills"] = kills,
            ["deaths"] = 2,
            ["assists"] = assists,
            ["goldEarned"] = 12345,
            ["totalDamageDealtToChampions"] = 20000,
            ["totalDamageTaken"] = 15000,
            ["totalMinionsKilled"] = 30,
            ["neutralMinionsKilled"] = 5,
            ["challenges"] = new JObject { ["killParticipation"] = 0.75 },
            ["win"] = true,
        };

    private static Panel CreateRow(DetailedViewLayoutManager layout, JObject player, MatchInfo match)
    {
        var row = new Panel { Size = new Size(1120, 44) };
        var stats = (20000, 1, 12345, 1, 0.75, 1, 15000, 1);
        Invoke(layout, "CreateScoreboardRow", row, player, "玩家#1234", true, stats, match);
        return row;
    }

    private static void RowContents()
    {
        using var tips = new ToolTip();
        using var layout = new DetailedViewLayoutManager(tips);
        var player = Player(12, 18);
        var tied = Player(12, 18);
        using var match = new MatchInfo
        {
            AllParticipants = new() { player, tied },
        };
        using var row = CreateRow(layout, player, match);
        Check(row.Controls["PlayerName"]!.Text == "玩家#1234", "Player identity must remain in the row");
        var kda = row.Controls["KdaValues"]!;
        Check(kda.Text == "12 / 2 / 18", "KDA separators and full values must be preserved");
        Check((int)kda.GetType().GetProperty("KillsLength")!.GetValue(kda)! == 2, "Tied kills must be highlighted");
        Check(
            (int)kda.GetType().GetProperty("AssistsStart")!.GetValue(kda)! == 9,
            "Tied assists must highlight only their glyphs"
        );
        Check(row.Controls["goldEarned"]!.Text == "12.3k", "Gold precision must remain one decimal");
        Check(row.Controls["totalDamageDealtToChampions"]!.Text == "20.0k", "Damage display must retain its unit");
        Check(row.Controls["totalCs"]!.Text == "35", "CS must include neutral minions");
        Check(
            row.Controls["killParticipation"]!.Text == $"{0.75:P1}",
            "Participation display must retain its precision"
        );
        Check(
            row.Controls.OfType<PictureBox>().Count() >= 10,
            "Champion, spells and seven item slots must be retained"
        );
        Check(
            tips.GetToolTip(row.Controls["totalDamageTaken"]!).Contains("全场最高"),
            "Tied damage taken must be highlighted"
        );
        tied.Remove("kills");
        tied.Remove("totalDamageTaken");
        using var incomplete = CreateRow(layout, player, match);
        Check(
            (int)
                incomplete.Controls["KdaValues"]!
                    .GetType()
                    .GetProperty("KillsLength")!
                    .GetValue(incomplete.Controls["KdaValues"])! == 0,
            "Missing opponent kills must suppress an unverified maximum"
        );
        Check(
            !tips.GetToolTip(incomplete.Controls["totalDamageTaken"]!).Contains("全场最高"),
            "Missing opponent damage must suppress an unverified maximum"
        );
    }

    private static (Panel Section, Panel Row) AddSyntheticSection(Panel scoreboard)
    {
        var section = new Panel();
        section.Controls.Add(new Button { Text = "玩家", Bounds = new Rectangle(58, 32, 180, 30) });
        section.Controls.Add(new Button { Text = "技能 / 装备", Bounds = new Rectangle(242, 32, 182, 30) });
        var row = new Panel { Top = 64 };
        row.Controls.Add(new LinkLabel { Name = "PlayerName", Bounds = new Rectangle(58, 4, 180, 30) });
        foreach (int x in new[] { 592, 686, 780, 880, 958, 1026 })
        {
            section.Controls.Add(
                new Button
                {
                    Name = "Header" + x,
                    Text = "指标",
                    Bounds = new Rectangle(x, 32, 90, 30),
                }
            );
            row.Controls.Add(
                new Label
                {
                    Name = "Cell" + x,
                    Text = x.ToString(),
                    Bounds = new Rectangle(x, 4, 90, 30),
                }
            );
        }
        section.Controls.Add(row);
        scoreboard.Controls.Add(section);
        return (section, row);
    }

    private static void ResizeAndMetricPages()
    {
        using var host = new Form();
        using var match = new MatchInfo { AllParticipants = new(), RawGameData = new JObject() };
        using var detail = new MatchDetailView(match) { Dock = DockStyle.Fill };
        host.Controls.Add(detail);
        host.Show();
        var scoreboard = (Panel)typeof(MatchDetailView).GetField("_scoreboard", PrivateInstance)!.GetValue(detail)!;
        var (section, row) = AddSyntheticSection(scoreboard);
        foreach (int width in new[] { 900, 1300, 900 })
        {
            host.ClientSize = new Size(width, 600);
            Application.DoEvents();
            Invoke(detail, "ReflowScoreboard");
            var page = (Button)section.Controls["MetricPage"]!;
            bool compact = width == 900;
            Check(page.Visible == compact, "Only compact scoreboards need metric pages");
            Check(
                !section.Controls[0].Bounds.IntersectsWith(section.Controls[1].Bounds),
                "Identity and equipment headings must not overlap"
            );
            Check(section.Controls[1].Text == "技能 / 装备", "Resize must retain full heading text");
            Check(row.Controls["Cell592"]!.Visible, "Primary metrics must be visible initially");
            Check(
                row.Controls["Cell880"]!.Visible == !compact,
                "Secondary metrics must remain on their own compact page"
            );
            if (compact)
            {
                CheckMetricPageSwitch(section, row, page);
            }
            else
            {
                Check(row.Controls["Cell592"]!.Left == 592, "Widening must restore the original metric position");
                Check(row.Controls["PlayerName"]!.Width == 180, "Widening must restore the original name width");
            }
        }
        using var image = new Bitmap(900, 600);
        detail.DrawToBitmap(image, new Rectangle(0, 0, 900, 600));
        image.Save(Path.Combine(AppContext.BaseDirectory, "scoreboard-layout-quality.png"));
    }

    private static void CheckMetricPageSwitch(Panel section, Panel row, Button page)
    {
        int primaryX = row.Controls["Cell592"]!.Left;
        int secondaryX = row.Controls["Cell880"]!.Left;
        page.PerformClick();
        Check(
            !row.Controls["Cell592"]!.Visible && row.Controls["Cell880"]!.Visible,
            "Compact flip must reveal secondary metrics"
        );
        Check(
            !section.Controls["Header592"]!.Visible && section.Controls["Header880"]!.Visible,
            "Headers must flip with their values"
        );
        Check(
            row.Controls["Cell592"]!.Left == primaryX && row.Controls["Cell880"]!.Left == secondaryX,
            "Metric flip must not move values"
        );
        page.PerformClick();
        Check(
            row.Controls["Cell592"]!.Visible && !row.Controls["Cell880"]!.Visible,
            "A second flip must restore primary metrics"
        );
    }

    private static void DetailNavigation()
    {
        using var match = new MatchInfo { AllParticipants = new(), RawGameData = new JObject() };
        using var detail = new MatchDetailView(match);
        var tabType = typeof(MatchDetailView).GetNestedType("DetailTab", BindingFlags.NonPublic)!;
        Check(detail.Controls.Count == 2, "Secondary tabs must not be created before selection");
        foreach (string tab in new[] { "Statistics", "Charts", "Timeline", "Scoreboard", "Charts" })
        {
            Invoke(detail, "SelectTab", Enum.Parse(tabType, tab));
            Check(detail.Controls.Count <= 5, "Returning to a tab must reuse its view");
        }
        Check(detail.Controls.Count == 5, "Each secondary tab must be created exactly once");
        var views = detail.Controls.Cast<Control>().Where(c => c.Name != "MatchHeader").ToArray();
        Check(
            views.Count(c => c.Visible) == 1 && views.Single(c => c.Visible) is MatchChartsView,
            "Only the selected tab may remain visible"
        );
        var actions = detail.Controls["MatchHeader"]!.Controls.OfType<FlowLayoutPanel>().Single();
        Check(
            actions
                .Controls.Cast<Button>()
                .Select(b => b.Text)
                .SequenceEqual(new[] { "查看回放", "基础数据", "统计", "图表", "时间线" }),
            "Navigation order and labels must remain unchanged"
        );
    }

    private static void TeamSorting()
    {
        var players = new List<JObject> { Player(2, 1), Player(9, 3), Player(3, 5) };
        foreach (var player in players)
        {
            player["teamId"] = 100;
        }
        players[0]["goldEarned"] = 9000;
        players[1]["goldEarned"] = 15000;
        players[2]["goldEarned"] = 10000;
        using var match = new MatchInfo { AllParticipants = players, RawGameData = new JObject() };
        using var detail = new MatchDetailView(match);
        using var panel = new Panel();
        var team = Invoke(detail, "CreateScoreboardTeam", panel, match, 100, 8)!;
        var section = (Panel)team.GetType().GetProperty("Section")!.GetValue(team)!;
        var rows = (System.Collections.IList)team.GetType().GetProperty("Rows")!.GetValue(team)!;
        foreach (var player in players)
        {
            var row = new Panel
            {
                Top = 64 + rows.Count * 44,
                Height = 44,
                Tag = player,
            };
            section.Controls.Add(row);
            rows.Add((player, row));
        }
        section.Visible = true;
        var gold = section.Controls.OfType<Button>().Single(b => b.Text == "经济");
        gold.PerformClick();
        CheckSortedPlayer(section, players[1], "Gold must sort descending on first click");
        gold.PerformClick();
        CheckSortedPlayer(section, players[0], "The same metric must reverse its sort order");
        var kda = section.Controls.OfType<Button>().Single(b => b.Text == "KDA");
        kda.PerformClick();
        CheckSortedPlayer(section, players[1], "Changing metrics must start with descending KDA");
        Check(gold.Text == "经济" && kda.Text == "KDA ↓", "Only the active sort heading may retain an arrow");
    }

    private static void CheckSortedPlayer(Panel section, JObject player, string message) =>
        Check(ReferenceEquals(section.Controls.OfType<Panel>().OrderBy(r => r.Top).First().Tag, player), message);

    private static void BanSlots()
    {
        using var match = new MatchInfo { AllParticipants = new(), RawGameData = new JObject() };
        using var detail = new MatchDetailView(match);
        var bans = JArray.Parse(
            "[{\"pickTurn\":3,\"championId\":12},{\"pickTurn\":1,\"championId\":-1},{\"pickTurn\":2,\"championId\":5}]"
        );
        var (content, slots) = ((FlowLayoutPanel, List<(PictureBox Picture, int Id)>))
            Invoke(detail, "CreateBanSlots", bans)!;
        using (content)
        {
            Check(
                slots.Select(s => s.Id).SequenceEqual(new[] { -1, 5, 12 }),
                "Bans must preserve pick order and unpicked slots"
            );
            Check(
                content.Controls.Count == 3 && slots.All(s => s.Picture.Size == new Size(34, 34)),
                "Each ban must retain its icon slot"
            );
        }
    }

    private static void RequestRetirement()
    {
        using var match = new MatchInfo { AllParticipants = new(), RawGameData = new JObject() };
        using var detail = new MatchDetailView(match);
        var cancellation = (CancellationTokenSource)
            typeof(MatchDetailView).GetField("_requestCancellation", PrivateInstance)!.GetValue(detail)!;
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callbackStarted = new ManualResetEventSlim();
        using var callbackRelease = new ManualResetEventSlim();
        using var registration = cancellation.Token.Register(() =>
        {
            callbackStarted.Set();
            // This callback deliberately stays pending after cancellation to verify retirement order.
            callbackRelease.Wait(CancellationToken.None);
        });
        try
        {
            Invoke(detail, "TrackRequest", worker.Task);
            detail.Dispose();
            Check(
                cancellation.Token.IsCancellationRequested && !worker.Task.IsCompleted,
                "Closing a detail view must cancel immediately without blocking for its worker"
            );
            Check(
                callbackStarted.Wait(2000, CancellationToken.None),
                "Retirement must run registered cancellation callbacks"
            );
            worker.SetResult();
            Check(!SourceDisposed(cancellation), "Pending cancellation callbacks must also retain the source");
            callbackRelease.Set();
            Check(
                SpinWait.SpinUntil(() => SourceDisposed(cancellation), 2000),
                "Cancellation source must be released after the worker and callbacks finish"
            );
            detail.Dispose();
        }
        finally
        {
            callbackRelease.Set();
            worker.TrySetResult();
        }
    }

    private static bool SourceDisposed(CancellationTokenSource cancellation)
    {
        try
        {
            _ = cancellation.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            // Token access confirms that retirement released the source after its worker.
            return true;
        }
    }

    private static MatchInfo StatisticsMatch()
    {
        var blue = Player(3, 5);
        blue["teamId"] = 100;
        blue["puuid"] = "self";
        blue["riotIdGameName"] = "蓝队玩家";
        var red = Player(8, 2);
        red["teamId"] = 200;
        red["riotIdGameName"] = "红队玩家";
        red.Remove("goldEarned");
        return new MatchInfo
        {
            CurrPuuid = "self",
            AllParticipants = new() { red, blue },
            RawGameData = new JObject(),
        };
    }

    private static void StatisticsTable()
    {
        using var match = StatisticsMatch();
        using var host = new Form { ClientSize = new Size(700, 480) };
        using var view = new MatchStatisticsView(match);
        // Portrait loading is unrelated to this layout fixture; do not contact the live resource client.
        typeof(MatchStatisticsView).GetField("_loaded", PrivateInstance)!.SetValue(view, true);
        host.Controls.Add(view);
        host.Show();
        Application.DoEvents();
        var grid = view.Controls.OfType<DataGridView>().Single();
        Check(
            grid.Columns.Count == 3 && grid.Columns[1].ToolTipText == "蓝队玩家",
            "Statistics columns must remain ordered by team"
        );
        Check(
            grid.Columns[1].DefaultCellStyle.Font?.Bold == true && grid.Columns[1].DividerWidth == 6,
            "The current player's column and team boundary must retain their emphasis"
        );
        string goldTitle = MatchMetricCatalog
            .Categories.SelectMany(c => c.Metrics)
            .Single(m => m.Key == "goldEarned")
            .Title;
        var gold = grid.Rows.Cast<DataGridViewRow>().Single(r => (string?)r.Cells[0].Value == goldTitle);
        Check(
            (string?)gold.Cells[1].Value == 12345L.ToString("N0") && (string?)gold.Cells[2].Value == "—",
            "Statistics must distinguish a formatted amount from missing data"
        );
        var group = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag is string);
        Invoke(view, "ToggleMetricGroup", group.Index);
        Check(
            !grid.Rows[group.Index + 1].Visible && ((string)group.Cells[0].Value!).StartsWith("▸"),
            "Clicking a group must collapse only its metric rows"
        );
        Invoke(view, "ToggleMetricGroup", group.Index);
        Check(grid.Rows[group.Index + 1].Visible, "Clicking a collapsed group must restore its metrics");
        using var image = new Bitmap(700, 480);
        view.DrawToBitmap(image, new Rectangle(0, 0, 700, 480));
        image.Save(Path.Combine(AppContext.BaseDirectory, "statistics-quality.png"));
    }

    private static void ChartSelectionAndRendering()
    {
        using var match = StatisticsMatch();
        using var host = new Form { ClientSize = new Size(800, 480) };
        using var view = new MatchChartsView(match);
        var chart = (Control)typeof(MatchChartsView).GetField("_chart", PrivateInstance)!.GetValue(view)!;
        chart.GetType().GetField("_loaded", PrivateInstance)!.SetValue(chart, true);
        host.Controls.Add(view);
        host.Show();
        Application.DoEvents();
        var metricsField = chart.GetType().GetField("_metrics", PrivateInstance)!;
        var initial = (MatchMetricCatalog.Metric[])metricsField.GetValue(chart)!;
        Check(
            initial.Length == 1 && initial[0].Key == "totalDamageDealtToChampions",
            "Charts must initially select champion damage"
        );
        Invoke(view, "UpdateSelection", new[] { "goldEarned" }, true, false);
        Check(
            ((MatchMetricCatalog.Metric[])metricsField.GetValue(chart)!).Length == 2,
            "Chart selection must retain existing metrics"
        );
        using var image = new Bitmap(chart.Width, chart.Height);
        chart.DrawToBitmap(image, chart.ClientRectangle);
        image.Save(Path.Combine(AppContext.BaseDirectory, "comparison-quality.png"));
        var hits =
            (List<(Rectangle Bounds, string Text)>)chart.GetType().GetField("_hits", PrivateInstance)!.GetValue(chart)!;
        Check(
            hits.Count == 6 && hits.Any(h => h.Text.Contains("红队玩家") && h.Text.Contains("未提供")),
            "Chart tooltips must preserve player identity and missing metric information"
        );
        var blueLane = hits.First(h => h.Text.Contains("蓝队玩家") && h.Text.Contains(initial[0].Title)).Bounds;
        var redLane = hits.First(h => h.Text.Contains("红队玩家") && h.Text.Contains(initial[0].Title)).Bounds;
        Check(
            image.GetPixel(blueLane.X + 10, blueLane.Y + 2).ToArgb() == Color.FromArgb(0, 161, 183).ToArgb(),
            "Blue team bars must retain their color"
        );
        Check(
            image.GetPixel(redLane.X + 10, redLane.Y + 2).ToArgb() == Color.FromArgb(215, 72, 96).ToArgb(),
            "Red team bars must retain their color"
        );
    }

    private static void PlayerCardLayout()
    {
        using var host = new Form { ClientSize = new Size(360, 480) };
        using var card = new PlayerCardControl { Dock = DockStyle.Fill };
        host.Controls.Add(card);
        var rows = Enumerable
            .Range(0, 5)
            .Select(i =>
            {
                var row = new ListViewItem { ForeColor = i % 2 == 0 ? Color.Green : Color.Red };
                row.SubItems.AddRange(new[] { "海克斯大乱斗", "22/10/24", "2026-09-08 01:54" });
                return row;
            })
            .ToList();
        card.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", rows, Color.SteelBlue);
        host.Show();
        Application.DoEvents();
        Check(card.SummaryPanel.Height == 154, "Ranked summaries must retain room for both rank labels");
        Check(card.ListViewControl.GetItemRect(0).Height >= 46, "History rows must retain room for two lines");
        host.ClientSize = new Size(250, 480);
        Application.DoEvents();
        var rank = card.lblFlexRank;
        Check(rank.Right <= card.SummaryPanel.Width, "Rank text must remain inside a narrow card");
        using var image = new Bitmap(250, 480);
        card.DrawToBitmap(image, new Rectangle(0, 0, 250, 480));
        image.Save(Path.Combine(AppContext.BaseDirectory, "player-card-quality.png"));
        card.SetPlayerInfo("玩家#1234", "未知", "未定级", null!, "公开", rows, Color.SteelBlue);
        Check(card.SummaryPanel.Height == 120 && !rank.Visible, "Unknown ranks must not reserve summary space");
    }
}
