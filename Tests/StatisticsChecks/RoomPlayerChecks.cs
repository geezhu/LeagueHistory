using League.Clients;
using League.Controls;
using League.Managers;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Reflection;

internal static class RoomPlayerChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static JObject Player(int id, int champion = 0) => new() { ["summonerId"] = id, ["puuid"] = "test-player-" + id, ["championId"] = champion, ["team"] = 2 };
    private static PlayerMatchInfo Info(int id) => new()
    {
        Player = new PlayerInfo { SummonerId = id, Puuid = "test-player-" + id, GameName = "玩家 " + id, SoloRank = "白银 II", FlexRank = "未知", IsPublic = "公开", NameColor = Color.Black },
        MatchItems = new() { new ListViewItem(new[] { "", "大乱斗", "3/2/5", "2026-09-17" }) }
    };
    private static async Task Eventually(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(5);
        Check(condition(), message);
    }

    public static void Run()
    {
        Task.Run(AsyncChecks).GetAwaiter().GetResult();
        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            RenderChecks();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        Console.WriteLine("PASS: independent player loading, cancellation, roster reconciliation, retries, bounded logs, HTTP cancellation and WinForms cards.");
    }

    private static async Task AsyncChecks()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "room-player-checks");
        Directory.CreateDirectory(directory);
        var log = new RoomLoadDiagnostics(Path.Combine(directory, "diagnostic.log"));
        var gates = new ConcurrentDictionary<int, TaskCompletionSource<PlayerMatchInfo>>();
        var calls = new ConcurrentDictionary<int, int>();
        var tokens = new ConcurrentDictionary<int, CancellationToken>();
        RoomPlayerState[] latest = Array.Empty<RoomPlayerState>();
        using var loader = new RoomPlayerLoader((p, token) =>
        {
            int id = p.Value<int>("summonerId");
            calls.AddOrUpdate(id, 1, (_, count) => count + 1); tokens[id] = token;
            return gates.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }, action => { action(); return Task.CompletedTask; }, states => Volatile.Write(ref latest, states.ToArray()), log);
        long generation = loader.Reset();
        await loader.UpdateTeamAsync(new JArray(Player(1), Player(2), Player(3)), true, generation);
        await Eventually(() => calls.Count == 3, "Three player requests must start without awaiting each other");
        gates[2].SetResult(Info(2)); gates[3].SetResult(Info(3));
        await Eventually(() => Volatile.Read(ref latest).Count(s => s.Info != null) == 2, "Ready players must render while another player remains blocked");
        Check(latest.Single(s => s.Player.Value<int>("summonerId") == 1).Info == null, "Slow player remains independently pending");

        await loader.UpdateTeamAsync(new JArray(Player(3, 99), Player(2), Player(1)), true, generation);
        Check(latest.Length == 3 && latest[0].Player.Value<int>("championId") == 99 && latest[0].Info != null, "Reordering and champion changes preserve ready data");
        Check(calls.Values.All(c => c == 1), "Champion-only updates must not restart history requests");
        await loader.UpdateTeamAsync(new JArray(Player(2), Player(3)), true, generation);
        Check(tokens[1].IsCancellationRequested, "Leaving player cancels its request");
        gates[1].SetResult(Info(1));
        await Task.Delay(25);
        Check(latest.Length == 2 && latest.All(s => s.Player.Value<int>("summonerId") != 1), "Late result must not resurrect a removed player");

        await loader.UpdateTeamAsync(new JArray(Player(4)), false, generation);
        await Eventually(() => calls.ContainsKey(4), "Enemy request starts independently");
        long next = loader.Reset();
        await loader.UpdateTeamAsync(new JArray(Player(5)), true, next);
        await Eventually(() => calls.ContainsKey(5), "New match must start without waiting for the old match");
        gates[4].SetResult(Info(4));
        await loader.UpdateTeamAsync(new JArray(Player(1)), false, generation);
        gates[5].SetResult(Info(5));
        await Eventually(() => latest.Length == 1 && latest[0].Info?.Player.SummonerId == 5, "Old generation snapshots and responses must be ignored");

        var duplicate = new JArray(Player(1), Player(1), Player(2), Player(3), Player(4), Player(5), Player(6));
        Check(RoomPlayerLoader.Normalize(duplicate).Count == 5, "Roster deduplicates known IDs and caps the fixed team layout");
        var hidden = new JArray(Enumerable.Range(0,5).Select(i => new JObject { ["summonerId"] = 0, ["puuid"] = "OBFUSCATED_" + i, ["cellId"] = i }));
        Check(RoomPlayerLoader.Normalize(hidden).Count == 5, "Hidden players with zero IDs remain five distinct slots");
        var selected = RoomPlayerLoader.WithChampionSelections(new JArray(Player(1)),new JArray(Player(1,99),Player(2,25)));
        Check(selected.Count==1 && selected[0].Value<int>("championId")==99, "Selection data enriches known members without inventing team membership");

        int modeCalls=0; RoomPlayerState[] modeStates=Array.Empty<RoomPlayerState>();
        using var modes=new RoomPlayerLoader((p,t)=>{Interlocked.Increment(ref modeCalls);return Task.FromResult(Info(10));},
            a=>{a();return Task.CompletedTask;}, s=>Volatile.Write(ref modeStates,s.ToArray()),log);
        long modeGeneration=modes.Reset();var modePlayer=Player(10);modePlayer["__roomQueue"]="420";
        await modes.UpdateTeamAsync(new JArray(modePlayer),true,modeGeneration);
        await Eventually(()=>modeStates[0].Info!=null,"Initial mode loads");
        modePlayer["__roomQueue"]="450";
        await modes.UpdateTeamAsync(new JArray(modePlayer),true,modeGeneration);
        await Eventually(()=>modeCalls==2 && modeStates[0].Info!=null,"Changed queue reloads histories rather than keeping the previous mode cache");

        RoomPlayerState[] queuedStates=Array.Empty<RoomPlayerState>();
        using var queued=new RoomPlayerLoader(async (p,t)=>{await Task.Delay(Timeout.Infinite,t);return Info(1);},
            a=>{a();return Task.CompletedTask;},s=>Volatile.Write(ref queuedStates,s.ToArray()),log,TimeSpan.FromMilliseconds(40),TimeSpan.FromMilliseconds(5));
        await queued.UpdateTeamAsync(new JArray(Enumerable.Range(1,5).Select(id=>Player(id))),true,queued.Reset());
        await Eventually(()=>queuedStates.Length==5 && queuedStates.All(s=>s.CanRetry),"Queued players also have a bounded timeout when all active requests stall");

        int attempts = 0; RoomPlayerState[] retryStates = Array.Empty<RoomPlayerState>();
        using var retry = new RoomPlayerLoader(async (p, token) =>
        {
            if (Interlocked.Increment(ref attempts) <= 2) await Task.Delay(Timeout.Infinite, token);
            return Info(8);
        }, action => { action(); return Task.CompletedTask; }, states => Volatile.Write(ref retryStates, states.ToArray()), log,
            TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(5));
        long retryGeneration = retry.Reset();
        await retry.UpdateTeamAsync(new JArray(Player(8)), true, retryGeneration);
        await Eventually(() => retryStates.FirstOrDefault()?.CanRetry == true, "Timeout must terminate loading and expose manual retry");
        Check(attempts == 2 && retryStates[0].Status.Contains("超时"), "Automatic retries are bounded");
        await retry.RetryAsync(retryStates[0].Key, retryGeneration);
        await Eventually(() => retryStates[0].Info != null, "Manual retry recovers only the failed player");
        Check(attempts == 3 && !retryStates[0].CanRetry, "Successful retry clears failure state");
        retry.Reset(); retry.Reset(); // Completed entries must remain safe to cancel/dispose.

        // A callback already queued for an old generation must also be rejected at delivery time.
        var callbacks = new ConcurrentQueue<Action>(); int delivered = 0;
        using var delayedUi = new RoomPlayerLoader((p,t) => Task.FromResult(Info(9)), action =>
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            callbacks.Enqueue(() => { action(); completion.SetResult(); }); return completion.Task;
        }, _ => delivered++, log);
        var updating = delayedUi.UpdateTeamAsync(new JArray(Player(9)), true, delayedUi.Reset());
        delayedUi.Reset();
        Check(callbacks.TryDequeue(out var callback), "UI delivery was queued"); callback!(); await updating;
        Check(delivered == 0, "Generation must be checked inside UI callback, not just before posting it");

        await HttpCancellationChecks();
        await log.FlushAsync();
        string text = await File.ReadAllTextAsync(log.LogPath);
        Check(text.Contains("stage=timeout") && text.Contains("stage=complete") && !text.Contains("test-player-") && !text.Contains("玩家"), "Logs persist stages without raw player identities");
        await File.WriteAllTextAsync(log.LogPath, new string('x', 530000));
        log.Report(1,"rotation"); await log.FlushAsync();
        Check(File.Exists(log.LogPath + ".previous") && new FileInfo(log.LogPath).Length < 1000, "Diagnostic log rotates at its size limit");
    }

    private static async Task HttpCancellationChecks()
    {
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose();
        using var http = new HttpClient(new WaitingHandler()) { BaseAddress = new Uri("https://127.0.0.1/") };
        typeof(LcuClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, http);
        var flow = new GameflowService(client); var summoner = new SummonerService(client); var ranked = new RankedService(client);
        foreach (var request in new Func<CancellationToken, Task>[] {
            t => flow.GetChampSelectSessionAsync(t), t => flow.GetGameSessionAsync(t),
            t => summoner.GetCurrentSummonerAsync(t), t => summoner.GetSummonerByIdAsync("room-cancel-check",t),
            t => ranked.GetCurrentRankedStatsAsync("test-player",t) })
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            try { await request(cancellation.Token); throw new Exception("LCU cancellation was swallowed"); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        var sgp = new SgpSession();
        typeof(SgpSession).GetField("_lcuClient",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(sgp,http);
        using var refreshCancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        try { await sgp.LoadEntitlementsTokenAsync(refreshCancellation.Token); throw new Exception("SGP token refresh ignored cancellation"); }
        catch(OperationCanceledException)when(refreshCancellation.IsCancellationRequested) { }
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
    }

    private static void RenderChecks()
    {
        using var form = new QuietForm { ClientSize = new Size(1400,800), Location = new Point(-16000,-16000), StartPosition = FormStartPosition.Manual, ShowInTaskbar = false };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill }; form.Controls.Add(table);
        var ui = new PlayerCardUIManager(form,table);
        form.Show(); Application.DoEvents();
        RoomPlayerState State(int id, bool ally, int column, long revision, PlayerMatchInfo? info = null) => new(1,(ally?"ally/":"enemy/")+id,ally,column,Player(id),info,info==null?"正在读取战绩…":"",false,revision);
        var states = Enumerable.Range(1,5).Select((id,col) => State(id,true,col,1,Info(id)))
            .Concat(Enumerable.Range(6,5).Select((id,col) => State(id,false,col,1,Info(id)))).ToArray();
        ui.Render(states);
        PlayerCardControl Card(int col,int row) => table.GetControlFromPosition(col,row)!.Controls.OfType<PlayerCardControl>().Single();
        var original = Card(0,0); var originalEnemy = Card(0,1);
        Check(table.Controls.Count == 10 && !original.IsLoading, "Ten completed cards display without loading state");
        states[0] = states[0] with { Column=1, Revision=2, Player=Player(1,99) };
        states[1] = states[1] with { Column=0, Revision=2 };
        ui.Render(states);
        Check(table.Controls.Count == 10 && ReferenceEquals(Card(1,0),original) && ReferenceEquals(Card(0,1),originalEnemy), "Swapping ally slots retains card objects and leaves enemies in place");
        Check(original.CurrentChampionId==99 && original.ListViewControl.Items.Count==1, "Hero changes retain loaded history");
        var shared = Info(20); states[0] = states[0] with { Info=shared, Revision=3 }; states[1] = states[1] with { Info=shared, Revision=3 };
        ui.Render(states);
        Check(Card(0,0).ListViewControl.Items.Count==1 && Card(1,0).ListViewControl.Items.Count==1 && shared.MatchItems[0].ListView==null, "Cached ListViewItems must be cloned for each card");
        states[2] = states[2] with { Info=null, CanRetry=true, Status="读取超时", Revision=4 };
        ui.Render(states); Application.DoEvents();
        Check(!Card(2,0).IsLoading && Descendants(Card(2,0)).OfType<Button>().Any(b=>b.Text=="重试" && b.Visible), "Failed card visibly exits loading and offers retry");
        var failedCard = Card(2,0);
        var retryButton = Descendants(failedCard).OfType<Button>().Single(b=>b.Text=="重试");
        var historyHost = failedCard.ListViewControl.Parent!;
        Check(historyHost.Bottom <= retryButton.Parent!.Top && retryButton.Parent.Bottom <= failedCard.ClientSize.Height,
            "Retry status must reserve space rather than be covered by the native history list");
        string image = Path.Combine(AppContext.BaseDirectory,"room-player-checks","cards.png");
        using (var bitmap = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(image); }
        ui.Render(states.Skip(1).ToArray());
        Check(table.Controls.Count==9 && original.IsDisposed, "Departed player's complete card is disposed");
        ui.Render(Array.Empty<RoomPlayerState>()); Check(table.Controls.Count==0,"New session clears every card");
        form.Close();
    }
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach(Control child in parent.Controls) { yield return child; foreach(var descendant in Descendants(child)) yield return descendant; } }
    private sealed class QuietForm : Form { protected override bool ShowWithoutActivation => true; }
}
