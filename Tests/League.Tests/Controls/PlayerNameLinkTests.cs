using League.Controls;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// A player entry point: it shows the name, keeps the link only for a player the host can look up, tells the host
/// when it is clicked, and takes the privacy of the resolved profile into account. The lookup itself is a stub, so
/// nothing is requested over the network.
/// </summary>
[Trait("Category", "UI")]
public class PlayerNameLinkTests
{
    [Fact]
    public void HandleCreation_PlayerWithoutASummonerId_ShowsTheNameWithoutALink()
    {
        UiHost.Run(host =>
        {
            using var link = Link(host, "甲#0001", summonerId: "");

            Assert.Equal("甲#0001", link.Text);
            Assert.Empty(link.Links);
            Assert.False(link.TabStop);
        });
    }

    [Fact]
    public void HandleCreation_PlayerWithTheUnknownSummonerId_ShowsTheNameWithoutALink()
    {
        UiHost.Run(host =>
        {
            using var link = Link(host, "甲#0001", summonerId: "0");

            Assert.Empty(link.Links);
            Assert.False(link.TabStop);
        });
    }

    [Fact]
    public void Click_TheLink_AsksTheHostForThatPlayer()
    {
        UiHost.Run(host =>
        {
            using var link = Link(host, "甲#0001", summonerId: "7");
            string? requested = null;
            link.ProfileRequested += id => requested = id;
            Repaint(link);

            // A link only becomes the active one when the pointer moves onto it, the way it does on screen.
            WindowMessages.Move(link, new Point(4, link.Height / 2));
            WindowMessages.Click(link, new Point(4, link.Height / 2));

            Assert.Equal("7", requested);
        });
    }

    [Fact]
    public void HandleCreation_ResolvedPublicProfile_DescribesItAndHidesTheLockBadge()
    {
        UiHost.Run(async host =>
        {
            using var link = Link(host, "甲", summonerId: "7", profile: """{"privacy":"PUBLIC"}""");

            await WaitForResolved(link);

            Assert.Equal("甲，隐私：公开，点击查看主页", link.AccessibleDescription);
            Assert.False(Badge(link).Visible);
        });
    }

    [Fact]
    public void Click_TheLockBadgeOfAHiddenProfile_AsksTheHostForThatPlayer()
    {
        UiHost.Run(async host =>
        {
            using var link = Link(host, "甲", summonerId: "7", profile: """{"privacy":"PRIVATE"}""");
            string? requested = null;
            link.ProfileRequested += id => requested = id;
            await WaitForResolved(link);

            WindowMessages.Click(Badge(link), new Point(4, Badge(link).Height / 2));

            Assert.Equal("7", requested);
        });
    }

    [Fact]
    public void HandleCreation_ProfileLookupFails_KeepsThePlainName()
    {
        UiHost.Run(host =>
        {
            using var link = Link(host, "甲", summonerId: "7", failing: true);

            Assert.Equal("甲", link.Text);
            Assert.Null(link.AccessibleDescription);
        });
    }

    private static async Task WaitForResolved(PlayerNameLink link)
    {
        for (int attempt = 0; attempt < 200 && string.IsNullOrEmpty(link.AccessibleDescription); attempt++)
        {
            await Task.Delay(5);
        }
    }

    private static Label Badge(PlayerNameLink link) => link.Controls.OfType<Label>().Single();

    private static PlayerNameLink Link(
        Form host,
        string name,
        string summonerId,
        string? profile = null,
        bool failing = false
    )
    {
        var link = new PlayerNameLink
        {
            PlayerName = name,
            SummonerId = summonerId,
            ResolveProfile = _ =>
                failing ? throw new InvalidOperationException("没有可用的客户端")
                : profile == null ? Task.FromResult<JObject?>(null)
                : Task.FromResult<JObject?>(JObject.Parse(profile)),
            Width = 200,
            Height = 24,
        };
        host.Controls.Add(link);
        return link;
    }

    private static Bitmap Repaint(Control control)
    {
        var painted = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(painted, new Rectangle(Point.Empty, control.Size));
        return painted;
    }
}
