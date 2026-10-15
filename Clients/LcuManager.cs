//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using System.Diagnostics;
//using System.Management;
//using System.Net.WebSockets;
//using System.Text;
//using System.Text.RegularExpressions;

//namespace League.Clients
//{
//    public enum LcuConnectionState
//    {
//        Disconnected,
//        Connecting,
//        Connected
//    }

//    /// <summary>
//    /// 专业级 LCU 连接管理器（HTTP + WebSocket 双通道）
//    /// 支持「游戏内关闭客户端」后的恢复连接（参考 LeagueAkari 的 lastConnectedClient 机制）
//    /// </summary>
//    public class LcuManager : IDisposable
//    {
//        public static LcuManager Instance { get; } = new LcuManager();

//        private bool _hasEverConnected = false;  // 只有真正连上过，断线才通知 UI

//        /// <summary>HTTP 客户端（供所有 Service 使用）</summary>
//        public LcuClient HttpClient { get; private set; }

//        public LcuConnectionState State { get; private set; } = LcuConnectionState.Disconnected;

//        /// <summary>连接成功</summary>
//        public event Action Connected;

//        /// <summary>真正断线（进程消失或多次重连失败）</summary>
//        public event Action Disconnected;

//        /// <summary>游戏阶段变化 (newPhase, oldPhase)</summary>
//        public event Func<string, string, Task> PhaseChanged;

//        /// <summary>没有 LCU，但 2999 显示正在对局（关客户端模式）</summary>
//        //public event Action InGameWithoutLcu;

//        // 内部状态
//        private ClientWebSocket _ws;
//        private CancellationTokenSource _cts;
//        private string _port;
//        private string _token;
//        private string _lastPhase;
//        private bool _isRunning;
//        private readonly object _lock = new object();

//        // ==================== 缓存相关 ====================
//        private readonly string _cacheFilePath;
//        private const int CacheValidMinutes = 60;   // 缓存有效期（分钟），可自行调整

//        public LcuManager()
//        {
//            // 缓存文件直接保存在程序启动目录
//            string appDir = AppDomain.CurrentDomain.BaseDirectory;
//            _cacheFilePath = Path.Combine(appDir, "last_lcu_cache.json");
//        }

//        // ========== 对外入口 ==========
//        public async Task StartAsync()
//        {
//            lock (_lock)
//            {
//                if (_isRunning) return;
//                _isRunning = true;
//            }

//            _cts = new CancellationTokenSource();
//            _ = Task.Run(ConnectionLoopAsync, _cts.Token);
//            Debug.WriteLine("[LcuManager] 启动连接循环");
//        }

//        public void Stop()
//        {
//            lock (_lock)
//            {
//                if (!_isRunning) return;
//                _isRunning = false;
//            }

//            _cts?.Cancel();
//            DisconnectInternal();
//            Debug.WriteLine("[LcuManager] 已停止");
//        }

//        // ========== 主连接循环 ==========
//        private async Task ConnectionLoopAsync()
//        {
//            while (_isRunning && !_cts.IsCancellationRequested)
//            {
//                try
//                {
//                    // 1. 获取凭证
//                    if (!await TryGetCredentialsAsync())
//                    {
//                        await SafeDelay(2000);
//                        continue;
//                    }

//                    // 2. 尝试建立连接（带重试，应对客户端刚启动 API 未就绪）
//                    State = LcuConnectionState.Connecting;
//                    Debug.WriteLine($"[LcuManager] 尝试连接 → Port:{_port}");

//                    bool connected = await TryConnectWithRetryAsync(maxAttempts: 15, delayMs: 1500);

//                    if (!connected)
//                    {
//                        Debug.WriteLine("[LcuManager] 多次尝试后仍无法连接，稍后重试...");
//                        await SafeDelay(3000);
//                        continue; // 注意：这里不要触发 Disconnected
//                    }

//                    // 3. HTTP + WS 已通，但登录会话可能还没就绪 → 先等 summoner
//                    Debug.WriteLine("[LcuManager] HTTP/WS 已连接，等待 current-summoner 就绪...");
//                    bool summonerReady = await WaitForSummonerReadyAsync(maxAttempts: 30, delayMs: 1000);
//                    if (!summonerReady)
//                    {
//                        Debug.WriteLine("[LcuManager] summoner 未就绪，本轮不触发 Connected，稍后重试");
//                        DisconnectInternal();
//                        await SafeDelay(2000);
//                        continue;
//                    }

