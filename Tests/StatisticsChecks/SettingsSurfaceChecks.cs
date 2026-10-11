using System.Reflection;
using League.Controls;
using League.Models;

internal static class SettingsSurfaceChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        CacheLifetimeBoundaries();
        OverlayShell();
        Console.WriteLine(
            "PASS: cache lifetime bounds, separate save callbacks, overlay placement, focus preservation and dismiss routing."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void CacheLifetimeBoundaries()
    {
        using var layout = new FlowLayoutPanel();
        var add = typeof(League.FormMain).GetMethod(
            "AddCacheLifetimeSetting",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var saved = new List<(string Name, int Minutes)>();
        var inputs = new[] { ("统计", -1, 30, 1), ("平衡", 20000, 30, 10080), ("成就", 30, 10, 30) };
        foreach (var (name, value, increment, expected) in inputs)
        {
            var control = (NumericUpDown)
                add.Invoke(
                    null,
                    new object[]
                    {
                        layout,
                        name,
                        value,
                        increment,
                        "修改后保存，下次加载时生效。",
                        (Action<int>)(minutes => saved.Add((name, minutes))),
                    }
                )!;
            Check(
                control.Value == expected && control.Increment == increment,
                "Invalid cached settings are clamped while preserving each setting's increment."
            );
            Check(saved.Count == 0, "Creating controls does not overwrite saved configuration.");
        }
        var settings = layout.Controls.OfType<NumericUpDown>().ToArray();
        settings[0].Value = 1440;
        settings[1].Value = 720;
        settings[2].Value = 60;
        Check(
            saved.SequenceEqual(new[] { ("统计", 1440), ("平衡", 720), ("成就", 60) }),
            "Each edited setting invokes only its own persistence callback."
        );
        Check(layout.Controls.Count == 9, "Every setting retains its title, value and explanation.");
    }

    private static void OverlayShell()
    {
        var type = typeof(LoadingOverlayView).Assembly.GetType("League.LoadingOverlayForm")!;
        using var form = (Form)
            Activator.CreateInstance(
                type,
                new object[] { (Func<int, string>)(id => $"英雄 {id}"), (Func<LoadingPlayer, string>)(_ => "测试详情") }
            )!;
        var parameters = (CreateParams)type.GetProperty("CreateParams", PrivateInstance)!.GetValue(form)!;
        Check(
            (parameters.ExStyle & 0x08000000) != 0 && (parameters.ExStyle & 0x80) != 0,
            "The overlay remains a nonactivating tool window."
        );
        object[] message = { Message.Create(IntPtr.Zero, 0x0021, IntPtr.Zero, IntPtr.Zero) };
        type.GetMethod("WndProc", PrivateInstance)!.Invoke(form, message);
        Check(((Message)message[0]).Result == new IntPtr(3), "A mouse selection does not steal game keyboard focus.");
        var placement = type.GetMethod("Placement")!;
        foreach (var work in new[] { new Rectangle(-1920, 0, 1920, 1040), new Rectangle(0, 0, 800, 600) })
        {
            var bounds = (Rectangle)placement.Invoke(null, new object[] { new Rectangle(-1700, 50, 1280, 720), work })!;
            Check(work.Contains(bounds), "The overlay fits both negative-coordinate and smaller displays.");
        }
        int dismissals = 0;
        type.GetEvent("Dismissed")!.AddEventHandler(form, (Action)(() => dismissals++));
        var close = form.Controls.OfType<Panel>().Single().Controls.OfType<Button>().Single();
        typeof(Control).GetMethod("OnClick", PrivateInstance)!.Invoke(close, new object[] { EventArgs.Empty });
        Check(dismissals == 1, "The close button routes one dismissal to the overlay manager.");
        type.GetMethod("SetPreviewTitle")!.Invoke(form, null);
        var title = form.Controls.OfType<Panel>().Single().Controls.OfType<Label>().Single();
        Check(title.Text.Contains("显示测试"), "Preview mode still explains why the shell is displayed.");
    }
}
