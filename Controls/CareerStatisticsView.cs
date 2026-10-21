using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class CareerStatisticsView : UserControl
{
    private readonly PlayerStatisticsService _service;
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 48,
        ForeColor = Theme.Muted,
    };
    private readonly Label _summary = new()
    {
        Dock = DockStyle.Top,
        Height = 66,
        Font = new Font("Microsoft YaHei UI", 11f),
    };
    private readonly ListView _modes = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.None,
    };
    private readonly Button _retry = new()
    {
        Text = "刷新统计",
        Dock = DockStyle.Right,
        Width = 84,
    };
    private CancellationTokenSource? _request;
    private Task? _requestWork;
    private string _puuid = "";
    private readonly Func<string, CancellationToken, Task<JArray?>>? _historyLoader;

    public CareerStatisticsView(
        PlayerStatisticsService? service = null,
        Func<string, CancellationToken, Task<JArray?>>? historyLoader = null
    )
    {
        _service = service ?? new(() => FormMain.Globals.lcuClient.Client);
        _historyLoader = historyLoader;
        Height = 320;
        Padding = new Padding(14);
        BackColor = Color.White;
        ForeColor = Theme.Heading;
        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(
            new Label
            {
                Text = "生涯与历史统计",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            }
        );
        header.Controls.Add(_retry);
        ButtonStyles.Secondary(_retry);
        _retry.Click += (_, _) => LoadPlayer(_puuid);
        foreach (var (title, width) in new[] { ("比赛模式", 150), ("有效场次", 90), ("胜率", 90), ("综合 KDA", 100) })
        {
            _modes.Columns.Add(title, width);
        }

        Controls.Add(_modes);
        Controls.Add(_summary);
        Controls.Add(_status);
        Controls.Add(header);
        _status.Text = "连接客户端后加载统计。";
    }

    public void LoadPlayer(string puuid)
    {
        if (string.IsNullOrWhiteSpace(puuid) || IsDisposed)
        {
            return;
        }
        _puuid = puuid;
        CancelRequest();
        var request = _request = new CancellationTokenSource();
        _retry.Enabled = false;
        _status.Text = "正在读取生涯统计…";
        _summary.Text = "";
        _modes.Items.Clear();
        _requestWork = LoadStatisticsAsync(puuid, request.Token);
    }

    private async Task LoadStatisticsAsync(string puuid, CancellationToken token)
    {
        try
        {
            var (records, history, message) = await ReadRecordsAsync(puuid, token);
            if (IsDisposed || token.IsCancellationRequested)
            {
                return;
            }
            if (records == null)
            {
                _status.Text = message.Length > 0 ? message : "接口未返回可识别的统计记录，可以稍后重试。";
                return;
            }
            ShowStatistics(records, puuid, history);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Switching players or closing the view retires the previous request.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "统计解析失败", ex);
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _status.Text = "统计数据格式暂不支持，未使用不完整数据计算。";
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

    private async Task<(JArray? Records, bool History, string Message)> ReadRecordsAsync(
        string puuid,
        CancellationToken token
    )
    {
        var response = await _service.GetAsync(
            "/lol-career-stats/v1/summoner-games/" + Uri.EscapeDataString(puuid),
            token
        );
        bool history = false;
        var records = CareerStatistics.Records(response.Data);
        if (records == null && response.Status is not (0 or 401 or 403))
        {
            history = true;
            response = await _service.GetAsync(
                $"/lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex=0&endIndex=99",
                token
            );
            records = CareerStatistics.Records(response.Data);
            if (records == null && response.Status is not (401 or 403) && _historyLoader != null)
            {
                records = await _historyLoader(puuid, token);
            }
        }
        return (records, history, response.Message);
    }

    private void ShowStatistics(JArray records, string puuid, bool history)
    {
        var stats = CareerStatistics.Parse(records, puuid);
        _status.Text = history
            ? $"生涯接口未提供可用记录；以下为最近 {records.Count} 条历史记录（最多 100 条），不代表完整生涯。"
            : $"生涯接口返回 {records.Count} 条记录；统计仅覆盖接口返回范围，不代表终身总计。";
        _status.Text += $"\n已排除 {stats.ExcludedRemakes} 场重开，{stats.Unrecognized} 条数据不完整。";
        if (stats.Games.Count == 0)
        {
            _summary.Text = "暂无可统计的比赛";
            return;
        }
        string kda = stats.Kda?.ToString("0.00") ?? "—";
        string Maximum(Func<CareerGame, long?> value) =>
            stats.Games.All(g => value(g).HasValue) ? stats.Games.Max(g => value(g))!.Value.ToString("N0") : "—";
        string pentas = stats.Games.All(g => g.Penta.HasValue) ? stats.Games.Sum(g => g.Penta!.Value).ToString() : "—";
        _summary.Text =
            $"{stats.Games.Count} 场  ·  {stats.Wins} 胜 {stats.Games.Count - stats.Wins} 负  ·  胜率 {100d * stats.Wins / stats.Games.Count:0.#}%  ·  KDA {kda}\n范围内最高伤害 {Maximum(g => g.Damage)}  ·  最高经济 {Maximum(g => g.Gold)}  ·  五杀 {pentas} 次";
        ShowModes(stats);
    }

    private void ShowModes(CareerStatistics stats)
    {
        foreach (var mode in stats.Games.GroupBy(g => g.Queue).OrderByDescending(g => g.Count()))
        {
            double? modeKda = mode.All(g => g.Kills.HasValue && g.Deaths.HasValue && g.Assists.HasValue)
                ? mode.Sum(g => (long)g.Kills!.Value + g.Assists!.Value)
                    / (double)Math.Max(1, mode.Sum(g => (long)g.Deaths!.Value))
                : null;
            _modes.Items.Add(
                new ListViewItem(
                    new[]
                    {
                        CareerStatistics.QueueName(mode.Key),
                        mode.Count().ToString(),
                        $"{100d * mode.Count(g => g.Win) / mode.Count():0.#}%",
                        modeKda?.ToString("0.00") ?? "—",
                    }
                )
            );
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelRequest();
        }
        base.Dispose(disposing);
    }
}
