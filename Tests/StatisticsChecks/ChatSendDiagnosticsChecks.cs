using League.Services;

internal static class ChatSendDiagnosticsChecks
{
    public static async Task RunAsync()
    {
        if(!GameWindowIdentity.Matches(123,123,"League of Legends") || !GameWindowIdentity.Matches(123,123,"league of legends")
            || GameWindowIdentity.Matches(124,123,"League of Legends") || GameWindowIdentity.Matches(123,123,"LeagueClientUx")
            || GameWindowIdentity.Matches(0,0,"League of Legends") || GameWindowIdentity.Matches(123,123,"League"))
            throw new Exception("Foreground guard must require both game process identity and the actual foreground PID");
        string directory=Path.Combine(Path.GetTempPath(),"league-chat-diagnostics-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"chat.log");
        var diagnostics=new ChatSendDiagnostics(path);
        try
        {
            int events=0;diagnostics.Changed+=()=>events++;
            for(int i=0;i<40;i++)diagnostics.Report("stage",$"步骤 {i}");
            await diagnostics.FlushAsync();
            var lines=await File.ReadAllLinesAsync(path);
            if(lines.Length!=40 || !lines[0].EndsWith("步骤 0") || !lines[^1].EndsWith("步骤 39") || events!=40)throw new Exception("Diagnostic records must persist in order and notify subscribers");
            diagnostics.Report("error","首行\n次行");await diagnostics.FlushAsync();
            if(diagnostics.Current.Message.Contains('\n'))throw new Exception("Diagnostic records remain single line");
            await File.WriteAllTextAsync(path,new string('x',270000));
            diagnostics.Report("ready","新日志");await diagnostics.FlushAsync();
            if(!File.Exists(path+".previous") || new FileInfo(path).Length>1000)throw new Exception("Diagnostic log rotates at bounded size");
            var input=new GameInputService();
            if(await input.SendInGameMessageAsync("" ) || input.LastFailureReason==null)throw new Exception("Empty input reports reason");
            if(await input.SendInGameMessageAsync("测试不会实际输入",()=>false))throw new Exception("Guarded input must not inject keys");
            if(await input.SendInGameMessageAsync("测试不会实际输入",()=>false))throw new Exception("Input gate must release after early stop");
        }
        finally{await diagnostics.FlushAsync();if(Directory.Exists(directory))Directory.Delete(directory,true);}
        Console.WriteLine("PASS: chat diagnostics persistence, ordered updates, rotation and guarded input without sending.");
    }
}