//                    // 4. 真正可用
//                    State = LcuConnectionState.Connected;
//                    _hasEverConnected = true;

//                    SaveCache(_port, _token);

//                    Connected?.Invoke();
//                    await Task.Delay(400); // 给主窗体注入 Service 的时间

//                    await QueryCurrentPhaseAsync();

//                    // ★ 必须加回：进入 WS 接收循环，直到断线才往下走
//                    Debug.WriteLine("[LcuManager] 已连接，进入接收循环");
//                    await ReceiveLoopAsync();
//                }
//                catch (OperationCanceledException)
//                {
//                    break;
//                }
//                catch (Exception ex)
//                {
//                    Debug.WriteLine($"[LcuManager] 连接循环异常: {ex.Message}");
//                }

//                // 走到这里说明连接断了
//                DisconnectInternal();

//                // 只有曾经成功连接过，才通知 UI 断线
//                if (_hasEverConnected)
//                {
//                    _hasEverConnected = false;
//                    Disconnected?.Invoke();
//                    Debug.WriteLine("[LcuManager] 已断线，3 秒后重试...");
//                }
//                else
//                {
//                    Debug.WriteLine("[LcuManager] 尚未成功连接过，继续静默重试...");
//                }

//                await SafeDelay(3000);
//            }
//        }

//        // ========== 获取 port / token（核心改造） ==========
//        private async Task<bool> TryGetCredentialsAsync()
//        {
//            try
//            {
//                // 1. 优先从 LeagueClientUx 获取（正常情况）
//                var uxProcess = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
//                if (uxProcess != null)
//                {
//                    string cmdLine = GetCommandLine(uxProcess);
//                    if (!string.IsNullOrEmpty(cmdLine))
//                    {
//                        string port = ExtractArgument(cmdLine, "--app-port=");
//                        string token = ExtractArgument(cmdLine, "--remoting-auth-token=");

//                        if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
//                        {
//                            _port = Clean(port);
//                            _token = Clean(token);

//                            // 成功拿到就立刻缓存
//                            SaveCache(_port, _token);
//                            Debug.WriteLine($"[LcuManager] 从 LeagueClientUx 获取凭证成功 → Port:{_port}");
//                            return true;
//                        }
//                    }
//                }

//                // 2. UX 不存在，检查是否还存在 LeagueClient.exe（游戏内关闭客户端的情况）
//                var clientProcess = Process.GetProcessesByName("LeagueClient").FirstOrDefault();
//                if (clientProcess != null)
//                {
//                    if (LoadCache(out string cachedPort, out string cachedToken))
//                    {
//                        _port = cachedPort;
//                        _token = cachedToken;
//                        Debug.WriteLine($"[LcuManager] UX 已关闭，使用缓存凭证尝试连接 → Port:{_port}");
//                        return true;
//                    }
//                    else
//                    {
//                        Debug.WriteLine("[LcuManager] UX 已关闭，且没有有效缓存");
//                    }
//                }
//                else
//                {
//                    // 3. 两个进程都没有时，也尝试一次缓存（应对极少数时序问题）
//                    if (LoadCache(out string cachedPort, out string cachedToken))
//                    {
//                        _port = cachedPort;
//                        _token = cachedToken;
//                        Debug.WriteLine($"[LcuManager] 进程检测不到，强制使用缓存尝试 → Port:{_port}");
//                        return true;
//                    }
//                }

//                return false;
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[LcuManager] 获取凭证失败: {ex.Message}");
//                return false;
//            }
//        }

//        /// <summary>
//        /// 带重试的连接：专门应对「进程已启动但 API 还没就绪」
//        /// </summary>
//        private async Task<bool> TryConnectWithRetryAsync(int maxAttempts, int delayMs)
//        {
//            for (int i = 1; i <= maxAttempts; i++)
//            {
//                if (_cts.IsCancellationRequested) return false;

//                try
//                {
//                    // 每次重试都重新读一次凭证（防止客户端重启后 port 变了）
//                    await TryGetCredentialsAsync();

//                    await ConnectHttpAndWsAsync();
//                    Debug.WriteLine($"[LcuManager] 第 {i} 次尝试连接成功");
//                    return true;
//                }
//                catch (Exception ex)
//                {
//                    Debug.WriteLine($"[LcuManager] 第 {i}/{maxAttempts} 次连接失败: {ex.Message}");

