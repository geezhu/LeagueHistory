using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Reflection;

internal static class LiveMatchChecks
{
    private const string Sample="""
    {"activePlayer":{"riotId":"Self#CN"},"gameData":{"gameMode":"ARAM","mapNumber":12,"gameTime":125},"allPlayers":[
    {"riotId":"Enemy#CN","rawChampionName":"game_character_displayname_Lux","championName":"拉克丝","team":"ORDER","level":8,"scores":{"kills":3,"deaths":1},"items":[{"itemID":3089,"displayName":"灭世者的死亡之帽","count":1}]},
    {"riotId":"Self#CN","rawChampionName":"game_character_displayname_Morgana","championName":"莫甘娜","team":"CHAOS","level":9,"scores":{"kills":1,"deaths":3},"items":[],"runes":{"keystone":{"id":8112}}},
    {"riotId":"Ally#CN","rawChampionName":"game_character_displayname_Lux","championName":"拉克丝","team":"CHAOS","level":8,"scores":{"kills":0,"deaths":0}}
    ],"events":{"Events":[{"EventID":0,"EventName":"GameStart","EventTime":0},
    {"EventID":1,"EventName":"ChampionKill","EventTime":10,"KillerName":"Enemy#CN","VictimName":"Self#CN"},
    {"EventID":2,"EventName":"ChampionKill","EventTime":20,"KillerName":"Enemy#CN","VictimName":"Self#CN"},
    {"EventID":3,"EventName":"ChampionKill","EventTime":30,"KillerName":"Self#CN","VictimName":"Enemy#CN"},
    {"EventID":4,"EventName":"ChampionKill","EventTime":40,"KillerName":"Enemy#CN","VictimName":"Self#CN"}]}}
    """;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private sealed class Handler:HttpMessageHandler
    {
        public bool Fail;
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;
            Check(request.Method==HttpMethod.Get && request.RequestUri!.ToString()=="https://127.0.0.1:2999/liveclientdata/allgamedata","Live API is loopback read-only");
            return Task.FromResult(Fail?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):new(HttpStatusCode.OK){Content=new StringContent(Sample)});
        }
    }
    public static void Run()
    {
        var previousContext=SynchronizationContext.Current;
        var snapshot=LiveMatchSnapshot.Parse(JObject.Parse(Sample));
        Check(snapshot.TeamLabel(snapshot.Players[0])=="对方" && snapshot.TeamLabel(snapshot.Players[1])=="我方","Team determined by self, not ORDER");
        Check(snapshot.Players[0].ChampionAlias=="Lux" && snapshot.Players[0].Items![0].Id==3089,"Raw champion alias and current item IDs");
        Check(snapshot.Players[1].Items is {Length:0} && snapshot.Players[2].Items==null,"Empty equipment differs from unavailable");
        Check(snapshot.Players.Length==3 && snapshot.Players[0].Key!=snapshot.Players[2].Key,"Duplicate champions remain independent players");
        Check(snapshot.DeathStreaks[snapshot.Players[1].Key] is{Current:1,Highest:2,Complete:true},"Current and highest death streak reconstruct from a midgame snapshot");
        var unknown=JObject.Parse(Sample);unknown.Remove("activePlayer");
        var neutral=LiveMatchSnapshot.Parse(unknown);
        Check(neutral.Players.All(p=>!p.Self) && neutral.TeamLabel(neutral.Players[0])=="ORDER","Unknown self must not assign friend/enemy");
        try{LiveMatchSnapshot.Parse(new JObject());throw new Exception("Malformed snapshot accepted");}catch(InvalidDataException){}
        var handler=new Handler();
        var champions=League.FormMain.Globals.resLoading.Champions.Champions;
        var testLux=new Champion{Id=99,Alias="Lux",Name="拉克丝",Description=""};var testMorgana=new Champion{Id=25,Alias="Morgana",Name="莫甘娜",Description=""};
        champions.Add(testLux);champions.Add(testMorgana);
        using var client=new LiveMatchClient(handler);
        using var form=new Form{ClientSize=new Size(1100,740)};
        var imageCalls=new Dictionary<(string,int),int>();
        using var view=new LiveMatchView(()=>Task.FromResult(0),(_,_)=>throw new Exception("Guide must not open automatically"),()=>TimeSpan.FromDays(1),client,
            (kind,id)=>{imageCalls[(kind,id)]=imageCalls.GetValueOrDefault((kind,id))+1;var bitmap=new Bitmap(48,48);using(var g=Graphics.FromImage(bitmap)){g.Clear(kind=="augment"?Color.Purple:Color.Teal);g.DrawEllipse(Pens.Gold,5,5,36,36);}return Task.FromResult<(Image?,string,string)>((bitmap,"测试图标","悬浮说明"));});
        form.Controls.Add(view);form.Show();Application.DoEvents();
        IEnumerable<Control> All(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        var cards=All(view).OfType<Panel>().Where(c=>c.Tag is string).ToArray();
        Check(cards.Length==3 && handler.Calls>=1,"Live page loads participant cards");
        Check(All(view).OfType<Label>().Any(l=>l.Text=="连死 1 · 最高 2"),"Live page and selected hero details show the reconstructed death streak");
        Check(All(view).OfType<Button>().Any(b=>b.AccessibleName?.Contains("死亡之帽")==true && b.BackgroundImage!=null),"Current equipment uses loaded icons");
        var type=typeof(LiveMatchView);
        type.GetField("_currentQueue",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(view,450);
        type.GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null);
        var retained=All(view).OfType<Panel>().First(c=>c.Tag is string);
        type.GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null);
        Check(!retained.IsDisposed,"Unchanged poll must retain card controls to prevent flashing");
        var builds=new List<(int[] Ids,double? Win,double? Pick)>{(new[]{3089,3020},.53,.16),(new[]{3089,3157},.52,.11)};
        var augments=new List<(string Title,AramMetaRecommendation[] Rows)>{("棱彩强化",new[]{new AramMetaRecommendation(new[]{2095},"测试强化","强化说明",.56,.03,100,new JObject())})};
        type.GetMethod("RenderRecommendations",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,new object[]{builds,augments});
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var before=All(view).OfType<Panel>().Where(p=>p.Tag is string).ToDictionary(p=>(string)p.Tag!);
        var recommendationHost=(FlowLayoutPanel)type.GetField("_recommendations",flags)!.GetValue(view)!;
        var recommendationControl=recommendationHost.Controls[0];
        var purchased=JObject.Parse(Sample);
        purchased["allPlayers"]![1]!["items"]=new JArray(new JObject{["itemID"]=3089,["count"]=1});
        type.GetField("_snapshot",flags)!.SetValue(view,LiveMatchSnapshot.Parse(purchased));
        type.GetMethod("Render",flags)!.Invoke(view,null);
        var after=All(view).OfType<Panel>().Where(p=>p.Tag is string).ToDictionary(p=>(string)p.Tag!);
        var selfKey=snapshot.Players.Single(p=>p.Self).Key;
        Check(before.Where(p=>p.Key!=selfKey).All(p=>ReferenceEquals(p.Value,after[p.Key])),"Buying an item retains all other player cards");
        Check(ReferenceEquals(recommendationControl,recommendationHost.Controls[0]),"Buying an item updates ownership without rebuilding recommendations");
        Check(All(recommendationHost).OfType<Button>().Where(b=>b.Tag is ValueTuple<string,int> item && item.Item2==3089).All(b=>b.FlatAppearance.BorderSize==2),"Owned recommendation items update immediately");
        using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(AppContext.BaseDirectory,"live-match.png"));}
        form.ClientSize=new Size(650,700);Application.DoEvents();
        using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(AppContext.BaseDirectory,"live-match-narrow.png"));}
        Check(All(view).OfType<Button>().Any(b=>b.BackgroundImage!=null),"Icons survive responsive layout");
        Check(imageCalls.Values.All(count=>count==1),"Each icon is loaded once across duplicate champions, recommendations and resizing");
        var testSeraphine=new Champion{Id=147,Alias="Seraphine",Name="星籁歌姬",Description=""};champions.Add(testSeraphine);
        try
        {
            var alternate=JObject.Parse(Sample);alternate["allPlayers"]![1]!["rawChampionName"]="Character_Seraphine_Name";
            alternate["allPlayers"]![1]!["championName"]="星籁歌姬";
            type.GetField("_currentQueue",flags)!.SetValue(view,0);
            type.GetField("_snapshot",flags)!.SetValue(view,LiveMatchSnapshot.Parse(alternate));
            type.GetMethod("Render",flags)!.Invoke(view,null);
            Check(All(view).OfType<Label>().Any(l=>l.Text=="星籁歌姬") && imageCalls.ContainsKey(("champion",147)),"The live page resolves Seraphine's alternate key into the actual champion and portrait");
        }
        finally{champions.Remove(testSeraphine);}
        handler.Fail=true;
        var task=(Task)type.GetMethod("PollAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,null)!;
        while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);}task.GetAwaiter().GetResult();
        Check(!All(view).OfType<Panel>().Any(c=>c.Tag is string),"Disconnect clears previous player cards");
        form.Close();
        champions.Remove(testLux);champions.Remove(testMorgana);
        SynchronizationContext.SetSynchronizationContext(previousContext);
        Console.WriteLine("PASS: live match teams, unknown identity, duplicate champions, equipment, loopback endpoint, UI and disconnect cleanup.");
    }
}
