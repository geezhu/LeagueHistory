using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class MasteryGalleryView : UserControl
{
    private readonly FlowLayoutPanel _grid = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), BackColor = Color.FromArgb(244,247,251) };
    private readonly Dictionary<MasteryPoster, int> _pending = new();
    private readonly System.Windows.Forms.Timer _loadTimer = new() { Interval = 100 };
    private bool _loading;
    public event Action? CloseRequested;

    public MasteryGalleryView(JObject[] records, string ownership = "— / —")
    {
        BackColor = Color.White;
        Padding = new Padding(16);
        var header = new Panel { Dock = DockStyle.Top, Height = 48 };
        var back = new Button { Text = "‹ 返回主页", Dock = DockStyle.Right, Width = 110, FlatStyle = FlatStyle.Flat };
        back.FlatAppearance.BorderSize = 0;
        back.Click += (_, _) => CloseRequested?.Invoke();
        header.Controls.Add(new Label { Text = $"全部英雄成就  ·  拥有 {ownership}", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(36,63,98) });
        header.Controls.Add(back);
        Controls.Add(_grid); Controls.Add(header);
        _grid.SuspendLayout();
        for (int i = 0; i < records.Length; i++)
        {
            var record = records[i];
            int id = (int)record["championId"]!;
            string name = FormMain.Globals.resLoading.GetChampionById(id)?.Name ?? $"英雄 {id}";
            var poster = new MasteryPoster(i + 1, null) { Dock = DockStyle.None, Size = new Size(160,270), Margin = new Padding(6) };
            poster.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 90, BackColor = Color.Transparent, ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
                Text = $"{name}\n{record["championLevel"]} 级英雄成就\n{(long)record["championPoints"]!:N0} 成就点数" });
            _grid.Controls.Add(poster);
            _pending.Add(poster, id);
        }
        _grid.ResumeLayout(true);
        _grid.Scroll += (_, _) => ScheduleImages();
        _grid.SizeChanged += (_, _) => ScheduleImages();
        VisibleChanged += (_, _) => ScheduleImages();
        _loadTimer.Tick += async (_, _) =>
        {
            _loadTimer.Stop();
            if (_loading || !Visible) return;
            _loading = true;
            try
            {
                // Only fetch visible posters, at most four at a time.
                var visible = _pending.Where(p => p.Key.Bounds.IntersectsWith(_grid.ClientRectangle)).Take(4).ToArray();
                foreach (var pair in visible) _pending.Remove(pair.Key);
                await Task.WhenAll(visible.Select(p => p.Key.LoadArtworkAsync(p.Value)));
            }
            finally { _loading = false; if (!IsDisposed) ScheduleImages(); }
        };
    }

    private void ScheduleImages()
    {
        if (!IsDisposed && Visible && _pending.Any(p => p.Key.Bounds.IntersectsWith(_grid.ClientRectangle))) _loadTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _loadTimer.Dispose(); _pending.Clear(); }
        base.Dispose(disposing);
    }
}
