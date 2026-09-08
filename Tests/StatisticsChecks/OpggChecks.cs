using League.Clients;
using League.Controls;
using League.Models;
using League.Services;
using League.Infrastructure;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Reflection;

internal static class OpggChecks
{
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
    private static JObject Fixture(string name)=>JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","Opgg",name+".json")));
    public static async Task RunAsync()
    {
        if(Environment.GetEnvironmentVariable("LEAGUE_OPGG_LIVE")=="1")
        {
            using var live=new OpggClient();
            foreach(var mode in new[]{"ranked","aram","arena"})
            {
                var query=new OpggQuery("global",mode,mode=="ranked"?"emerald_plus":"all","mid",103);
                var build=await live.GetBuildAsync(query,false,default);
                var tiers=await live.GetTiersAsync(query,false,default);
                Check(build.Query.ChampionId==103 && tiers.Rows.Length>0,"Live OP.GG request "+mode);
                Console.WriteLine($"PASS: live OP.GG {mode}, version {build.Version}, {tiers.Rows.Length} tier rows");
            }
        }
        foreach(var mode in new[]{"ranked","aram","arena"})
        {
            var query=new OpggQuery("global",mode,"all","mid",103).Normalize();
            var build=OpggBuild.Parse(Fixture(mode),query);
            Check(build.Version=="16.17" && build.Query.ChampionId==103,"Live OP.GG sample parses "+mode);
            Check(mode=="arena" || build.Runes.Length>0,"Rune schemes parsed for "+mode);
        }
        Check(OpggBuild.ParseTiers(Fixture("tiers"),"ranked").Any(t=>t.ChampionId==103 && t.Position=="MID"),"Ranked list preserves lane");
        var lanes=new[]{new OpggTier(67,"TOP",0.5,0.01,0.1,2,12),new OpggTier(67,"ADC",0.5,0.12,0.1,1,3),new OpggTier(103,"MID",0.5,0.2,0.1,1,1)};
        Check(OpggQuery.PositionForChampion(lanes,67,null)=="adc","Unassigned champion uses its most played available lane");
        Check(OpggQuery.PositionForChampion(lanes,67,"mid")=="adc","Unavailable assigned lane falls back to an available champion lane");
        Check(OpggQuery.PositionForChampion(lanes,67,"top")=="top","Valid assigned lane takes priority over popularity");
        Check(OpggQuery.PositionForChampion(lanes,999,null)==null,"Missing champion data never borrows another champion's lane");
        Check(OpggQuery.ModeForFollowing(2400)=="ranked" && OpggQuery.ModeForFollowing(900)=="urf"
            && OpggQuery.ModeForFollowing(450)=="aram" && OpggQuery.ModeForFollowing(1700)=="arena","Following falls back to ranked while preserving supported modes");
        Check(OpggQuery.ModeForFollowing(1900)=="urf" && OpggQuery.ModeForFollowing(1300)=="nexus_blitz","Rotating queues select their own OP.GG modes");
        foreach(var mode in new[]{"urf","nexus_blitz"})
        {
            var query=new OpggQuery("global",mode,"all","mid",103).Normalize();
            OpggClient.Validate(query);
            var build=OpggBuild.Parse(Fixture(mode),query);
            Check(query.Position=="none" && build.Runes.Length>0,"Rotating mode build and runes parse: "+mode);
            Check(build.Version==(mode=="urf"?"16.04":"13.23"),"Rotating modes preserve the actual source patch");
            Check(OpggBuild.ParseTiers(Fixture(mode+"-tiers"),mode).Length>0,"Rotating mode ranking parses: "+mode);
        }
        var empty=Fixture("aram");empty["data"]!["runes"]=new JArray();empty["data"]!["skills"]=new JArray();
        Check(OpggBuild.Parse(empty,new("global","aram","all","none",103)).Runes.Length==0,"Empty recommendations are supported");
        Check(OpggBuild.Ratio(0,0)==null,"Zero samples have unknown win rate");
        var calls=new List<string>();
        using var client=new OpggClient(new Handler(async (request,token)=>
        {
            calls.Add(request.RequestUri!.PathAndQuery);
            await Task.Delay(20,token);
            return Json(Fixture(request.RequestUri.AbsolutePath.Contains("/aram/")?"aram":"ranked"));
        }));
        var aram=new OpggQuery("global","aram","all","none",103);
        var loads=await Task.WhenAll(client.GetBuildAsync(aram,false,default),client.GetBuildAsync(aram,false,default));
        Check(calls.Count==1,"Concurrent identical OP.GG loads reuse cached response");
        loads[0].Data.RemoveAll();
        Check((await client.GetBuildAsync(aram,false,default)).Data["summary"]!=null,"Consumer mutation does not corrupt cache");
        await client.GetBuildAsync(aram,true,default);Check(calls.Count==2,"Refresh bypasses cache");
        await client.GetBuildAsync(new("kr","ranked","emerald_plus","mid",103),false,default);
        Check(calls.Count==3 && calls.Last().Contains("/kr/") && calls.Last().Contains("tier=emerald_plus"),"Cache separates region mode and tier");
        using(var slow=new OpggClient(new Handler(async (_,token)=> {await Task.Delay(10000,token);return Json(Fixture("aram"));})))
        using(var cancel=new CancellationTokenSource(30))
        {
            bool cancelled=false;try{await slow.GetBuildAsync(aram,false,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Cancelled OP.GG request stops waiting");
        }
        var runes=OpggBuild.Parse(Fixture("aram"),aram).Runes[0];
        Check(runes.PickRate>0,"Rune scheme retains pick rate separately from win rate");
        var styles=RuneResourceLoader.ParseStyles(JObject.Parse("{\"styles\":[{\"id\":8000,\"name\":\"精密\",\"slots\":[{\"perks\":[8005,8008]},{\"perks\":[5008,5005]}]}]}"));
        Check(styles[8000].Slots[0].SequenceEqual(new[]{8005,8008}),"Style parser preserves slot order and candidates");
        var treeType=typeof(PlayerCardControl).Assembly.GetType("League.Controls.OpggRuneTree")!;
        var selected=treeType.GetMethod("IsSelected",BindingFlags.Static|BindingFlags.NonPublic)!;
        var shardPlan=new OpggRunes(8000,8100,new[]{1,2,3,4,5,6,5008,5008,5001},1,0.5);
        Check((bool)selected.Invoke(null,new object[]{shardPlan,5008,0})! && (bool)selected.Invoke(null,new object[]{shardPlan,5008,1})!
            && !(bool)selected.Invoke(null,new object[]{shardPlan,5008,2})!,"Repeated shards highlight only their selected rows");
        foreach(bool existing in new[]{false,true})
        {
            string? method=null,path=null;JObject? body=null;
            using var lcu=new HttpClient(new Handler(async (request,token)=>
            {
                if(request.Method==HttpMethod.Get)return Json(existing
                    ? JArray.Parse("[{\"id\":99,\"name\":\"Personal\",\"isEditable\":true,\"current\":true},{\"id\":12,\"name\":\"[League] OP.GG Ahri\",\"isEditable\":true}]")
                    : JArray.Parse("[{\"id\":99,\"name\":\"Personal\",\"isEditable\":true,\"current\":true}]"));
                method=request.Method.Method;path=request.RequestUri!.AbsolutePath;
                body=JObject.Parse(await request.Content!.ReadAsStringAsync(token));return Json(new JObject());
            })) {BaseAddress=new Uri("https://localhost")};
            await OpggRuneService.ApplyAsync(lcu,runes,"阿狸",default);
            Check(method==(existing?"PUT":"POST") && path==(existing?"/lol-perks/v1/pages/12":"/lol-perks/v1/pages"),"Only tool-owned rune page is updated");
            Check(body!["selectedPerkIds"]!.Count()==9 && (bool?)body["current"]==true,"Full rune payload selects page");
        }
    }
    public static void Render()
    {
        var tooltipType=typeof(PlayerCardControl).Assembly.GetType("League.Controls.TooltipText")!;
        var plain=(string)tooltipType.GetMethod("PlainText",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{"第一段<br />第二段<BR>第三段<p>正文</p>末尾 &amp; 数值"})!;
        Check(plain.Contains("第一段\n第二段\n第三段") && plain.Contains("正文\n末尾 & 数值"),"Tooltip preserves HTML line breaks and decodes entities");
        var wrapped=(string)tooltipType.GetMethod("Format")!.Invoke(null,new object[]{"符文",string.Concat(Enumerable.Repeat("这是没有换行标签的长符文说明。",20))})!;
        Check(wrapped.Split('\n').Length>3 && wrapped.Replace("\r","").Replace("\n","")=="符文"+string.Concat(Enumerable.Repeat("这是没有换行标签的长符文说明。",20)),"Long tooltip wraps without dropping content");
        var type=typeof(PlayerCardControl).Assembly.GetType("League.Controls.OpggView")!;
        using var view=(Control)Activator.CreateInstance(type)!;
        using var form=new Form {ClientSize=new Size(1200,800)};
        form.Controls.Add(view);form.Show();Application.DoEvents();
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        using(var nav=(Control)Activator.CreateInstance(type)!)
        {
            var record=type.GetMethod("RecordNavigation",flags)!;
            var move=type.GetMethod("MoveNavigation",flags)!;
            var hero=type.GetField("_championId",flags)!;
            var back=(Button)type.GetField("_back",flags)!.GetValue(nav)!;
            var forward=(Button)type.GetField("_forward",flags)!.GetValue(nav)!;
            Check(!back.Enabled && !forward.Enabled,"Initial OP.GG page has no navigation history");
            hero.SetValue(nav,103);record.Invoke(nav,null);
            ((ComboBox)type.GetField("_mode",flags)!.GetValue(nav)!).SelectedIndex=1;
            record.Invoke(nav,null);
            Check((bool)move.Invoke(nav,new object[]{-1})! && (int)hero.GetValue(nav)! ==103
                && ((ComboBox)type.GetField("_mode",flags)!.GetValue(nav)!).SelectedIndex==0,"Back restores hero and ranked filters");
            Check((bool)move.Invoke(nav,new object[]{-1})! && (int)hero.GetValue(nav)! ==0 && !back.Enabled && forward.Enabled,"Back reaches original rankings");
            Check((bool)move.Invoke(nav,new object[]{1})! && (int)hero.GetValue(nav)! ==103,"Forward restores hero details");
            record.Invoke(nav,null);
            Check(forward.Enabled,"Refreshing the current page retains forward history");
            hero.SetValue(nav,67);record.Invoke(nav,null);
            Check(!forward.Enabled && !(bool)move.Invoke(nav,new object[]{1})!,"New navigation after going back discards the old forward branch");
            var tierBox=(ComboBox)type.GetField("_tier",flags)!.GetValue(nav)!;
            var navMode=(ComboBox)type.GetField("_mode",flags)!.GetValue(nav)!;
            Check(tierBox.Items.Count==10 && tierBox.SelectedIndex==4,"Complete ordered rank filters default to emerald plus");
            tierBox.SelectedIndex=5;
            Check((int)hero.GetValue(nav)! ==67,"Changing rank retains the hero detail page");
            navMode.SelectedIndex=1;Check(tierBox.SelectedIndex==0,"ARAM starts with all ranks");
            tierBox.SelectedIndex=2;
            navMode.SelectedIndex=2;Check(tierBox.SelectedIndex==0,"Arena uses all ranks");
            navMode.SelectedIndex=0;Check(tierBox.SelectedIndex==5,"Returning to ranked restores diamond plus");
            navMode.SelectedIndex=1;Check(tierBox.SelectedIndex==2,"ARAM retains its independent rank choice");
        }
        var follow=(CheckBox)type.GetField("_follow",flags)!.GetValue(view)!;
        Check(follow.Checked,"OP.GG follows champion selection by default");
        follow.Checked=true;
        var modeBox=(ComboBox)type.GetField("_mode",flags)!.GetValue(view)!;
        modeBox.SelectedIndex=1;
        Check(!follow.Checked && !((ComboBox)type.GetField("_position",flags)!.GetValue(view)!).Visible,
            "Manual mode switching disables following and hides inapplicable lane selector");
        var filterTimer=(System.Windows.Forms.Timer)type.GetField("_filterTimer",flags)!.GetValue(view)!;
        Check(filterTimer.Enabled,"Mode change schedules automatic refresh");
        modeBox.SelectedIndex=2;
        Check(!((ComboBox)type.GetField("_tier",flags)!.GetValue(view)!).Visible,"Arena mode hides rank filter");
        modeBox.SelectedIndex=1;filterTimer.Stop();
        var rankings=(Control)type.GetField("_tiers",flags)!.GetValue(view)!;
        var rows=OpggBuild.ParseTiers(Fixture("tiers"),"ranked").Where(r=>r.Position=="MID").ToArray();
        Check(rows.Any(r=>r.Counters.Length>0),"Ranking matchup avatars parse from source");
        rankings.GetType().GetMethod("SetRows")!.Invoke(rankings,new object[]{rows});
        ((Control)type.GetField("_content",flags)!.GetValue(view)!).Visible=false;
        rankings.Visible=true;rankings.BringToFront();Application.DoEvents();
        var grid=(DataGridView)rankings.Controls[0];
        Check(grid.Visible && grid.Width>600 && grid.Height>200,"Ranking grid is visible and fills the page: "+grid.Bounds+" / "+rankings.Bounds);
        Check(grid.Rows.Count==rows.Length && grid.Columns.Count==7,"Ranking presents seven columns and all matching champions");
        using(var rankingImage=new Bitmap(1200,800))
        {form.DrawToBitmap(rankingImage,new Rectangle(0,0,1200,800));rankingImage.Save(Path.Combine(AppContext.BaseDirectory,"opgg-ranking.png"));}
        var build=OpggBuild.Parse(Fixture("aram"),new("global","aram","all","none",103));
        var resource=League.FormMain.Globals.resLoading.Runes;
        var savedStyles=resource.Styles.ToArray();
        var plan=build.Runes[0];
        resource.Styles[plan.Primary]=new(plan.Primary,"主系","",new[]{
            new[]{plan.Perks[0],90001,90002},new[]{90003,plan.Perks[1],90004},new[]{plan.Perks[2],90005,90006},new[]{90007,90008,plan.Perks[3]},
            new[]{5008,5005,5007},new[]{5008,5010,5001},new[]{5001,5011,5013}});
        resource.Styles[plan.Secondary]=new(plan.Secondary,"副系","",new[]{new[]{90009},new[]{plan.Perks[4],90010,90011},new[]{90012,plan.Perks[5],90013},new[]{90014,90015,90016}});
        var task=(Task)type.GetMethod("RenderAsync",flags)!.Invoke(view,new object[]{build,CancellationToken.None})!;
        while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);}
        task.GetAwaiter().GetResult();Application.DoEvents();
        using var image=new Bitmap(1200,800);form.DrawToBitmap(image,new Rectangle(0,0,1200,800));
        image.Save(Path.Combine(AppContext.BaseDirectory,"opgg-view.png"));
        form.ClientSize=new Size(820,600);Application.DoEvents();
        using var narrow=new Bitmap(820,600);form.DrawToBitmap(narrow,new Rectangle(0,0,820,600));
        narrow.Save(Path.Combine(AppContext.BaseDirectory,"opgg-narrow.png"));
        var content=(FlowLayoutPanel)type.GetField("_content",flags)!.GetValue(view)!;
        var skillSection=content.Controls.Cast<Control>().First(c=>c.Controls.OfType<Label>().Any(l=>l.Text=="技能加点"));
        content.AutoScrollPosition=new Point(0,skillSection.Top-content.DisplayRectangle.Y);Application.DoEvents();
        using(var equipmentImage=new Bitmap(820,600)){form.DrawToBitmap(equipmentImage,new Rectangle(0,0,820,600));equipmentImage.Save(Path.Combine(AppContext.BaseDirectory,"opgg-equipment.png"));}
        var longBuild=OpggBuild.Parse(Fixture("ranked"),new("global","ranked","emerald_plus","mid",103));
        longBuild.Data["counters"]=new JArray(Enumerable.Range(1,32).Select(i=>new JObject {["champion_id"]=i,["play"]=1000+i,["win"]=i%2==0?600:400}));
        var matchupTask=(Task)type.GetMethod("RenderMatchupsAsync",flags)!.Invoke(view,new object[]{longBuild,CancellationToken.None})!;
        while(!matchupTask.IsCompleted){Application.DoEvents();Thread.Sleep(5);}matchupTask.GetAwaiter().GetResult();Application.DoEvents();
        var matchupSection=content.Controls.Cast<Control>().Last();
        var matchupLayout=matchupSection.Controls.OfType<FlowLayoutPanel>().Single();
        Check(matchupLayout.Controls.Cast<Control>().Sum(c=>c.Controls.Cast<Control>().Count(r=>r.Tag is int))==32,"Long matchup list retains every opponent beyond the first five");
        content.AutoScrollPosition=new Point(0,matchupSection.Top-content.DisplayRectangle.Y);Application.DoEvents();
        using(var matchupsImage=new Bitmap(820,600)){form.DrawToBitmap(matchupsImage,new Rectangle(0,0,820,600));matchupsImage.Save(Path.Combine(AppContext.BaseDirectory,"opgg-matchups.png"));}
        content.AutoScrollPosition=Point.Empty;
        form.ClientSize=new Size(640,800);Application.DoEvents();
        using(var compact=new Bitmap(640,800)){form.DrawToBitmap(compact,new Rectangle(0,0,640,800));compact.Save(Path.Combine(AppContext.BaseDirectory,"opgg-runes-compact.png"));}
        resource.Styles.Clear();foreach(var entry in savedStyles)resource.Styles.Add(entry.Key,entry.Value);
        form.Close();
    }
    private static HttpResponseMessage Json(JToken value)=>new(HttpStatusCode.OK){Content=new StringContent(value.ToString())};
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
}
