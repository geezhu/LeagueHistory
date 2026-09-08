using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Net;

internal static class Program
{
    private static void Check(bool condition,string message) { if (!condition) throw new Exception(message); }
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        string historyFile = Path.Combine(Path.GetTempPath(),"league-search-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var history = new League.Services.PlayerSearchHistory(historyFile);
            history.Add("玩家#123"); history.Add("Other#456"); history.Add(" 玩家#123 ");
            Check(history.Items.Count == 2 && history.Items[0] == "玩家#123", "Search history deduplicates and promotes");
            Check(new League.Services.PlayerSearchHistory(historyFile).Items.SequenceEqual(history.Items), "Search history survives reload");
            history.Add("invalid"); Check(history.Items.Count == 2,"Invalid ID excluded");
            for (int i=0;i<25;i++) history.Add($"Player{i}#123");
            Check(history.Items.Count == 20 && history.Items[0] == "Player24#123","Search history bounded");
            history.Remove("player24#123"); Check(history.Items.Count == 19,"History removal ignores case");
            history.Clear(); Check(new League.Services.PlayerSearchHistory(historyFile).Items.Count == 0,"History clear persists");
            File.WriteAllText(historyFile,"invalid json");
            Check(new League.Services.PlayerSearchHistory(historyFile).Items.Count == 0,"Damaged history does not prevent startup");
        }
        finally { if (File.Exists(historyFile)) File.Delete(historyFile); }
        Check(LcuProcessDiscovery.TryParse("\"C:\\Program Files\\LeagueClientUx.exe\" \"--app-port=12345\" --remoting-auth-token=\"test-value\"",out var connectionPort,out var connectionToken)
            && connectionPort == "12345" && connectionToken == "test-value", "Quoted LCU arguments");
        Check(!LcuProcessDiscovery.TryParse("client.exe --app-port=70000 --remoting-auth-token=test",out _,out _), "Reject invalid port");
        Check(!LcuProcessDiscovery.TryParse("client.exe --app-port=12345",out _,out _), "Reject missing authentication");
        Check(LcuProcessDiscovery.TryReadCommandLine(Environment.ProcessId,out var ownCommandLine,out _) && ownCommandLine.Length > 0, "Native command line query");
        Check(MatchMap.GetName(null) == "未知地图", "Missing map must not be inferred from queue");
        Check(MatchMap.GetName(JObject.Parse("{\"json\":{\"info\":{\"mapId\":12}}}")) == "随机地图", "ARAM ID must not imply a specific bridge");
        Check(MatchMap.GetName(new JObject { ["mapId"] = 11 }) == "召唤师峡谷", "Rift map name");
        foreach (var (mutator,name) in new[] { ("MapSkin_Map12_Bloom","莲华栈桥"), ("MapSkin_HA_Bilgewater","屠夫之桥"), ("MapSkin_HA_Crepe","进步之桥") })
        {
            var mapData = new JObject { ["mapId"] = 12, ["gameModeMutators"] = new JArray("ARAM",mutator.ToLowerInvariant()) };
            Check(MatchMap.GetName(new JObject { ["json"] = new JObject { ["info"] = mapData } }) == name, "Map variants in nested SGP data");
            mapData["mapId"] = 11;
            Check(MatchMap.GetName(mapData) == "召唤师峡谷", "Bridge overrides apply only to map 12");
        }
        Check(MatchMap.GetName(new JObject { ["mapId"] = 12, ["gameModeMutators"] = new JArray() }) == "嚎哭深渊", "Explicit base map without variant");
        var metricSelection = new MatchMetricSelection();
        Check(metricSelection.Set(new[] { "a", "b", "c", "d", "e", "f" }, true), "Six chart metrics allowed");
        Check(!metricSelection.Set(new[] { "g", "h" }, true) && metricSelection.Count == 6 && !metricSelection.Contains("g"), "Over-limit group is atomic");
        Check(metricSelection.Set(new[] { "a", "b" }, false) && metricSelection.Set(new[] { "g", "h" }, true), "Chart slots reusable after deselection");
        Check(metricSelection.Set(new[] { "g", "g" }, true) && metricSelection.Count == 6, "Duplicate selection does not consume slots");
        Check(metricSelection.SetGroup(new[] { "x", "y" }, true) && metricSelection.Count == 2 && !metricSelection.Contains("g"), "Full selection replaced by another group");
        Check(metricSelection.SetGroup(new[] { "x", "y" }, false) && metricSelection.Count == 0, "Selected group can be deselected");
        Check(MatchMetricCatalog.Number(new JObject(), "goldEarned") == null && MatchMetricCatalog.Number(new JObject { ["goldEarned"] = 0 }, "goldEarned") == 0, "Chart distinguishes zero and missing");
        Check(MatchStatisticsView.Value(new JObject { ["goldEarned"] = 0 }, "goldEarned") == "0", "Statistics preserves zero");
        Check(MatchStatisticsView.Value(new JObject(), "goldEarned") == "—", "Statistics preserves missing values");
        Check(MatchStatisticsView.Value(JObject.Parse("{\"stats\":{\"kills\":1,\"deaths\":0,\"assists\":2}}"), "kda") == "1/0/2", "Statistics supports nested stats");
        Check(MatchStatisticsView.Value(new JObject { ["firstBloodKill"] = false }, "firstBloodKill") == "○", "Statistics preserves false");
        var rankNames = RankedStats.FromJson(JObject.Parse("""
        {"queueMap":{"RANKED_SOLO_5x5":{"tier":"PLATINUM","division":"II","highestTier":"MASTER","previousSeasonEndTier":"DIAMOND","previousSeasonEndDivision":"II"}}}
        """))["单双排"];
        Check(rankNames.FormattedTier == "华贵铂金 II" && rankNames.HighestTierDisplay == "超凡大师"
            && rankNames.PreviousSeasonDisplay == "璀璨钻石 II", "Full Chinese rank names in all profile fields");
        var game = JObject.Parse("""
        {"gameId":1,"queueId":420,"participants":[{"participantId":1,"puuid":"self","championId":1,"stats":{"win":true,"kills":10,"deaths":0,"assists":5,"totalDamageDealtToChampions":20000,"goldEarned":12000,"pentaKills":0}}]}
        """);
        var remake = (JObject)game.DeepClone(); remake["gameId"] = 2; remake["participants"]![0]!["stats"]!["gameEndedInEarlySurrender"] = true;
        Check(MatchOutcomeFilter.GetWin(remake,"self") == null,"Remakes excluded from win/loss filters");
        using (var remakeInfo = new MatchInfo { ResultText = "胜利", RawGameData = new JObject { ["json"] = new JObject { ["info"] = remake.DeepClone() } } })
            Check(remakeInfo.ResultText == "重开" && remakeInfo.ResultColor == MatchResult.RemakeColor,"Remake overrides win text and color");
        Check(MatchResult.IsRemake(new JObject { ["gameEndedInEarlySurrender"] = true }),"Root remake flag");
        Check(MatchResult.IsRemake(null,new[] { new JObject { ["gameEndedInEarlySurrender"] = true } }),"Flattened participant remake flag");
        Check(!MatchResult.IsRemake(new JObject { ["gameDuration"] = 90, ["gameEndedInSurrender"] = true }),"Short matches and ordinary surrenders are not assumed remakes");
        Check(MatchOutcomeFilter.GetWin(game,"self") == true && MatchOutcomeFilter.GetWin(game,"other") == null,
            "Outcome filtering must select the viewed player");
        var lost = (JObject)game.DeepClone(); lost["participants"]![0]!["stats"]!["win"] = false;
        Check(MatchOutcomeFilter.GetWin(new JObject { ["json"] = new JObject { ["info"] = lost } },"self") == false,
            "SGP nested defeat filtering");
        var noResult = (JObject)game.DeepClone(); ((JObject)noResult["participants"]![0]!["stats"]!).Remove("win");
        Check(MatchOutcomeFilter.GetWin(noResult,"self") == null,"Missing result must not count as defeat");
        var source = new JArray(game,lost,game,lost,game);
        var pager = new OutcomeHistoryPager();
        var offsets = new List<int>();
        Task<JArray> FetchOutcomes(int offset,int count)
        {
            offsets.Add(offset);
            return Task.FromResult(new JArray(source.Skip(offset).Take(count)));
        }
        Check(pager.LoadPage("self",true,1,2,FetchOutcomes).GetAwaiter().GetResult().Count == 2
            && pager.Scanned == 3 && !pager.Exhausted,"Stop scanning as soon as first result page is full");
        int requests = offsets.Count;
        pager.LoadPage("self",true,1,2,FetchOutcomes).GetAwaiter().GetResult();
        Check(offsets.Count == requests,"Previously loaded pages must not fetch again");
        Check(pager.LoadPage("self",true,2,2,FetchOutcomes).GetAwaiter().GetResult().Count == 1
            && offsets[requests] == 3 && pager.Exhausted,"Continue from cursor and preserve final partial page");
        requests = offsets.Count;
        Check(pager.LoadPage("self",false,1,2,FetchOutcomes).GetAwaiter().GetResult().Count == 2
            && offsets.Count == requests,"Changing outcome reuses scanned records");
        var wrong = (JObject)game.DeepClone(); wrong["gameId"] = 3; wrong["participants"]![0]!["puuid"] = "other";
        var records = new JArray(game,game.DeepClone(),remake,wrong);
        var stats = CareerStatistics.Parse(records,"self");
        Check(stats.Games.Count == 1 && stats.Kda == 15 && stats.Wins == 1,"Deduplication/zero-death KDA");
        Check(stats.ExcludedRemakes == 1 && stats.Unrecognized == 1,"Remake/player identity filtering");
        var missing = (JObject)game.DeepClone(); ((JObject)missing["participants"]![0]!["stats"]!).Remove("kills");
        Check(CareerStatistics.Parse(new JArray(missing),"self").Kda == null,"Missing kills must not become zero");
        Check(CareerStatistics.Records(new JObject { ["games"]="invalid" }) == null,"Unknown record shape");
        var sgp = new JObject { ["json"]=new JObject { ["info"]=game.DeepClone() } };
        Check(CareerStatistics.Parse(new JArray(sgp),"self").Games.Count == 1,"SGP info wrapper");
        var players = new List<JObject>
        {
            new() { ["participantId"]=1,["teamId"]=200,["riotIdGameName"]="红队玩家" },
            new() { ["participantId"]=7,["teamId"]=100,["riotIdGameName"]="蓝队玩家" }
        };
        var timeline = JObject.Parse("""
        {"frames":[
          {"timestamp":0,"participantFrames":{"1":{"totalGold":500},"7":{"totalGold":600}},"events":[]},
          {"timestamp":60000,"participantFrames":{"1":{"totalGold":1300},"7":{"totalGold":1500}},"events":[{"type":"CHAMPION_KILL","timestamp":40000,"killerId":7,"victimId":1,"position":{"x":100,"y":200}}]},
          {"timestamp":120000,"participantFrames":{"7":{"totalGold":2500}},"events":[{"type":"ELITE_MONSTER_KILL","timestamp":90000,"killerId":0,"monsterType":"DRAGON"}]},
          {"timestamp":180000,"participantFrames":{"1":{"totalGold":3200},"7":{"totalGold":3400}},"events":[]}
        ]}
        """);
        var parsed = MatchTimeline.Parse(timeline,players);
        Check(parsed.Frames[0].Blue == 600 && parsed.Frames[0].Red == 500,"Teams must use participant mapping, not ID ranges");
        Check(parsed.Frames[2].Red == null,"Missing gold must leave a gap");
        Check(parsed.GoldDifference(parsed.Frames[0]) == 100 && parsed.GoldDifference(parsed.Frames[2]) == null,
            "Average gold difference and missing intervals");
        Check(parsed.GoldDifference(new GoldFrame(0,300,500)) == -200 && parsed.Events[0].Team == 100,
            "Negative gold advantage and killer team mapping");
        Check(parsed.Events[0].Actor == "蓝队玩家" && parsed.Events[0].X == 100 && parsed.Events[1].Actor == "环境","Event identities/positions");
        Check(MatchTimeline.Parse(new JArray(),players).Frames.Count == 0,"Unexpected timeline shape");
        var handler = new Handler(path => path.Contains("game-timelines") ? new(HttpStatusCode.OK,timeline.ToString())
            : path.Contains("career-stats") ? new(HttpStatusCode.NotFound,"") : new(HttpStatusCode.OK,new JObject { ["games"]=new JObject { ["games"]=records } }.ToString()));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new PlayerStatisticsService(() => client);
        Check(new PlayerStatisticsService(() => null).GetAsync("/",default).GetAwaiter().GetResult().Data == null,"Disconnected client");
        using var forbiddenClient = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden,""))) { BaseAddress=client.BaseAddress };
        Check(new PlayerStatisticsService(() => forbiddenClient).GetAsync("/",default).GetAwaiter().GetResult().Status == 403,"Privacy status");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { service.GetAsync("/",canceled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation swallowed"); }
        catch (OperationCanceledException) { }
        using var form = new Form { ClientSize=new Size(850,650),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000),Opacity=0 };
        var view = new TimelineView(new MatchInfo { GameId=1,QueueId="q_450",AllParticipants=players },service) { Dock=DockStyle.Fill };
        form.Controls.Add(view); form.Show(); Application.DoEvents();
        Control[] Children(Control root) => root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Children(c))).ToArray();
        var events = Children(view).OfType<ListView>().Single();
        Check(events.Items.Count == 2,"Timeline event rendering");
        Children(view).OfType<ComboBox>().Single().SelectedIndex=1; Check(events.Items.Count == 1,"Kill filter");
        Children(view).OfType<TrackBar>().Single().Value=2;
        using (var bitmap=new Bitmap(view.Width,view.Height)) { view.DrawToBitmap(bitmap,view.ClientRectangle); bitmap.Save(Path.Combine(AppContext.BaseDirectory,"timeline-preview.png")); }
        view.Dispose();
        var career = new CareerStatisticsView(service) { Dock=DockStyle.Fill }; form.Controls.Add(career); career.LoadPlayer("self"); Application.DoEvents();
        Check(Children(career).OfType<ListView>().Single().Items.Count == 1,"Career fallback mode table");
        Check(Children(career).OfType<Label>().Any(l=>l.Text.Contains("不代表完整生涯")),"Fallback scope disclosure");
        using (var bitmap=new Bitmap(career.Width,career.Height)) { career.DrawToBitmap(bitmap,career.ClientRectangle); bitmap.Save(Path.Combine(AppContext.BaseDirectory,"career-preview.png")); }
        career.Dispose();
        int deniedCalls=0;
        using var deniedClient = new HttpClient(new Handler(_=> { deniedCalls++; return new(HttpStatusCode.Forbidden,""); })) { BaseAddress=client.BaseAddress };
        var deniedView=new CareerStatisticsView(new PlayerStatisticsService(()=>deniedClient),(_,_)=>throw new Exception("403 must not fall back"));
        form.Controls.Add(deniedView); deniedView.LoadPlayer("self"); Application.DoEvents();
        Check(deniedCalls==1,"Denied career must not request history");
        deniedView.Dispose();
        var masteryData=JArray.Parse("""
        [{"championId":1,"championLevel":10,"championPoints":12000},{"championId":2,"championLevel":20,"championPoints":80000},{"championId":3,"championLevel":8,"championPoints":9000},{"championId":1,"championPoints":1000},{"championId":4}]
        """);
        Check(ProfileMasteryView.SelectTop(masteryData).Select(p=>(int)p["championId"]!).SequenceEqual(new[]{2,1,3}),"Mastery ordering/deduplication/missing scores");
        var sixMasteries = new JArray(Enumerable.Range(1,6).Select(id => new JObject { ["championId"] = id, ["championPoints"] = id * 100 }));
        Check(ProfileMasteryView.SelectTop(sixMasteries).Length == 5, "Homepage previews five champions");
        Check(ProfileMasteryView.StandardChampions(JArray.Parse("[{\"id\":-1},{\"id\":1},{\"id\":1},{\"id\":2},{\"id\":60001}]")).Length == 2, "Ownership roster excludes placeholder, duplicates and classic variants");
        var masteryPaths = new List<string>();
        using var masteryClient=new HttpClient(new Handler(path=>
        {
            masteryPaths.Add(path);
            return path == "/lol-champion-mastery/v1/self/champion-mastery"
                ? new(HttpStatusCode.OK, masteryData.ToString())
                : new(HttpStatusCode.NotFound, "");
        })) { BaseAddress=client.BaseAddress };
        using var icon=new Bitmap(32,32);
        var masteryView=new ProfileMasteryView(new PlayerStatisticsService(()=>masteryClient),id=>Task.FromResult<(Image?,string,string)>((icon,"英雄 "+id,""))) { Dock=DockStyle.Fill };
        form.Controls.Add(masteryView);masteryView.LoadPlayer("self");Application.DoEvents();
        Check(Children(masteryView).OfType<PictureBox>().Count()==3,"Three mastery champions rendered");
        Check(Children(masteryView).OfType<Label>().Any(l=>l.Text.Contains("80,000")),"Mastery points displayed");
        Check(masteryView.OwnershipText.StartsWith("成就英雄 3 /"), "Unavailable inventory uses clearly labelled mastery count");
        using (var largeName = new LinkLabel { Size = new Size(320,36), Font = new Font("Segoe UI", 15f) })
        {
            League.UIHelpers.PlayerNamePresentation.Apply(largeName, "Player#123", false);
            var privacyBadge = largeName.Controls.OfType<Label>().Single();
            Check(privacyBadge.Font.SizeInPoints == largeName.Font.SizeInPoints && largeName.Padding.Right >= largeName.Font.Height, "Profile privacy badge scales with player name");
        }
        JObject[]? allMastery = null;
        masteryView.AllRequested += records => allMastery = records;
        Children(masteryView).OfType<Button>().Single(b => b.Text == "全部 ›").PerformClick();
        Check(allMastery?.Length == 3, "All mastery navigation reuses loaded records");
        var extendedMastery = (JArray)masteryData.DeepClone();
        extendedMastery.Add(JObject.Parse("{\"championId\":5,\"championPoints\":0,\"championLevel\":0}"));
        Check(ProfileMasteryView.SelectAll(extendedMastery).Length == 4 && ProfileMasteryView.SelectTop(extendedMastery).Length == 3, "Gallery includes zero-point records beyond homepage preview");
        var galleryType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.MasteryGalleryView")!;
        var backgroundType = typeof(ProfileMasteryView).Assembly.GetType("League.Controls.ProfileBackgroundPanel")!;
        var findSplash = backgroundType.GetMethod("FindSplash")!;
        var selectSkin = backgroundType.GetMethod("SelectSkin")!;
        Check((int)selectSkin.Invoke(null, new object?[] { new JObject(), masteryData })! == 2000, "Missing profile background uses highest mastery champion base skin");
        Check((int)selectSkin.Invoke(null, new object?[] { JObject.Parse("{\"backgroundSkinId\":103015}"), masteryData })! == 103015, "Explicit client background takes priority over mastery");
        Check((int)selectSkin.Invoke(null, new object?[] { new JObject(), new JArray() })! == 0, "No profile or mastery keeps default background");
        var skinFixture = JObject.Parse("{\"skins\":[{\"id\":103000,\"splashPath\":\"/lol-game-data/assets/center.jpg\",\"uncenteredSplashPath\":\"/lol-game-data/assets/full.jpg\"}]}");
        Check((string?)findSplash.Invoke(null, new object[] { skinFixture, 103000 }) == "/lol-game-data/assets/full.jpg", "Profile background resolves exact skin artwork");
        Check(findSplash.Invoke(null, new object[] { skinFixture, 103001 }) == null, "Missing background skin does not substitute another skin");
        using (var background = (Control)Activator.CreateInstance(backgroundType)!)
        {
            background.Size = new Size(800,200);
            var name = new LinkLabel { Text = "Player", LinkColor = Color.Blue, BackColor = Color.LightBlue };
            background.Controls.Add(name);
            var setArtwork = backgroundType.GetMethod("SetArtwork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            setArtwork.Invoke(background, new object[] { new Bitmap(100,100) });
            Check(name.LinkColor == Color.White && name.BackColor == Color.Transparent, "Artwork uses readable transparent identity controls");
            setArtwork.Invoke(background, new object?[] { null });
            Check(name.LinkColor == Color.Blue && name.BackColor == Color.LightBlue, "Missing background restores original profile colors");
        }
        using var gallery = (Control)Activator.CreateInstance(galleryType, new object[] { ProfileMasteryView.SelectAll(extendedMastery), "— / —" })!;
        gallery.Dock = DockStyle.Fill;
        form.Controls.Add(gallery); gallery.BringToFront(); Application.DoEvents();
        Check(Children(gallery).OfType<PictureBox>().Count() == 4, "Gallery renders every deduplicated mastery record");
        using (var screenshot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            gallery.DrawToBitmap(screenshot, new Rectangle(Point.Empty, gallery.Size));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "mastery-gallery.png"));
        }
        Check(masteryPaths.Contains("/lol-champion-mastery/v1/self/champion-mastery") && !masteryPaths.Any(p=>p.Contains("lol-collections")), "Mastery uses PUUID endpoint despite unavailable profile");
        form.Close();
        Console.WriteLine("PASS: identity, remakes, duplicates, missing values, SGP wrapper, team mapping, frame gaps, events, cancellation, privacy, fallback scope, WinForms controls.");
    }

    private sealed record Reply(HttpStatusCode Status,string Body);
    private sealed class Handler(Func<string,Reply> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var result=reply(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(result.Status) { Content=new StringContent(result.Body) });
        }
    }
}
