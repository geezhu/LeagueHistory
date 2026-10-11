namespace League.Services;

// Stored as an int by Interlocked/Volatile. Stop retains audio; Dispose discards it.
internal enum VoiceCaptureState
{
    Running,
    StopRequested,
    Disposed,
}
