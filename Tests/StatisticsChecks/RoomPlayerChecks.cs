using System.Collections.Concurrent;
using System.Reflection;
using League.Clients;
using League.Controls;
using League.Managers;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class RoomPlayerChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static JObject Player(int id, int champion = 0) =>
        new()
        {
            ["summonerId"] = id,
            ["puuid"] = "test-player-" + id,
            ["championId"] = champion,
            ["team"] = 2,
        };

    private static PlayerMatchInfo Info(int id) =>
        new()
        {
            Player = new PlayerInfo
            {
                SummonerId = id,
                Puuid = "test-player-" + id,
                GameName = "玩家 " + id,
                SoloRank = "白银 II",
                FlexRank = "未知",
                IsPublic = "公开",
                NameColor = Color.Black,
            },
            MatchItems = new() { new ListViewItem(new[] { "", "大乱斗", "3/2/5", "2026-09-17" }) },
        };

    private static async Task Eventually(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        Check(condition(), message);
    }

    public static void Run()
    {
        Task.Run(AsyncChecks).GetAwaiter().GetResult();
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            RenderChecks();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        Console.WriteLine(
            "PASS: independent player loading, cancellation, roster reconciliation, retries, bounded logs, HTTP cancellation and WinForms cards."
        );
    }

    private static async Task AsyncChecks()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "room-player-checks");
        Directory.CreateDirectory(directory);
        var log = new RoomLoadDiagnostics(Path.Combine(directory, "diagnostic.log"));
        var gates = new ConcurrentDictionary<int, TaskCompletionSource<PlayerMatchInfo>>();
        var calls = new ConcurrentDictionary<int, int>();
        var tokens = new ConcurrentDictionary<int, CancellationToken>();
        RoomPlayerState[] latest = Array.Empty<RoomPlayerState>();
        using var loader = new RoomPlayerLoader(
            (p, token) =>
            {
                int id = p.Value<int>("summonerId");
                calls.AddOrUpdate(id, 1, (_, count) => count + 1);
                tokens[id] = token;
                return gates.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            },
            action =>
            {
                action();
                return Task.CompletedTask;
            },
            states => Volatile.Write(ref latest, states.ToArray()),
            log
        );
        long generation = loader.Reset();
        await loader.UpdateTeamAsync(new JArray(Player(1), Player(2), Player(3)), true, generation);
        await Eventually(() => calls.Count == 3, "Three player requests must start without awaiting each other");
        gates[2].SetResult(Info(2));
        gates[3].SetResult(Info(3));
        await Eventually(
            () => Volatile.Read(ref latest).Count(s => s.Info != null) == 2,
            "Ready players must render while another player remains blocked"
        );
        Check(
            latest.Single(s => s.Player.Value<int>("summonerId") == 1).Info == null,
            "Slow player remains independently pending"
        );

        await loader.UpdateTeamAsync(new JArray(Player(3, 99), Player(2), Player(1)), true, generation);
        Check(
            latest.Length == 3 && latest[0].Player.Value<int>("championId") == 99 && latest[0].Info != null,
            "Reordering and champion changes preserve ready data"
        );
        Check(calls.Values.All(c => c == 1), "Champion-only updates must not restart history requests");
        await loader.UpdateTeamAsync(new JArray(Player(2), Player(3)), true, generation);
        Check(tokens[1].IsCancellationRequested, "Leaving player cancels its request");
        gates[1].SetResult(Info(1));
        await Task.Delay(25);
        Check(
            latest.Length == 2 && latest.All(s => s.Player.Value<int>("summonerId") != 1),
            "Late result must not resurrect a removed player"
        );

        await loader.UpdateTeamAsync(new JArray(Player(4)), false, generation);
        await Eventually(() => calls.ContainsKey(4), "Enemy request starts independently");
        long next = loader.Reset();
        await loader.UpdateTeamAsync(new JArray(Player(5)), true, next);
        await Eventually(() => calls.ContainsKey(5), "New match must start without waiting for the old match");
        gates[4].SetResult(Info(4));
        await loader.UpdateTeamAsync(new JArray(Player(1)), false, generation);
        gates[5].SetResult(Info(5));
        await Eventually(
            () => latest.Length == 1 && latest[0].Info?.Player.SummonerId == 5,
            "Old generation snapshots and responses must be ignored"
        );

        var duplicate = new JArray(Player(1), Player(1), Player(2), Player(3), Player(4), Player(5), Player(6));
        Check(
            RoomPlayerLoader.Normalize(duplicate).Count == 5,
            "Roster deduplicates known IDs and caps the fixed team layout"
        );
        var hidden = new JArray(
            Enumerable
                .Range(0, 5)
                .Select(i => new JObject
                {
                    ["summonerId"] = 0,
                    ["puuid"] = "OBFUSCATED_" + i,
                    ["cellId"] = i,
                })
        );
        Check(RoomPlayerLoader.Normalize(hidden).Count == 5, "Hidden players with zero IDs remain five distinct slots");
        var selected = RoomPlayerLoader.WithChampionSelections(
            new JArray(Player(1)),
            new JArray(Player(1, 99), Player(2, 25))
        );
        Check(
            selected.Count == 1 && selected[0].Value<int>("championId") == 99,
            "Selection data enriches known members without inventing team membership"
        );
        await HiddenRosterChecks(log);

        int modeCalls = 0;
        RoomPlayerState[] modeStates = Array.Empty<RoomPlayerState>();
        using var modes = new RoomPlayerLoader(
            (p, t) =>
            {
                Interlocked.Increment(ref modeCalls);
                return Task.FromResult(Info(10));
            },
            a =>
            {
                a();
                return Task.CompletedTask;
            },
            s => Volatile.Write(ref modeStates, s.ToArray()),
            log
        );
        long modeGeneration = modes.Reset();
        var modePlayer = Player(10);
        modePlayer["__roomQueue"] = "420";
        await modes.UpdateTeamAsync(new JArray(modePlayer), true, modeGeneration);
        await Eventually(() => modeStates[0].Info != null, "Initial mode loads");
        modePlayer["__roomQueue"] = "450";
        await modes.UpdateTeamAsync(new JArray(modePlayer), true, modeGeneration);
        await Eventually(
            () => modeCalls == 2 && modeStates[0].Info != null,
            "Changed queue reloads histories rather than keeping the previous mode cache"
        );

        RoomPlayerState[] queuedStates = Array.Empty<RoomPlayerState>();
        using var queued = new RoomPlayerLoader(
            async (p, t) =>
            {
                await Task.Delay(Timeout.Infinite, t);
                return Info(1);
            },
            a =>
            {
                a();
                return Task.CompletedTask;
            },
            s => Volatile.Write(ref queuedStates, s.ToArray()),
            log,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(5)
        );
        await queued.UpdateTeamAsync(new JArray(Enumerable.Range(1, 5).Select(id => Player(id))), true, queued.Reset());
        await Eventually(
            () => queuedStates.Length == 5 && queuedStates.All(s => s.CanRetry),
            "Queued players also have a bounded timeout when all active requests stall"
        );

        int attempts = 0;
        RoomPlayerState[] retryStates = Array.Empty<RoomPlayerState>();
        using var retry = new RoomPlayerLoader(
            async (p, token) =>
            {
                if (Interlocked.Increment(ref attempts) <= 2)
                {
                    await Task.Delay(Timeout.Infinite, token);
                }

                return Info(8);
            },
            action =>
            {
                action();
                return Task.CompletedTask;
            },
            states => Volatile.Write(ref retryStates, states.ToArray()),
            log,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(5)
        );
        long retryGeneration = retry.Reset();
        await retry.UpdateTeamAsync(new JArray(Player(8)), true, retryGeneration);
        await Eventually(
            () => retryStates.FirstOrDefault()?.CanRetry == true,
            "Timeout must terminate loading and expose manual retry"
        );
        Check(attempts == 2 && retryStates[0].Status.Contains("超时"), "Automatic retries are bounded");
        await retry.RetryAsync(retryStates[0].Key, retryGeneration);
        await Eventually(() => retryStates[0].Info != null, "Manual retry recovers only the failed player");
        Check(attempts == 3 && !retryStates[0].CanRetry, "Successful retry clears failure state");
        retry.Reset();
        retry.Reset(); // Completed entries must remain safe to cancel/dispose.

        // A callback already queued for an old generation must also be rejected at delivery time.
        var callbacks = new ConcurrentQueue<Action>();
        int delivered = 0;
        using var delayedUi = new RoomPlayerLoader(
            (p, t) => Task.FromResult(Info(9)),
            action =>
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                callbacks.Enqueue(() =>
                {
                    action();
                    completion.SetResult();
                });
                return completion.Task;
            },
            _ => delivered++,
            log
        );
        var updating = delayedUi.UpdateTeamAsync(new JArray(Player(9)), true, delayedUi.Reset());
        delayedUi.Reset();
        Check(callbacks.TryDequeue(out var callback), "UI delivery was queued");
        callback!();
        await updating;
        Check(delivered == 0, "Generation must be checked inside UI callback, not just before posting it");

        await HttpCancellationChecks();
        await log.FlushAsync();
        string text = await File.ReadAllTextAsync(log.LogPath);
        Check(
            text.Contains("stage=timeout")
                && text.Contains("stage=complete")
                && !text.Contains("test-player-")
                && !text.Contains("玩家"),
            "Logs persist stages without raw player identities"
        );
        await File.WriteAllTextAsync(log.LogPath, new string('x', 530000));
        log.Report(1, "rotation");
        await log.FlushAsync();
        Check(
            File.Exists(log.LogPath + ".previous") && new FileInfo(log.LogPath).Length < 1000,
            "Diagnostic log rotates at its size limit"
        );
    }

    private static async Task HttpCancellationChecks()
    {
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose();
        using var http = new HttpClient(new WaitingHandler()) { BaseAddress = new Uri("https://127.0.0.1/") };
        typeof(LcuClient)
            .GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, http);
        var flow = new GameflowService(client);
        var summoner = new SummonerService(client);
        var ranked = new RankedService(client);
        foreach (
            var request in new Func<CancellationToken, Task>[]
            {
                t => flow.GetChampSelectSessionAsync(t),
                t => flow.GetGameSessionAsync(t),
                t => summoner.GetCurrentSummonerAsync(t),
                t => summoner.GetSummonerByIdAsync("room-cancel-check", t),
                t => ranked.GetCurrentRankedStatsAsync("test-player", t),
            }
        )
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            try
            {
                await request(cancellation.Token);
                throw new Exception("LCU cancellation was swallowed");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        var sgp = new SgpSession();
        typeof(SgpSession).GetField("_lcuClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sgp, http);
        using var refreshCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        try
        {
            await sgp.LoadEntitlementsTokenAsync(refreshCancellation.Token);
            throw new Exception("SGP token refresh ignored cancellation");
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested) { }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        }
    }

    private static JObject HiddenRosterFixture() =>
        new()
        {
            ["queue"] = new JObject { ["id"] = 2400, ["numPlayersPerTeam"] = 5 },
            ["teamOne"] = new JArray(Enumerable.Range(1, 5).Select(id => Player(id, 100 + id))),
            ["teamTwo"] = new JArray(new[] { 6, 8, 9 }.Select(id => Player(id, 100 + id))),
            ["playerChampionSelections"] = new JArray(
                Enumerable
                    .Range(1, 10)
                    .Select(id => new JObject { ["puuid"] = "test-player-" + id, ["championId"] = 100 + id })
            ),
        };

    private static PlayerMatchInfo HiddenInfo(int hero) =>
        new()
        {
            Player = new PlayerInfo
            {
                GameName = "隐藏玩家",
                ChampionId = hero,
                SoloRank = "隐藏",
                FlexRank = "隐藏",
                IsPublic = "隐藏",
                NameColor = Color.Gray,
            },
        };

    private static async Task HiddenRosterChecks(RoomLoadDiagnostics log)
    {
        var data = HiddenRosterFixture();
        var teams = RoomPlayerLoader.ResolveGameTeams(data);
        Check(
            teams.One.Count == 5 && teams.Two.Count == 5 && ((JArray)data["teamTwo"]!).Count == 3,
            "Five known allies plus ten selections restore two missing opponents without mutating LCU input"
        );
        var added = teams.Two.Where(p => p.Value<bool?>("__roomSelectionOnly") == true).ToArray();
        Check(
            added.Select(p => p.Value<int>("championId")).SequenceEqual(new[] { 107, 110 })
                && added.All(p =>
                    p.Value<long>("summonerId") == 0 && p.Value<string>("nameVisibilityType") == "HIDDEN"
                ),
            "Missing profiles retain selected heroes but cannot request account histories"
        );

        var reverse = HiddenRosterFixture();
        reverse["teamOne"] = data["teamTwo"]!.DeepClone();
        reverse["teamTwo"] = data["teamOne"]!.DeepClone();
        Check(RoomPlayerLoader.ResolveGameTeams(reverse).One.Count == 5, "Completion works for either team color");
        var partial = HiddenRosterFixture();
        ((JArray)partial["teamOne"]!).RemoveAt(4);
        Check(
            RoomPlayerLoader.ResolveGameTeams(partial).Two.Count == 3,
            "Two incomplete teams must not assign unknown selections by vacant slots"
        );
        var custom = HiddenRosterFixture();
        custom["queue"]!["numPlayersPerTeam"] = 2;
        Check(
            RoomPlayerLoader.ResolveGameTeams(custom).Two.Count == 3,
            "Non-five-player modes cannot use the complement rule"
        );
        var missing = HiddenRosterFixture();
        ((JArray)missing["playerChampionSelections"]!).RemoveAt(9);
        Check(
            RoomPlayerLoader.ResolveGameTeams(missing).Two.Count == 3,
            "Incomplete selections cannot prove team membership"
        );
        var duplicates = HiddenRosterFixture();
        duplicates["playerChampionSelections"]![9]!["puuid"] = "test-player-1";
        Check(
            RoomPlayerLoader.ResolveGameTeams(duplicates).Two.Count == 3,
            "Duplicate selections must not be counted as ten players"
        );
        var conflict = HiddenRosterFixture();
        conflict["teamTwo"]![0]!["puuid"] = "test-player-1";
        Check(
            RoomPlayerLoader.ResolveGameTeams(conflict).Two.Count == 3,
            "Conflicting team identities block completion"
        );
        var stale = HiddenRosterFixture();
        stale["teamOne"]![0]!["puuid"] = "old-player";
        Check(
            RoomPlayerLoader.ResolveGameTeams(stale).Two.Count == 3,
            "Selection identities must agree with every known member"
        );
        var zeros = new JArray(
            Enumerable
                .Range(0, 5)
                .Select(i => new JObject
                {
                    ["summonerId"] = 0,
                    ["puuid"] = "00000000-0000-0000-0000-000000000000",
                    ["cellId"] = i,
                })
        );
        Check(
            RoomPlayerLoader.Normalize(zeros).Count == 5,
            "Zero UUID is a hidden placeholder, not a shared public identity"
        );

        RoomPlayerState[] states = [];
        int calls = 0;
        using var loader = new RoomPlayerLoader(
            (p, t) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(
                    p.Value<string>("nameVisibilityType") == "HIDDEN"
                        ? HiddenInfo(p.Value<int>("championId"))
                        : Info(p.Value<int>("summonerId"))
                );
            },
            a =>
            {
                a();
                return Task.CompletedTask;
            },
            s => Volatile.Write(ref states, s.ToArray()),
            log
        );
        long generation = loader.Reset();
        await loader.UpdateTeamAsync(teams.Two, false, generation);
        await Eventually(
            () => states.Length == 5 && states.All(s => s.Info != null),
            "Supplemented opponents become real hidden-player cards"
        );
        Check(
            states.Count(s => s.Info?.Player.GameName == "隐藏玩家") == 2 && states.All(s => !s.CanRetry),
            "Hidden profiles are terminal states, not failures"
        );
        await loader.UpdateTeamAsync(RoomPlayerLoader.ResolveGameTeams(data).Two, false, generation);
        Check(calls == 5, "Repeated incomplete snapshots reuse hidden cards without history reloads");
        data["teamTwo"] = new JArray(Enumerable.Range(6, 5).Select(id => Player(id, 100 + id)));
        await loader.UpdateTeamAsync(RoomPlayerLoader.ResolveGameTeams(data).Two, false, generation);
        await Eventually(
            () => states.Length == 5 && states.All(s => s.Info?.Player.SummonerId > 0),
            "Later explicit identities replace hidden profiles in place"
        );
        Check(
            calls == 7 && states.Select(s => s.Key).Distinct().Count() == 5,
            "Only the two newly disclosed profiles reload, without duplicate cards"
        );
    }

    private static void RenderChecks()
    {
        using var form = new QuietForm
        {
            ClientSize = new Size(1400, 800),
            Location = new Point(-16000, -16000),
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
        };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill };
        form.Controls.Add(table);
        var ui = new PlayerCardUIManager(form, table);
        form.Show();
        Application.DoEvents();
        RoomPlayerState State(int id, bool ally, int column, long revision, PlayerMatchInfo? info = null) =>
            new(
                1,
                (ally ? "ally/" : "enemy/") + id,
                ally,
                column,
                Player(id),
                info,
                info == null ? "正在读取战绩…" : "",
                false,
                revision
            );
        var states = Enumerable
            .Range(1, 5)
            .Select((id, col) => State(id, true, col, 1, Info(id)))
            .Concat(Enumerable.Range(6, 5).Select((id, col) => State(id, false, col, 1, Info(id))))
            .ToArray();
        ui.Render(states);
        PlayerCardControl Card(int col, int row) =>
            table.GetControlFromPosition(col, row)!.Controls.OfType<PlayerCardControl>().Single();
        var original = Card(0, 0);
        var originalEnemy = Card(0, 1);
        Check(table.Controls.Count == 10 && !original.IsLoading, "Ten completed cards display without loading state");
        states[0] = states[0] with { Column = 1, Revision = 2, Player = Player(1, 99) };
        states[1] = states[1] with { Column = 0, Revision = 2 };
        ui.Render(states);
        Check(
            table.Controls.Count == 10
                && ReferenceEquals(Card(1, 0), original)
                && ReferenceEquals(Card(0, 1), originalEnemy),
            "Swapping ally slots retains card objects and leaves enemies in place"
        );
        Check(
            original.CurrentChampionId == 99 && original.ListViewControl.Items.Count == 1,
            "Hero changes retain loaded history"
        );
        var shared = Info(20);
        states[0] = states[0] with { Info = shared, Revision = 3 };
        states[1] = states[1] with { Info = shared, Revision = 3 };
        ui.Render(states);
        Check(
            Card(0, 0).ListViewControl.Items.Count == 1
                && Card(1, 0).ListViewControl.Items.Count == 1
                && shared.MatchItems[0].ListView == null,
            "Cached ListViewItems must be cloned for each card"
        );
        states[2] = states[2] with { Info = null, CanRetry = true, Status = "读取超时", Revision = 4 };
        ui.Render(states);
        Application.DoEvents();
        Check(
            !Card(2, 0).IsLoading && Descendants(Card(2, 0)).OfType<Button>().Any(b => b.Text == "重试" && b.Visible),
            "Failed card visibly exits loading and offers retry"
        );
        var failedCard = Card(2, 0);
        var retryButton = Descendants(failedCard).OfType<Button>().Single(b => b.Text == "重试");
        var historyHost = failedCard.ListViewControl.Parent!;
        Check(
            historyHost.Bottom <= retryButton.Parent!.Top && retryButton.Parent.Bottom <= failedCard.ClientSize.Height,
            "Retry status must reserve space rather than be covered by the native history list"
        );
        string image = Path.Combine(AppContext.BaseDirectory, "room-player-checks", "cards.png");
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(image);
        }
        ui.Render(states.Skip(1).ToArray());
        Check(table.Controls.Count == 9 && original.IsDisposed, "Departed player's complete card is disposed");
        var hiddenTeams = RoomPlayerLoader.ResolveGameTeams(HiddenRosterFixture());
        var hiddenStates = hiddenTeams
            .One.Select(
                (p, i) =>
                    new RoomPlayerState(
                        2,
                        "ally/" + RoomPlayerLoader.Identity(p, i),
                        true,
                        i,
                        (JObject)p,
                        Info(p.Value<int>("summonerId")),
                        "",
                        false,
                        10
                    )
            )
            .Concat(
                hiddenTeams.Two.Select(
                    (p, i) =>
                        new RoomPlayerState(
                            2,
                            "enemy/" + RoomPlayerLoader.Identity(p, i),
                            false,
                            i,
                            (JObject)p,
                            p.Value<bool?>("__roomSelectionOnly") == true
                                ? HiddenInfo(p.Value<int>("championId"))
                                : Info(p.Value<int>("summonerId")),
                            "",
                            false,
                            10
                        )
                )
            )
            .ToArray();
        ui.Render(hiddenStates);
        Application.DoEvents();
        Check(
            table.Controls.Count == 10,
            "Incomplete LCU team still renders ten occupied slots when membership is proven"
        );
        foreach (int col in new[] { 3, 4 })
        {
            var hiddenCard = Card(col, 1);
            hiddenCard.BindChampionInfo(
                _ => Task.FromResult(new PlayerChampionTier("T2", "胜 52.0%", "test", 2400, "none")),
                (_, _) => { }
            );
            Check(
                !hiddenCard.IsLoading
                    && hiddenCard.lblPlayerName.Text == "隐藏玩家"
                    && hiddenCard.CurrentChampionId > 0
                    && hiddenCard.ListViewControl.Items.Count == 0
                    && Descendants(hiddenCard).OfType<Label>().Any(l => l.Text.Contains("客户端未提供玩家资料")),
                "Hidden cards keep hero and tier while explicitly marking unavailable player history"
            );
            Check(
                Descendants(hiddenCard).OfType<Label>().Any(l => l.Text == "T2 · 胜 52.0%"),
                "Hidden identities must not suppress champion tiers"
            );
        }
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Path.GetDirectoryName(image)!, "hidden-cards.png"));
        }
        ui.Render(Array.Empty<RoomPlayerState>());
        Check(table.Controls.Count == 0, "New session clears every card");
        form.Close();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
}
