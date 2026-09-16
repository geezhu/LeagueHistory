using System.Diagnostics;
using System.Runtime.InteropServices;

namespace League.Services
{
    public class GameInputService
    {
        #region Win32 P/Invoke 定义 (保持不变)
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const ushort VK_RETURN = 0x0D;
        private const ushort SCAN_RETURN = 0x1C;

        private readonly Random _random = new Random();
        public string? LastFailureReason {get;private set;}
        internal bool UseScanCodeKeys { get; init; } = true;
        internal int? ReturnHoldMilliseconds { get; init; } = 70;
        public static bool HasInputPermission()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        private static readonly SemaphoreSlim SendGate=new(1,1);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        public static bool ModifiersReleased()=>new[]{0x10,0x11,0x12,0x5B,0x5C}.All(key=>(GetAsyncKeyState(key)&0x8000)==0);
        public static bool IsGameForeground()
            =>GetGameForegroundState().IsGame;
        public static (bool IsGame,string Reason) GetGameForegroundState()
        {
            uint id=0;
            try
            {
                var window=GetForegroundWindow();
                if(window==IntPtr.Zero)return(false,"当前没有前台窗口");
                GetWindowThreadProcessId(window,out id);
                if(id==0)return(false,"无法取得前台窗口的进程编号");
                // Enumerated processes carry cached process information. GetProcessById followed
                // by ProcessName can instead require a handle to the protected game process.
                var processes=Process.GetProcesses();
                try
                {
                    foreach(var process in processes)
                        if(process.Id==id)
                            return GameWindowIdentity.Matches(id,process.Id,process.ProcessName)
                                ?(true,$"已匹配游戏窗口，PID {id}"):(false,$"前台窗口不属于游戏进程，PID {id}");
                    return(false,$"进程快照中未找到前台进程，PID {id}");
                }
                finally{foreach(var process in processes)process.Dispose();}
            }
            catch(Exception ex){return(false,$"前台进程识别失败，PID {id}，{ex.GetType().Name}，HRESULT {ex.HResult:X8}");}
        }
        #endregion

        internal async Task<bool> ProbeEnterAsync(Func<bool> canContinue)
        {
            if (!await SendGate.WaitAsync(0)) return false;
            try
            {
                if (!canContinue()) return false;
                SendHardwareKey(VK_RETURN, SCAN_RETURN, false);
                try { await Task.Delay(70); }
                finally { SendHardwareKey(VK_RETURN, SCAN_RETURN, true); }
                return true;
            }
            catch (Exception ex) { LastFailureReason = ex.Message; return false; }
            finally { SendGate.Release(); }
        }

        /// <summary>
        /// Inputs chat lines while the game is foreground. The chat box must initially be closed.
        /// </summary>
        public async Task<bool> SendInGameMessageAsync(string message,Func<bool>? canContinue=null)
        {
            LastFailureReason=null;
            if (string.IsNullOrWhiteSpace(message)){LastFailureReason="消息为空";return false;}
            if(!await SendGate.WaitAsync(0)){LastFailureReason="其他消息正在使用游戏输入通道";return false;}

            try
            {
                bool CanSend()
                {
                    if (canContinue?.Invoke() == false) { LastFailureReason = "发送条件已变化，停止输入"; return false; }
                    if (!HasInputPermission()) { LastFailureReason = "请以管理员身份重新启动 League，再使用游戏内发送"; return false; }
                    var foreground = GetGameForegroundState();
                    if (!foreground.IsGame) { LastFailureReason = foreground.Reason; return false; }
                    if (!ModifiersReleased()) { LastFailureReason = "请松开 Ctrl、Shift、Alt 和 Win 后再发送"; return false; }
                    return true;
                }
                string[] lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if(!CanSend())return false;
                    // Open chat; callers must leave the chat box closed before starting.
                    SendHardwareKey(VK_RETURN, SCAN_RETURN, false);
                    try { await Task.Delay(ReturnHoldMilliseconds ?? _random.Next(15, 25)); }
                    finally { SendHardwareKey(VK_RETURN, SCAN_RETURN, true); }

                    // Allow the chat UI to process Enter before inserting text.
                    await Task.Delay(_random.Next(80, 120));
                    if(!CanSend())return false;

                    // Queue Unicode input for the line.
                    SendUnicodeStringInstant(line);

                    // 预留极其短暂的字数缓冲区反应时间
                    await Task.Delay(_random.Next(100, 150));
                    if(!CanSend())return false;

                    // 3. 回车发送
                    SendHardwareKey(VK_RETURN, SCAN_RETURN, false);
                    try { await Task.Delay(ReturnHoldMilliseconds ?? _random.Next(15, 25)); }
                    finally { SendHardwareKey(VK_RETURN, SCAN_RETURN, true); }

                    // 行间延迟（防国服速度过快被屏蔽，留 500ms 足够安全）
                    await Task.Delay(_random.Next(500, 700));
                }

                return true;
            }
            catch (Exception ex)
            {
                LastFailureReason=ex is System.ComponentModel.Win32Exception native?$"系统输入失败，错误码 {native.NativeErrorCode}（也可能受程序权限限制）":$"输入异常：{ex.GetType().Name}";
                Debug.WriteLine($"[秒级输入失败]: {ex.Message}");
                return false;
            }
            finally{SendGate.Release();}
        }

        /// <summary>
        /// Queues UTF-16 key down/up events; acceptance does not confirm game delivery.
        /// </summary>
        private void SendUnicodeStringInstant(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            int len = text.Length;
            // 每个字符包含 按下 和 弹起 2个事件
            INPUT[] inputs = new INPUT[len * 2];
            int inputSize = Marshal.SizeOf(typeof(INPUT));

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                // 填充按下事件
                inputs[i * 2] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYDOWN,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                // 填充弹起事件
                inputs[i * 2 + 1] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = c,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
            }

            // 一条系统指令将整个数组全部发送出去，底层多线程原子化执行
            if(SendInput((uint)inputs.Length, inputs, inputSize)!=(uint)inputs.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private void SendHardwareKey(ushort vk, ushort scan, bool isKeyUp)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                            wVk = UseScanCodeKeys ? (ushort)0 : vk,
                            wScan = scan,
                            dwFlags = (isKeyUp ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN) | (UseScanCodeKeys ? KEYEVENTF_SCANCODE : 0),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            if(SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)))!=1)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
