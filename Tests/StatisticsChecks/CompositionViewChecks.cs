using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

internal static class CompositionViewChecks
{
    public static void Run()
    {
        var fixture = JObject.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/AramMeta/team-score.json"))
        );
        var index = AramMetaIndex.Parse((JObject)fixture["index"]!);
        int[] ids = index.Champions.Select(champion => champion.Id).ToArray();
        var roster = new ChampionSelectRoster(
            ids.Take(5).Select((id, slot) => new ChampionSelectRosterEntry(id, slot, slot == 0, false)).ToArray(),
            ids.Skip(5).ToArray(),
            true
        );
        string Name(int id) => index.Champions.First(champion => champion.Id == id).Name;

        var mayhem = Render(roster, _ => Task.FromResult(index), true, Name, "composition-view");
        var lines = Labels(mayhem).Select(label => label.Text).ToArray();
        Check(lines.Any(text => text.StartsWith("估计胜率")), "Mayhem analysis leads with the estimated win rate");
        Check(
            mayhem.Controls.OfType<Panel>().Count() == 7,
            "All seven capabilities are drawn against their gap thresholds"
        );
        var swaps = lines.Where(text => text.StartsWith("换成 ")).ToArray();
        var expected = roster
            .Bench.Select(id =>
                AramTeamScore.Evaluate(index, [id, .. ids.Skip(1).Take(4)]).EstimatedWinRate
                - AramTeamScore.Evaluate(index, ids.Take(5).ToArray()).EstimatedWinRate
            )
            .OrderByDescending(delta => delta)
            .ToArray();
        Check(swaps.Length == roster.Bench.Length, "Every bench champion is compared as a replacement for the user");
        Check(
            swaps.First().Contains(Signed(expected.First())) && swaps.Last().Contains(Signed(expected.Last())),
            "Swaps are ordered by the change in estimated win rate"
        );
        string message = mayhem.ChatMessage!;
        Check(
            message.StartsWith("【海克斯大乱斗·阵容分析】")
                && message.Contains("估计胜率")
                && message.Length <= 180
                && !message.Contains(" %"),
            "The Mayhem chat summary carries the estimate within one chat line: " + message
        );
        mayhem.Parent!.Dispose();
        ChatSummary(index, ids, Name);

        var aram = Render(roster, _ => Task.FromResult(index), false, Name, "composition-view-aram");
        Check(
            !Labels(aram).Any(label => label.Text.Contains("估计胜率 "))
                && Labels(aram).Any(label => label.Text.Contains("不显示估计胜率")),
            "Normal ARAM shows capabilities without a Mayhem-trained win estimate"
        );
        Check(
            aram.ChatMessage!.StartsWith("【大乱斗·阵容分析】") && !aram.ChatMessage.Contains("估计胜率"),
            "The normal ARAM chat summary leaves out the Mayhem-trained estimate"
        );
        aram.Parent!.Dispose();

        var failed = Render(
            roster,
            _ => Task.FromException<AramMetaIndex>(new HttpRequestException("offline")),
            true,
            Name,
            null
        );
        Check(
            Labels(failed).Any(label => label.Text.EndsWith("点击重试")),
            "A failed statistics load offers a retry instead of an empty panel"
        );
        Check(failed.ChatMessage == null, "Nothing is offered for sending until statistics load");
        failed.Parent!.Dispose();
        Console.WriteLine("PASS: composition view shows estimate, capabilities, synergy and ordered swap suggestions.");
    }

    private static void ChatSummary(AramMetaIndex index, int[] ids, Func<int, string> name)
    {
        var current = AramTeamScore.Evaluate(index, ids.Take(5).ToArray());
        var helpful = new AramTeamSwap(ids[5], 0.012, current.Capabilities.Take(1).ToArray(), []);
        var harmful = new AramTeamSwap(ids[6], -0.02, [], current.Capabilities.Take(1).ToArray());
        string withSwap = League.Services.TeamAnalysisChatMessage.Build(current, helpful, true, "16.20", name);
        string withoutSwap = League.Services.TeamAnalysisChatMessage.Build(current, harmful, true, "16.20", name);
        Check(
            withSwap.Contains("可换 " + name(ids[5]) + " +1.2%") && !withoutSwap.Contains("可换"),
            "Only a swap that raises the estimate without new gaps is announced"
        );
    }

    private static League.Controls.ChampionCompositionView Render(
        ChampionSelectRoster roster,
        Func<CancellationToken, Task<AramMetaIndex>> load,
        bool mayhem,
        Func<int, string> name,
        string? screenshot
    )
    {
        var host = new Form
        {
            ClientSize = new Size(620, 720),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-2000, 0),
            BackColor = Color.FromArgb(8, 29, 40),
            ShowInTaskbar = false,
        };
        var view = new League.Controls.ChampionCompositionView(name, load, mayhem, CancellationToken.None)
        {
            Dock = DockStyle.Fill,
        };
        host.Controls.Add(view);
        host.Show();
        view.SetRoster(roster);
        Pump(() => view.Loading.IsCompleted);
        Application.DoEvents();
        if (screenshot != null)
        {
            using var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, screenshot + ".png"));
        }

        return view;
    }

    private static IEnumerable<Label> Labels(Control root) => root.Controls.OfType<Label>();

    private static string Signed(double value) =>
        Math.Abs(value) < 0.0005 ? "±0.0%" : (value > 0 ? "+" : "−") + Math.Abs(value).ToString("P1");

    private static void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(done(), "Composition statistics load completes");
    }
}
