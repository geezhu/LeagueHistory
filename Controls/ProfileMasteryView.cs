using League.Clients;
using Newtonsoft.Json.Linq;

namespace League.Controls;

/// <summary>Client profile achievements, independent of match-history samples.</summary>
public sealed class ProfileMasteryView : UserControl
{
    private readonly PlayerStatisticsService _service;
    private readonly Func<int,Task<(Image? image,string name,string description)>> _champion;
    private readonly TableLayoutPanel _cards = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 26, ForeColor = Color.FromArgb(111,123,139) };
    private readonly Button _refresh = new() { Text = "刷新", Dock = DockStyle.Right, Width = 64, FlatStyle = FlatStyle.Flat };
    private CancellationTokenSource? _request;
    private string _puuid = "";
    public event Action<JObject>? ProfileLoaded;

    public ProfileMasteryView(PlayerStatisticsService? service = null, Func<int,Task<(Image? image,string name,string description)>>? champion = null)
    {
        _service = service ?? new(() => FormMain.Globals.lcuClient.Client);
        _champion = champion ?? FormMain.Globals.resLoading.GetChampionInfoAsync;
        BackColor = Color.White; ForeColor = Color.FromArgb(36,63,98); Height = 100; Padding = new Padding(16);
        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(new Label { Text = "最高英雄成就", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI",11f,FontStyle.Bold) });
        header.Controls.Add(_refresh); _refresh.Click += (_, _) => LoadPlayer(_puuid);
        for (int i=0;i<3;i++) _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        _cards.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(_cards); Controls.Add(_status); Controls.Add(header);
        _status.Text = "连接客户端后加载英雄成就。";
    }

    public static JObject[] SelectTop(JToken? data) => data is JArray records
        ? records.OfType<JObject>().Where(p => p["championId"]?.Type == JTokenType.Integer && p["championPoints"]?.Type == JTokenType.Integer && (long)p["championPoints"]! > 0)
            .GroupBy(p => (int)p["championId"]!).Select(g => g.OrderByDescending(p => (long)p["championPoints"]!).First())
            .OrderByDescending(p => (long)p["championPoints"]!).Take(3).ToArray() : Array.Empty<JObject>();

    public async void LoadPlayer(string puuid)
    {
        if (string.IsNullOrWhiteSpace(puuid) || IsDisposed) return;
        _puuid = puuid; _request?.Cancel(); _request?.Dispose(); var request = _request = new CancellationTokenSource();
        ClearCards(); Height = 100; _refresh.Enabled = false; _status.Text = "正在加载英雄成就…";
        try
        {
            var profile = await _service.GetAsync("/lol-summoner/v1/summoners-by-puuid-cached/" + Uri.EscapeDataString(puuid),request.Token);
            if (IsDisposed || request.IsCancellationRequested) return;
            if (profile.Data is not JObject summoner || summoner["summonerId"]?.Type != JTokenType.Integer)
            { _status.Text = "暂时无法获取玩家资料。"; return; }
            ProfileLoaded?.Invoke(summoner);
            long id = (long)summoner["summonerId"]!;
            var response = await _service.GetAsync($"/lol-collections/v1/inventories/{id}/champion-mastery",request.Token);
            if (IsDisposed || request.IsCancellationRequested) return;
            var top = SelectTop(response.Data);
            if (top.Length == 0) { _status.Text = response.Data is JArray ? "暂无英雄成就记录。" : "暂时无法获取英雄成就。"; return; }
            _status.Text = ""; Height = 252;
            int column = 0;
            foreach (var mastery in top)
            {
                int championId = (int)mastery["championId"]!;
                var resource = await _champion(championId);
                if (IsDisposed || request.IsCancellationRequested) return;
                var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6), BackColor = Color.FromArgb(244,247,251) };
                var icon = new PictureBox { Size = new Size(64,64), Top = 10, SizeMode = PictureBoxSizeMode.Zoom,
                    Image = resource.image == null ? null : new Bitmap(resource.image) };
                card.Resize += (_, _) => icon.Left = (card.Width-icon.Width)/2;
                string level = mastery["championLevel"]?.Type == JTokenType.Integer ? $"{mastery["championLevel"]} 级英雄成就" : "";
                var label = new Label { Dock = DockStyle.Bottom, Height = 72, TextAlign = ContentAlignment.MiddleCenter,
                    Text = $"{(string.IsNullOrWhiteSpace(resource.name) ? "英雄 " + championId : resource.name)}\n{level}\n{(long)mastery["championPoints"]!:N0} 成就点数" };
                card.Controls.Add(icon); card.Controls.Add(label); _cards.Controls.Add(card,column++,0);
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

    private void ClearCards()
    {
        foreach (Control card in _cards.Controls.Cast<Control>().ToArray())
        {
            foreach (var picture in card.Controls.OfType<PictureBox>()) picture.Image?.Dispose();
            card.Dispose();
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _request?.Cancel(); _request?.Dispose(); ClearCards(); }
        base.Dispose(disposing);
    }
}
