using League.Clients;
using League.Controls;
using League.Models;
using System.Net;

internal static class ChampionCompositionChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(20, token);
            if (Fail) return new(HttpStatusCode.ServiceUnavailable);
            string alias = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath));
            return new(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ChampionAnalysis", alias + ".json"))) };
        }
    }
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-composition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler();
            using (var client = new ChampionAnalysisClient(handler, directory))
            {
                var tasks = Enumerable.Range(0, 4).Select(_ => client.GetAsync("Lux", TimeSpan.FromDays(1), CancellationToken.None));
                var snapshots = await Task.WhenAll(tasks);
                Check(handler.Calls == 1 && snapshots.Select(s => s.ContentHash).Distinct().Count() == 1, "Concurrent loads share snapshot");
                handler.Fail = true;
                var stale = await client.GetAsync("lux", TimeSpan.Zero, CancellationToken.None);
                Check(stale.Stale && stale.Warning != null, "Expired cache retained on failure");
                await client.GetAsync("lux", TimeSpan.Zero, CancellationToken.None);
                Check(handler.Calls == 2, "Failure retry backoff");
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await client.GetAsync("morgana", TimeSpan.FromDays(1), cancel.Token); throw new Exception("Cancellation ignored"); }
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
            try { await repaired.GetAsync("../lux", TimeSpan.FromDays(1), CancellationToken.None); throw new Exception("Unsafe alias accepted"); }
            catch (ArgumentException) { }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Console.WriteLine("Champion composition cache checks passed.");
    }

    public static void Render()
    {
        string directory = Path.Combine(Path.GetTempPath(), "league-composition-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new Handler();
            using var client = new ChampionAnalysisClient(handler, directory);
            using var form = new Form { ClientSize = new Size(760, 550) };
            using var view = new ChampionCompositionView(id => id switch { 99 => ("拉克丝", "Lux"), 25 => ("莫甘娜", "Morgana"), _ => ("诺提勒斯", "Nautilus") }, () => TimeSpan.FromDays(1), client) { Dock = DockStyle.Fill };
            form.Controls.Add(view); form.Show();
            var roster = new ChampionSelectRoster([new(99, 0, true, false), new(25, 1, false, true), new(111, 2, false, false), new(0, 3, false, false), new(99, 4, false, false)], [25, 111], true);
            view.SetRoster(roster);
            IEnumerable<Control> Descendants(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c)));
            var grid = Descendants(view).OfType<DataGridView>().Single();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (grid.Rows.Cast<DataGridViewRow>().Count(r => Equals(r.Cells[5].Value, "已读取")) < 4 && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
            Check(grid.Rows.Count == 5 && handler.Calls == 3, "Team duplicates and empty slots retained; only unique heroes downloaded");
            Check(grid.Rows.Cast<DataGridViewRow>().Count(r => Equals(r.Cells[5].Value, "已读取")) == 4, "Rows finish loading");
            var select = Descendants(view).OfType<ComboBox>().Single();
            Check(Descendants(view).OfType<Label>().Any(l=>l.Text.Contains("硬控制") && l.Text.Contains("个位置有记录")),"Overview exposes sourced capability coverage");
            Check(Descendants(view).OfType<Label>().Any(l=>l.Text.Contains("未记录不代表没有")),"Unknown coverage is not presented as a missing capability");
            select.SelectedIndex = 1; Application.DoEvents();
            Check(grid.Rows[0].Cells[0].Value!.ToString()!.Contains("莫甘娜"), "Swap preview replaces only self");
            Check(roster.Team[0].ChampionId == 99 && grid.Rows.Count == 5, "Preview does not mutate actual roster");
            using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(image, form.ClientRectangle);
            image.Save(Path.Combine(AppContext.BaseDirectory, "champion-composition.png"));
            form.ClientSize=new Size(620,650);Application.DoEvents();
            var heroStrip=view.Controls.OfType<FlowLayoutPanel>().Single(p=>p.Controls.Count==5 && p.Controls.Cast<Control>().All(c=>c is Panel));
            Check(heroStrip.Controls.Cast<Control>().All(c=>c.Right<=heroStrip.ClientSize.Width),"All five heroes remain visible in a narrow window");
            Check(Descendants(view).OfType<Label>().Any(l=>l.Text.Contains("移除已记录来源")),"Swap reports capability changes without claiming an unknown ability is absent");
            using var narrow=new Bitmap(form.ClientSize.Width,form.ClientSize.Height);
            form.DrawToBitmap(narrow,form.ClientRectangle);narrow.Save(Path.Combine(AppContext.BaseDirectory,"champion-composition-narrow.png"));
            form.Close(); Application.DoEvents();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Console.WriteLine("Champion composition UI checks passed.");
    }
}
