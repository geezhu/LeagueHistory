using League.Controls;
using League.Services;
using System.Reflection;

internal static class PlayerChampionChecks
{
    public static void Run()
    {
        var rows=new[]{new League.Models.OpggTier(99,"mid",.51,.1,null,2,2),new League.Models.OpggTier(99,"support",.53,.2,null,1,1)};
        var reference=PlayerChampionTier.FromOpgg(99,430,"","test",rows);
        if(reference.Position!="support" || !reference.ReferencePosition || !reference.Rate.Contains("参考辅助"))throw new Exception("Unknown lane uses a visibly labelled popular reference lane");
        var assigned=PlayerChampionTier.FromOpgg(99,420,"middle","test",rows);
        if(assigned.Position!="mid" || assigned.ReferencePosition || assigned.Tier!="T2")throw new Exception("Actual lane overrides popular lane");
        var missing=PlayerChampionTier.FromOpgg(99,420,"top","test",rows);
        if(missing.Tier!="—" || missing.Position!="top")throw new Exception("Missing actual-lane stats must not silently switch lanes");
        foreach(var queue in new[]{450,900,1900,1200,1300,1700,1710})
        {
            var modeTier=PlayerChampionTier.FromOpgg(99,queue,"middle","test",new[]{rows[0] with{Position="none"}});
            if(modeTier.Tier!="T2" || modeTier.Position!="none" || modeTier.ReferencePosition)throw new Exception("Special modes use their own non-lane statistics");
        }
        if(PlayerChampionTier.NormalizeQueue(0,"URF")!=900 || PlayerChampionTier.NormalizeQueue(3270,"ARAM")!=2400 || PlayerChampionTier.NormalizeQueue(480,"")!=480)throw new Exception("Queue and custom-mode normalization");
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        using var form=new Form{ClientSize=new Size(265,430)};
        using var card=new PlayerCardControl{Dock=DockStyle.Fill};form.Controls.Add(card);form.Show();
        var first=new TaskCompletionSource<PlayerChampionTier>();var second=new TaskCompletionSource<PlayerChampionTier>();
        card.CurrentChampionId=99;
        card.BindChampionInfo(id=>id==99?first.Task:second.Task,(_,_)=>{});
        card.CurrentChampionId=25;
        second.SetResult(new("T2","胜 52.0%","测试来源",450,"none"));
        Pump();
        first.SetResult(new("T5","胜 40.0%","过期请求",450,"none"));Pump();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var tier=(Label)typeof(PlayerCardControl).GetField("_heroTier",flags)!.GetValue(card)!;
        if(tier.Text!="T2 · 胜 52.0%")throw new Exception("Old champion response overwrote new selection");
        var bar=(Panel)typeof(PlayerCardControl).GetField("_championBar",flags)!.GetValue(card)!;
        if(bar.Bottom>card.SummaryPanel.Height || bar.Top<74)throw new Exception("Champion bar overlaps player statistics");
        using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){form.DrawToBitmap(image,form.ClientRectangle);image.Save(Path.Combine(AppContext.BaseDirectory,"player-champion-tier.png"));}
        card.CurrentChampionId=0;Pump();
        if(tier.Text!="")throw new Exception("Unselected hero retains old tier");
        form.Close();SynchronizationContext.SetSynchronizationContext(previous);
        Console.WriteLine("PASS: player champion bar, response ordering, unknown selection and layout.");
        void Pump(){for(int i=0;i<10;i++){Application.DoEvents();Thread.Sleep(5);}}
    }
}
