using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League;

public partial class FormMain
{
    private Panel? _roomMatchSurface;

    public async Task OpenRoomMatchAsync(MatchMetadata metadata, string puuid)
    {
        if (metadata.GameData == null || metadata.GameId <= 0)
            return;
        _roomMatchSurface?.Dispose();
        var surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 247, 251) };
        _roomMatchSurface = surface;
        var back = new Button
        {
            Text = "← 返回房间战绩",
            Dock = DockStyle.Top,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
        };
        back.FlatAppearance.BorderSize = 0;
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
                surface.Dispose();
        };
        surface.Disposed += (_, _) =>
        {
            if (ReferenceEquals(_roomMatchSurface, surface))
                _roomMatchSurface = null;
        };
        MatchListPanel? source = null;
        MatchDetailView? detail = null;
        try
        {
            var game = (JObject)metadata.GameData.DeepClone();
            var participant = (game["participants"] as JArray)
                ?.OfType<JObject>()
                .FirstOrDefault(p => p["puuid"]?.ToString() == puuid);
            if (participant == null)
            {
                status.Text = "无法确定这场战绩中的玩家。";
                return;
            }
            var parsed = await ParseGameToPanelAsync(
                game,
                participant["summonerId"]?.ToString() ?? "",
                participant["riotIdGameName"]?.ToString() ?? "",
                participant["riotIdTagline"]?.ToString() ?? "",
                0
            );
            source = parsed as MatchListPanel;
            if (source == null)
            {
                parsed?.Dispose();
                if (!surface.IsDisposed)
                    status.Text = "这场比赛暂时无法加载详情。";
                return;
            }
            if (surface.IsDisposed)
                return;
            if (
                source.MatchInfo.AllParticipants == null
                || !source.MatchInfo.AllParticipants.Any(p => (int?)p["teamId"] is 100 or 200)
            )
            {
                status.Text = "这场比赛暂不支持计分板详情。";
                return;
            }
            detail = new MatchDetailView(source.MatchInfo) { Dock = DockStyle.Fill, Visible = false };
            surface.Controls.Add(detail);
            detail.BringToFront();
            detail.PlayerProfileRequested += async id => await OpenPlayerProfileAsync(id);
            var replaySource = source;
            detail.ReplayRequested += replaySource.RequestReplay;
            await detail.PrepareAsync();
            if (surface.IsDisposed || detail.IsDisposed)
                return;
            status.Dispose();
            detail.Visible = true;
            // Keep the parsed match alive for replay and detail tabs until the page closes.
            surface.Disposed += (_, _) => replaySource.Dispose();
            source = null;
            detail = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Room match detail: {ex}");
            if (!surface.IsDisposed)
                status.Text = "加载详情失败，请返回后重试。";
        }
        finally
        {
            detail?.Dispose();
            source?.Dispose();
        }
    }
}
