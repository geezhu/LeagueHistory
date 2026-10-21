internal static class DefaultStatisticsSuite
{
    public static void Run()
    {
        DataChecks();
        WinFormsCheckHost.Run(UiChecks);
        Console.WriteLine(
            "PASS: identity, remakes, duplicates, missing values, SGP wrapper, team mapping, frame gaps, events, cancellation, privacy, fallback scope, room history filters/cache, WinForms controls, and the offline parsing, Game Bar, overlay and settings checks."
        );
    }

    private static void DataChecks()
    {
        CoreUtilityChecks.Run();
        UtilityLogicChecks.Run();
        AugmentIconRendererChecks.Run();
        ChampionDataChecks.Run();
        UpdateSupportChecks.Run();
        HistoryDataChecks.Run();
        LiveChampionChecks.Run();
        LiveDeathStreakChecks.Run();
        AugmentStackChecks.Run();
        CooldownChecks.Run();
        CooldownOptionChecks.Run();
        SurvivalSoundChecks.Run();
        SummonerTimerChecks.Run();
        MinionWaveChecks.Run();
        RoomPlayerChecks.Run();
        Task.Run(ChatSendDiagnosticsChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AppLogChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(OpggChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AramMetaChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(LcuClientServiceChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(SgpSessionChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(GameBarVoiceRecognitionChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(GameBarVoiceRecognitionEdgeChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(MatchParsingChecks.RunAsync).GetAwaiter().GetResult();
        OfflineChecks();
    }

    /// <summary>
    /// Checks that were written but never ran here, so their code counted as uncovered. They cover parsing, the Game
    /// Bar, the overlays and settings and need neither a running client nor a voice model; the checks that do stay
    /// behind their own command.
    /// </summary>
    private static void OfflineChecks()
    {
        DeathTimingChecks.Run();
        TurretChecks.Run();
        GameQueueChecks.Run();
        GameFlowWatcherChecks.Run();
        LeagueConfigChecks.Run();
        ChampionManagerChecks.Run();
        ShutdownSettingsChecks.Run();
        ShutdownPlanChecks.Run();
        Task.Run(ControlChecks.Run).GetAwaiter().GetResult();
        Task.Run(GameBarBridgeChecks.Run).GetAwaiter().GetResult();
        Task.Run(GameBarCompositionChecks.Run).GetAwaiter().GetResult();
        Task.Run(StaleSessionChecks.Run).GetAwaiter().GetResult();
        Task.Run(OverlayChampionDetailChecks.Run).GetAwaiter().GetResult();
        Task.Run(OverlayRecordChecks.Run).GetAwaiter().GetResult();
        Task.Run(OverlayTierChatChecks.Run).GetAwaiter().GetResult();
        Task.Run(OverlayVoiceChatChecks.Run).GetAwaiter().GetResult();
        Task.Run(LoadingRosterIdentityChecks.Run).GetAwaiter().GetResult();
        Task.Run(ChampionMasteryChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(ModelChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(ClientChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(SkinBrowserSourceChecks.RunAsync).GetAwaiter().GetResult();
        MatchDetailManagerChecks.Run();
        PlayerCardFactoryChecks.Run();
        LcuProcessDiscoveryChecks.Run();
    }

    private static void UiChecks()
    {
        OverlayPartyChecks.Run();
        SkinBrowserChecks.Run();
        PreliminaryChecks.Run();
        PreliminaryListChecks.Run();
        AssistantWindowChecks.Run();
        GameHotkeyChecks.Run();
        CompositionViewChecks.Run();
        OpggChecks.Render();
        AramMetaChecks.Render();
        SettingsChecks.Render();
        PlayerChampionChecks.Run();
        AssistantChecks.Run();
        MayhemAssistantChecks.Run();
        RosterChecks.Run();
        AssistantRosterWindowChecks.Run();
        Task.Run(LiveInsightChecks.RunAsync).GetAwaiter().GetResult();
        GameBarPackageChecks.Run();
        StatisticsViewChecks.Run();
        ProfileViewChecks.Run();
        RoomViewChecks.Run();
        LauncherInstallationChecks.Run();
        OfflineUiChecks();
    }

    /// <summary>The interface halves of the checks above: layouts, navigation, overlays and settings surfaces.</summary>
    private static void OfflineUiChecks()
    {
        AramMetaViewLifecycleChecks.Run();
        AssistantLifecycleChecks.Run();
        AssistantSessionChecks.Run();
        HistoryNavigationChecks.Run();
        HistoryPresentationChecks.Run();
        LoadingOverlayChecks.Run();
        LoadingOverlayManagerChecks.Run();
        TabbedContentChecks.Run();
        Task.Run(ProfileIconChecks.RunAsync).GetAwaiter().GetResult();
        LoadingViewChecks.Run();
        MainNavigationChecks.Run();
        OpggViewLifecycleChecks.Run();
        OpggViewSurfaceChecks.Run();
        OpggRankingsChecks.Run();
        OverlayLifecycleChecks.Run();
        ProfilePresentationChecks.Run();
        ProfileSurfaceChecks.Run();
        ScoreboardLayoutChecks.Run();
        SelectionControlChecks.Run();
        SettingsSurfaceChecks.Run();
        ShutdownUiChecks.Run();
        VoiceSettingsChecks.Run();
        UiStateChecks.Run();
        MatchListPanelChecks.Run();
        MatchTabPageContentChecks.Run();
        MatchTabPageSurfaceChecks.Run();
        MatchTabPageDetailChecks.Run();
        MatchDetailChecks.Run();
        TeamCardDisplayChecks.Run();
        Task.Run(MatchQueryProcessorChecks.RunAsync).GetAwaiter().GetResult();
        ClosableTabControlChecks.Run();
        VoiceInputDialogChecks.Run();
        ProgressFormChecks.Run();
    }
}
