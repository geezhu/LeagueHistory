using System.Diagnostics;

namespace League.Clients
{
    /// <summary>
    /// LCU 连接管理器 - 负责连接和初始化
    /// </summary>
    public class LcuConnectionManager
    {
        private LcuConnector _connector;
        private LcuClient _lcuClient;

        public LcuClient Client => _lcuClient;

        public async Task<(bool success, LcuClient client)> InitializeAsync()
        {
            _connector = new LcuConnector();

            var (success, port, token) = await _connector.InitializeAsync();

            if (!success)
            {
                Debug.WriteLine("[LCU] 连接初始化失败");
                return (false, null);
            }

            try
            {
                _lcuClient = new LcuClient(port, token);

                // 测试连接
                if (await TestConnectionAsync())
                {
                    Services.AppLog.Shared.State(Services.LogCategory.Connection, "lcu-api", "已连接客户端接口");
                    return (true, _lcuClient);
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Services.AppLog.Shared.State(
                    Services.LogCategory.Connection,
                    "lcu-api",
                    $"客户端接口初始化异常：{ex.GetType().Name} HRESULT={ex.HResult:X8}"
                );
                return (false, null);
            }
        }

        private async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _lcuClient.GetAsync("/lol-summoner/v1/current-summoner", cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                LogApiState($"客户端接口返回 {(int)response.StatusCode}（登录未完成时常见）");
                return false;
            }
            catch (TaskCanceledException)
            {
                LogApiState("客户端接口 10 秒内没有响应");
                return false;
            }
            catch (HttpRequestException ex)
            {
                LogApiState($"无法连接客户端接口：{ex.HttpRequestError}");
                return false;
            }
            catch (Exception ex)
            {
                LogApiState($"客户端接口测试异常：{ex.GetType().Name} HRESULT={ex.HResult:X8}");
                return false;
            }
        }

        private static void LogApiState(string message) =>
            Services.AppLog.Shared.State(Services.LogCategory.Connection, "lcu-api", message);

        public void Dispose()
        {
            _lcuClient?.HttpClient?.Dispose();
        }
    }
}
