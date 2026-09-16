using League.Models;
using Newtonsoft.Json.Linq;
using System.Threading.Channels;

namespace League.Services;

public sealed partial class GameBarBridge
{
    private readonly Channel<JObject> _chatPending=Channel.CreateBounded<JObject>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
    public async Task<OverlayChatRequest?> ReadChatRequestAsync()
    {
        var scope=_detailScope;
        string directory=_testDirectory??_directory;
        if(_disposed || scope.Session.Length==0 || directory.Length==0)return null;
        try
        {
            string path=Path.Combine(directory,"chat-request.json");
            if(!File.Exists(path) || new FileInfo(path).Length>4096)return null;
            var value=JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            var current=_detailScope;
            if(current.Session!=scope.Session || !current.Heroes.SequenceEqual(scope.Heroes))return null;
            return OverlayChatRequest.Parse(value,current.Session,current.Heroes,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or FormatException or OverflowException or ArgumentException){return null;}
    }
    public void PublishChatStatus(OverlayChatRequest request,string state,string message,bool complete)
    {
        if(_disposed || request.Session!=_detailScope.Session)return;
        _chatPending.Writer.TryWrite(new JObject{["protocol"]=1,["requestId"]=request.Id,["session"]=request.Session,
            ["state"]=state,["message"]=message,["complete"]=complete,["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
    }
    private async Task WriteChatLoop()
    {
        await foreach(var data in _chatPending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            string directory=_testDirectory??_directory;
            if(directory.Length==0 || (string?)data["session"]!=_detailScope.Session)continue;
            string temporary=Path.Combine(directory,"chat-"+_instance+".tmp");
            try
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(temporary,data.ToString(Newtonsoft.Json.Formatting.None),new System.Text.UTF8Encoding(false)).ConfigureAwait(false);
                File.Move(temporary,Path.Combine(directory,"chat-status.json"),true);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){ }
            finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}}
        }
    }
}