//                    // 清理半成品连接
//                    try
//                    {
//                        _ws?.Abort();
//                        _ws?.Dispose();
//                    }
//                    catch { }
//                    _ws = null;

//                    if (i < maxAttempts)
//                        await SafeDelay(delayMs);
//                }
//            }
//            return false;
//        }

//        private async Task ConnectHttpAndWsAsync()
//        {
//            // --- HTTP ---
//            HttpClient?.HttpClient?.Dispose();
//            HttpClient = new LcuClient(_port, _token);

//            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
//            var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");

//            // 注意：刚登录时 current-summoner 可能暂时 404/空，只要不是「连接被拒绝」就算 HTTP 通了
//            if (!response.IsSuccessStatusCode)
//            {
//                // 再试一个几乎总会有的端点
//                response = await HttpClient.GetAsync("/riotclient/app-name");
//                if (!response.IsSuccessStatusCode)
//                    throw new Exception($"HTTP 测试失败: {response.StatusCode}");
//            }

//            // --- WebSocket ---
//            _ws?.Dispose();
//            _ws = new ClientWebSocket();
//            _ws.Options.RemoteCertificateValidationCallback = (s, c, ch, e) => true;
//            _ws.Options.SetRequestHeader("Authorization",
//                "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_token}")));

//            var uri = new Uri($"wss://127.0.0.1:{_port}/");
//            await _ws.ConnectAsync(uri, _cts.Token);

//            // 订阅 phase
//            await SendWsAsync("[5,\"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
//        }

//        /// <summary>
//        /// 等待 current-summoner 真正可用（登录完成）
//        /// 解决：进程/端口已通，但账号会话尚未建立的时序问题
//        /// </summary>
//        private async Task<bool> WaitForSummonerReadyAsync(int maxAttempts = 30, int delayMs = 1000)
//        {
//            for (int i = 1; i <= maxAttempts; i++)
//            {
//                if (_cts == null || _cts.IsCancellationRequested)
//                    return false;

//                try
//                {
//                    var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");
//                    if (response.IsSuccessStatusCode)
//                    {
//                        var json = await response.Content.ReadAsStringAsync();
//                        if (!string.IsNullOrWhiteSpace(json))
//                        {
//                            var obj = JObject.Parse(json);
//                            if (!string.IsNullOrEmpty(obj["puuid"]?.ToString()))
//                            {
//                                Debug.WriteLine($"[LcuManager] current-summoner 已就绪（第 {i} 次）");
//                                return true;
//                            }
//                        }
//                    }
//                    Debug.WriteLine($"[LcuManager] current-summoner 未就绪 ({i}/{maxAttempts}) status={response.StatusCode}");
//                }
//                catch (Exception ex)
//                {
//                    Debug.WriteLine($"[LcuManager] 等待 summoner 异常 ({i}/{maxAttempts}): {ex.Message}");
//                }

//                await SafeDelay(delayMs);
//            }

//            Debug.WriteLine("[LcuManager] 等待 current-summoner 超时");
//            return false;
//        }

//        // ========== 接收循环 ==========
//        private async Task ReceiveLoopAsync()
//        {
//            var buffer = new byte[16384];
//            var messageBuffer = new StringBuilder();

//            while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
//            {
//                try
//                {
//                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

//                    if (result.MessageType == WebSocketMessageType.Close)
//                    {
//                        Debug.WriteLine("[LcuManager] WebSocket 收到关闭帧");
//                        break;
//                    }

//                    if (result.MessageType == WebSocketMessageType.Text)
//                    {
//                        messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

//                        if (result.EndOfMessage)
//                        {
//                            string raw = messageBuffer.ToString();
//                            messageBuffer.Clear();
//                            await ProcessWsMessageAsync(raw);
//                        }
//                    }
//                }
//                catch (OperationCanceledException)
//                {
//                    break;
//                }
//                catch (WebSocketException ex)
//                {
//                    Debug.WriteLine($"[LcuManager] WebSocket 异常: {ex.Message}");
//                    break;
//                }
//                catch (Exception ex)
//                {
//                    Debug.WriteLine($"[LcuManager] 接收异常: {ex.Message}");
//                    // 不立刻退出，继续尝试接收
//                }
//            }
//        }

//        private async Task ProcessWsMessageAsync(string raw)
//        {
//            if (string.IsNullOrWhiteSpace(raw))
//                return; // 空消息直接忽略

