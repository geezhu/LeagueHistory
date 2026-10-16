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
    private readonly Button _import = new() { Name = "VoiceModelImport", Text = "导入已下载的文件…" };
    private readonly LinkLabel _links = new() { Name = "VoiceModelLinks" };
    private readonly ToolTip _tips = new();
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
        _import.Click += async (_, _) => await ImportAsync();
        BuildLinks();
    }

    // Manual download for networks where the in-app download fails: open each file in the browser,
    // or copy every link for a download manager, then import the files with the button beside it.
    private void BuildLinks()
    {
        var text = new System.Text.StringBuilder("手动下载 · ");
        var links = new List<(int Start, int Length, string Data)>();
        void Link(string label, string data)
        {
            links.Add((text.Length, label.Length, data));
            text.Append(label);
        }

        Link("复制全部链接", "");
        // One line per file, so a narrow page never wraps inside a file's pair of links.
        foreach (var group in _store.DownloadLinks.GroupBy(link => link.File))
        {
            text.Append('\n').Append(group.Key).Append("：");
            bool first = true;
            foreach (var (_, source, url) in group)
            {
                if (!first)
                {
                    text.Append(" ｜ ");
                }

                Link(source.StartsWith("hf-mirror", StringComparison.Ordinal) ? "镜像" : "官方", url);
                first = false;
            }
        }

        _links.Text = text.ToString();
        foreach (var (start, length, data) in links)
        {
            _links.Links.Add(start, length, data);
        }

        _links.LinkClicked += (_, e) => OpenLink((string)e.Link!.LinkData!);
        _tips.SetToolTip(
            _links,
            "下载后点「导入已下载的文件…」，选择 model.int8.onnx 和 tokens.txt；导入前校验 SHA-256。\n浏览器直接显示 tokens.txt 内容时，按 Ctrl+S 保存。"
        );
    }

    private void OpenLink(string url)
    {
        if (url.Length == 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _store.DownloadLinks.Select(link => link.Url)));
            _status.Text = "已复制全部下载链接，可粘贴到浏览器或下载工具。";
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    // Two setting rows: the model with its prepare action, and the input with its refresh action.
    public Control[] Content
    {
        get
        {
            var model = new SettingRow("中文语音模型", null, _prepare, _import);
            model.AddNote(_status);
            model.AddNote(_links);
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
            AppLog.Shared.Warn(LogCategory.Ui, "Voice input device listing", error);
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
            AppLog.Shared.Warn(LogCategory.Ui, "Voice model settings", error);
            if (!_disposed)
            {
                ShowFailure("模型准备失败", error);
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

    // The message says what failed; the underlying network or file error stays in the tooltip.
    private void ShowFailure(string title, Exception error)
    {
        _status.Text = $"{title}：{error.Message} 可用下方直链手动下载后导入。";
        _tips.SetToolTip(_status, error.InnerException?.Message ?? error.Message);
    }

    private async Task ImportAsync()
    {
        if (_disposed || _downloading != null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "选择已下载的模型文件",
            Filter = "SenseVoice 模型文件|*.onnx;*.txt|所有文件|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(_import.FindForm()) != DialogResult.OK)
        {
            return;
        }

        using var source = new CancellationTokenSource();
        _downloading = source;
        _prepare.Enabled = _import.Enabled = false;
        _status.Text = "正在校验并导入模型文件…";
        try
        {
            string[] imported = await _store.ImportAsync(dialog.FileNames, source.Token);
            string[] missing = _store.MissingFiles;
            if (!_disposed)
            {
                _status.Text =
                    missing.Length == 0
                        ? "中文模型已导入 · 选人助手与 Game Bar 共用，识别时无需联网。"
                        : $"已导入 {string.Join("、", imported)}，还缺少 {string.Join("、", missing)}。";
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (!_disposed)
            {
                ShowFailure("导入失败", error);
            }
        }
        finally
        {
            _downloading = null;
            if (!_disposed)
            {
                _prepare.Enabled = _import.Enabled = true;
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
        _tips.Dispose();
    }
}
