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
        var rankNames = RankedStats.FromJson(JObject.Parse("""
        {"queueMap":{"RANKED_SOLO_5x5":{"tier":"PLATINUM","division":"II","highestTier":"MASTER","previousSeasonEndTier":"DIAMOND","previousSeasonEndDivision":"II"}}}
        """))["单双排"];
        Check(rankNames.FormattedTier == "华贵铂金 II" && rankNames.HighestTierDisplay == "超凡大师"
            && rankNames.PreviousSeasonDisplay == "璀璨钻石 II", "Full Chinese rank names in all profile fields");
        var game = JObject.Parse("""
        {"gameId":1,"queueId":420,"participants":[{"participantId":1,"puuid":"self","championId":1,"stats":{"win":true,"kills":10,"deaths":0,"assists":5,"totalDamageDealtToChampions":20000,"goldEarned":12000,"pentaKills":0}}]}
        """);
        var remake = (JObject)game.DeepClone(); remake["gameId"] = 2; remake["participants"]![0]!["stats"]!["gameEndedInEarlySurrender"] = true;
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
        var view = new TimelineView(new MatchInfo { GameId=1,AllParticipants=players },service) { Dock=DockStyle.Fill };
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
        using var masteryClient=new HttpClient(new Handler(path=>new(HttpStatusCode.OK,path.Contains("summoners-by-puuid") ? "{\"summonerId\":42}" : masteryData.ToString()))) { BaseAddress=client.BaseAddress };
        using var icon=new Bitmap(32,32);
        var masteryView=new ProfileMasteryView(new PlayerStatisticsService(()=>masteryClient),id=>Task.FromResult<(Image?,string,string)>((icon,"英雄 "+id,""))) { Dock=DockStyle.Fill };
        form.Controls.Add(masteryView);masteryView.LoadPlayer("self");Application.DoEvents();
        Check(Children(masteryView).OfType<PictureBox>().Count()==3,"Three mastery champions rendered");
        Check(Children(masteryView).OfType<Label>().Any(l=>l.Text.Contains("80,000")),"Mastery points displayed");
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
