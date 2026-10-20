//using League.Extensions;
//using System.Diagnostics;
//using System.Runtime.InteropServices;
//using static League.FormMain;

//namespace League.Managers
//{
//    public class HotkeyManager : IDisposable
//    {
//        private IntPtr _hookId = IntPtr.Zero;
//        private LowLevelKeyboardProc? _proc;
//        private DateTime _lastHookTrigger = DateTime.MinValue;
//        private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(800);

//        private readonly FormMain _mainForm;

//        public event Action? OnMyTeamHotkey;      // F9  发送我方
//        public event Action? OnFullTeamHotkey;    // F11  发送全队（原F12）
//        public event Action? OnChampSelectF7;     // Ctrl + F7

//        public HotkeyManager(FormMain mainForm)
//        {
//            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
//            InstallKeyboardHook();
//        }

//        #region Windows API 声明
//        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

//        private const int WH_KEYBOARD_LL = 13;
//        private const int WM_KEYDOWN = 0x0100;

//        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
//        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

//        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
//        [return: MarshalAs(UnmanagedType.Bool)]
//        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

//        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
//        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

//        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
//        private static extern IntPtr GetModuleHandle(string lpModuleName);

//        [DllImport("user32.dll")]
//        private static extern short GetAsyncKeyState(int vKey);
//        #endregion

//        private void InstallKeyboardHook()
//        {
//            _proc = HookCallback;
//            using var curModule = Process.GetCurrentProcess().MainModule!;
//            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

//            if (_hookId == IntPtr.Zero)
//                Debug.WriteLine($"[HotkeyManager] 安装失败，错误码: {Marshal.GetLastWin32Error()}");
//            else
//                Debug.WriteLine("[HotkeyManager] 钩子安装成功");
//        }

//        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
//        {
//            if (nCode < 0)
//                return CallNextHookEx(_hookId, nCode, wParam, lParam);

//            try
//            {
//                if (wParam == (IntPtr)WM_KEYDOWN)
//                {
//                    int vkCode = Marshal.ReadInt32(lParam);
//                    Keys key = (Keys)vkCode;
//                    bool isCtrlDown = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;

//                    // 判断是否是我们需要处理的热键（F12 不再拦截）
//                    bool isTargetKey = (key == Keys.F9 || key == Keys.F11 ||
//                                      (isCtrlDown && key == Keys.F7));

//                    if (isTargetKey)
//                    {
//                        if (DateTime.Now - _lastHookTrigger < _debounceInterval)
//                            return (IntPtr)1;

//                        _lastHookTrigger = DateTime.Now;

//                        Debug.WriteLine($"[HotkeyManager] 热键按下: {key} (Ctrl={isCtrlDown})");

//                        _mainForm.InvokeIfRequiredAsync(async () =>
//                            await HandleHotkeyAsync(key, isCtrlDown));

//                        return (IntPtr)1; // 吞掉按键
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[HotkeyManager] HookCallback 异常: {ex.Message}");
//            }

//            return CallNextHookEx(_hookId, nCode, wParam, lParam);
//        }

//        private async Task HandleHotkeyAsync(Keys key, bool isCtrlDown)
//        {
//            try
//            {
//                string phase = await GetGameflowPhaseSafe();

//                if (key == Keys.F7 && isCtrlDown)
//                {
//                    if (phase == "ChampSelect")
//                    {
//                        Debug.WriteLine("[HotkeyManager] Ctrl+F7 已触发 (选人阶段)");
//                        OnChampSelectF7?.Invoke();
//                    }
//                    else
//                    {
//                        Debug.WriteLine($"[HotkeyManager] Ctrl+F7 已忽略 (当前阶段: {phase})");
//                    }
//                }
//                else if (phase == "InProgress")
//                {
//                    if (key == Keys.F9)
//                    {
//                        Debug.WriteLine("[HotkeyManager] F9 已触发 → 发送我方队伍");
//                        OnMyTeamHotkey?.Invoke();
//                    }
//                    else if (key == Keys.F11)
//                    {
//                        Debug.WriteLine("[HotkeyManager] F11 已触发 → 发送全队信息");
//                        OnFullTeamHotkey?.Invoke();
//                    }
//                }
//                else
//                {
//                    // 可注释掉这行，减少非游戏阶段的日志
//                    // Debug.WriteLine($"[HotkeyManager] 热键 {key} 已忽略 (当前阶段: {phase})");
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"[HotkeyManager] 处理热键异常: {ex.Message}");
//            }
//        }

