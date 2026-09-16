using System.Collections.Concurrent;
using System.Drawing.Imaging;
using League.Clients;
using League.Controls;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

// View-independent recommendations; both the source clients and image loaders retain their existing caches.
public sealed class OverlayChampionDetailService : IDisposable
{
    private readonly OpggClient _opgg=new();
    private readonly AramMetaClient _am;
    private readonly Func<TimeSpan> _lifetime;
    private readonly ConcurrentDictionary<string,(DateTime Time,JObject Data)> _cache=new();
    private readonly ConcurrentDictionary<int,JObject> _skills=new();
    private readonly ConcurrentDictionary<string,Task<string>> _images=new();
    private readonly SemaphoreSlim _imageGate=new(4,4);
    public OverlayChampionDetailService(Func<TimeSpan> lifetime){_lifetime=lifetime;_am=new(cacheLifetime:lifetime);}
    public static string ModeName(int queue)=>queue switch{2400 or 3270=>"海克斯大乱斗",450=>"极地大乱斗",900 or 1900=>"无限火力",1700 or 1710=>"斗魂竞技场",1200 or 1300=>"极限闪击",400 or 420 or 430 or 440 or 480 or 490=>"召唤师峡谷",_=>"当前模式"};
    private static string Rate(double? value)=>value is double n && double.IsFinite(n)?n.ToString("P1"):"—";
    public static JObject Icon(string kind,int id)=>new(){["kind"]=kind,["id"]=id,["name"]=kind switch{"item"=>$"装备 {id}","rune"=>$"符文 {id}","augment"=>$"强化 {id}",_=>$"技能 {id}"}};
    public static JObject Row(string title,string description="",IEnumerable<JObject>? icons=null,string stats="")=>new(){["title"]=title,["description"]=description,["stats"]=stats,["icons"]=new JArray(icons??[])};
    public static JObject Section(string tab,string title,IEnumerable<JObject> rows)=>new(){["tab"]=tab,["title"]=title,["rows"]=new JArray(rows)};
    private static string Stats(double? win,double? pick,long? games,bool adjusted=false)=>$"{(adjusted?"调整后胜率":"胜率")} {Rate(win)} · 使用率 {Rate(pick)} · {(games.HasValue?$"{games:N0} 场":"样本未知")}";
    private static JObject Option(JObject row,string title,string kind="item")=>Row(title,icons:OpggItemSetService.Ids(row).Select(id=>Icon(kind,id)),stats:Stats((double?)row["win_rate"]??OpggBuild.Ratio(row["win"],row["play"]),(double?)row["pick_rate"],(long?)row["play"]));
    private static JObject EquipmentRow(int id,double? win,double? pick,long? games,string? name=null)
    {
        var icon=Icon("item",id);if(!string.IsNullOrWhiteSpace(name))icon["name"]=name;
        var row=Row("",icons:[icon],stats:Stats(win,pick,games));
        row["winRateText"]="胜率 "+Rate(win);row["pickRateText"]="使用率 "+Rate(pick);return row;
    }
    private static JObject EquipmentSection(string order,IEnumerable<JObject> rows)
    {
        var section=Section("builds","推荐装备 · "+order,rows);
        section["layout"]="equipment";
        section["note"]="该英雄在当前模式下的单件装备统计 · 悬浮查看介绍与样本量";
        return section;
    }

