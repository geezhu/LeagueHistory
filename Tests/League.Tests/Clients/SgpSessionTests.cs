using System.Net;
using System.Text;
using League.Clients;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The SGP history session's handshake with the client: the login session, the access token and the service address,
/// including the fallback that reads the address out of the purchase widget, the region table for Riot's servers and
/// the client's player platform edge. Every answer comes from a stub handler, so no request leaves the process; the
/// queries that need the history service get a stub through the session's client parameter. The handshake
/// expectations come from the checks in Tests/StatisticsChecks/SgpSessionChecks.cs, which this class replaces.
/// <para>
/// One case of the old check is not migrated: a query against a client that has an address but no usable token,
/// which the old check builds by writing the private <c>_sgpUrl</c> field with reflection. No public call can leave
/// that state behind, because <c>InitSgpAsync</c> sets the address only after the token was accepted and it never
/// clears the token. The entitlements token is written but read nowhere in the application
/// (requests carry <c>_accessToken</c>), so only the cancellation of that member is observable, and that expectation
/// comes from Tests/StatisticsChecks/RoomPlayerChecks.cs. This class joins the process-wide state collection because
/// it resets one key of <c>AppLog.Shared</c> before reading the two messages the handshake writes.
/// </para>
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class SgpSessionTests
{
    // History requests in these tests go to SgpRoutes through SgpSession's client seam, never to a real host.
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
            { "no-address-at-all", [.. Handshaken, .. RiotSteps] },
            { "malformed-namespaces", [.. Handshaken, .. RiotSteps] },
        };

    // Every step up to the platform configuration, which a complete handshake always reaches.
    private static readonly string[] Handshaken =
    [
        "/lol-login/v1/session",
        "/entitlements/v1/token",
        "/lol-platform-config/v1/namespaces",
    ];

    private const string RegionPath = "/riotclient/region-locale";
    private const string ClientSettingsPath = "/lol-client-config/v3/client-config/lol.client_settings";

    // What a client that is not on a Tencent address is asked next: its region, then its own settings.
    private static readonly string[] RiotSteps = [RegionPath, ClientSettingsPath];

    private const string NorthEastAsia = "https://apne1-red.pp.sgp.pvp.net";

    // A platform edge that differs from the table's host, as after Riot moved a region's history.
    private const string MovedSettings = """{"player_platform_edge":{"url":"https://moved.example.com"}}""";

    // What the two kinds of client configure: Tencent names its history service, Riot its preferences service.
    private const string TencentService =
        """{"PlayerPreferences":{"ServiceEndpoint":"https://cq100-sgp.lol.qq.com:21019"}}""";
    private const string PreferencesService =
        """{"PlayerPreferences":{"ServiceEndpoint":"https://player-preferences-usw2.pp.sgp.pvp.net"}}""";

    // A Japanese client's settings, as read from a live one: history on the platform edge, the region on the league edge.
    private const string JapaneseSettings = """
        {"player_platform_edge":{"url":"https://apne1-red.pp.sgp.pvp.net"},
         "league_edge":{"url":"https://jp-red.lol.sgp.pvp.net"}}
        """;

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
    public async Task InitSgpAsync_AnAnsweredHandshake_AsksForTheSessionTheTokenTheNamespacesThenTheSettings()
    {
        var routes = new Routes(Replies());
        using var lcu = Lcu(routes);
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal([.. Handshaken, .. RiotSteps], routes.Paths);
    }

    [Fact]
    public async Task InitSgpAsync_RegionInTheTable_UsesTheTableHost()
    {
        using var lcu = Lcu(
            new Routes(Replies(namespaces: PreferencesService, region: Region("JP"), settings: MovedSettings))
        );
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.StartsWith(NorthEastAsia + "|", session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_RegionInTheTableWithoutSettings_ScopesTheSharedHostByRegion()
    {
        using var lcu = Lcu(new Routes(Replies(namespaces: PreferencesService, region: Region("KR"))));
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal(NorthEastAsia + "|KR", session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_RegionOutsideTheTable_UsesThePlatformEdge()
    {
        using var lcu = Lcu(
            new Routes(Replies(namespaces: PreferencesService, region: Region("EUN1"), settings: MovedSettings))
        );
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal("https://moved.example.com", session.DataScope);
    }

    [Fact]
    public async Task SgpFetchLatestMatches_TableHostRefuses_SwitchesToThePlatformEdge()
    {
        var sgp = new SgpRoutes(host => host == "moved.example.com" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        using var history = new HttpClient(sgp);
        using var lcu = Lcu(
            new Routes(Replies(namespaces: PreferencesService, region: Region("JP"), settings: MovedSettings))
        );
        var session = new SgpSession(history);
        await session.InitSgpAsync(lcu);

        JArray? games = await session.SgpFetchLatestMatches("p-1");

        Assert.NotNull(games);
        Assert.Equal(["apne1-red.pp.sgp.pvp.net", "moved.example.com"], sgp.Hosts);
    }

    [Fact]
    public async Task SgpFetchLatestMatches_TableHostRefuses_LogsTheSwitch()
    {
        AppLog.Shared.ResetStates(InitState);
        var sgp = new SgpRoutes(host => host == "moved.example.com" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        using var history = new HttpClient(sgp);
        using var lcu = Lcu(
            new Routes(Replies(namespaces: PreferencesService, region: Region("JP"), settings: MovedSettings))
        );
        var session = new SgpSession(history);
        await session.InitSgpAsync(lcu);

        await session.SgpFetchLatestMatches("p-1");

        Assert.Contains("改用客户端配置的地址：https://moved.example.com", await SharedLogAsync());
    }

    [Fact]
    public async Task SgpFetchLatestMatches_TableHostAnswers_StaysOnTheTableHost()
    {
        var sgp = new SgpRoutes(_ => HttpStatusCode.OK);
        using var history = new HttpClient(sgp);
        using var lcu = Lcu(
            new Routes(Replies(namespaces: PreferencesService, region: Region("JP"), settings: MovedSettings))
        );
        var session = new SgpSession(history);
        await session.InitSgpAsync(lcu);

        await session.SgpFetchLatestMatches("p-1");

        Assert.Equal(["apne1-red.pp.sgp.pvp.net"], sgp.Hosts);
    }

    [Fact]
    public async Task SgpFetchLatestMatches_SwitchedToThePlatformEdge_KeepsTheCacheScope()
    {
        var sgp = new SgpRoutes(host => host == "moved.example.com" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        using var history = new HttpClient(sgp);
        using var lcu = Lcu(
            new Routes(Replies(namespaces: PreferencesService, region: Region("JP"), settings: MovedSettings))
        );
        var session = new SgpSession(history);
        await session.InitSgpAsync(lcu);
        string before = session.DataScope;

        await session.SgpFetchLatestMatches("p-1");

        Assert.Equal(before, session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_TencentAddress_DoesNotAskForTheClientSettings()
    {
        var routes = new Routes(Replies(namespaces: TencentService, settings: JapaneseSettings));
        using var lcu = Lcu(routes);
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal(Handshaken, routes.Paths);
    }

    [Fact]
    public async Task InitSgpAsync_TencentAddress_KeepsTheConfiguredAddress()
    {
        using var lcu = Lcu(new Routes(Replies(namespaces: TencentService, settings: JapaneseSettings)));
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Equal("https://cq100-sgp.lol.qq.com:21019", session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_JapaneseClient_UsesThePlatformEdgeInsteadOfThePreferencesService()
    {
        using var lcu = Lcu(new Routes(Replies(namespaces: PreferencesService, settings: JapaneseSettings)));
        var session = new SgpSession();

        bool ready = await session.InitSgpAsync(lcu);

        Assert.True(ready, "A Japanese client initialises on its platform edge");
        Assert.Equal("https://apne1-red.pp.sgp.pvp.net|https://jp-red.lol.sgp.pvp.net", session.DataScope);
    }

    [Fact]
    public async Task InitSgpAsync_JapaneseClient_LogsThePlatformEdge()
    {
        AppLog.Shared.ResetStates(InitState);
        using var lcu = Lcu(new Routes(Replies(namespaces: PreferencesService, settings: JapaneseSettings)));
        var session = new SgpSession();

        await session.InitSgpAsync(lcu);

        Assert.Contains("战绩服务已就绪：https://apne1-red.pp.sgp.pvp.net", await SharedLogAsync());
    }

    [Fact]
    public async Task InitSgpAsync_MalformedClientSettings_KeepsTheConfiguredAddress()
    {
        using var lcu = Lcu(new Routes(Replies(settings: "not json")));
        var session = new SgpSession();

        bool ready = await session.InitSgpAsync(lcu);

        Assert.True(ready, "Unreadable client settings leave the configured address");
        Assert.Equal("https://sgp.example.com", session.DataScope);
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
    /// <remarks>Without settings the client answers 404 for them, as one that has no platform edge.</remarks>
    private static Func<string, (HttpStatusCode Status, string Body)> Replies(
        string session = Session,
        string token = Tokens,
        string namespaces = Endpoints,
        string? settings = null,
        string? region = null,
        HttpStatusCode sessionStatus = HttpStatusCode.OK,
        HttpStatusCode tokenStatus = HttpStatusCode.OK
    ) =>
        path =>
            path switch
            {
                "/lol-login/v1/session" => (sessionStatus, session),
                "/entitlements/v1/token" => (tokenStatus, token),
                ClientSettingsPath => settings == null
                    ? (HttpStatusCode.NotFound, "{}")
                    : (HttpStatusCode.OK, settings),
                RegionPath => region == null ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.OK, region),
                _ => (HttpStatusCode.OK, namespaces),
            };

    private static string Region(string region) => $$"""{"locale":"en_US","region":"{{region}}"}""";

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

    /// <summary>The history service: answers each request by its host with an empty history or a bare status.</summary>
    private sealed class SgpRoutes(Func<string, HttpStatusCode> status) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string host = request.RequestUri!.Host;
            Hosts.Add(host);
            return Task.FromResult(
                new HttpResponseMessage(status(host))
                {
                    Content = new StringContent("""{"games":[]}""", Encoding.UTF8, "application/json"),
                }
            );
        }
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
