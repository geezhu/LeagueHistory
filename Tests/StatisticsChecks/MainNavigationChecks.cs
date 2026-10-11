using System.Reflection;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

internal static class MainNavigationChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        RoomDetailReplacement();
        RoomDetailClosure();
        MissingRoomParticipant();
        SearchSurface();
        ClosedNavigation();
        Console.WriteLine(
            "PASS: main navigation request retirement, parsed source ownership, missing identity, search layout and closed-window guards."
        );
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, PrivateInstance)!.SetValue(target, value);

    private static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, PrivateInstance)!.GetValue(target)!;

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, null);

    private static MatchMetadata Metadata(long id) =>
        new()
        {
            GameId = id,
            GameData = new JObject
            {
                ["participants"] = new JArray(new JObject { ["puuid"] = "self", ["teamId"] = 100 }),
            },
        };

    private static MatchListPanel Card(long id) =>
        new(
            new MatchInfo
            {
                GameId = id,
                AllParticipants = new() { new JObject { ["teamId"] = 100 } },
            }
        );

    private static void Complete(Task work)
    {
        PumpUntil(() => work.IsCompleted);
        work.GetAwaiter().GetResult();
    }

    private static void RoomDetailReplacement()
    {
        using var owner = new League.FormMain();
        var waiting = new TaskCompletionSource<Panel?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var current = Card(2);
        var retired = Card(1);
        Set(
            owner,
            "_parseRoomMatch",
            (Func<JObject, JObject, Task<Panel?>>)(
                (_, _) => ++calls == 1 ? waiting.Task : Task.FromResult<Panel?>(current)
            )
        );
        Set(owner, "_prepareRoomMatchDetail", (Func<MatchDetailView, Task>)(_ => Task.CompletedTask));
        var first = owner.OpenRoomMatchAsync(Metadata(1), "self");
        var firstSurface = Get<Panel>(owner, "_roomMatchSurface");
        Complete(owner.OpenRoomMatchAsync(Metadata(2), "self"));
        var surface = Get<Panel>(owner, "_roomMatchSurface");
        Check(
            firstSurface.IsDisposed && surface.Controls.OfType<MatchDetailView>().Count() == 1,
            "A replacement owns exactly one detail surface."
        );
        waiting.SetResult(retired);
        Complete(first);
        Check(
            retired.IsDisposed
                && !current.IsDisposed
                && ReferenceEquals(surface, Get<Panel>(owner, "_roomMatchSurface")),
            "Late parsed cards are released without altering the replacement."
        );
        surface.Dispose();
        Check(current.IsDisposed, "The displayed detail owns its parsed source until closure.");
    }

    private static void RoomDetailClosure()
    {
        using var owner = new League.FormMain();
        var source = Card(1);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(
            owner,
            "_parseRoomMatch",
            (Func<JObject, JObject, Task<Panel?>>)((_, _) => Task.FromResult<Panel?>(source))
        );
        Set(owner, "_prepareRoomMatchDetail", (Func<MatchDetailView, Task>)(_ => waiting.Task));
        var work = owner.OpenRoomMatchAsync(Metadata(1), "self");
        var surface = Get<Panel>(owner, "_roomMatchSurface");
        var detail = surface.Controls.OfType<MatchDetailView>().Single();
        surface.Dispose();
        waiting.SetResult();
        Complete(work);
        Check(source.IsDisposed && detail.IsDisposed, "Closing a preparing detail releases the parsed card and view.");
    }

    private static void MissingRoomParticipant()
    {
        using var owner = new League.FormMain();
        Set(
            owner,
            "_parseRoomMatch",
            (Func<JObject, JObject, Task<Panel?>>)(
                (_, _) => throw new Exception("Unconfirmed participants must not start resource parsing.")
            )
        );
        Complete(owner.OpenRoomMatchAsync(Metadata(1), "missing"));
        var surface = Get<Panel>(owner, "_roomMatchSurface");
        Check(
            surface.Controls.OfType<Label>().Single().Text.Contains("无法确定"),
            "Missing account identity is explained."
        );
    }

    private static void SearchSurface()
    {
        using var owner = new League.FormMain();
        Invoke(owner, "InitializeSearchHistory");
        var box = Get<Panel>(owner, "_searchBox");
        var input = Get<ComboBox>(owner, "txtGameName");
        Check(
            input.Parent == box && input.ContextMenuStrip!.Items.Count == 5,
            "Search input retains its history actions."
        );
        Check(
            box.Region != null && input.AutoCompleteMode == AutoCompleteMode.SuggestAppend,
            "Search chrome and completion are configured independently."
        );
        input.Text = "测试玩家#1234";
        Invoke(owner, "RefreshSearchHistory");
        Check(input.Text == "测试玩家#1234", "Refreshing history preserves the pending input.");
    }

    private static void ClosedNavigation()
    {
        var owner = new League.FormMain();
        owner.Dispose();
        owner.OpenPlayerProfileAsync("123").GetAwaiter().GetResult();
        (
            (Task)
                owner
                    .GetType()
                    .GetMethod("OpenAssistantGuideAsync", PrivateInstance)!
                    .Invoke(owner, new object?[] { 99, 450, "none", null })!
        )
            .GetAwaiter()
            .GetResult();
        owner.OpenRoomMatchAsync(Metadata(1), "self").GetAwaiter().GetResult();
        Check(owner.IsDisposed, "Navigation cannot recreate a closed main window.");
    }
}
