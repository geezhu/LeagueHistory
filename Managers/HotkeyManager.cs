using System.Diagnostics;
using static League.FormMain;

namespace League.Managers
{
    public class HotkeyManager : IDisposable
    {
        private readonly FormMain _mainForm;
        private readonly TierHotkeyWindow _hotkeyWindow;
        private bool _disposed;

        public event Action? OnMyTeamHotkey;
        public event Action? OnFullTeamHotkey;
        public event Action? OnChampSelectF7;
        public event Action? OnMyTierHotkey;
        public event Action? OnEnemyTierHotkey;

        public HotkeyManager(FormMain mainForm)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _hotkeyWindow = new TierHotkeyWindow(QueueHotkey);
            Services.ChatSendDiagnostics.Shared.Report("permission", Services.GameInputService.HasInputPermission()
                ? "管理员权限已就绪。快捷键使用 Windows 事件接收，无键盘钩子或按键轮询。发送前请关闭聊天输入框。"
                : "当前没有管理员权限，请以管理员身份重新启动程序。");
        }

        private void QueueHotkey(Keys shortcut)
        {
            if (_disposed || _mainForm.IsDisposed || !_mainForm.IsHandleCreated) return;
            try
            {
                // Always leave the native message callback before starting application work.
                _mainForm.BeginInvoke((Action)(async () =>
                {
                    if (!_disposed)
                        await HandleHotkeyAsync(shortcut & Keys.KeyCode, (shortcut & Keys.Control) != 0);
                }));
            }
            catch (InvalidOperationException) { }
        }

        private async Task HandleHotkeyAsync(Keys key, bool isCtrlDown)
        {
            try
            {
                bool tierKey=isCtrlDown && key is Keys.F9 or Keys.F11;
                if(tierKey)Services.ChatSendDiagnostics.Shared.Report("trigger",key==Keys.F9?"已收到 Ctrl + F9，准备发送我方梯队。":"已收到 Ctrl + F11，准备发送对方梯队。");
                string phase = await GetGameflowPhaseSafe();
                if(tierKey && phase!="InProgress")Services.ChatSendDiagnostics.Shared.Report("phase",$"未发送：当前游戏阶段为 {phase}，需要进入游戏。");

                if (key == Keys.F7 && isCtrlDown)
                {
                    if (phase == "ChampSelect")
                    {
                        Debug.WriteLine("[HotkeyManager] Ctrl+F7 已触发 (选人阶段)");
                        OnChampSelectF7?.Invoke();
                    }
                    else
                    {
                        Debug.WriteLine($"[HotkeyManager] Ctrl+F7 已忽略 (当前阶段: {phase})");
                    }
                }
                else if (phase == "InProgress")
                {
                    if (isCtrlDown && key == Keys.F9)OnMyTierHotkey?.Invoke();
                    else if (isCtrlDown && key == Keys.F11)OnEnemyTierHotkey?.Invoke();
                    else if (key == Keys.F9)
                    {
                        Debug.WriteLine("[HotkeyManager] F9 已触发 → 发送我方队伍");
                        OnMyTeamHotkey?.Invoke();
                    }
                    else if (key == Keys.F11)
                    {
                        Debug.WriteLine("[HotkeyManager] F11 已触发 → 发送全队信息");
                        OnFullTeamHotkey?.Invoke();
                    }
                }
                else
                {
                    // 可注释掉这行，减少非游戏阶段的日志
                    // Debug.WriteLine($"[HotkeyManager] 热键 {key} 已忽略 (当前阶段: {phase})");
                }
            }
            catch (Exception ex)
            {
                if(isCtrlDown && key is Keys.F9 or Keys.F11)Services.ChatSendDiagnostics.Shared.Report("trigger-error","快捷键处理失败："+ex.GetType().Name);
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
            if (_disposed) return;
            _disposed = true;
            _hotkeyWindow.Dispose();
            OnMyTeamHotkey = null;
            OnFullTeamHotkey = null;
            OnChampSelectF7 = null;
            OnMyTierHotkey = null;
            OnEnemyTierHotkey = null;
        }
    }
}