//            try
//            {
//                var arr = JArray.Parse(raw);
//                if (arr.Count < 3 || arr[0].Value<int>() != 8) return;

//                var payload = arr[2] as JObject;
//                if (payload == null) return;

//                string uri = payload["uri"]?.ToString();
//                var data = payload["data"];

//                if (uri == "/lol-gameflow/v1/gameflow-phase" && data != null)
//                {
//                    string phase = data.Type == JTokenType.String
//                        ? data.Value<string>()?.Trim('"')
//                        : data.ToString();

//                    if (!string.IsNullOrEmpty(phase) && phase != _lastPhase)
//                    {
//                        string oldPhase = _lastPhase;
//                        _lastPhase = phase;

//                        Debug.WriteLine($"[LcuManager] Phase: {oldPhase ?? "null"} → {phase}");

//                        if (PhaseChanged != null)
//                            await PhaseChanged.Invoke(phase, oldPhase);
//                    }
//                }
//            }
//            catch (JsonReaderException)
//            {
//                // 非 JSON 或空帧，直接忽略
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[LcuManager] 解析消息失败: {ex.Message}");
//            }
//        }

//        // ========== 主动查询当前 phase（连接成功后补一次） ==========
//        private async Task QueryCurrentPhaseAsync()
//        {
//            try
//            {
//                var response = await HttpClient.GetAsync("/lol-gameflow/v1/gameflow-phase");
//                if (response.IsSuccessStatusCode)
//                {
//                    string content = await response.Content.ReadAsStringAsync();
//                    string phase = content.Trim().Trim('"');
//                    if (!string.IsNullOrEmpty(phase) && phase != _lastPhase)
//                    {
//                        string old = _lastPhase;
//                        _lastPhase = phase;
//                        if (PhaseChanged != null)
//                            await PhaseChanged.Invoke(phase, old);
//                    }
//                }
//            }
//            catch { /* 忽略 */ }
//        }

//        private async Task SendWsAsync(string json)
//        {
//            if (_ws == null || _ws.State != WebSocketState.Open) return;
//            var bytes = Encoding.UTF8.GetBytes(json);
//            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
//        }

//        private void DisconnectInternal()
//        {
//            try
//            {
//                if (_ws != null)
//                {
//                    if (_ws.State == WebSocketState.Open)
//                    {
//                        try
//                        {
//                            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
//                               .Wait(500);
//                        }
//                        catch { }
//                    }
//                    _ws.Dispose();
//                }
//            }
//            catch { }

//            _ws = null;
//            State = LcuConnectionState.Disconnected;
//        }

//        private async Task SafeDelay(int ms)
//        {
//            try { await Task.Delay(ms, _cts.Token); }
//            catch (OperationCanceledException) { }
//        }

//        // ==================== 缓存读写 ====================
//        private void SaveCache(string port, string token)
//        {
//            try
//            {
//                var data = new
//                {
//                    Port = port,
//                    Token = token,
//                    Time = DateTime.Now
//                };
//                File.WriteAllText(_cacheFilePath, JsonConvert.SerializeObject(data, Formatting.Indented));
//                Debug.WriteLine($"[LcuManager] 已更新缓存 → {_cacheFilePath}");
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[LcuManager] 保存缓存失败: {ex.Message}");
//            }
//        }

//        private bool LoadCache(out string port, out string token)
//        {
//            port = null;
//            token = null;

//            try
//            {
//                if (!File.Exists(_cacheFilePath)) return false;

//                var json = File.ReadAllText(_cacheFilePath);
//                var obj = JObject.Parse(json);

//                var time = obj["Time"]?.ToObject<DateTime>() ?? DateTime.MinValue;
//                if ((DateTime.Now - time).TotalMinutes > CacheValidMinutes)
//                {
//                    Debug.WriteLine("[LcuManager] 缓存已过期");
//                    return false;
//                }

//                port = obj["Port"]?.ToString();
//                token = obj["Token"]?.ToString();

//                return !string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token);
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[LcuManager] 读取缓存失败: {ex.Message}");
//                return false;
//            }
//        }

//        private string Clean(string value)
//        {
//            return value?.Trim().Trim('"', '\'', ' ', '\r', '\n');
//        }

