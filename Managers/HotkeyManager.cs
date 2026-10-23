using System.Diagnostics;
using League.Models;
using League.Services;
using static League.FormMain;

namespace League.Managers
{
    public class HotkeyManager : IDisposable
    {
        private readonly FormMain _mainForm;
        private GameHotkeyWindow? _hotkeyWindow;
        private IReadOnlyDictionary<Keys, GameHotkeyAction> _actions = new Dictionary<Keys, GameHotkeyAction>();
        private bool _disposed;

        public event Action? OnMyTeamHotkey;
        public event Action? OnFullTeamHotkey;
        public event Action? OnChampSelectF7;

        /// <summary>Nothing is registered until <see cref="ApplyConfig"/> names the shortcuts.</summary>
        public HotkeyManager(FormMain mainForm)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            Services.ChatSendDiagnostics.Shared.Report(
                "permission",
                Services.GameInputService.HasInputPermission()
                    ? "管理员权限已就绪。快捷键使用 Windows 事件接收，无键盘钩子或按键轮询。发送前请关闭聊天输入框。"
                    : "当前没有管理员权限，请以管理员身份重新启动程序。"
            );
        }

        /// <summary>
        /// Registers the configured shortcuts, replacing the previous ones; unchanged shortcuts are left registered.
        /// </summary>
        public void ApplyConfig(LeagueConfig? config)
        {
            if (_disposed)
            {
                return;
            }

            // A hand-edited config can bind one key twice; the first action keeps it.
            var actions = new Dictionary<Keys, GameHotkeyAction>();
            foreach (var (action, keys) in GameHotkeys.From(config))
            {
                if (!actions.TryAdd(keys, action))
                {
                    ChatSendDiagnostics.Shared.Report(
                        "hotkey-duplicate",
                        $"{GameHotkeys.Format(keys)} 同时设给了两个动作，只保留第一个，请在设置中修改。"
                    );
                }
            }

            if (_hotkeyWindow != null && actions.Keys.ToHashSet().SetEquals(_actions.Keys))
            {
                _actions = actions;
                return;
            }

            _hotkeyWindow?.Dispose();
            _actions = actions;
            _hotkeyWindow = new GameHotkeyWindow(QueueHotkey, actions.Keys.ToArray());
        }

        private void QueueHotkey(Keys shortcut)
        {
            if (_disposed || _mainForm.IsDisposed || !_mainForm.IsHandleCreated)
            {
                return;
            }

            try
            {
                // Always leave the native message callback before starting application work.
                _mainForm.BeginInvoke(
                    (Action)(
                        async () =>
                        {
                            if (!_disposed && _actions.TryGetValue(shortcut, out var action))
                            {
                                await HandleHotkeyAsync(action, GameHotkeys.Format(shortcut));
                            }
                        }
                    )
                );
            }
            // The window handle can be destroyed between the check above and BeginInvoke while closing; the hotkey is dropped.
            catch (InvalidOperationException) { }
        }

        private async Task HandleHotkeyAsync(GameHotkeyAction action, string label)
        {
            try
            {
                string phase = await GetGameflowPhaseSafe();

                if (action == GameHotkeyAction.ChampSelect)
                {
                    if (phase == "ChampSelect")
                    {
                        Debug.WriteLine($"[HotkeyManager] {label} 已触发 (选人阶段)");
                        OnChampSelectF7?.Invoke();
                    }
                    else
                    {
                        Debug.WriteLine($"[HotkeyManager] {label} 已忽略 (当前阶段: {phase})");
                    }
                }
                else if (phase == "InProgress")
                {
                    if (action == GameHotkeyAction.MyTeam)
                    {
                        Debug.WriteLine($"[HotkeyManager] {label} 已触发 → 发送我方队伍");
                        OnMyTeamHotkey?.Invoke();
                    }
                    else
                    {
                        Debug.WriteLine($"[HotkeyManager] {label} 已触发 → 发送全队信息");
                        OnFullTeamHotkey?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[HotkeyManager] 处理热键异常", ex);
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
            {
                return;
            }

            _disposed = true;
            _hotkeyWindow?.Dispose();
            OnMyTeamHotkey = null;
            OnFullTeamHotkey = null;
            OnChampSelectF7 = null;
        }
    }
}
