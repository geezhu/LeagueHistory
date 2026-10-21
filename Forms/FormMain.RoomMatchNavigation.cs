using League.Controls;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

public partial class FormMain
{
    private Panel? _roomMatchSurface;
    private Func<JObject, JObject, Task<Panel?>>? _parseRoomMatch;
    private Func<MatchDetailView, Task>? _prepareRoomMatchDetail;

    public async Task OpenRoomMatchAsync(MatchMetadata metadata, string puuid)
    {
        if (IsDisposed || Disposing || metadata.GameData == null || metadata.GameId <= 0)
        {
            return;
        }
        _roomMatchSurface?.Dispose();
        var (surface, status) = CreateRoomMatchSurface();
        MatchListPanel? source = null;
        MatchDetailView? detail = null;
        try
        {
            var game = (JObject)metadata.GameData.DeepClone();
            var participant = (game["participants"] as JArray)
                ?.OfType<JObject>()
                .FirstOrDefault(player => player["puuid"]?.ToString() == puuid);
            if (participant == null)
            {
                status.Text = "无法确定这场战绩中的玩家。";
                return;
            }
            var parsed = await ParseRoomMatchAsync(game, participant);
            source = parsed as MatchListPanel;
            if (source == null)
            {
                parsed?.Dispose();
                if (!surface.IsDisposed)
                {
                    status.Text = "这场比赛暂时无法加载详情。";
                }
                return;
            }
            if (surface.IsDisposed)
            {
                return;
            }
            if (
                source.MatchInfo.AllParticipants == null
                || !source.MatchInfo.AllParticipants.Any(player => (int?)player["teamId"] is 100 or 200)
            )
            {
                status.Text = "这场比赛暂不支持计分板详情。";
                return;
            }
            detail = CreateRoomMatchDetail(surface, source);
            await (_prepareRoomMatchDetail?.Invoke(detail) ?? detail.PrepareAsync());
            if (surface.IsDisposed || detail.IsDisposed)
            {
                return;
            }
            status.Dispose();
            detail.Visible = true;
            // Replay and detail tabs retain the parsed match until the page closes.
            var replaySource = source;
            surface.Disposed += (_, _) => replaySource.Dispose();
            source = null;
            detail = null;
        }
        catch (Exception error)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Room match detail", error);
            if (!surface.IsDisposed)
            {
                status.Text = "加载详情失败，请返回后重试。";
            }
        }
        finally
        {
            detail?.Dispose();
            source?.Dispose();
        }
    }

    private (Panel Surface, Label Status) CreateRoomMatchSurface()
    {
        var surface = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        _roomMatchSurface = surface;
        var back = new Button
        {
            Text = "← 返回房间战绩",
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        ButtonStyles.Ghost(back);
        back.Click += (_, _) => surface.Dispose();
        var status = new Label
        {
            Text = "正在加载战绩详情…",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        surface.Controls.Add(status);
        surface.Controls.Add(back);
        penalGameMatchData.Controls.Add(surface);
        surface.BringToFront();
        // Status changes can detach the room contents while resources are loading.
        surface.ParentChanged += (_, _) =>
        {
            if (surface.Parent == null && !surface.IsDisposed)
            {
                surface.Dispose();
            }
        };
        surface.Disposed += (_, _) =>
        {
            if (ReferenceEquals(_roomMatchSurface, surface))
            {
                _roomMatchSurface = null;
            }
        };
        return (surface, status);
    }

    private async Task<Panel?> ParseRoomMatchAsync(JObject game, JObject participant)
    {
        if (_parseRoomMatch != null)
        {
            return await _parseRoomMatch(game, participant);
        }
        return await ParseGameToPanelAsync(
            game,
            participant["summonerId"]?.ToString() ?? "",
            participant["riotIdGameName"]?.ToString() ?? "",
            participant["riotIdTagline"]?.ToString() ?? "",
            0
        );
    }

    private MatchDetailView CreateRoomMatchDetail(Panel surface, MatchListPanel source)
    {
        var detail = new MatchDetailView(source.MatchInfo) { Dock = DockStyle.Fill, Visible = false };
        surface.Controls.Add(detail);
        detail.BringToFront();
        detail.PlayerProfileRequested += async id => await OpenPlayerProfileAsync(id);
        detail.ReplayRequested += source.RequestReplay;
        return detail;
    }
}
