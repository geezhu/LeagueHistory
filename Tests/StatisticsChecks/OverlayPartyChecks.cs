using League.Models;
using League.Services;

internal static class OverlayPartyChecks
{
    private static void Check(bool value,string note){if(!value)throw new Exception(note);}
    public static void Run()
    {
        var players=Enumerable.Range(0,10).Select(i=>new LoadingPlayer("k"+i,"同名","account-"+i,i+1,99,i<5?100:200,i==0)).ToArray();
        var roster=new LoadingRoster("match",450,"ARAM",players);
        var cache=players.Select(p=>new PlayerMatchInfo{Player=new PlayerInfo{Puuid=p.Puuid,SummonerId=p.SummonerId,GameName="同名",GroupKey="unchanged",NameColor=Color.Blue},PartyId="unchanged"}).ToArray();
        try
        {
            for(int i=0;i<10;i++)cache[i].MatchKeys.AddRange(i<5?["a_100","b_100"]:i<7?["c_200","d_200"]:["e_200","f_200"]);
            var groups=OverlayPartyAnalysis.ForRoster(roster,cache);
            Check(groups.Count==10 && players.Take(5).All(p=>groups[p.Key].Size==5 && groups[p.Key].Color==groups["k0"].Color),"A five-player history group has one color and size five");
            Check(groups["k5"].Size==2 && groups["k6"]==groups["k5"] && groups["k7"].Size==3 && groups["k9"]==groups["k7"],"Two and three-player groups coexist within one side");
            Check(groups.Values.Select(g=>g.Color).Distinct().Count()==3,"Separate groups and opposing sides remain visually distinct");
            Check(groups["k0"].Badge=="5人" && groups["k0"].Label.Contains("疑似") && groups["k0"].Description.Contains("不能确认当前实际组队"),"The visible count is explained as a history-based inference");
            Check(cache.All(c=>c.PartyId=="unchanged" && c.Player.GroupKey=="unchanged" && c.Player.NameColor==Color.Blue),"Overlay analysis never mutates the main page's cached party metadata");
            var reversed=OverlayPartyAnalysis.ForRoster(roster,cache.Reverse());
            Check(groups.All(g=>reversed[g.Key]==g.Value),"Cache enumeration order does not change grouping or colors");
            Check(OverlayPartyAnalysis.ForRoster(null,cache).Count==0 && OverlayPartyAnalysis.ForRoster(roster,[]).Count==0,"No active roster or no history produces no invented groups");
            Check(OverlayPartyAnalysis.ForRoster(roster with{Players=[players[0]]},cache).Count==0,"Out-of-roster cached friends cannot inflate a current group");
            var hidden=roster with{Players=players.Select(p=>p.Key=="k4"?p with{IdentityHidden=true}:p).ToArray()};
            var partial=OverlayPartyAnalysis.ForRoster(hidden,cache);
            Check(!partial.ContainsKey("k4") && partial["k0"].Size==4,"Hidden players are not guessed into a five-player group");
            var missing=roster with{Players=players.Select(p=>p.Key=="k4"?p with{Puuid="",SummonerId=0}:p).ToArray()};
            Check(!OverlayPartyAnalysis.ForRoster(missing,cache).ContainsKey("k4"),"Matching display names or champions do not replace account identity");
            var byId=roster with{Players=players.Select(p=>p.Key=="k0"?p with{Puuid=""}:p).ToArray()};
            Check(OverlayPartyAnalysis.ForRoster(byId,cache)["k0"].Size==5,"A confirmed unique summoner ID can join the existing cache");
            var contradictory=roster with{Players=players.Select(p=>p.Key=="k0"?p with{Puuid="another-account"}:p).ToArray()};
            Check(!OverlayPartyAnalysis.ForRoster(contradictory,cache).ContainsKey("k0"),"A conflicting PUUID never falls back to someone else's summoner ID");
            Check(!OverlayPartyAnalysis.ForRoster(roster,cache.Append(cache[0])).ContainsKey("k0"),"Ambiguous cached accounts cannot supply a marker");
            var duplicate=roster with{Players=[players[0],players[0] with{Key="duplicate",Puuid=""}]};
            Check(OverlayPartyAnalysis.ForRoster(duplicate,cache).Count==0,"Mixed PUUID and summoner-ID entries cannot count one account twice");
            foreach(var info in cache)info.MatchKeys.Clear();
            cache[0].MatchKeys.AddRange(["one_100","one_100",""]);cache[1].MatchKeys.AddRange(["one_100","one_100",""]);
            Check(OverlayPartyAnalysis.ForRoster(roster,cache).Count==0,"Duplicate keys, blank keys and one shared match do not meet the threshold");
            cache[0].MatchKeys.Add("two_100");cache[1].MatchKeys=["one_200","two_200"];
            Check(OverlayPartyAnalysis.ForRoster(roster,cache).Count==0,"Facing each other in the same games is not shared-team history");
            cache[1].MatchKeys=["one_100","two_100","three_100","four_100"];cache[2].MatchKeys=["three_100","four_100"];
            var chain=OverlayPartyAnalysis.ForRoster(roster,cache);
            Check(chain.Count==3 && chain.Values.All(g=>g.Size==3),"Connected history associations preserve the existing main-page detection rule");
            foreach(var info in cache)info.MatchKeys.Clear();
            Check(OverlayPartyAnalysis.ForRoster(roster,cache).Count==0,"A disappeared association does not leave a retained marker");
            Console.WriteLine("PASS: cached five/two/three-player groups, distinct colors, hidden/unknown identities, same-game opposing teams, duplicates, no cache mutation and marker removal.");
        }
        finally{foreach(var info in cache)info.HeroIcons.Dispose();}
    }
}
