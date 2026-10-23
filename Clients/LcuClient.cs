using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;

namespace League.Clients
{
    /// <summary>
    /// LCU API 核心客户端
    /// </summary>
    public class LcuClient
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl;

        /// <summary>
        /// 获取 HttpClient 实例
        /// </summary>
        public HttpClient HttpClient => _httpClient;

        /// <summary>
        /// 获取或设置基础URL
        /// </summary>
        public string BaseUrl
        {
            get => _baseUrl;
            set
            {
                _baseUrl = value;
                if (_httpClient != null)
                {
                    _httpClient.BaseAddress = new Uri(value);
                }
            }
        }

        /// <summary>
        /// 初始化LCU客户端
        /// </summary>
        /// <param name="port">LCU端口</param>
        /// <param name="token">认证令牌</param>
        public LcuClient(string port, string token)
        {
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                throw new ArgumentException("端口和令牌不能为空");
            }

            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.Zero,
                PooledConnectionIdleTimeout = TimeSpan.Zero,
                MaxConnectionsPerServer = int.MaxValue,
                EnableMultipleHttp2Connections = true,
                ConnectTimeout = TimeSpan.FromSeconds(3.0),
                UseCookies = false,
                AllowAutoRedirect = false,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true,
                },
            };

            _httpClient = new HttpClient(handler)
            {
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            // 设置认证头
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + token))
            );

            _baseUrl = $"https://127.0.0.1:{port}/";
            _httpClient.BaseAddress = new Uri(_baseUrl);
        }

        /// <summary>
        /// 使用已设置好地址和认证的 HttpClient，例如测试里的桩传输
        /// </summary>
        public LcuClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = httpClient.BaseAddress?.ToString() ?? "";
        }

        /// <summary>
        /// 发送通用HTTP请求
        /// </summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// 发送PATCH请求
        /// </summary>
        public async Task<HttpResponseMessage> PatchAsync(string requestUri, HttpContent content)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUri) { Content = content };
            return await SendAsync(request);
        }

        /// <summary>
        /// 发送POST请求
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content = null)
        {
            return await _httpClient.PostAsync(requestUri, content);
        }

        /// <summary>
        /// 发送GET请求
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(
            string requestUri,
            CancellationToken cancellationToken = default
        )
        {
            return await _httpClient.GetAsync(requestUri, cancellationToken);
        }
    }
}