//        private async Task<string> GetGameflowPhaseSafe()
//        {
//            try
//            {
//                return await Task.Run(async () => await Globals.lcuClient.GetGameflowPhase())
//                                 .WaitAsync(TimeSpan.FromSeconds(3));
//            }
//            catch
//            {
//                return "Unknown";
//            }
//        }

//        public void Dispose()
//        {
//            if (_hookId != IntPtr.Zero)
//            {
//                UnhookWindowsHookEx(_hookId);
//                _hookId = IntPtr.Zero;
//                Debug.WriteLine("[HotkeyManager] 钩子已卸载");
//            }

//            OnMyTeamHotkey = null;
//            OnFullTeamHotkey = null;
//            OnChampSelectF7 = null;
//        }
//    }
//}

using League.Extensions;
using League.Models;
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

        private HotkeySpec _champSelect = HotkeySpec.Parse("Ctrl+F7");
        private HotkeySpec _myTeam = HotkeySpec.Parse("F9");
        private HotkeySpec _fullTeam = HotkeySpec.Parse("F11");

        public event Action? OnMyTeamHotkey;      // 游戏内·我方
        public event Action? OnFullTeamHotkey;    // 游戏内·全队
        public event Action? OnChampSelectF7;     // 选人阶段发送（事件名可保持）

        public HotkeyManager(FormMain mainForm)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            InstallKeyboardHook();
        }

        /// <summary>从配置刷新三个热键</summary>
        public void ApplyConfig(LeagueConfig? config)
        {
            if (config == null) return;

            _champSelect = HotkeySpec.Parse(config.HotkeyChampSelect, "Ctrl+F7");
            _myTeam = HotkeySpec.Parse(config.HotkeyMyTeam, "F9");
            _fullTeam = HotkeySpec.Parse(config.HotkeyFullTeam, "F11");

            Debug.WriteLine($"[HotkeyManager] 热键已更新: 选人={_champSelect} 我方={_myTeam} 全队={_fullTeam}");
        }

        #region Windows API
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
                Debug.WriteLine($"[HotkeyManager] 安装失败，错误码: {Marshal.GetLastWin32Error()}");
            else
                Debug.WriteLine("[HotkeyManager] 钩子安装成功");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            try
            {
                // 正在热键框里改键时，不拦截、不触发发送
                if (_mainForm.IsHotkeyCaptureFocused())
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Keys key = (Keys)vkCode;
                    bool isCtrlDown = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;

                    bool isTargetKey =
                        _champSelect.Matches(key, isCtrlDown) ||
                        _myTeam.Matches(key, isCtrlDown) ||
                        _fullTeam.Matches(key, isCtrlDown);

                    if (isTargetKey)
                    {
                        if (DateTime.Now - _lastHookTrigger < _debounceInterval)
                            return (IntPtr)1;

                        _lastHookTrigger = DateTime.Now;
                        Debug.WriteLine($"[HotkeyManager] 热键按下: {key} (Ctrl={isCtrlDown})");

                        _mainForm.InvokeIfRequiredAsync(async () =>
                            await HandleHotkeyAsync(key, isCtrlDown));

                        return (IntPtr)1; // 吞掉，避免进游戏
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
                    if (_myTeam.Matches(key, isCtrlDown))
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
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Debug.WriteLine("[HotkeyManager] 钩子已卸载");
            }

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