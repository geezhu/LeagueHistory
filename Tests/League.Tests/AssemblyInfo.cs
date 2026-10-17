using Xunit;

// The application keeps process-wide state (FormMain.Globals, AppLog.Shared, global hotkeys, the data directory) and
// its WinForms controls are not thread safe, so the whole assembly runs one test at a time. Tests that change that
// state share a collection and put it back when they are disposed.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
