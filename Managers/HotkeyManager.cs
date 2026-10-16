using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    public class HotkeyManager : IDisposable
    {
        private readonly FormMain _mainForm;
        private readonly GameHotkeyWindow _hotkeyWindow;
        private bool _disposed;

        public event Action? OnMyTeamHotkey;
        public event Action? OnFullTeamHotkey;
        public event Action? OnChampSelectF7;

        public HotkeyManager(FormMain mainForm)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _hotkeyWindow = new GameHotkeyWindow(QueueHotkey);
            Services.ChatSendDiagnostics.Shared.Report(
                "permission",
                Services.GameInputService.HasInputPermission()
                    ? "管理员权限已就绪。快捷键使用 Windows 事件接收，无键盘钩子或按键轮询。发送前请关闭聊天输入框。"
                    : "当前没有管理员权限，请以管理员身份重新启动程序。"
            );
        }

        private void QueueHotkey(Keys shortcut)
        {
            if (_disposed || _mainForm.IsDisposed || !_mainForm.IsHandleCreated)
                return;
            try
            {
                // Always leave the native message callback before starting application work.
                _mainForm.BeginInvoke(
                    (Action)(
                        async () =>
                        {
                            if (!_disposed)
                                await HandleHotkeyAsync(shortcut & Keys.KeyCode, (shortcut & Keys.Control) != 0);
                        }
                    )
                );
            }
            // The window handle can be destroyed between the check above and BeginInvoke while closing; the hotkey is dropped.
            catch (InvalidOperationException) { }
        }

        private async Task HandleHotkeyAsync(Keys key, bool isCtrlDown)
        {
            try
            {
                string phase = await GetGameflowPhaseSafe();

                if (_champSelect.Matches(key, isCtrlDown))
                {
                    if (phase == "ChampSelect")
                    {
                        Debug.WriteLine("[HotkeyManager] 选人热键已触发");
                        OnChampSelectF7?.Invoke();
                    }
                    else
                    {
                        Debug.WriteLine($"[HotkeyManager] 选人热键已忽略 (当前阶段: {phase})");
                    }
                    return;
                }

                if (phase == "InProgress")
                {
                    if (key == Keys.F9)
                    {
                        Debug.WriteLine("[HotkeyManager] 我方热键 → 发送我方队伍");
                        OnMyTeamHotkey?.Invoke();
                    }
                    else if (_fullTeam.Matches(key, isCtrlDown))
                    {
                        Debug.WriteLine("[HotkeyManager] 全队热键 → 发送全队信息");
                        OnFullTeamHotkey?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HotkeyManager] 处理热键异常: {ex.Message}");
            }
        }

        private async Task<string> GetGameflowPhaseSafe()
        {
            try
            {
                return await Task.Run(async () => await Globals.lcuClient.GetGameflowPhase())
                    .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                return "Unknown";
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _hotkeyWindow.Dispose();
            OnMyTeamHotkey = null;
            OnFullTeamHotkey = null;
            OnChampSelectF7 = null;
        }

        private readonly struct HotkeySpec
        {
            public Keys Key { get; }
            public bool RequireCtrl { get; }

            public HotkeySpec(Keys key, bool requireCtrl)
            {
                Key = key;
                RequireCtrl = requireCtrl;
            }

            public bool Matches(Keys key, bool isCtrlDown)
            {
                if (key != Key) return false;
                // 要求 Ctrl 时必须按下；不要求时不能按着 Ctrl（避免和 Ctrl+Fx 冲突）
                return RequireCtrl ? isCtrlDown : !isCtrlDown;
            }

            public override string ToString() =>
                RequireCtrl ? $"Ctrl+{Key}" : Key.ToString();

            public static HotkeySpec Parse(string? text, string fallback = "F9")
            {
                if (string.IsNullOrWhiteSpace(text))
                    text = fallback;

                text = text.Trim();
                bool ctrl = false;
                string keyPart = text;

                if (text.Contains('+'))
                {
                    var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var p in parts)
                    {
                        if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                            p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                        {
                            ctrl = true;
                        }
                        else
                        {
                            keyPart = p;
                        }
                    }
                }

                if (Enum.TryParse<Keys>(keyPart, ignoreCase: true, out var key) && key != Keys.None)
                    return new HotkeySpec(key, ctrl);

                return Parse(fallback, "F9");
            }
        }
    }
}
