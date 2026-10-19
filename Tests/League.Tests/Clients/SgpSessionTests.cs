using System.Net;
using System.Text;
using League.Clients;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The SGP history session's handshake with the client: the login session, the access token and the service address,
/// including the fallback that reads the address out of the purchase widget. Every answer comes from a stub handler,
/// so no request leaves the process; only handshake responses are served, so the shared SGP transport is never
/// reached and no history is queried. The expectations come from the checks in
/// Tests/StatisticsChecks/SgpSessionChecks.cs, which this class replaces.
/// <para>
/// One case of the old check is not migrated: a query against a client that has an address but no usable token,
/// which the old check builds by writing the private <c>_sgpUrl</c> field with reflection. No public call can leave
/// that state behind, because <c>InitSgpAsync</c> sets the address only after the token was accepted and it never
/// clears the token. A query that gets past the address guard cannot be tested either — it runs on the static shared
/// SGP client, which has no injection seam, so the URL, the retries and the success path would need the real
/// service; the old check does not cover them. The entitlements token is written but read nowhere in the application
/// (requests carry <c>_accessToken</c>), so only the cancellation of that member is observable, and that expectation
/// comes from Tests/StatisticsChecks/RoomPlayerChecks.cs. This class joins the process-wide state collection because
/// it resets one key of <c>AppLog.Shared</c> before reading the two messages the handshake writes.
/// </para>
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class SgpSessionTests
{
    private const string Session = """{"puuid":"p-1","summonerId":"s-1","idToken":"id-token"}""";
    private const string Tokens = """{"accessToken":"access","token":"entitlement"}""";
    private const string Endpoints = """{"PlayerPreferences":{"ServiceEndpoint":"https://sgp.example.com/"}}""";
    private const string NoAddress = """{"PlayerPreferences":{}}""";
    private const string PurchaseWidget =
        """{"LcuPurchaseWidget":{"CapOrdersUrl":"https://cap.example.com/orders/x"}}""";

    // The state key SgpSession writes its initialisation result under.
    private const string InitState = "sgp-init";

    /// <summary>The message the handshake writes when it found an address, and the one it writes when it did not.</summary>
    private const string ReadyMessage = "战绩服务已就绪：https://sgp.example.com";

    private const string NoAddressMessage = "战绩服务初始化失败：未取得服务地址";

    /// <summary>Each handshake below answers one step wrong; the paths are the steps that were reached.</summary>
    public static TheoryData<string, string[]> IncompleteHandshakes =>
        new()
        {
            { "session-missing", ["/lol-login/v1/session"] },
            { "session-without-an-id-token", ["/lol-login/v1/session"] },
            { "token-refused", ["/lol-login/v1/session", "/entitlements/v1/token"] },
            { "token-without-an-access-token", ["/lol-login/v1/session", "/entitlements/v1/token"] },
            {
                "no-address-at-all",
                ["/lol-login/v1/session", "/entitlements/v1/token", "/lol-platform-config/v1/namespaces"]
            },
            {
                "malformed-namespaces",
                ["/lol-login/v1/session", "/entitlements/v1/token", "/lol-platform-config/v1/namespaces"]
            },
        };

    [Fact]
    public void DataScope_NewSession_IsEmpty()
    {
        var session = new SgpSession();

        string scope = session.DataScope;

        Assert.Equal(string.Empty, scope);
    }

    [Fact]
    public async Task InitSgpAsync_AClientAnsweringEveryStep_IsReady()
    {
        using var lcu = Lcu(new Routes(Replies()));
        var session = new SgpSession();

        bool ready = await session.InitSgpAsync(lcu);

        Assert.True(ready, "A client answering the session, token and namespaces initialises the history service");
    }

    [Fact]
    public async Task InitSgpAsync_AnEndpointWithATrailingSlash_KeepsTheAddressWithoutIt()
    {
        using var lcu = Lcu(new Routes(Replies()));
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal("https://sgp.example.com", session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_AnAnsweredHandshake_AsksForTheSessionTheTokenThenTheNamespaces()
    {
        var routes = new Routes(Replies());
        using var lcu = Lcu(routes);
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal(
            ["/lol-login/v1/session", "/entitlements/v1/token", "/lol-platform-config/v1/namespaces"],
            routes.Paths
        );
    }

    [Theory]
    [MemberData(nameof(IncompleteHandshakes))]
    public async Task InitSgpAsync_AnIncompleteHandshake_IsRefusedWhereItStops(string handshake, string[] asked)
    {
        var routes = new Routes(Handshake(handshake));
        using var lcu = Lcu(routes);
        var session = new SgpSession();

        bool ready = await session.InitSgpAsync(lcu);

        Assert.False(ready, "A client that does not answer the whole handshake cannot initialise");
        Assert.Equal(asked, routes.Paths);
    }

    [Fact]
    public async Task InitSgpAsync_NoPreferenceButAPurchaseWidget_FallsBackToTheWidgetHost()
    {
        using var lcu = Lcu(new Routes(Replies(namespaces: PurchaseWidget)));
        var session = new SgpSession();

        bool ready = await session.InitSgpAsync(lcu);

        Assert.True(ready, "A client without the preference still yields an address");
        Assert.Equal("https://cap.example.com", session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_AClientAnsweringEveryStep_LogsTheReadyAddress()
    {
        AppLog.Shared.ResetStates(InitState);
        using var lcu = Lcu(new Routes(Replies()));
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Contains(ReadyMessage, await SharedLogAsync());
    }

    [Fact]
    public async Task InitSgpAsync_WithoutAnyAddress_LogsTheFailure()
    {
        AppLog.Shared.ResetStates(InitState);
        using var lcu = Lcu(new Routes(Replies(namespaces: NoAddress)));
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Contains(NoAddressMessage, await SharedLogAsync());
    }

    [Fact]
    public async Task LoadEntitlementsTokenAsync_ACancelledRefresh_StopsWaitingForTheResponse()
    {
        var routes = new Routes(Replies()) { HoldTheTokenRefresh = true };
        using var lcu = Lcu(routes);
        var session = new SgpSession();
        await session.InitSgpAsync(lcu);
        using var cancel = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task<bool> refresh = session.LoadEntitlementsTokenAsync(cancel.Token);
        await routes.RefreshStarted.WaitAsync(deadline.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Fact]
    public async Task SgpFetchLatestMatches_BeforeInitialisation_ReportsUnavailable()
    {
        var session = new SgpSession();

        JArray? games = await session.SgpFetchLatestMatches("p-1");

        Assert.Null(games);
    }

    [Fact]
    public async Task SgpFetchLatestMatches_AFailedInitialisation_AsksNothingMore()
    {
        var routes = new Routes(Replies(tokenStatus: HttpStatusCode.Unauthorized, token: "{}"));
        using var lcu = Lcu(routes);
        var session = new SgpSession();
        await session.InitSgpAsync(lcu);

        JArray? games = await session.SgpFetchLatestMatches("p-1");

        Assert.Null(games);
        Assert.Equal(["/lol-login/v1/session", "/entitlements/v1/token"], routes.Paths);
    }

    /// <summary>The client answering the handshake; a step that refuses answers with a status and an empty document.</summary>
    private static Func<string, (HttpStatusCode Status, string Body)> Replies(
        string session = Session,
        string token = Tokens,
        string namespaces = Endpoints,
        HttpStatusCode sessionStatus = HttpStatusCode.OK,
        HttpStatusCode tokenStatus = HttpStatusCode.OK
    ) =>
        path =>
            path switch
            {
                "/lol-login/v1/session" => (sessionStatus, session),
                "/entitlements/v1/token" => (tokenStatus, token),
                _ => (HttpStatusCode.OK, namespaces),
            };

    private static Func<string, (HttpStatusCode Status, string Body)> Handshake(string step) =>
        step switch
        {
            "session-missing" => Replies(sessionStatus: HttpStatusCode.NotFound, session: "{}"),
            "session-without-an-id-token" => Replies(session: """{"puuid":"p-1","summonerId":"s-1"}"""),
            "token-refused" => Replies(tokenStatus: HttpStatusCode.NotFound, token: "{}"),
            "token-without-an-access-token" => Replies(token: "{}"),
            "no-address-at-all" => Replies(namespaces: NoAddress),
            "malformed-namespaces" => Replies(namespaces: "not json"),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown handshake"),
        };

    private static HttpClient Lcu(Routes routes) => new(routes) { BaseAddress = new Uri("https://127.0.0.1/") };

    /// <summary>
    /// The shared log once its background writer has caught up. The caller resets the state key first, because a state
    /// is written only when it changed and the message is otherwise left over from an earlier test.
    /// </summary>
    private static async Task<string> SharedLogAsync()
    {
        await AppLog.Shared.FlushAsync();
        return await File.ReadAllTextAsync(AppLog.Shared.CurrentPath);
    }

    /// <summary>The client side of the handshake, answering from one function and remembering the paths asked for.</summary>
    private sealed class Routes(Func<string, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _refreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tokenRequests;

        public List<string> Paths { get; } = [];

        /// <summary>Holds every token request after the first, so a test can cancel one that is in flight.</summary>
        public bool HoldTheTokenRefresh { get; set; }

        public Task RefreshStarted => _refreshStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (HoldTheTokenRefresh && path == "/entitlements/v1/token" && ++_tokenRequests > 1)
            {
                _refreshStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }

            var (status, body) = reply(path);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
