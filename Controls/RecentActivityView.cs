using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class RecentActivityView : Control
{
    private static readonly string[] Roles = { "fighter", "tank", "mage", "assassin", "marksman", "support" };
    private static readonly string[] RoleNames = { "战士", "坦克", "法师", "刺客", "射手", "辅助" };
    private readonly List<(string name, int count, int? level, Image? image)> _heroes = new();
    private double[] _weights = new double[6];
    private int _sample, _classified;
    private string _status = "正在加载近期对局…";
    private CancellationTokenSource? _request;
    public event Action<CareerGame[],string>? OverviewLoaded;
    private readonly ToolTip _tip = new();
    private readonly Image[] _roleIcons = Roles.Select(role =>
    {
        using var stream = typeof(RecentActivityView).Assembly.GetManifestResourceStream($"League.Assets.ChampionClasses.{role}.png")
            ?? throw new InvalidOperationException($"Missing champion class icon: {role}");
        using var source = Image.FromStream(stream);
        return (Image)new Bitmap(source);
    }).ToArray();
    private readonly List<(Rectangle bounds, string text)> _hits = new();

    public RecentActivityView()
    {
        DoubleBuffered = true; BackColor = Color.White; ForeColor = Color.FromArgb(36,63,98);
        Height = 86; Font = new Font("Microsoft YaHei UI", 9f);
        SizeChanged += (_, _) => { Height = Width >= 620 ? 86 : 150; Invalidate(); };
        MouseMove += (_, e) =>
        {
            string text = _hits.FirstOrDefault(h => h.bounds.Contains(e.Location)).text ?? _status;
            if (_tip.GetToolTip(this) != text) _tip.SetToolTip(this, text);
        };
    }

    public static double[] CountRoles(IEnumerable<CareerGame> games, JArray catalog, out int classified)
    {
        var weights = new double[6]; classified = 0;
        foreach (var game in games)
        {
            var champion = catalog.OfType<JObject>().FirstOrDefault(c => (int?)c["id"] == game.Champion);
            var roles = (champion?["roles"] as JArray)?.Values<string>().Select(r => Array.IndexOf(Roles, r?.ToLowerInvariant())).Where(i => i >= 0).Distinct().ToArray();
            if (roles == null || roles.Length == 0) continue;
            classified++;
            foreach (int role in roles) weights[role] += 1d / roles.Length;
        }
        return weights;
    }

    public async void LoadPlayer(string puuid)
    {
        _request?.Cancel(); _request?.Dispose(); var request = _request = new CancellationTokenSource();
        ClearImages(); _sample = _classified = 0; _weights = new double[6]; _status = "正在加载近期对局…"; Invalidate();
        OverviewLoaded?.Invoke(Array.Empty<CareerGame>(), "正在加载最近 50 场…");
        try
        {
            var service = new PlayerStatisticsService(() => FormMain.Globals.lcuClient.Client);
            var records = new JArray();
            CareerGame[] games = Array.Empty<CareerGame>();
            bool partial = false;
            // Share a bounded fifty-match sample with the overview; the hero strip uses its first twenty.
            for (int start = 0; start < 50; start += 20)
            {
                int count = Math.Min(20,50-start);
                var response = await service.GetAsync($"/lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex={start}&endIndex={start + count - 1}", request.Token);
                var batch = CareerStatistics.Records(response.Data);
                if (batch == null && response.Status is not (0 or 401 or 403))
                    batch = await FormMain.Globals.sgpClient.SgpFetchLatestMatches(puuid, start, count, cancellationToken: request.Token);
                if (IsDisposed || request.IsCancellationRequested) return;
                if (batch == null)
                {
                    if (records.Count == 0) { _status = "近期对局暂不可用，请稍后重新打开主页。"; OverviewLoaded?.Invoke(Array.Empty<CareerGame>(),"近期对局暂不可用"); Invalidate(); return; }
                    partial = true; break;
                }
                foreach (var record in batch) records.Add(record.DeepClone());
                games = CareerStatistics.Parse(records, puuid, includeRemakes: true).Games.Where(g => g.Champion > 0).OrderByDescending(g => g.Created).ThenByDescending(g => g.Id).Take(50).ToArray();
                if (batch.Count < count) break;
            }
            OverviewLoaded?.Invoke(games,$"最近 {games.Length} 场" + (partial ? " · 部分记录暂不可用" : ""));
            games = games.Take(20).ToArray();
            _sample = games.Length;
            if (_sample == 0) { _status = "暂无可统计的近期对局。"; Invalidate(); return; }
            _status = $"最近 {_sample} 场对局" + (partial ? " · 部分记录暂不可用" : ""); Invalidate();
            var catalogTask = service.GetAsync("/lol-game-data/assets/v1/champion-summary.json", request.Token);
            var masteryTask = ChampionMasteryService.Shared.GetAsync(puuid, request.Token);
            await Task.WhenAll(catalogTask, masteryTask);
            if (IsDisposed || request.IsCancellationRequested) return;
            _weights = CountRoles(games, catalogTask.Result.Data as JArray ?? new JArray(), out _classified);
            var mastery = ProfileMasteryView.SelectAll(masteryTask.Result.Data);
            foreach (var group in games.GroupBy(g => g.Champion).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(m => m.Created)).Take(3))
            {
                var resource = await FormMain.Globals.resLoading.GetChampionInfoAsync(group.Key);
                if (IsDisposed || request.IsCancellationRequested) { resource.image?.Dispose(); return; }
                int? level = (int?)mastery.FirstOrDefault(m => (int?)m["championId"] == group.Key)?["championLevel"];
                _heroes.Add((resource.name, group.Count(), level, resource.image));
            }
            Invalidate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recent activity: {ex.GetType().Name}");
            if (!IsDisposed && !request.IsCancellationRequested) { _status = "近期统计加载失败，请稍后重新打开主页。"; OverviewLoaded?.Invoke(Array.Empty<CareerGame>(),"统计加载失败"); Invalidate(); }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _hits.Clear();
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        void Text(string text, Rectangle rect) => TextRenderer.DrawText(g,text,Font,rect,ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        bool wide = Width >= 620;
        using var line = new Pen(Color.FromArgb(230,235,242));
        g.DrawLine(line,16,0,Width-16,0);
        g.DrawLine(line,16,Height-1,Width-16,Height-1);
        Text("近期使用英雄",new Rectangle(12,20,92,40));
        _hits.Add((new Rectangle(12,20,92,40), _status));
        for (int i = 0; i < _heroes.Count; i++)
        {
            var hero = _heroes[i]; var rect = new Rectangle(112+i*48,23,36,36);
            if (hero.image != null) g.DrawImage(hero.image,rect);
            else Text("—",rect);
            _hits.Add((rect,$"{hero.name}\n{hero.count} / {_sample} 场 · {100d*hero.count/Math.Max(1,_sample):0.#}%\n成就 {hero.level?.ToString() ?? "—"} 级\n{_status}"));
        }
        if (_heroes.Count == 0) Text(_status,new Rectangle(108,20,wide ? 154 : Math.Max(1,Width-120),42));
        int left = wide ? 276 : 12, top = wide ? 12 : 80;
        if (wide) g.DrawLine(line,266,22,266,62);
        float cell = Math.Max(1,(Width-left-12)/6f);
        for (int i = 0; i < 6; i++)
        {
            int x = (int)(left+i*cell), w = (int)cell;
            DrawRole(g,i,new Rectangle(x+(w-22)/2,top+3,22,22));
            double ratio = _classified > 0 ? _weights[i]/_classified : 0;
            Text(_classified > 0 ? $"{ratio:P0}" : "—",new Rectangle(x,top+29,w,24));
            _hits.Add((new Rectangle(x,top,w,56),$"{RoleNames[i]} · {(_classified > 0 ? ratio.ToString("P1") : "暂无数据")}\n按 {_classified} 场已知职业统计，双职业各计一半\n{_status}"));
        }
    }

    private void DrawRole(Graphics g, int role, Rectangle rect)
    {
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(_roleIcons[role], rect);
    }

    private void ClearImages() { foreach (var hero in _heroes) hero.image?.Dispose(); _heroes.Clear(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _request?.Cancel(); _request?.Dispose(); _tip.Dispose(); ClearImages(); foreach (var icon in _roleIcons) icon.Dispose(); }
        base.Dispose(disposing);
    }
}


