namespace League.Controls;

public partial class PlayerCardControl
{
    private const int StatusStripHeight = 32;
    private Panel? _historyStatus;
    private Label? _historyStatusText;
    private Button? _historyRetry;
    private Action? _retryHistory;

    public void SetHistoryLoadStatus(string text, bool loading, bool canRetry, Action retry)
    {
        IsLoading = loading;
        if (_historyStatus == null)
        {
            _historyStatus = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = StatusStripHeight,
                BackColor = Theme.Canvas,
            };
            _historyStatusText = new Label { AutoEllipsis = true };
            _historyRetry = new Button { Text = "重试" };
            ButtonStyles.Ghost(_historyRetry);
            _historyRetry.Click += (_, _) => _retryHistory?.Invoke();
            _historyStatus.Controls.Add(_historyStatusText);
            _historyStatus.Controls.Add(_historyRetry);
            _historyStatus.Layout += (_, _) => ArrangeHistoryStatus();
            // Dock this before the fill host is laid out; bringing it to the front would
            // let the native history ListView cover the status and retry button.
            Controls.Add(_historyStatus);
        }
        _retryHistory = retry;
        _historyStatusText!.Text = text;
        _historyStatusText.ForeColor = canRetry ? Theme.Loss : Theme.Muted;
        _historyRetry!.Visible = canRetry;
        _historyStatus.Visible = text.Length > 0;
        FitHistoryStatus();
    }

    // The status height depends on the card's own size and the summary above it, so it is settled here, before docking
    // places it: changed from a Resize handler, the new height arrived after docking and pushed the strip off the card.
    protected override void OnLayout(LayoutEventArgs e)
    {
        SizeHistoryStatus();
        base.OnLayout(e);
    }

    private void FitHistoryStatus()
    {
        if (_historyStatus != null)
        {
            PerformLayout();
        }
    }

    // With games to show, the status is a thin strip under them. With none, it takes the whole history area so the
    // message and the retry button sit in the middle of the card instead of under an empty list.
    private void SizeHistoryStatus()
    {
        if (_historyStatus == null)
        {
            return;
        }

        int height =
            listViewGames.Items.Count == 0
                ? Math.Max(StatusStripHeight, ClientSize.Height - Padding.Vertical - SummaryPanel.Height)
                : StatusStripHeight;
        if (_historyStatus.Height != height)
        {
            _historyStatus.Height = height;
        }
        _historyStatus.BackColor = height > StatusStripHeight ? Theme.Surface : Theme.Canvas;
    }

    private void ArrangeHistoryStatus()
    {
        if (_historyStatus == null || _historyStatusText == null || _historyRetry == null)
        {
            return;
        }

        int width = _historyStatus.ClientSize.Width,
            height = _historyStatus.ClientSize.Height;
        if (height <= StatusStripHeight)
        {
            int retryWidth = _historyRetry.Visible ? 52 : 0;
            _historyStatusText.TextAlign = ContentAlignment.MiddleLeft;
            _historyStatusText.SetBounds(6, 0, Math.Max(1, width - retryWidth - 6), height);
            _historyRetry.SetBounds(width - 52, 0, 52, height);
            return;
        }

        const int textHeight = 40,
            buttonWidth = 72,
            buttonHeight = 28;
        int top = (height - textHeight - (_historyRetry.Visible ? buttonHeight + 8 : 0)) / 2;
        _historyStatusText.TextAlign = ContentAlignment.MiddleCenter;
        _historyStatusText.SetBounds(12, top, Math.Max(1, width - 24), textHeight);
        _historyRetry.SetBounds((width - buttonWidth) / 2, top + textHeight + 8, buttonWidth, buttonHeight);
    }
}
