using System.Net;
using League.Clients;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The statistics requests behind the champion detail and match pages.
///
/// The shape of every answer is a value rather than an exception: the caller shows a message either way, so a failed
/// request and an empty result would look the same in the panel unless the service distinguishes them. It does, and it
/// also distinguishes whose cancellation it was - the caller's own is rethrown so an abandoned page stops being loaded,
/// while the twelve-second timeout comes back as a message, because that one is worth telling the user about.
/// </summary>
public class PlayerStatisticsServiceTests
{
    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://lcu.invalid") };

    // ---- no client at all ----

    [Fact]
    public async Task GetAsync_NoClient_SaysToLogIn()
    {
        // The client is behind a factory because it comes and goes; this is the state before it connects.
        StatisticsResponse response = await new PlayerStatisticsService(() => null).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Null(response.Data);
        Assert.Equal("客户端尚未连接，请登录后重试。", response.Message);
        Assert.Equal(0, response.Status);
    }

    // ---- a good answer ----

    [Fact]
    public async Task GetAsync_AJsonBody_IsHandedBackWithNoMessage()
    {
        Answer handler = new(HttpStatusCode.OK, """{"games": 12}""");

        StatisticsResponse response = await new PlayerStatisticsService(() => Client(handler)).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.NotNull(response.Data);
        Assert.Equal(12, (int?)response.Data!["games"]);
        Assert.Equal("", response.Message);
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task GetAsync_AList_IsValidToo()
    {
        // The service does not insist on an object; only an object carrying an error code is refused.
        Answer handler = new(HttpStatusCode.OK, "[1, 2, 3]");

        StatisticsResponse response = await new PlayerStatisticsService(() => Client(handler)).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Equal(JTokenType.Array, response.Data!.Type);
    }

    [Fact]
    public async Task GetAsync_ThePathItWasGiven_IsThePathItAsksFor()
    {
        Answer handler = new(HttpStatusCode.OK, "{}");

        await new PlayerStatisticsService(() => Client(handler)).GetAsync("/lol/summoner/v4/x", CancellationToken.None);

        Assert.EndsWith("/lol/summoner/v4/x", Assert.Single(handler.Requests).ToString(), StringComparison.Ordinal);
    }

    // ---- statuses ----

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401, "客户端登录已失效，请重新连接。")]
    [InlineData(HttpStatusCode.Forbidden, 403, "当前玩家资料不允许访问。")]
    [InlineData(HttpStatusCode.NotFound, 404, "客户端暂未提供这项数据。")]
    public async Task GetAsync_AKnownStatus_ExplainsIt(HttpStatusCode status, int code, string message)
    {
        // The three the client actually sends get their own wording; anything else is reported with its number.
        Answer handler = new(status, "");

        StatisticsResponse response = await new PlayerStatisticsService(() => Client(handler)).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Null(response.Data);
        Assert.Equal(message, response.Message);
        Assert.Equal(code, response.Status);
    }

    [Fact]
    public async Task GetAsync_AnotherStatus_IsReportedWithItsNumber()
    {
        Answer handler = new(HttpStatusCode.InternalServerError, "");

        StatisticsResponse response = await new PlayerStatisticsService(() => Client(handler)).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Equal("数据暂时不可用（HTTP 500），可以重试。", response.Message);
        Assert.Equal(500, response.Status);
    }

    // ---- a body that is not statistics ----

    [Fact]
    public async Task GetAsync_AnErrorBody_IsNotHandedBackAsData()
    {
        // The client answers 200 with an error object; showing that as statistics would be worse than showing nothing.
        Answer handler = new(HttpStatusCode.OK, """{"errorCode": "RESOURCE_NOT_FOUND", "message": "nope"}""");

        StatisticsResponse response = await new PlayerStatisticsService(() => Client(handler)).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Null(response.Data);
        Assert.Equal("客户端未返回有效统计数据。", response.Message);
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task GetAsync_ABodyThatIsNotJson_IsReportedAsAFailureToLoad()
    {
        Answer handler = new(HttpStatusCode.OK, "not json at all");

        StatisticsResponse response = await new PlayerStatisticsService(() => Client(handler)).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Null(response.Data);
        Assert.Equal("数据加载失败，请确认客户端连接后重试。", response.Message);
    }

    // ---- the caller's own cancellation ----

    [Fact]
    public async Task GetAsync_TheCallerCancelled_Rethrows()
    {
        // An abandoned page must stop being loaded, not turn into a message nobody will read.
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PlayerStatisticsService(() => Client(new Answer(HttpStatusCode.OK, "{}"))).GetAsync(
                "/x",
                cancelled.Token
            )
        );
    }

    [Fact]
    public async Task GetAsync_TheClientThrows_IsReportedAsAFailureToLoad()
    {
        // A connection that breaks mid-request is the ordinary case this wording is for - and it is caught, so the
        // page shows a message rather than an unhandled exception.
        StatisticsResponse response = await new PlayerStatisticsService(() => Client(new Throwing())).GetAsync(
            "/x",
            CancellationToken.None
        );

        Assert.Null(response.Data);
        Assert.Equal("数据加载失败，请确认客户端连接后重试。", response.Message);
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("连接已断开");
    }
}
