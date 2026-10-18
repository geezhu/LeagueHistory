using System.Collections.Concurrent;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The room loader turns one LCU roster snapshot into independently cancellable per-player loads and guarded UI
/// delivery. Every dependency is injected, so a test needs no client and no window: the fetch delegate stands in for
/// the client and the dispatch delegate for the UI thread. This class replaces the loader half of the checks in
/// Tests/StatisticsChecks/RoomPlayerChecks.cs. The card layout half of those checks belongs to PlayerCardUIManager.
/// </summary>
public class RoomPlayerLoaderTests
{
    private const string LoadingStatus = "正在读取战绩…";
    private const string TimeoutStatus = "读取超时";
    private const string FailedStatus = "读取失败";

    [Fact]
    public void Normalize_RepeatedKnownIdentity_CapsTheTeamAtFiveDistinctSlots()
    {
        var roster = new JArray(Player(1), Player(1), Player(2), Player(3), Player(4), Player(5), Player(6));

        JArray normalized = RoomPlayerLoader.Normalize(roster);

        Assert.Equal(5, normalized.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, normalized.Select(player => player.Value<long>("summonerId")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("OBFUSCATED_7")]
    [InlineData("HIDDEN_7")]
    public void Normalize_PlayersSharingAnInvisibleIdentity_KeepsFiveDistinctSlots(string puuid)
    {
        JObject[] hidden = Enumerable
            .Range(0, 5)
            .Select(cell => new JObject
            {
                ["summonerId"] = 0,
                ["puuid"] = puuid,
                ["cellId"] = cell,
            })
            .ToArray();

        JArray normalized = RoomPlayerLoader.Normalize(new JArray(hidden));

        Assert.Equal(5, normalized.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, normalized.Select(player => player.Value<int>("cellId")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("OBFUSCATED_7")]
    [InlineData("HIDDEN_7")]
    public void PublicPuuid_IdentityThatIsNotPublic_ReturnsEmpty(string puuid)
    {
        string identity = RoomPlayerLoader.PublicPuuid(new JObject { ["puuid"] = puuid });

        Assert.Equal(string.Empty, identity);
    }

    [Theory]
    [InlineData("test-player-1", 7, 3, "p:test-player-1")]
    [InlineData("00000000-0000-0000-0000-000000000000", 7, 3, "s:7")]
    [InlineData("", 0, 3, "cell:3")]
    [InlineData("", 0, null, "cell:0")]
    public void Identity_NoPublicIdentity_FallsBackToSummonerIdOrCell(
        string puuid,
        long summonerId,
        int? cell,
        string expected
    )
    {
        string identity = RoomPlayerLoader.Identity(IdentityPlayer(puuid, summonerId, cell), column: 0);

        Assert.Equal(expected, identity);
    }

    [Fact]
    public void WithChampionSelections_KnownMember_AppliesTheSelectedChampion()
    {
        JArray enriched = RoomPlayerLoader.WithChampionSelections(
            new JArray(Team(1)),
            new JArray(Selected(1, 99), Selected(2, 25))
        );

        Assert.Equal(99, Assert.Single(enriched.OfType<JObject>()).Value<int>("championId"));
    }

    [Fact]
    public void WithChampionSelections_SelectionForUnknownIdentity_AddsNoMember()
    {
        JArray enriched = RoomPlayerLoader.WithChampionSelections(new JArray(Team(1)), new JArray(Selected(2, 25)));

        Assert.Equal(101, Assert.Single(enriched.OfType<JObject>()).Value<int>("championId"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void ResolveGameTeams_NonFivePlayerMode_KeepsRawTeams(int playersPerTeam)
    {
        JObject data = GameData(playersPerTeam, Team(1, 2, 3, 4, 5), Team(6, 8, 9), Selections(1, 10));

        var teams = RoomPlayerLoader.ResolveGameTeams(data);

        Assert.Equal(5, teams.One.Count);
        Assert.Equal(3, teams.Two.Count);
    }

    [Fact]
    public void ResolveGameTeams_TwoIncompleteTeams_LeavesVacantSlotsEmpty()
    {
        JObject data = GameData(5, Team(1, 2, 3), Team(6, 8, 9), Selections(1, 10));

        var teams = RoomPlayerLoader.ResolveGameTeams(data);

        Assert.Equal(3, teams.One.Count);
        Assert.Equal(3, teams.Two.Count);
    }

    [Theory]
    [InlineData(9, 110)]
    [InlineData(10, 0)]
    public void ResolveGameTeams_IncompleteSelections_KeepsRawTeams(int count, int lastChampion)
    {
        JObject[] selections = Selections(1, count);
        selections[^1]["championId"] = lastChampion;
        JObject data = GameData(5, Team(1, 2, 3, 4, 5), Team(6, 8, 9), selections);

        var teams = RoomPlayerLoader.ResolveGameTeams(data);

        Assert.Equal(3, teams.Two.Count);
    }

    [Fact]
    public void ResolveGameTeams_DuplicateSelections_KeepsRawTeams()
    {
        JObject[] selections = Selections(1, 9).Append(Selected(9, 109)).ToArray();
        JObject data = GameData(5, Team(1, 2, 3, 4, 5), Team(6, 8, 9), selections);

        var teams = RoomPlayerLoader.ResolveGameTeams(data);

        Assert.Equal(3, teams.Two.Count);
    }

    [Fact]
    public void ResolveGameTeams_MemberMissingFromSelections_KeepsRawTeams()
    {
        JObject data = GameData(5, Team(1, 2, 3, 4, 5), Team(6, 8, 11), Selections(1, 10));

        var teams = RoomPlayerLoader.ResolveGameTeams(data);

        Assert.Equal(3, teams.Two.Count);
    }

    [Fact]
    public void ResolveGameTeams_IdentityKnownToBothTeams_KeepsRawTeams()
    {
        JObject data = GameData(5, Team(1, 2, 3, 4, 5), Team(5, 6, 7), Selections(1, 10));

        var teams = RoomPlayerLoader.ResolveGameTeams(data);

        Assert.Equal(3, teams.Two.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveGameTeams_CompleteTeamWithTenSelections_FillsTheOtherTeam(bool completeTeamIsOne)
    {
        JObject data = HiddenRoster(completeTeamIsOne);

        var teams = RoomPlayerLoader.ResolveGameTeams(data);
        JArray incomplete = completeTeamIsOne ? teams.Two : teams.One;
        JObject[] added = incomplete
            .OfType<JObject>()
            .Where(player => player.Value<bool?>("__roomSelectionOnly") == true)
            .ToArray();

        Assert.Equal(5, incomplete.Count);
        Assert.Equal(new[] { 107, 110 }, added.Select(player => player.Value<int>("championId")));
        Assert.Equal(new long[] { 0, 0 }, added.Select(player => player.Value<long>("summonerId")));
        Assert.Equal(new[] { "HIDDEN", "HIDDEN" }, added.Select(player => player.Value<string>("nameVisibilityType")));
        Assert.Equal(3, ((JArray)data[completeTeamIsOne ? "teamTwo" : "teamOne"]!).Count);
    }

    [Fact]
    public async Task UpdateTeamAsync_QueuedDispatch_HoldsTheRenderUntilTheUiThreadRunsIt()
    {
        using var fixture = new LoaderFixture();
        fixture.QueueDeliveries();
        long generation = fixture.Reset();

        Task update = fixture.UpdateAsync(new[] { Player(1), Player(2) }, ally: true, generation);

        Assert.Equal(1, fixture.PostedDeliveries);
        Assert.Empty(fixture.Latest);

        await fixture.RunPostedDeliveryAsync();
        await update;

        Assert.Equal(new[] { LoadingStatus, LoadingStatus }, fixture.Latest.Select(state => state.Status));
    }

    [Fact]
    public async Task UpdateTeamAsync_ResetBeforeTheQueuedRender_DeliversNothing()
    {
        using var fixture = new LoaderFixture();
        fixture.QueueDeliveries();
        long generation = fixture.Reset();
        Task update = fixture.UpdateAsync(new[] { Player(1), Player(2) }, ally: true, generation);

        fixture.Reset();
        await fixture.RunPostedDeliveryAsync();
        await update;

        Assert.Empty(fixture.Latest);
        Assert.Equal(0, fixture.Calls(1));
    }

    [Fact]
    public async Task UpdateTeamAsync_SlowPlayerStaysPending_WhileTheOthersComplete()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();

        await fixture.UpdateAsync(new[] { Player(1), Player(2), Player(3) }, ally: true, generation);
        await fixture.StartedAsync(1, 2, 3);
        fixture.Release(2);
        fixture.Release(3);
        await fixture.RenderedAsync(states => states.Count(state => state.Info != null) == 2);

        Assert.Equal(LoadingStatus, fixture.State(true, 1).Status);
        Assert.Null(fixture.State(true, 1).Info);
        Assert.Same(fixture.Fetched(2), fixture.State(true, 2).Info);
        Assert.Same(fixture.Fetched(3), fixture.State(true, 3).Info);
        Assert.Equal(new[] { 1, 1, 1 }, new long[] { 1, 2, 3 }.Select(fixture.Calls));
    }

    [Fact]
    public async Task UpdateTeamAsync_ChampionOnlyChange_KeepsTheLoadedHistory()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();
        JObject player = Player(1, 99);
        await fixture.UpdateAsync(new[] { player }, ally: true, generation);
        await fixture.StartedAsync(1);
        fixture.Release(1);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        player["championId"] = 55;
        await fixture.UpdateAsync(new[] { player }, ally: true, generation);

        Assert.Same(fixture.Fetched(1), fixture.State(true, 1).Info);
        Assert.Equal(55, fixture.State(true, 1).Player.Value<int>("championId"));
        Assert.Equal(1, fixture.Calls(1));
    }

    [Fact]
    public async Task UpdateTeamAsync_ReorderedSlotsWithAChampionChange_KeepsBothLoadedHistories()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();
        JObject first = Player(1, 99);
        JObject second = Player(2, 25);
        await fixture.UpdateAsync(new[] { first, second }, ally: true, generation);
        await fixture.StartedAsync(1, 2);
        fixture.Release(1);
        fixture.Release(2);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        first["championId"] = 55;
        await fixture.UpdateAsync(new[] { second, first }, ally: true, generation);

        Assert.Equal(0, fixture.State(true, 2).Column);
        Assert.Equal(1, fixture.State(true, 1).Column);
        Assert.Equal(25, fixture.State(true, 2).Player.Value<int>("championId"));
        Assert.Equal(55, fixture.State(true, 1).Player.Value<int>("championId"));
        Assert.Same(fixture.Fetched(1), fixture.State(true, 1).Info);
        Assert.Same(fixture.Fetched(2), fixture.State(true, 2).Info);
        Assert.Equal(1, fixture.Calls(1));
        Assert.Equal(1, fixture.Calls(2));
    }

    [Fact]
    public async Task UpdateTeamAsync_QueueChange_ReloadsTheSameSlot()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();
        JObject player = Player(10);
        player["__roomQueue"] = "420";
        await fixture.UpdateAsync(new[] { player }, ally: true, generation);
        await fixture.StartedAsync(10);
        fixture.Release(10);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        player["__roomQueue"] = "450";
        await fixture.UpdateAsync(new[] { player }, ally: true, generation);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        Assert.Equal(2, fixture.Calls(10));
        Assert.Equal(new[] { LoaderFixture.Key(true, 10) }, fixture.Latest.Select(state => state.Key));
        Assert.Same(fixture.Fetched(10), fixture.State(true, 10).Info);
    }

    [Fact]
    public async Task UpdateTeamAsync_LeavingPlayer_CancelsItsRequest()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1), Player(2) }, ally: true, generation);
        await fixture.StartedAsync(1, 2);
        CancellationToken token = fixture.FetchToken(2);

        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);

        Assert.True(token.IsCancellationRequested, "the removed player's request must be cancelled");
        Assert.Equal(new[] { LoaderFixture.Key(true, 1) }, fixture.Latest.Select(state => state.Key));
    }

