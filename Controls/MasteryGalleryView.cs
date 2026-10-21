using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class MasteryGalleryView : UserControl
{
    private readonly FlowLayoutPanel _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(12),
        BackColor = Theme.Canvas,
    };
    private readonly Dictionary<MasteryPoster, int> _pending = new();
    private readonly System.Windows.Forms.Timer _loadTimer = new() { Interval = 100 };
    private bool _loading;
    private bool _closing;
    private readonly Font _headingFont = new("Microsoft YaHei UI", 12f, FontStyle.Bold);
    private readonly Font _posterFont = new("Microsoft YaHei UI", 10f, FontStyle.Bold);
    public event Action? CloseRequested;

    public MasteryGalleryView(JObject[] records, string ownership = "— / —")
    {
        BackColor = Color.White;
        Padding = new Padding(16);
        Controls.Add(_grid);
        Controls.Add(CreateHeader(ownership));
        PopulatePosters(records);
        _grid.Scroll += (_, _) => ScheduleImages();
        _grid.SizeChanged += (_, _) => ScheduleImages();
        VisibleChanged += (_, _) => ScheduleImages();
        _loadTimer.Tick += async (_, _) => await LoadVisiblePostersAsync();
    }

    private Panel CreateHeader(string ownership)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 48 };
        var back = new Button
        {
            Text = "‹ 返回主页",
            Dock = DockStyle.Right,
            Width = 110,
        };
        ButtonStyles.Ghost(back);
        back.Click += (_, _) => CloseRequested?.Invoke();
        header.Controls.Add(
            new Label
            {
                Text = $"全部英雄成就  ·  {ownership}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = _headingFont,
                ForeColor = Theme.Heading,
            }
        );
        header.Controls.Add(back);
        return header;
    }

    private void PopulatePosters(JObject[] records)
    {
        _grid.SuspendLayout();
        try
        {
            for (int i = 0; i < records.Length; i++)
            {
                AddPoster(records[i], i + 1);
            }
        }
        finally
        {
            _grid.ResumeLayout(true);
        }
    }

    private void AddPoster(JObject record, int rank)
    {
        int id = (int)record["championId"]!;
        string name = FormMain.Globals.resLoading.GetChampionById(id)?.Name ?? $"英雄 {id}";
        var poster = new MasteryPoster(rank, null)
        {
            Dock = DockStyle.None,
            Size = new Size(160, 270),
            Margin = new Padding(6),
        };
        poster.Controls.Add(
            new Label
            {
                Dock = DockStyle.Bottom,
                Height = 90,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = _posterFont,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"{name}\n{record["championLevel"]} 级英雄成就\n{(long)record["championPoints"]!:N0} 成就点数",
            }
        );
        _grid.Controls.Add(poster);
        _pending.Add(poster, id);
    }

    private async Task LoadVisiblePostersAsync()
    {
        if (_closing)
        {
            return;
        }
        _loadTimer.Stop();
        if (_loading || !Visible)
        {
            return;
        }
        _loading = true;
        try
        {
            // Only fetch visible posters, at most four at a time.
            var visible = _pending.Where(p => p.Key.Bounds.IntersectsWith(_grid.ClientRectangle)).Take(4).ToArray();
            foreach (var pair in visible)
            {
                _pending.Remove(pair.Key);
            }
            // Posters own the returned image copy and discard it themselves if they close during loading.
            await Task.WhenAll(visible.Select(p => p.Key.LoadArtworkAsync(p.Value)));
        }
        finally
        {
            _loading = false;
            ScheduleImages();
        }
    }

    private void ScheduleImages()
    {
        if (!_closing && Visible && _pending.Any(p => p.Key.Bounds.IntersectsWith(_grid.ClientRectangle)))
        {
            _loadTimer.Start();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _loadTimer.Stop();
            _loadTimer.Dispose();
            _pending.Clear();
            _headingFont.Dispose();
            _posterFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
