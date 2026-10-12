using League.Services;

namespace League.Controls;

/// <summary>Loads input choices and prepares local model files; it never opens a recording session.</summary>
internal sealed class VoiceSettingsController : IDisposable
{
    private readonly VoiceModelStore _store;
    private readonly Func<IReadOnlyList<VoiceMicrophoneChoice>> _listDevices;
    private readonly Func<string> _selectedDevice;
    private readonly Action<string> _saveDevice;
    private readonly Label _status = new() { Name = "VoiceModelStatus" };
    private readonly Button _prepare = new() { Name = "VoiceModelDownload" };
    private readonly ComboBox _microphone = new()
    {
        Name = "VoiceMicrophone",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300,
        Enabled = false,
    };
    private readonly Button _refresh = new() { Name = "VoiceMicrophoneRefresh", Text = "刷新麦克风" };
    private readonly Label _microphoneStatus = new() { Name = "VoiceMicrophoneStatus", Text = "正在读取麦克风列表…" };
    private bool _loadingDevices;
    private bool _choosing;
    private bool _disposed;
    private CancellationTokenSource? _downloading;

    public VoiceSettingsController(
        Func<string> selectedDevice,
        Action<string> saveDevice,
        VoiceModelStore? store = null,
        Func<IReadOnlyList<VoiceMicrophoneChoice>>? listDevices = null
    )
    {
        _selectedDevice = selectedDevice;
        _saveDevice = saveDevice;
        _store = store ?? VoiceModelStore.Shared;
        _listDevices = listDevices ?? VoiceMicrophoneDevices.List;
        _status.Text = _store.AvailableDirectory == null ? "模型尚未下载 · 约 228 MiB" : "中文模型已准备 · 离线识别";
        ResetPrepareButton();
        _microphone.SelectedIndexChanged += (_, _) => SaveSelection();
        _refresh.Click += async (_, _) => await LoadMicrophonesAsync();
        _prepare.Click += async (_, _) => await PrepareAsync();
    }

    // Two setting rows: the model with its prepare action, and the input with its refresh action.
    public Control[] Content
    {
        get
        {
            var model = new SettingRow("中文语音模型", null, _prepare);
            model.AddNote(_status);
            var microphone = new SettingRow("录音麦克风", null, _refresh);
            microphone.AddNote(_microphone);
            microphone.AddNote(_microphoneStatus);
            return
            [
                model,
                microphone,
                new Label { Text = "最长录音 20 秒，首次下载后在本机识别，不上传或保存录音；发送前需确认文字。" },
            ];
        }
    }

    public void Attach()
    {
        _prepare.Disposed += (_, _) => Dispose();
        _ = LoadMicrophonesAsync();
    }

    private async Task LoadMicrophonesAsync()
    {
        if (_disposed || _loadingDevices)
        {
            return;
        }
        _loadingDevices = true;
        _refresh.Enabled = _microphone.Enabled = _choosing = false;
        try
        {
            // Endpoint enumeration cannot be aborted; disposal only retires its UI result.
            var choices = await Task.Run(_listDevices, CancellationToken.None);
            if (!_disposed)
            {
                ShowMicrophones(choices);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Voice input device listing: {error}");
            if (!_disposed)
            {
                _microphoneStatus.Text = "麦克风列表读取失败，请连接设备后刷新。";
            }
        }
        finally
        {
            _loadingDevices = false;
            if (!_disposed)
            {
                _refresh.Enabled = true;
                _microphone.Enabled = _choosing;
            }
        }
    }

    private void ShowMicrophones(IReadOnlyList<VoiceMicrophoneChoice> choices)
    {
        string selected = _selectedDevice();
        _microphone.Items.Clear();
        _microphone.Items.AddRange(choices.Cast<object>().ToArray());
        var choice = choices.FirstOrDefault(item => item.Id == selected);
        if (choice == null)
        {
            choice = new(selected, "已选麦克风不可用 · 请重新选择");
            _microphone.Items.Add(choice);
        }
        _microphone.SelectedItem = choice;
        _microphoneStatus.Text =
            choices.Count > 1
                ? "选人助手与 Game Bar 共用此麦克风；更改后，下次录音生效。"
                : "没有找到可用的麦克风，请连接设备后刷新。";
        _choosing = true;
    }

    private void SaveSelection()
    {
        if (!_disposed && _choosing && _microphone.SelectedItem is VoiceMicrophoneChoice choice)
        {
            _saveDevice(choice.Id);
        }
    }

    private async Task PrepareAsync()
    {
        if (_disposed)
        {
            return;
        }
        if (_downloading != null)
        {
            await _downloading.CancelAsync();
            return;
        }
        using var source = new CancellationTokenSource();
        _downloading = source;
        _prepare.Text = "取消下载";
        bool Current() => !_disposed && !source.IsCancellationRequested;
        try
        {
            _status.Text = "正在检查本机中文模型…";
            await EnsureModelAsync(source, Current);
            if (Current())
            {
                _status.Text = "中文模型已准备 · 选人助手与 Game Bar 共用，识别时无需联网。";
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
            {
                _status.Text = "下载已取消，可以稍后重试。";
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Voice model settings: {error}");
            if (!_disposed)
            {
                _status.Text = "模型准备失败，请检查网络和本地空间后重试。";
            }
        }
        finally
        {
            _downloading = null;
            if (!_disposed)
            {
                ResetPrepareButton();
            }
        }
    }

    private async Task EnsureModelAsync(CancellationTokenSource source, Func<bool> current)
    {
        try
        {
            await _store.ValidateAsync(source.Token);
        }
        catch (VoiceRecognitionException)
        {
            var progress = new Progress<int>(value =>
            {
                if (current())
                {
                    _status.Text = $"正在下载中文模型 {value}% · 约 228 MiB";
                }
            });
            await _store.DownloadAsync(progress, source.Token);
            await _store.ValidateAsync(source.Token);
        }
    }

    private void ResetPrepareButton() =>
        _prepare.Text = _store.AvailableDirectory == null ? "下载中文模型" : "检查 / 修复模型";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // The worker owns its cancellation source until file operations have unwound.
        _downloading?.Cancel();
    }
}
