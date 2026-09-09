using Newtonsoft.Json.Linq;
using System.Text;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private string? _lastRuneNotice;

    private async Task SendMayhemNoticeAsync()
    {
        if(_applying || _hero<=0)return;
        _applying=true;_apply.Enabled=false;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_load?.Token??default);timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            string champion=FormMain.Globals.resLoading.GetChampionById(_hero)?.Name??_hero.ToString();
            var client=FormMain.Globals.lcuClient?.Client??throw new InvalidOperationException("客户端未连接。");
            bool sent=await AnnounceRunesAsync(client,$"【海克斯大乱斗】{champion} · 符文已配置成功——数据来自 arammeta",timeout.Token);
            if(!IsDisposed)_status.Text=sent?"配置提示已发送。":"配置提示未发送。";
        }
        catch(Exception){if(!IsDisposed)_status.Text="配置提示发送失败。";}
        finally{_applying=false;if(!IsDisposed)_apply.Enabled=_selecting && (SelectedMode=="mayhem" || _runes!=null);}
    }

    private async Task<bool> AnnounceRunesAsync(HttpClient client,string message,CancellationToken token)
    {
        var phase=(await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase",token)).Trim('"');
        if(phase!="ChampSelect")return false;
        var rooms=JArray.Parse(await client.GetStringAsync("/lol-chat/v1/conversations",token));
        var room=rooms.OfType<JObject>().FirstOrDefault(r=>
            string.Equals((string?)r["type"],"championSelect",StringComparison.OrdinalIgnoreCase) ||
            string.Equals((string?)r["type"],"champSelect",StringComparison.OrdinalIgnoreCase));
        var id=(string?)room?["id"];
        if(string.IsNullOrWhiteSpace(id))return false;
        string key=id+"\n"+message;
        if(_lastRuneNotice==key)return true;
        using var content=new StringContent(new JObject{["body"]=message}.ToString(),Encoding.UTF8,"application/json");
        using var response=await client.PostAsync($"/lol-chat/v1/conversations/{Uri.EscapeDataString(id)}/messages",content,token);
        if(!response.IsSuccessStatusCode)return false;
        _lastRuneNotice=key;
        return true;
    }
}
