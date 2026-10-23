using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text.RegularExpressions;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// SGP 会话：复用 HttpClient + 全局限流，避免游戏内 10 人卡片把国服 SGP 打爆
    /// </summary>
    public class SgpSession
    {
        private HttpClient _lcuClient;
        private string _accessToken;
        private string _entitlementsToken;
        private string _puuid;
        private string _summonerId;

        // Read by concurrent queries; one of them may replace a refused table host with the client's own.
        private volatile string _sgpUrl;

        // The client's player platform edge while SgpServers' host is in use and differs from it; taken once, by the
        // first query the table's host refuses.
        private string _clientHistoryUrl;

        // Public service addresses only; used to separate local caches across platforms. Fixed at initialisation, so
        // switching to the client's host does not move the caches. Several Riot regions share one history host
        // (Japan and Korea both use apne1), so theirs also names the region.
        private string _dataScope = "";

        public string DataScope => _sgpUrl == null ? "" : _dataScope;

        private string _leagueSessionToken;

        // 全局复用一个 SGP HttpClient（线程安全）
        private static readonly HttpClient _sharedSgpClient = CreateSharedSgpClient();

        private readonly HttpClient _sgpClient;

        public SgpSession()
            : this(null) { }

        /// <param name="sgpClient">Sends the history requests; the shared client unless a test answers them.</param>
        public SgpSession(HttpClient sgpClient)
        {
            _sgpClient = sgpClient ?? _sharedSgpClient;
        }

        // 国服 SGP 对并发敏感：最多同时 2 个请求
        private static readonly SemaphoreSlim _sgpLimiter = new(2, 2);

        // 单次请求超时（秒）—— 比原来的 30 更合理，失败快速重试
        private const int RequestTimeoutSeconds = 12;

        private static HttpClient CreateSharedSgpClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 8,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },
            };

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds + 2) };

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "LeagueOfLegendsClient/14.13.596.7996 (rcp-be-lol-match-history)"
            );
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Riot-ClientPlatform", "Windows");

            return client;
        }

        public async Task<bool> InitSgpAsync(HttpClient lcu)
        {
            _lcuClient = lcu;
            if (!await LoadSessionAsync())
            {
                LogInitState("战绩服务初始化未完成：登录会话尚未就绪");
                return false;
            }

            if (!await LoadEntitlementsTokenAsync())
            {
                LogInitState("战绩服务初始化未完成：未取得访问令牌");
                return false;
            }

            if (string.IsNullOrEmpty(_puuid) || string.IsNullOrEmpty(_accessToken))
            {
                LogInitState("战绩服务初始化未完成：账号信息不完整");
                return false;
            }

            _sgpUrl = await LoadSgpEndpointAsync();
            // The endpoint is the public regional service address, useful for telling servers apart.
            LogInitState(
                string.IsNullOrEmpty(_sgpUrl) ? "战绩服务初始化失败：未取得服务地址" : $"战绩服务已就绪：{_sgpUrl}"
            );
            return !string.IsNullOrEmpty(_sgpUrl);
        }

        private static void LogInitState(string message) =>
            Services.AppLog.Shared.State(Services.LogCategory.Sgp, "sgp-init", message);

        public async Task<bool> LoadSessionAsync()
        {
            HttpResponseMessage resp = await _lcuClient.GetAsync("lol-login/v1/session");
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            _puuid = obj["puuid"]?.ToString();
            _summonerId = obj["summonerId"]?.ToString();
            _leagueSessionToken = obj["idToken"]?.ToString();

            return !string.IsNullOrEmpty(_puuid) && !string.IsNullOrEmpty(_leagueSessionToken);
        }

        public async Task<bool> LoadEntitlementsTokenAsync(CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage resp = await _lcuClient.GetAsync("entitlements/v1/token", cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            _accessToken = obj["accessToken"]?.ToString();
            _entitlementsToken = obj["token"]?.ToString();

            return !string.IsNullOrEmpty(_accessToken);
        }

        /// <summary>
        /// Tencent clients name their history service in the platform configuration. On Riot's servers that entry is
        /// the player-preferences service, which refuses history requests with 403; there the history host comes
        /// from SgpServers by the client's region, else from the client settings' player platform edge, the host the
        /// client itself uses for its login queue. The configured entry is the last resort.
        /// </summary>
        public async Task<string> LoadSgpEndpointAsync()
        {
            _clientHistoryUrl = null;
            _dataScope = "";
            string configured = await LoadConfiguredEndpointAsync();
            if (IsTencentHost(configured))
            {
                _dataScope = configured;
                return configured;
            }

            string region = await LoadRegionAsync();
            var (platformEdge, leagueEdge) = await LoadPlatformEdgeAsync();
            string tableHost = SgpServers.MatchHistoryHost(region);
            string chosen = tableHost ?? platformEdge ?? configured;
            if (chosen == null)
            {
                return null;
            }

            if (
                tableHost != null
                && platformEdge != null
                && !string.Equals(tableHost, platformEdge, StringComparison.OrdinalIgnoreCase)
            )
            {
                _clientHistoryUrl = platformEdge;
            }

            string regionScope = leagueEdge ?? (tableHost != null ? region : null);
            _dataScope = regionScope == null ? chosen : chosen + "|" + regionScope;
            return chosen;
        }

        private static bool IsTencentHost(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith(".lol.qq.com", StringComparison.OrdinalIgnoreCase);

        /// <summary>The client's region in SgpServers' spelling, such as JP; empty when the client does not answer.</summary>
        private async Task<string> LoadRegionAsync()
        {
            try
            {
                using HttpResponseMessage resp = await _lcuClient.GetAsync("riotclient/region-locale");
                if (!resp.IsSuccessStatusCode)
                {
                    return "";
                }

                JObject obj = JObject.Parse(await resp.Content.ReadAsStringAsync());
                return SgpServers.RegionId(obj["region"]?.ToString() ?? "");
            }
            catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException)
            {
                // Without a region the table is skipped and the client's own platform edge is used.
                AppLog.Shared.Warn(LogCategory.Sgp, "[SGP] 读取大区失败", ex);
                return "";
            }
        }

        private async Task<(string PlatformEdge, string LeagueEdge)> LoadPlatformEdgeAsync()
        {
            try
            {
                using HttpResponseMessage resp = await _lcuClient.GetAsync(
                    "lol-client-config/v3/client-config/lol.client_settings"
                );
                if (!resp.IsSuccessStatusCode)
                {
                    return (null, null);
                }

                JObject settings = JObject.Parse(await resp.Content.ReadAsStringAsync());
                string platformEdge = settings["player_platform_edge"]?["url"]?.ToString();
                string leagueEdge = settings["league_edge"]?["url"]?.ToString();
                return (
                    string.IsNullOrEmpty(platformEdge) ? null : platformEdge.TrimEnd('/'),
                    string.IsNullOrEmpty(leagueEdge) ? null : leagueEdge.TrimEnd('/')
                );
            }
            catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException)
            {
                // The table, or else the configured address, is still tried.
                AppLog.Shared.Warn(LogCategory.Sgp, "[SGP] 读取客户端平台地址失败", ex);
                return (null, null);
            }
        }

        private async Task<string> LoadConfiguredEndpointAsync()
        {
            try
            {
                HttpResponseMessage resp = await _lcuClient.GetAsync("lol-platform-config/v1/namespaces");
                if (!resp.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await resp.Content.ReadAsStringAsync();
                JObject obj = JObject.Parse(json);

                string sgpUrl = obj["PlayerPreferences"]?["ServiceEndpoint"]?.ToString();

                if (string.IsNullOrEmpty(sgpUrl))
                {
                    string capUrl = obj["LcuPurchaseWidget"]?["CapOrdersUrl"]?.ToString();
                    if (!string.IsNullOrEmpty(capUrl))
                    {
                        var match = Regex.Match(capUrl, @"(https?://[^/]+)");
                        if (match.Success)
                        {
                            sgpUrl = match.Groups[1].Value;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(sgpUrl))
                {
                    Debug.WriteLine($"[SGP] 成功获取 SGP 地址: {sgpUrl}");
                    return sgpUrl.TrimEnd('/');
                }
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Sgp, "[SGP] 获取 Endpoint 异常", ex);
            }

            Debug.WriteLine("[SGP] 未能获取 SGP 地址");
            return null;
        }

        public async Task<JArray> SgpFetchLatestMatches(
            string puuid,
            int startIndex = 0,
            int count = 19,
            string tag = null,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(_sgpUrl))
            {
                Debug.WriteLine("❌ _sgpUrl 为空");
                return null; // 配置/初始化失败，视为不可用
            }

            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                Debug.WriteLine("❌ accessToken 为空，尝试刷新");
                if (!await LoadEntitlementsTokenAsync(cancellationToken))
                {
                    return null;
                }
            }

            string host = _sgpUrl;
            string fullUrl = BuildMatchHistoryUrl(host, puuid, startIndex, count, tag);
            string failure = "";
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var (outcome, games, reason) = await TryFetchMatchesOnceAsync(fullUrl, attempt, cancellationToken);
                if (outcome == SgpAttempt.Succeeded)
                {
                    LogFetchState("战绩查询正常");
                    return games;
                }

                failure = reason;
                if (outcome == SgpAttempt.TokenRefreshFailed)
                {
                    LogFetchState("战绩查询失败：访问令牌刷新失败");
                    return null;
                }

                if (outcome == SgpAttempt.Refused && SwitchToClientHost(host))
                {
                    // The table's host is gone; the new one gets the attempt back.
                    host = _sgpUrl;
                    fullUrl = BuildMatchHistoryUrl(host, puuid, startIndex, count, tag);
                    attempt--;
                    continue;
                }

                if (attempt < 3)
                {
                    await LoadEntitlementsTokenAsync(cancellationToken);
                    await Task.Delay(400 * attempt, cancellationToken);
                }
            }

            // Logged once per change: ten cards failing for the same reason during maintenance leave one line.
            LogFetchState($"战绩查询重试 3 次仍失败：{failure}");
            return null; // ★ 明确表示：请求失败，不是「没有战绩」
        }

        private static void LogFetchState(string message) =>
            Services.AppLog.Shared.State(Services.LogCategory.Sgp, "sgp-fetch", message);

        /// <summary>
        /// Replaces a refused SgpServers host with the client's player platform edge; true when the query should go
        /// to a newer host, also when a concurrent query has already switched.
        /// </summary>
        private bool SwitchToClientHost(string refused)
        {
            if (!string.Equals(_sgpUrl, refused, StringComparison.Ordinal))
            {
                return true;
            }

            string next = Interlocked.Exchange(ref _clientHistoryUrl, null);
            if (next == null)
            {
                return false;
            }

            _sgpUrl = next;
            LogInitState($"地址表中的战绩服务 {refused} 拒绝请求，改用客户端配置的地址：{next}");
            return true;
        }

        private enum SgpAttempt
        {
            Succeeded,
            Retry,

            // 401, 403 or 404 with a valid token: this host does not serve the player's history.
            Refused,
            TokenRefreshFailed,
        }

        private static string BuildMatchHistoryUrl(string host, string puuid, int startIndex, int count, string tag)
        {
            string path = $"/match-history-query/v1/products/lol/player/{puuid}/SUMMARY";
            var queries = new List<string> { $"startIndex={startIndex}", $"count={count}" };
            if (!string.IsNullOrEmpty(tag))
            {
                queries.Add($"tag={tag}");
            }

            return host.TrimEnd('/') + path + "?" + string.Join("&", queries);
        }

        /// <summary>
        /// 一次限流请求。成功时返回 games（没有 games 字段时为空数组）；超时、网络错误和服务器错误可以重试。
        /// 失败原因只含状态码或异常类型，不含请求地址里的玩家 ID。
        /// </summary>
        private async Task<(SgpAttempt Outcome, JArray Games, string Failure)> TryFetchMatchesOnceAsync(
            string fullUrl,
            int attempt,
            CancellationToken cancellationToken
        )
        {
            // Acquire before try: a cancelled wait must not release a slot it never took.
            await _sgpLimiter.WaitAsync(cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                Debug.WriteLine($"SGP 请求地址: {fullUrl} (尝试 {attempt}/3)");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                var watch = Stopwatch.StartNew();
                using HttpResponseMessage response = await _sgpClient.SendAsync(request, cts.Token);
                watch.Stop();

                Debug.WriteLine($"SGP 查询状态: {response.StatusCode} 耗时: {watch.ElapsedMilliseconds}ms");

                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(cts.Token);
                    JObject json = JObject.Parse(content);

                    // ★ 成功：有 games 就返回，没有就返回空数组（不要返回 null）
                    var games = json["games"] as JArray;
                    return (SgpAttempt.Succeeded, games ?? new JArray(), "");
                }

                string errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                Debug.WriteLine($"请求失败: {response.StatusCode} - {errorContent}");
                string failure = $"服务器返回 {(int)response.StatusCode}";

                // 401/403 再刷新 token
                if (
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    && !await LoadEntitlementsTokenAsync(cancellationToken)
                )
                {
                    Debug.WriteLine("刷新 Token 失败");
                    return (SgpAttempt.TokenRefreshFailed, null, failure);
                }

                if (
                    response.StatusCode
                    is HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden
                        or HttpStatusCode.NotFound
                )
                {
                    return (SgpAttempt.Refused, null, failure);
                }

                // 5xx / 503 等服务器错误：继续重试，最后仍失败则返回 null
                return (SgpAttempt.Retry, null, failure);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException tce)
            {
                Debug.WriteLine($"SGP 查询超时（尝试 {attempt}/3）: {tce.Message}");
                return (SgpAttempt.Retry, null, $"{RequestTimeoutSeconds} 秒超时");
            }
            catch (HttpRequestException hre)
            {
                Debug.WriteLine($"HTTP 请求异常: {hre.Message} (Inner: {hre.InnerException?.Message})");
                return (SgpAttempt.Retry, null, $"网络错误 {hre.HttpRequestError}");
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Sgp, "其他异常", ex);
                return (SgpAttempt.Retry, null, $"{ex.GetType().Name} HRESULT={ex.HResult:X8}");
            }
            finally
            {
                _sgpLimiter.Release();
            }
        }
    }
}
