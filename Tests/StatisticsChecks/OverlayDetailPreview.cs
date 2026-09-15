using System.Drawing.Imaging;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class OverlayDetailPreview
{
    // Exports a separate, explicitly labelled fixture. Never touches live snapshot/request files.
    public static void Export(string destination,string assetDirectory)
    {
        Directory.CreateDirectory(destination);
        string Encode(string path,int width=48,int height=48)
        {
            if(!File.Exists(path))return "";
            using var source=Image.FromFile(path);using var small=new Bitmap(source,new Size(width,height));
            using var stream=new MemoryStream();small.Save(stream,width>48?ImageFormat.Jpeg:ImageFormat.Png);return Convert.ToBase64String(stream.ToArray());
        }
        var index=AramMetaIndex.Parse(JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/AramMeta/index.json"))));
        var detail=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/AramMeta/103.json")));
        var content=OverlayChampionDetailService.FromAramMeta(new(index,detail,true,DateTime.UtcNow),103);
        var opgg=OpggBuild.Parse(JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/Opgg/aram.json"))),new("global","aram","all","none",103));
        foreach(var section in ((JArray)OverlayChampionDetailService.FromOpgg(opgg)["sections"]!).Where(s=>(string?)s["tab"]=="runes"))((JArray)content["sections"]!).Add(section.DeepClone());
        ((JArray)content["sections"]!).Insert(0,OverlayChampionDetailService.Section("skills","技能说明 · 布局测试",new[]{"被动","Q","W","E","R"}.Select(key=>
            OverlayChampionDetailService.Row(key+" · 技能说明示例","这是一段用于检查换行的长说明。技能说明应随着面板宽度自然换行，不能越过卡片边缘，也不能遮挡下面的推荐内容。\n第二段用于检查多段文字的间距。",
            [new JObject{["kind"]="skill",["id"]=21,["key"]=key,["name"]="布局测试图标",["description"]="悬浮说明应完整换行。"}]))));
        foreach(var icon in content.Descendants().OfType<JObject>().Where(o=>o["kind"]!=null))
        {
            string kind=(string?)icon["kind"]??"";int id=(int?)icon["id"]??0;string? path=null;
            if(kind=="skill")path=Path.Combine(assetDirectory,"skills",$"{id}-{(string?)icon["key"]}.png");
            else
            {
                string folder=Path.Combine(assetDirectory,kind switch{"item"=>"item","augment"=>"augments","rune"=>"rune",_=>"spell"});
                if(Directory.Exists(folder))path=Directory.EnumerateFiles(folder).FirstOrDefault(f=>Path.GetFileNameWithoutExtension(f)==id.ToString() || Path.GetFileNameWithoutExtension(f).StartsWith(id+"_") || Path.GetFileNameWithoutExtension(f).EndsWith("_"+id));
            }
            if(path!=null)icon["image"]=Encode(path);
        }
        string heroIcon=Encode(Path.Combine(assetDirectory,"champion","九尾妖狐.png"));
        content["art"]=Encode(Path.Combine(assetDirectory,"profile-backgrounds","21000.png"),560,280);
        content["revision"]="fixture-1";content["state"]="ready";content["mode"]="布局测试";content["source"]="本地测试样例 · 不代表当前对局";
        var roster=new JObject{["protocol"]=1,["detailProtocol"]=1,["queue"]=2400,["revision"]="fixture-1",["session"]="fixture-session",["command"]="fixture-command",["enabled"]=true,["active"]=true,["show"]=true,
            ["status"]="英雄详情布局验证 · 本地样例",["teams"]=new JArray("我方","对方"),["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["players"]=new JArray(Enumerable.Range(0,10).Select(i=>new JObject{["championId"]=103,["champion"]="样例英雄",["name"]=$"测试玩家 {i+1}",["tier"]="T2",["stats"]="胜率 52.4% · 选用率 8.7%",["details"]="缓存战绩示例",["icon"]=heroIcon}))};
        File.WriteAllText(Path.Combine(destination,"detail-preview-content.json"),content.ToString());
        File.WriteAllText(Path.Combine(destination,"detail-preview-snapshot.json"),roster.ToString());
        Console.WriteLine("Detail preview fixture exported to "+destination);
    }
}
