using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchDetailView
{
    private readonly CancellationTokenSource _rankRequests = new();
    private readonly List<ToolStripDropDown> _banPopups = new();
    private static readonly SemaphoreSlim RankGate = new(3);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (DateTime Time, RankedStats Stats)
    > RankCache = new();

    private async void AddPlayerRank(Panel row, JObject participant)
    {
        if (_match.QueueId.Replace("q_", "") != "420")
            return;
        string? puuid = participant["puuid"]?.ToString();
        if (string.IsNullOrEmpty(puuid))
            return;
        var emblem = new RankEmblem { Name = "PlayerRankEmblem", Bounds = new Rectangle(206, 5, 30, 28) };
        var name = row.Controls.OfType<LinkLabel>().FirstOrDefault();
        if (name != null)
            name.Width = Math.Min(name.Width, 144);
        row.Controls.Add(emblem);
        _toolTip.SetToolTip(emblem, "正在获取当前单双排段位…");
        var token = _rankRequests.Token;
        try
        {
            await RankGate.WaitAsync(token);
            RankedStats? rank = null;
            try
            {
                if (
                    RankCache.TryGetValue(puuid, out var cached)
                    && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5)
                )
                    rank = cached.Stats;
                else
                {
                    var service = new PlayerStatisticsService(() => FormMain.Globals.lcuClient.Client);
                    var response = await service.GetAsync(
                        "/lol-ranked/v1/ranked-stats/" + Uri.EscapeDataString(puuid),
                        token
                    );
                    if (response.Data is JObject data && data["queueMap"]?["RANKED_SOLO_5x5"] is JObject)
                    {
                        rank = RankedStats.FromJson(data)["单双排"];
                        if (RankCache.Count >= 256)
                            RankCache.Clear();
                        RankCache[puuid] = (DateTime.UtcNow, rank);
                    }
                }
            }
            finally
            {
                RankGate.Release();
            }
            if (IsDisposed || emblem.IsDisposed || token.IsCancellationRequested)
                return;
            if (rank != null)
                emblem.SetTier(rank.Tier);
            _toolTip.SetToolTip(
                emblem,
                rank == null ? "当前单双排段位暂不可用" : $"当前单双排：{rank.FormattedTier}\n不是本场比赛发生时的段位"
            );
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Player rank: {ex.GetType().Name}");
        }
    }

    private void AddTeamBans(Panel section, int teamId)
    {
        var teams = _match.RawGameData?["teams"] as JArray;
        var team = teams?.OfType<JObject>().FirstOrDefault(t => (int?)t["teamId"] == teamId);
        if (team?["bans"] is not JArray bans || bans.Count == 0)
            return;
        var button = new Button
        {
            Name = "TeamBans",
            Text = "BAN",
            Bounds = new Rectangle(1038, 2, 64, 26),
            FlatStyle = FlatStyle.Flat,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(215, 223, 233);
        section.Controls.Add(button);
        button.BringToFront();
        button.Click += async (_, _) =>
        {
            var content = new FlowLayoutPanel
            {
                AutoSize = true,
                Padding = new Padding(8),
                BackColor = Color.White,
                WrapContents = false,
            };
            var popup = new ToolStripDropDown { Padding = Padding.Empty };
            popup.Items.Add(new ToolStripControlHost(content) { Margin = Padding.Empty, Padding = Padding.Empty });
            _banPopups.Add(popup);
            popup.Closed += (_, _) =>
            {
                // ToolStripDropDown still touches its handle after raising Closed.
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(
                        (Action)(
                            () =>
                            {
                                if (!popup.IsDisposed)
                                {
                                    DisposeImagesInControls(content.Controls);
                                    popup.Dispose();
                                }
                                _banPopups.Remove(popup);
                            }
                        )
                    );
            };
            var slots = new List<(PictureBox Picture, int Id)>();
            foreach (var ban in bans.OfType<JObject>().OrderBy(b => (int?)b["pickTurn"] ?? int.MaxValue))
            {
                int id = (int?)ban["championId"] ?? -1;
                var picture = new PictureBox
                {
                    Width = 34,
                    Height = 34,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(232, 241, 253),
                };
                _toolTip.SetToolTip(picture, id > 0 ? $"英雄 {id}" : "未禁用英雄");
                content.Controls.Add(picture);
                slots.Add((picture, id));
            }
            popup.Show(button, new Point(0, button.Height));
            foreach (var (picture, id) in slots)
            {
                if (id <= 0 || popup.IsDisposed || IsDisposed)
                    continue;
                try
                {
                    var image = await FormMain.Globals.resLoading.GetChampionIconAsync(id);
                    if (popup.IsDisposed || IsDisposed)
                    {
                        image?.Dispose();
                        break;
                    }
                    picture.Image = image;
                    _toolTip.SetToolTip(picture, FormMain.Globals.resLoading.GetChampionById(id)?.Name ?? $"英雄 {id}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ban icon: {ex.GetType().Name}");
                }
            }
        };
    }
}
