internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Settings, logs and search history go to a throwaway folder, never the user's own files.
        string dataRoot = Path.Combine(Path.GetTempPath(), "league-checks-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(League.Services.AppDataPaths.RootVariable, dataRoot);
        try
        {
            RunChecks(args);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dataRoot))
                {
                    Directory.Delete(dataRoot, true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A log still open by a background writer is left in the temp folder.
                Console.WriteLine("Test data folder kept: " + dataRoot);
            }
        }
    }

    private static void RunChecks(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if (RunParameterizedCheck(args))
        {
            return;
        }
        var checks = CreateChecks();
        var selected = args.FirstOrDefault(checks.ContainsKey);
        if (selected != null)
        {
            checks[selected]();
            return;
        }
        if (args.Length > 0)
        {
            throw new ArgumentException("Unknown check command: " + string.Join(" ", args));
        }
        DefaultStatisticsSuite.Run();
    }

    private static bool RunParameterizedCheck(string[] args)
    {
        if (args.Length == 2 && args[0] == "--sensevoice-engine-check")
        {
            Background(() => SenseVoiceChecks.EngineAsync(args[1]));
        }
        else if (args.Length == 3 && args[0] == "--overlay-detail-fixture")
        {
            OverlayDetailPreview.Export(args[1], args[2]);
        }
        else
        {
            return false;
        }
        return true;
    }

    private static Dictionary<string, Action> CreateChecks()
    {
        var checks = new Dictionary<string, Action>(StringComparer.Ordinal);
        AddDataChecks(checks);
        AddUiChecks(checks);
        AddAssistantChecks(checks);
        AddOverlayChecks(checks);
        AddDiagnostics(checks);
        return checks;
    }

    private static void Background(Func<Task> checks) => Task.Run(checks).GetAwaiter().GetResult();

    private static void Background(Action checks) => Task.Run(checks).GetAwaiter().GetResult();

    private static void Ui(Action checks) => WinFormsCheckHost.Run(checks);

    private static void AddDataChecks(Dictionary<string, Action> checks)
    {
        checks["--client-checks"] = () => Background(ClientChecks.RunAsync);
        checks["--lcu-service-checks"] = () => Background(LcuClientServiceChecks.RunAsync);
        checks["--sgp-session-checks"] = () => Background(SgpSessionChecks.RunAsync);
        checks["--model-checks"] = () => Background(ModelChecks.RunAsync);
        checks["--sensevoice-checks"] = () => Background(SenseVoiceChecks.RunAsync);
        checks["--gamebar-voice-checks"] = () => Background(GameBarVoiceRecognitionChecks.RunAsync);
        checks["--gamebar-voice-edge-checks"] = () => Background(GameBarVoiceRecognitionEdgeChecks.RunAsync);
        checks["--roster-identity-checks"] = () => Background(LoadingRosterIdentityChecks.Run);
        checks["--live-champion-checks"] = LiveChampionChecks.Run;
        checks["--death-streak-checks"] = LiveDeathStreakChecks.Run;
        checks["--augment-stack-checks"] = AugmentStackChecks.Run;
        checks["--cooldown-checks"] = CooldownChecks.Run;
        checks["--cooldown-option-checks"] = CooldownOptionChecks.Run;
        checks["--survival-sound-checks"] = SurvivalSoundChecks.Run;
        checks["--summoner-timer-checks"] = SummonerTimerChecks.Run;
        checks["--room-player-checks"] = RoomPlayerChecks.Run;
        checks["--config-checks"] = LeagueConfigChecks.Run;
        checks["--shutdown-checks"] = ShutdownSettingsChecks.Run;
        checks["--shutdown-plan-checks"] = ShutdownPlanChecks.Run;
        checks["--core-utility-checks"] = CoreUtilityChecks.Run;
        checks["--utility-logic-checks"] = UtilityLogicChecks.Run;
        checks["--augment-icon-checks"] = AugmentIconRendererChecks.Run;
        checks["--champion-data-checks"] = ChampionDataChecks.Run;
        checks["--champion-manager-checks"] = ChampionManagerChecks.Run;
        checks["--update-support-checks"] = UpdateSupportChecks.Run;
        checks["--diagnostic-log-checks"] = () =>
        {
            Background(AppLogChecks.RunAsync);
        };
        checks["--history-data-checks"] = HistoryDataChecks.Run;
        checks["--match-parse-checks"] = () => Background(MatchParsingChecks.RunAsync);
    }

    private static void AddUiChecks(Dictionary<string, Action> checks)
    {
        checks["--voice-settings-checks"] = () => Ui(VoiceSettingsChecks.Run);
        checks["--progress-form-checks"] = () => Ui(ProgressFormChecks.Run);
        checks["--voice-dialog-checks"] = () => Ui(VoiceInputDialogChecks.Run);
        checks["--settings-surface-checks"] = () => Ui(SettingsSurfaceChecks.Run);
        checks["--ui-state-checks"] = () => Ui(UiStateChecks.Run);
        checks["--match-card-checks"] = () => Ui(MatchListPanelChecks.Run);
        checks["--history-page-checks"] = () => Ui(MatchTabPageContentChecks.Run);
        checks["--history-page-surface-checks"] = () =>
            Ui(() =>
            {
                MatchTabPageSurfaceChecks.Run();
                MatchTabPageDetailChecks.Run();
            });
        checks["--match-detail-checks"] = () => Ui(MatchDetailChecks.Run);
        checks["--query-processor-checks"] = () => Background(MatchQueryProcessorChecks.RunAsync);
        checks["--loading-view-checks"] = () => Ui(LoadingViewChecks.Run);
        checks["--loading-overlay-manager-checks"] = () => Ui(LoadingOverlayManagerChecks.Run);
        checks["--tabbed-content-checks"] = () => Ui(TabbedContentChecks.Run);
        checks["--profile-icon-checks"] = () => Background(ProfileIconChecks.RunAsync);
        checks["--main-navigation-checks"] = () => Ui(MainNavigationChecks.Run);
        checks["--team-card-checks"] = TeamCardDisplayChecks.Run;
        checks["--history-navigation-checks"] = () => Ui(HistoryNavigationChecks.Run);
        checks["--profile-surface-checks"] = () => Ui(ProfileSurfaceChecks.Run);
        checks["--opgg-lifecycle-checks"] = () => Ui(OpggViewLifecycleChecks.Run);
        checks["--opgg-surface-checks"] = () =>
            Ui(() =>
            {
                OpggRankingsChecks.Run();
                OpggViewSurfaceChecks.Run();
            });
        checks["--profile-presentation-checks"] = () => Ui(ProfilePresentationChecks.Run);
        checks["--selection-control-checks"] = () => Ui(SelectionControlChecks.Run);
        checks["--player-champion-checks"] = () => Ui(PlayerChampionChecks.Run);
        checks["--history-presentation-checks"] = () => Ui(HistoryPresentationChecks.Run);
        checks["--scoreboard-layout-checks"] = () => Ui(ScoreboardLayoutChecks.Run);
        checks["--overlay-lifecycle-checks"] = () => Ui(OverlayLifecycleChecks.Run);
        checks["--shutdown-ui-checks"] = () => Ui(ShutdownUiChecks.Run);
        checks["--settings-checks"] = () => Ui(SettingsChecks.Render);
        checks["--live-insight-checks"] = () => Background(LiveInsightChecks.RunAsync);
        checks["--gamebar-package-checks"] = GameBarPackageChecks.Run;
        checks["--statistics-view-checks"] = () => Ui(StatisticsViewChecks.Run);
        checks["--profile-view-checks"] = () => Ui(ProfileViewChecks.Run);
        checks["--room-view-checks"] = () => Ui(RoomViewChecks.Run);
        checks["--launcher-installation-checks"] = () => Ui(LauncherInstallationChecks.Run);
    }

    private static void AddAssistantChecks(Dictionary<string, Action> checks)
    {
        checks["--assistant-lifecycle-checks"] = () => Ui(AssistantLifecycleChecks.Run);
        checks["--assistant-session-checks"] = () => Ui(AssistantSessionChecks.Run);
        checks["--assistant-voice-checks"] = () => Ui(AssistantVoiceChecks.Run);
        checks["--assistant-voice-engine-check"] = () => Ui(AssistantVoiceChecks.EngineCheck);
        checks["--assistant-checks"] = () =>
            Ui(() =>
            {
                AssistantChecks.Run();
                MayhemAssistantChecks.Run();
            });
        checks["--assistant-placement-checks"] = () =>
            Ui(() =>
            {
                AssistantChecks.Placement();
            });
        checks["--assistant-window-checks"] = () =>
            Ui(() =>
            {
                AssistantWindowChecks.Run();
            });
        checks["--mastery-checks"] = MasteryChecks;
        checks["--composition-checks"] = () =>
        {
            Ui(CompositionViewChecks.Run);
        };
        checks["--opgg-checks"] = () =>
        {
            Background(OpggChecks.RunAsync);
            Ui(OpggChecks.Render);
        };
        checks["--arammeta-checks"] = () =>
        {
            Background(AramMetaChecks.RunAsync);
            Ui(() =>
            {
                AramMetaChecks.Render();
                AramMetaViewLifecycleChecks.Run();
            });
        };
    }

    private static void AddOverlayChecks(Dictionary<string, Action> checks)
    {
        checks["--gamebar-protocol-checks"] = () => Background(GameBarProtocolChecks);
        checks["--overlay-chat-checks"] = () =>
            Background(() =>
            {
                OverlayVoiceChatChecks.Run();
                OverlayTierChatChecks.Run();
                OverlayRecordChecks.Run();
            });
        checks["--overlay-record-checks"] = () => Background(OverlayRecordChecks.Run);
        checks["--control-checks"] = () => Background(ControlChecks.Run);
        checks["--overlay-party-checks"] = () => Ui(OverlayPartyChecks.Run);
        checks["--skin-browser-checks"] = () => Ui(SkinBrowserChecks.Run);
        checks["--skin-source-checks"] = () => Background(SkinBrowserSourceChecks.RunAsync);
        checks["--closable-tab-checks"] = () => Ui(ClosableTabControlChecks.Run);
        checks["--match-detail-manager-checks"] = MatchDetailManagerChecks.Run;
        checks["--player-card-checks"] = PlayerCardFactoryChecks.Run;
        checks["--lcu-discovery-checks"] = LcuProcessDiscoveryChecks.Run;
        checks["--roster-window-checks"] = () => Ui(AssistantRosterWindowChecks.Run);
        checks["--preliminary-checks"] = () => Ui(PreliminaryChecks.Run);
        checks["--preliminary-list-checks"] = () => Ui(PreliminaryListChecks.Run);
        checks["--gamebar-composition-checks"] = () => Background(GameBarCompositionChecks.Run);
        checks["--stale-session-checks"] = () => Background(StaleSessionChecks.Run);
        checks["--game-queue-checks"] = () => Background(GameQueueChecks.Run);
        checks["--game-flow-checks"] = GameFlowWatcherChecks.Run;
        checks["--minion-wave-checks"] = MinionWaveChecks.Run;
        checks["--death-timing-checks"] = DeathTimingChecks.Run;
        checks["--turret-checks"] = TurretChecks.Run;
        checks["--tier-chat-checks"] = () =>
        {
            Ui(GameHotkeyChecks.Run);
            Background(ChatSendDiagnosticsChecks.RunAsync);
        };
        checks["--loading-overlay-checks"] = () =>
        {
            Background(GameBarProtocolChecks);
            Ui(() =>
            {
                AssistantWindowChecks.Run();
                LoadingOverlayChecks.Run();
            });
        };
    }

    private static void AddDiagnostics(Dictionary<string, Action> checks)
    {
        // These opt-in diagnostics are never included in the default fixture suite.
        checks["--mastery-live-diagnostics"] = () => Background(MasteryLiveDiagnostics.Run);
        checks["--live-champion-diagnostics"] = () => Background(LiveChampionChecks.RunLiveAsync);
        checks["--death-streak-live-diagnostics"] = () => Background(LiveDeathStreakChecks.RunLiveAsync);
        checks["--shutdown-validate"] = ShutdownSettingsChecks.ValidateWindows;
        // Live integration checks: they reach the internet or a running client, so they are opt-in only.
        checks["--live-source-checks"] = () => Background(LiveSourceChecks.RunAsync);
        checks["--live-client-checks"] = () => Background(LiveClientChecks.RunAsync);
        checks["--live-champ-select-checks"] = () => Background(LiveChampSelectChecks.RunAsync);
        checks["--live-sgp-checks"] = () => Background(LiveSgpChecks.RunAsync);
        checks["--live-match-detail-checks"] = () => Background(LiveMatchDetailChecks.RunAsync);
    }

    private static void GameBarProtocolChecks()
    {
        GameBarBridgeChecks.Run();
        OverlayChampionDetailChecks.Run();
        OverlayTierChatChecks.Run();
        OverlayRecordChecks.Run();
        ControlChecks.Run();
        GameBarCompositionChecks.Run();
        StaleSessionChecks.Run();
        GameQueueChecks.Run();
        OverlayVoiceChatChecks.Run();
    }

    private static void MasteryChecks()
    {
        Background(ChampionMasteryChecks.RunAsync);
        Background(LoadingRosterIdentityChecks.Run);
        Background(GameBarBridgeChecks.Run);
        Ui(() =>
        {
            ChampionMasteryChecks.Render();
            MayhemAssistantChecks.Run();
            RosterChecks.Run();
            SettingsChecks.Render();
        });
    }
}
