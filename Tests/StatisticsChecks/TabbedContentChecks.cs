using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The player tabs above the history list: opening one per player, refusing to open the same player twice,
/// closing one, and clearing them all. The owner profile icon is passed as "0" so nothing is downloaded.
/// </summary>
internal static class TabbedContentChecks
{
    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var content = new MatchTabContent { Dock = DockStyle.Fill };
        host.Controls.Add(content);
        host.Show();
        content.CreateControl();

        ClosableTabControl tabs = content.MainTabControl;
        Check(tabs != null, "The content owns its closable tab control");
        Check(content.Controls.Count > 0, "The content builds its own surface");

        var ranks = RankedStats.FromJson(
            JObject.Parse("""{"queueMap":{"RANKED_SOLO_5x5":{"tier":"GOLD","division":"II","leaguePoints":45}}}""")
        );

        int before = tabs.TabPages.Count;
        content.CreateNewTab("7", "甲", "0001", "puuid-1", "0", "30", "公开", ranks);
        PumpUntil(() => tabs.TabPages.Count == before + 1);
        Check(tabs.TabPages.Count == before + 1, "A player gets a tab");
        Check(tabs.TabPages.Cast<TabPage>().Any(page => page.Text == "甲#0001"), "The tab is named after the player");

        int opened = tabs.TabPages.Count;
        content.CreateNewTab("7", "甲", "0001", "puuid-1", "0", "30", "公开", ranks);
        Check(tabs.TabPages.Count == opened, "Opening the same player again selects the tab instead of adding one");

        content.CreateNewTab("7", "乙", "0002", "", "0", "30", "公开", ranks);
        Check(tabs.TabPages.Count == opened, "A player without a puuid gets no tab");

        var target = tabs.TabPages.Cast<TabPage>().First(page => page.Text == "甲#0001");
        content.CloseTab(target);
        PumpUntil(() => tabs.TabPages.Count == opened - 1);
        Check(tabs.TabPages.Count == opened - 1, "A tab can be closed");

        content.CreateNewTab("8", "丙", "0003", "puuid-2", "0", "12", "隐藏", ranks);
        PumpUntil(() => tabs.TabPages.Count == opened);
        content.CleanupAllTabs();
        PumpUntil(() => tabs.TabPages.Count == 0);
        Check(tabs.TabPages.Count == 0, "Cleaning the content closes every tab");

        content.DrawToBitmap(new Bitmap(1200, 800), content.ClientRectangle);
        content.Dispose();
        host.Close();
        Console.WriteLine(
            "PASS: a tab per player, the same player not opened twice, a tab closed, all tabs cleared, and the "
                + "closable tab control's own layout."
        );
    }
}
