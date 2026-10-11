using System.Net;
using League.Clients;
using League.Controls;
using League.Models;

internal static class ChampionCompositionChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(20, token);
            if (Fail)
                return new(HttpStatusCode.ServiceUnavailable);
            string alias = Path.GetFileNameWithoutExtension(
                Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath)
            );
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    File.ReadAllText(
                        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ChampionAnalysis", alias + ".json")
                    )
                ),
            };
        }
    }

    public static async Task RunAsync()
    {
        WeaknessChecks();
        TacticsChecks();
        string directory = Path.Combine(Path.GetTempPath(), "league-composition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler();
            using (var client = new ChampionAnalysisClient(handler, directory))
            {
                var tasks = Enumerable
                    .Range(0, 4)
                    .Select(_ => client.GetAsync("Lux", TimeSpan.FromDays(1), CancellationToken.None));
                var snapshots = await Task.WhenAll(tasks);
                Check(
                    handler.Calls == 1 && snapshots.Select(s => s.ContentHash).Distinct().Count() == 1,
                    "Concurrent loads share snapshot"
                );
                handler.Fail = true;
                var stale = await client.GetAsync("lux", TimeSpan.Zero, CancellationToken.None);
                Check(stale.Stale && stale.Warning != null, "Expired cache retained on failure");
                await client.GetAsync("lux", TimeSpan.Zero, CancellationToken.None);
                Check(handler.Calls == 2, "Failure retry backoff");
                using var cancel = new CancellationTokenSource();
                cancel.Cancel();
                try
                {
                    await client.GetAsync("morgana", TimeSpan.FromDays(1), cancel.Token);
                    throw new Exception("Cancellation ignored");
                }
                catch (OperationCanceledException) { }
                Check(handler.Calls == 2, "Canceled request does not download");
            }
            var restartedHandler = new Handler { Fail = true };
            using (var restarted = new ChampionAnalysisClient(restartedHandler, directory))
            {
                var restored = await restarted.GetAsync("lux", TimeSpan.FromDays(1), CancellationToken.None);
                Check(!restored.Stale && restartedHandler.Calls == 0, "Disk cache survives restart");
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "lux.json"), "bad json");
            var repairHandler = new Handler();
            using var repaired = new ChampionAnalysisClient(repairHandler, directory);
            await repaired.GetAsync("lux", TimeSpan.FromDays(1), CancellationToken.None);
            Check(repairHandler.Calls == 1, "Corrupt cache refreshed");
            try
            {
                await repaired.GetAsync("../lux", TimeSpan.FromDays(1), CancellationToken.None);
                throw new Exception("Unsafe alias accepted");
            }
            catch (ArgumentException) { }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        Console.WriteLine("Champion composition cache checks passed.");
    }

    private static void WeaknessChecks()
    {
        ChampionAnalysisReport Report(params string[] tags) =>
            new("test", "fixture", "test", [], [new("Q", "/spell", "unknown", [], [], tags, [])], []);
        var reports = new Dictionary<int, ChampionAnalysisReport>
        {
            [1] = Report("Trait_ImmobilizingCCSpell"),
            [2] = Report(),
            [3] = Report("Trait_Shield"),
        };
        Check(
            CompositionWeakness.HeroInsights(reports[1]).Any(s => s.Contains("控制依赖") && s.Contains("Q")),
            "Single hero gets actionable control dependency"
        );
        Check(
            !CompositionWeakness.HeroInsights(reports[2]).Any(s => s.Contains("缺少护盾")),
            "Missing tags do not imply absent abilities"
        );
        var ultimate = reports[1] with
        {
            Spells = [reports[1].Spells[0] with { Key = "R" }],
            Attributes = [new("基础攻速", "", [0.5], "fixture")],
        };
        Check(
            CompositionWeakness.HeroInsights(ultimate).Any(s => s.Contains("大招不可用")),
            "Ultimate-only control gets specific advice"
        );
        Check(
            CompositionWeakness.HeroInsights(ultimate).Any(s => s.Contains("2 秒一次")),
            "Attack interval uses base attack speed"
        );
        var single = CompositionWeakness.Analyze([1, 2, 2, 2, 2], reports);
        Check(
            single.Complete && single.Concerns.Length == 1 && single.Concerns[0].Providers.SequenceEqual(new[] { 1 }),
            "Concentrated control identifies its actual provider"
        );
        Check(single.Unconfirmed.Contains("护盾"), "No shield tags means unconfirmed, not absent");
        var duplicates = CompositionWeakness.Analyze([1, 1, 2, 2, 2], reports);
        Check(duplicates.Concerns.Length == 0, "Duplicate champions in separate slots count as independent providers");
        var partial = CompositionWeakness.Analyze([1, 0, 999, 2, 2], reports);
        Check(!partial.Complete && partial.Known == 3, "Empty and unread slots remain unknown");
        Check(
            CompositionWeakness.Compare([1, 0, 999, 2, 2], [1, 1, 2, 2, 2], reports).Single().Contains("暂不判断"),
            "Incomplete data cannot claim risk improvement"
        );
        Check(
            CompositionWeakness.Compare([1, 2, 2, 2, 2], [1, 1, 2, 2, 2], reports).Single().Contains("集中风险降低"),
            "Additional control provider reduces recorded concentration"
        );
        Check(
            CompositionWeakness.Compare([1, 1, 2, 2, 2], [1, 2, 2, 2, 2], reports).Single().Contains("集中风险增加"),
            "Removing a backup provider increases concentration"
        );
        Check(
            CompositionWeakness
                .Compare([1, 2, 2, 2, 2], [2, 2, 2, 2, 2], reports)
                .Single()
                .Contains("不能认定能力缺失"),
            "Losing the last recorded provider does not prove absence"
        );
        Check(
            CompositionWeakness.Analyze([3, 2, 2, 2, 2], reports).Concerns.Single().Impact.Contains("仅作用自身"),
            "Shield tags are not treated as confirmed team protection"
        );
        Check(
            CompositionWeakness.Analyze([], reports).Complete == false,
            "Empty rosters do not produce a complete assessment"
        );
        Console.WriteLine("PASS: composition concentration, incomplete evidence, duplicates and swap risks.");
    }

    private static void TacticsChecks()
    {
        ChampionAnalysisReport Report(string targeting = "Location") =>
            new(
                "test",
                "fixture",
                "test",
                [new("普攻距离", "", [175], "fixture")],
                [
                    new(
                        "Q",
                        "/q",
                        targeting,
                        [new("显示施法范围", "", [900], "fixture")],
                        [],
                        ["Trait_ImmobilizingCCSpell"],
                        []
                    ),
                ],
                []
            );
        var reports = new Dictionary<int, ChampionAnalysisReport> { [1] = Report(), [2] = Report("Target") };
        var texts = new Dictionary<int, CompositionSkillText[]>
        {
            [1] =
            [
                new("Q", "测试控制", "命中第一个敌人时禁锢并造成魔法伤害。"),
                new("W", "自盾", "为自己提供护盾。"),
                new("R", "大招", "造成魔法伤害。"),
            ],
            [2] =
            [
                new("Q", "控制", "眩晕目标并造成物理伤害。"),
                new("W", "保护", "为友方英雄提供护盾。"),
                new("R", "大招", "造成物理伤害。"),
            ],
        };
        var findings = CompositionTactics.Analyze([1, 1, 1, 1, 1], reports, texts, id => "英雄" + id);
        Check(findings.Length == 5, "All requested tactical dimensions are evaluated");
        Check(
            findings.All(f => f.Radar?.Score is >= 0 and <= 100),
            "Complete fixture has five bounded radar dimensions"
        );
        Check(
            findings[0].Radar!.Score == 100 && findings[1].Radar!.Score == 0 && findings[4].Radar!.Score == 0,
            "Coverage and mono damage scores follow fixed rules"
        );
        Check(
            findings.Single(f => f.Category == "控制分布").Detail.Contains("5/5"),
            "Duplicate slots count independently"
        );
        Check(
            findings.Single(f => f.Category == "团队保护").Detail.Contains("0/5"),
            "Self shield does not count as team protection"
        );
        Check(
            findings.Single(f => f.Category == "可靠先手").Status.Contains("条件"),
            "Skillshot control is not treated as guaranteed engage"
        );
        Check(
            findings.Single(f => f.Category == "伤害结构").Status.Contains("集中"),
            "Uniform spell damage detects concentration"
        );
        Check(
            findings.Single(f => f.Category == "射程分布").Evidence.Any(e => e.Contains("900")),
            "Damaging spell display range is shown with scope"
        );
        findings = CompositionTactics.Analyze([1, 1, 1, 1, 2], reports, texts, id => "英雄" + id);
        Check(findings.Single(f => f.Category == "团队保护").Detail.Contains("1/5"), "Ally shield is included");
        Check(
            findings.Single(f => f.Category == "可靠先手").Status.Contains("指向"),
            "Explicit targeting produces a clue, not guaranteed hit"
        );
        var incomplete = CompositionTactics.Analyze([1, 0, 999], reports, texts, id => "英雄" + id);
        Check(incomplete.All(f => f.Detail.Contains("资料未齐")), "Incomplete roster cannot imply full-team coverage");
        Check(incomplete.All(f => f.Radar!.Score == null), "Unknown radar evidence is not scored zero");
        Check(
            CompositionTactics.Analyze([], reports, texts, id => "未知").All(f => !f.Status.Contains("集中")),
            "Empty team does not generate concentration"
        );
        Console.WriteLine(
            "PASS: tactical control, ally protection, conditional engage, range, damage and unknown evidence."
        );
    }

    public static void Render()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-composition-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler();
            using var client = new ChampionAnalysisClient(handler, directory);
            using var form = new Form { ClientSize = new Size(760, 550) };
            using var view = new ChampionCompositionView(
                id =>
                    id switch
                    {
                        99 => ("拉克丝", "Lux"),
                        25 => ("莫甘娜", "Morgana"),
                        _ => ("诺提勒斯", "Nautilus"),
                    },
                () => TimeSpan.FromDays(1),
                client
            )
            {
                Dock = DockStyle.Fill,
            };
            form.Controls.Add(view);
            form.Show();
            var roster = new ChampionSelectRoster(
                [
                    new(99, 0, true, false),
                    new(25, 1, false, true),
                    new(111, 2, false, false),
                    new(0, 3, false, false),
                    new(99, 4, false, false),
                ],
                [25, 111],
                true
            );
            view.SetRoster(roster);
            IEnumerable<Control> Descendants(Control root) =>
                root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
            var grid = Descendants(view).OfType<DataGridView>().Single();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (
                grid.Rows.Cast<DataGridViewRow>().Count(r => Equals(r.Cells[5].Value, "已读取")) < 4
                && DateTime.UtcNow < deadline
            )
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(
                grid.Rows.Count == 5 && handler.Calls == 3,
                "Team duplicates and empty slots retained; only unique heroes downloaded"
            );
            Check(
                grid.Rows.Cast<DataGridViewRow>().Count(r => Equals(r.Cells[5].Value, "已读取")) == 4,
                "Rows finish loading"
            );
            var select = Descendants(view).OfType<ComboBox>().Single();
            Check(
                Descendants(view)
                    .OfType<Label>()
                    .Any(l => l.Text.Contains("硬控制") && l.Text.Contains("个位置有记录")),
                "Overview exposes sourced capability coverage"
            );
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text.Contains("未记录不代表没有")),
                "Unknown coverage is not presented as a missing capability"
            );
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text == "可能的短板"),
                "Current roster exposes weakness summary"
            );
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text.StartsWith("可靠先手 ·")),
                "Tactical findings are rendered"
            );
            select.SelectedIndex = 1;
            Application.DoEvents();
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text == "可能的短板 · 换人预览"),
                "Preview assessment is clearly labelled"
            );
            Check(grid.Rows[0].Cells[0].Value!.ToString()!.Contains("莫甘娜"), "Swap preview replaces only self");
            Check(roster.Team[0].ChampionId == 99 && grid.Rows.Count == 5, "Preview does not mutate actual roster");
            using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(image, form.ClientRectangle);
            image.Save(Path.Combine(AppContext.BaseDirectory, "champion-composition.png"));
            form.ClientSize = new Size(620, 650);
            Application.DoEvents();
            var heroStrip = view
                .Controls.OfType<FlowLayoutPanel>()
                .Single(p => p.Controls.Count == 5 && p.Controls.Cast<Control>().All(c => c is Panel));
            Check(
                heroStrip.Controls.Cast<Control>().All(c => c.Right <= heroStrip.ClientSize.Width),
                "All five heroes remain visible in a narrow window"
            );
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text.Contains("移除已记录来源")),
                "Swap reports capability changes without claiming an unknown ability is absent"
            );
            using var narrow = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(narrow, form.ClientRectangle);
            narrow.Save(Path.Combine(AppContext.BaseDirectory, "champion-composition-narrow.png"));
            view.SetRoster(new ChampionSelectRoster([new(99, 0, true, false)], [], false));
            Application.DoEvents();
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text == "英雄能力分析"),
                "Single player switches to hero analysis"
            );
            Check(
                Descendants(view).OfType<Label>().Any(l => l.Text.Contains("控制依赖")),
                "Single player sees specific advice instead of empty team assessment"
            );
            using var solo = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(solo, form.ClientRectangle);
            solo.Save(Path.Combine(AppContext.BaseDirectory, "champion-composition-solo.png"));
            Check(Descendants(view).OfType<CompositionRadar>().Count() == 1, "Overview displays a radar");
            var embedded = Descendants(view).OfType<CompositionRadar>().Single();
            Check(
                embedded.Height >= 250 && embedded.Bottom <= embedded.Parent!.ClientSize.Height,
                "Radar remains inside its auto-sized card"
            );
            // Count removed cards to detect repeated whole-page rebuilding.
            int overviewPasses = 0;
            var overview = embedded.Parent.Parent;
            overview.ControlRemoved += (_, _) => overviewPasses++;
            int initialCards = overview.Controls.Count;
            var queue = typeof(ChampionCompositionView).GetMethod(
                "QueueDashboardRefresh",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!;
            for (int i = 0; i < 100; i++)
                queue.Invoke(view, null);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 450)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            Check(overviewPasses == initialCards, "Burst of 100 refresh requests produces a single overview rebuild");
            watch.Restart();
            for (int i = 0; i < 20; i++)
            {
                form.ClientSize = new Size(i % 2 == 0 ? 760 : 620, 650);
                Application.DoEvents();
            }
            Check(watch.Elapsed < TimeSpan.FromSeconds(5), "Repeated resizing does not enter a layout loop");
            Console.WriteLine(
                $"PASS: 100 refresh requests coalesced; 20 resizes completed in {watch.ElapsedMilliseconds} ms."
            );
            var axes = new[]
            {
                new CompositionRadarAxis("控制覆盖", 80, "四个位置有控制"),
                new("保护覆盖", 40, "两个位置有保护"),
                new("指向控制", 20, "一个位置有指向控制"),
                new("普攻距离", 53, "平均 530"),
                new("伤害多样", 80, "类型平衡参考"),
            };
            using var radar = new CompositionRadar(axes) { Size = new Size(540, 290) };
            using var radarImage = new Bitmap(540, 290);
            radar.DrawToBitmap(radarImage, new Rectangle(0, 0, 540, 290));
            radarImage.Save(Path.Combine(AppContext.BaseDirectory, "composition-radar-complete.png"));
            radar.Width = 280;
            using var compactImage = new Bitmap(280, 290);
            radar.DrawToBitmap(compactImage, new Rectangle(0, 0, 280, 290));
            compactImage.Save(Path.Combine(AppContext.BaseDirectory, "composition-radar-narrow.png"));
            Check(
                radar.AccessibleDescription!.Contains("控制覆盖") && radar.AccessibleDescription.Contains("80"),
                "Radar exposes accessible values"
            );
            form.Close();
            Application.DoEvents();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        Console.WriteLine("Champion composition UI checks passed.");
    }
}
