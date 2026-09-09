using System.Reflection;

internal static class AssistantChecks
{
    public static void Run()
    {
        var type=typeof(League.FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        var place=type.GetMethod("Placement",BindingFlags.Static|BindingFlags.NonPublic)!;
        Rectangle Place(Rectangle client,Rectangle screen,Size size)=>(Rectangle)place.Invoke(null,new object[]{client,screen,size})!;
        var screen=new Rectangle(0,0,1920,1080);
        var right=Place(new Rectangle(100,100,1000,700),screen,new Size(320,630));
        if(right.Left!=1106 || !screen.Contains(right))throw new Exception("Assistant should attach on the right when space permits");
        var left=Place(new Rectangle(800,100,1000,700),screen,new Size(320,630));
        if(left.Left!=474 || !screen.Contains(left))throw new Exception("Assistant should fall back to the left");
        var secondary=new Rectangle(-1280,0,1280,720);
        var center=type.GetMethod("EquipmentEditorBounds",BindingFlags.Static|BindingFlags.NonPublic)!;
        var centered=(Rectangle)center.Invoke(null,new object[]{new Rectangle(400,100,1000,700),screen,new Size(660,620)})!;
        if(centered.Location!=new Point(570,140))throw new Exception("Equipment editor must center on LOL, not the assistant");
        var fitted=(Rectangle)center.Invoke(null,new object[]{secondary,secondary,new Size(1500,900)})!;
        if(!secondary.Contains(fitted))throw new Exception("Equipment editor must fit a small secondary monitor");
        var clamped=Place(new Rectangle(-1280,0,1280,720),secondary,new Size(320,900));
        if(!secondary.Contains(clamped))throw new Exception("Assistant must stay inside a smaller secondary monitor");
        var skills=type.GetMethod("ParseSkills",BindingFlags.Static|BindingFlags.NonPublic)!;
        var spellOptions=Newtonsoft.Json.Linq.JArray.Parse("[{\"ids\":[4,14],\"pick_rate\":0.7,\"play\":100,\"win\":50},{\"ids\":[4,12],\"pick_rate\":0.3,\"play\":20,\"win\":12}]");
        var selectSpells=type.GetMethod("SelectSpellRecommendations",BindingFlags.Static|BindingFlags.NonPublic)!;
        var best=((Newtonsoft.Json.Linq.JObject?,Newtonsoft.Json.Linq.JObject?))selectSpells.Invoke(null,new object[]{spellOptions})!;
        if((int?)best.Item1?["ids"]?[1]!=14 || (int?)best.Item2?["ids"]?[1]!=12)throw new Exception("Summoner spell usage and win-rate recommendations must be selected independently");
        var empty=((Newtonsoft.Json.Linq.JObject?,Newtonsoft.Json.Linq.JObject?))selectSpells.Invoke(null,new object[]{Newtonsoft.Json.Linq.JArray.Parse("[{\"ids\":[4,12],\"play\":0,\"win\":0}]")})!;
        if(empty.Item1!=null || empty.Item2!=null)throw new Exception("Missing spell rates must not be treated as zero-rate recommendations");
        var data=Newtonsoft.Json.Linq.JObject.Parse("{\"passive\":{\"name\":\"Passive\",\"description\":\"First<br>Second\"},\"spells\":[{\"spellKey\":\"w\",\"name\":\"Shield\"},{\"spellKey\":\"q\",\"name\":\"Strike\"}]}");
        var parsed=Newtonsoft.Json.Linq.JArray.FromObject(skills.Invoke(null,new object[]{data})!);
        if(parsed.Count!=3 || (string?)parsed[1]?["Key"]!="Q" || (string?)parsed[2]?["Key"]!="W" || ((string?)parsed[0]?["Description"])?.Contains("<br>")==true)
            throw new Exception("Skill descriptions must preserve passive and map ability keys while removing markup");
        using var owner=new League.FormMain();
        using var assistant=(Form)Activator.CreateInstance(type,new object[]{owner})!;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var spellService=typeof(League.FormMain).Assembly.GetType("League.Services.ChampionSelectSpellService")!;
        foreach(bool accept in new[]{true,false})
        {
            int first=7,second=4;
            using var spellClient=new HttpClient(new ChatHandler(request=>
            {
                if(request.Method==HttpMethod.Patch)
                {
                    var payload=Newtonsoft.Json.Linq.JObject.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    if((int?)payload["spell1Id"]!=14 || (int?)payload["spell2Id"]!=4)throw new Exception("Flash must retain its existing slot");
                    if(accept){first=14;second=4;}
                    return new(System.Net.HttpStatusCode.OK);
                }
                return new(System.Net.HttpStatusCode.OK){Content=new StringContent($"{{\"localPlayerCellId\":0,\"myTeam\":[{{\"cellId\":0,\"championId\":103,\"spell1Id\":{first},\"spell2Id\":{second}}}]}}")};
            })){BaseAddress=new Uri("https://test.invalid")};
            bool success=false;
            try
            {
                Task.Run(async()=>await (Task)spellService.GetMethod("ApplyAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{spellClient,new[]{4,14},103,CancellationToken.None})!).GetAwaiter().GetResult();success=true;
            }
            catch(InvalidOperationException){}
            if(success!=accept)throw new Exception("Spell configuration must verify the resulting client state");
        }
        Console.WriteLine("PASS: summoner spell application preserves slots and verifies actual changes.");
        var modeFilter=(ComboBox)type.GetField("_mode",flags)!.GetValue(assistant)!;
        var regionFilter=(ComboBox)type.GetField("_region",flags)!.GetValue(assistant)!;
        var tierFilter=(ComboBox)type.GetField("_tier",flags)!.GetValue(assistant)!;
        var laneFilter=(ComboBox)type.GetField("_lane",flags)!.GetValue(assistant)!;
        League.Models.OpggQuery Query()=>(League.Models.OpggQuery)type.GetProperty("SelectedQuery",flags)!.GetValue(assistant)!;
        regionFilter.SelectedIndex=1;tierFilter.SelectedIndex=5;laneFilter.SelectedIndex=1;
        if(Query().Region!="kr" || Query().Tier!="diamond_plus" || Query().Position!="top")throw new Exception("Assistant filters must feed the request");
        modeFilter.SelectedIndex=2;
        if(Query().Mode!="aram" || Query().Tier!="all" || laneFilter.Enabled)throw new Exception("ARAM must use its own tier and disable lanes");
        modeFilter.SelectedIndex=1;
        if(Query().Tier!="diamond_plus")throw new Exception("Mode-specific tier must survive switching");
        modeFilter.SelectedIndex=3;
        if(regionFilter.Enabled || tierFilter.Enabled || laneFilter.Enabled)throw new Exception("Mayhem must disable unsupported filters");
        modeFilter.SelectedIndex=0;regionFilter.SelectedIndex=0;tierFilter.SelectedIndex=0;laneFilter.SelectedIndex=0;
        Console.WriteLine("PASS: assistant filters drive requests and preserve mode-specific tiers.");
        int notices=0;string phase="ChampSelect";bool reject=false;
        using var chatClient=new HttpClient(new ChatHandler(request=>
        {
            string path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("gameflow-phase"))return new(System.Net.HttpStatusCode.OK){Content=new StringContent("\""+phase+"\"")};
            if(path.EndsWith("conversations"))return new(System.Net.HttpStatusCode.OK){Content=new StringContent("[{\"id\":\"private\",\"type\":\"chat\"},{\"id\":\"room\",\"type\":\"championSelect\"}]")};
            if(path!="/lol-chat/v1/conversations/room/messages" || request.Method!=HttpMethod.Post)throw new Exception("Notice must target champion select only");
            notices++;return new(reject?System.Net.HttpStatusCode.Forbidden:System.Net.HttpStatusCode.OK);
        })){BaseAddress=new Uri("https://test.invalid")};
        bool Announce(string message)=>((Task<bool>)type.GetMethod("AnnounceRunesAsync",flags)!.Invoke(assistant,new object[]{chatClient,message,CancellationToken.None})!).GetAwaiter().GetResult();
        if(!Announce("configured") || !Announce("configured") || notices!=1)throw new Exception("Repeated rune notice must be deduplicated");
        phase="InProgress";
        if(Announce("another") || notices!=1)throw new Exception("Notice must not send outside champion select");
        phase="ChampSelect";reject=true;
        if(Announce("another"))throw new Exception("Chat rejection must be reported as unsent");
        Console.WriteLine("PASS: rune notices use champion-select chat, deduplicate, and report rejection.");
        var schemes=new[]{new League.Models.OpggRunes(8000,8100,Enumerable.Range(1,9).ToArray(),100,.5),new League.Models.OpggRunes(8100,8000,Enumerable.Range(11,9).ToArray(),80,.55)};
        var highlights=type.GetMethod("RuneHighlights",BindingFlags.NonPublic|BindingFlags.Static)!;
        var tagged=new[]{schemes[0] with {PickRate=.6},schemes[1] with {PickRate=.4}};
        var popular=(string[])highlights.Invoke(null,new object[]{tagged[0],tagged})!;
        var winner=(string[])highlights.Invoke(null,new object[]{tagged[1],tagged})!;
        if(popular.Length!=1 || popular[0]!="使用率最高" || winner.Length!=1 || winner[0]!="胜率最高")throw new Exception("Rune highlights must independently compare wins and usage");
        var missing=schemes[0] with {WinRate=null,PickRate=null};
        if(((string[])highlights.Invoke(null,new object[]{missing,new[]{missing}})!).Length!=0)throw new Exception("Missing rates must not receive highest labels");
        var resources=League.FormMain.Globals.resLoading.Runes;
        var original=resources.Runes.ToArray();resources.Runes.Clear();
        resources.Runes.Add(new League.Models.RuneInfo{id=1,name="First keystone",shortDesc="First summary",longDesc="First full description",iconPath=""});
        resources.Runes.Add(new League.Models.RuneInfo{id=11,name="Second keystone",shortDesc="Second<br>summary",longDesc="Second full description",iconPath=""});
        try
        {
        type.GetMethod("AddRuneChoices",flags)!.Invoke(assistant,new object[]{schemes,CancellationToken.None});
        var body=(FlowLayoutPanel)type.GetField("_body",flags)!.GetValue(assistant)!;
        var select=body.Controls[0].Controls.OfType<ComboBox>().Single();
        select.SelectedIndex=1;
        if(!ReferenceEquals(type.GetField("_runes",flags)!.GetValue(assistant),schemes[1]))throw new Exception("Switching rune schemes must change the applied payload");
        var labels=body.Controls[0].Controls.OfType<Label>().ToArray();
        if(!select.Items[1]!.ToString()!.Contains("Second keystone") || labels.Any(l=>l.Text.Contains("summary")))
            throw new Exception("Scheme selector must retain client rune names without expanded descriptions");
        }
        finally{resources.Runes.Clear();resources.Runes.AddRange(original);}
        ((System.Windows.Forms.Timer)type.GetField("_timer",flags)!.GetValue(assistant)!).Stop();
        var scroll=(FlowLayoutPanel)type.GetField("_body",flags)!.GetValue(assistant)!;
        type.GetMethod("ClearBody",flags)!.Invoke(assistant,null);
        scroll.Controls.Add(new Panel{Size=new Size(282,80),Margin=new Padding(0,0,0,4)});
        void Line(string text)=>type.GetMethod("Line",flags)!.Invoke(assistant,new object[]{text,true});
        Line("T1 / 50.5% / MID");Line("Runes");
        type.GetMethod("AddRuneChoices",flags)!.Invoke(assistant,new object[]{schemes,CancellationToken.None});
        var runeIcons=scroll.Controls[3].Controls.OfType<FlowLayoutPanel>().Single();
        runeIcons.Controls.Clear();
        for(int i=0;i<9;i++)runeIcons.Controls.Add(new PictureBox{Size=new Size(26,26),Margin=new Padding(2),BackColor=Color.Teal});
        Line("Core items / Q > W > E");
        var equipmentData=Newtonsoft.Json.Linq.JObject.Parse("{\"core_items\":[{\"ids\":[99991,99992,99993],\"pick_rate\":0.6,\"play\":100,\"win\":52},{\"ids\":[99991,99992,99994],\"pick_rate\":0.3,\"play\":100,\"win\":55}],\"starter_items\":[{\"ids\":[99990,99989,99989]}],\"boots\":[{\"ids\":[99988]}],\"last_items\":[{\"ids\":[99991]},{\"ids\":[99994]},{\"ids\":[99995]}]}");
        var equipmentBuild=new League.Models.OpggBuild(new("global","ranked","emerald_plus","mid",103),"16.17",equipmentData,new(),Array.Empty<League.Models.OpggRunes>());
        foreach(int id in Enumerable.Range(99800,26))((Newtonsoft.Json.Linq.JArray)equipmentData["last_items"]!).Add(new Newtonsoft.Json.Linq.JObject{["ids"]=new Newtonsoft.Json.Linq.JArray(id),["win"]=52,["play"]=100});
        type.GetMethod("AddEquipmentChoices",flags)!.Invoke(assistant,new object[]{equipmentBuild,CancellationToken.None});
        Line("Summoner spells");
        var layoutOptions=Newtonsoft.Json.Linq.JArray.Parse("[{\"ids\":[99998,99999],\"pick_rate\":0.8294,\"win_rate\":0.5276}]");
        ((Task)type.GetMethod("AddSpellRecommendations",flags)!.Invoke(assistant,new object[]{layoutOptions,CancellationToken.None})!).GetAwaiter().GetResult();
        void ReplaceSpellIcons(Control cards)
        {
            foreach(Control card in cards.Controls)
            {
                var icons=card.Controls.OfType<FlowLayoutPanel>().Single();icons.Controls.Clear();
                for(int i=0;i<2;i++)icons.Controls.Add(new PictureBox{Size=new Size(32,32),Margin=new Padding(2),BackColor=Color.Teal});
            }
        }
        ReplaceSpellIcons(scroll.Controls[7]);
        assistant.Show();Application.DoEvents();
        if(scroll.VerticalScroll.Visible || scroll.HorizontalScroll.Visible)throw new Exception("Normal assistant recommendations should fit a single page");
        using(var preview=new Bitmap(assistant.Width,assistant.Height))
        {
            assistant.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));
            preview.Save(Path.Combine(AppContext.BaseDirectory,"assistant-single-page.png"));
        }
        Console.WriteLine("PASS: normal assistant recommendations fit without scrolling.");
        modeFilter.SelectedIndex=3;Application.DoEvents();
        if(regionFilter.Visible || tierFilter.Visible || laneFilter.Visible || modeFilter.Parent!.Height!=44)throw new Exception("Mayhem must hide irrelevant filters and remove their row");
        using(var mayhemPreview=new Bitmap(assistant.Width,assistant.Height))
        {
            assistant.DrawToBitmap(mayhemPreview,new Rectangle(Point.Empty,mayhemPreview.Size));
            mayhemPreview.Save(Path.Combine(AppContext.BaseDirectory,"assistant-mayhem-toolbar.png"));
        }
        modeFilter.SelectedIndex=0;Application.DoEvents();
        if(!regionFilter.Visible || !tierFilter.Visible || !laneFilter.Visible)throw new Exception("Ranked filters must reappear when switching back");
        bool editorRendered=false;
        using(var closeEditor=new System.Windows.Forms.Timer{Interval=300})
        {
            closeEditor.Tick+=(_,_)=>
            {
                var editor=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="OP.GG 出装方案");
                if(editor==null)return;
                closeEditor.Stop();
                var choices=editor.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<ComboBox>().ToArray();
                choices[0].SelectedIndex=1;
                editor.PerformLayout();
                using var screenshot=new Bitmap(editor.Width,editor.Height);editor.DrawToBitmap(screenshot,new Rectangle(Point.Empty,screenshot.Size));
                screenshot.Save(Path.Combine(AppContext.BaseDirectory,"assistant-equipment-editor.png"));
                editorRendered=true;editor.Close();
            };
            closeEditor.Start();
            scroll.Controls[5].Controls.OfType<Button>().Single().PerformClick();
        }
        if(!editorRendered)throw new Exception("Equipment editor must open from assistant");
        type.GetMethod("ClearBody",flags)!.Invoke(assistant,null);
        scroll.Controls.Add(new Panel{Size=new Size(270,650)});
        ((Task)type.GetMethod("AddSpellRecommendations",flags)!.Invoke(assistant,new object[]{layoutOptions,CancellationToken.None})!).GetAwaiter().GetResult();
        var columns=scroll.Controls[1];
        foreach(Control card in columns.Controls)
        {
            var icons=card.Controls.OfType<FlowLayoutPanel>().Single();
            icons.Controls.Clear();
            for(int i=0;i<2;i++)icons.Controls.Add(new PictureBox{Size=new Size(32,32),Margin=new Padding(2),BackColor=Color.Teal});
        }
        assistant.Show();Application.DoEvents();
        foreach(int height in new[]{590,420})
        {
            assistant.ClientSize=new Size(320,height);Application.DoEvents();
            scroll.AutoScrollPosition=new Point(0,10000);Application.DoEvents();
            var footer=assistant.Controls.Cast<Control>().Single(c=>c.Dock==DockStyle.Bottom);
            if(scroll.Bottom>footer.Top || scroll.HorizontalScroll.Visible)throw new Exception("Assistant content must stay above fixed actions without horizontal scrolling");
            foreach(Control card in columns.Controls)
            {
                var rate=card.Controls.OfType<Label>().Last();
                var bounds=scroll.RectangleToClient(rate.RectangleToScreen(rate.ClientRectangle));
                if(bounds.Top<0 || bounds.Bottom>scroll.ClientSize.Height-12)throw new Exception($"Final spell rate must be fully visible: rate={bounds}, viewport={scroll.ClientSize}, card={card.Bounds}, columns={columns.Bounds}, display={scroll.DisplayRectangle}");
            }
        }
        using(var bitmap=new Bitmap(assistant.Width,assistant.Height))
        {
            assistant.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory,"assistant-footer.png"));
        }
        Console.WriteLine("PASS: assistant bottom rates remain visible above fixed actions at normal and short heights.");
        Console.WriteLine("PASS: assistant placement, left fallback, and monitor bounds.");
    }
    private sealed class ChatHandler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(send(request));
    }
}
