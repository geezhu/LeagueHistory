using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The card in the middle of an empty page while there is no client or no match. Its parts are direct children
/// because the window's status handling finds the card by a label's text and by its loading bar. The not-connected
/// card is never shown: showing it would let its launcher panel look for the real game folder.
/// </summary>
[Trait("Category", "UI")]
public class ClientStatusCardTests
{
    [Theory]
    [InlineData(ClientStatus.NotConnected, true)]
    [InlineData(ClientStatus.WaitingForGame, false)]
    public void New_Status_OffersLaunchersOnlyWithoutAClient(ClientStatus status, bool launch)
    {
        UiHost.Run(_ =>
        {
            using var card = new ClientStatusCard(status);

            Assert.Equal(launch, card.OffersLaunch);
        });
    }

    [Theory]
    [InlineData(ClientStatus.NotConnected, "客户端")]
    [InlineData(ClientStatus.WaitingForGame, "正在等待加入游戏")]
    public void New_Status_SaysWhatItIsWaitingFor(ClientStatus status, string words)
    {
        UiHost.Run(_ =>
        {
            using var card = new ClientStatusCard(status);

            var labels = card.Controls.OfType<Label>().Select(label => label.Text);

            Assert.Contains(labels, text => text.Contains(words, StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(ClientStatus.NotConnected)]
    [InlineData(ClientStatus.WaitingForGame)]
    public void New_Status_WaitsWithALoadingBar(ClientStatus status)
    {
        UiHost.Run(_ =>
        {
            using var card = new ClientStatusCard(status);

            var bars = card.Controls.OfType<ProgressBar>();

            Assert.IsType<LoadingBar>(Assert.Single(bars));
        });
    }

    [Theory]
    [InlineData(ClientStatus.NotConnected)]
    [InlineData(ClientStatus.WaitingForGame)]
    public void New_Status_KeepsEveryPartInsideTheCard(ClientStatus status)
    {
        UiHost.Run(_ =>
        {
            using var card = new ClientStatusCard(status);

            Assert.All(
                card.Controls.Cast<Control>(),
                part => Assert.True(part.Right <= card.Width && part.Bottom <= card.Height, part.GetType().Name)
            );
        });
    }

    [Fact]
    public void New_NotConnected_IsTallerThanWaitingBecauseOfTheLaunchers()
    {
        UiHost.Run(_ =>
        {
            using var waiting = new ClientStatusCard(ClientStatus.WaitingForGame);
            using var missing = new ClientStatusCard(ClientStatus.NotConnected);

            Assert.True(missing.Height > waiting.Height, $"{missing.Height} <= {waiting.Height}");
        });
    }
}
