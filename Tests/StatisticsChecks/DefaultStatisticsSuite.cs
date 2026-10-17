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
        ImageProcessorChecks.Run();
        AugmentIconRendererChecks.Run();
        ChampionDataChecks.Run();
        VoicePcmBufferChecks.Run();
        UpdateSupportChecks.Run();
        BuildInfoChecks.Run();
        HistoryDataChecks.Run();
        TimelineDataChecks.Run();
        AramTeamScoreChecks.Run();
        ChampionTierChatChecks.Run();
        LiveChampionChecks.Run();
        LiveDeathStreakChecks.Run();
        AugmentStackChecks.Run();
        HealthEstimateChecks.Run();
        CooldownChecks.Run();
        CooldownOptionChecks.Run();
        SurvivalSoundChecks.Run();
        SummonerTimerChecks.Run();
        MinionWaveChecks.Run();
        RespawnMarkChecks.Run();
        RoomPlayerChecks.Run();
        Task.Run(ChatSendDiagnosticsChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(DiagnosticLogChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AppLogChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(DiagnosticReportChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(UiHangMonitorChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(OpggChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AramMetaChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(ItemSetChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AramBalanceChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(LcuClientServiceChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(VoiceModelStoreChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(SgpSessionChecks.RunAsync).GetAwaiter().GetResult();
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
        LeagueConfigChecks.Run();
        ShutdownSettingsChecks.Run();
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
    }

    private static void UiChecks()
    {
        AssistantVisibilityChecks.Run();
        OverlayPartyChecks.Run();
        SkinBrowserChecks.Run();
        PreliminaryChecks.Run();
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
        LiveMatchChecks.Run();
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
        HistoryNavigationChecks.Run();
        HistoryPresentationChecks.Run();
        LoadingOverlayChecks.Run();
        LoadingOverlayManagerChecks.Run();
        LoadingViewChecks.Run();
        MainNavigationChecks.Run();
        OpggViewLifecycleChecks.Run();
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
        AugmentDescriptionChecks.Run();
        MatchTabPageContentChecks.Run();
        MatchDetailChecks.Run();
        Task.Run(MatchQueryProcessorChecks.RunAsync).GetAwaiter().GetResult();
    }
}
