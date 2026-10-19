using System.Net;
using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The loading-screen identity service: which account ids it accepts, how it publishes full Riot IDs from a stubbed
/// public endpoint, and how repeated overlay polls share one lookup, back off after a failure, keep a request alive
/// when one view is cancelled, and drop the cache when the client reconnects. Every request goes to a stub handler
/// answering from a hand-written profile document, so nothing leaves the process and no test waits on real time.
/// The expected values come from the checks in Tests/StatisticsChecks/LoadingRosterIdentityChecks.cs, which this
/// class replaces.
/// <para>
/// The stubbed profile names are written the same way the service leaves them in the merged list, because
/// <c>LoadingRoster.FromLive</c> only hands an account identity on to a live player whose name it matches exactly and
/// uniquely. A fixture whose profile name differs from the live snapshot's name drops the very identity under test,
/// and a poll whose live list is shorter than <c>roster.Players</c> never reads the selection list at all.
/// </para>
/// </summary>
public class LoadingRosterIdentityServiceTests
{
    private const string NameA = "玩家甲";
    private const string NameB = "玩家乙";
    private const string NameC = "玩家丙";
    private const string SelfPuuid = "a";

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("2400")]
    [InlineData("1")]
    [InlineData("a")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void HasPuuid_NonEmptyValue_IsUsable(string value)
    {
        Assert.True(LoadingRosterIdentityService.HasPuuid(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    [InlineData("(00000000-0000-0000-0000-000000000000)")]
    public void HasPuuid_EmptyValueOrEmptyGuid_IsNotUsable(string value)
    {
        Assert.False(LoadingRosterIdentityService.HasPuuid(value));
    }

    [Fact]
    public void HasPuuid_Nothing_IsNotUsable()
    {
        Assert.False(LoadingRosterIdentityService.HasPuuid(null!));
    }

    [Fact]
    public async Task MergeAsync_LegacyNamesAndSelectionOnlyPlayers_RecoverExactIdentities()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [
                new LoadingPlayer("key-a", "旧名称甲", "a", 1L, 99, 100, true),
                new LoadingPlayer("key-b", "旧名称乙", "b", 2L, 99, 200, false),
            ]
        );

        var merged = await service.MergeAsync(
            roster,
            Flow(new JObject { ["puuid"] = "c", ["championId"] = 99 }),
            SelfPuuid,
            Live(NameA, NameB, NameC),
            _ => 99,
            CancellationToken.None
        );

        // Three lookups: the two legacy accounts plus the selection-only player the live list adds, which the
        // merged list then reports under its live name.
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { "a", "b" }, merged.Players.Take(2).Select(p => p.Puuid));
        Assert.Equal(new[] { 1L, 2L }, merged.Players.Take(2).Select(p => p.SummonerId));
        Assert.Equal(new[] { NameA, NameB }, merged.Players.Take(2).Select(p => p.Name));
        Assert.Equal(200, merged.Players[2].Team);
        Assert.Equal(99, merged.Players[2].ChampionId);
    }

    [Fact]
    public async Task MergeAsync_PlayerWithOnlyASummonerId_ResolvesThroughTheSummonerEndpoint()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster("game-1", 450, "ARAM", [Anonymous()]);

        var merged = await service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameB),
            _ => 99,
            CancellationToken.None
        );

        var player = Assert.Single(merged.Players);
        Assert.Equal("b", player.Puuid);
        Assert.Equal(2L, player.SummonerId);
        Assert.Equal(NameB, player.Name);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_AnAccountMissingFromTheRoster_IsLookedUpFromTheSelectionList()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", NameA, "a", 1L, 99, 100, true)]
        );

        var merged = await service.MergeAsync(
            roster,
            Flow(new JObject { ["puuid"] = "c", ["championId"] = 99 }),
            SelfPuuid,
            Live(NameA, NameB),
            _ => 0,
            CancellationToken.None
        );

        // The live list reports two public players and the session only confirmed one, so the selection list is
        // read and its account is looked up as well; the merged list still reports the live name.
        Assert.Equal(2, handler.Calls);
        Assert.Equal("a", merged.Players[0].Puuid);
        Assert.Equal(1L, merged.Players[0].SummonerId);
        Assert.Equal(NameA, merged.Players[0].Name);
        Assert.Equal(NameB, merged.Players[1].Name);
    }

    [Fact]
    public async Task MergeAsync_RepeatedPolls_ReuseThePublishedIdentityMetadata()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = PoolRoster();
        var flow = Flow(new JObject { ["puuid"] = "c", ["championId"] = 99 });
        var live = Live(NameA, NameB, NameC);

        await service.MergeAsync(roster, flow, SelfPuuid, live, _ => 99, CancellationToken.None);
        await service.MergeAsync(roster, flow, SelfPuuid, live, _ => 99, CancellationToken.None);

        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_TheSameSelectionTwiceInOnePoll_RequestsTheProfileOnce()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var flow = Flow(
            new JObject { ["puuid"] = "c", ["championId"] = 99 },
            new JObject { ["puuid"] = "c", ["championId"] = 43 }
        );

        var merged = await service.MergeAsync(
            PoolRoster(),
            flow,
            SelfPuuid,
            Live(NameA, NameB, NameC),
            _ => 99,
            CancellationToken.None
        );

        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { "a", "b" }, merged.Players.Take(2).Select(p => p.Puuid));
    }

    [Fact]
    public async Task MergeAsync_HiddenPlayer_KeepsTheOtherIdentitiesAndSkipsHiddenProfileLookups()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));

        var merged = await service.MergeAsync(
            PoolRoster(),
            Flow(new JObject { ["puuid"] = "hidden", ["championId"] = 99 }),
            SelfPuuid,
            Live(NameA, NameB, NameC),
            _ => 99,
            CancellationToken.None
        );

        // Three lookups for two accounts plus the selection-only player; the hidden slot costs none and is not a
        // candidate the identity join ever sees.
        Assert.Equal(3, handler.Calls);
        Assert.Equal("a", merged.Players[0].Puuid);
        Assert.Equal("b", merged.Players[1].Puuid);
        Assert.Equal(NameA, merged.Players[0].Name);
        Assert.Equal(NameB, merged.Players[1].Name);
    }

    [Fact]
    public async Task MergeAsync_HiddenSlot_IsHiddenAndInheritsNothing()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));

        var merged = await service.MergeAsync(
            PoolRoster(),
            new JObject(),
            SelfPuuid,
            Live(NameA, NameB, "#"),
            _ => 99,
            CancellationToken.None
        );

        Assert.Equal(2, handler.Calls);
        Assert.Equal("", merged.Players[2].Puuid);
        Assert.Equal(0L, merged.Players[2].SummonerId);
        Assert.True(merged.Players[2].IdentityHidden);
        Assert.Equal("隐藏玩家", merged.Players[2].Name);
    }

    [Fact]
    public async Task MergeAsync_ProfileWithoutAnIdentity_StaysPendingAndInheritsNoChampion()
    {
        var handler = new StubHandler(Summoner) { Status = HttpStatusCode.NotFound };
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", "旧名称甲", "a", 1L, 99, 100, false)]
        );

        var merged = await service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameA),
            _ => 7,
            CancellationToken.None
        );

        var player = Assert.Single(merged.Players);
        Assert.Equal("", player.Puuid);
        Assert.Equal(0L, player.SummonerId);
        Assert.False(player.IdentityHidden);
        Assert.Equal(7, player.ChampionId);
    }

    [Fact]
    public async Task MergeAsync_ANameTheLiveGameDoesNotReport_StaysPendingAndIsNotHidden()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));

        var merged = await service.MergeAsync(
            PoolRoster(),
            new JObject(),
            SelfPuuid,
            Live(NameA, "不匹配的玩家"),
            _ => 99,
            CancellationToken.None
        );

        Assert.Equal("", merged.Players[1].Puuid);
        Assert.False(merged.Players[1].IdentityHidden);
    }

    [Theory]
    [InlineData("zzz", 2L)]
    [InlineData("b", 2L)]
    [InlineData("a", 9L)]
    public async Task MergeAsync_AProfileThatContradictsTheConfirmedAccount_IsRejected(string puuid, long id)
    {
        Func<string, CancellationToken, Task<HttpResponseMessage>> answer = (_, _) =>
            Task.FromResult(Profile(puuid, id, NameA));
        var handler = new StubHandler(answer);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", NameA, "a", 1L, 99, 100, true)]
        );

        var merged = await service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameA),
            _ => 0,
            CancellationToken.None
        );

        Assert.Equal("a", merged.Players[0].Puuid);
        Assert.Equal(1L, merged.Players[0].SummonerId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_ZeroPuuidFlow_KeepsTheIdentityTheSessionAlreadyConfirmed()
    {
        var flow = new JObject
        {
            ["gameData"] = new JObject
            {
                ["teamOne"] = new JArray(new JObject { ["puuid"] = Guid.Empty.ToString(), ["summonerId"] = 1 }),
                ["playerChampionSelections"] = new JArray(
                    new JObject
                    {
                        ["summonerId"] = 1,
                        ["puuid"] = "a",
                        ["championId"] = 99,
                    }
                ),
            },
        };
        var loaded = LoadingRoster.Parse(flow, SelfPuuid);
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));

        var merged = await service.MergeAsync(loaded, flow, SelfPuuid, Live(NameA), _ => 99, CancellationToken.None);

        // The live list reports one public player while the identity the parse recovered carries no name, so the
        // account is confirmed rather than recognised: the lookup matches it by summoner id, not by name.
        Assert.Equal(1, handler.Calls);
        Assert.Equal("a", merged.Players[0].Puuid);
        Assert.Equal(1L, merged.Players[0].SummonerId);
        Assert.Equal(NameA, merged.Players[0].Name);
        Assert.Equal(99, merged.Players[0].ChampionId);
        Assert.True(merged.Players[0].Self);
    }

    [Fact]
    public async Task MergeAsync_TwoSharesOfOneFullName_LeaveNoIdentityToLose()
    {
        Func<string, CancellationToken, Task<HttpResponseMessage>> answer = (_, _) =>
            Task.FromResult(Profile("a", 0, NameA));
        var handler = new StubHandler(answer);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [
                new LoadingPlayer("key-a", "旧名称甲", "a", 1L, 99, 100, true),
                new LoadingPlayer("key-b", "旧名称乙", "b", 2L, 99, 200, false),
            ]
        );

        var merged = await service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameA, NameA),
            _ => 99,
            CancellationToken.None
        );

        // Both accounts resolve to the same full name, so the live list cannot tell them apart and neither
        // inherits an identity: a wrong player must never be handed another player's mastery.
        Assert.Equal(new[] { "", "" }, merged.Players.Select(p => p.Puuid));
        Assert.Equal(new[] { 0L, 0L }, merged.Players.Select(p => p.SummonerId));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_FailedMetadataRequests_BackOffInsteadOfRepeatingEveryPoll()
    {
        var handler = new StubHandler(Summoner) { Status = HttpStatusCode.ServiceUnavailable };
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", NameA, "a", 1L, 99, 100, false)]
        );

        await service.MergeAsync(roster, new JObject(), SelfPuuid, Live(NameA), _ => 99, CancellationToken.None);
        await service.MergeAsync(roster, new JObject(), SelfPuuid, Live(NameA), _ => 99, CancellationToken.None);
        await service.MergeAsync(roster, new JObject(), SelfPuuid, Live(NameA), _ => 99, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_CancelledView_KeepsTheCoalescedLookupForTheOtherView()
    {
        int delayedCalls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage> Slow(string path, CancellationToken token)
        {
            Interlocked.Increment(ref delayedCalls);
            started.TrySetResult();
            return gate.Task;
        }

        var handler = new StubHandler(Slow);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", NameA, "a", 1L, 99, 100, true)]
        );
        using var cancellation = new CancellationTokenSource();

        var cancelled = service.MergeAsync(roster, new JObject(), SelfPuuid, Live(NameA), _ => 99, cancellation.Token);
        var continuing = service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameA),
            _ => 99,
            CancellationToken.None
        );
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await cancellation.CancelAsync();

        // The request is still in flight, so cancelling one view can only abandon that caller's wait.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.TrySetResult(Profile("a", 1, NameA));
        var merged = await continuing;

        Assert.Equal("a", merged.Players[0].Puuid);
        Assert.Equal(1, delayedCalls);
    }

    [Fact]
    public async Task MergeAsync_CancelledView_DoesNotRestartTheRequestForANewCaller()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage> Once(string path, CancellationToken token)
        {
            started.TrySetResult();
            return gate.Task;
        }

        var handler = new StubHandler(Once);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", NameA, "a", 1L, 99, 100, true)]
        );
        using var cancellation = new CancellationTokenSource();

        var cancelled = service.MergeAsync(roster, new JObject(), SelfPuuid, Live(NameA), _ => 99, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.TrySetResult(Profile("a", 1, NameA));

        var merged = await service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameA),
            _ => 99,
            CancellationToken.None
        );

        Assert.Equal("a", merged.Players[0].Puuid);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_PreCancelledToken_DoesNotPublishALateResult()
    {
        var handler = new StubHandler(Summoner);
        var service = new LoadingRosterIdentityService(StubHandler.Counted(handler));
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", NameA, "a", 1L, 99, 100, true)]
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MergeAsync(roster, new JObject(), SelfPuuid, Live(NameA), _ => 99, cancellation.Token)
        );

        // The token is checked after the fan-out, so a pre-cancelled call still performs its one lookup and
        // then reports the cancellation instead of publishing the merged roster.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MergeAsync_TheSamePlayerReachingTwoClients_KeepsTheOneItStartedWith()
    {
        // A reconnect that changes the served client mid-merge cannot be merged with the snapshot: the second
        // client's answers belong to a different session, so the rebuilt roster keeps the live document instead.
        var live = new StubHandler((_, _) => Task.FromResult(Profile("a", 1, NameA)));
        var pool = new StubHandler((_, _) => Task.FromResult(Profile("a", 2, NameB)));
        var liveConnection = live.Client();
        var poolConnection = pool.Client();
        int served = 0;
        var service = new LoadingRosterIdentityService(() => served++ == 0 ? liveConnection : poolConnection);
        var roster = new LoadingRoster(
            "game-1",
            450,
            "ARAM",
            [new LoadingPlayer("key-a", "旧名称甲", "a", 1L, 99, 100, true)]
        );

        var merged = await service.MergeAsync(
            roster,
            new JObject(),
            SelfPuuid,
            Live(NameA),
            _ => 99,
            CancellationToken.None
        );

        Assert.Equal(1, live.Calls);
        Assert.Equal(0, pool.Calls);
        Assert.Equal("live:0", Assert.Single(merged.Players).Key);
        Assert.Equal(NameA, merged.Players[0].Name);
        Assert.True(merged.Players[0].Self);
    }

    private static LoadingPlayer Anonymous() => new("2", "", "", 2L, 0, 0, false);

    private static LoadingRoster PoolRoster() =>
        new(
            "game-1",
            450,
            "ARAM",
            [
                new LoadingPlayer("key-a", "旧名称甲", "a", 1L, 99, 100, true),
                new LoadingPlayer("key-b", "旧名称乙", "b", 2L, 99, 200, false),
            ]
        );

    private static JObject Flow(params JObject[] picks) =>
        new() { ["gameData"] = new JObject { ["playerChampionSelections"] = new JArray(picks) } };

    private static HttpResponseMessage Profile(string puuid, long summonerId, string name) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JObject
                {
                    ["puuid"] = puuid,
                    ["summonerId"] = summonerId,
                    ["displayName"] = name,
                }.ToString()
            ),
        };

    private static Task<HttpResponseMessage> Summoner(string path, CancellationToken token) =>
        Task.FromResult(
            path switch
            {
                "/lol-summoner/v1/summoners/1" => Profile("a", 1, NameA),
                "/lol-summoner/v1/summoners/2" => Profile("b", 2, NameB),
                _ => Profile("c", 3, NameC),
            }
        );

    private static LiveMatchPlayer LivePlayer(string name, int index) =>
        new("live:" + index, name, "Lux", "拉克丝", index == 0 ? "ORDER" : "CHAOS", index == 0, 1, []);

    private static LiveMatchSnapshot Live(params string[] names) =>
        new("ARAM", 12, 1, names.Select((name, i) => LivePlayer(name, i)).ToArray());

    /// <summary>
    /// A client whose handler answers from one document table and counts what it served. It hands out the same client
    /// every call, because the service caches per client instance: a test that simulates a reconnect builds a second
    /// handler, not a second client of this one. No request leaves the process.
    /// </summary>
    private sealed class StubHandler(Func<string, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        private HttpClient? _client;

        public int Calls { get; private set; }

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public static Func<HttpClient?> Counted(StubHandler handler) => handler.Client;

        public HttpClient Client() =>
            _client ??= new HttpClient(this) { BaseAddress = new Uri("https://test.invalid/") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Status == HttpStatusCode.OK
                ? answer(request.RequestUri!.AbsolutePath, token)
                : Task.FromResult(new HttpResponseMessage(Status));
        }
    }
}
