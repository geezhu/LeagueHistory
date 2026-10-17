namespace League.Tests.Support;

/// <summary>
/// Tests that change process-wide state — environment variables, <c>FormMain.Globals</c>, <c>AppLog.Shared</c>, global
/// hotkeys, the data directory — join this collection. The assembly already runs one test at a time; the collection
/// marks which tests must not be reordered against each other and reminds each one to put the old value back.
/// </summary>
[CollectionDefinition(Name)]
public class EnvironmentCollection
{
    public const string Name = "process-wide state";
}
