using System.Diagnostics;
using League.Services;

namespace League.Infrastructure
{
    public class Poller
    {
        private CancellationTokenSource _cts;
        private int _interval;

        public void Start(Func<Task> action, int interval)
        {
            Stop();

            _interval = interval;
            _cts = new CancellationTokenSource();
            // 每个循环只看自己的取消源：重新 Start 后旧循环读到新的 _cts 会继续跑，变成两个循环同时轮询。
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
                            AppLog.Shared.Warn(LogCategory.App, "[Poller异常]", ex);
                        }

                        try
                        {
                            await Task.Delay(_interval, token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                },
                token
            );
        }

        public void Stop()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
    }
}