//        // ========== 辅助方法 ==========
//        private string GetCommandLine(Process process)
//        {
//            try
//            {
//                using var searcher = new ManagementObjectSearcher(
//                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
//                using var collection = searcher.Get();
//                foreach (ManagementObject obj in collection)
//                    return obj["CommandLine"]?.ToString() ?? "";
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[LcuManager] 获取命令行失败: {ex.Message}");
//            }
//            return "";
//        }

//        private string ExtractArgument(string cmdLine, string key)
//        {
//            var match = Regex.Match(cmdLine, key + "([^ ]+)");
//            return match.Success ? match.Groups[1].Value : null;
//        }

//        public void Dispose()
//        {
//            Stop();
//            HttpClient?.HttpClient?.Dispose();
//        }
//    }
//}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Management;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;

namespace League.Clients
{
    public enum LcuConnectionState
    {
        Disconnected,
        Connecting,
        Reconnecting,   // 新增：曾经连上过，现在在重连
        Connected
    }

    /// <summary>
    /// 专业级 LCU 连接管理器（HTTP + WebSocket 双通道）
    /// 永久持续检测版本：软件启动后永远循环检测，连上忽略，断了立刻重连并通知 UI
    /// 彻底解决网络抖动 / 掉线后「永远卡在检测中」的问题
    /// </summary>
    public class LcuManager : IDisposable
    {
        public static LcuManager Instance { get; } = new LcuManager();

        /// <summary>HTTP 客户端（供所有 Service 使用）</summary>
        public LcuClient HttpClient { get; private set; }

        public LcuConnectionState State { get; private set; } = LcuConnectionState.Disconnected;

        /// <summary>连接成功（真正可用）</summary>
        public event Action Connected;

        /// <summary>断线 / 重连中（UI 应显示“正在重新连接”）</summary>
        public event Action Disconnected;

        /// <summary>游戏阶段变化 (newPhase, oldPhase)</summary>
        public event Func<string, string, Task> PhaseChanged;

        // 内部状态
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _port;
        private string _token;
        private string _lastPhase;
        private bool _isRunning;
        private readonly object _lock = new object();
        private int _consecutiveFailCount = 0;
        private const int MaxFailBeforeClearCache = 5;

        // ==================== 缓存相关 ====================
        private readonly string _cacheFilePath;
        private const int CacheValidMinutes = 60;

        public LcuManager()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            _cacheFilePath = Path.Combine(appDir, "last_lcu_cache.json");
        }

