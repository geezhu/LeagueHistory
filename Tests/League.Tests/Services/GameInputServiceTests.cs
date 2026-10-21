using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The service that types a message into the game's chat box, and the gates that stop it.
///
/// Every test here stops before a key is pressed. The conditions are checked in a fixed order - the caller's own
/// "may I continue" first, then the administrator permission - and this file relies on that order: a callback that
/// always says no stops the call whatever the elevation of the machine running the tests.
///
/// The gate matters as much as the message. It serialises sends so two of them cannot interleave keystrokes, and it
/// has to be released when a send stops early - otherwise one refused message would block every later one.
/// </summary>
public class GameInputServiceTests
{
    /// <summary>A message that is not empty, so the call gets past the first guard.</summary>
    private const string Message = "测试不会实际输入";

    // ---- the guards that run before anything is typed ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public async Task SendInGameMessageAsync_AnEmptyMessage_IsRefusedWithAReason(string message)
    {
        // Nothing to send is checked before the gate is taken, so the reason is the plain one.
        GameInputService input = new();

        Assert.False(await input.SendInGameMessageAsync(message));
        Assert.Equal("消息为空", input.LastFailureReason);
    }

    [Fact]
    public async Task SendInGameMessageAsync_TheCallerSaysStop_IsRefusedWithItsOwnReason()
    {
        // The caller's gate is checked before the permission one, which is why this is safe to run anywhere.
        GameInputService input = new();

        Assert.False(await input.SendInGameMessageAsync(Message, canContinue: () => false));
        Assert.Equal("发送条件已变化，停止输入", input.LastFailureReason);
    }

    [Fact]
    public async Task SendInGameMessageAsync_TheCallerSaysStop_IsNotThePermissionMessage()
    {
        // The two refusals call for different actions, so they must not be confused: one means "the game moved on",
        // the other means "run as administrator".
        GameInputService input = new();

        await input.SendInGameMessageAsync(Message, canContinue: () => false);

        Assert.NotEqual("请以管理员身份重新启动 League，再使用游戏内发送", input.LastFailureReason);
    }

    // ---- the gate ----

    [Fact]
    public async Task SendInGameMessageAsync_AfterARefusedSend_TheGateIsFreeAgain()
    {
        // Traced: the first call stops inside the guarded region, and the finally block releases the gate. A second
        // call must therefore report its own reason rather than "another message is using the channel".
        GameInputService input = new();

        await input.SendInGameMessageAsync(Message, canContinue: () => false);
        await input.SendInGameMessageAsync(Message, canContinue: () => false);

        Assert.Equal("发送条件已变化，停止输入", input.LastFailureReason);
    }

    [Fact]
    public async Task SendInGameMessageAsync_ManyRefusedSends_KeepWorking()
    {
        // A gate leaked once would refuse everything after it; ten rounds make that visible.
        GameInputService input = new();

        for (int i = 0; i < 10; i++)
        {
            Assert.False(await input.SendInGameMessageAsync(Message, canContinue: () => false));
            Assert.Equal("发送条件已变化，停止输入", input.LastFailureReason);
        }
    }

    // ---- the reason is about the latest attempt ----

    [Fact]
    public async Task LastFailureReason_IsClearedWhenTheNextAttemptStarts()
    {
        // The message shown to the user belongs to the latest attempt, not to any earlier one.
        GameInputService input = new();
        await input.SendInGameMessageAsync(Message, canContinue: () => false);

        Assert.Equal("发送条件已变化，停止输入", input.LastFailureReason);

        // An attempt that fails even earlier replaces it with its own reason.
        await input.SendInGameMessageAsync("");

        Assert.Equal("消息为空", input.LastFailureReason);
    }

    // ---- the permission query ----

    [Fact]
    public void HasInputPermission_IsAQueryAndKeepsItsAnswer()
    {
        // It reads the process token; two calls must agree, whichever answer this machine gives.
        bool first = GameInputService.HasInputPermission();
        bool second = GameInputService.HasInputPermission();

        Assert.Equal(first, second);
    }
}
