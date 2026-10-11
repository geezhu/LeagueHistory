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
        bool busy = false;
        DateTime checkedAt = DateTime.MinValue;
        int checkedMinutes = 0;
        _tips.SetToolTip(hint, "正在读取平衡数据…");
        async Task Load()
        {
            if (busy || token.IsCancellationRequested || hint.IsDisposed)
                return;
            int minutes = Math.Clamp(_owner.GetAppConfig()?.AramBalanceCacheMinutes ?? 1440, 1, 10080);
            if (checkedMinutes == minutes && DateTime.UtcNow - checkedAt < TimeSpan.FromMinutes(1))
                return;
            busy = true;
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
                if (hint.IsDisposed || token.IsCancellationRequested)
                    return;
                checkedAt = DateTime.UtcNow;
                checkedMinutes = minutes;
                _tips.SetToolTip(hint, text);
            }
            catch (Exception ex)
            {
                if (hint.IsDisposed || token.IsCancellationRequested)
                    return;
                checkedAt = DateTime.UtcNow;
                checkedMinutes = minutes;
                _tips.SetToolTip(
                    hint,
                    ex is OperationCanceledException
                        ? "平衡数据读取超时，稍后悬浮重试。"
                        : "平衡数据暂不可用，稍后悬浮重试。\n" + ex.Message
                );
            }
            finally
            {
                busy = false;
            }
        }
        hint.MouseEnter += (_, _) => _ = Load();
        hint.Click += (_, _) => _ = Load();
        _ = Load();
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
            _body.Controls.SetChildIndex(_masteryRow, _body.Controls.GetChildIndex(row) + 1);
        return row;
    }
}