    public static JObject FromOpgg(OpggBuild build)
    {
        var sections=new JArray();
        sections.Add(EquipmentSection("按使用率排序",OpggItemSetService.Options(build.Data,"last_items")
            .Where(row=>OpggItemSetService.Ids(row).Length==1).DistinctBy(row=>OpggItemSetService.Ids(row)[0]).Take(30)
            .Select(row=>EquipmentRow(OpggItemSetService.Ids(row)[0],(double?)row["win_rate"]??OpggBuild.Ratio(row["win"],row["play"]),(double?)row["pick_rate"],(long?)row["play"]))));
        var cores=OpggItemSetService.Options(build.Data,"core_items").Take(5).Select((core,i)=>
        {
            var row=Option(core,$"核心组合 {i+1}");
            row["later"]=new JArray(OpggItemSetService.Later(build.Data,core).Take(20).Select(id=>Icon("item",id)));
            return row;
        }).ToArray();
        sections.Add(Section("builds","核心出装 · 按使用率排序",cores));
        foreach(var (key,title) in new[]{("starter_items","出门装"),("boots","鞋子")})
            sections.Add(Section("builds",title,OpggItemSetService.Options(build.Data,key).Take(3).Select(row=>Option(row,title))));
        var spells=OpggItemSetService.Options(build.Data,"summoner_spells");
        var highlights=new[]{("使用率最高",spells.Where(s=>s["pick_rate"]?.Type is JTokenType.Float or JTokenType.Integer).OrderByDescending(s=>(double?)s["pick_rate"]).FirstOrDefault()),
            ("胜率最高",spells.Where(s=>OpggBuild.Ratio(s["win"],s["play"])!=null).OrderByDescending(s=>OpggBuild.Ratio(s["win"],s["play"])).FirstOrDefault())};
        sections.Add(Section("builds","召唤师技能",highlights.Where(s=>s.Item2!=null).Select(s=>Option(s.Item2!,s.Item1,"spell"))));
        var runes=build.Runes.OrderByDescending(r=>r.PickRate??-1).Take(5).ToArray();
        sections.Add(Section("runes","符文方案 · 图标可查看说明",runes.Select((r,i)=>Row(
            $"方案 {i+1} · "+string.Join(" / ",ChampionSelectAssistant.RuneHighlights(r,build.Runes)),
            icons:r.Perks.Select(id=>Icon("rune",id)),stats:Stats(r.WinRate,r.PickRate,r.Plays)))));
        string priority=string.Join(" → ",(build.Data["skill_masteries"]?.First?["ids"] as JArray??new()).Values<string>());
        if(priority.Length>0)sections.Add(Section("skills","推荐加点",[Row(priority)]));
        return new(){["source"]=$"OP.GG · {build.Version} · 全球 / 全段位"+(build.Query.Mode=="ranked"?$" · {Lane(build.Query.Position)}":""),["sections"]=sections};
    }
    private static string Lane(string lane)=>lane switch{"top"=>"上路","jungle"=>"打野","mid"=>"中路","adc"=>"下路","support"=>"辅助",_=>"参考分路"};
    public static JObject FromAramMeta(AramMetaPage page,int hero)
    {
        var sections=new JArray();
        foreach(var (key,title) in new[]{("kPrismatic","棱彩强化"),("kGold","黄金强化"),("kSilver","白银强化")})
            sections.Add(Section("augments",title,AramMetaRecommendation.Read(page,hero,key).Take(8).Select(r=>
            {
                var icon=Icon("augment",r.Ids[0]);icon["name"]=r.Name;icon["description"]=TooltipText.PlainText(r.Description);
                var row=Row("",icons:[icon],stats:Stats(r.WinRate,r.PickRate,r.Games,true));
                row["winRateText"]="胜率 "+Rate(r.WinRate);row["pickRateText"]="选取率 "+Rate(r.PickRate);return row;
            })));
        if(page.Detail!=null)
        {
            sections.Add(EquipmentSection("来源推荐顺序",AramMetaRecommendation.Read(page,hero,"singleItems")
                .Where(row=>row.Ids.Length==1).DistinctBy(row=>row.Ids[0]).Take(30)
                .Select(row=>EquipmentRow(row.Ids[0],row.WinRate,row.PickRate,row.Games,row.Name))));
            var build=OpggItemSetService.FromAramMeta(page,hero);
            foreach(var (key,title) in new[]{("items","出装组合"),("boots","鞋子")})
                sections.Add(Section("builds",title,AramMetaRecommendation.Read(page,hero,key).Take(5).Select((r,i)=>
                {
                    var row=Row($"{title} {i+1}",icons:r.Ids.Select(id=>Icon("item",id)),stats:Stats(r.WinRate,r.PickRate,r.Games));
                    if(key=="items")row["later"]=new JArray(OpggItemSetService.Later(build.Data,r.ToOption()).Take(20).Select(id=>Icon("item",id)));
                    return row;
                })));
            sections.Add(Section("builds","召唤师技能",AramMetaRecommendation.SpellHighlights(AramMetaRecommendation.Read(page,hero,"spells"))
                .Where(s=>s.Entry!=null).Select(s=>Row(s.Title,icons:s.Entry!.Ids.Select(id=>Icon("spell",id)),stats:Stats(s.Entry.WinRate,s.Entry.PickRate,s.Entry.Games)))));
        }
        return new(){["source"]=$"arammeta · {page.Index.Patch} · 来源推荐顺序"+(page.UpdateFailed?" · 更新失败，使用旧缓存":""),["sections"]=sections};
    }

