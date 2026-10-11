namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private void AddBalanceHint(FlowLayoutPanel row, int hero, CancellationToken token)
    {
        var hint = new Label
        {
            Text = "?",
            AccessibleName = "普通大乱斗平衡调整",
            Size = new Size(22, 24),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(208, 185, 130),
            Cursor = Cursors.Help,
            Margin = new Padding(0, 0, 5, 0),
        };
        row.Controls.Add(hint);
        row.Controls.SetChildIndex(hint, 1);
        var lookup = new BalanceHintLookup();
        _tips.SetToolTip(hint, "正在读取平衡数据…");
        hint.MouseEnter += async (_, _) => await LoadBalanceHintAsync(hint, hero, lookup, token);
        hint.Click += async (_, _) => await LoadBalanceHintAsync(hint, hero, lookup, token);
        _ = LoadBalanceHintAsync(hint, hero, lookup, token);
    }

    private sealed class BalanceHintLookup
    {
        public bool Busy { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.MinValue;
        public int CacheMinutes { get; set; }
    }

    private async Task LoadBalanceHintAsync(Label hint, int hero, BalanceHintLookup lookup, CancellationToken token)
    {
        if (lookup.Busy || token.IsCancellationRequested || hint.IsDisposed)
        {
            return;
        }
        int minutes = Math.Clamp(_owner.GetAppConfig()?.AramBalanceCacheMinutes ?? 1440, 1, 10080);
        if (lookup.CacheMinutes == minutes && DateTime.UtcNow - lookup.CheckedAt < TimeSpan.FromMinutes(1))
        {
            return;
        }
        lookup.Busy = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            string alias = FormMain.Globals.resLoading.GetChampionById(hero)?.Alias ?? "";
            string text = await Clients.AramBalanceClient.Shared.GetAsync(
                alias,
                TimeSpan.FromMinutes(minutes),
                timeout.Token
            );
            if (!hint.IsDisposed && !token.IsCancellationRequested)
            {
                lookup.CheckedAt = DateTime.UtcNow;
                lookup.CacheMinutes = minutes;
                _tips.SetToolTip(hint, text);
            }
        }
        catch (Exception error)
        {
            if (!hint.IsDisposed && !token.IsCancellationRequested)
            {
                lookup.CheckedAt = DateTime.UtcNow;
                lookup.CacheMinutes = minutes;
                _tips.SetToolTip(
                    hint,
                    error is OperationCanceledException
                        ? "平衡数据读取超时，稍后悬浮重试。"
                        : "平衡数据暂不可用，稍后悬浮重试。\n" + error.Message
                );
            }
        }
        finally
        {
            lookup.Busy = false;
        }
    }

    private FlowLayoutPanel AddTierSummary(string tier, string statistics)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            MaximumSize = new Size(282, 0),
            Margin = new Padding(0, 2, 0, 2),
        };
        var color = League.Controls.AramMetaView.TierColor(tier);
        var badge = new Label
        {
            Text = tier,
            Size = new Size(42, 24),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(24, color),
            ForeColor = color,
            Margin = new Padding(0, 0, 7, 0),
            AccessibleName = "英雄梯队 " + tier,
        };
        var bold = new Font(Font, FontStyle.Bold);
        badge.Font = bold;
        badge.Disposed += (_, _) => bold.Dispose();
        _tips.SetToolTip(badge, "当前统计条件下的英雄梯队：" + tier);
        row.Controls.Add(badge);
        row.Controls.Add(
            new Label
            {
                Text = statistics,
                AutoSize = true,
                MaximumSize = new Size(230, 0),
                ForeColor = Color.FromArgb(208, 185, 130),
                Margin = new Padding(0, 3, 0, 0),
            }
        );
        _body.Controls.Add(row);
        if (_masteryRow is { IsDisposed: false })
        {
            _body.Controls.SetChildIndex(_masteryRow, _body.Controls.GetChildIndex(row) + 1);
        }

        return row;
    }
}
