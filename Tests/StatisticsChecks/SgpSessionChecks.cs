using System.Net;
using System.Reflection;
using System.Text;
using League.Clients;
using static StatisticsCheckSupport;

/// <summary>
/// The SGP history session's handshake with the client: the login session, the access token and the service
/// address, including the fallback that reads the address out of the purchase widget. Only handshake responses are
/// served here, so the checks stop before the shared SGP transport is used and no history is queried.
/// </summary>
internal static class SgpSessionChecks
{
    public static async Task RunAsync()
    {
        await InitChecks();
        await AddressChecks();
        await UnavailableChecks();
        Console.WriteLine(
            "PASS: history session login, token and service address, the purchase-widget fallback, and the "
                + "unavailable paths that ask nothing of the server."
        );
    }

    private static async Task InitChecks()
    {
        var routes = new Routes(path =>
            path switch
            {
                "/lol-login/v1/session" => (HttpStatusCode.OK, Session),
                "/entitlements/v1/token" => (HttpStatusCode.OK, Tokens),
                _ => (HttpStatusCode.OK, Namespaces),
            }
        );
        var session = new SgpSession();
        Check(
            await session.InitSgpAsync(Lcu(routes)),
            "A client answering the session, token and namespaces initialises the history service"
        );
        Check(session.DataScope == "https://sgp.example.com", "The service address loses its trailing slash");
        Check(
            routes.Paths.SequenceEqual([
                "/lol-login/v1/session",
                "/entitlements/v1/token",
                "/lol-platform-config/v1/namespaces",
                "/riotclient/region-locale",
                "/lol-client-config/v3/client-config/lol.client_settings",
            ]),
            "Initialisation asks for the session, the token, the namespaces, the region, then the client settings"
        );

        var noSession = new Routes(_ => (HttpStatusCode.NotFound, "{}"));
        Check(
            !await new SgpSession().InitSgpAsync(Lcu(noSession)),
            "A client without a login session cannot initialise"
        );
        Check(noSession.Paths.Count == 1, "Nothing else is asked for once the login session is missing");

        var noIdToken = new Routes(_ => (HttpStatusCode.OK, """{"puuid":"p-1","summonerId":"s-1"}"""));
        Check(
            !await new SgpSession().InitSgpAsync(Lcu(noIdToken)),
            "A login session without an id token cannot initialise"
        );

        var noToken = new Routes(path =>
            path == "/entitlements/v1/token" ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.OK, Session)
        );
        Check(!await new SgpSession().InitSgpAsync(Lcu(noToken)), "A client refusing the token cannot initialise");

        var emptyToken = new Routes(path =>
            path == "/entitlements/v1/token" ? (HttpStatusCode.OK, "{}") : (HttpStatusCode.OK, Session)
        );
        Check(
            !await new SgpSession().InitSgpAsync(Lcu(emptyToken)),
            "A token response without an access token cannot initialise"
        );
    }

    private static async Task AddressChecks()
    {
        var session = new SgpSession();
        var fallback = new Routes(path =>
            path switch
            {
                "/lol-login/v1/session" => (HttpStatusCode.OK, Session),
                "/entitlements/v1/token" => (HttpStatusCode.OK, Tokens),
                _ => (
                    HttpStatusCode.OK,
                    """{"LcuPurchaseWidget":{"CapOrdersUrl":"https://cap.example.com/orders/x"}}"""
                ),
            }
        );
        Check(await session.InitSgpAsync(Lcu(fallback)), "A client without the preference still yields an address");
        Check(
            session.DataScope == "https://cap.example.com",
            "The service address falls back to the host of the purchase widget"
        );

        Check(
            !await new SgpSession().InitSgpAsync(
                Lcu(
                    new Routes(path =>
                        path switch
                        {
                            "/lol-login/v1/session" => (HttpStatusCode.OK, Session),
                            "/entitlements/v1/token" => (HttpStatusCode.OK, Tokens),
                            _ => (HttpStatusCode.OK, """{"PlayerPreferences":{}}"""),
                        }
                    )
                )
            ),
            "A client offering no address at all cannot initialise"
        );

        Check(
            !await new SgpSession().InitSgpAsync(
                Lcu(
                    new Routes(path =>
                        path switch
                        {
                            "/lol-login/v1/session" => (HttpStatusCode.OK, Session),
                            "/entitlements/v1/token" => (HttpStatusCode.OK, Tokens),
                            _ => (HttpStatusCode.OK, "not json"),
                        }
                    )
                )
            ),
            "A malformed namespace document cannot initialise"
        );
    }

    private static async Task UnavailableChecks()
    {
        var refused = new Routes(path =>
            path switch
            {
                "/lol-login/v1/session" => (HttpStatusCode.OK, Session),
                "/entitlements/v1/token" => (HttpStatusCode.Unauthorized, "{}"),
                _ => (HttpStatusCode.OK, "{}"),
            }
        );

        // Initialisation stops at the token, leaving no service address behind.
        var addressless = new SgpSession();
        Check(!await addressless.InitSgpAsync(Lcu(refused)), "A client refusing the token leaves no service address");
        Check(
            await addressless.SgpFetchLatestMatches("p-1") == null,
            "A history query without a service address reports unavailable instead of asking the server"
        );

        // An address without a usable token: the query refreshes once and then gives up.
        var tokenless = new SgpSession();
        await tokenless.InitSgpAsync(Lcu(refused));
        SgpUrl.SetValue(tokenless, "https://sgp.example.com");
        Check(
            await tokenless.SgpFetchLatestMatches("p-1") == null,
            "A history query without an access token refreshes once and then gives up"
        );
        Check(
            refused.Paths.Count(path => path == "/entitlements/v1/token") == 3,
            "The token is asked for once per initialisation and once more before the query is abandoned"
        );
    }

    private static HttpClient Lcu(Routes routes) => new(routes) { BaseAddress = new Uri("https://127.0.0.1/") };

    private static readonly FieldInfo SgpUrl = typeof(SgpSession).GetField(
        "_sgpUrl",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private const string Session = """{"puuid":"p-1","summonerId":"s-1","idToken":"id-token"}""";

    private const string Tokens = """{"accessToken":"access","token":"entitlement"}""";

    private const string Namespaces = """{"PlayerPreferences":{"ServiceEndpoint":"https://sgp.example.com/"}}""";

    /// <summary>The client side of the handshake, answering from one function and remembering the paths asked for.</summary>
    private sealed class Routes(Func<string, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var (status, body) = reply(path);
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
