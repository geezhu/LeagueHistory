internal static class DefaultStatisticsSuite
{
    public static void Run()
    {
        DataChecks();
        WinFormsCheckHost.Run(UiChecks);
        Console.WriteLine(
            "PASS: identity, remakes, duplicates, missing values, SGP wrapper, team mapping, frame gaps, events, cancellation, privacy, fallback scope, room history filters/cache, WinForms controls."
        );
    }

    private static void DataChecks()
    {
        CoreUtilityChecks.Run();
        HistoryDataChecks.Run();
        TimelineDataChecks.Run();
        AramTeamScoreChecks.Run();
        ChampionTierChatChecks.Run();
        LiveChampionChecks.Run();
        LiveDeathStreakChecks.Run();
        RoomPlayerChecks.Run();
        Task.Run(ChatSendDiagnosticsChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(OpggChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AramMetaChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(ItemSetChecks.RunAsync).GetAwaiter().GetResult();
        Task.Run(AramBalanceChecks.RunAsync).GetAwaiter().GetResult();
    }

    private static void UiChecks()
    {
        AssistantVisibilityChecks.Run();
        OverlayPartyChecks.Run();
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
        StatisticsViewChecks.Run();
        ProfileViewChecks.Run();
        RoomViewChecks.Run();
        LauncherInstallationChecks.Run();
    }
}
