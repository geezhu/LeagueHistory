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
        Grades(index, mayhem.GradesMessage!, AramTeamScore.Evaluate(index, ids.Take(5).ToArray()));
        mayhem.Parent!.Dispose();
        ChatSummary(index, ids, Name);
        AnalysisSummary(index, roster, Name, message);

        NormalAram(index, roster, Name);

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
        Check(
            failed.ChatMessage == null && failed.GradesMessage == null,
            "Nothing is offered for sending until statistics load"
        );
        failed.Parent!.Dispose();
        Console.WriteLine("PASS: composition view shows estimate, capabilities, synergy and ordered swap suggestions.");
    }

    // Every capability travels as a grade without its total, and the line fits one chat message.
    private static void Grades(AramMetaIndex index, string message, AramTeamEvaluation evaluation)
    {
        var grades = AramCapabilityGrades.Build(index);
        Check(
            message.StartsWith("【海克斯大乱斗·阵容能力】")
                && evaluation.Capabilities.All(capability =>
                    message.Contains($"{capability.Name} {grades.Grade(capability, 5)} ·")
                    || message.Contains($"{capability.Name} {grades.Grade(capability, 5)}；")
                )
                && !System.Text.RegularExpressions.Regex.IsMatch(message, @"\d\.\d")
                && message.Length <= 180,
            "The grades message names every capability's grade without totals: " + message
        );

        AramTeamCapability Front(double sum, bool lack) => new("front", "前排", sum, 1.8, lack);
        Check(
            grades.Grade(Front(100, false), 5) == "S+"
                && grades.Grade(Front(0, false), 5) == "C"
                && grades.Grade(Front(100, true), 5) == "D",
            "Grades run from C to S+ by percentile, and a gap is always D"
        );
        var again = AramCapabilityGrades.Build(index);
        Check(
            evaluation.Capabilities.All(capability =>
                again.Percentile(capability, 5) == grades.Percentile(capability, 5)
            ),
            "The same statistics always give the same grades"
        );
        double one = grades.Percentile(Front(1.5, false), 1),
            five = grades.Percentile(Front(1.5, false), 5);
        Check(one > five, "A total ranks against random teams of the same size: " + one + " / " + five);
    }

    // Normal ARAM keeps the capabilities and grades but leaves out the Mayhem-trained estimate.
    private static void NormalAram(AramMetaIndex index, ChampionSelectRoster roster, Func<int, string> name)
    {
        var aram = Render(roster, _ => Task.FromResult(index), false, name, "composition-view-aram");
        Check(
            !Labels(aram).Any(label => label.Text.Contains("估计胜率 "))
                && Labels(aram).Any(label => label.Text.Contains("不显示估计胜率")),
            "Normal ARAM shows capabilities without a Mayhem-trained win estimate"
        );
        Check(
            aram.ChatMessage!.StartsWith("【大乱斗·阵容分析】") && !aram.ChatMessage.Contains("估计胜率"),
            "The normal ARAM chat summary leaves out the Mayhem-trained estimate"
        );
        Check(aram.GradesMessage!.StartsWith("【大乱斗·阵容能力】"), "The normal ARAM grades name the mode");
        Check(
            League.Services.TeamAnalysisChatMessage.Analyze(index, roster, false, name) is var plain
                && plain.Message == aram.ChatMessage
                && !plain.Brief.Contains("胜率"),
            "The normal ARAM assistant line leaves out the estimate"
        );
        aram.Parent!.Dispose();
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

    // The assistant line sends what the roster window sends, plus a brief that names the estimate first.
    private static void AnalysisSummary(
        AramMetaIndex index,
        ChampionSelectRoster roster,
        Func<int, string> name,
        string message
    )
    {
        var analysis = League.Services.TeamAnalysisChatMessage.Analyze(index, roster, true, name);
        Check(analysis.Message == message, "The assistant line sends the same summary as the roster window");
        Check(
            analysis.Brief.StartsWith("阵容 胜率 ")
                && analysis.Brief.Contains("短板")
                && analysis.Brief.Contains("物理"),
            "The assistant line leads with the estimate, gaps and damage mix: " + analysis.Brief
        );
        var unpicked = roster with { Team = roster.Team.Select(entry => entry with { IsSelf = false }).ToArray() };
        Check(
            League.Services.TeamAnalysisChatMessage.Analyze(index, unpicked, true, name).Best == null,
            "No swap is suggested before the user's own pick is known"
        );
        AssistantLine(index, roster, analysis.Brief);
    }

    // The main panel line: the assistant shows the brief and enables sending once statistics and a pick exist.
    private static void AssistantLine(AramMetaIndex index, ChampionSelectRoster roster, string brief)
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(League.FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        using var owner = new League.FormMain();
        using var assistant = (Form)Activator.CreateInstance(type, owner)!;
        T Field<T>(string name) => (T)type.GetField(name, Flags)!.GetValue(assistant)!;
        void Set(string name, object value) => type.GetField(name, Flags)!.SetValue(assistant, value);
        Field<System.Windows.Forms.Timer>("_timer").Stop();
        Set("_queue", 2400);
        Set("_selecting", true);
        Set("_roster", roster);
        Set("_analysisIndex", index);
        assistant.StartPosition = FormStartPosition.Manual;
        assistant.Location = new Point(-2000, 0);
        assistant.Show();
        type.GetMethod("RefreshTeamAnalysis", Flags)!.Invoke(assistant, null);
        Application.DoEvents();
        var text = Field<Label>("_analysisText");
        // Champion names come from game resources in the app; the part before the swap does not depend on them.
        string conclusion = brief.Split(" · 可换")[0];
        Check(
            Field<Panel>("_analysisBar").Visible && text.Text.StartsWith(conclusion),
            $"The assistant shows the team conclusion: {text.Text}"
        );
        Check(Field<Button>("_sendAnalysis").Enabled, "Sending is offered during champion select");
        using var bitmap = new Bitmap(assistant.Width, assistant.Height);
        assistant.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "assistant-team-analysis.png"));
        Set("_selecting", false);
        type.GetMethod("ClearRoster", Flags)!.Invoke(assistant, null);
        Check(!Field<Panel>("_analysisBar").Visible, "Leaving champion select hides the line");
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
