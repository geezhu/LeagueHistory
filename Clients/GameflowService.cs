using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// 游戏流程状态服务
    /// </summary>
    public class GameflowService
    {
        private readonly LcuClient _client;

        public GameflowService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 获取当前游戏流程阶段
        /// </summary>
        public async Task<string> GetGameflowPhaseAsync()
        {
            try
            {
                var response = await _client.GetAsync("/lol-gameflow/v1/gameflow-phase");

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                return content.Trim().Trim('"');
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"由于目标计算机积极拒绝，无法连接。");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取游戏阶段失败: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 获取英雄选择会话信息
        /// </summary>
        public async Task<JObject> GetChampSelectSessionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _client.GetAsync("/lol-champ-select/v1/session", cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JObject.Parse(content);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return new JObject();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取选人阶段数据失败: {ex}");
                return new JObject();
            }
        }

        /// <summary>
        /// 获取完整游戏会话信息
        /// </summary>
        public async Task<JObject> GetGameSessionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await _client.GetAsync("/lol-gameflow/v1/session", cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JObject.Parse(content);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[LCU] 请求超时: {ex.Message}");
                return new JObject();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取游戏会话失败: {ex}");
                return new JObject();
            }
        }

        /// <summary>
        /// 检查是否在英雄选择阶段
        /// </summary>
        public async Task<bool> IsInChampSelectAsync()
        {
            var phase = await GetGameflowPhaseAsync();
            return phase == "ChampSelect";
        }

        /// <summary>
        /// 检查是否在游戏中
        /// </summary>
        public async Task<bool> IsInGameAsync()
        {
            var phase = await GetGameflowPhaseAsync();
            return phase == "InProgress";
        }
    }
}
