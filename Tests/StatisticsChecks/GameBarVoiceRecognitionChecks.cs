using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class GameBarVoiceRecognitionChecks
{
    private static void Check(bool value,string note){if(!value)throw new Exception(note);}
    public static async Task RunAsync()
    {
        string directory=Path.Combine(Path.GetTempPath(),"league-voice-bridge-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();string session="main-instance:game/3140";
        var sessions=new List<FakeSession>();
        using var service=new GameBarVoiceRecognition(()=>{var s=new FakeSession();sessions.Add(s);return s;});
        Task Poll(bool enabled=true)=>service.PollAsync(directory,session,enabled,time:now);
        JObject Request(string? id=null,string action="start")=>new(){["protocol"]=1,["id"]=id??Guid.NewGuid().ToString("N"),["session"]=session,
            ["createdAt"]=now,["updatedAt"]=now,["action"]=action,["ack"]=0};
        string path=Path.Combine(directory,"voice-request.json"),statusPath=Path.Combine(directory,"voice-status.json");
        async Task Write(JObject request){request["updatedAt"]=now;await File.WriteAllTextAsync(path,request.ToString());}
        JObject Status()=>JObject.Parse(File.ReadAllText(statusPath));
        try
        {
            var stale=Request();await Write(stale);await File.WriteAllTextAsync(statusPath,"leftover draft");await Poll();
            Check(!File.Exists(path) && !File.Exists(statusPath) && sessions.Count==0,"Startup deletes prior transport and does not replay recording");
            await Poll();Check(sessions.Count==0,"Polling without a start never opens a microphone");
            var invalid=Request();invalid["session"]="old-game";Check(OverlayVoiceRequest.Parse(invalid,session,now)==null,"Old game cannot request audio");
            invalid=Request();invalid["updatedAt"]=now-4000;invalid["createdAt"]=now-4000;Check(OverlayVoiceRequest.Parse(invalid,session,now)==null,"Old lease cannot request audio");
            invalid=Request();invalid["updatedAt"]=now+1;Check(OverlayVoiceRequest.Parse(invalid,session,now)==null,"Future recording requests rejected");
            invalid=Request();invalid["text"]="cannot send";Check(OverlayVoiceRequest.Parse(invalid,session,now)==null,"Recognition protocol cannot carry chat text");
            invalid=Request();invalid["action"]="send";Check(OverlayVoiceRequest.Parse(invalid,session,now)==null,"Recognition protocol cannot authorize sending");
            invalid=Request();invalid["protocol"]="1";Check(OverlayVoiceRequest.Parse(invalid,session,now)==null,"Malformed types rejected");
            var first=Request();await Write(first);await Poll();
            Check(sessions.Count==1 && (string?)Status()["state"]=="preparing","A fresh explicit click starts exactly one local recognizer");
            var recognizer=sessions[0];recognizer.Emit(new("",false,Stage:"preparing"));now+=100;await Write(first);await Poll();
            Check((string?)Status()["state"]=="preparing" && (string?)Status()["hint"]=="","A silent preparation update cannot make the microphone ready");
            recognizer.Emit(new("",false,Message:"可以说话了 · 测试麦克风"));now+=100;await Write(first);await Poll();
            Check((string?)Status()["state"]=="recording","Microphone readiness is reported rather than assumed");
            recognizer.Emit(new("临时假设",false));now+=100;await Write(first);await Poll();
            Check((string?)Status()["text"]=="" && (string?)Status()["hint"]=="","Hypotheses cannot become drafts or redundant process messages");
            recognizer.Emit(new("等队友到齐",true));recognizer.Emit(new("再开团。",true));now+=100;await Write(first);await Poll();
            var recognized=Status();Check((string?)recognized["text"]=="等队友到齐 再开团。" && sessions.Count==1,"Final chunks reach the widget and keep-alives cannot reopen the microphone");
            first["ack"]=recognized["sequence"];now+=100;await Write(first);await Poll();
            Check(!File.Exists(statusPath),"Acknowledged unconfirmed draft is removed from transport");
            first["action"]="stop";now+=100;await Write(first);await Poll();await recognizer.Finished.Task;await Poll();
            Check(recognizer.Stopped && recognizer.Disposed && (bool?)Status()["complete"]==true && (string?)Status()["text"]=="等队友到齐 再开团。","Stopping flushes final results and releases the microphone");
            first["ack"]=Status()["sequence"];now+=100;await Write(first);await Poll();Check(!File.Exists(statusPath),"Terminal draft is removed after acknowledgement");
            File.Delete(path);now+=4000;await Poll();recognizer.Emit(new("迟到内容",true));await Write(first);await Poll();
            Check(sessions.Count==1 && !File.Exists(statusPath),"Consumed id cannot replay recording or late results");
            var second=Request();await Write(second);await Poll();Check(sessions.Count==2,"A new user click can start a new session");
            sessions[1].Emit(new("尚未确认",true));now+=100;await Write(second);await Poll();Check(File.Exists(statusPath),"Draft can be read while recording");
            await File.WriteAllTextAsync(path,"{incomplete request");now+=3100;await Poll();await sessions[1].Finished.Task;
            Check(sessions[1].Cancelled && !File.Exists(statusPath),"Unreadable widget heartbeat cancels audio and removes draft transport");
            var third=Request();await Write(third);await Poll();Check(sessions.Count==3,"Next click works after microphone cancellation");
            session="next-game";await Poll();await sessions[2].Finished.Task;
            Check(sessions[2].Cancelled && !File.Exists(path) && !File.Exists(statusPath),"Match change cancels audio and invalidates old drafts");
            var fourth=Request();await Write(fourth);await Poll(false);Check(sessions.Count==3,"Disabled backend cannot start audio");
            await Poll();await Write(Request(action:"cancel"));await Poll();Check(sessions.Count==3,"Cancellation without a start cannot open audio");
            await Write(Request());await Poll();Check(sessions.Count==4,"Recording can resume after backend re-enabled");
            sessions[3].Fail();await sessions[3].Finished.Task;
            for(int i=0;i<20 && !(bool?)Status()["complete"]==true;i++){await Task.Delay(10);await Poll();}
            await Poll();Check((string?)Status()["state"]=="error" && (bool?)Status()["complete"]==true && ((string?)Status()["hint"]??"").Contains("麦克风"),"Recognition failures are reported and unlock the editor");
            await Write(Request());await Poll();Check(sessions.Count==5,"A failed recognizer can be retried by a new explicit click");
            service.Dispose();await sessions[4].Finished.Task;
            Check(sessions[4].Cancelled && !File.Exists(path) && !File.Exists(statusPath) && !File.Exists(Path.Combine(directory,"chat-request.json")),"Shutdown cleans transport; recording never writes a chat request");
            Console.WriteLine("PASS: Game Bar local recognition bridge, readiness/results, explicit stop, acknowledgement cleanup, lease expiry, stale/mutated requests, replay protection, match/backend cancellation. No microphone or chat used.");
        }
        finally{Directory.Delete(directory,true);}
    }
    private sealed class FakeSession : IDesktopVoiceSession
    {
        private readonly TaskCompletionSource<VoiceRecognitionOutcome> _result=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event Action<VoiceRecognitionUpdate>? Update;
        public bool Stopped,Cancelled,Disposed;
        public void Emit(VoiceRecognitionUpdate update)=>Update?.Invoke(update);
        public Task<VoiceRecognitionOutcome> RunAsync(CancellationToken token)
        {token.Register(()=>{Cancelled=true;_result.TrySetResult(new(true));});return _result.Task;}
        public void Stop(){Stopped=true;_result.TrySetResult(new(false));}
        public void Fail()=>_result.TrySetException(new UnauthorizedAccessException());
        public void Dispose(){Disposed=true;if(!Stopped){Cancelled=true;_result.TrySetResult(new(true));}Finished.TrySetResult();}
    }
}
