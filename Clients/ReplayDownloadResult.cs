namespace League.Clients;

/// <summary>How a replay download ended; only <see cref="Success"/> and <see cref="AlreadyExists"/> can be watched.</summary>
public enum ReplayDownloadResult
{
    Success,
    AlreadyExists,

    // The client calls a replay from an older game version "incompatible"; it can no longer be downloaded.
    Incompatible,
    Failed,
    Timeout,
    RequestError,
}
