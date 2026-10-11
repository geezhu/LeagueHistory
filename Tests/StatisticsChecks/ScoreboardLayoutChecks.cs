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
        Console.WriteLine("PASS: scoreboard values, tied maxima, responsive metric pages and player card layout.");
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
