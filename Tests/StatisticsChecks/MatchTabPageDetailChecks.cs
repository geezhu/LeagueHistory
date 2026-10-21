using System.Reflection;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The history page's inline match detail: opening a match beside the list, replacing the page while a detail is
/// open, returning to the profile, the gallery it can raise, and the small layout hooks. Every game is a
/// hand-written document, so no client and no network is involved.
/// </summary>
internal static class MatchTabPageDetailChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        InlineDetail();
        PageReplacement();
        PrepareFailures();
        GalleryAndReturn();
        LayoutHooks();
        FailingControlCleanup();
        Console.WriteLine(
            "PASS: the history page's inline detail, its page replacement and return, the mastery gallery, the "
                + "layout hooks and a cleanup that has to survive a control that will not dispose."
        );
    }

    private static void InlineDetail()
    {
        using var host = HiddenForm(new Size(1300, 800));
        var view = Page(host);
        var info = Info(1);
        var card = new MatchListPanel(info);
        view.flowLayoutPanelRight.Controls.Add(card);
        try
        {
            Invoke(view, "OpenInlineMatch", info);
            PumpUntil(() => Field<MatchDetailView?>(view, "_detailView") != null);
            Check(Field<MatchDetailView?>(view, "_detailView") != null, "Opening a card shows the match detail");
            Check(
                ReferenceEquals(Field<MatchListPanel?>(view, "_selectedCard"), card),
                "The card that was opened is the selected one"
            );
            Check(!Field<Control>(view, "_profileSurface").Visible, "The profile is hidden while a match is open");
            Check(Field<Control>(view, "_backToProfile").Visible, "Returning to the profile is offered");

            Invoke(view, "OpenInlineMatch", info);
            Drain();
            Check(
                ReferenceEquals(Field<MatchListPanel?>(view, "_selectedCard"), card),
                "Opening the card that is already open changes nothing"
            );

            Invoke(view, "OpenInlineMatch", Info(9));
            Drain();
            Check(
                Field<MatchDetailView?>(view, "_detailView") != null,
                "A match that is not in the list opens nothing"
            );
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void PageReplacement()
    {
        using var host = HiddenForm(new Size(1300, 800));
        var view = Page(host);
        var info = Info(1);
        view.flowLayoutPanelRight.Controls.Add(new MatchListPanel(info));
        try
        {
            Invoke(view, "OpenInlineMatch", info);
            PumpUntil(() => Field<MatchDetailView?>(view, "_detailView") != null);

            var old = new MatchListPanel(Info(7));
            Field<List<WeakReference>>(view, "_allParsedPanels").Add(new WeakReference(old));
            var fresh = new MatchListPanel(Info(2));
            Replace(view, new List<Panel> { fresh }, new[] { new WeakReference(old) });
            Check(old.IsDisposed, "Replacing the page disposes the cards it replaced");
            Check(view.flowLayoutPanelRight.Controls.Count == 1, "Replacing the page swaps in the new cards");
            PumpUntil(() => Detail(view)?.GameId == fresh.MatchInfo.GameId);
            Check(
                Detail(view)?.GameId == fresh.MatchInfo.GameId,
                "A replaced page opens the first match of the new page"
            );

            Replace(view, new List<Panel> { new Panel() }, System.Array.Empty<WeakReference>());
            PumpUntil(() => Field<MatchDetailView?>(view, "_detailView") == null);
            Check(Field<MatchDetailView?>(view, "_detailView") == null, "A page without a match closes the detail");
            Check(Field<Control>(view, "_profileSurface").Visible, "Closing the detail shows the profile again");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void PrepareFailures()
    {
        using var host = HiddenForm(new Size(1300, 800));
        var view = Page(host);
        var info = Info(1);
        view.flowLayoutPanelRight.Controls.Add(new MatchListPanel(info));
        try
        {
            Set(
                view,
                "_prepareInlineDetail",
                (Func<MatchDetailView, Task>)(
                    _ =>
                    {
                        Set(view, "_navigationClosing", true);
                        throw new OperationCanceledException("检查用取消");
                    }
                )
            );
            Invoke(view, "OpenInlineMatch", info);
            Drain();
            Check(
                Field<MatchDetailView?>(view, "_detailView") == null,
                "A detail that is discarded while closing shows"
            );
            Check(Field<MatchDetailView?>(view, "_pendingDetail") == null, "A discarded detail is not kept as pending");
            Set(view, "_navigationClosing", false);

            Set(
                view,
                "_prepareInlineDetail",
                (Func<MatchDetailView, Task>)(_ => throw new InvalidOperationException("检查用失败"))
            );
            Invoke(view, "OpenInlineMatch", info);
            Drain();
            Check(Field<MatchDetailView?>(view, "_detailView") == null, "A detail that cannot be prepared is dropped");

            Set(
                view,
                "_prepareInlineDetail",
                (Func<MatchDetailView, Task>)(
                    _ =>
                    {
                        Set(view, "_pendingDetail", null);
                        return Task.CompletedTask;
                    }
                )
            );
            Invoke(view, "OpenInlineMatch", info);
            Drain();
            Check(
                Field<MatchDetailView?>(view, "_detailView") == null,
                "A detail that is no longer pending is dropped"
            );

            Set(view, "_prepareInlineDetail", (Func<MatchDetailView, Task>)(_ => Task.CompletedTask));
            var surface = Field<Control>(view, "_contentSurface");
            Set(view, "_contentSurface", null);
            Invoke(view, "OpenInlineMatch", info);
            Drain();
            Set(view, "_contentSurface", surface);
            Check(Field<MatchDetailView?>(view, "_detailView") == null, "A detail that cannot be shown is reported");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void GalleryAndReturn()
    {
        using var host = HiddenForm(new Size(1300, 800));
        var view = Page(host);
        try
        {
            Invoke(view, "OpenMasteryGallery", new object?[] { System.Array.Empty<JObject>() });
            var gallery = Children(view).First(control => control.GetType().Name == "MasteryGalleryView");
            Check(gallery != null, "Asking for all champion mastery opens the gallery");
            var close = (Action?)gallery.GetType().GetField("CloseRequested", Any)?.GetValue(gallery);
            close?.Invoke();
            Check(gallery.IsDisposed, "Closing the gallery disposes it");

            Invoke(view, "OpenRequestedPlayer", "检查用玩家");
            Drain();
            Check(
                !Field<bool>(view, "_navigationClosing"),
                "A player asked for from another host leaves the page alone"
            );

            Set(view, "_navigationClosing", true);
            Invoke(view, "OpenMasteryGallery", new object?[] { System.Array.Empty<JObject>() });
            Invoke(view, "ReturnToProfile");
            Check(Field<MatchDetailView?>(view, "_detailView") == null, "A closing page returns to nothing");
            Set(view, "_navigationClosing", false);

            var surface = Field<Control>(view, "_historySurface");
            Set(view, "_historySurface", null);
            Invoke(view, "ReturnToProfile");
            Set(view, "_historySurface", surface);
            Check(surface != null, "Returning without a history surface is harmless");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void LayoutHooks()
    {
        using var host = HiddenForm(new Size(1300, 800));
        var view = Page(host);
        try
        {
            var solo = Children(view).OfType<Label>().First(label => label.Name == "SoloRankHistory");
            solo.Text = "检查用最高段位";
            Check(solo.Text == "检查用最高段位", "The rank history line accepts its text");

            Set(view, "_navigationClosing", true);
            view.flowLayoutPanelRight.Controls.Add(new MatchListPanel(Info(3)));
            Invoke(view, "ResizeMatchCards");
            Set(view, "_navigationClosing", false);
            Check(!Field<bool>(view, "_navigationClosing"), "A closing page skips its list sizing");

            var icon = Children(view).FirstOrDefault(control => control.GetType().Name == "IdentityIcon");
            Check(icon != null, "The identity panel offers its copy button");
            Field<Control>(view, "linkGameName").Text = string.Empty;
            Click(icon!);
            Check(Field<Control>(view, "linkGameName").Text.Length == 0, "Copying an empty name copies nothing");

            // The filter is a combo box now; the button that opened a dropdown went away when the page was restyled.
            // What still has to hold is that the queue options are there and that picking one sticks.
            var filter = Field<ComboBox>(view, "comboFilter");
            Check(filter.Items.Count >= 7, "The history filter offers a queue for each mode");
            Check(filter.SelectedIndex == 0, "The history filter starts on the unfiltered entry");
            filter.SelectedIndex = 3;
            Check(filter.SelectedIndex == 3, "The history filter keeps the queue that was picked");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    /// <summary>A control that refuses to be disposed once, which the page's cleanup has to survive.</summary>
    private static void FailingControlCleanup()
    {
        using var host = HiddenForm(new Size(900, 600));
        var view = Page(host);
        var stubborn = new StubbornPanel();
        view.flowLayoutPanelRight.Controls.Add(stubborn);
        var card = new StubbornCard(Info(4));
        Field<List<WeakReference>>(view, "_allParsedPanels").Add(new WeakReference(card));
        view.Dispose();
        Check(
            stubborn.DisposeAttempts >= 2,
            $"Cleanup survives a control that will not dispose: {stubborn.DisposeAttempts}"
        );
        Check(Field<bool>(view, "_disposed"), "The page is marked as disposed after the stubborn control");
        host.Close();
    }

    private static MatchInfo? Detail(Control view) =>
        Field<MatchDetailView?>(view, "_detailView") is { } detail ? Field<MatchInfo>(detail, "_match") : null;

    private static MatchTabPageContent Page(Control host)
    {
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();
        view.LoadDataRequested += (_, _, _, _) => Task.FromResult(new JArray());
        view.ParsePanelRequested += (_, _, index) => Task.FromResult<Panel>(new MatchListPanel(Info(index)));
        return view;
    }

    private static void Replace(Control view, List<Panel> panels, WeakReference[] old) =>
        InvokeTyped(view, "ReplacePanels", new[] { typeof(List<Panel>), typeof(WeakReference[]) }, panels, old);

    private static void Click(Control button) =>
        typeof(Control).GetMethod("OnClick", Any)!.Invoke(button, new object[] { EventArgs.Empty });

    private static void Drain()
    {
        for (int i = 0; i < 40; i++)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static MatchInfo Info(int index) =>
        new()
        {
            GameId = 7412345671L + index,
            ResultText = "胜利",
            Mode = "极地大乱斗",
            QueueId = "q_450",
            GameTime = "2026-09-08 01:54",
            DurationText = "22分5秒",
            Kills = 12,
            Deaths = 2,
            Assists = 18,
            GoldText = "12.3K |1",
            DamageText = "20K |1",
            KPPercentage = "75% |1",
            DamageTakenText = "15K |1",
            ChampionName = "拉克丝",
            HeroIcon = new Bitmap(8, 8),
            Items = [],
            PrimaryRunes = [],
            SecondaryRunes = [],
            StatRunes = [],
            AllParticipants = [Participant(100, 7, "甲", true, index), Participant(200, 8, "乙", false, index)],
            RawGameData = Game(index),
        };

    private static JObject Game(int index) =>
        new()
        {
            ["gameId"] = 7412345671L + index,
            ["gameDuration"] = 1325,
            ["gameMode"] = "CLASSIC",
            ["queueId"] = 450,
            ["gameStartTimestamp"] = 1767225600000L,
            ["participants"] = new JArray(
                Participant(100, 7, "甲", true, index),
                Participant(200, 8, "乙", false, index)
            ),
            ["teams"] = new JArray(
                new JObject
                {
                    ["teamId"] = 100,
                    ["win"] = true,
                    ["bans"] = new JArray(new JObject { ["championId"] = 1 }),
                },
                new JObject { ["teamId"] = 200, ["win"] = false }
            ),
        };

    private static JObject Participant(int teamId, long summonerId, string name, bool self, int index) =>
        new()
        {
            ["participantId"] = teamId == 100 ? 1 : 6,
            ["teamId"] = teamId,
            ["summonerId"] = summonerId,
            ["puuid"] = self ? "self-puuid-0001" : $"other-puuid-000{summonerId}",
            ["riotIdGameName"] = name,
            ["riotIdTagline"] = "0001",
            ["championId"] = 1,
            ["championName"] = "拉克丝",
            ["kills"] = self ? 12 : 3,
            ["deaths"] = self ? 2 : 9,
            ["assists"] = self ? 18 : 4,
            ["goldEarned"] = self ? 12345 : 8000,
            ["totalDamageDealtToChampions"] = self ? 20000 : 9000,
            ["totalDamageTaken"] = self ? 15000 : 22000,
            ["totalMinionsKilled"] = self ? 35 : 12,
            ["neutralMinionsKilled"] = 0,
            ["win"] = self,
            ["spell1Id"] = 4,
            ["spell2Id"] = 14,
            ["challenges"] = new JObject { ["killParticipation"] = self ? 0.75 : 0.4 },
        };

    private sealed class StubbornPanel : Panel
    {
        public int DisposeAttempts { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeAttempts++;
            if (DisposeAttempts == 1)
            {
                throw new InvalidOperationException("检查用释放失败");
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>A parsed card whose first release throws, so the page has to give up on its panel list.</summary>
    private sealed class StubbornCard(MatchInfo info) : MatchListPanel(info)
    {
        private bool _thrown;

        protected override void Dispose(bool disposing)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("检查用释放失败");
            }
            base.Dispose(disposing);
        }
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, Any) ?? throw new InvalidOperationException($"Missing field {name}.");

    private static T Field<T>(object target, string name) => (T)Field(target, name).GetValue(target)!;

    private static void Set(object target, string name, object? value) => Field(target, name).SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Any)!.Invoke(target, args);

    private static object? InvokeTyped(object target, string method, Type[] types, params object?[] args) =>
        target.GetType().GetMethod(method, Any, null, types, null)!.Invoke(target, args);
}
