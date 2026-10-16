using System.Diagnostics;
using static League.FormMain;

namespace League.Services
{
    public class AutoQueueAccepter
    {
        private readonly FormMain _form;
        private bool _hasAcceptedReadyCheck = false;
        private CancellationTokenSource? _pending;

        public AutoQueueAccepter(FormMain form) => _form = form;

        public async Task TryAcceptAsync(int delaySeconds = 0)
        {
            if (_hasAcceptedReadyCheck || _pending is { IsCancellationRequested: false })
            {
                return;
            }

            var pending = _pending = new CancellationTokenSource();
            var token = pending.Token;
            delaySeconds = delaySeconds is 5 or 10 ? delaySeconds : 0;

            try
            {
                if (delaySeconds > 0)
                {
                    AppLog.Shared.Info(LogCategory.Automation, $"找到对局，{delaySeconds} 秒后自动接受");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }

                // 延迟后再尝试接受（最多重试3次）
                for (int i = 0; i < 3; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (_form.GetAppConfig()?.EnableAutoAcceptQueue != true)
                    {
                        return;
                    }

                    var client = Globals.lcuClient?.Client;
                    if (client == null)
                    {
                        return;
                    }

                    string phase = await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase", token);
                    if (phase.Trim().Trim('"') != "ReadyCheck")
                    {
                        return;
                    }

                    bool success = await Globals.lcuClient.AcceptReadyCheckAsync();
                    if (success)
                    {
                        _hasAcceptedReadyCheck = true;
                        AppLog.Shared.Info(
                            LogCategory.Automation,
                            $"已自动接受对局（第 {i + 1} 次尝试，延迟 {delaySeconds} 秒）"
                        );
                        return;
                    }
                    await Task.Delay(300, token);
                }
                AppLog.Shared.Warn(LogCategory.Automation, "自动接受对局失败：3 次尝试都未成功");
            }
            // Reset() or a newer ready check cancelled this attempt; nothing to accept.
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Automation, "[自动接受] 异常", ex);
            }
            finally
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }

                pending.Dispose();
            }
        }

        public void Reset()
        {
            _hasAcceptedReadyCheck = false;
            _pending?.Cancel();
        }
    }
}
