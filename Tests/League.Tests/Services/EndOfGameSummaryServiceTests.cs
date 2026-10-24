using System.Net;
using System.Text;
using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Reading the finished game from the end-of-game stats block, and sending the panel's line to the post-game chat
/// and nowhere else. Every request goes to a stub client on a fake host.
/// </summary>
public class EndOfGameSummaryServiceTests
{
    private const int MayhemQueue = 2400;
    private const int RankedQueue = 420;
    private const string PostGame = """[{"id":"post-1","type":"postGame"}]""";

    [Fact]
    public void Parse_WonGame_IsAWin()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Build(), RankedQueue);

        Assert.True(summary!.Win);
    }

    [Fact]
    public void Parse_LostGame_IsALoss()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Lost().Build(), RankedQueue);

        Assert.False(summary!.Win);
    }

    [Fact]
    public void Parse_Teammates_AreTheOwnTeamWithoutTheLocalPlayer()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Build(), RankedQueue);

        Assert.Equal(["队友1", "队友2", "队友3", "队友4"], summary!.Teammates.Select(t => t.Name));
    }

    [Fact]
    public void Parse_LocalPlayer_GivesNameChampionKdaLengthAndGame()
    {
        var summary = EndOfGameSummaryService.Parse(
            new EndOfGameBlockBuilder().WithGameLength(965).Build(),
            RankedQueue
        );

        Assert.Equal("本人", summary!.PlayerName);
        Assert.Equal(1, summary.ChampionId);
        Assert.Equal(new EndOfGameKda(1, 10, 0), summary.Kda);
        Assert.Equal(TimeSpan.FromSeconds(965), summary.Duration);
        Assert.Equal(EndOfGameBlockBuilder.GameId, summary.GameId);
    }

    [Fact]
    public void Parse_TeammateKda_ComesFromItsStats()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Build(), RankedQueue);

        Assert.Equal("2/9/2", summary!.Teammates[0].Kda.ToString());
    }

    [Fact]
    public void Parse_HiddenTeammate_HasNoName()
    {
        var block = new EndOfGameBlockBuilder().WithTeammateName(2, null).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.False(summary!.Teammates[1].CanBeNamed);
    }

    [Fact]
    public void Parse_NoLocalPlayer_IsNull()
    {
        var block = new EndOfGameBlockBuilder().Build();
        block.Remove("localPlayer");

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.Null(summary);
    }

    [Fact]
    public void Parse_UnmodelledQueue_HasNoScores()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Build(), RankedQueue);

        Assert.Null(summary!.Score);
        Assert.All(summary.Teammates, teammate => Assert.Null(teammate.Score));
    }

    [Fact]
    public void Parse_MayhemQueueFromTheCaller_ScoresEveryone()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Build(), MayhemQueue);

        Assert.NotNull(summary!.Score);
        Assert.All(summary.Teammates, teammate => Assert.NotNull(teammate.Score));
    }

    [Fact]
    public void Parse_QueueInTheBlock_WinsOverTheCaller()
    {
        var block = new EndOfGameBlockBuilder().WithQueue(MayhemQueue).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.NotNull(summary!.Score);
    }

    [Fact]
    public void Parse_WonMayhemGame_GivesTheOwnTeamOneMvp()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Build(), MayhemQueue);

        var honours = summary!.Teammates.Select(t => t.Score!.Value.Honour).Append(summary.Score!.Value.Honour);
        Assert.Single(honours, honour => honour == MatchHonour.Mvp);
    }

    [Fact]
    public void Parse_LostMayhemGame_GivesTheOwnTeamOneSvp()
    {
        var summary = EndOfGameSummaryService.Parse(new EndOfGameBlockBuilder().Lost().Build(), MayhemQueue);

        var honours = summary!.Teammates.Select(t => t.Score!.Value.Honour).Append(summary.Score!.Value.Honour);
        Assert.Single(honours, honour => honour == MatchHonour.Svp);
    }

    [Fact]
    public void Parse_MissingScoreStat_HasNoScoresRatherThanZeros()
    {
        var block = new EndOfGameBlockBuilder().WithoutStat("TIME_CCING_OTHERS").Build();

        var summary = EndOfGameSummaryService.Parse(block, MayhemQueue);

        Assert.Null(summary!.Score);
    }

    [Fact]
    public void Parse_TripleKill_GivesThatTeammateTheTripleIcon()
    {
        var block = new EndOfGameBlockBuilder().WithStat(2, "TRIPLE_KILLS", 1).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.Contains("triple", summary!.Teammates[1].Honors);
    }

    [Fact]
    public void Parse_TripleKillUnderTheMatchRecordName_GivesTheTripleIcon()
    {
        // The live client sends multi-kills only as match record names, not as TRIPLE_KILLS.
        var block = new EndOfGameBlockBuilder().WithoutStat("TRIPLE_KILLS").WithStat(2, "tripleKills", 1).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.Contains("triple", summary!.Teammates[1].Honors);
    }

    [Fact]
    public void Parse_QuadraKillUnderTheMatchRecordName_ShowsOnlyTheQuadraIcon()
    {
        var block = new EndOfGameBlockBuilder().WithStat(4, "quadraKills", 1).WithStat(4, "tripleKills", 2).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.Contains("quadra", summary!.Teammates[3].Honors);
        Assert.DoesNotContain("triple", summary.Teammates[3].Honors);
    }

    [Fact]
    public void Parse_MostKillsOfTheGame_GoesToThatPlayerOnly()
    {
        var block = new EndOfGameBlockBuilder().WithStat(3, "CHAMPIONS_KILLED", 40).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.Contains("kills", summary!.Teammates[2].Honors);
        Assert.DoesNotContain("kills", summary.Teammates[0].Honors);
    }

    [Fact]
    public void Parse_EightKillSpree_IsGodlike()
    {
        var block = new EndOfGameBlockBuilder().WithStat(0, "LARGEST_KILLING_SPREE", 8).Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.Contains("godlike", summary!.Honors);
    }

    [Fact]
    public void Parse_StatMissingFromSomePlayers_AwardsNoMaximumForIt()
    {
        var block = new EndOfGameBlockBuilder().WithoutStat("ASSISTS").Build();

        var summary = EndOfGameSummaryService.Parse(block, RankedQueue);

        Assert.All(summary!.Teammates, teammate => Assert.DoesNotContain("assists", teammate.Honors));
    }

    [Fact]
    public void Parse_TeammateWithTheMvp_ComesFirst()
    {
        // Teammate 2 outplays everyone, so the estimate's MVP is that player.
        var block = new EndOfGameBlockBuilder()
            .WithStat(2, "CHAMPIONS_KILLED", 40)
            .WithStat(2, "GOLD_EARNED", 30000)
            .WithStat(2, "TOTAL_DAMAGE_DEALT_TO_CHAMPIONS", 90000)
            .WithStat(2, "NUM_DEATHS", 0)
            .Build();

        var summary = EndOfGameSummaryService.Parse(block, MayhemQueue);

        Assert.Equal("队友2", summary!.Teammates[0].Name);
        Assert.Equal(MatchHonour.Mvp, summary.Teammates[0].Score!.Value.Honour);
        Assert.Equal(["队友1", "队友3", "队友4"], summary.Teammates.Skip(1).Select(t => t.Name));
    }

    [Fact]
    public void Parse_LocalPlayerWithTheMvp_KeepsTheClientOrder()
    {
        var block = new EndOfGameBlockBuilder()
            .WithStat(0, "CHAMPIONS_KILLED", 40)
            .WithStat(0, "GOLD_EARNED", 30000)
            .WithStat(0, "TOTAL_DAMAGE_DEALT_TO_CHAMPIONS", 90000)
            .WithStat(0, "NUM_DEATHS", 0)
            .Build();

        var summary = EndOfGameSummaryService.Parse(block, MayhemQueue);

        Assert.Equal(MatchHonour.Mvp, summary!.Score!.Value.Honour);
        Assert.Equal(["队友1", "队友2", "队友3", "队友4"], summary.Teammates.Select(t => t.Name));
    }

    [Fact]
    public async Task GetAsync_StatsBlockReady_ReadsTheGame()
    {
        var handler = new ChatStub { StatsBlock = new EndOfGameBlockBuilder().Build() };
        var service = new EndOfGameSummaryService(() => handler.Client());

        var summary = await service.GetAsync(RankedQueue, CancellationToken.None);

        Assert.Equal(4, summary!.Teammates.Count);
    }

    [Fact]
    public async Task GetAsync_StatsBlockNotReady_IsNull()
    {
        var handler = new ChatStub();
        var service = new EndOfGameSummaryService(() => handler.Client());

        var summary = await service.GetAsync(RankedQueue, CancellationToken.None);

        Assert.Null(summary);
    }

    [Fact]
    public async Task GetAsync_NoClient_IsNull()
    {
        var service = new EndOfGameSummaryService(() => null);

        var summary = await service.GetAsync(RankedQueue, CancellationToken.None);

        Assert.Null(summary);
    }

    [Fact]
    public async Task SendAsync_PostGameLobby_PostsTheLineToIt()
    {
        var handler = new ChatStub { Conversations = [PostGame] };
        var service = Sender(handler);

        var result = await service.SendAsync(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            CancellationToken.None
        );

        Assert.Equal(PostGameSendResult.Sent, result);
        Assert.Equal("/lol-chat/v1/conversations/post-1/messages", handler.PostedPath);
        Assert.Equal(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            (string?)handler.PostedBody?["body"]
        );
    }

    [Fact]
    public async Task SendAsync_OnlyAChampSelectLobby_SendsNothing()
    {
        // The name would satisfy ChatService's fallback; the post-game sender matches the type only.
        var handler = new ChatStub
        {
            Conversations = ["""[{"id":"cs-1","type":"championSelect","name":"召唤师峡谷 匹配"}]"""],
        };
        var service = Sender(handler);

        var result = await service.SendAsync(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            CancellationToken.None
        );

        Assert.Equal(PostGameSendResult.NoConversation, result);
        Assert.Null(handler.PostedPath);
    }

    [Fact]
    public async Task SendAsync_ChatRejectsTheLine_IsFailed()
    {
        var handler = new ChatStub { Conversations = [PostGame], PostStatus = HttpStatusCode.BadRequest };
        var service = Sender(handler);

        var result = await service.SendAsync(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            CancellationToken.None
        );

        Assert.Equal(PostGameSendResult.Failed, result);
    }

    [Fact]
    public async Task SendAsync_Cancelled_Throws()
    {
        var handler = new ChatStub { Conversations = [PostGame] };
        var service = Sender(handler);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendAsync("和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory", cancelled.Token)
        );

        Assert.Null(handler.PostedPath);
    }

    [Fact]
    public async Task SendAsync_ChatOpensAfterTheHonorScreen_WaitsAndSends()
    {
        var handler = new ChatStub { Conversations = ["[]", "[]", PostGame] };
        var delays = new List<TimeSpan>();
        var service = Sender(handler, delays);

        var result = await service.SendAsync(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            CancellationToken.None
        );

        Assert.Equal(PostGameSendResult.Sent, result);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task SendAsync_ChatNeverOpens_GivesUpAfterTheWait()
    {
        var handler = new ChatStub();
        var delays = new List<TimeSpan>();
        var service = Sender(handler, delays, wait: TimeSpan.FromSeconds(3));

        var result = await service.SendAsync(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            CancellationToken.None
        );

        Assert.Equal(PostGameSendResult.NoConversation, result);
        Assert.Equal(3, delays.Count);
        Assert.Null(handler.PostedPath);
    }

    [Fact]
    public async Task SendAsync_ConversationListRefused_FailsWithoutWaiting()
    {
        var handler = new ChatStub { ListStatus = HttpStatusCode.InternalServerError };
        var delays = new List<TimeSpan>();
        var service = Sender(handler, delays);

        var result = await service.SendAsync(
            "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
            CancellationToken.None
        );

        Assert.Equal(PostGameSendResult.Failed, result);
        Assert.Empty(delays);
    }

    // Waits are recorded instead of slept, so a test that waits for the chat finishes at once.
    private static EndOfGameSummaryService Sender(
        ChatStub handler,
        List<TimeSpan>? delays = null,
        TimeSpan? wait = null
    ) =>
        new(
            () => handler.Client(),
            wait ?? TimeSpan.FromSeconds(90),
            (delay, _) =>
            {
                delays?.Add(delay);
                return Task.CompletedTask;
            }
        );

    /// <summary>The two chat routes and the stats block; anything else is not found.</summary>
    private sealed class ChatStub : HttpMessageHandler
    {
        public JObject? StatsBlock { get; init; }

        /// <summary>One conversation list per request, the last repeated: a chat that opens after a few looks.</summary>
        public string[] Conversations { get; init; } = ["[]"];
        public HttpStatusCode ListStatus { get; init; } = HttpStatusCode.OK;
        private int _lists;
        public HttpStatusCode PostStatus { get; init; } = HttpStatusCode.OK;
        public string? PostedPath { get; private set; }
        public JObject? PostedBody { get; private set; }

        public HttpClient Client() => new(this, disposeHandler: false) { BaseAddress = new Uri("https://lcu.test/") };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                PostedPath = path;
                PostedBody = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
                return Reply(PostStatus, "{}");
            }

            return path switch
            {
                "/lol-end-of-game/v1/eog-stats-block" when StatsBlock != null => Reply(
                    HttpStatusCode.OK,
                    StatsBlock.ToString()
                ),
                "/lol-chat/v1/conversations" => Reply(
                    ListStatus,
                    Conversations[Math.Min(_lists++, Conversations.Length - 1)]
                ),
                _ => Reply(HttpStatusCode.NotFound, "{}"),
            };
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
