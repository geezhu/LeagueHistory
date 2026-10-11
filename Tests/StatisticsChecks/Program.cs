using System.Net;
using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--history-navigation-checks"))
        {
            WinFormsCheckHost.Run(HistoryNavigationChecks.Run);
            return;
        }
        if (args.Contains("--profile-surface-checks"))
        {
            WinFormsCheckHost.Run(ProfileSurfaceChecks.Run);
            return;
        }
        if (args.Contains("--opgg-lifecycle-checks"))
        {
            WinFormsCheckHost.Run(OpggViewLifecycleChecks.Run);
            return;
        }
        if (args.Contains("--opgg-checks"))
        {
            Task.Run(OpggChecks.RunAsync).GetAwaiter().GetResult();
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            WinFormsCheckHost.Run(OpggChecks.Render);
            return;
        }
        if (args.Contains("--profile-presentation-checks"))
        {
            ProfilePresentationChecks.Run();
            return;
        }
        if (args.Contains("--composition-lifecycle-checks"))
        {
            CompositionViewLifecycleChecks.Run();
            return;
        }
        if (args.Contains("--arammeta-checks"))
        {
            Task.Run(AramMetaChecks.RunAsync).GetAwaiter().GetResult();
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            AramMetaChecks.Render();
            AramMetaViewLifecycleChecks.Run();
            return;
        }
        if (args.Contains("--selection-control-checks"))
        {
            SelectionControlChecks.Run();
            return;
        }
        if (args.Contains("--player-champion-checks"))
        {
            PlayerChampionChecks.Run();
            return;
        }
        if (args.Contains("--history-presentation-checks"))
        {
            HistoryPresentationChecks.Run();
            return;
        }
        if (args.Contains("--scoreboard-layout-checks"))
        {
            ScoreboardLayoutChecks.Run();
            return;
        }
        if (args.Contains("--overlay-lifecycle-checks"))
        {
            OverlayLifecycleChecks.Run();
            return;
        }
        if (args.Contains("--client-checks"))
        {
            Task.Run(ClientChecks.RunAsync).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--model-checks"))
        {
            Task.Run(ModelChecks.RunAsync).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--sensevoice-checks"))
        {
            Task.Run(SenseVoiceChecks.RunAsync).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 2 && args[0] == "--sensevoice-engine-check")
        {
            Task.Run(() => SenseVoiceChecks.EngineAsync(args[1])).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--gamebar-voice-checks"))
        {
            Task.Run(GameBarVoiceRecognitionChecks.RunAsync).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--assistant-voice-checks"))
        {
            AssistantVoiceChecks.Run();
            return;
        }
        if (args.Contains("--assistant-voice-engine-check"))
        {
            AssistantVoiceChecks.EngineCheck();
            return;
        }
        if (args.Contains("--assistant-checks"))
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            AssistantChecks.Run();
            MayhemAssistantChecks.Run();
            return;
        }
        if (args.Contains("--overlay-chat-checks"))
        {
            OverlayVoiceChatChecks.Run();
            OverlayTierChatChecks.Run();
            return;
        }
        if (args.Contains("--mastery-live-diagnostics"))
        {
            Task.Run(MasteryLiveDiagnostics.Run).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--roster-identity-checks"))
        {
            Task.Run(LoadingRosterIdentityChecks.Run).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--overlay-party-checks"))
        {
            OverlayPartyChecks.Run();
            return;
        }
        if (args.Contains("--live-champion-checks"))
        {
            LiveChampionChecks.Run();
            return;
        }
        if (args.Contains("--live-champion-diagnostics"))
        {
            Task.Run(LiveChampionChecks.RunLiveAsync).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--death-streak-checks"))
        {
            LiveDeathStreakChecks.Run();
            return;
        }
        if (args.Contains("--death-streak-live-diagnostics"))
        {
            Task.Run(LiveDeathStreakChecks.RunLiveAsync).GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--room-player-checks"))
        {
            RoomPlayerChecks.Run();
            return;
        }
        if (args.Contains("--config-checks"))
        {
            LeagueConfigChecks.Run();
            return;
        }
        if (args.Contains("--shutdown-checks"))
        {
            ShutdownSettingsChecks.Run();
            return;
        }
        if (args.Contains("--shutdown-ui-checks"))
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            ShutdownUiChecks.Run();
            return;
        }
        if (args.Contains("--shutdown-validate"))
        {
            ShutdownSettingsChecks.ValidateWindows();
            return;
        }
        if (args.Contains("--assistant-placement-checks"))
        {
            AssistantChecks.Placement();
            AssistantVisibilityChecks.Run();
            return;
        }
        if (args.Contains("--assistant-window-checks"))
        {
            AssistantVisibilityChecks.Run();
            AssistantWindowChecks.Run();
            return;
        }
        if (args.Length == 3 && args[0] == "--overlay-detail-fixture")
        {
            OverlayDetailPreview.Export(args[1], args[2]);
            return;
        }
        if (args.Length > 0 && args[0] == "--champion-analysis")
        {
            ChampionAnalysisChecks.Run(
                args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "ChampionAnalysisReports")
            );
            return;
        }
        ChampionAnalysisChecks.Run();
        AssistantVisibilityChecks.Run();
        ChampionTierChatChecks.Run();
        LiveChampionChecks.Run();
        OverlayPartyChecks.Run();
        LiveDeathStreakChecks.Run();
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if (args.Contains("--mastery-checks"))
        {
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            Task.Run(ChampionMasteryChecks.RunAsync).GetAwaiter().GetResult();
            Task.Run(LoadingRosterIdentityChecks.Run).GetAwaiter().GetResult();
            ChampionMasteryChecks.Render();
            SynchronizationContext.SetSynchronizationContext(context);
            GameBarBridgeChecks.Run();
            MayhemAssistantChecks.Run();
            SynchronizationContext.SetSynchronizationContext(context);
            RosterChecks.Run();
            SynchronizationContext.SetSynchronizationContext(context);
            SettingsChecks.Render();
            // Drain UI continuations from the disposed fixture windows before process exit.
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            return;
        }
        AssistantWindowChecks.Run();
        if (args.Contains("--loading-overlay-checks"))
        {
            GameBarBridgeChecks.Run();
            OverlayChampionDetailChecks.Run();
            OverlayTierChatChecks.Run();
            OverlayVoiceChatChecks.Run();
            LoadingOverlayChecks.Run();
            return;
        }
        if (args.Contains("--settings-checks"))
        {
            SettingsChecks.Render();
            return;
        }
        RoomPlayerChecks.Run();
        TierHotkeyChecks.Run();
        Task.Run(ChatSendDiagnosticsChecks.RunAsync).GetAwaiter().GetResult();
        LiveMatchChecks.Run();
        if (args.Contains("--live-match-checks"))
        {
            return;
        }

        Task.Run(ChampionCompositionChecks.RunAsync).GetAwaiter().GetResult();
        ChampionCompositionChecks.Render();
        if (args.Contains("--composition-checks"))
        {
            return;
        }

        Task.Run(OpggChecks.RunAsync).GetAwaiter().GetResult();
        OpggChecks.Render();
        Task.Run(AramMetaChecks.RunAsync).GetAwaiter().GetResult();
        AramMetaChecks.Render();
        SettingsChecks.Render();
        PlayerChampionChecks.Run();
        AssistantChecks.Run();
        MayhemAssistantChecks.Run();
        RosterChecks.Run();
        Task.Run(ItemSetChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AramBalanceChecks.RunAsync).GetAwaiter().GetResult();
        string historyFile = Path.Combine(
            Path.GetTempPath(),
            "league-search-" + Guid.NewGuid().ToString("N") + ".json"
        );
        try
        {
            var history = new League.Services.PlayerSearchHistory(historyFile);
            history.Add("玩家#123");
            history.Add("Other#456");
            history.Add(" 玩家#123 ");
            Check(
                history.Items.Count == 2 && history.Items[0] == "玩家#123",
                "Search history deduplicates and promotes"
            );
            Check(
                new League.Services.PlayerSearchHistory(historyFile).Items.SequenceEqual(history.Items),
                "Search history survives reload"
            );
            history.Add("invalid");
            Check(history.Items.Count == 2, "Invalid ID excluded");
            for (int i = 0; i < 25; i++)
            {
                history.Add($"Player{i}#123");
            }

            Check(history.Items.Count == 20 && history.Items[0] == "Player24#123", "Search history bounded");
            history.Remove("player24#123");
            Check(history.Items.Count == 19, "History removal ignores case");
            history.Clear();
            Check(new League.Services.PlayerSearchHistory(historyFile).Items.Count == 0, "History clear persists");
            File.WriteAllText(historyFile, "invalid json");
            Check(
                new League.Services.PlayerSearchHistory(historyFile).Items.Count == 0,
                "Damaged history does not prevent startup"
            );
        }
        finally
        {
            if (File.Exists(historyFile))
            {
                File.Delete(historyFile);
            }
        }
        Check(
            LcuProcessDiscovery.TryParse(
                "\"C:\\Program Files\\LeagueClientUx.exe\" \"--app-port=12345\" --remoting-auth-token=\"test-value\"",
                out var connectionPort,
                out var connectionToken
            )
                && connectionPort == "12345"
                && connectionToken == "test-value",
            "Quoted LCU arguments"
        );
        Check(
            !LcuProcessDiscovery.TryParse("client.exe --app-port=70000 --remoting-auth-token=test", out _, out _),
            "Reject invalid port"
        );
        Check(
            !LcuProcessDiscovery.TryParse("client.exe --app-port=12345", out _, out _),
            "Reject missing authentication"
        );
        Check(
            LcuProcessDiscovery.TryReadCommandLine(Environment.ProcessId, out var ownCommandLine, out _)
                && ownCommandLine.Length > 0,
            "Native command line query"
        );
        Check(MatchMap.GetName(null) == "未知地图", "Missing map must not be inferred from queue");
        Check(
            MatchMap.GetName(JObject.Parse("{\"json\":{\"info\":{\"mapId\":12}}}")) == "随机地图",
            "ARAM ID must not imply a specific bridge"
        );
        Check(MatchMap.GetName(new JObject { ["mapId"] = 11 }) == "召唤师峡谷", "Rift map name");
        foreach (
            var (mutator, name) in new[]
            {
                ("MapSkin_Map12_Bloom", "莲华栈桥"),
                ("MapSkin_HA_Bilgewater", "屠夫之桥"),
                ("MapSkin_HA_Crepe", "进步之桥"),
            }
        )
        {
            var mapData = new JObject
            {
                ["mapId"] = 12,
                ["gameModeMutators"] = new JArray("ARAM", mutator.ToLowerInvariant()),
            };
            Check(
                MatchMap.GetName(new JObject { ["json"] = new JObject { ["info"] = mapData } }) == name,
                "Map variants in nested SGP data"
            );
            mapData["mapId"] = 11;
            Check(MatchMap.GetName(mapData) == "召唤师峡谷", "Bridge overrides apply only to map 12");
        }
        Check(
            MatchMap.GetName(new JObject { ["mapId"] = 12, ["gameModeMutators"] = new JArray() }) == "嚎哭深渊",
            "Explicit base map without variant"
        );
        var metricSelection = new MatchMetricSelection();
        Check(metricSelection.Set(new[] { "a", "b", "c", "d", "e", "f" }, true), "Six chart metrics allowed");
        Check(
            !metricSelection.Set(new[] { "g", "h" }, true)
                && metricSelection.Count == 6
                && !metricSelection.Contains("g"),
            "Over-limit group is atomic"
        );
        Check(
            metricSelection.Set(new[] { "a", "b" }, false) && metricSelection.Set(new[] { "g", "h" }, true),
            "Chart slots reusable after deselection"
        );
        Check(
            metricSelection.Set(new[] { "g", "g" }, true) && metricSelection.Count == 6,
            "Duplicate selection does not consume slots"
        );
        Check(
            metricSelection.SetGroup(new[] { "x", "y" }, true)
                && metricSelection.Count == 2
                && !metricSelection.Contains("g"),
            "Full selection replaced by another group"
        );
        Check(
            metricSelection.SetGroup(new[] { "x", "y" }, false) && metricSelection.Count == 0,
            "Selected group can be deselected"
        );
        Check(
            MatchMetricCatalog.Number(new JObject(), "goldEarned") == null
                && MatchMetricCatalog.Number(new JObject { ["goldEarned"] = 0 }, "goldEarned") == 0,
            "Chart distinguishes zero and missing"
        );
        Check(
            MatchStatisticsView.Value(new JObject { ["goldEarned"] = 0 }, "goldEarned") == "0",
            "Statistics preserves zero"
        );
        Check(MatchStatisticsView.Value(new JObject(), "goldEarned") == "—", "Statistics preserves missing values");
        Check(
            MatchStatisticsView.Value(JObject.Parse("{\"stats\":{\"kills\":1,\"deaths\":0,\"assists\":2}}"), "kda")
                == "1/0/2",
            "Statistics supports nested stats"
        );
        Check(
            MatchStatisticsView.Value(new JObject { ["firstBloodKill"] = false }, "firstBloodKill") == "○",
            "Statistics preserves false"
        );
        var rankNames = RankedStats.FromJson(
            JObject.Parse(
                """
                {"queueMap":{"RANKED_SOLO_5x5":{"tier":"PLATINUM","division":"II","highestTier":"MASTER","previousSeasonEndTier":"DIAMOND","previousSeasonEndDivision":"II"}}}
                """
            )
        )["单双排"];
        Check(
            rankNames.FormattedTier == "华贵铂金 II"
                && rankNames.HighestTierDisplay == "超凡大师"
                && rankNames.PreviousSeasonDisplay == "璀璨钻石 II",
            "Full Chinese rank names in all profile fields"
        );
        var game = JObject.Parse(
            """
            {"gameId":1,"queueId":420,"participants":[{"participantId":1,"puuid":"self","championId":1,"stats":{"win":true,"kills":10,"deaths":0,"assists":5,"totalDamageDealtToChampions":20000,"goldEarned":12000,"pentaKills":0}}]}
            """
        );
        var remake = (JObject)game.DeepClone();
        remake["gameId"] = 2;
        remake["participants"]![0]!["stats"]!["gameEndedInEarlySurrender"] = true;
        Check(MatchOutcomeFilter.GetWin(remake, "self") == null, "Remakes excluded from win/loss filters");
        using (
            var remakeInfo = new MatchInfo
            {
                ResultText = "胜利",
                RawGameData = new JObject { ["json"] = new JObject { ["info"] = remake.DeepClone() } },
            }
        )
        {
            Check(
                remakeInfo.ResultText == "重开" && remakeInfo.ResultColor == MatchResult.RemakeColor,
                "Remake overrides win text and color"
            );
        }

        Check(MatchResult.IsRemake(new JObject { ["gameEndedInEarlySurrender"] = true }), "Root remake flag");
        Check(
            MatchResult.IsRemake(null, new[] { new JObject { ["gameEndedInEarlySurrender"] = true } }),
            "Flattened participant remake flag"
        );
        Check(
            !MatchResult.IsRemake(new JObject { ["gameDuration"] = 90, ["gameEndedInSurrender"] = true }),
            "Short matches and ordinary surrenders are not assumed remakes"
        );
        Check(
            MatchOutcomeFilter.GetWin(game, "self") == true && MatchOutcomeFilter.GetWin(game, "other") == null,
            "Outcome filtering must select the viewed player"
        );
        var lost = (JObject)game.DeepClone();
        lost["participants"]![0]!["stats"]!["win"] = false;
        Check(
            MatchOutcomeFilter.GetWin(new JObject { ["json"] = new JObject { ["info"] = lost } }, "self") == false,
            "SGP nested defeat filtering"
        );
        var noResult = (JObject)game.DeepClone();
        ((JObject)noResult["participants"]![0]!["stats"]!).Remove("win");
        Check(MatchOutcomeFilter.GetWin(noResult, "self") == null, "Missing result must not count as defeat");
        var source = new JArray(game, lost, game, lost, game);
        var pager = new OutcomeHistoryPager();
        var offsets = new List<int>();
        Task<JArray> FetchOutcomes(int offset, int count)
        {
            offsets.Add(offset);
            return Task.FromResult(new JArray(source.Skip(offset).Take(count)));
        }
        Check(
            pager.LoadPage("self", true, 1, 2, FetchOutcomes).GetAwaiter().GetResult().Count == 2
                && pager.Scanned == 3
                && !pager.Exhausted,
            "Stop scanning as soon as first result page is full"
        );
        int requests = offsets.Count;
        pager.LoadPage("self", true, 1, 2, FetchOutcomes).GetAwaiter().GetResult();
        Check(offsets.Count == requests, "Previously loaded pages must not fetch again");
        Check(
            pager.LoadPage("self", true, 2, 2, FetchOutcomes).GetAwaiter().GetResult().Count == 1
                && offsets[requests] == 3
                && pager.Exhausted,
            "Continue from cursor and preserve final partial page"
        );
        requests = offsets.Count;
        Check(
            pager.LoadPage("self", false, 1, 2, FetchOutcomes).GetAwaiter().GetResult().Count == 2
                && offsets.Count == requests,
            "Changing outcome reuses scanned records"
        );
        var wrong = (JObject)game.DeepClone();
        wrong["gameId"] = 3;
        wrong["participants"]![0]!["puuid"] = "other";
        var records = new JArray(game, game.DeepClone(), remake, wrong);
        var stats = CareerStatistics.Parse(records, "self");
        Check(stats.Games.Count == 1 && stats.Kda == 15 && stats.Wins == 1, "Deduplication/zero-death KDA");
        Check(stats.ExcludedRemakes == 1 && stats.Unrecognized == 1, "Remake/player identity filtering");
        var recentWithRemakes = CareerStatistics.Parse(records, "self", includeRemakes: true);
        Check(
            recentWithRemakes.ExcludedRemakes == 0 && recentWithRemakes.Games.Count == stats.Games.Count + 1,
            "Recent activity includes remakes while retaining deduplication"
        );
        var missing = (JObject)game.DeepClone();
        ((JObject)missing["participants"]![0]!["stats"]!).Remove("kills");
        Check(CareerStatistics.Parse(new JArray(missing), "self").Kda == null, "Missing kills must not become zero");
        Check(CareerStatistics.Records(new JObject { ["games"] = "invalid" }) == null, "Unknown record shape");
        var sgp = new JObject { ["json"] = new JObject { ["info"] = game.DeepClone() } };
        Check(CareerStatistics.Parse(new JArray(sgp), "self").Games.Count == 1, "SGP info wrapper");
        var players = new List<JObject>
        {
            new()
            {
                ["participantId"] = 1,
                ["teamId"] = 200,
                ["riotIdGameName"] = "红队玩家",
            },
            new()
            {
                ["participantId"] = 7,
                ["teamId"] = 100,
                ["riotIdGameName"] = "蓝队玩家",
            },
        };
        var timeline = JObject.Parse(
            """
            {"frames":[
              {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
              {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]},
              {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
              {"timestamp":180000,"participantFrames":{"1":{"totalGold":3200},"7":{"totalGold":3400}},"events":[]}
            ]}
            """
        );
        var parsed = MatchTimeline.Parse(timeline, players);
        Check(
            parsed.Frames[0].Blue == 600 && parsed.Frames[0].Red == 500,
            "Teams must use participant mapping, not ID ranges"
        );
        Check(parsed.Frames[2].Red == null, "Missing gold must leave a gap");
        Check(
            parsed.GoldDifference(parsed.Frames[0]) == 100 && parsed.GoldDifference(parsed.Frames[2]) == null,
            "Average gold difference and missing intervals"
        );
        Check(
            parsed.GoldDifference(new GoldFrame(0, 300, 500)) == -200 && parsed.Events[0].Team == 100,
            "Negative gold advantage and killer team mapping"
        );
        Check(
            parsed.Events[0].Actor == "蓝队玩家" && parsed.Events[0].X == 100 && parsed.Events[1].Actor == "环境",
            "Event identities/positions"
        );
        Check(MatchTimeline.Parse(new JArray(), players).Frames.Count == 0, "Unexpected timeline shape");
        var handler = new Handler(path =>
            path.Contains("game-timelines") ? new(HttpStatusCode.OK, timeline.ToString())
            : path.Contains("career-stats") ? new(HttpStatusCode.NotFound, "")
            : new(HttpStatusCode.OK, new JObject { ["games"] = new JObject { ["games"] = records } }.ToString())
        );
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new PlayerStatisticsService(() => client);
        Check(
            new PlayerStatisticsService(() => null).GetAsync("/", default).GetAwaiter().GetResult().Data == null,
            "Disconnected client"
        );
        using var forbiddenClient = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden, "")))
        {
            BaseAddress = client.BaseAddress,
        };
        Check(
            new PlayerStatisticsService(() => forbiddenClient).GetAsync("/", default).GetAwaiter().GetResult().Status
                == 403,
            "Privacy status"
        );
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            service.GetAsync("/", canceled.Token).GetAwaiter().GetResult();
            throw new Exception("Cancellation swallowed");
        }
        catch (OperationCanceledException) { }
        using var form = new Form
        {
            ClientSize = new Size(850, 650),
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            Opacity = 0,
        };
        var view = new TimelineView(
            new MatchInfo
            {
                GameId = 1,
                QueueId = "q_450",
                AllParticipants = players,
            },
            service
        )
        {
            Dock = DockStyle.Fill,
        };
        form.Controls.Add(view);
        form.Show();
        Application.DoEvents();
        Control[] Children(Control root) =>
            root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c))).ToArray();
        var events = Children(view).OfType<ListView>().Single();
        Check(events.Items.Count == 2, "Timeline event rendering");
        Children(view).OfType<ComboBox>().Single().SelectedIndex = 1;
        Check(events.Items.Count == 1, "Kill filter");
        Children(view).OfType<TrackBar>().Single().Value = 2;
        using (var bitmap = new Bitmap(view.Width, view.Height))
        {
            view.DrawToBitmap(bitmap, view.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "timeline-preview.png"));
        }
        view.Dispose();
        var career = new CareerStatisticsView(service) { Dock = DockStyle.Fill };
        form.Controls.Add(career);
        career.LoadPlayer("self");
        Application.DoEvents();
        Check(Children(career).OfType<ListView>().Single().Items.Count == 1, "Career fallback mode table");
        Check(
            Children(career).OfType<Label>().Any(l => l.Text.Contains("不代表完整生涯")),
            "Fallback scope disclosure"
        );
        using (var bitmap = new Bitmap(career.Width, career.Height))
        {
            career.DrawToBitmap(bitmap, career.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "career-preview.png"));
        }
        career.Dispose();
        int deniedCalls = 0;
        using var deniedClient = new HttpClient(
            new Handler(_ =>
            {
                deniedCalls++;
                return new(HttpStatusCode.Forbidden, "");
            })
        )
        {
            BaseAddress = client.BaseAddress,
        };
        var deniedView = new CareerStatisticsView(
            new PlayerStatisticsService(() => deniedClient),
            (_, _) => throw new Exception("403 must not fall back")
        );
        form.Controls.Add(deniedView);
        deniedView.LoadPlayer("self");
        Application.DoEvents();
        Check(deniedCalls == 1, "Denied career must not request history");
        deniedView.Dispose();
        var masteryData = JArray.Parse(
            """
            [{"championId":1,"championLevel":10,"championPoints":12000},{"championId":2,"championLevel":20,"championPoints":80000},{"championId":3,"championLevel":8,"championPoints":9000},{"championId":1,"championPoints":1000},{"championId":4}]
            """
        );
        Check(
            ProfileMasteryView
                .SelectTop(masteryData)
                .Select(p => (int)p["championId"]!)
                .SequenceEqual(new[] { 2, 1, 3 }),
            "Mastery ordering/deduplication/missing scores"
        );
        var sixMasteries = new JArray(
            Enumerable.Range(1, 6).Select(id => new JObject { ["championId"] = id, ["championPoints"] = id * 100 })
        );
        Check(ProfileMasteryView.SelectTop(sixMasteries).Length == 5, "Homepage previews five champions");
        Check(
            ProfileMasteryView
                .StandardChampions(JArray.Parse("[{\"id\":-1},{\"id\":1},{\"id\":1},{\"id\":2},{\"id\":60001}]"))
                .Length == 2,
            "Ownership roster excludes placeholder, duplicates and classic variants"
        );
        var masteryPaths = new List<string>();
        using var masteryClient = new HttpClient(
            new Handler(path =>
            {
                masteryPaths.Add(path);
                return path == "/lol-champion-mastery/v1/self/champion-mastery"
                    ? new(HttpStatusCode.OK, masteryData.ToString())
                    : new(HttpStatusCode.NotFound, "");
            })
        )
        {
            BaseAddress = client.BaseAddress,
        };
        using var icon = new Bitmap(32, 32);
        var masteryView = new ProfileMasteryView(
            new PlayerStatisticsService(() => masteryClient),
            id => Task.FromResult<(Image?, string, string)>((icon, "英雄 " + id, ""))
        )
        {
            Dock = DockStyle.Fill,
        };
        form.Controls.Add(masteryView);
        masteryView.LoadPlayer("self");
        Application.DoEvents();
        Check(Children(masteryView).OfType<PictureBox>().Count() == 3, "Three mastery champions rendered");
        Check(Children(masteryView).OfType<Label>().Any(l => l.Text.Contains("80,000")), "Mastery points displayed");
        Check(
            masteryView.OwnershipText.StartsWith("成就英雄 3 /"),
            "Unavailable inventory uses clearly labelled mastery count"
        );
        using (var largeName = new LinkLabel { Size = new Size(320, 36), Font = new Font("Segoe UI", 15f) })
        {
            League.UIHelpers.PlayerNamePresentation.Apply(largeName, "Player#123", false);
            var privacyBadge = largeName.Controls.OfType<Label>().Single();
            Check(
                privacyBadge.Font.SizeInPoints == largeName.Font.SizeInPoints
                    && largeName.Padding.Right >= largeName.Font.Height,
                "Profile privacy badge scales with player name"
            );
        }
        JObject[]? allMastery = null;
        masteryView.AllRequested += records => allMastery = records;
        Children(masteryView).OfType<Button>().Single(b => b.Text == "全部 ›").PerformClick();
        Check(allMastery?.Length == 3, "All mastery navigation reuses loaded records");
        var extendedMastery = (JArray)masteryData.DeepClone();
        extendedMastery.Add(JObject.Parse("{\"championId\":5,\"championPoints\":0,\"championLevel\":0}"));
        Check(
            ProfileMasteryView.SelectAll(extendedMastery).Length == 4
                && ProfileMasteryView.SelectTop(extendedMastery).Length == 3,
            "Gallery includes zero-point records beyond homepage preview"
        );
        var galleryType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.MasteryGalleryView")!;
        var posterType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.MasteryPoster")!;
        foreach (int height in new[] { 270, 280, 350 })
        {
            using var poster = (Control)Activator.CreateInstance(posterType, new object?[] { 4, null })!;
            poster.Size = new Size(160, height);
            poster.BackColor = Color.FromArgb(180, 180, 180);
            using var rendered = new Bitmap(160, height);
            poster.DrawToBitmap(rendered, new Rectangle(0, 0, 160, height));
            for (int y = height / 3 - 2; y <= height / 3 + 2; y++)
            {
                Check(
                    Math.Abs(rendered.GetPixel(80, y).R - rendered.GetPixel(80, y - 1).R) <= 3,
                    "Poster gradient starts without a dark seam"
                );
            }
        }
        var activityType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.RecentActivityView")!;
        League.Models.CareerGame OverviewGame(int id, int queue, int kills, int deaths, int assists) =>
            new(id, id, queue, 1, id % 2 == 0, kills, deaths, assists, null, null, null);
        var overviewGames = new[]
        {
            OverviewGame(1, 430, 4, 2, 4),
            OverviewGame(2, 420, 2, 0, 4),
            OverviewGame(3, 2400, 90, 1, 90),
        };
        var overviewStats = League.Models.RecentOverview.Build(overviewGames);
        Check(
            overviewStats.Kda == 21 && overviewStats.KdaGames == 2,
            "Overview aggregates rift KDA with multiplier three and excludes ARAM"
        );
        Check(
            League.Models.RecentOverview.Build(new[] { OverviewGame(1, 440, 2, 0, 4) }).Kda == 18,
            "Overview zero total deaths uses denominator one"
        );
        var aramKda = League.Models.RecentOverview.Build(
            new[] { OverviewGame(1, 450, 2, 1, 4), OverviewGame(2, 2400, 4, 2, 2) }
        );
        Check(
            aramKda.Kda == 12 && aramKda.UsesAramKda && aramKda.KdaGames == 2,
            "Overview falls back to both ARAM modes when rift sample is absent"
        );
        Check(!overviewStats.UsesAramKda, "Rift sample takes priority over ARAM");
        Check(
            League.Models.RecentOverview.Build(new[] { OverviewGame(1, 1700, 2, 1, 4) }).Kda == null,
            "Other modes do not enter KDA fallback"
        );
        Check(
            League.Models.RecentOverview.Build(new[] { OverviewGame(1, 430, 2, 1, 4) with { Kills = null } }).Kda
                == null,
            "Overview missing KDA fields are not treated as zero"
        );
        Check(
            overviewStats.Modes.Length == 4 && overviewStats.Modes.Any(m => m.Queue == 2400 && m.Games == 1),
            "Hextech ARAM replaces an empty mode without changing slot count"
        );
        var hexOnly = League.Models.RecentOverview.Build(new[] { OverviewGame(1, 2400, 1, 1, 1) });
        Check(
            hexOnly.Modes[1].Queue == 2400 && hexOnly.Modes[0].Queue == 430,
            "Hextech ARAM prefers replacing empty standard ARAM over other empty modes"
        );
        var allModes = League.Models.RecentOverview.Build(
            new[] { 430, 450, 420, 440, 2400 }.Select((queue, i) => OverviewGame(i + 1, queue, 1, 1, 1))
        );
        Check(
            allModes.Modes.Length == 4 && allModes.Modes.Any(m => m.Queue == 2400),
            "Hextech ARAM is visible even with all default modes played"
        );
        Check(
            League
                .Models.RecentOverview.Build(Enumerable.Range(1, 60).Select(i => OverviewGame(i, 430, 1, 1, 1)))
                .Sample == 50,
            "Overview bounds sample to fifty games"
        );
        var overviewType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.RecentOverviewView")!;
        using (var overviewControl = (Control)Activator.CreateInstance(overviewType)!)
        {
            overviewControl.Width = 800;
            overviewType.GetMethod("SetGames")!.Invoke(overviewControl, new object[] { overviewGames, "最近 3 场" });
            using var bitmap = new Bitmap(800, 170);
            overviewControl.DrawToBitmap(bitmap, new Rectangle(0, 0, 800, 170));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "recent-overview.png"));
        }
        var roleGames = new[]
        {
            new League.Models.CareerGame(1, 1, 450, 1, true, null, null, null, null, null, null),
            new League.Models.CareerGame(2, 2, 450, 2, true, null, null, null, null, null, null),
            new League.Models.CareerGame(3, 3, 450, 3, true, null, null, null, null, null, null),
        };
        object?[] roleArgs =
        {
            roleGames,
            JArray.Parse("[{\"id\":1,\"roles\":[\"mage\",\"support\"]},{\"id\":2,\"roles\":[\"mage\",\"mage\"]}]"),
            0,
        };
        var roleWeights = (double[])activityType.GetMethod("CountRoles")!.Invoke(null, roleArgs)!;
        Check(
            (int)roleArgs[2]! == 2 && roleWeights.Sum() == 2 && roleWeights[2] == 1.5 && roleWeights[5] == .5,
            "Recent roles split dual classes, deduplicate roles and exclude missing classifications"
        );
        using (var activity = (Control)Activator.CreateInstance(activityType)!)
        {
            activity.Width = 900;
            Check(activity.Height == 86, "Wide recent activity uses compact strip");
            activity.Width = 500;
            Check(activity.Height == 150, "Narrow recent activity wraps compact strip");
            using var preview = new Bitmap(500, 150);
            activity.DrawToBitmap(preview, new Rectangle(0, 0, 500, 150));
            preview.Save(Path.Combine(AppContext.BaseDirectory, "recent-activity.png"));
        }
        var backgroundType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.ProfileBackgroundPanel")!;
        var findSplash = backgroundType.GetMethod("FindSplash")!;
        var selectSkin = backgroundType.GetMethod("SelectSkin")!;
        Check(
            (int)selectSkin.Invoke(null, new object?[] { new JObject(), masteryData })! == 2000,
            "Missing profile background uses highest mastery champion base skin"
        );
        Check(
            (int)selectSkin.Invoke(null, new object?[] { JObject.Parse("{\"backgroundSkinId\":103015}"), masteryData })!
                == 103015,
            "Explicit client background takes priority over mastery"
        );
        Check(
            (int)selectSkin.Invoke(null, new object?[] { new JObject(), new JArray() })! == 0,
            "No profile or mastery keeps default background"
        );
        var skinFixture = JObject.Parse(
            "{\"skins\":[{\"id\":103000,\"splashPath\":\"/lol-game-data/assets/center.jpg\",\"uncenteredSplashPath\":\"/lol-game-data/assets/full.jpg\"}]}"
        );
        Check(
            (string?)findSplash.Invoke(null, new object[] { skinFixture, 103000 }) == "/lol-game-data/assets/full.jpg",
            "Profile background resolves exact skin artwork"
        );
        Check(
            findSplash.Invoke(null, new object[] { skinFixture, 103001 }) == null,
            "Missing background skin does not substitute another skin"
        );
        using (var background = (Control)Activator.CreateInstance(backgroundType)!)
        {
            background.Size = new Size(800, 200);
            var name = new LinkLabel
            {
                Text = "Player",
                LinkColor = Color.Blue,
                BackColor = Color.LightBlue,
            };
            background.Controls.Add(name);
            var setArtwork = backgroundType.GetMethod(
                "SetArtwork",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!;
            setArtwork.Invoke(background, new object[] { new Bitmap(100, 100) });
            Check(
                name.LinkColor == Color.White && name.BackColor == Color.Transparent,
                "Artwork uses readable transparent identity controls"
            );
            setArtwork.Invoke(background, new object?[] { null });
            Check(
                name.LinkColor == Color.Blue && name.BackColor == Color.LightBlue,
                "Missing background restores original profile colors"
            );
        }
        using var gallery = (Control)
            Activator.CreateInstance(
                galleryType,
                new object[] { ProfileMasteryView.SelectAll(extendedMastery), "— / —" }
            )!;
        gallery.Dock = DockStyle.Fill;
        form.Controls.Add(gallery);
        gallery.BringToFront();
        Application.DoEvents();
        Check(Children(gallery).OfType<PictureBox>().Count() == 4, "Gallery renders every deduplicated mastery record");
        using (var screenshot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            gallery.DrawToBitmap(screenshot, new Rectangle(Point.Empty, gallery.Size));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "mastery-gallery.png"));
        }
        Check(
            masteryPaths.Contains("/lol-champion-mastery/v1/self/champion-mastery")
                && !masteryPaths.Any(p => p.Contains("lol-collections")),
            "Mastery uses PUUID endpoint despite unavailable profile"
        );
        using (var playerCard = new PlayerCardControl { Size = new Size(360, 480) })
        {
            form.Controls.Add(playerCard);
            playerCard.BringToFront();
            var rows = Enumerable
                .Range(0, 5)
                .Select(i =>
                {
                    var row = new ListViewItem { ForeColor = i % 2 == 0 ? Color.Green : Color.Red };
                    row.SubItems.AddRange(new[] { "海克斯大乱斗", "22/10/24", "2026-09-08 01:54" });
                    return row;
                })
                .ToList();
            playerCard.SetPlayerInfo("玩家#1234", "华贵铂金 II", "璀璨钻石 II", null!, "公开", rows, Color.SteelBlue);
            playerCard.SetRecentStatistics(
                new PlayerMatchInfo
                {
                    RecentMatches = new()
                    {
                        new()
                        {
                            Kills = 2,
                            Deaths = 1,
                            Assists = 2,
                        },
                        new()
                        {
                            Kills = 4,
                            Deaths = 3,
                            Assists = 4,
                        },
                    },
                    WinHistory = new() { true, false },
                }
            );
            var avatar = playerCard.SummaryPanel.Controls.Cast<Control>().Single(c => c.AccessibleName == "召唤师头像");
            avatar.GetType().GetMethod("SetProfile")!.Invoke(avatar, new object?[] { null, 108 });
            Application.DoEvents();
            Check(
                playerCard.ListViewControl.GetItemRect(0).Height >= 46,
                "Redesigned player card has room for two-line match rows"
            );
            using var bitmap = new Bitmap(360, 480);
            playerCard.DrawToBitmap(bitmap, new Rectangle(0, 0, 360, 480));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "player-card.png"));
        }
        form.Close();
        using (var teamForm = new Form { ClientSize = new Size(900, 600) })
        using (
            var teamTable = new TableLayoutPanel
            {
                Size = new Size(900, 600),
                ColumnCount = 5,
                RowCount = 2,
            }
        )
        {
            for (int i = 0; i < 5; i++)
            {
                teamTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            }

            teamTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            teamTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            teamForm.Controls.Add(teamTable);
            typeof(PlayerCardControl)
                .Assembly.GetType("League.Controls.PlayerTeamsLayout")!
                .GetMethod("Attach")!
                .Invoke(null, new object[] { teamTable });
            var friendly = new PlayerCardControl { Dock = DockStyle.Fill };
            var opposing = new PlayerCardControl { Dock = DockStyle.Fill };
            friendly.lblSoloRank.Text = "未定级(0)";
            friendly.lblFlexRank.Text = "未定级";
            var friendlyHost = new Panel { Tag = true, Dock = DockStyle.Fill };
            friendlyHost.Controls.Add(friendly);
            var opposingHost = new Panel { Tag = false, Dock = DockStyle.Fill };
            opposingHost.Controls.Add(opposing);
            teamTable.Controls.Add(friendlyHost, 0, 0);
            teamTable.Controls.Add(opposingHost, 0, 1);
            teamForm.Show();
            Application.DoEvents();
            Check(
                friendly.SummaryPanel.Parent == friendly
                    && !friendly.lblSoloRank.Visible
                    && !friendly.lblFlexRank.Visible,
                "Grid cards keep summaries and hide unranked labels"
            );
            Check(
                opposingHost.Visible && friendlyHost.Visible && opposing.SummaryPanel.Parent == opposing,
                "Both teams remain visible together"
            );
            for (int i = 0; i < 12; i++)
            {
                teamForm.ClientSize = new Size(900 + i * 3, 600);
                Application.DoEvents();
            }
            Check(
                teamTable.ColumnCount == 5 && teamTable.RowCount == 2 && teamTable.Width >= 1100,
                "Sparse rooms retain ten stable grid slots"
            );
            teamTable.Controls.Remove(opposingHost);
            opposingHost.Dispose();
            Application.DoEvents();
            Check(friendlyHost.Visible, "Single player remains visible after another player leaves");
            var teamsLayout = typeof(PlayerCardControl).Assembly.GetType("League.Controls.PlayerTeamsLayout")!;
            var originalSurface = teamTable.Parent;
            teamForm.Controls.Clear(); // Connection status replaces the page contents.
            teamsLayout.GetMethod("Hide")!.Invoke(null, new object[] { teamTable });
            teamsLayout.GetMethod("Show")!.Invoke(null, new object[] { teamTable, teamForm });
            Application.DoEvents();
            Check(
                originalSurface?.Parent == teamForm && originalSurface.Visible && teamTable.Visible,
                "Entering room restores detached team surface after connection status clears the page"
            );
            Check(friendly.SummaryPanel.Parent == friendly, "Restored room retains player summary inside card");
            teamForm.ClientSize = new Size(1200, 800);
            for (int slot = 0; slot < 10; slot++)
            {
                var card = slot == 0 ? friendly : new PlayerCardControl { Dock = DockStyle.Fill };
                var matches = Enumerable
                    .Range(0, 20)
                    .Select(i =>
                    {
                        var item = new ListViewItem { ForeColor = i % 2 == 0 ? Color.Green : Color.Red };
                        item.SubItems.AddRange(new[] { "海克斯大乱斗", "12/5/18", "2026-09-08" });
                        return item;
                    })
                    .ToList();
                card.SetPlayerInfo(
                    $"玩家{slot + 1}#1234",
                    "华贵铂金 II",
                    "璀璨钻石 II",
                    null!,
                    "公开",
                    matches,
                    Color.SteelBlue
                );
                card.SetRecentStatistics(
                    new PlayerMatchInfo
                    {
                        RecentMatches = new()
                        {
                            new()
                            {
                                Kills = 12,
                                Deaths = 5,
                                Assists = 18,
                            },
                        },
                        WinHistory = new() { true },
                    }
                );
                if (slot > 0)
                {
                    var host = new Panel
                    {
                        Tag = slot < 5,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(5),
                    };
                    host.Controls.Add(card);
                    teamTable.Controls.Add(host, slot % 5, slot / 5);
                }
            }
            Application.DoEvents();
            Check(
                friendly.lblSoloRank.Visible && friendly.lblFlexRank.Visible,
                "Both ranked queues remain visible in card header"
            );
            friendly.ListViewControl.TopItem = friendly.ListViewControl.Items[10];
            Check(
                friendly.ListViewControl.TopItem.Index > 0
                    && teamTable
                        .Controls.Cast<Control>()
                        .Where(p => p != friendlyHost)
                        .SelectMany(p => p.Controls.OfType<PlayerCardControl>())
                        .All(c => c.ListViewControl.TopItem.Index == 0),
                "Card histories scroll independently"
            );
            using (var gridBitmap = new Bitmap(1200, 800))
            {
                teamForm.DrawToBitmap(gridBitmap, new Rectangle(0, 0, 1200, 800));
                gridBitmap.Save(Path.Combine(AppContext.BaseDirectory, "room-grid.png"));
            }
            teamForm.Close();
        }
        foreach (var queue in new[] { "400", "420", "430", "440", "450", "480", "490", "2400" })
        {
            Check(
                League.Parsers.PlayerMatchDataFetcher.GetQueueFilter(queue) == "q_" + queue,
                "Room history filters supported queue " + queue
            );
        }

        foreach (var queue in new[] { "", "0", "900", "1700", "700", "99999" })
        {
            Check(
                League.Parsers.PlayerMatchDataFetcher.GetQueueFilter(queue) == "",
                "Other room modes use global history " + queue
            );
        }

        var launcherFixtureRoot = Path.Combine(
            AppContext.BaseDirectory,
            "launcher-fixture-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(launcherFixtureRoot);
        try
        {
            string FixtureFile(string relative)
            {
                var path = Path.Combine(launcherFixtureRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fixture");
                return path;
            }
            var delta = FixtureFile(@"Delta Force\Launcher\startup_runner.exe");
            var deltaWeGame = FixtureFile(@"Delta Force\Launcher\WeGameLauncher\launcher.exe");
            Check(
                League.UIHelpers.LOLHelper.ResolveInstallation(delta) == null
                    && League.UIHelpers.LOLHelper.ResolveInstallation(deltaWeGame) == null,
                "Generic Delta Force launchers are not recognized as League"
            );
            var official = FixtureFile(@"League\Launcher\startup_runner.exe");
            var wegame = FixtureFile(@"League\WeGameLauncher\launcher.exe");
            FixtureFile(@"League\LeagueClient\LeagueClient.exe");
            var ux = FixtureFile(@"League\LeagueClient\LeagueClientUx.exe");
            var installation = League.UIHelpers.LOLHelper.ResolveInstallation(ux);
            Check(
                installation?.OfficialPath == official && installation.WeGamePath == wegame,
                "Verified League install resolves both launchers despite other games"
            );
            var launcherPanelType = typeof(PlayerCardControl).Assembly.GetType("League.Controls.LolLauncherPanel")!;
            using (var launcherPanel = (Control)Activator.CreateInstance(launcherPanelType)!)
            {
                launcherPanelType
                    .GetMethod(
                        "ShowInstallation",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                    )!
                    .Invoke(launcherPanel, new object?[] { installation });
                Check(
                    Children(launcherPanel).OfType<Button>().Count(b => b.Enabled) == 3,
                    "Verified launchers enable launch and manual selection controls"
                );
                using var screenshot = new Bitmap(580, 150);
                launcherPanel.DrawToBitmap(screenshot, new Rectangle(0, 0, 580, 150));
                screenshot.Save(Path.Combine(AppContext.BaseDirectory, "launcher-panel.png"));
                launcherPanel.Width = 300;
                launcherPanel.PerformLayout();
                var buttons = Children(launcherPanel).OfType<Button>().ToArray();
                Check(
                    buttons.All(b => b.Right <= b.Parent!.ClientSize.Width && b.Bottom <= b.Parent.ClientSize.Height),
                    "Launcher buttons wrap without clipping in narrow panels"
                );
                Check(
                    Children(launcherPanel).OfType<TextBox>().Single().ReadOnly,
                    "Installation paths can be selected and copied without bypassing validation"
                );
                using var narrow = new Bitmap(launcherPanel.Width, launcherPanel.Height);
                launcherPanel.DrawToBitmap(narrow, new Rectangle(Point.Empty, narrow.Size));
                narrow.Save(Path.Combine(AppContext.BaseDirectory, "launcher-panel-narrow.png"));
            }
            var configPath = Path.Combine(launcherFixtureRoot, "LOLPathConfig.json");
            var helper = new League.UIHelpers.LOLHelper(configPath);
            File.WriteAllText(configPath, new JObject { ["LastLOLPath"] = delta }.ToString());
            Check(helper.ReadSavedInstallation() == null, "Legacy wrong-game cache is ignored");
            bool rejected = false;
            try
            {
                helper.StartValidatedLauncher(delta);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Check(rejected, "Wrong-game launcher is rejected before process start");
            helper.SaveInstallation(Path.GetDirectoryName(Path.GetDirectoryName(ux)!)!);
            var saved = JObject.Parse(File.ReadAllText(configPath));
            Check(
                saved["OfficialPath"]?.ToString() == official
                    && saved["WeGamePath"]?.ToString() == wegame
                    && saved["LastLOLPath"] == null,
                "Official and WeGame paths persist separately"
            );
            Check(helper.ReadSavedInstallation()?.OfficialPath == official, "Validated settings reload successfully");
            File.Delete(ux);
            Check(helper.ReadSavedInstallation() == null, "Moved or damaged install invalidates saved paths");
            File.WriteAllText(configPath, "broken json");
            Check(helper.ReadSavedInstallation() == null, "Damaged launcher config does not prevent startup");
        }
        finally
        {
            if (
                Path.GetFullPath(launcherFixtureRoot)
                    .StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
            )
            {
                Directory.Delete(launcherFixtureRoot, true);
            }
        }
        var roomCache = new League.Parsers.PlayerMatchCacheManager();
        using (var headerMatch = new MatchInfo { AllParticipants = new(), RawGameData = new JObject() })
        using (var detailLayout = new MatchDetailView(headerMatch))
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var scoreboard = (Panel)typeof(MatchDetailView).GetField("_scoreboard", flags)!.GetValue(detailLayout)!;
            var section = new Panel();
            var playerHeader = new Button { Text = "玩家", Bounds = new Rectangle(58, 32, 180, 30) };
            var itemsHeader = new Button { Text = "技能 / 装备", Bounds = new Rectangle(242, 32, 182, 30) };
            section.Controls.Add(playerHeader);
            section.Controls.Add(itemsHeader);
            scoreboard.Controls.Add(section);
            var reflow = typeof(MatchDetailView).GetMethod("ReflowScoreboard", flags)!;
            foreach (var width in new[] { 900, 1300, 900 })
            {
                detailLayout.Size = new Size(width, 600);
                reflow.Invoke(detailLayout, null);
                Check(
                    !playerHeader.Bounds.IntersectsWith(itemsHeader.Bounds),
                    "Player and equipment headers do not overlap when resizing detail view"
                );
                Check(itemsHeader.Text == "技能 / 装备", "Equipment heading retains its full text");
            }
        }
        using (
            var summaryMatch = new MatchInfo
            {
                QueueId = "420",
                AllParticipants = new()
                {
                    JObject.Parse("{\"teamId\":100,\"kills\":3,\"deaths\":2,\"assists\":8,\"goldEarned\":12345}"),
                },
                RawGameData = JObject.Parse(
                    "{\"teams\":[{\"teamId\":100,\"towerKills\":2,\"objectives\":{\"tower\":{\"kills\":3},\"dragon\":{\"kills\":0}}},{\"teamId\":200,\"towerKills\":9}]}"
                ),
            }
        )
        {
            var summary = TeamSummary.From(summaryMatch, 100);
            Check(
                summary.Kills == 3
                    && summary.Gold == 12345
                    && summary.Objectives.Single(o => o.Key == "tower").Count == 3,
                "Team summary uses correct team and modern objective fields"
            );
            Check(
                summary.Objectives.Single(o => o.Key == "dragon").Count == 0
                    && summary.Objectives.Single(o => o.Key == "baron").Count == null,
                "Team objectives distinguish missing values from zero"
            );
            var headerType = typeof(PlayerCardControl).Assembly.GetType("League.Controls.TeamSummaryHeader")!;
            using var tips = new ToolTip();
            using var header = (Control)
                Activator.CreateInstance(headerType, new object[] { "胜方 · 蓝队", Color.SeaGreen, summary, tips })!;
            header.Size = new Size(700, 32);
            using var screenshot = new Bitmap(700, 32);
            header.DrawToBitmap(screenshot, new Rectangle(0, 0, 700, 32));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "team-summary.png"));
            summaryMatch.QueueId = "2400";
            Check(
                TeamSummary.From(summaryMatch, 100).Objectives.Length == 2,
                "Hextech ARAM hides inapplicable neutral objectives"
            );
            summaryMatch.AllParticipants[0].Remove("goldEarned");
            Check(
                TeamSummary.From(summaryMatch, 100).Gold == null,
                "Incomplete team gold is not presented as a complete sum"
            );
        }
        var roomStatisticsFixture = new PlayerMatchInfo
        {
            RecentMatches = new()
            {
                new()
                {
                    Kills = 2,
                    Deaths = 1,
                    Assists = 2,
                },
                new()
                {
                    Kills = 4,
                    Deaths = 3,
                    Assists = 4,
                },
            },
            WinHistory = new() { true, false },
        };
        var roomStatistics = RoomRecentStatistics.From(roomStatisticsFixture);
        Check(
            roomStatistics.Games == 2 && roomStatistics.Kda == 9 && roomStatistics.WinRate == 50,
            "Room statistics aggregate kills/deaths instead of averaging individual ratios"
        );
        Check(
            RoomRecentStatistics.From(new PlayerMatchInfo()).Kda == null
                && RoomRecentStatistics.From(new PlayerMatchInfo()).WinRate == null,
            "Empty room history has no fabricated statistics"
        );
        roomStatisticsFixture.RecentMatches = new()
        {
            new()
            {
                Kills = 1,
                Assists = 2,
                Deaths = 0,
            },
        };
        Check(RoomRecentStatistics.From(roomStatisticsFixture).Kda == 9, "Room KDA handles zero total deaths");
        var aramHistory = new League.Models.PlayerMatchInfo();
        roomCache.CachePlayerMatch(123, "q_450", aramHistory);
        Check(
            roomCache.TryGetPlayerMatch(123, "q_450", out var cachedHistory)
                && ReferenceEquals(cachedHistory, aramHistory),
            "Room cache reuses history only for the same mode"
        );
        Check(
            !roomCache.TryGetPlayerMatch(123, "q_2400", out _) && !roomCache.TryGetPlayerMatch(123, "", out _),
            "Switching to Hextech or global history cannot reuse ARAM history"
        );
        roomCache.ClearCache();
        Check(!roomCache.TryGetPlayerMatch(123, "q_450", out _), "Leaving room clears history cache");
        Console.WriteLine(
            "PASS: identity, remakes, duplicates, missing values, SGP wrapper, team mapping, frame gaps, events, cancellation, privacy, fallback scope, room history filters/cache, WinForms controls."
        );
    }

    private sealed record Reply(HttpStatusCode Status, string Body);

    private sealed class Handler(Func<string, Reply> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = reply(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(result.Status) { Content = new StringContent(result.Body) });
        }
    }
}
