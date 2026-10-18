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

    [Fact]
    public void Normalize_RepeatedKnownIdentity_CapsTheTeamAtFiveDistinctSlots()
    {
        var roster = new JArray(Player(1), Player(1), Player(2), Player(3), Player(4), Player(5), Player(6));

        JArray normalized = RoomPlayerLoader.Normalize(roster);

        Assert.Equal(5, normalized.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, normalized.Select(player => player.Value<long>("summonerId")));
    }

    [Fact]
    public void Normalize_HiddenPlayersWithZeroIdentities_KeepsFiveDistinctSlots()
    {
        JObject[] hidden = Enumerable
            .Range(0, 5)
            .Select(cell => new JObject
            {
                ["summonerId"] = 0,
                ["puuid"] = "OBFUSCATED_" + cell,
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
        private RoomPlayerState[] _latest = Array.Empty<RoomPlayerState>();

        public LoaderFixture(
            Func<JObject, CancellationToken, Task<PlayerMatchInfo>>? fetch = null,
            TimeSpan? timeout = null,
            TimeSpan? retryDelay = null
        )
        {
            _fetch = fetch ?? GatedFetch;
            _loader = new RoomPlayerLoader(
                (player, token) => _fetch(player, token),
                action => _deliver(action),
                OnRender,
                null,
                timeout,
                retryDelay
            );
        }

        public IReadOnlyList<RoomPlayerState> Latest => _latest;

        public int PostedDeliveries => _posted.Count;

        public static string Key(long id) => "ally/p:test-player-" + id;

        public long Reset() => _loader.Reset();

        public Task UpdateAsync(JObject[] players, bool ally, long generation) =>
            _loader.UpdateTeamAsync(new JArray(players), ally, generation);

        public Task RetryAsync(string key, long generation) => _loader.RetryAsync(key, generation);

        public RoomPlayerState State(long id) => _latest.Single(state => state.Key == Key(id));

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
            return await Gate(id).Task.WaitAsync(token);
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
