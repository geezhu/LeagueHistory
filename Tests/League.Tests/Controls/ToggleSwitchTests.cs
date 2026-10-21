using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// A text-less check box drawn as a switch: it takes the switch's size and still behaves as the same check box, so a
/// click flips it and drawing it does not fail in either state.
/// </summary>
[Trait("Category", "UI")]
public class ToggleSwitchTests
{
    [Fact]
    public void Apply_TextlessCheckBox_TakesTheSwitchSize()
    {
        UiHost.Run(host =>
        {
            var check = Switch(host);

            Assert.False(check.AutoSize);
            Assert.Equal(ToggleSwitch.SwitchSize, check.Size);
        });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Click_AppliedSwitch_FlipsChecked(bool before, bool after)
    {
        UiHost.Run(host =>
        {
            var check = Switch(host);
            check.Checked = before;

            WindowMessages.Click(check, new Point(20, 11));

            Assert.Equal(after, check.Checked);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawToBitmap_EitherState_PaintsTheWholeSwitch(bool on)
    {
        UiHost.Run(host =>
        {
            var check = Switch(host);
            check.Checked = on;
            using var painted = new Bitmap(check.Width, check.Height);

            check.DrawToBitmap(painted, new Rectangle(Point.Empty, check.Size));

            Assert.Equal(check.Size, painted.Size);
        });
    }

    private static CheckBox Switch(Form host)
    {
        var check = new CheckBox { Text = "" };
        host.Controls.Add(check);
        ToggleSwitch.Apply(check);
        return check;
    }
}
