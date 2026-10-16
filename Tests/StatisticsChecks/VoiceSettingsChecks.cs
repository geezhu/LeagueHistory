using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using League.Services;
using static StatisticsCheckSupport;

internal static class VoiceSettingsChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type ControllerType = typeof(League.FormMain).Assembly.GetType(
        "League.Controls.VoiceSettingsController"
    )!;

    public static void Run()
    {
        DeviceSelectionAndRecovery();
        LateDeviceList();
        ModelCancellation();
        ModelStateDisplay();
        Console.WriteLine(
            "PASS: voice settings device selection, unavailable inputs, listing failure/recovery, cancelled model preparation and the ready-model summary. No microphone or network used."
        );
    }

    // A ready model shows what it is instead of the manual download links; a missing one offers them.
    private static void ModelStateDisplay()
    {
        string root = Path.Combine(Path.GetTempPath(), "voice-model-state-" + Guid.NewGuid().ToString("N"));
        byte[] content = [1, 2, 3, 4];
        var file = new VoiceModelFile(
            "fixture.bin",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
        );
        try
        {
            (bool Links, bool Import, string Status) Show()
            {
                using var controller = (IDisposable)Controller(
                    "",
                    _ => { },
                    new VoiceModelStore(root, null, [file]),
                    () => []
                );
                using var form = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-30000, -30000),
                };
                var flow = new FlowLayoutPanel { Dock = DockStyle.Fill };
                flow.Controls.AddRange(Content(controller));
                form.Controls.Add(flow);
                form.Show();
                var links = flow.Controls.Find("VoiceModelLinks", true).Single();
                var import = flow.Controls.Find("VoiceModelImport", true).Single();
                var status = flow.Controls.Find("VoiceModelStatus", true).Single();
                return (links.Visible, import.Visible, status.Text);
            }

            var missing = Show();
            Check(
                missing.Links && missing.Import && missing.Status.Contains("尚未下载"),
                "A missing model offers the links"
            );
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, file.Name), content);
            var ready = Show();
            Check(
                !ready.Links && !ready.Import && ready.Status.Contains("SenseVoice 中文模型 · 2024-07-17 · int8"),
                "A ready model shows its name and state instead of the links: " + ready.Status
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static object Controller(
        string selected,
        Action<string> save,
        VoiceModelStore store,
        Func<IReadOnlyList<VoiceMicrophoneChoice>> choices
    ) => Activator.CreateInstance(ControllerType, (Func<string>)(() => selected), save, store, choices)!;

    // Controller content nests labels inside setting rows.
    private static IEnumerable<Label> Labels(Control root) => Descendants(root).OfType<Label>();

    private static IEnumerable<Control> Descendants(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));

    private static Control[] Content(object controller) =>
        (Control[])ControllerType.GetProperty("Content")!.GetValue(controller)!;

    private static Task Run(object controller, string method) =>
        (Task)ControllerType.GetMethod(method, PrivateInstance)!.Invoke(controller, null)!;

    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void DeviceSelectionAndRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "voice-settings-" + Guid.NewGuid().ToString("N"));
        bool fail = false;
        var saved = new List<string>();
        IReadOnlyList<VoiceMicrophoneChoice> Choices() =>
            fail
                ? throw new InvalidOperationException("fixture devices unavailable")
                : [new("", "默认设备"), new("device-one", "测试输入")];
        using var controller = (IDisposable)Controller("removed-device", saved.Add, new VoiceModelStore(root), Choices);
        using var flow = new FlowLayoutPanel();
        flow.Controls.AddRange(Content(controller));
        var input = flow.Controls.Find("VoiceMicrophone", true).OfType<ComboBox>().Single();
        Complete(Run(controller, "LoadMicrophonesAsync"));
        Check(
            input.Enabled && ((VoiceMicrophoneChoice)input.SelectedItem!).Id == "removed-device" && saved.Count == 0,
            "An unavailable saved input is retained without saving during population."
        );
        input.SelectedIndex = 1;
        Check(saved.SequenceEqual(new[] { "device-one" }), "Only a user selection persists the input.");
        fail = true;
        Complete(Run(controller, "LoadMicrophonesAsync"));
        Check(
            !input.Enabled && Labels(flow).Any(label => label.Text.Contains("读取失败")),
            "Listing failure disables the invalid choice without starting recording."
        );
        fail = false;
        Complete(Run(controller, "LoadMicrophonesAsync"));
        Check(input.Enabled && saved.Count == 1, "Recovery repopulates inputs without a synthetic selection write.");
    }

    private static void LateDeviceList()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int writes = 0;
        IReadOnlyList<VoiceMicrophoneChoice> Choices()
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The simulated device list was not released.");
            }
            return [new("", "默认设备")];
        }
        using var controller = (IDisposable)Controller(
            "",
            _ => writes++,
            new VoiceModelStore(Path.Combine(Path.GetTempPath(), "unused-voice-store")),
            Choices
        );
        using var flow = new FlowLayoutPanel();
        flow.Controls.AddRange(Content(controller));
        var work = Run(controller, "LoadMicrophonesAsync");
        try
        {
            PumpUntil(() => entered.IsSet);
            controller.Dispose();
            flow.Dispose();
        }
        finally
        {
            release.Set();
            Complete(work);
        }
        Check(writes == 0 && flow.IsDisposed, "Late enumeration cannot write preferences or restore a disposed card.");
    }

    private static void ModelCancellation()
    {
        string root = Path.Combine(Path.GetTempPath(), "voice-model-settings-" + Guid.NewGuid().ToString("N"));
        byte[] content = [1, 2, 3, 4];
        var file = new VoiceModelFile(
            "fixture.bin",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()
        );
        var handler = new PendingDownload();
        var store = new VoiceModelStore(root, () => new HttpClient(handler, disposeHandler: false), [file]);
        using var controller = (IDisposable)Controller("", _ => { }, store, () => []);
        using var flow = new FlowLayoutPanel();
        flow.Controls.AddRange(Content(controller));
        var prepare = Run(controller, "PrepareAsync");
        try
        {
            PumpUntil(() => handler.Started.Task.IsCompleted);
            Complete(Run(controller, "PrepareAsync")); // A repeated prepare action requests cancellation.
            Complete(prepare);
            Check(
                Labels(flow).Any(label => label.Text.Contains("下载已取消")),
                "Cancelled preparation keeps the card available for retry."
            );
            Check(!File.Exists(Path.Combine(root, file.Name)), "Cancellation cannot publish a partial model file.");
        }
        finally
        {
            controller.Dispose();
            Complete(prepare);
            handler.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private sealed class PendingDownload : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
