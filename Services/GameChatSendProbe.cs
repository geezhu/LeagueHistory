using League.Clients;

namespace League.Services;

/// <summary>Explicit, one-shot diagnostic mode; never enabled during normal startup.</summary>
internal static class GameChatSendProbe
{
    public static async Task<int> RunAsync(bool enterOnly = false, bool shortReturn = false)
    {
        var log = ChatSendDiagnostics.Shared;
        void PrintStatus() => Console.WriteLine($"[{log.Current.Stage}] {log.Current.Message}");
        log.Changed += PrintStatus;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var live = new LiveMatchClient();
        string? lastWait = null;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            bool administrator = new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            log.Report("probe-permission", administrator ? "已确认测试进程具有管理员权限。" : "测试进程没有管理员权限，取消测试。");
            if (!administrator) return 4;
            log.Report("probe-start", "单次聊天测试：等待游戏数据、游戏前台和修饰键释放，最长等待两分钟。");
            DateTime? readySince = null;
            while (!timeout.IsCancellationRequested)
            {
                string? waiting = null;
                try
                {
                    var snapshot = await live.GetAsync(timeout.Token);
                    if (snapshot.Players.Length == 0 || snapshot.Time < 1) waiting = "等待游戏载入完成。";
                }
                catch (Exception ex) when (!timeout.IsCancellationRequested)
                {
                    waiting = "等待游戏实时数据：" + ex.GetType().Name;
                }
                var foreground = GameInputService.GetGameForegroundState();
                if (waiting == null && !foreground.IsGame) waiting = foreground.Reason;
                if (waiting == null && !GameInputService.ModifiersReleased()) waiting = "等待 Ctrl、Shift、Alt 松开。";
                if (waiting != null)
                {
                    readySince = null;
                    if (waiting != lastWait) log.Report("probe-wait", waiting);
                    lastWait = waiting;
                }
                else
                {
                    if (readySince == null)
                    {
                        readySince = DateTime.UtcNow;
                        log.Report("probe-ready", "游戏已就绪，保持前台五秒后尝试一次发送。请保持聊天输入框关闭。");
                    }
                    if (DateTime.UtcNow - readySince.Value >= TimeSpan.FromSeconds(5))
                    {
                        var input = new GameInputService { UseScanCodeKeys = true, ReturnHoldMilliseconds = shortReturn ? null : 70 };
                        log.Report("probe-input", enterOnly ? "开始单次扫描码回车测试，按住 70 毫秒。" : shortReturn ? "开始完整发送对照测试，两次扫描码回车均使用原来的 15～25 毫秒。" : "开始完整发送测试，两次扫描码回车均按住 70 毫秒。");
                        bool CanContinue() => !timeout.IsCancellationRequested && GameInputService.IsGameForeground() && GameInputService.ModifiersReleased();
                        bool completed = enterOnly
                            ? await input.ProbeEnterAsync(CanContinue)
                            : await input.SendInGameMessageAsync("LeagueHistory 管理员发送测试。", CanContinue);
                        log.Report(completed ? "probe-submitted" : "probe-failed", completed
                            ? (enterOnly ? "单次回车输入完成，未输入文字；请确认游戏聊天框是否打开。" : "系统输入序列已完成；需要在游戏聊天中确认是否实际显示，程序无法验证服务器接收。")
                            : "单次输入未完成，不自动重试：" + (input.LastFailureReason ?? GameInputService.GetGameForegroundState().Reason));
                        return completed ? 0 : 3;
                    }
                }
                await Task.Delay(500, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.Report("probe-failed", "单次测试异常：" + ex.GetType().Name);
            return 3;
        }
        finally { await log.FlushAsync(); log.Changed -= PrintStatus; }
        log.Report("probe-timeout", "两分钟内未满足发送条件，测试已退出，没有自动重试。");
        await log.FlushAsync();
        return 2;
    }
}
