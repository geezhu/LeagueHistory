using System.Net;
using System.Reflection;
using League.Controls;
using League.Models;
using League.UIHelpers;
using Newtonsoft.Json.Linq;

internal static class ProfileSurfaceChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly Views = typeof(PlayerCardControl).Assembly;

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(send(request));
    }

    public static void Run()
    {
        BackgroundSelectionAndColors();
        LateBackground();
        LauncherSelectionAndClose();
        HighlightMissingData();
        Console.WriteLine(
            "PASS: profile backgrounds, color ownership, late artwork, launcher selection and incomplete highlights. No game process launched."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance)!.SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target
            .GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(target, args);

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(5);
            Check(DateTime.UtcNow < deadline, "Timed out waiting for simulated profile surface work.");
        }
        Application.DoEvents();
    }

    private static Control Background() =>
        (Control)Activator.CreateInstance(Views.GetType("League.Controls.ProfileBackgroundPanel")!)!;

    private static bool Disposed(Image image)
    {
        try
        {
            _ = image.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool Disposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static void CloseBackground(Control view)
    {
        view.Dispose();
        PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
        Check(Field<Task>(view, "_cleanupWork").IsCompletedSuccessfully, "Background work must finish cleanly.");
    }

    private static void BackgroundSelectionAndColors()
    {
        using var view = Background();
        var type = view.GetType();
        int Select(JToken? profile, JToken? mastery) =>
            (int)type.GetMethod("SelectSkin")!.Invoke(null, new object?[] { profile, mastery })!;
        Check(
            Select(null, JArray.Parse("[{\"championId\":25,\"championPoints\":100}]")) == 25000,
            "Missing profile artwork uses the top mastery champion's base skin."
        );
        Check(
            Select(JObject.Parse("{\"backgroundSkinId\":103015}"), null) == 103015,
            "An explicit background retains its exact skin ID."
        );
        var champion = JObject.Parse(
            "{\"skins\":[{\"id\":103015,\"uncenteredSplashPath\":\"/lol-game-data/assets/art.jpg\"},"
                + "{\"id\":103000,\"questSkinInfo\":{\"tiers\":[{\"id\":103017,\"splashPath\":\"/lol-game-data/assets/quest.jpg\"}]}}]}"
        );
        Check(
            (string?)type.GetMethod("FindSplash")!.Invoke(null, new object[] { champion, 103017 })
                == "/lol-game-data/assets/quest.jpg",
            "Quest skin tiers resolve exact artwork paths."
        );
        using var label = new LinkLabel
        {
            BackColor = Color.LightBlue,
            ForeColor = Color.Navy,
            LinkColor = Color.Blue,
        };
        view.Controls.Add(label);
        var image = new Bitmap(8, 8);
        Invoke(view, "SetArtwork", image);
        Invoke(view, "SetArtwork", image);
        Check(
            !Disposed(image) && label.BackColor == Color.Transparent && label.LinkColor == Color.White,
            "Reusing the same owned artwork does not dispose the current bitmap."
        );
        Invoke(view, "SetArtwork", (object?)null);
        Check(
            Disposed(image) && label.BackColor == Color.LightBlue && label.LinkColor == Color.Blue,
            "Removing artwork disposes the image and restores the original colors."
        );
        label.Dispose();
        var colors = Field<object>(view, "_colors");
        Check(
            (int)colors.GetType().GetProperty("Count")!.GetValue(colors)! == 0,
            "Rebuilt profile controls are not retained by the color cache."
        );
    }

    private static HttpResponseMessage Reply(JToken value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };

    private static void LateBackground()
    {
        using var view = Background();
        using var client = new HttpClient(
            new Handler(request =>
                Reply(
                    request.RequestUri!.AbsolutePath.Contains("summoner-profile")
                        ? new JObject
                        {
                            ["backgroundSkinId"] = request.RequestUri.Query.Contains("old") ? 103000 : 25000,
                        }
                        : JObject.Parse(
                            "{\"skins\":[{\"id\":103000,\"splashPath\":\"/lol-game-data/assets/old.jpg\"},"
                                + "{\"id\":25000,\"splashPath\":\"/lol-game-data/assets/new.jpg\"}]}"
                        )
                )
            )
        )
        {
            BaseAddress = new Uri("https://fixture.test/"),
        };
        SetField(view, "_clientProvider", (Func<HttpClient?>)(() => client));
        var release = new TaskCompletionSource<Image?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldImage = new Bitmap(8, 8);
        var currentImage = new Bitmap(16, 16);
        bool oldStarted = false;
        SetField(
            view,
            "_readArtwork",
            (Func<HttpClient, string, int, Task<Image?>>)(
                (_, _, skin) =>
                {
                    if (skin == 103000)
                    {
                        oldStarted = true;
                        return release.Task;
                    }
                    return Task.FromResult<Image?>(currentImage);
                }
            )
        );
        try
        {
            Invoke(view, "LoadPlayer", "old");
            PumpUntil(() => oldStarted);
            var source = Field<CancellationTokenSource>(view, "_request");
            Invoke(view, "LoadPlayer", "new");
            PumpUntil(() => ReferenceEquals(Field<Image?>(view, "_artwork"), currentImage));
            Check(source.Token.IsCancellationRequested, "Changing players cancels the old artwork waiter.");
            release.SetResult(oldImage);
            PumpUntil(() => Disposed(source));
            Check(
                Disposed(oldImage) && ReferenceEquals(Field<Image?>(view, "_artwork"), currentImage),
                "A late owned image is disposed without replacing the current player's background."
            );
        }
        finally
        {
            release.TrySetResult(oldImage);
            CloseBackground(view);
            oldImage.Dispose();
            currentImage.Dispose();
        }
    }

    private static Control Launcher() =>
        (Control)Activator.CreateInstance(Views.GetType("League.Controls.LolLauncherPanel")!)!;

    private static void LauncherSelectionAndClose()
    {
        using var view = Launcher();
        var release = new TaskCompletionSource<LolInstallation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var discovered = new LolInstallation("detected", "old.exe", null);
        var selected = new LolInstallation("selected", "official.exe", "wegame.exe");
        SetField(view, "_detectInstallation", (Func<Task<LolInstallation?>>)(() => release.Task));
        SetField(view, "_saveInstallation", (Func<string, Task<LolInstallation>>)(_ => Task.FromResult(selected)));
        var choose = Field<FlowLayoutPanel>(view, "_buttons").Controls.OfType<Button>().First();
        var detection = (Task)Invoke(view, "DetectInstallationAsync")!;
        var saving = (Task)Invoke(view, "SaveInstallationAsync", "selected", choose)!;
        PumpUntil(() => saving.IsCompleted);
        release.SetResult(discovered);
        PumpUntil(() => detection.IsCompleted);
        Check(
            ReferenceEquals(Field<LolInstallation>(view, "_installation"), selected),
            "Late automatic detection cannot replace an explicit directory selection."
        );
        Check(
            Field<TextBox>(view, "_path").ReadOnly && Field<Button>(view, "_wegame").Enabled,
            "Validated paths remain copyable and enable available launchers."
        );
        view.Width = 300;
        view.PerformLayout();
        var buttons = Field<FlowLayoutPanel>(view, "_buttons").Controls.Cast<Control>();
        Check(
            buttons.All(button => button.Right <= button.Parent!.ClientSize.Width),
            "Launcher buttons wrap within a narrow panel."
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetField(view, "_startLauncher", (Func<string, Task>)(_ => started.Task));
        var work = (Task)Invoke(view, "StartAsync", "official.exe")!;
        view.Dispose();
        started.SetResult();
        PumpUntil(() => work.IsCompleted);
        Check(
            work.IsCompletedSuccessfully,
            "A user-authorized launch can finish after the panel closes without updating disposed UI."
        );
    }

    private static void HighlightMissingData()
    {
        using var empty = new MatchInfo { AllParticipants = new() };
        using var view = new MatchDetailView(empty);
        Invoke(view, "AddMatchHighlights", empty);
        var scoreboard = Field<Panel>(view, "_scoreboard");
        Check(scoreboard.Controls.Count == 0, "Empty match data has no leaders and no highlight strip.");
        using var match = new MatchInfo
        {
            AllParticipants = new()
            {
                new JObject { ["totalDamageTaken"] = 123, ["largestKillingSpree"] = 5 },
                new JObject { ["totalDamageTaken"] = 123, ["largestKillingSpree"] = 3 },
            },
        };
        Invoke(view, "AddMatchHighlights", match);
        var cards = scoreboard.Controls["MatchHighlights"]!.Controls.Cast<Control>().ToArray();
        Check(
            cards.Length == 2 && Field<string>(cards[0], "_title").Contains("并列"),
            "Incomplete metrics are omitted while tied maxima are retained."
        );
        Check(Field<string>(cards[1], "_value") == "5", "A complete metric retains its actual maximum.");
    }
}
