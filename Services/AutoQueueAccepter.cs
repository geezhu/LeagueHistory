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
            if (_hasAcceptedReadyCheck || _pending is {IsCancellationRequested:false}) return;
            var pending=_pending=new CancellationTokenSource();
            var token=pending.Token;
            delaySeconds=delaySeconds is 5 or 10?delaySeconds:0;

            try
            {
                if (delaySeconds > 0)
                {
                    Debug.WriteLine($"[自动接受] 等待 {delaySeconds} 秒后接受...");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds),token);
                }

                // 延迟后再尝试接受（最多重试3次）
                for (int i = 0; i < 3; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if(_form.GetAppConfig()?.EnableAutoAcceptQueue!=true)return;
                    var client=Globals.lcuClient?.Client;
                    if(client==null || (await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase",token)).Trim().Trim('"')!="ReadyCheck")return;
                    bool success = await Globals.lcuClient.AcceptReadyCheckAsync();
                    if (success)
                    {
                        _hasAcceptedReadyCheck = true;
                        Debug.WriteLine($"[自动接受] ✅ 第 {i + 1} 次尝试成功（延迟 {delaySeconds}s）");
                        return;
                    }
                    await Task.Delay(300,token);
                }
                Debug.WriteLine("[自动接受] 多次尝试后仍失败");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[自动接受] 异常: {ex.Message}");
            }
            finally {if(ReferenceEquals(_pending,pending))_pending=null;pending.Dispose();}
        }

        public void Reset() {_hasAcceptedReadyCheck=false;_pending?.Cancel();}
    }
}