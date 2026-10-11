internal static class StatisticsCheckSupport
{
    public static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static Control[] Children(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child))).ToArray();

    public static Form HiddenForm(Size size) =>
        new()
        {
            ClientSize = size,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            Opacity = 0,
        };

    public static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(ready(), "The simulated UI result completed within five seconds.");
    }
}
