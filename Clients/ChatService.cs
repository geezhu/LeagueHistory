using System;
using System.Diagnostics;
using League.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Clients
{
    /// <summary>
    /// 聊天服务（选人 / 游戏内）- 增强版，支持多种发送方式
    /// </summary>
    public class ChatService
    {
        private readonly LcuClient _client;

        public ChatService(LcuClient client)
        {
            _client = client;
        }

        /// <summary>
        /// 向指定会话发送消息
        /// </summary>
        public async Task<bool> SendMessageAsync(string conversationId, string message)
        {
            var payload = new { body = message };

            string json = JsonConvert.SerializeObject(payload);

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var resp = await _client.PostAsync($"/lol-chat/v1/conversations/{conversationId}/messages", content);

            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync();
                Debug.WriteLine($"[Chat] 发送失败: {resp.StatusCode} - {errorBody}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 增强版：查找会话（支持多种状态）
        /// </summary>
        public async Task<string?> FindConversationIdAsync(string type, int maxRetries = 3, int delayMs = 500)
        {
            string[] targetTypes = GetTargetTypes(type);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var resp = await _client.GetAsync("/lol-chat/v1/conversations");
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[Chat] 获取会话失败: {resp.StatusCode}");
                        return null;
                    }

                    string rawJson = await resp.Content.ReadAsStringAsync();
                    var conversations = JArray.Parse(rawJson);

                    Debug.WriteLine($"[Chat] 第 {attempt} 次查询，会话总数: {conversations.Count}");

                    if (attempt == 1 || conversations.Count == 0)
                    {
                        LogConversations(conversations);
                    }

                    string? id = FindMatchingConversation(conversations, targetTypes);
                    if (id != null)
                    {
                        return id;
                    }

                    if (attempt < maxRetries)
                    {
                        Debug.WriteLine($"[Chat] 未找到 {type} 会话，{delayMs}ms 后重试...");
                        await Task.Delay(delayMs);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Shared.Warn(LogCategory.Automation, "[Chat] 查找会话异常", ex);
                }
            }

            Debug.WriteLine($"[Chat] 重试 {maxRetries} 次后仍未找到 {type} 会话");
            return null;
        }

        // 调用方用的场景名对应会话可能出现的 type 或 gameStatus，按优先级排列。
        private static string[] GetTargetTypes(string type) =>
            type.ToLower() switch
            {
                "game" => new[] { "game", "champSelect", "customGame", "practiceTool", "inProgress" },
                "champselect" => new[] { "champSelect", "championSelect", "inProgress" },
                "ingame" => new[] { "inProgress", "game", "champSelect" },
                _ => new[] { type },
            };

        private static void LogConversations(JArray conversations)
        {
            foreach (var c in conversations)
            {
                Debug.WriteLine(
                    $"[会话] id={c["id"]}, type={c["type"]}, gameStatus={c["gameStatus"]}, state={c["state"]}, name={c["name"] ?? c["gameName"] ?? "无"}"
                );
            }
        }

        // 按会话顺序找第一个匹配任一目标、且带 id 的会话。
        private static string? FindMatchingConversation(JArray conversations, string[] targetTypes)
        {
            foreach (var conv in conversations)
            {
                foreach (var target in targetTypes)
                {
                    if (!MatchesTarget(conv, target))
                    {
                        continue;
                    }

                    string? id = conv["id"]?.ToString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        Debug.WriteLine(
                            $"[Chat] 找到会话: type={conv["type"]}, gameStatus={conv["gameStatus"]}, state={conv["state"]}, name={ConversationName(conv)}, id={id}"
                        );
                        return id;
                    }
                }
            }
            return null;
        }

        private static string ConversationName(JToken conv) =>
            conv["gameName"]?.ToString() ?? conv["name"]?.ToString() ?? "";

        /// <summary>
        /// 依次比较 type、gameStatus；目标是 inProgress 时还看会话状态和名称；其他目标按游戏名称关键词兜底。
        /// </summary>
        private static bool MatchesTarget(JToken conv, string target)
        {
            string convType = conv["type"]?.ToString() ?? "";
            string gameStatus = conv["gameStatus"]?.ToString() ?? "";
            string convState = conv["state"]?.ToString() ?? "";
            string convName = ConversationName(conv);

            if (
                convType.Equals(target, StringComparison.OrdinalIgnoreCase)
                || gameStatus.Equals(target, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            if (target.Equals("inprogress", StringComparison.OrdinalIgnoreCase))
            {
                return gameStatus.Equals("inProgress", StringComparison.OrdinalIgnoreCase)
                    || convState.Equals("active", StringComparison.OrdinalIgnoreCase)
                    || convName.Contains("游戏进行中")
                    || convName.Contains("正在游戏中");
            }

            return convName.Contains("召唤师峡谷")
                || convName.Contains("扭曲丛林")
                || convName.Contains("极地大乱斗")
                || convName.Contains("ARAM")
                || convName.Contains("匹配")
                || convName.Contains("排位")
                || convName.Contains("自定义");
        }
    }
}
