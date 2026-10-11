using System.Net;
using System.Reflection;
using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class RosterChecks
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) =>
            Task.FromResult(send(r));
    }

    public static void Run()
    {
        var session = JObject.Parse(
            "{\"localPlayerCellId\":0,\"myTeam\":[{\"cellId\":0,\"championId\":103},{\"cellId\":1,\"championId\":0,\"championPickIntent\":157},{\"cellId\":2,\"championId\":103},{\"cellId\":3,\"championId\":0},{\"cellId\":4,\"championId\":0}],\"benchEnabled\":true,\"benchChampions\":[{\"championId\":134},{\"championId\":134},{\"championId\":0}],\"actions\":[[{\"actorCellId\":3,\"type\":\"pick\",\"championId\":99,\"completed\":false},{\"actorCellId\":4,\"type\":\"ban\",\"championId\":1}]]}"
        );
        var roster = ChampionSelectRoster.Parse(session);
        if (
            roster.Team.Length != 5
            || roster.Team.Count(t => t.ChampionId == 103) != 2
            || !roster.Team[0].IsSelf
            || !roster.Team[1].Preview
            || roster.Team[3].ChampionId != 99
            || roster.Team[4].ChampionId != 0
            || !roster.Bench.SequenceEqual(new[] { 134 })
        )
        {
            throw new Exception(
                "Roster must retain team slots, distinguish previews and ignore bans/duplicate bench entries"
            );
        }

        using var owner = new League.FormMain();
        var type = typeof(League.FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var assistant = (Form)Activator.CreateInstance(type, new object[] { owner })!;
        ((System.Windows.Forms.Timer)type.GetField("_timer", flags)!.GetValue(assistant)!).Stop();
        int requests = 0;
        var fake = new AramMetaClient(
            new Handler(r =>
            {
                requests++;
                if (!r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))
                {
                    throw new Exception("Roster must not fetch individual builds");
                }

                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AramMeta", "index.json"))
                    ),
                };
            })
        );
        ((AramMetaClient)type.GetField("_am", flags)!.GetValue(assistant)!).Dispose();
        type.GetField("_am", flags)!.SetValue(assistant, fake);
        type.GetField("_queue", flags)!.SetValue(assistant, 2400);
        type.GetMethod("UpdateRoster", flags)!.Invoke(assistant, new object[] { session, 2400 });
        assistant.Show();
        Application.DoEvents();
        using (var bitmap = new Bitmap(assistant.Width, assistant.Height))
        {
            assistant.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "roster-entry.png"));
        }
        var entry = (Button)type.GetField("_rosterButton", flags)!.GetValue(assistant)!;
        if (!entry.Visible || entry.Parent!.GetChildAtPoint(new Point(entry.Left + 5, entry.Top + 5)) != entry)
        {
            throw new Exception("Roster entry must be visible and unobscured");
        }

        type.GetMethod("ShowRoster", flags)!.Invoke(assistant, null);
        Application.DoEvents();
        var window = (Form)type.GetField("_rosterWindow", flags)!.GetValue(assistant)!;
        var until = DateTime.UtcNow.AddSeconds(5);
        while (requests == 0 && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Application.DoEvents();
        if (requests != 1)
        {
            throw new Exception(
                "Roster uses one complete index: "
                    + requests
                    + " "
                    + string.Join(
                        " / ",
                        window
                            .Controls.OfType<Label>()
                            .Select(l =>
                                l.Text
                                + " "
                                + ((ToolTip)type.GetField("_tips", flags)!.GetValue(assistant)!).GetToolTip(l)
                            )
                    )
            );
        }

        session["benchChampions"] = new JArray(
            Enumerable.Range(90000, 18).Select(id => new JObject { ["championId"] = id })
        );
        type.GetMethod("UpdateRoster", flags)!.Invoke(assistant, new object[] { session, 2400 });
        Application.DoEvents();
        if (requests != 1)
        {
            throw new Exception("Roster changes must reuse loaded tiers");
        }

        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "champion-roster.png"));
        }
        window.ClientSize = new Size(350, 450);
        Application.DoEvents();
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "champion-roster-narrow.png"));
        }
        type.GetMethod("Reset", flags)!.Invoke(assistant, null);
        if (!window.IsDisposed)
        {
            throw new Exception("Leaving champion select must close roster");
        }

        Console.WriteLine("PASS: roster team/bench parsing, shared rankings, updates and phase cleanup.");
    }
}
