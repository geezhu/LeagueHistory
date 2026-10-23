using League.Services;

namespace League.Tests.Support;

/// <summary>
/// A 准备情况 environment with every fact written down: a developer machine may or may not run elevated or have the
/// component installed, reading the component starts PowerShell, and restarting would raise a UAC prompt.
/// </summary>
public static class PinnedSetup
{
    public static readonly GameBarPackageStatus CurrentPackage = new(new Version(1, 0, 0, 1), new Version(1, 0, 0, 1));

    public static SetupEnvironment Create(
        bool administrator = true,
        bool voiceModel = true,
        Func<CancellationToken, Task<GameBarPackageStatus>>? package = null,
        Func<bool>? startElevated = null
    ) =>
        new()
        {
            IsAdministrator = () => administrator,
            VoiceModelAvailable = () => voiceModel,
            ReadGameBarPackage = package ?? (_ => Task.FromResult(CurrentPackage)),
            StartElevatedCopy =
                startElevated ?? (() => throw new InvalidOperationException("Tests never restart elevated.")),
        };
}
