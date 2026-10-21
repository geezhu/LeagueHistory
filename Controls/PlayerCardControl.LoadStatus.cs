namespace League.Controls;

public partial class PlayerCardControl
{
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
                Height = 32,
                BackColor = Theme.Canvas,
            };
            _historyStatusText = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
            };
            _historyRetry = new Button
            {
                Text = "重试",
                Dock = DockStyle.Right,
                Width = 52,
                FlatStyle = FlatStyle.Flat,
            };
            _historyRetry.FlatAppearance.BorderSize = 0;
            _historyRetry.Click += (_, _) => _retryHistory?.Invoke();
            _historyStatus.Controls.Add(_historyStatusText);
            _historyStatus.Controls.Add(_historyRetry);
            // Dock this before the fill host is laid out; bringing it to the front would
            // let the native history ListView cover the status and retry button.
            Controls.Add(_historyStatus);
        }
        _retryHistory = retry;
        _historyStatusText!.Text = text;
        _historyStatusText.ForeColor = canRetry ? Theme.Loss : Theme.Muted;
        _historyRetry!.Visible = canRetry;
        _historyStatus.Visible = text.Length > 0;
    }
}
