using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed record PlayerChampionTier(string Tier,string Rate,string Note,int Queue,string Position)
{
    public bool ReferencePosition { get; init; }
    public static int NormalizeQueue(int queue,string mode)
    {
        if(queue==3270 || mode.Equals("KIWI",StringComparison.OrdinalIgnoreCase))return 2400;
        if(queue==2400 || OpggQuery.ModeForQueue(queue)!=null)return queue;
        return mode.ToUpperInvariant() switch {"ARAM"=>450,"URF"=>900,"NEXUSBLITZ"=>1300,"CHERRY"=>1700,"CLASSIC"=>420,_=>queue};
    }
    public static PlayerChampionTier FromOpgg(int hero,int queue,string assigned,string version,OpggTier[] rows)
    {
        string? mode=OpggQuery.ModeForQueue(queue);
        bool ranked=mode=="ranked";
        string position=assigned.ToLowerInvariant() switch {"top"=>"top","jungle"=>"jungle","middle" or "mid"=>"mid","bottom" or "adc"=>"adc","utility" or "support"=>"support",_=>"none"};
        bool reference=ranked && position=="none";
        if(reference)position=OpggQuery.PositionForChampion(rows,hero,null)??"none";
        if(!ranked)position="none";
        var row=rows.FirstOrDefault(c=>c.ChampionId==hero && (!ranked || c.Position.Equals(position,StringComparison.OrdinalIgnoreCase)));
        string lane=position switch {"top"=>"上路","jungle"=>"打野","mid"=>"中路","adc"=>"下路","support"=>"辅助",_=>"分路未知"};
        string title=mode switch {"ranked"=>"召唤师峡谷","aram"=>"普通大乱斗","arena"=>"斗魂竞技场","urf"=>"无限火力","nexus_blitz"=>"极限闪击",_=>"未知模式"};
        string rate=row?.WinRate is double win?$"胜 {win:P1}":"暂无数据";
        if(ranked)rate+=" · "+(reference?"参考":"")+lane;
        string note=$"OP.GG · {title} · 全球 / 全段位 · {version}\n选用率 {(row?.PickRate is double pick?pick.ToString("P1"):"—")}";
        if(ranked)note+=$"\n{(reference?"参考分路":"玩家分路")}：{lane}";
        if(reference)note+="\n未读取到玩家分路，优先使用该英雄选用率最高的可用分路；不代表玩家实际位置。";
        return new(row?.Tier is int tier?"T"+tier:"—",rate,note,queue,position){ReferencePosition=reference};
    }
}
internal sealed class PlayerChampionTierService : IDisposable
{
    private readonly OpggClient _opgg=new();
    private readonly AramMetaClient _am;
    private readonly CancellationTokenSource _life=new();
    private readonly Dictionary<string,(DateTime Time,Task<(string Version,OpggTier[] Rows)> Load)> _tiers=new();
    private Task<AramMetaPage>? _amPage;
    private DateTime _amTime;
    private Task<JObject>? _context;
    private DateTime _contextTime;
    private readonly FormMain _form;
    public PlayerChampionTierService(FormMain form)
    {
        _form=form;_am=new(cacheLifetime:()=>TimeSpan.FromMinutes(Math.Clamp(form.GetAppConfig()?.AramMetaCacheMinutes??1440,1,10080)));
    }
    private Task<JObject> Context()
    {
        if(_context==null || DateTime.UtcNow-_contextTime>TimeSpan.FromSeconds(5))
        {
            _contextTime=DateTime.UtcNow;_context=Load();
        }
        return _context;
        async Task<JObject> Load()
        {
            var client=FormMain.Globals.lcuClient?.Client??throw new InvalidOperationException("客户端未连接");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);timeout.CancelAfter(TimeSpan.FromSeconds(4));
            return JObject.Parse(await client.GetStringAsync("/lol-gameflow/v1/session",timeout.Token));
        }
    }
    public async Task<PlayerChampionTier> GetAsync(int hero,string puuid)
    {
        var context=await Context();
        int queue=(int?)context["gameData"]?["queue"]?["id"]??0;
        string mode=(string?)context["gameData"]?["queue"]?["gameMode"]??(string?)context["gameData"]?["gameMode"]??"";
        queue=PlayerChampionTier.NormalizeQueue(queue,mode);
        if(queue==2400)
        {
            var lifetime=TimeSpan.FromMinutes(Math.Clamp(_form.GetAppConfig()?.AramMetaCacheMinutes??1440,1,10080));
            if(_amPage==null || DateTime.UtcNow-_amTime>(_amPage.IsFaulted?TimeSpan.FromSeconds(30):lifetime))
            {_amTime=DateTime.UtcNow;_amPage=_am.GetAsync(0,false,_life.Token);}
            var page=await _amPage;
            var c=page.Index.Champions.FirstOrDefault(c=>c.Id==hero);
            return new(c?.Tier??"—",c?.WinRate is double win?$"胜 {win:P1}":"暂无数据",$"arammeta · 海克斯大乱斗 · {page.Index.Patch}\n调整后胜率 · 样本选取率 {(c==null?null:page.Index.PickRate(c)):P1}"+(page.UpdateFailed?"\n更新失败，使用旧缓存":""),2400,"none");
        }
        string? key=OpggQuery.ModeForQueue(queue);
        if(key==null)return new("—","暂无梯队","当前模式暂无对应统计。",queue,"none");
        // The ranking endpoint returns all positions together; one request serves every player.
        if(!_tiers.TryGetValue(key,out var saved) || DateTime.UtcNow-saved.Time>TimeSpan.FromMinutes(saved.Load.IsFaulted?0.5:15))
            _tiers[key]=saved=(DateTime.UtcNow,_opgg.GetTiersAsync(new("global",key,"all",key=="ranked"?"mid":"none",0),false,_life.Token));
        var data=await saved.Load;
        var players=(_form._cachedMyTeam??new JArray()).Concat(_form._cachedEnemyTeam??new JArray())
            .Concat(context["gameData"]?["teamOne"] as JArray??new JArray()).Concat(context["gameData"]?["teamTwo"] as JArray??new JArray());
        var player=players.FirstOrDefault(p=>!string.IsNullOrEmpty(puuid) && (string?)p["puuid"]==puuid && !string.IsNullOrEmpty((string?)p["assignedPosition"]));
        return PlayerChampionTier.FromOpgg(hero,queue,(string?)player?["assignedPosition"]??"",data.Version,data.Rows);
    }
    public void Dispose(){_life.Cancel();_opgg.Dispose();_am.Dispose();}
}
