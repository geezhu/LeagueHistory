using League.Clients;
using League.Extensions;
using League.Models;

namespace League.Services;

/// <summary>
/// Opens the end-of-game panel once per game, as the game ends, and closes it when the next queue starts. The stats
/// block may not exist yet while the client waits for stats, so it is asked for once a second for up to a minute; a
/// newer request or a close cancels an older one, and a cancelled request never opens a panel.
/// </summary>
internal sealed class EndOfGamePanelLauncher
{
    private const int Attempts = 60;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly Control _owner;
    private readonly EndOfGameSummaryService _service;
    private readonly ChampionMasteryService _mastery;
    private readonly Func<int, Task<Image?>> _championIcon;
    private CancellationTokenSource? _request;
    private EndOfGamePanelForm? _panel;
    private long _shownGameId;

    public EndOfGamePanelLauncher(
        Control owner,
        EndOfGameSummaryService service,
        ChampionMasteryService mastery,
        Func<int, Task<Image?>> championIcon
    )
    {
        _owner = owner;
        _service = service;
        _mastery = mastery;
        _championIcon = championIcon;
    }

    public async Task ShowAsync(int queueId, string? puuid)
    {
        var request = new CancellationTokenSource();
        // Not disposed: a superseded request may still be awaiting its token, and cancelling is all it needs.
        if (Interlocked.Exchange(ref _request, request) is { } superseded)
        {
            await superseded.CancelAsync();
        }

        try
        {
            var summary = await FetchAsync(queueId, request.Token);
            if (summary == null)
            {
                AppLog.Shared.Info(LogCategory.Game, "结算面板：未取得结算数据，不显示");
                return;
            }

            _owner.InvokeIfRequired(() => Present(summary, puuid, request.Token));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Superseded or closed before the stats arrived.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Game, "结算面板：读取结算数据失败", ex);
        }
    }

    public void Close()
    {
        Interlocked.Exchange(ref _request, null)?.Cancel();
        _owner.InvokeIfRequired(() => _panel?.Close());
    }

    private async Task<EndOfGameSummary?> FetchAsync(int queueId, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            var summary = await _service.GetAsync(queueId, token);
            if (summary != null || attempt == Attempts)
            {
                // Temporary: how long after the game ends the stats block appears; remove once known.
                AppLog.Shared.Info(
                    LogCategory.Game,
                    $"结算面板：第 {attempt} 次读取结算数据，{(summary != null ? "已取得" : "仍未取得")}"
                );
                return summary;
            }

            await Task.Delay(RetryDelay, token);
        }
    }

    private void Present(EndOfGameSummary summary, string? puuid, CancellationToken token)
    {
        // The phase can pass through the end-of-game screen twice; one game gets one panel.
        if (token.IsCancellationRequested || (summary.GameId != 0 && summary.GameId == _shownGameId))
        {
            return;
        }

        _shownGameId = summary.GameId;
        _panel?.Close();
        var panel = new EndOfGamePanelForm(new EndOfGamePanelState(summary), _service.SendAsync, _championIcon);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(_owner);
        // Bottom left, resting on the taskbar: WeGame's own end-of-game panel takes the bottom right corner.
        panel.Location = new Point(area.Left + 24, area.Bottom - panel.Height);
        // A modeless form disposes itself when closed.
        panel.FormClosed += (_, _) =>
        {
            if (_panel == panel)
            {
                _panel = null;
            }
        };
        _panel = panel;
        panel.Show();
        AppLog.Shared.Info(
            LogCategory.Game,
            $"结算面板已显示：{(summary.Win ? "胜" : "负")}，队友 {summary.Teammates.Count} 人"
        );
        _ = LoadMasteryAsync(panel, summary.ChampionId, puuid, token);
    }

    private async Task LoadMasteryAsync(
        EndOfGamePanelForm panel,
        int championId,
        string? puuid,
        CancellationToken token
    )
    {
        if (string.IsNullOrEmpty(puuid))
        {
            return;
        }

        try
        {
            var page = await _mastery.GetAsync(puuid, token, refresh: true);
            if (!token.IsCancellationRequested && !panel.IsDisposed)
            {
                panel.ShowMastery(EndOfGamePanelState.MasteryText(page.ForChampion(championId)));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The next queue started; the panel is closing.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Game, "结算面板：读取英雄熟练度失败", ex);
        }
    }
}
