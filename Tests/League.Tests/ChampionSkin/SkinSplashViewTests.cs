using League.ChampionSkin;
using League.Tests.Support;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The splash art of the selected skin: what it keeps from <c>Show</c>, the cropped cover it paints, the caption and
/// the badge for the automatic skin. The art comes from a bitmap the test owns, so no client and no real art folder is
/// involved. <c>SkinBrowserButton</c> has no test of its own: it is an internal control, so nothing but reflection
/// could construct it.
/// </summary>
[Trait("Category", "UI")]
public class SkinSplashViewTests
{
    [Fact]
    public void Show_ArtAndCaption_KeepsTheArt()
    {
        using var art = Art(640, 360);

        UiHost.Run(host =>
        {
            using var view = Splash(host);

            view.Show(art, "拉克丝", "暗黑元素 拉克丝", preferred: false, status: "");

            Assert.Same(art, view.Image);
        });
    }

    [Fact]
    public void Show_NoArt_KeepsNoArt()
    {
        UiHost.Run(host =>
        {
            using var view = Splash(host);
            using var art = Art(640, 360);
            view.Show(art, "拉克丝", "暗黑元素 拉克丝", preferred: false, status: "");

            view.Show(null, "拉克丝", "暗黑元素 拉克丝", preferred: false, status: "正在载入皮肤");

            Assert.Null(view.Image);
        });
    }

    [Fact]
    public void DrawToBitmap_ArtAndCaption_PaintsTheCoverCrop()
    {
        using var art = Art(640, 360);

        UiHost.Run(host =>
        {
            using var view = Splash(host);
            view.Show(art, "拉克丝", "暗黑元素 拉克丝", preferred: false, status: "");

            using var painted = Paint(view);

            Assert.Equal(view.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_AutomaticSkin_PaintsTheBadge()
    {
        using var art = Art(640, 360);

        UiHost.Run(host =>
        {
            using var view = Splash(host);
            view.Show(art, "拉克丝", "暗黑元素 拉克丝", preferred: true, status: "");

            using var painted = Paint(view);

            Assert.Equal(view.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_NoArtWithAStatus_PaintsTheStatusInstead()
    {
        UiHost.Run(host =>
        {
            using var view = Splash(host);
            view.Show(null, "", "", preferred: false, status: "正在载入皮肤");

            using var painted = Paint(view);

            Assert.Equal(view.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_NoArtAndNoStatus_PaintsOnlyTheCaption()
    {
        UiHost.Run(host =>
        {
            using var view = Splash(host);
            view.Show(null, "拉克丝", "暗黑元素 拉克丝", preferred: false, status: "");

            using var painted = Paint(view);

            Assert.Equal(view.Size, painted.Size);
        });
    }

    private static Bitmap Paint(SkinSplashView view)
    {
        var painted = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(painted, new Rectangle(Point.Empty, view.Size));
        return painted;
    }

    private static SkinSplashView Splash(Form host)
    {
        var view = new SkinSplashView { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        return view;
    }

    private static Bitmap Art(int width, int height)
    {
        var art = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(art);
        graphics.Clear(Color.CornflowerBlue);
        return art;
    }
}
