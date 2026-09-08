using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace League.Clients;

public static class LcuProcessDiscovery
{
    public static bool TryFind(out string port, out string token)
    {
        port = token = "";
        var processes = Process.GetProcessesByName("LeagueClientUx");
        try
        {
            foreach (var process in processes)
            {
                if (TryReadCommandLine(process.Id,out var commandLine,out var error)
                    && TryParse(commandLine,out port,out token)) return true;
                Debug.WriteLine($"[LCU] PID={process.Id} 原生参数读取未成功，状态={error}，尝试 WMI");
                try
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                    using var results = searcher.Get();
                    foreach (ManagementObject obj in results)
                    {
                        using (obj)
                            if (TryParse(obj["CommandLine"]?.ToString() ?? "",out port,out token)) return true;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[LCU] WMI 参数读取失败：{ex.GetType().Name}，HRESULT={ex.HResult:X8}"); }
            }
            Debug.WriteLine(processes.Length == 0 ? "[LCU] 客户端尚未启动" : "[LCU] 客户端已启动，但无法读取连接参数（权限受限或启动尚未完成）");
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public static bool TryReadCommandLine(int pid, out string commandLine, out string error)
    {
        commandLine = ""; error = "";
        // ProcessCommandLineInformation works with limited query rights; no VM read or debug privilege.
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) { error = $"Win32:{Marshal.GetLastWin32Error()}"; return false; }
        int initialStatus = NtQueryInformationProcess(process,60,IntPtr.Zero,0,out int size);
        if (size <= 0) { error = $"NTSTATUS:{initialStatus:X8}"; return false; }
        for (int attempt=0;attempt<3;attempt++)
        {
            if (size < Marshal.SizeOf<UnicodeString>() || size > 1024*1024) { error = "InvalidBufferSize"; return false; }
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQueryInformationProcess(process,60,buffer,size,out int required);
                if (status < 0)
                {
                    error = $"NTSTATUS:{status:X8}";
                    if (required > size) { size = required; continue; }
                    return false;
                }
                var value = Marshal.PtrToStructure<UnicodeString>(buffer);
                long offset = value.Buffer.ToInt64()-buffer.ToInt64();
                if (value.Length % 2 != 0 || offset < 0 || offset > size-value.Length) { error = "InvalidString"; return false; }
                commandLine = Marshal.PtrToStringUni(value.Buffer,value.Length/2) ?? "";
                return commandLine.Length > 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return false;
    }

    public static bool TryParse(string commandLine, out string port, out string token)
    {
        port = token = "";
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var argv = CommandLineToArgvW(commandLine,out int count);
        if (argv == IntPtr.Zero) return false;
        try
        {
            for (int i=0;i<count;i++)
            {
                string arg = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,i*IntPtr.Size)) ?? "";
                if (arg.StartsWith("--app-port=",StringComparison.Ordinal)) port = arg[11..];
                if (arg.StartsWith("--remoting-auth-token=",StringComparison.Ordinal)) token = arg[22..];
            }
            if (int.TryParse(port,out int number) && number is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(token)) return true;
            port = token = "";
            return false;
        }
        finally { LocalFree(argv); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [DllImport("kernel32.dll",SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process,int infoClass,IntPtr buffer,int length,out int returnLength);
    [DllImport("shell32.dll",CharSet = CharSet.Unicode,SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine,out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
