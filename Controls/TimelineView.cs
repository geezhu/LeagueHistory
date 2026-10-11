using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class TimelineView : UserControl
{
    private readonly MatchInfo _match;
    private readonly PlayerStatisticsService _service;
    private readonly TimelineGoldChart _chart = new() { Dock = DockStyle.Top, Height = 218 };
    private readonly TrackBar _time = new()
    {
        Dock = DockStyle.Top,
        Height = 42,
        TickStyle = TickStyle.None,
        Enabled = false,
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 44,
        Padding = new Padding(8, 4, 0, 0),
    };
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ListView _events = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.None,
    };
    private readonly Button _retry = new()
    {
        Text = "重新加载",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
    };
    private CancellationTokenSource? _request;
    private Task? _requestWork;
    private MatchTimeline? _data;
    private bool _started;
    private readonly TimelineKillMap _map;

    public TimelineView(MatchInfo match, PlayerStatisticsService? service = null)
    {
        _match = match;
        _service = service ?? new(() => FormMain.Globals.lcuClient.Client);
        BackColor = Color.White;
        ForeColor = Color.FromArgb(36, 63, 98);
        var raw = match.RawGameData;
        raw = raw?["json"] as JObject ?? raw;
        raw = raw?["info"] as JObject ?? raw;
        int mapId =
            (int?)raw?["mapId"]
            ?? (
                match.QueueId.Replace("q_", "") switch
                {
                    "420" or "440" or "430" or "400" => 11,
                    "450" or "2400" => 12,
                    _ => 0,
                }
            );
        _map = new TimelineKillMap(mapId) { Dock = DockStyle.Right, Width = 260 };
        var visual = new Panel { Dock = DockStyle.Top, Height = 270 };
        _chart.Dock = DockStyle.Fill;
        visual.Controls.Add(_chart);
        visual.Controls.Add(_map);
        _filter.Items.AddRange(new[] { "全部事件", "击杀", "地图目标" });
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ShowEvents();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        toolbar.Controls.AddRange(new Control[] { _filter, _retry });
        _retry.Click += (_, _) => LoadTimeline();
        foreach (var (title, width) in new[] { ("时间", 65), ("玩家", 150), ("事件", 260), ("位置坐标", 110) })
        {
            _events.Columns.Add(title, width);
        }

        _events.SelectedIndexChanged += (_, _) =>
        {
            if (_data == null || _events.SelectedItems.Count == 0 || _events.SelectedItems[0].Tag is not long stamp)
            {
                return;
            }

            int index = _data.Frames.FindLastIndex(f => f.Timestamp <= stamp);
            if (index >= 0)
            {
                _time.Value = Math.Min(index, _time.Maximum);
            }
        };
        _time.ValueChanged += (_, _) => UpdateFrame();
        Controls.Add(_events);
        Controls.Add(toolbar);
        Controls.Add(_time);
        Controls.Add(visual);
        Controls.Add(_status);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!_started)
        {
            _started = true;
            LoadTimeline();
        }
    }

    private void LoadTimeline()
    {
        if (IsDisposed)
        {
            return;
        }
        CancelRequest();
        var request = _request = new CancellationTokenSource();
        _retry.Enabled = false;
        _time.Enabled = false;
        _events.Items.Clear();
        _data = null;
        _chart.Data = null;
        _chart.Invalidate();
        _status.Text = "正在加载时间线…";
        _map.Data = null;
        _map.Invalidate();
        _requestWork = LoadTimelineAsync(request.Token);
    }

    private async Task LoadTimelineAsync(CancellationToken token)
    {
        try
        {
            var response = await _service.GetAsync($"/lol-match-history/v1/game-timelines/{_match.GameId}", token);
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                ShowTimeline(response);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A retry or closing the view retires the previous request.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"时间线解析失败: {ex.GetType().Name}");
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _status.Text = "时间线格式暂不支持，请重试。";
            }
        }
        finally
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _retry.Enabled = true;
            }
        }
    }

    private void ShowTimeline(StatisticsResponse response)
    {
        if (response.Data == null)
        {
            _status.Text = response.Message;
            return;
        }
        if (
            response.Data is Newtonsoft.Json.Linq.JObject root
            && root["gameId"] != null
            && root["gameId"]!.Value<long>() != _match.GameId
        )
        {
            _status.Text = "返回的时间线与当前比赛不匹配。";
            return;
        }
        _data = MatchTimeline.Parse(response.Data, _match.AllParticipants);
        if (_data.Frames.Count == 0)
        {
            _status.Text = "这场比赛暂无时间线数据；仍可查看基础计分板。";
            return;
        }
        _chart.Data = _data;
        _map.Data = _data;
        _time.Maximum = _data.Frames.Count - 1;
        _time.Value = _time.Maximum;
        _time.Enabled = true;
        ShowEvents();
        UpdateFrame();
    }

    private void CancelRequest()
    {
        var request = _request;
        var work = _requestWork;
        _request = null;
        _requestWork = null;
        if (request == null)
        {
            return;
        }
        // The retired worker and cancellation callbacks may still use this source.
        var cancellation = request.CancelAsync();
        _ = Task.WhenAll(work ?? Task.CompletedTask, cancellation)
            .ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                    request.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
    }

    private void UpdateFrame()
    {
        if (_data == null || _data.Frames.Count == 0)
        {
            return;
        }

        var frame = _data.Frames[_time.Value];
        _chart.Selected = _time.Value;
        _chart.Invalidate();
        _map.Until = frame.Timestamp;
        _map.Invalidate();
        string Gold(long? value) => value.HasValue ? $"{value.Value:N0}" : "数据缺失";
        string difference = _data.GoldDifference(frame) is double value ? value.ToString("+0;-0;0") : "数据缺失";
        _status.Text =
            $"{FormatTime(frame.Timestamp)} · 人均经济差 {difference} · 蓝队总经济 {Gold(frame.Blue)} · 红队总经济 {Gold(frame.Red)}\n蓝队人均 − 红队人均；拖动滑块查看累计击杀点，悬停查看击杀信息。";
    }

    private void ShowEvents()
    {
        _events.BeginUpdate();
        _events.Items.Clear();
        if (_data != null)
        {
            foreach (
                var ev in _data.Events.Where(e =>
                    _filter.SelectedIndex == 0
                    || (_filter.SelectedIndex == 1 ? e.Kind == "CHAMPION_KILL" : e.Kind != "CHAMPION_KILL")
                )
            )
            {
                _events.Items.Add(
                    new ListViewItem(
                        new[]
                        {
                            FormatTime(ev.Timestamp),
                            ev.Actor,
                            ev.Description,
                            ev.X.HasValue && ev.Y.HasValue ? $"{ev.X}, {ev.Y}" : "—",
                        }
                    )
                    {
                        Tag = ev.Timestamp,
                    }
                );
            }
        }

        _events.EndUpdate();
    }

    public static string FormatTime(long milliseconds) => $"{milliseconds / 60000:00}:{milliseconds / 1000 % 60:00}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelRequest();
        }
        base.Dispose(disposing);
    }
}
