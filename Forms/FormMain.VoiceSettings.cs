using League.Services;

namespace League;

public partial class FormMain
{
    private void ConfigureVoiceSettings(FlowLayoutPanel parent)
    {
        var store = VoiceModelStore.Shared;
        var status = new Label
        {
            Name = "VoiceModelStatus",
            Text = store.AvailableDirectory == null ? "模型尚未下载 · 约 228 MiB" : "中文模型已准备 · 离线识别",
        };
        var prepare = new Button
        {
            Name = "VoiceModelDownload",
            Text = store.AvailableDirectory == null ? "下载中文模型" : "检查 / 修复模型",
        };
        var microphone = new ComboBox
        {
            Name = "VoiceMicrophone",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 480,
            Enabled = false,
        };
        var refresh = new Button { Name = "VoiceMicrophoneRefresh", Text = "刷新麦克风" };
        var microphoneStatus = new Label { Name = "VoiceMicrophoneStatus", Text = "正在读取麦克风列表…" };
        bool choosing = false;
        async Task LoadMicrophones()
        {
            refresh.Enabled = false;
            microphone.Enabled = false;
            choosing = false;
            try
            {
                var choices = await Task.Run(VoiceMicrophoneDevices.List);
                if (microphone.IsDisposed)
                    return;
                string selected = _appConfig?.VoiceMicrophoneId ?? "";
                microphone.Items.Clear();
                microphone.Items.AddRange(choices.Cast<object>().ToArray());
                var choice = choices.FirstOrDefault(c => c.Id == selected);
                if (choice == null)
                {
                    choice = new(selected, "已选麦克风不可用 · 请重新选择");
                    microphone.Items.Add(choice);
                }
                microphone.SelectedItem = choice;
                microphoneStatus.Text =
                    choices.Count > 1
                        ? "选人助手与 Game Bar 共用此麦克风；更改后，下次录音生效。"
                        : "没有找到可用的麦克风，请连接设备后刷新。";
                choosing = true;
            }
            catch (Exception)
            {
                if (!microphoneStatus.IsDisposed)
                    microphoneStatus.Text = "麦克风列表读取失败，请连接设备后刷新。";
            }
            finally
            {
                if (!refresh.IsDisposed)
                    refresh.Enabled = true;
                if (!microphone.IsDisposed)
                    microphone.Enabled = choosing;
            }
        }
        microphone.SelectedIndexChanged += (_, _) =>
        {
            if (!choosing || _appConfig == null || microphone.SelectedItem is not VoiceMicrophoneChoice choice)
                return;
            _appConfig.VoiceMicrophoneId = choice.Id;
            SaveAppConfig();
        };
        refresh.Click += async (_, _) => await LoadMicrophones();
        CancellationTokenSource? downloading = null;
        prepare.Disposed += (_, _) => downloading?.Cancel();
        prepare.Click += async (_, _) =>
        {
            if (downloading != null)
            {
                downloading.Cancel();
                return;
            }
            using var source = new CancellationTokenSource();
            downloading = source;
            prepare.Text = "取消下载";
            bool Current() => !prepare.IsDisposed && !source.IsCancellationRequested;
            try
            {
                status.Text = "正在检查本机中文模型…";
                try
                {
                    await store.ValidateAsync(source.Token);
                }
                catch (VoiceRecognitionException)
                {
                    var progress = new Progress<int>(value =>
                    {
                        if (Current())
                            status.Text = $"正在下载中文模型 {value}% · 约 228 MiB";
                    });
                    await store.DownloadAsync(progress, source.Token);
                    await store.ValidateAsync(source.Token);
                }
                if (Current())
                    status.Text = "中文模型已准备 · 选人助手与 Game Bar 共用，识别时无需联网。";
            }
            catch (OperationCanceledException)
            {
                if (!status.IsDisposed)
                    status.Text = "下载已取消，可以稍后重试。";
            }
            catch (Exception)
            {
                if (!status.IsDisposed)
                    status.Text = "模型准备失败，请检查网络和本地空间后重试。";
            }
            finally
            {
                downloading = null;
                if (!prepare.IsDisposed)
                    prepare.Text = store.AvailableDirectory == null ? "下载中文模型" : "检查 / 修复模型";
            }
        };
        SettingsCard(
            parent,
            "本机语音识别",
            status,
            prepare,
            new Label { Text = "录音麦克风" },
            microphone,
            refresh,
            microphoneStatus,
            new Label
            {
                Text =
                    "最长录音 20 秒，提示“可以说话了”后开口，说完点击停止。\n麦克风和中文模型并行准备，模型加载时也能录音。\n首次下载需要联网，之后在本机识别；不上传或保存录音。\n发送前检查文字并确认。",
            }
        );
        _ = LoadMicrophones();
    }
}
