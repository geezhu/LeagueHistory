using League.Clients;
using Newtonsoft.Json.Linq;

namespace League.Controls;

/// <summary>Client profile achievements, independent of match-history samples.</summary>
public sealed class ProfileMasteryView : UserControl
{
    private readonly PlayerStatisticsService _service;
    private readonly bool _sharedMastery;
    private readonly Func<int,Task<(Image? image,string name,string description)>> _champion;
    private readonly TableLayoutPanel _cards = new() { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
    private readonly Label _heading = new() { Text = "英雄成就", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI",11f,FontStyle.Bold), AutoEllipsis = true };
    public string OwnershipText { get; private set; } = "— / —";
    private string _owned = "—", _total = "—";
    private bool _masteryAvailable;
    private void UpdateCountHeading()
    {
        OwnershipText = _owned != "—" ? $"拥有 {_owned} / {_total}" : _masteryAvailable
            ? $"成就英雄 {_records.Length} / {_total}" : $"英雄总数 {_total}";
        _heading.Text = "英雄成就  ·  " + OwnershipText;
    }
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Color.FromArgb(111,123,139) };
    private readonly Button _refresh = new() { Text = "刷新", Dock = DockStyle.Right, Width = 64, FlatStyle = FlatStyle.Flat };
    private readonly Button _all = new() { Text = "全部 ›", Dock = DockStyle.Right, Width = 72, FlatStyle = FlatStyle.Flat, Enabled = false };
    private JObject[] _records = Array.Empty<JObject>();
    public event Action<JObject[]>? AllRequested;
    private CancellationTokenSource? _request;
    private string _puuid = "";
    public event Action<JObject>? ProfileLoaded;

    public ProfileMasteryView(PlayerStatisticsService? service = null, Func<int,Task<(Image? image,string name,string description)>>? champion = null)
    {
        _service = service ?? new(() => FormMain.Globals.lcuClient.Client);
        _sharedMastery = service == null;
        _champion = champion ?? FormMain.Globals.resLoading.GetChampionInfoAsync;
        BackColor = Color.White; ForeColor = Color.FromArgb(36,63,98); Height = 100; Padding = new Padding(16);
        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(_heading);
        header.Controls.Add(_refresh); _refresh.Click += (_, _) => LoadPlayer(_puuid, true);
        header.Controls.Add(_all);
        _all.FlatAppearance.BorderSize = 0;
        _all.Click += (_, _) => AllRequested?.Invoke(_records);
        for (int i=0;i<5;i++) _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20f));
        _cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(_cards); Controls.Add(_status); Controls.Add(header);
        _status.Text = "连接客户端后加载英雄成就。";
    }

    public static JObject[] SelectTop(JToken? data) => SelectAll(data).Where(p => (long)p["championPoints"]! > 0).Take(5).ToArray();

    // The client also lists the placeholder (-1) and classic-mode variants (60000+).
    public static JObject[] StandardChampions(JToken? data) => data is JArray array
        ? array.OfType<JObject>().Where(p => p["id"]?.Type == JTokenType.Integer && (int)p["id"]! > 0 && (int)p["id"]! < 60000)
            .GroupBy(p => (int)p["id"]!).Select(g => g.First()).ToArray() : Array.Empty<JObject>();

    public static JObject[] SelectAll(JToken? data) => data is JArray records
        ? records.OfType<JObject>().Where(p => p["championId"]?.Type == JTokenType.Integer && (int)p["championId"]! > 0 && p["championPoints"]?.Type == JTokenType.Integer && (long)p["championPoints"]! >= 0)
            .GroupBy(p => (int)p["championId"]!).Select(g => g.OrderByDescending(p => (long)p["championPoints"]!).First())
            .OrderByDescending(p => (long)p["championPoints"]!).ThenBy(p => (int)p["championId"]!).ToArray() : Array.Empty<JObject>();

    public async void LoadPlayer(string puuid, bool refresh = false)
    {
        if (string.IsNullOrWhiteSpace(puuid) || IsDisposed) return;
        _puuid = puuid; _request?.Cancel(); _request?.Dispose(); var request = _request = new CancellationTokenSource();
        ClearCards(); Height = 100; _refresh.Enabled = false; _status.Visible = true; _status.Text = "正在加载英雄成就…";
        _records = Array.Empty<JObject>(); _all.Enabled = false;
        _owned = _total = "—"; _masteryAvailable = false; UpdateCountHeading();
        _ = LoadProfileAsync(puuid, request.Token);
        try
        {
            StatisticsResponse response;
            bool stale=false;
            if(_sharedMastery)
            {
                var page=await ChampionMasteryService.Shared.GetAsync(puuid,request.Token,refresh);
                response=new(page.Data,page.Message);stale=page.Stale;
            }
            else response=await _service.GetAsync($"/lol-champion-mastery/v1/{Uri.EscapeDataString(puuid)}/champion-mastery",request.Token);
            if (IsDisposed || request.IsCancellationRequested) return;
            _records = SelectAll(response.Data); _all.Enabled = _records.Length > 0;
            _masteryAvailable = response.Data is JArray; UpdateCountHeading();
            var top = SelectTop(response.Data);
            if (top.Length == 0) { _status.Text = response.Data is JArray ? "暂无英雄成就记录。" : string.IsNullOrEmpty(response.Message) ? "暂时无法获取英雄成就。" : response.Message; return; }
            _status.Text = stale?"当前显示旧成就缓存，后台检查更新；点击刷新可等待最新结果。":""; _status.Visible = stale; Height = 360;
            int column = 0;
            foreach (var mastery in top)
            {
                int championId = (int)mastery["championId"]!;
                var resource = await _champion(championId);
                if (IsDisposed || request.IsCancellationRequested) return;
                var card = new Panel { Dock = DockStyle.Fill, MaximumSize = new Size(210,280), Margin = new Padding(6) };
                var icon = new MasteryPoster(column + 1, resource.image);
                string level = mastery["championLevel"]?.Type == JTokenType.Integer ? $"{mastery["championLevel"]} 级英雄成就" : "";
                var label = new Label { Dock = DockStyle.Bottom, Height = 90, TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                    Text = $"{(string.IsNullOrWhiteSpace(resource.name) ? "英雄 " + championId : resource.name)}\n{level}\n{(long)mastery["championPoints"]!:N0} 成就点数" };
                icon.Controls.Add(label); card.Controls.Add(icon); _cards.Controls.Add(card,column++,0);
                _ = icon.LoadArtworkAsync(championId);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"成就加载失败: {ex.GetType().Name}");
            if (!IsDisposed && !request.IsCancellationRequested) _status.Text = "英雄成就加载失败，请重试。";
        }
        finally { if (!IsDisposed && !request.IsCancellationRequested) _refresh.Enabled = true; }
    }

    private async Task LoadProfileAsync(string puuid, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _service.GetAsync("/lol-summoner/v1/summoners-by-puuid-cached/" + Uri.EscapeDataString(puuid), cancellationToken);
            if (!IsDisposed && !cancellationToken.IsCancellationRequested && profile.Data is JObject summoner)
                ProfileLoaded?.Invoke(summoner);
            if (IsDisposed || cancellationToken.IsCancellationRequested) return;
            var catalogTask = _service.GetAsync("/lol-game-data/assets/v1/champion-summary.json", cancellationToken);
            var inventoryTask = profile.Data?["summonerId"]?.Type == JTokenType.Integer
                ? _service.GetAsync($"/lol-champions/v1/inventories/{profile.Data["summonerId"]}/champions-minimal", cancellationToken)
                : Task.FromResult(new StatisticsResponse(null, ""));
            await Task.WhenAll(catalogTask, inventoryTask);
            if (IsDisposed || cancellationToken.IsCancellationRequested) return;
            var catalog = StandardChampions(catalogTask.Result.Data);
            var inventory = StandardChampions(inventoryTask.Result.Data);
            string total = catalog.Length > 0 ? catalog.Length.ToString() : "—";
            string owned = inventoryTask.Result.Data is JArray && catalog.Length > 0
                && catalog.All(c => inventory.Any(i => (int)i["id"]! == (int)c["id"]! && i["ownership"]?["owned"]?.Type == JTokenType.Boolean))
                ? inventory.Count(i => (bool?)i["ownership"]?["owned"] == true && catalog.Any(c => (int)c["id"]! == (int)i["id"]!)).ToString() : "—";
            _owned = owned; _total = total; UpdateCountHeading();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"玩家资料加载失败: {ex.GetType().Name}");
        }
    }

    private void ClearCards()
    {
        foreach (Control card in _cards.Controls.Cast<Control>().ToArray())
        {
            card.Dispose();
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _request?.Cancel(); _request?.Dispose(); ClearCards(); }
        base.Dispose(disposing);
    }
}