    [Fact]
    public async Task UpdateTeamAsync_LateResultForARemovedPlayer_IsNotDelivered()
    {
        using var fixture = new LoaderFixture(ignoreCancellation: true);
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1), Player(2) }, ally: true, generation);
        await fixture.StartedAsync(1, 2);
        await fixture.UpdateAsync(new[] { Player(2) }, ally: true, generation);

        Task<bool> delivered = fixture.WaitForDeliveryAsync(TimeSpan.FromMilliseconds(500));
        fixture.Release(1);

        Assert.False(await delivered, "a result for a removed player must not be delivered");
        Assert.Equal(new[] { LoaderFixture.Key(true, 2) }, fixture.Latest.Select(state => state.Key));
    }

    [Fact]
    public async Task UpdateTeamAsync_ResultForAPresentPlayer_IsDelivered()
    {
        using var fixture = new LoaderFixture(ignoreCancellation: true);
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);

        fixture.Release(1);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        Assert.Same(fixture.Fetched(1), fixture.State(true, 1).Info);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateTeamAsync_EitherTeamColor_LoadsAndKeepsTheTeamFlag(bool ally)
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();

        await fixture.UpdateAsync(new[] { Player(1) }, ally, generation);
        await fixture.StartedAsync(1);
        fixture.Release(1);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        RoomPlayerState state = fixture.State(ally, 1);
        Assert.Equal(ally, state.Ally);
        Assert.Equal(0, state.Column);
        Assert.Equal(generation, state.Generation);
        Assert.Same(fixture.Fetched(1), state.Info);
        Assert.Equal(string.Empty, state.Status);
    }

    [Fact]
    public async Task UpdateTeamAsync_TenCompletedPlayers_ReportNoLoadingState()
    {
        using var fixture = new LoaderFixture(
            fetch: (player, _) => Task.FromResult(Info(player.Value<long>("summonerId")))
        );
        long generation = fixture.Reset();
        JObject[] allies = Enumerable.Range(1, 5).Select(id => Player(id)).ToArray();
        JObject[] enemies = Enumerable.Range(6, 5).Select(id => Player(id)).ToArray();
        long[] expected = Enumerable.Range(1, 10).Select(id => (long)id).ToArray();

        await fixture.UpdateAsync(allies, ally: true, generation);
        await fixture.UpdateAsync(enemies, ally: false, generation);
        await fixture.RenderedAsync(states => states.Count == 10 && states.All(state => state.Info != null));

        Assert.Equal(
            Enumerable
                .Range(1, 5)
                .Select(id => LoaderFixture.Key(true, id))
                .Concat(Enumerable.Range(6, 5).Select(id => LoaderFixture.Key(false, id))),
            fixture.Latest.Select(state => state.Key)
        );
        Assert.Equal(Enumerable.Repeat(string.Empty, 10), fixture.Latest.Select(state => state.Status));
        Assert.Equal(expected, fixture.Latest.Select(state => state.Player.Value<long>("summonerId")));
    }

    [Fact]
    public async Task UpdateTeamAsync_MorePendingPlayersThanRequestSlots_AllTimeOutWithARetryOffer()
    {
        using var fixture = new LoaderFixture(
            timeout: TimeSpan.FromMilliseconds(50),
            retryDelay: TimeSpan.FromMilliseconds(5)
        );
        long generation = fixture.Reset();
        JObject[] players = Enumerable.Range(1, 5).Select(id => Player(id)).ToArray();

        await fixture.UpdateAsync(players, ally: true, generation);
        await fixture.RenderedAsync(states => states.Count == 5 && states.All(state => state.CanRetry));

        Assert.Equal(
            Enumerable.Range(1, 5).Select(id => LoaderFixture.Key(true, id)),
            fixture.Latest.Select(state => state.Key)
        );
        Assert.Equal(Enumerable.Repeat(TimeoutStatus, 5), fixture.Latest.Select(state => state.Status));
        Assert.Equal(Enumerable.Repeat(true, 5), fixture.Latest.Select(state => state.CanRetry));
        Assert.Equal(Enumerable.Repeat(false, 5), fixture.Latest.Select(state => state.Info != null));
    }

    [Fact]
    public async Task RetryAsync_AfterBothAttemptsFailed_RecoversWithoutFailureState()
    {
        using var fixture = new LoaderFixture(
            timeout: TimeSpan.FromMilliseconds(30),
            retryDelay: TimeSpan.FromMilliseconds(1)
        );
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(8) }, ally: true, generation);
        await fixture.RenderedAsync(states => states.All(state => state.CanRetry));
        RoomPlayerState failed = fixture.State(true, 8);
        int attemptsBeforeRetry = fixture.Calls(8);

        fixture.Release(8);
        await fixture.RetryAsync(failed.Key, generation);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));

        Assert.Equal(2, attemptsBeforeRetry);
        Assert.Equal(3, fixture.Calls(8));
        Assert.Equal(TimeoutStatus, failed.Status);
        Assert.Null(failed.Info);
        Assert.Same(fixture.Fetched(8), fixture.State(true, 8).Info);
        Assert.Equal(string.Empty, fixture.State(true, 8).Status);
        Assert.False(fixture.State(true, 8).CanRetry);
    }

    [Fact]
    public async Task UpdateTeamAsync_StaleGeneration_StartsNoRequestAndDeliversNothing()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();

        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation - 1);

        Assert.Empty(fixture.Latest);
        Assert.Equal(0, fixture.Calls(1));
    }

    [Fact]
    public async Task UpdateTeamAsync_FetchReceivesAClone_MutatingItLeavesTheSnapshotIntact()
    {
        var handed = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new LoaderFixture(
            fetch: (player, _) =>
            {
                handed.TrySetResult(player);
                return Task.FromResult(Info(1));
            }
        );
        long generation = fixture.Reset();
        JObject roster = Player(1, 99);

        await fixture.UpdateAsync(new[] { roster }, ally: true, generation);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));
        JObject captured = await handed.Task;

        roster["championId"] = 55;
        captured["championId"] = 77;

        Assert.NotSame(roster, captured);
        Assert.Equal(99, fixture.State(true, 1).Player.Value<int>("championId"));
    }

    [Fact]
    public async Task UpdateTeamAsync_FetchFails_RetriesOnceAndOffersManualRetry()
    {
        using var fixture = new LoaderFixture(retryDelay: TimeSpan.FromMilliseconds(1));
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);

        fixture.Fail(1, new InvalidOperationException("client refused"));
        await fixture.RenderedAsync(states => states.All(state => state.CanRetry));

        Assert.Equal(2, fixture.Calls(1));
        Assert.Equal(FailedStatus, fixture.State(true, 1).Status);
        Assert.Null(fixture.State(true, 1).Info);
    }

    [Fact]
    public async Task UpdateTeamAsync_ReturnedIdentityDiffersFromTheSlot_KeepsTheFailureState()
    {
        using var fixture = new LoaderFixture(retryDelay: TimeSpan.FromMilliseconds(1));
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);

        fixture.Release(1, Info(2));
        await fixture.RenderedAsync(states => states.All(state => state.CanRetry));

        Assert.Equal(2, fixture.Calls(1));
        Assert.Equal(FailedStatus, fixture.State(true, 1).Status);
        Assert.Null(fixture.State(true, 1).Info);
    }

    [Fact]
    public async Task UpdateTeamAsync_FetchReportsUnavailableData_KeepsTheFailureState()
    {
        using var fixture = new LoaderFixture(retryDelay: TimeSpan.FromMilliseconds(1));
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);

        fixture.Release(1, UnavailableInfo(1));
        await fixture.RenderedAsync(states => states.All(state => state.CanRetry));

        Assert.Equal(2, fixture.Calls(1));
        Assert.Equal(FailedStatus, fixture.State(true, 1).Status);
        Assert.Null(fixture.State(true, 1).Info);
    }

    [Fact]
    public async Task RetryAsync_WhileThePlayerIsStillLoading_StartsNoSecondRequest()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);

        await fixture.RetryAsync(LoaderFixture.Key(true, 1), generation);

        Assert.Equal(1, fixture.Calls(1));
        Assert.Equal(LoadingStatus, fixture.State(true, 1).Status);
        Assert.False(fixture.State(true, 1).CanRetry);
    }

    [Fact]
    public async Task Dispose_WithAPendingPlayer_CancelsItAndDeliversNothing()
    {
        using var fixture = new LoaderFixture(ignoreCancellation: true);
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);

        Task<bool> delivered = fixture.WaitForDeliveryAsync(TimeSpan.FromMilliseconds(500));
        fixture.Dispose();
        fixture.Release(1);

        Assert.False(await delivered, "a disposed loader must not deliver anything");
        Assert.True(fixture.FetchToken(1).IsCancellationRequested, "a disposed loader must cancel its requests");
    }

    [Fact]
    public void Reset_NewSession_ReportsTheNewGeneration()
    {
        using var fixture = new LoaderFixture();

        long first = fixture.Reset();
        long second = fixture.Reset();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(second, fixture.Generation);
    }

    [Fact]
    public async Task UpdateTeamAsync_UnchangedRoster_DeliversNothingMore()
    {
        using var fixture = new LoaderFixture();
        long generation = fixture.Reset();
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);
        await fixture.StartedAsync(1);
        fixture.Release(1);
        await fixture.RenderedAsync(states => states.All(state => state.Info != null));
        int deliveries = fixture.PostedDeliveries;

        Task<bool> delivered = fixture.WaitForDeliveryAsync(TimeSpan.FromMilliseconds(300));
        await fixture.UpdateAsync(new[] { Player(1) }, ally: true, generation);

        Assert.False(await delivered, "an unchanged roster must not render again");
        Assert.Equal(deliveries, fixture.PostedDeliveries);
        Assert.Equal(1, fixture.Calls(1));
    }

    private static JObject Player(long id, int champion = 0) =>
        new()
        {
            ["summonerId"] = id,
            ["puuid"] = "test-player-" + id,
            ["championId"] = champion,
            ["team"] = 2,
        };

    private static JObject Selected(long id, int champion) =>
        new() { ["puuid"] = "test-player-" + id, ["championId"] = champion };

    private static JObject[] Team(params long[] ids) => ids.Select(id => Player(id, 100 + (int)id)).ToArray();

    private static JObject[] Selections(int from, int count) =>
        Enumerable.Range(from, count).Select(id => Selected(id, 100 + id)).ToArray();

    private static JObject IdentityPlayer(string puuid, long summonerId, int? cell) =>
        cell is null
            ? new JObject { ["puuid"] = puuid, ["summonerId"] = summonerId }
            : new JObject
            {
                ["puuid"] = puuid,
                ["summonerId"] = summonerId,
                ["cellId"] = cell.Value,
            };

    private static JObject GameData(int playersPerTeam, JObject[] teamOne, JObject[] teamTwo, JObject[] selections) =>
        new()
        {
            ["queue"] = new JObject { ["id"] = 2400, ["numPlayersPerTeam"] = playersPerTeam },
            ["teamOne"] = new JArray(teamOne),
            ["teamTwo"] = new JArray(teamTwo),
            ["playerChampionSelections"] = new JArray(selections),
        };

    private static JObject HiddenRoster(bool completeTeamIsOne)
    {
        JObject[] complete = Team(1, 2, 3, 4, 5);
        JObject[] partial = Team(6, 8, 9);
        return GameData(
            playersPerTeam: 5,
            teamOne: completeTeamIsOne ? complete : partial,
            teamTwo: completeTeamIsOne ? partial : complete,
            selections: Selections(1, 10)
        );
    }

    private static PlayerMatchInfo Info(long id) =>
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
            MatchItems = { new ListViewItem(new[] { "", "大乱斗", "3/2/5", "2026-09-17" }) },
        };

    private static PlayerMatchInfo UnavailableInfo(long id) =>
        new()
        {
            Player = new PlayerInfo
            {
                SummonerId = id,
                Puuid = "test-player-" + id,
                GameName = "失败",
            },
        };

    private sealed record Waiter(
        Func<IReadOnlyList<RoomPlayerState>, bool> Match,
        TaskCompletionSource<IReadOnlyList<RoomPlayerState>> Completion
    );

    private sealed record PostedDelivery(Action Action, TaskCompletionSource Done);

    /// <summary>
    /// One loader with a recording render delegate, one gate per player, and a dispatch seam that either runs the
    /// posted action at once or holds it for the test to run, standing in for the UI thread.
    /// </summary>
    private sealed class LoaderFixture : IDisposable
    {
        private readonly RoomPlayerLoader _loader;
        private readonly Func<JObject, CancellationToken, Task<PlayerMatchInfo>> _fetch;
        private readonly bool _ignoreCancellation;
        private readonly ConcurrentDictionary<long, TaskCompletionSource<PlayerMatchInfo>> _gates = new();
        private readonly ConcurrentDictionary<long, TaskCompletionSource> _started = new();
        private readonly ConcurrentDictionary<long, CancellationToken> _tokens = new();
        private readonly ConcurrentDictionary<long, PlayerMatchInfo> _fetched = new();
        private readonly ConcurrentDictionary<long, int> _calls = new();
        private readonly ConcurrentQueue<PostedDelivery> _posted = new();
        private readonly List<Waiter> _waiters = new();
        private Func<Action, Task> _deliver = action =>
        {
            action();
            return Task.CompletedTask;
        };
        private TaskCompletionSource _nextDelivery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RoomPlayerState[] _latest = Array.Empty<RoomPlayerState>();

        public LoaderFixture(
            Func<JObject, CancellationToken, Task<PlayerMatchInfo>>? fetch = null,
            TimeSpan? timeout = null,
            TimeSpan? retryDelay = null,
            bool ignoreCancellation = false
        )
        {
            _ignoreCancellation = ignoreCancellation;
            _fetch = fetch ?? GatedFetch;
            _loader = new RoomPlayerLoader(
                (player, token) => _fetch(player, token),
                action =>
                {
                    TaskCompletionSource signalled = _nextDelivery;
                    _nextDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    signalled.TrySetResult();
                    return _deliver(action);
                },
                OnRender,
                null,
                timeout,
                retryDelay
            );
        }

        public IReadOnlyList<RoomPlayerState> Latest => _latest;

        public int PostedDeliveries => _posted.Count;

        public static string Key(bool ally, long id) => (ally ? "ally/" : "enemy/") + "p:test-player-" + id;

        public long Reset() => _loader.Reset();

        public long Generation => _loader.Generation;

        public Task UpdateAsync(JObject[] players, bool ally, long generation) =>
            _loader.UpdateTeamAsync(new JArray(players), ally, generation);

        public Task RetryAsync(string key, long generation) => _loader.RetryAsync(key, generation);

        public RoomPlayerState State(bool ally, long id) => _latest.Single(state => state.Key == Key(ally, id));

        /// <summary>
        /// Reports whether the loader dispatched one more render inside <paramref name="window"/>. Snapshot the wait
        /// before provoking the work: a test that expects nothing uses it as a bounded proof of absence.
        /// </summary>
        public async Task<bool> WaitForDeliveryAsync(TimeSpan window)
        {
            Task next = _nextDelivery.Task;
            using var deadline = new CancellationTokenSource(window);
            try
            {
                await next.WaitAsync(deadline.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public int Calls(long id) => _calls.TryGetValue(id, out int count) ? count : 0;

        public PlayerMatchInfo Fetched(long id) => _fetched[id];

        public CancellationToken FetchToken(long id) => _tokens[id];

        /// <summary>Stands in for a UI thread that is busy: the render waits until the test runs it.</summary>
        public void QueueDeliveries() =>
            _deliver = action =>
            {
                var delivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _posted.Enqueue(new PostedDelivery(action, delivery));
                return delivery.Task;
            };

        public async Task RunPostedDeliveryAsync()
        {
            bool posted = _posted.TryDequeue(out var delivery);
            Assert.True(posted, "the loader must post its render to the UI thread");
            delivery!.Action();
            delivery.Done.TrySetResult();
            await Bounded(delivery.Done.Task);
        }

        public void Release(long id) => Release(id, Info(id));

        public void Release(long id, PlayerMatchInfo info)
        {
            _fetched[id] = info;
            Gate(id).TrySetResult(info);
        }

        public void Fail(long id, Exception error) => Gate(id).TrySetException(error);

        public Task StartedAsync(params long[] ids) => Bounded(Task.WhenAll(ids.Select(StartedTask)));

        public Task<IReadOnlyList<RoomPlayerState>> RenderedAsync(Func<IReadOnlyList<RoomPlayerState>, bool> match)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<RoomPlayerState>>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lock (_waiters)
            {
                if (match(_latest))
                {
                    completion.SetResult(_latest);
                }
                else
                {
                    _waiters.Add(new Waiter(match, completion));
                }
            }

            return Bounded(completion.Task);
        }

        public void Dispose() => _loader.Dispose();

        private static async Task Bounded(Task task)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await task.WaitAsync(deadline.Token);
        }

        private static async Task<T> Bounded<T>(Task<T> task)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await task.WaitAsync(deadline.Token);
        }

        private async Task<PlayerMatchInfo> GatedFetch(JObject player, CancellationToken token)
        {
            long id = player.Value<long>("summonerId");
            _calls.AddOrUpdate(id, 1, (_, count) => count + 1);
            _tokens[id] = token;
            Started(id).TrySetResult();
            Task<PlayerMatchInfo> result = Gate(id).Task;
            // A loader that ignores cancellation stands in for a resource that does not take a token: the loader's
            // own wait has to protect delivery from its late answer.
            return await (_ignoreCancellation ? result : result.WaitAsync(token));
        }

        private Task StartedTask(long id) => Started(id).Task;

        private void OnRender(IReadOnlyList<RoomPlayerState> states)
        {
            var snapshot = states.ToArray();
            _latest = snapshot;
            Waiter[] matched;
            lock (_waiters)
            {
                matched = _waiters.Where(waiter => waiter.Match(snapshot)).ToArray();
                foreach (var waiter in matched)
                {
                    _waiters.Remove(waiter);
                }
            }

            foreach (var waiter in matched)
            {
                waiter.Completion.TrySetResult(snapshot);
            }
        }

        private TaskCompletionSource<PlayerMatchInfo> Gate(long id) =>
            _gates.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

        private TaskCompletionSource Started(long id) =>
            _started.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
