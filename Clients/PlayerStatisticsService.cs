using Newtonsoft.Json.Linq;

namespace League.Clients;

public sealed record StatisticsResponse(JToken? Data, string Message, int Status = 0);

/// <summary>Read-only statistics requests. Failures are never cached as empty statistics.</summary>
public sealed class PlayerStatisticsService
{
    private readonly Func<HttpClient?> _client;

    public PlayerStatisticsService(Func<HttpClient?> client) => _client = client;

    public async Task<StatisticsResponse> GetAsync(string path, CancellationToken cancellationToken)
    {
        var client = _client();
        if (client == null)
        {
            return new(null, "客户端尚未连接，请登录后重试。");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var response = await client.GetAsync(path, timeout.Token);
            int status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new(
                    null,
                    status switch
                    {
                        401 => "客户端登录已失效，请重新连接。",
                        403 => "当前玩家资料不允许访问。",
                        404 => "客户端暂未提供这项数据。",
                        _ => $"数据暂时不可用（HTTP {status}），可以重试。",
                    },
                    status
                );
            }

            var data = JToken.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (data is JObject error && error["errorCode"] != null)
            {
                return new(null, "客户端未返回有效统计数据。", status);
            }

            return new(data, "", status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, "请求超时，请重试。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
            when (ex is HttpRequestException or Newtonsoft.Json.JsonException or InvalidOperationException)
        {
            return new(null, "数据加载失败，请确认客户端连接后重试。");
        }
    }
}
