using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace League.Services;

public sealed record VoiceMicrophoneChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Enumerating endpoints does not start recording; selection is resolved for each user-started session.</summary>
public static class VoiceMicrophoneDevices
{
    public static IReadOnlyList<VoiceMicrophoneChoice> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        string defaultName = "跟随 Windows 默认麦克风";
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            defaultName += " · " + device.FriendlyName;
        }
        catch (COMException) { }
        var result = new List<VoiceMicrophoneChoice> { new("", defaultName) };
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
                result.Add(new(device.ID, device.FriendlyName));
        }
        return result;
    }

    public static MMDevice Open(string selected)
    {
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            var device = string.IsNullOrWhiteSpace(selected)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                : enumerator.GetDevice(selected);
            if (device.State == DeviceState.Active && device.DataFlow == DataFlow.Capture)
                return device;
            device.Dispose();
        }
        catch (COMException) { }
        throw new VoiceRecognitionException("所选麦克风已断开或不可用，请到设置 → 消息发送重新选择麦克风。");
    }
}
