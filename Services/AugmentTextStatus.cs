namespace League.Services;

public enum AugmentTextState
{
    /// <summary>Not asked for yet: the client has not connected.</summary>
    Waiting,

    /// <summary>The patch's texts are being downloaded for the first time; the hand-written ones stand meanwhile.</summary>
    Downloading,

    /// <summary>The game's texts replaced the hand-written ones.</summary>
    Ready,

    /// <summary>The download failed; the hand-written texts stand until a retry.</summary>
    Failed,
}

/// <summary>
/// Where the game's augment descriptions are, for the settings row and the augment tab to show. One per process,
/// since the resources behind it are; <see cref="Changed"/> is raised on whichever thread the load is on, so a page
/// marshals it to its own.
/// </summary>
public sealed class AugmentTextStatus
{
    public static AugmentTextStatus Shared { get; } = new();

    private readonly object _gate = new();

    public AugmentTextState State { get; private set; }

    public string? Patch { get; private set; }

    /// <summary>0 to 1 while downloading.</summary>
    public double Progress { get; private set; }

    public int Count { get; private set; }

    public event Action? Changed;

    public void Downloading(string patch, double progress)
    {
        lock (_gate)
        {
            State = AugmentTextState.Downloading;
            Patch = patch;
            Progress = Math.Clamp(progress, 0, 1);
        }
        Changed?.Invoke();
    }

    public void Ready(string patch, int count)
    {
        lock (_gate)
        {
            State = AugmentTextState.Ready;
            Patch = patch;
            Progress = 1;
            Count = count;
        }
        Changed?.Invoke();
    }

    public void Failed(string? patch)
    {
        lock (_gate)
        {
            State = AugmentTextState.Failed;
            Patch = patch ?? Patch;
            Progress = 0;
        }
        Changed?.Invoke();
    }

    /// <summary>One line for the settings row and the augment tab.</summary>
    public string Describe()
    {
        lock (_gate)
        {
            return State switch
            {
                AugmentTextState.Downloading => $"正在下载 {Patch} 版海克斯说明（{Progress:P0}），完成前显示内置说明",
                AugmentTextState.Ready => $"{Patch} 版 · {Count} 条 · 已就绪",
                AugmentTextState.Failed => "海克斯说明下载失败，正在使用内置说明",
                _ => "连接客户端后加载",
            };
        }
    }
}
