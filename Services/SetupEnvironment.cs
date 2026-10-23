using System.ComponentModel;
using System.Diagnostics;

namespace League.Services;

/// <summary>
/// Where the 准备情况 page reads this computer's state and how it restarts elevated. The defaults are the real ones;
/// tests pin each fact, because a developer machine may or may not run elevated or have the component installed, and
/// reading the component starts PowerShell.
/// </summary>
public sealed class SetupEnvironment
{
    public const string WaitForExitArgument = "--wait-for-exit";
    private const int UserCancelled = 1223;

    public Func<bool> IsAdministrator { get; init; } = AppLog.IsAdministrator;

    public Func<CancellationToken, Task<GameBarPackageStatus>> ReadGameBarPackage { get; init; } =
        token => new GameBarPackageInstaller().ReadStatusAsync(token);

    public Func<bool> VoiceModelAvailable { get; init; } = () => VoiceModelStore.Shared.AvailableDirectory != null;

    /// <summary>Starts an elevated copy of this program; <c>false</c> when the user declined the prompt.</summary>
    public Func<bool> StartElevatedCopy { get; init; } = StartElevated;

    // The copy waits for this process to exit first, so the two never hold the global hotkeys at the same time.
    private static bool StartElevated()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"{WaitForExitArgument} {Environment.ProcessId}",
        };
        try
        {
            using var process = Process.Start(start);
            return process != null;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == UserCancelled)
        {
            AppLog.Shared.Info(LogCategory.App, "管理员授权已取消，继续以当前权限运行");
            return false;
        }
    }

    /// <summary>Waits for the process that started this elevated copy, before any window or hotkey exists.</summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        if (args is not [WaitForExitArgument, var text] || !int.TryParse(text, out int id))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(id);
            previous.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // It has already exited.
        }
    }
}
