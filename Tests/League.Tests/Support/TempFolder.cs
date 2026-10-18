namespace League.Tests.Support;

/// <summary>
/// A directory under the system temp path, removed on dispose. Tests that need to read or write files use this
/// instead of the user's own cache or config directories.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder(string prefix)
    {
        Root = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