        // ========== 对外入口 ==========
        public async Task StartAsync()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                _isRunning = true;
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(ConnectionLoopAsync, _cts.Token);
            Debug.WriteLine("[LcuManager] 启动永久连接循环（持续检测）");
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _isRunning = false;
            }

            _cts?.Cancel();
            DisconnectInternal();
            Debug.WriteLine("[LcuManager] 已停止");
        }

        // ========== 主连接循环（永久运行） ==========
        private async Task ConnectionLoopAsync()
        {
            while (_isRunning && !_cts.IsCancellationRequested)
            {
                try
                {
                    // 1. 获取凭证
                    if (!await TryGetCredentialsAsync())
                    {
                        // 没有进程也没有有效缓存 → 真正断线
                        if (State != LcuConnectionState.Disconnected)
                        {
                            State = LcuConnectionState.Disconnected;
                            Disconnected?.Invoke();
                            Debug.WriteLine("[LcuManager] 无进程/缓存，触发 Disconnected");
                        }
                        await SafeDelay(2500);
                        continue;
                    }

                    // 2. 尝试建立连接
                    bool wasConnected = State == LcuConnectionState.Connected;
                    State = wasConnected ? LcuConnectionState.Reconnecting : LcuConnectionState.Connecting;
                    Debug.WriteLine($"[LcuManager] 尝试连接 → Port:{_port}  State={State}");

                    // 如果之前是 Connected，先通知 UI 进入重连状态
                    if (wasConnected)
                    {
                        Disconnected?.Invoke();
                    }

                    bool connected = await TryConnectWithRetryAsync(maxAttempts: 12, delayMs: 1200);

                    if (!connected)
                    {
                        _consecutiveFailCount++;
                        Debug.WriteLine($"[LcuManager] 连接失败 (连续{_consecutiveFailCount}次)，稍后重试...");

                        if (_consecutiveFailCount >= MaxFailBeforeClearCache)
                        {
                            ClearCache();
                            _consecutiveFailCount = 0;
                            Debug.WriteLine("[LcuManager] 连续失败过多，已清除缓存");
                        }

                        if (State != LcuConnectionState.Disconnected)
                        {
                            State = LcuConnectionState.Disconnected;
                            Disconnected?.Invoke();
                        }
                        await SafeDelay(3000);
                        continue;
                    }

                    // 3. HTTP + WS 已通，等待 summoner 就绪（更宽松）
                    Debug.WriteLine("[LcuManager] HTTP/WS 已连接，等待 current-summoner 就绪...");
                    bool summonerReady = await WaitForSummonerReadyAsync(maxAttempts: 45, delayMs: 800); // 约36秒
                    if (!summonerReady)
                    {
                        Debug.WriteLine("[LcuManager] summoner 仍未就绪，本轮放弃，稍后重试");
                        DisconnectInternal();
                        _consecutiveFailCount++;
                        if (State != LcuConnectionState.Disconnected)
                        {
                            State = LcuConnectionState.Disconnected;
                            Disconnected?.Invoke();
                        }
                        await SafeDelay(2000);
                        continue;
                    }

                    // 4. 真正可用
                    State = LcuConnectionState.Connected;
                    _consecutiveFailCount = 0;
                    SaveCache(_port, _token);

                    Connected?.Invoke();
                    Debug.WriteLine("[LcuManager] ★ 连接成功，触发 Connected");
                    await Task.Delay(300); // 给主窗体注入时间

                    await QueryCurrentPhaseAsync();

                    // 进入接收循环 + 后台健康检查
                    Debug.WriteLine("[LcuManager] 已连接，进入接收循环 + 健康检查");
                    await ReceiveLoopWithHealthCheckAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 连接循环异常: {ex.Message}");
                }

                // 走到这里说明连接断了
                DisconnectInternal();

                // 永远通知 UI 断线（不再用 _hasEverConnected 静默）
                if (State != LcuConnectionState.Disconnected)
                {
                    State = LcuConnectionState.Disconnected;
                    Disconnected?.Invoke();
                    Debug.WriteLine("[LcuManager] 已断线，3 秒后自动重连...");
                }

                await SafeDelay(3000);
            }
        }

        // ========== 获取 port / token ==========
        private async Task<bool> TryGetCredentialsAsync()
        {
            try
            {
                // 1. 优先从 LeagueClientUx 获取（正常情况）
                var uxProcess = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (uxProcess != null)
                {
                    string cmdLine = GetCommandLine(uxProcess);
                    if (!string.IsNullOrEmpty(cmdLine))
                    {
                        string port = ExtractArgument(cmdLine, "--app-port=");
                        string token = ExtractArgument(cmdLine, "--remoting-auth-token=");

                        if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                        {
                            _port = Clean(port);
                            _token = Clean(token);
                            SaveCache(_port, _token);
                            Debug.WriteLine($"[LcuManager] 从 LeagueClientUx 获取凭证成功 → Port:{_port}");
                            return true;
                        }
                    }
                }

                // 2. UX 不存在，检查是否还存在 LeagueClient.exe（游戏内关闭客户端）
                var clientProcess = Process.GetProcessesByName("LeagueClient").FirstOrDefault();
                if (clientProcess != null)
                {
                    if (LoadCache(out string cachedPort, out string cachedToken))
                    {
                        _port = cachedPort;
                        _token = cachedToken;
                        Debug.WriteLine($"[LcuManager] UX 已关闭，使用缓存凭证 → Port:{_port}");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine("[LcuManager] UX 已关闭，且没有有效缓存");
                    }
                }
                else
                {
                    // 3. 两个进程都没有时，也尝试一次缓存（极少数时序）
                    if (LoadCache(out string cachedPort, out string cachedToken))
                    {
                        _port = cachedPort;
                        _token = cachedToken;
                        Debug.WriteLine($"[LcuManager] 进程检测不到，强制使用缓存 → Port:{_port}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 获取凭证失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 带重试的连接
        /// </summary>
        private async Task<bool> TryConnectWithRetryAsync(int maxAttempts, int delayMs)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                if (_cts.IsCancellationRequested) return false;

                try
                {
                    // 每次重试都重新读一次凭证
                    await TryGetCredentialsAsync();

                    await ConnectHttpAndWsAsync();
                    Debug.WriteLine($"[LcuManager] 第 {i} 次尝试连接成功");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 第 {i}/{maxAttempts} 次连接失败: {ex.Message}");

                    try
                    {
                        _ws?.Abort();
                        _ws?.Dispose();
                    }
                    catch { }
                    _ws = null;

                    if (i < maxAttempts)
                        await SafeDelay(delayMs);
                }
            }
            return false;
        }

        private async Task ConnectHttpAndWsAsync()
        {
            // --- HTTP ---
            HttpClient?.HttpClient?.Dispose();
            HttpClient = new LcuClient(_port, _token);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");

            // 刚登录或网络抖动时 current-summoner 可能暂时失败，只要不是「连接被拒绝」就算 HTTP 通了
            if (!response.IsSuccessStatusCode)
            {
                response = await HttpClient.GetAsync("/riotclient/app-name");
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"HTTP 测试失败: {response.StatusCode}");
            }

            // --- WebSocket ---
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            _ws.Options.RemoteCertificateValidationCallback = (s, c, ch, e) => true;
            _ws.Options.SetRequestHeader("Authorization",
                "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_token}")));

            var uri = new Uri($"wss://127.0.0.1:{_port}/");
            await _ws.ConnectAsync(uri, _cts.Token);

            // 订阅 phase
            await SendWsAsync("[5,\"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
        }

        /// <summary>
        /// 等待 current-summoner 真正可用（登录完成）
        /// 更宽松：网络抖动后客户端自己重连时可能需要更长时间
        /// </summary>
        private async Task<bool> WaitForSummonerReadyAsync(int maxAttempts = 45, int delayMs = 800)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                if (_cts == null || _cts.IsCancellationRequested)
                    return false;

                try
                {
                    var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var obj = JObject.Parse(json);
                            if (!string.IsNullOrEmpty(obj["puuid"]?.ToString()))
                            {
                                Debug.WriteLine($"[LcuManager] current-summoner 已就绪（第 {i} 次）");
                                return true;
                            }
                        }
                    }
                    Debug.WriteLine($"[LcuManager] current-summoner 未就绪 ({i}/{maxAttempts}) status={response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LcuManager] 等待 summoner 异常 ({i}/{maxAttempts}): {ex.Message}");
                }

                await SafeDelay(delayMs);
            }

            Debug.WriteLine("[LcuManager] 等待 current-summoner 超时");
            return false;
        }

        // ========== 接收循环 + 健康检查（永久方案核心） ==========
        private async Task ReceiveLoopWithHealthCheckAsync()
        {
            var buffer = new byte[16384];
            var messageBuffer = new StringBuilder();
            var lastHealthCheck = DateTime.UtcNow;
            const int HealthCheckIntervalSeconds = 20;   // 稍微放宽一点

            // 单独的健康检查取消源（只在需要主动断开时用）
            using var healthCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

            // 启动后台健康检查任务
            var healthTask = Task.Run(async () =>
            {
                while (!healthCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(HealthCheckIntervalSeconds), healthCts.Token);

                        bool healthy = await DoHealthCheckAsync();
                        if (!healthy)
                        {
                            Debug.WriteLine("[LcuManager] 健康检查失败，主动中断 WebSocket 以触发重连");
                            try
                            {
                                _ws?.Abort();   // 强制让下面的 Receive 退出
                            }
                            catch { }
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LcuManager] 健康检查任务异常: {ex.Message}");
                    }
                }
            }, healthCts.Token);

            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    try
                    {
                        // 正常长接收，不再用短超时取消
                        var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.WriteLine("[LcuManager] WebSocket 收到关闭帧");
                            break;
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                            if (result.EndOfMessage)
                            {
                                string raw = messageBuffer.ToString();
                                messageBuffer.Clear();
                                await ProcessWsMessageAsync(raw);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (WebSocketException ex)
                    {
                        Debug.WriteLine($"[LcuManager] WebSocket 异常: {ex.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LcuManager] 接收异常: {ex.Message}");
                        // 轻微异常继续尝试
                    }
                }
            }
            finally
            {
                // 停止健康检查任务
                try { healthCts.Cancel(); } catch { }
                try { await healthTask; } catch { }
            }
        }

        /// <summary>
        /// 健康检查：轻量 ping，确认会话还活着
        /// </summary>
        private async Task<bool> DoHealthCheckAsync()
        {
            try
            {
                if (HttpClient == null) return false;

                // 用独立的短超时，避免影响主连接
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                // 优先测最关键的端点
                var response = await HttpClient.GetAsync("/lol-summoner/v1/current-summoner");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var obj = JObject.Parse(json);
                        if (!string.IsNullOrEmpty(obj["puuid"]?.ToString()))
                        {
                            return true;
                        }
                    }
                }

                // 兜底测一个几乎总会存在的端点
                response = await HttpClient.GetAsync("/riotclient/app-name");
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[LcuManager] 健康检查超时");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 健康检查异常: {ex.Message}");
                return false;
            }
        }

        private async Task ProcessWsMessageAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            try
            {
                var arr = JArray.Parse(raw);
                if (arr.Count < 3 || arr[0].Value<int>() != 8) return;

                var payload = arr[2] as JObject;
                if (payload == null) return;

                string uri = payload["uri"]?.ToString();
                var data = payload["data"];

                if (uri == "/lol-gameflow/v1/gameflow-phase" && data != null)
                {
                    string phase = data.Type == JTokenType.String
                        ? data.Value<string>()?.Trim('"')
                        : data.ToString();

                    if (!string.IsNullOrEmpty(phase) && phase != _lastPhase)
                    {
                        string oldPhase = _lastPhase;
                        _lastPhase = phase;

                        Debug.WriteLine($"[LcuManager] Phase: {oldPhase ?? "null"} → {phase}");

                        if (PhaseChanged != null)
                            await PhaseChanged.Invoke(phase, oldPhase);
                    }
                }
            }
            catch (JsonReaderException)
            {
                // 非 JSON 或空帧，忽略
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 解析消息失败: {ex.Message}");
            }
        }

        // ========== 主动查询当前 phase ==========
        private async Task QueryCurrentPhaseAsync()
        {
            try
            {
                var response = await HttpClient.GetAsync("/lol-gameflow/v1/gameflow-phase");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    string phase = content.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(phase) && phase != _lastPhase)
                    {
                        string old = _lastPhase;
                        _lastPhase = phase;
                        if (PhaseChanged != null)
                            await PhaseChanged.Invoke(phase, old);
                    }
                }
            }
            catch { /* 忽略 */ }
        }

        private async Task SendWsAsync(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private void DisconnectInternal()
        {
            try
            {
                if (_ws != null)
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        try
                        {
                            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                               .Wait(500);
                        }
                        catch { }
                    }
                    _ws.Dispose();
                }
            }
            catch { }

            _ws = null;
            // 注意：这里不强制改 State，由调用方决定
        }

        private async Task SafeDelay(int ms)
        {
            try { await Task.Delay(ms, _cts.Token); }
            catch (OperationCanceledException) { }
        }

        // ==================== 缓存读写 ====================
        private void SaveCache(string port, string token)
        {
            try
            {
                var data = new
                {
                    Port = port,
                    Token = token,
                    Time = DateTime.Now
                };
                File.WriteAllText(_cacheFilePath, JsonConvert.SerializeObject(data, Formatting.Indented));
                Debug.WriteLine($"[LcuManager] 已更新缓存 → {_cacheFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 保存缓存失败: {ex.Message}");
            }
        }

        private bool LoadCache(out string port, out string token)
        {
            port = null;
            token = null;

            try
            {
                if (!File.Exists(_cacheFilePath)) return false;

                var json = File.ReadAllText(_cacheFilePath);
                var obj = JObject.Parse(json);

                var time = obj["Time"]?.ToObject<DateTime>() ?? DateTime.MinValue;
                if ((DateTime.Now - time).TotalMinutes > CacheValidMinutes)
                {
                    Debug.WriteLine("[LcuManager] 缓存已过期");
                    return false;
                }

                port = obj["Port"]?.ToString();
                token = obj["Token"]?.ToString();

                return !string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 读取缓存失败: {ex.Message}");
                return false;
            }
        }

        private void ClearCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    File.Delete(_cacheFilePath);
                    Debug.WriteLine("[LcuManager] 缓存已清除");
                }
            }
            catch { }
        }

        private string Clean(string value)
        {
            return value?.Trim().Trim('"', '\'', ' ', '\r', '\n');
        }

        // ========== 辅助方法 ==========
        private string GetCommandLine(Process process)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                    return obj["CommandLine"]?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LcuManager] 获取命令行失败: {ex.Message}");
            }
            return "";
        }

        private string ExtractArgument(string cmdLine, string key)
        {
            var match = Regex.Match(cmdLine, key + "([^ ]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public void Dispose()
        {
            Stop();
            HttpClient?.HttpClient?.Dispose();
        }
    }
}