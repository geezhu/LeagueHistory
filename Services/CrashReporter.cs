using Newtonsoft.Json;

namespace League.Services;

public sealed record CrashMarker(DateTime Time, string Version, string Commit, string Kind, string Error);

/// <summary>
/// 全局异常记录。界面线程的未处理异常写日志后提示用户、程序继续运行（与 WinForms 默认对话框的「继续」一致）；
/// 后台线程的未处理异常会结束进程，先同步写日志并留下标记，下次启动时提示导出诊断报告。
/// </summary>
public static class CrashReporter
{
    private static readonly string MarkerPath = Path.Combine(LogRetention.LogsDirectory, "last-crash.json");

    /// <summary>必须在创建任何窗口之前调用。</summary>
    public static void Install()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => OnUiThreadException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
            {
                OnFatalException(error);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            AppLog.Shared.Warn(LogCategory.Crash, "后台任务的异常没有被处理", e.Exception);
    }

    /// <summary>读取并删除上次异常退出留下的标记；没有时返回 null。</summary>
    public static CrashMarker? TakeLastCrash(string? path = null)
    {
        path ??= MarkerPath;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var marker = JsonConvert.DeserializeObject<CrashMarker>(File.ReadAllText(path));
            File.Delete(path);
            return marker;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 标记损坏或删不掉时当作没有，不能因此影响启动。
            return null;
        }
    }

    public static void WriteMarker(string kind, Exception error, string? path = null)
    {
        path ??= MarkerPath;
        var build = Infrastructure.BuildInfo.Current;
        var marker = new CrashMarker(
            DateTime.Now,
            build.Version,
            build.ShortCommit,
            kind,
            DiagnosticRedactor.Shared.Redact($"{error.GetType().Name}: {error.Message}")
        );
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(marker));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 进程正在退出，写不了标记只少一次启动提示，日志里仍有记录。
        }
    }

    private static long _lastPromptTick = -30_000;

    private static void OnUiThreadException(Exception error)
    {
        AppLog.Shared.Crash("界面线程未处理异常", error);
        // 绘制中的异常每次重绘都会再触发，提示框限 30 秒一次，避免连续弹窗；日志每次都记。
        long now = Environment.TickCount64;
        if (now - _lastPromptTick < 30_000)
        {
            return;
        }

        _lastPromptTick = now;
        MessageBox.Show(
            "程序遇到了一个错误，详细信息已写入日志，可以继续使用。\n"
                + "如果反复出现，请在「设置 → 功能辅助 → 关于与交流」导出诊断报告并反馈。",
            "联盟视界",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning
        );
    }

    private static void OnFatalException(Exception error)
    {
        AppLog.Shared.Crash("未处理异常，程序将退出", error);
        WriteMarker("fatal", error);
    }
}
