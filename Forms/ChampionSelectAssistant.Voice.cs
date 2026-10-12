using League.Services;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private readonly Button _voiceButton = new()
    {
        Name = "assistantVoiceInput",
        Text = "语音输入",
        Enabled = false,
    };
    private VoiceInputDialog? _voiceDialog;
    private CancellationTokenSource? _voiceOpen;
    private bool _openingVoice;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            CloseVoiceInput();
        }
    }

    private void CloseVoiceInput()
    {
        _voiceOpen?.Cancel();
        if (_voiceDialog is { IsDisposed: false })
        {
            _voiceDialog.Close();
        }

        _voiceButton.Enabled = false;
    }

    private async Task ShowVoiceInputAsync()
    {
        if (!_selecting || _openingVoice)
        {
            return;
        }

        if (_voiceDialog is { IsDisposed: false })
        {
            _voiceDialog.Activate();
            return;
        }
        _openingVoice = true;
        _voiceButton.Enabled = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _voiceOpen = timeout;
        try
        {
            var client = FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接。");
            string room = await ChampionSelectVoiceChatService.CaptureRoomAsync(client, timeout.Token);
            if (IsDisposed || !Visible || !_selecting || timeout.IsCancellationRequested)
            {
                return;
            }

            OpenVoiceDialog(client, room);
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && _selecting)
            {
                _status.Text = "语音输入打开取消或超时，请重试。";
            }
        }
        catch (InvalidOperationException exception)
        {
            if (!IsDisposed)
            {
                _status.Text = exception.Message;
            }
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                _status.Text = "选人聊天未连接，请稍后重试。";
            }
        }
        finally
        {
            _voiceOpen = null;
            _openingVoice = false;
            if (!IsDisposed)
            {
                _voiceButton.Enabled = _selecting;
            }
        }
    }

    private void OpenVoiceDialog(HttpClient client, string room)
    {
        VoiceInputDialog? dialog = null;
        bool Current() =>
            !IsDisposed
            && Visible
            && _selecting
            && dialog is { IsDisposed: false }
            && _voiceDialog == dialog
            && FormMain.Globals.lcuClient?.Client == client;
        dialog = new VoiceInputDialog(
            (text, token) => ChampionSelectVoiceChatService.SendAsync(client, room, text, token, Current),
            Current
        );
        _voiceDialog = dialog;
        dialog.Bounds = EquipmentEditorBounds(ClientBounds(), Screen.FromControl(this).WorkingArea, dialog.Size);
        dialog.FormClosed += (_, _) =>
        {
            if (_voiceDialog == dialog)
            {
                _voiceDialog = null;
            }
        };
        dialog.Show(this);
    }

    private Rectangle ClientBounds()
    {
        if (
            _clientWindow != IntPtr.Zero
            && GetWindowRect(_clientWindow, out var rect)
            && rect.Right > rect.Left
            && rect.Bottom > rect.Top
        )
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        return Bounds;
    }
}
