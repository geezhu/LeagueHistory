using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Forms;

/// <summary>
/// The end-of-game panel driven through its buttons with window messages. Sending goes to a recorder instead of the
/// client, so no line ever reaches a real chat.
/// </summary>
[Trait("Category", "UI")]
public class EndOfGamePanelFormTests
{
    [Fact]
    public void RowButton_Win_SendsThatTeammatesThumbsUp()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder();
            using var panel = Show(Summary(win: true), sender);

            Click(panel, "endOfGameRowButton1");

            Assert.Equal(["和乙这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory"], sender.Lines);
            Assert.Equal("已赞", Find<Button>(panel, "endOfGameRowButton1").Text);
            Assert.False(Find<Button>(panel, "endOfGameRowButton1").Enabled);
        });
    }

    [Fact]
    public void HonorAll_AfterOneThumbsUp_SendsTheRestInOneLine()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder();
            using var panel = Show(Summary(win: true), sender);
            Click(panel, "endOfGameRowButton0");

            Click(panel, "endOfGameHonorAll");

            Assert.Equal(
                [
                    "和甲这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
                    "和乙、丙这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory",
                ],
                sender.Lines
            );
            Assert.Equal("已全赞", Find<Button>(panel, "endOfGameHonorAll").Text);
        });
    }

    [Fact]
    public void HonorAll_EveryoneThanked_SendsNothingMore()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder();
            using var panel = Show(Summary(win: true), sender);
            Click(panel, "endOfGameHonorAll");

            Click(panel, "endOfGameHonorAll");

            Assert.Equal(["和甲、乙、丙这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory"], sender.Lines);
        });
    }

    [Fact]
    public void RowButton_WinWithTheChatClosed_KeepsTheRowAndSaysWhy()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder(PostGameSendResult.NoConversation);
            using var panel = Show(Summary(win: true), sender);

            Click(panel, "endOfGameRowButton0");

            Assert.Equal("点赞", Find<Button>(panel, "endOfGameRowButton0").Text);
            Assert.True(Find<Button>(panel, "endOfGameRowButton0").Enabled);
            Assert.Equal("结算聊天已关闭，未发送", Find<Label>(panel, "endOfGameStatus").Text);
        });
    }

    [Fact]
    public void RowButton_Loss_OnlySelectsAndTurnsTheFooterIntoSend()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder();
            using var panel = Show(Summary(win: false), sender);

            Click(panel, "endOfGameRowButton2");

            Assert.Empty(sender.Lines);
            Assert.Equal("已选", Find<Button>(panel, "endOfGameRowButton2").Text);
            Assert.Equal("发送", Find<Button>(panel, "endOfGameFooter").Text);
        });
    }

    [Fact]
    public void Footer_LossWithSelection_SendsOneLineAndCloses()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder();
            using var panel = Show(Summary(win: false), sender);
            Click(panel, "endOfGameRowButton0");
            Click(panel, "endOfGameRowButton2");

            Click(panel, "endOfGameFooter");

            Assert.Equal(["不怕神一样的对手，就怕甲、丙一样的队友"], sender.Lines);
            Assert.True(panel.IsDisposed);
        });
    }

    [Fact]
    public void Footer_LossWithoutSelection_ClosesWithoutSending()
    {
        UiHost.Run(_ =>
        {
            var sender = new SendRecorder();
            using var panel = Show(Summary(win: false), sender);

            Click(panel, "endOfGameFooter");

            Assert.Empty(sender.Lines);
            Assert.True(panel.IsDisposed);
        });
    }

    [Fact]
    public void SelfRow_Win_HoldsHonorAllAboveTheTeammates()
    {
        UiHost.Run(_ =>
        {
            using var panel = Show(Summary(win: true), new SendRecorder());

            var selfRow = Find<Panel>(panel, "endOfGameSelfRow");

            Assert.Same(selfRow, Find<Button>(panel, "endOfGameHonorAll").Parent);
            Assert.True(selfRow.Bottom <= Find<Panel>(panel, "endOfGameRow0").Top);
        });
    }

    [Fact]
    public void HonorAll_Loss_IsHidden()
    {
        UiHost.Run(_ =>
        {
            using var panel = Show(Summary(win: false), new SendRecorder());

            Assert.False(Find<Button>(panel, "endOfGameHonorAll").Visible);
        });
    }

    [Fact]
    public void RowButton_UnnamedTeammate_IsDisabled()
    {
        UiHost.Run(_ =>
        {
            using var panel = Show(Summary(win: true, "甲", "", "丙"), new SendRecorder());

            Assert.False(Find<Button>(panel, "endOfGameRowButton1").Enabled);
        });
    }

    [Fact]
    public void Close_WhileSending_CancelsTheSend()
    {
        UiHost.Run(async _ =>
        {
            var pending = new TaskCompletionSource<PostGameSendResult>();
            CancellationToken sendToken = default;
            var panel = Show(
                Summary(win: true),
                (_, token) =>
                {
                    sendToken = token;
                    return pending.Task;
                }
            );
            Click(panel, "endOfGameRowButton0");

            panel.Close();
            pending.SetResult(PostGameSendResult.Sent);
            await Task.Yield();

            Assert.True(sendToken.IsCancellationRequested);
            Assert.True(panel.IsDisposed);
        });
    }

    [Fact]
    public void ShowMastery_ReplacesTheDashes()
    {
        UiHost.Run(_ =>
        {
            using var panel = Show(Summary(win: true), new SendRecorder());

            panel.ShowMastery("186,321");

            Assert.Equal("186,321", panel.MasteryText);
        });
    }

    [Fact]
    public void DrawToBitmap_MvpOnTheAvatar_Paints()
    {
        UiHost.Run(_ =>
        {
            var score = new OwnMatchScore(14.3, MatchHonour.Mvp);
            using var panel = Show(Summary(win: true) with { Score = score }, new SendRecorder());
            using var bitmap = new Bitmap(panel.Width, panel.Height);

            panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, panel.Size));

            Assert.Equal(panel.Size, bitmap.Size);
        });
    }

    private static EndOfGamePanelForm Show(EndOfGameSummary summary, SendRecorder sender) =>
        Show(summary, sender.SendAsync);

    private static EndOfGamePanelForm Show(
        EndOfGameSummary summary,
        Func<string, CancellationToken, Task<PostGameSendResult>> send
    )
    {
        // Off screen: the panel is top-most and would otherwise flash over the developer's desktop.
        var panel = new EndOfGamePanelForm(new EndOfGamePanelState(summary), send)
        {
            Location = new Point(-3000, -3000),
        };
        panel.Show();
        return panel;
    }

    private static void Click(Form panel, string name)
    {
        var button = Find<Button>(panel, name);
        WindowMessages.Click(button, new Point(button.Width / 2, button.Height / 2));
    }

    private static T Find<T>(Control root, string name)
        where T : Control =>
        Assert.IsAssignableFrom<T>(Assert.Single(root.Controls.Find(name, searchAllChildren: true)));

    private static EndOfGameSummary Summary(bool win, params string[] names) =>
        new(
            1,
            "本人",
            win,
            TimeSpan.FromMinutes(16),
            1,
            new EndOfGameKda(9, 8, 19),
            null,
            [],
            (names.Length == 0 ? ["甲", "乙", "丙"] : names)
                .Select(name => new EndOfGameTeammate(name, 2, new EndOfGameKda(1, 2, 3), null, []))
                .ToArray()
        );

    private sealed class SendRecorder(PostGameSendResult result = PostGameSendResult.Sent)
    {
        public List<string> Lines { get; } = new();

        public Task<PostGameSendResult> SendAsync(string line, CancellationToken token)
        {
            Lines.Add(line);
            return Task.FromResult(result);
        }
    }
}
