using System.Diagnostics;
using League.Services;

namespace League.Infrastructure
{
    public class AsyncPoller
    {
        private CancellationTokenSource _cts;

        public void Start(Func<Task> action, int intervalMs = 1000)
        {
            Stop(); // 确保只有一个实例在跑
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(
                async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await action();
                        }
                        catch (Exception ex)
                        {
                            AppLog.Shared.Warn(LogCategory.App, "轮询异常", ex);
                        }

                        await Task.Delay(intervalMs, token);
                    }
                },
                token
            );
        }

        public void Stop()
        {
            _cts?.Cancel();
        }
    }
}
