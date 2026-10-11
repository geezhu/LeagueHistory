using System.Reflection;
using League.Controls;
using League.Models;
using League.Services;

internal static class LoadingViewChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        TeamOrderAndSelection();
        MasteryAndEmptyRows();
        BorrowedArtworkAndClose();
        Console.WriteLine(
            "PASS: loading view team order, ten retained rows, child selection, mastery identity, empty slots and borrowed artwork."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static IEnumerable<Control> Children(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Children(control)));

    private static LoadingPlayer Player(string key, int hero, int team, bool self = false) =>
        new(key, key, key, 1, hero, team, self);

    private static PlayerChampionTier Tier(string tier = "T2") =>
        new(tier, "胜 52.8%", "OP.GG\n普通大乱斗", 450, "none") { PickRate = "7.5%" };

    private static LoadingOverlayView View() => new(id => $"英雄 {id}", player => $"近期统计：{player.Key}");

    private static Panel[] Cards(LoadingOverlayView view) =>
        Field<TableLayoutPanel>(view, "_teams").Controls.OfType<Panel>().ToArray();

    private static void TeamOrderAndSelection()
    {
        using var view = View();
        var players = Enumerable
            .Range(0, 10)
            .Select(index => Player($"p{index}", index + 1, index < 5 ? 100 : 200, index == 5))
            .ToArray();
        var roster = new LoadingRoster("game", 450, "ARAM", players);
        var ranks = players.ToDictionary(player => player.Key, _ => Tier());
        view.UpdateRoster(roster, ranks, new Dictionary<int, Image>(), "已缓存");
        var teams = Field<TableLayoutPanel>(view, "_teams");
        var first = (Panel)teams.GetControlFromPosition(0, 1)!;
        Check((first.Tag as LoadingPlayer)?.Key == "p5", "A red-side self puts team 200 in the own column.");
        Check(
            teams.GetControlFromPosition(0, 0)!.Text == "我方" && teams.GetControlFromPosition(1, 0)!.Text == "对方",
            "Team labels follow confirmed self identity."
        );
        var retained = Cards(view);
        for (int index = 0; index < 100; index++)
        {
            view.UpdateRoster(roster, ranks, new Dictionary<int, Image>(), "已缓存");
        }
        Check(
            Cards(view).Length == 10 && Cards(view).SequenceEqual(retained),
            "Repeated updates retain all ten slot controls."
        );
        var hero = Children(first).OfType<Label>().Single(label => label.Text == "英雄 6");
        typeof(Control).GetMethod("OnClick", PrivateInstance)!.Invoke(hero, new object[] { EventArgs.Empty });
        var details = Field<Label>(view, "_details");
        Check(
            details.Text.Contains("p5 · 英雄 6")
                && details.Text.Contains("近期统计：p5")
                && details.Text.Contains("OP.GG · 普通大乱斗"),
            "Clicking a child label shows the selected player's details and source."
        );
        var changed = roster with
        {
            Players = players.Select(player => player.Key == "p5" ? player with { ChampionId = 99 } : player).ToArray(),
        };
        view.UpdateRoster(changed, ranks, new Dictionary<int, Image>(), "更新");
        Check(
            details.Text.Contains("p5 · 英雄 99"),
            "Selection follows the latest champion for the same confirmed player."
        );
    }

    private static void MasteryAndEmptyRows()
    {
        using var view = View();
        var hidden = new LoadingPlayer("hidden", "隐藏玩家", "", 0, 25, 200, false, true);
        var own = Player("own", 99, 100, true);
        var roster = new LoadingRoster("game", 450, "ARAM", [own, hidden]);
        var ranks = new Dictionary<string, PlayerChampionTier> { [own.Key] = Tier("T4") };
        var masteries = new Dictionary<string, PlayerChampionMastery>
        {
            [own.Key] = new(99, 7, 12345, null, true),
            [hidden.Key] = PlayerChampionMastery.Unavailable(25),
        };
        view.UpdateRoster(roster, ranks, new Dictionary<int, Image>(), "已缓存", masteries);
        var rows = Cards(view);
        Check(
            rows.Count(row => row.Tag is LoadingPlayer) == 2 && rows.Length == 10,
            "An incomplete roster keeps the unused slots."
        );
        var ownCard = rows.Single(row => row.Tag is LoadingPlayer player && player.Key == own.Key);
        var mastery = Children(ownCard).OfType<Label>().Single(label => label.Text == masteries[own.Key].Text);
        var tips = Field<ToolTip>(view, "_masteryTips");
        Check(tips.GetToolTip(mastery).Contains("12,345"), "A matching mastery record retains its full-point tooltip.");
        Check(
            Children(view).OfType<Label>().Count(label => label.Text == "成就未公开") == 1,
            "One hidden player does not hide everyone else's mastery."
        );
        masteries[own.Key] = new(25, 9, 99999, null, true);
        view.UpdateRoster(roster, ranks, new Dictionary<int, Image>(), "已更新", masteries);
        Check(
            mastery.Text == "成就读取中…" && tips.GetToolTip(mastery) == "",
            "A previous champion's mastery cannot appear on the new champion."
        );
        view.UpdateRoster(
            null,
            new Dictionary<string, PlayerChampionTier>(),
            new Dictionary<int, Image>(),
            "等待新对局"
        );
        Check(
            rows.All(row => row.Tag == null)
                && rows.All(row => Children(row).OfType<PictureBox>().Single().Image == null),
            "Resetting removes all player and icon associations."
        );
        Check(
            !Children(view)
                .OfType<Label>()
                .Any(label => label.Text.Contains("成就读取中") || label.Text.Contains("成就未公开")),
            "Empty slots clear previous mastery text."
        );
        Check(
            Field<Label>(view, "_details").Text.Contains("英雄统计尚未读取"),
            "An empty roster reports missing statistics."
        );
    }

    private static void BorrowedArtworkAndClose()
    {
        using var image = new Bitmap(48, 48);
        var icons = new Dictionary<int, Image> { [99] = image };
        var roster = new LoadingRoster("game", 450, "ARAM", [Player("self", 99, 100, true)]);
        using var view = View();
        using var host = new Form { ClientSize = new Size(1020, 670), ShowInTaskbar = false };
        host.Controls.Add(view);
        view.UpdateRoster(roster, new Dictionary<string, PlayerChampionTier>(), icons, "已缓存");
        Check(
            Children(view).OfType<PictureBox>().Count(icon => ReferenceEquals(icon.Image, image)) == 1,
            "The slot borrows its owner's cached image."
        );
        view.UpdateRoster(null, new Dictionary<string, PlayerChampionTier>(), new Dictionary<int, Image>(), "等待");
        Check(image.Width == 48, "Replacing a roster does not dispose borrowed artwork.");
        view.Dispose();
        view.UpdateRoster(roster, new Dictionary<string, PlayerChampionTier>(), icons, "过期结果");
        Check(image.Width == 48, "Closing the view preserves shared cache ownership and ignores late updates.");
    }
}
