using League.Extensions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static League.FormMain;

namespace League.Managers
{
    public class HotkeyManager : IDisposable
    {
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;
        private DateTime _lastHookTrigger = DateTime.MinValue;
        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(800);

        private readonly FormMain _mainForm;
        private readonly HashSet<Keys> _heldTierKeys=new();
        private readonly TierHotkeyWindow _tierWindow;
        private readonly System.Threading.Timer _tierPollTimer;
        private readonly TierHotkeyState _tierState = new();
        private readonly object _tierSync = new();
        private readonly Dictionary<Keys, long> _lastTierDispatch = new();
        private bool _disposed;

        public event Action? OnMyTeamHotkey;      // F9  发送我方
        public event Action? OnFullTeamHotkey;    // F11  发送全队（原F12）
        public event Action? OnChampSelectF7;     // Ctrl + F7
        public event Action? OnMyTierHotkey;      // Ctrl + F9
        public event Action? OnEnemyTierHotkey;   // Ctrl + F11

        public HotkeyManager(FormMain mainForm)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            InstallKeyboardHook();
            _tierWindow=new TierHotkeyWindow(key=>QueueTierHotkey(key,"Windows 消息"));
            _tierPollTimer = new System.Threading.Timer(_ => PollTierKeys(), null, 50, 50);
            Services.ChatSendDiagnostics.Shared.Report("hotkey-poll-ready", "快捷键状态检测已启动，与 Windows 消息接收共同工作。");
            Services.ChatSendDiagnostics.Shared.Report("permission", Services.GameInputService.HasInputPermission()
                ? "管理员权限已就绪。游戏内发送前请先关闭聊天输入框，按快捷键后松开 Ctrl。"
                : "当前没有管理员权限，请关闭程序后以管理员身份重新启动，游戏内快捷键可能无法接收。");
        }

        private void PollTierKeys()
        {
            lock (_tierSync)
            {
                if (_disposed) return;
                bool Down(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;
                foreach (var key in _tierState.Read(Down(Keys.LControlKey) || Down(Keys.RControlKey), Down(Keys.F9), Down(Keys.F11)))
                    QueueTierHotkey(key, "按键状态检测");
            }
        }

        private void QueueTierHotkey(Keys key, string source)
        {
            lock (_tierSync)
            {
                if (_disposed) return;
                long now = Environment.TickCount64;
                // The hook, WM_HOTKEY and polling may observe the same press.
                if (_lastTierDispatch.TryGetValue(key, out long previous) && now - previous < 800) return;
                _lastTierDispatch[key] = now;
            }
            Services.ChatSendDiagnostics.Shared.Report("hotkey-received", $"{source}收到 Ctrl + {key}。");
            try
            {
                if (!_mainForm.IsDisposed && _mainForm.IsHandleCreated)
                    _mainForm.BeginInvoke((Action)(async () => { if (!_disposed) await HandleHotkeyAsync(key, true); }));
                else Services.ChatSendDiagnostics.Shared.Report("hotkey-dispatch-error", "快捷键已收到，但主窗口尚未就绪。");
            }
            catch (InvalidOperationException)
            {
                Services.ChatSendDiagnostics.Shared.Report("hotkey-dispatch-error", "快捷键已收到，但主窗口已关闭。");
            }
        }

        #region Windows API 声明
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        #endregion

        private void InstallKeyboardHook()
        {
            _proc = HookCallback;
            using var curModule = Process.GetCurrentProcess().MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
                Services.ChatSendDiagnostics.Shared.Report("hook-error",$"快捷键监听安装失败，系统错误码 {Marshal.GetLastWin32Error()}。");
            else
            {
                Services.ChatSendDiagnostics.Shared.Report("ready",$"快捷键监听已启动，实例 {Environment.ProcessId}。");
                var processes=Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName);
                try{if(processes.Length>1)Services.ChatSendDiagnostics.Shared.Report("multiple-instances",$"检测到 {processes.Length} 个程序实例，快捷键可能冲突；请仅保留需要使用的版本。");}
                finally{foreach(var process in processes)process.Dispose();}
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            try
            {
                if(wParam==(IntPtr)0x0101)_heldTierKeys.Remove((Keys)Marshal.ReadInt32(lParam));
                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Keys key = (Keys)vkCode;
                    bool isCtrlDown = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
                    // Let Windows generate WM_HOTKEY; swallowing this key would suppress it.
                    if(isCtrlDown && key is Keys.F9 or Keys.F11)
                    {
                        if (_heldTierKeys.Add(key)) QueueTierHotkey(key, "键盘监听");
                        if (_tierWindow?.Handles(key)==true) return CallNextHookEx(_hookId,nCode,wParam,lParam);
                        return (IntPtr)1;
                    }
                    if(_heldTierKeys.Contains(key))return (IntPtr)1;

                    // 判断是否是我们需要处理的热键（F12 不再拦截）
                    bool isTargetKey = (key == Keys.F9 || key == Keys.F11 ||
                                      (isCtrlDown && key == Keys.F7));

                    if (isTargetKey)
                    {
                        if (DateTime.Now - _lastHookTrigger < _debounceInterval)
                            return (IntPtr)1;

                        _lastHookTrigger = DateTime.Now;

                        Debug.WriteLine($"[HotkeyManager] 热键按下: {key} (Ctrl={isCtrlDown})");

                        _mainForm.InvokeIfRequiredAsync(async () =>
                            await HandleHotkeyAsync(key, isCtrlDown));

                        return (IntPtr)1; // 吞掉按键
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HotkeyManager] HookCallback 异常: {ex.Message}");
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
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
            lock (_tierSync) _disposed = true;
            _tierPollTimer.Dispose();
            _tierWindow.Dispose();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Debug.WriteLine("[HotkeyManager] 钩子已卸载");
            }

            OnMyTeamHotkey = null;
            OnFullTeamHotkey = null;
            OnChampSelectF7 = null;
            OnMyTierHotkey=null;OnEnemyTierHotkey=null;
        }
    }
}
