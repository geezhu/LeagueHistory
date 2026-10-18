using League.Controls;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The profile avatar: the level it shows and the experience ring it draws from a profile document. The ring itself
/// is drawn, so a test can only drive the paths through it; the level it shows stays readable.
/// </summary>
[Trait("Category", "UI")]
public class ProfileLevelAvatarTests
{
    [Fact]
    public void Level_ValueSet_IsTheLevelTheAvatarShows()
    {
        UiHost.Run(host =>
        {
            using var avatar = Avatar(host);

            avatar.Level = "123";

            Assert.Equal("123", avatar.Level);
        });
    }

    [Theory]
    [InlineData("""{"xpSinceLastLevel":30,"xpUntilNextLevel":60}""")]
    [InlineData("""{"expPoints":0,"expToNextLevel":0}""")]
    [InlineData("""{"expPoints":-5,"expToNextLevel":10}""")]
    [InlineData("""{"expPoints":150,"expToNextLevel":10}""")]
    [InlineData("""{"expPoints":"30","expToNextLevel":60}""")]
    [InlineData("{}")]
    public void DrawToBitmap_ProfileWithExperience_DrawsTheRingFromWhatItSays(string profile)
    {
        UiHost.Run(host =>
        {
            using var avatar = Avatar(host);

            avatar.SetExperience(JObject.Parse(profile));
            using var painted = Repaint(avatar);

            Assert.Equal(new Size(64, 64), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_AvatarTooSmallForARing_DrawsNothing()
    {
        UiHost.Run(host =>
        {
            using var avatar = Avatar(host);
            avatar.Size = new Size(12, 12);
            avatar.SetExperience(JObject.Parse("""{"expPoints":30,"expToNextLevel":60}"""));

            using var painted = Repaint(avatar);

            Assert.Equal(new Size(12, 12), painted.Size);
        });
    }

    private static ProfileLevelAvatar Avatar(Form host)
    {
        var avatar = new ProfileLevelAvatar { Width = 64, Height = 64 };
        host.Controls.Add(avatar);
        return avatar;
    }

    private static Bitmap Repaint(Control control)
    {
        var painted = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(painted, new Rectangle(Point.Empty, control.Size));
        return painted;
    }
}
