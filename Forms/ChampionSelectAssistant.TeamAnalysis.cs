using League.Models;
using League.Services;

namespace League;

/// <summary>
/// The team analysis conclusion on the main panel: gaps, damage mix and a helpful swap, with a send action.
/// The full analysis stays in the roster window, which the line opens.
/// </summary>
internal sealed partial class ChampionSelectAssistant
{
    private readonly Panel _analysisBar = new() { Dock = DockStyle.Top, Visible = false };
    private readonly Label _analysisText = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Cursor = Cursors.Hand,
        ForeColor = Color.FromArgb(208, 185, 130),
    };
    private readonly Button _sendAnalysis = new()
    {
        Text = "发送分析",
        Dock = DockStyle.Right,
        Enabled = false,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
    };
    private AramMetaIndex? _analysisIndex;
    private CancellationTokenSource? _analysisLoad;
    private TeamAnalysis? _analysis;
    private string _analysisSignature = "";
    private bool _sendingAnalysis;

    private Panel CreateTeamAnalysisBar()
    {
        _sendAnalysis.ForeColor = ActionText;
        _sendAnalysis.BackColor = Color.Transparent;
        _sendAnalysis.FlatAppearance.BorderSize = 0;
        _sendAnalysis.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 45, 57);
        FitButton(_sendAnalysis, 24);
        // Two lines fit the conclusion and a swap at the panel's width; the tooltip keeps the full message.
        _analysisBar.Height = Math.Max(_sendAnalysis.Height, Dip(40));
        _analysisBar.Padding = DipPadding(10, 0, 6, 0);
        _analysisText.Click += (_, _) => ShowRoster();
        _sendAnalysis.Click += async (_, _) => await TrackAssistantWork(SendTeamAnalysisAsync());
        _tips.SetToolTip(_sendAnalysis, "把估计胜率、短板和换人建议发送到选人聊天。");
        _analysisBar.Controls.Add(_analysisText);
        _analysisBar.Controls.Add(_sendAnalysis);
        return _analysisBar;
    }

    private bool AnalysisMayhem => _queue == 2400 || SelectedMode == "mayhem";

    /// <summary>Recomputes when the roster or mode changes; the statistics load once per champion select.</summary>
    private void RefreshTeamAnalysis()
    {
        if (_roster == null)
        {
            HideTeamAnalysis();
            return;
        }

        ShowAnalysisBar(true);
        if (_analysisIndex == null)
        {
            _analysisText.Text = "阵容分析读取中…";
            _sendAnalysis.Enabled = false;
            // Statistics load only during a live champion select, never for a roster set up elsewhere.
            if (_analysisLoad == null && _selecting)
            {
                TrackAssistantWork(LoadTeamAnalysisAsync());
            }
            return;
        }

        string signature = _roster.Signature + "|" + AnalysisMayhem;
        if (signature != _analysisSignature)
        {
            _analysisSignature = signature;
            _analysis = TeamAnalysisChatMessage.Analyze(_analysisIndex, _roster, AnalysisMayhem, ChampionName);
            _analysisText.Text = _analysis.Brief;
            _tips.SetToolTip(_analysisText, _analysis.Message + "\n点击查看完整阵容分析。");
        }
        _sendAnalysis.Enabled = _selecting && !_sendingAnalysis;
    }

    private static string ChampionName(int id) =>
        FormMain.Globals.resLoading?.GetChampionById(id)?.Name ?? $"英雄 {id}";

    private async Task LoadTeamAnalysisAsync()
    {
        var load = _analysisLoad = new CancellationTokenSource();
        try
        {
            // Reuses the cached arammeta statistics, like the roster window; no separate download.
            var index = (await _am.GetAsync(0, false, load.Token)).Index;
            if (!IsDisposed && ReferenceEquals(load, _analysisLoad))
            {
                _analysisIndex = index;
                RefreshTeamAnalysis();
            }
        }
        catch (OperationCanceledException)
        {
            // Champion select ended or the assistant closed; nothing is shown after cancellation.
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException)
        {
            if (!IsDisposed && ReferenceEquals(load, _analysisLoad))
            {
                _analysisText.Text = "arammeta 统计暂不可用 · 点击查看阵容";
            }
        }
        finally
        {
            if (ReferenceEquals(load, _analysisLoad))
            {
                _analysisLoad = null;
            }
            load.Dispose();
        }
    }

    private void HideTeamAnalysis()
    {
        _analysisLoad?.Cancel();
        _analysisLoad = null;
        _analysisIndex = null;
        _analysis = null;
        _analysisSignature = "";
        ShowAnalysisBar(false);
    }

    private void ShowAnalysisBar(bool visible)
    {
        if (_analysisBar.Visible != visible)
        {
            _analysisBar.Visible = visible;
            QueueFitToContent();
        }
    }

    private async Task SendTeamAnalysisAsync(HttpClient? client = null)
    {
        var analysis = _analysis;
        string signature = _analysisSignature;
        if (_sendingAnalysis || !_selecting || analysis == null)
        {
            return;
        }

        bool Current() => !IsDisposed && _selecting && signature == _analysisSignature;
        _sendingAnalysis = true;
        _sendAnalysis.Enabled = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            client ??= FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接。");
            bool sent = await AnnounceRunesAsync(client, analysis.Message, timeout.Token, Current, deduplicate: false);
            if (Current())
            {
                _status.Text = sent ? "阵容分析已发送到选人聊天。" : "未发送：阵容已变化或已离开选人阶段。";
            }
        }
        catch (OperationCanceledException)
        {
            if (Current())
            {
                _status.Text = "阵容分析发送超时，请重试。";
            }
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            if (Current())
            {
                _status.Text = "阵容分析发送失败，请检查客户端连接。";
            }
        }
        finally
        {
            _sendingAnalysis = false;
            if (!IsDisposed)
            {
                _sendAnalysis.Enabled = _selecting && _analysis != null;
            }
        }
    }
}
