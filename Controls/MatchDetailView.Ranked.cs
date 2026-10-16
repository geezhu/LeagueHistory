using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchDetailView
{
    private readonly CancellationTokenSource _requestCancellation = new();
    private readonly List<ToolStripDropDown> _banPopups = new();
    private static readonly SemaphoreSlim RankGate = new(3);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (DateTime Time, RankedStats Stats)
    > RankCache = new();

    private readonly HashSet<Task> _requestWork = new();
    private bool _requestsRetired;

    private void TrackRequest(Task task)
    {
        lock (_requestWork)
        {
            _requestWork.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_requestWork)
                {
                    _requestWork.Remove(completed);
                }
                if (completed.Exception is { } error)
                {
                    System.Diagnostics.Debug.WriteLine($"Detail request: {error.GetBaseException().GetType().Name}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private void RetireRequests()
    {
        Task[] pending;
        lock (_requestWork)
        {
            if (_requestsRetired)
            {
                return;
            }
            _requestsRetired = true;
            pending = _requestWork.ToArray();
        }
        if (_preparation is { } preparation)
        {
            pending = pending.Append(preparation).ToArray();
        }
        // Cancel immediately, but keep the source alive until callbacks and all current requests finish.
        var cancellation = _requestCancellation.CancelAsync();
        _ = Task.WhenAll(pending.Append(cancellation))
            .ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                    _requestCancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
    }

    private void AddPlayerRank(Panel row, JObject participant)
    {
        if (IsDisposed || _requestCancellation.IsCancellationRequested || _match.QueueId.Replace("q_", "") != "420")
        {
            return;
        }
        string? puuid = participant["puuid"]?.ToString();
        if (string.IsNullOrEmpty(puuid))
        {
            return;
        }
        var emblem = new RankEmblem { Name = "PlayerRankEmblem", Bounds = new Rectangle(206, 5, 30, 28) };
        var name = row.Controls.OfType<LinkLabel>().FirstOrDefault();
        if (name != null)
        {
            name.Width = Math.Min(name.Width, 144);
        }

        row.Controls.Add(emblem);
        _toolTip.SetToolTip(emblem, "正在获取当前单双排段位…");
        TrackRequest(LoadPlayerRankAsync(emblem, puuid, _requestCancellation.Token));
    }

    private async Task LoadPlayerRankAsync(RankEmblem emblem, string puuid, CancellationToken token)
    {
        try
        {
            var rank = await ReadCurrentRankAsync(puuid, token);
            if (IsDisposed || emblem.IsDisposed || token.IsCancellationRequested)
            {
                return;
            }

            if (rank != null)
            {
                emblem.SetTier(rank.Tier);
            }

            _toolTip.SetToolTip(
                emblem,
                rank == null ? "当前单双排段位暂不可用" : $"当前单双排：{rank.FormattedTier}\n不是本场比赛发生时的段位"
            );
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Closing the detail view cancels its optional rank lookup.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Player rank", ex);
        }
    }

    private static async Task<RankedStats?> ReadCurrentRankAsync(string puuid, CancellationToken token)
    {
        await RankGate.WaitAsync(token);
        RankedStats? rank = null;
        try
        {
            if (RankCache.TryGetValue(puuid, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5))
            {
                rank = cached.Stats;
            }
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
                    {
                        RankCache.Clear();
                    }

                    RankCache[puuid] = (DateTime.UtcNow, rank);
                }
            }
        }
        finally
        {
            RankGate.Release();
        }
        return rank;
    }

    private void AddTeamBans(Panel section, int teamId)
    {
        var teams = _match.RawGameData?["teams"] as JArray;
        var team = teams?.OfType<JObject>().FirstOrDefault(t => (int?)t["teamId"] == teamId);
        if (team?["bans"] is not JArray bans || bans.Count == 0)
        {
            return;
        }
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
        var token = _requestCancellation.Token;
        button.Click += (_, _) =>
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                TrackRequest(ShowTeamBansAsync(button, bans, token));
            }
        };
    }

    private (FlowLayoutPanel Content, List<(PictureBox Picture, int Id)> Slots) CreateBanSlots(JArray bans)
    {
        var content = new FlowLayoutPanel
        {
            AutoSize = true,
            Padding = new Padding(8),
            BackColor = Color.White,
            WrapContents = false,
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
        return (content, slots);
    }

    private ToolStripDropDown CreateBanPopup(FlowLayoutPanel content)
    {
        var popup = new ToolStripDropDown { Padding = Padding.Empty };
        popup.Items.Add(new ToolStripControlHost(content) { Margin = Padding.Empty, Padding = Padding.Empty });
        _banPopups.Add(popup);
        popup.Closed += (_, _) =>
        {
            // ToolStripDropDown still touches its handle after raising Closed.
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((Action)(() => DisposeBanPopup(popup, content)));
            }
        };
        return popup;
    }

    private void DisposeBanPopup(ToolStripDropDown popup, FlowLayoutPanel content)
    {
        if (!popup.IsDisposed)
        {
            DisposeImagesInControls(content.Controls);
            popup.Dispose();
        }
        _banPopups.Remove(popup);
    }

    private async Task ShowTeamBansAsync(Button button, JArray bans, CancellationToken token)
    {
        var (content, slots) = CreateBanSlots(bans);
        var popup = CreateBanPopup(content);
        popup.Show(button, new Point(0, button.Height));
        foreach (var (picture, id) in slots)
        {
            if (id <= 0 || popup.IsDisposed || IsDisposed || token.IsCancellationRequested)
            {
                continue;
            }

            try
            {
                var image = await FormMain.Globals.resLoading.GetChampionIconAsync(id);
                if (popup.IsDisposed || IsDisposed || token.IsCancellationRequested)
                {
                    image?.Dispose();
                    break;
                }
                picture.Image = image;
                _toolTip.SetToolTip(picture, FormMain.Globals.resLoading.GetChampionById(id)?.Name ?? $"英雄 {id}");
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Ui, "Ban icon", ex);
            }
        }
    }
}