    public async Task<JObject> LoadAsync(int hero,int queue,string position,CancellationToken token,Action<JObject> preview)
    {
        queue=queue==3270?2400:queue;
        string key=$"{hero}:{queue}:{position}";
        var lifetime=queue==2400?_lifetime():TimeSpan.FromMinutes(15);
        if(_cache.TryGetValue(key,out var cached) && DateTime.UtcNow-cached.Time<lifetime)return (JObject)cached.Data.DeepClone();
        var data=new JObject{["state"]="loading",["mode"]=ModeName(queue),["source"]="正在读取推荐…",["sections"]=new JArray()};
        bool failed=false;
        // Skills do not depend on the remote statistics request.
        async Task Skills()
        {
            try
            {
                var client=FormMain.Globals.lcuClient?.Client??throw new InvalidOperationException();
                if(!_skills.TryGetValue(hero,out var champion))
                {
                    champion=JObject.Parse(await client.GetStringAsync($"/lol-game-data/assets/v1/champions/{hero}.json",token));
                    if((int?)champion["id"]!=hero)throw new InvalidDataException();
                    _skills[hero]=champion;
                }
                var skills=ChampionSelectAssistant.ParseSkills(champion);
                ((JArray)data["sections"]!).Insert(0,Section("skills","英雄技能 · 悬浮图标查看说明",skills.Select(s=>
                    Row(s.Key+" · "+s.Name,s.Description,[new JObject{["kind"]="skill",["id"]=hero,["key"]=s.Key,["path"]=s.Icon,["name"]=s.Name,["description"]=s.Description}]))));
                data["artPath"]=ProfileBackgroundPanel.FindSplash(champion,hero*1000)??"";
            }
            catch(OperationCanceledException){throw;}
            catch{failed=true;((JArray)data["sections"]!).Add(Section("skills","英雄技能",[Row("技能资料暂不可用","客户端资源恢复后可重试。") ]));}
            token.ThrowIfCancellationRequested();preview((JObject)data.DeepClone());
        }
        // Keep JObject mutations on the caller's UI context; expensive image conversion runs separately.
        var skillsTask=Skills();
        try
        {
            JObject recommendations;
            if(queue==2400)
            {
                try
                {
                    recommendations=FromAramMeta(await _am.GetAsync(hero,false,token,early=>
                    {
                        var partial=(JObject)data.DeepClone();var available=FromAramMeta(early,hero);partial["source"]=available["source"];
                        foreach(var section in (JArray)available["sections"]!)((JArray)partial["sections"]!).Add(section.DeepClone());
                        preview(partial);
                    }),hero);
                }
                catch(OperationCanceledException){throw;}
                catch
                {
                    recommendations=FromAramMeta(await _am.GetAsync(0,false,token),hero);
                    recommendations["source"]+=" · 出装详情暂不可用，可重试";failed=true;
                }
            }
            else if(OpggQuery.ModeForQueue(queue) is string mode)
            {
                if(mode=="ranked" && !new[]{"top","jungle","mid","adc","support"}.Contains(position))
                {
                    var tiers=await _opgg.GetTiersAsync(new("global",mode,"all","mid",0),false,token);
                    position=OpggQuery.PositionForChampion(tiers.Rows,hero,null)??"mid";
                }
                recommendations=FromOpgg(await _opgg.GetBuildAsync(new("global",mode,"all",mode=="ranked"?position:"none",hero),false,token));
            }
            else recommendations=new JObject{["source"]="当前模式暂无对应推荐，仍可查看英雄技能。",["sections"]=new JArray()};
            data["source"]=recommendations["source"];
            foreach(var section in (JArray)recommendations["sections"]!)((JArray)data["sections"]!).Add(section.DeepClone());
        }
        catch(OperationCanceledException){try{await skillsTask;}catch{}throw;}
        catch{failed=true;data["source"]="推荐暂不可用，可重试；已读取的技能仍可查看。";}
        await skillsTask;token.ThrowIfCancellationRequested();
        Decorate(data);preview((JObject)data.DeepClone());
        var assets=data.Descendants().OfType<JObject>().Where(o=>o["kind"]!=null).ToArray();
        foreach(var chunk in assets.Chunk(4))
        {
            token.ThrowIfCancellationRequested();
            await Task.WhenAll(chunk.Select(async icon=>icon["image"]=await LoadImage(icon,token)));
        }
        string artPath=(string?)data["artPath"]??"";data.Remove("artPath");
        if(artPath.StartsWith("/lol-game-data/assets/",StringComparison.OrdinalIgnoreCase))
            data["art"]=await LoadImage(new JObject{["kind"]="art",["id"]=hero,["path"]=artPath},token);
        data["state"]=failed?"partial":"ready";
        if(!failed){if(_cache.Count>=30)_cache.Clear();_cache[key]=(DateTime.UtcNow,(JObject)data.DeepClone());}
        return data;
    }
    private static void Decorate(JObject data)
    {
        var res=FormMain.Globals.resLoading;if(res==null)return;
        foreach(var icon in data.Descendants().OfType<JObject>().Where(o=>o["kind"]!=null))
        {
            int id=(int?)icon["id"]??0;
            var (name,description)=((string?)icon["kind"]) switch
            {
                "item"=>(res.GetItemById(id)?.Name,res.GetItemById(id)?.Description),
                "spell"=>(res.GetSpellById(id)?.Name,res.GetSpellById(id)?.Description),
                "rune"=>(res.GetRuneById(id)?.name??res.GetRuneById(id)?.Name,res.GetRuneById(id)?.longDesc),
                "augment"=>(res.GetAugmentById(id)?.Name,res.GetAugmentById(id)?.Description),_ => (null,null)
            };
            if(!string.IsNullOrEmpty(name))icon["name"]=name;
            if(!string.IsNullOrEmpty(description))icon["description"]=TooltipText.PlainText(description);
        }
        foreach(var section in ((JArray?)data["sections"]??new()).OfType<JObject>().Where(s=>(string?)s["tab"]=="runes"))
        foreach(var row in ((JArray?)section["rows"]??new()).OfType<JObject>())
        {
            string name=(string?)row["icons"]?.First?["name"]??"符文方案";
            string title=(string?)row["title"]??"";int separator=title.IndexOf('·');
            string tags=separator>=0?title[(separator+1)..].Trim():"";
            row["title"]=name+(tags.Length>0?" · "+tags:"");
        }
    }
    private async Task<string> LoadImage(JObject icon,CancellationToken token)
    {
        string kind=(string?)icon["kind"]??"",path=(string?)icon["path"]??"";
        int id=(int?)icon["id"]??0;string key=$"{kind}:{id}:{path}";
        var task=_images.GetOrAdd(key,_=>Task.Run(async ()=>
        {
            await _imageGate.WaitAsync();
            try
            {
                Image? image=null;var res=FormMain.Globals.resLoading;
                if(kind is "skill" or "art")
                {
                    var client=FormMain.Globals.lcuClient?.Client;
                    if(client!=null && path.StartsWith("/lol-game-data/assets/",StringComparison.OrdinalIgnoreCase))
                        image=await new ImageCacheService(client).GetOrDownloadImageAsync(path,kind=="art"?"profile-backgrounds":"skills",kind=="art"?$"{id*1000}.png":$"{id}-{(string?)icon["key"]}.png");
                }
                else if(res!=null)image=(await (kind switch{"item"=>res.GetItemInfoAsync(id),"rune"=>res.GetRuneInfoAsync(id),"augment"=>res.GetAugmentInfoAsync(id),_=>res.GetSpellInfoAsync(id)})).image;
                using(image)
                {
                    if(image==null)return "";
                    using var small=new Bitmap(image,kind=="art"?new Size(560,280):new Size(48,48));
                    using var stream=new MemoryStream();small.Save(stream,kind=="art"?ImageFormat.Jpeg:ImageFormat.Png);
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch{return "";}finally{_imageGate.Release();}
        }));
        string result=await task.WaitAsync(token);
        if(result.Length==0)_images.TryRemove(key,out _);
        return result;
    }
    public void Dispose(){_opgg.Dispose();_am.Dispose();}
}
