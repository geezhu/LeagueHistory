using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly OverlayChatRequests _chatRequests=new();
    private bool _readingChat,_sendingChat;
    private async Task ReadChatCommand()
    {
        if(_disposed || _readingChat || !UseGameBar || !(_owner.GetAppConfig()?.EnableLoadingOverlay??true))return;
        _readingChat=true;
        try
        {
            var request=await _gameBar.ReadChatRequestAsync();
            if(_disposed || request==null || !_chatRequests.Take(request))return;
            if(_sendingChat){_gameBar.PublishChatStatus(request,"busy","上一条消息仍在处理，请稍后重试。",true);return;}
            _sendingChat=true;
            _=SendChatCommand(request);
        }
        finally{_readingChat=false;}
    }
    private async Task SendChatCommand(OverlayChatRequest request)
    {
        string state="cancelled",message="未发送：请求已取消或对局已变化。";
        void Report(string next,string text){state=next;message=text;_gameBar.PublishChatStatus(request,state,message,false);}
        int generation=_generation;
        var roster=_roster;
        var players=roster==null?[]:OverlayTierChatMessage.Select(roster,request);
        bool currentRequest=true;
        bool Current()=>currentRequest && !_disposed && !_life.IsCancellationRequested && generation==_generation &&
            UseGameBar && (_owner.GetAppConfig()?.EnableLoadingOverlay??true) && _phase=="InProgress" &&
            Environment.TickCount64-_lastPhaseRead<15000 && _roster!=null &&
            players.Length>0 && players.All(p=>_roster.Players.Any(q=>p.Key==q.Key && p.ChampionId==q.ChampionId && p.Team==q.Team));
        try
        {
            if(request.Cancelled)return;
            if(_phase!="InProgress"){state="phase";message="进入游戏后才能发送；加载界面只提供查看。";return;}
            if(!GameInputService.HasInputPermission()){state="permission";message="未发送：请以管理员身份启动 League.exe。";return;}
            if(!Current())return;
            IReadOnlyList<string> DeathMessages()
            {
                if(_lastDeathStreakRead==0 || Environment.TickCount64-_lastDeathStreakRead>=15000)
                    throw new InvalidOperationException("未发送：本局事件数据未更新，请等待 2999 连接恢复。");
                return OverlayDeathStreakChatMessage.Build(_roster!,request,_deathStreaks,Champion);
            }
            var messages=request.Kind switch
            {
                "mastery"=>OverlayMasteryChatMessage.Build(roster!,request,_masteries,Champion),
                "deathStreak"=>DeathMessages(),
                _=>OverlayTierChatMessage.Build(roster!,request,_ranks,Champion)
            };
            Report("waiting","请在 20 秒内按 Win＋G 返回游戏，保持聊天输入框关闭；返回后发送到游戏聊天。");
            var gate=new OverlayChatReady(request);
            var readiness=OverlayChatReadiness.Waiting;
            while(Current())
            {
                var latest=await _gameBar.ReadChatRequestAsync();
                // SDK dismissal and foreground game must both agree. Pinning is not required.
                readiness=gate.Observe(latest,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    latest?.Dismissed==true && GameInputService.ModifiersReleased() && GameInputService.IsGameForeground());
                if(readiness!=OverlayChatReadiness.Waiting)break;
                await Task.Delay(200,_life.Token);
            }
            if(!Current() || readiness==OverlayChatReadiness.Cancelled){state="cancelled";message="未发送：请求已取消或对局已变化。";return;}
            if(readiness!=OverlayChatReadiness.Ready){state="timeout";message="未发送：等待返回游戏超时，请重新点击发送。";return;}
            // Counts may change while the user is in Game Bar; use the latest polled events.
            if(request.Kind=="deathStreak")messages=DeathMessages();
            // Keep observing cancellation / Game Bar reopening while input is in flight.
            using var monitor=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            async Task Observe()
            {
                try
                {
                    while(!monitor.IsCancellationRequested)
                    {
                        var latest=await _gameBar.ReadChatRequestAsync();
                        currentRequest=latest!=null && request.SameCommand(latest) && !latest.Cancelled && latest.Dismissed;
                        if(!currentRequest)return;
                        await Task.Delay(100,monitor.Token);
                    }
                }
                catch(OperationCanceledException){ }
            }
            var watching=Observe();
            try{await _owner.SendOverlayTiersAsync(messages,Report,Current,monitor.Token);}
            finally{monitor.Cancel();await watching;}
        }
        catch(OperationCanceledException){state="cancelled";message="未发送：请求已取消。";}
        catch(InvalidOperationException e){state="unavailable";message=e.Message;}
        catch{state="error";message="未发送：读取请求失败，请重新点击发送。";}
        finally{_sendingChat=false;_gameBar.PublishChatStatus(request,state,message,true);}
    }
}
