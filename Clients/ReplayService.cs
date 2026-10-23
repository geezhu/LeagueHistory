using System.Diagnostics;
using System.Net;
using System.Text;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// 游戏回放服务
    /// </summary>
    public class ReplayService
    {
        private readonly LcuClient _client;
        private const int DownloadTimeoutMs = 90000;
        private const int CheckIntervalMs = 2000;

        public ReplayService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 下载游戏回放，返回结束原因
        /// </summary>
        public async Task<ReplayDownloadResult> DownloadReplayAsync(long gameId, string contextData = "match-history")
        {
            try
            {
                Debug.WriteLine($"[回放] 开始下载回放，GameId: {gameId}");

                // 检查是否已存在有效回放
                var existingCheck = await CheckExistingReplayAsync(gameId);
                if (existingCheck.exists && existingCheck.isValid)
                {
                    Debug.WriteLine("[回放] 回放已存在且有效，跳过下载");
                    return ReplayDownloadResult.AlreadyExists;
                }

                // 版本更新后旧回放失效，再请求下载也不会成功
                if (existingCheck.state == "incompatible")
                {
                    return ReplayDownloadResult.Incompatible;
                }

                // 发送下载请求
                var jsonObj = new JObject { ["contextData"] = contextData };
                var postContent = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"/lol-replays/v1/rofls/{gameId}/download", postContent);
                Debug.WriteLine($"[回放] 下载请求响应: {response.StatusCode}");

                if (
                    !response.IsSuccessStatusCode
                    && response.StatusCode != HttpStatusCode.NoContent
                    && response.StatusCode != HttpStatusCode.Accepted
                )
                {
                    AppLog.Shared.Warn(LogCategory.Data, $"[回放] 下载请求被拒绝：{(int)response.StatusCode}");
                    return ReplayDownloadResult.RequestError;
                }

                // 等待下载完成
                return await WaitForDownloadCompleteAsync(gameId);
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "[回放] 下载异常", ex);
                return ReplayDownloadResult.Failed;
            }
        }

        /// <summary>
        /// 播放游戏回放
        /// </summary>
        public async Task<bool> PlayReplayAsync(long gameId, string contextData = "match-history")
        {
            try
            {
                Debug.WriteLine($"[回放] 尝试播放回放，GameId: {gameId}");

                var jsonObj = new JObject { ["contextData"] = contextData };
                var postContent = new StringContent(jsonObj.ToString(), Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"/lol-replays/v1/rofls/{gameId}/watch", postContent);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[回放] 播放请求成功");
                    return true;
                }

                var respText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[回放] 播放失败: {response.StatusCode} {respText}");
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "[回放] 播放异常", ex);
                return false;
            }
        }

        /// <summary>
        /// 检查回放元数据
        /// </summary>
        private async Task<(bool exists, bool isValid, string state)> CheckExistingReplayAsync(long gameId)
        {
            try
            {
                var response = await _client.GetAsync($"/lol-replays/v1/metadata/{gameId}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var metaJson = JObject.Parse(content);

                    var state = metaJson["state"]?.ToString()?.ToLowerInvariant() ?? "";
                    var progress = ParseProgress(metaJson["downloadProgress"]);

                    Debug.WriteLine($"[回放] 现有metadata状态: {state}, 进度: {progress}%");

                    return (exists: true, isValid: state == "watch" && progress >= 100, state);
                }

                return (exists: false, isValid: false, state: "");
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "[回放] 检查现有回放异常", ex);
                return (exists: false, isValid: false, state: "");
            }
        }

        /// <summary>
        /// 下载进度只接受 0–100；客户端偶尔给出越界或非数字的值，按未开始处理。
        /// </summary>
        private static int ParseProgress(JToken? token)
        {
            if (token?.Type is JTokenType.Integer or JTokenType.Float)
            {
                double value = token.Value<double>();
                return value is >= 0 and <= 100 ? (int)value : 0;
            }

            return int.TryParse(token?.ToString(), out int parsed) && parsed is >= 0 and <= 100 ? parsed : 0;
        }

        /// <summary>
        /// 等待下载完成
        /// </summary>
        private async Task<ReplayDownloadResult> WaitForDownloadCompleteAsync(long gameId)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < DownloadTimeoutMs)
            {
                await Task.Delay(CheckIntervalMs);

                try
                {
                    var response = await _client.GetAsync($"/lol-replays/v1/metadata/{gameId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var metaJson = JObject.Parse(content);

                        var state = metaJson["state"]?.ToString()?.ToLowerInvariant() ?? "";
                        var progress = ParseProgress(metaJson["downloadProgress"]);

                        Debug.WriteLine($"[回放] 下载状态: {state}, 进度: {progress}%");

                        if (state == "watch" && progress >= 100)
                        {
                            Debug.WriteLine("[回放] 下载完成");
                            return ReplayDownloadResult.Success;
                        }

                        if (state == "incompatible")
                        {
                            return ReplayDownloadResult.Incompatible;
                        }

                        if (state == "failed" || state == "error")
                        {
                            AppLog.Shared.Warn(LogCategory.Data, $"[回放] 下载失败：{state}");
                            return ReplayDownloadResult.Failed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Shared.Warn(LogCategory.Data, "[回放] 状态查询异常", ex);
                }
            }

            AppLog.Shared.Warn(LogCategory.Data, "[回放] 下载超时");
            return ReplayDownloadResult.Timeout;
        }
    }
}
