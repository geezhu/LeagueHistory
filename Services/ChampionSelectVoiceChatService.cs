using League.Models;
using Newtonsoft.Json.Linq;
using System.Text;

namespace League.Services;

public static class ChampionSelectVoiceChatService
{
    public static async Task<string> CaptureRoomAsync(HttpClient client,CancellationToken token)
    {
        async Task<bool> Selecting()=> (await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase",token).ConfigureAwait(false)).Trim('"')=="ChampSelect";
        if(!await Selecting().ConfigureAwait(false))throw new InvalidOperationException("请在选人阶段使用语音输入。");
        var rooms=JArray.Parse(await client.GetStringAsync("/lol-chat/v1/conversations",token).ConfigureAwait(false));
        var ids=rooms.OfType<JObject>().Where(r=>(string?)r["type"] is string type &&
            (type.Equals("championSelect",StringComparison.OrdinalIgnoreCase) || type.Equals("champSelect",StringComparison.OrdinalIgnoreCase)))
            .Select(r=>(string?)r["id"]??"").Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        string id=ids.Length==1?ids[0]:"";
        if(string.IsNullOrWhiteSpace(id) || !await Selecting().ConfigureAwait(false))throw new InvalidOperationException("选人聊天尚未连接或选人已结束，请稍后重试。");
        return id;
    }
    public static async Task SendAsync(HttpClient client,string room,string text,CancellationToken token,Func<bool>? current=null)
    {
        if(!OverlayVoiceChatText.TryNormalize(text,out string message))throw new InvalidOperationException("请输入一条不超过 180 字符的聊天文字；不支持换行或斜杠命令。");
        void CheckCurrent(){token.ThrowIfCancellationRequested();if(current?.Invoke()==false)throw new OperationCanceledException(token);}
        CheckCurrent();
        if(await CaptureRoomAsync(client,token)!=room)throw new InvalidOperationException("选人房间已变化，请关闭旧草稿后重新打开语音输入。");
        CheckCurrent();
        using var content=new StringContent(new JObject{["body"]=message}.ToString(),Encoding.UTF8,"application/json");
        using var response=await client.PostAsync($"/lol-chat/v1/conversations/{Uri.EscapeDataString(room)}/messages",content,token);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("客户端未接受消息，请检查选人聊天连接。");
    }
}
