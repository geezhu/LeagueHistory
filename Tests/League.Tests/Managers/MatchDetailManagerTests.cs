using League.Managers;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Managers;

/// <summary>
/// Which queues have a detail page. The rule is a fixed list of standard five-a-side queues - the modes whose games
/// really have ten players and a scoreboard worth opening - and everything else is refused, including the arena and
/// the one-lane modes.
///
/// The manager needs a main window to exist, which the startup options make cheap: nothing is polled, no hotkey is
/// registered and the shutdown plan stays in memory. The predicate itself touches none of that.
/// </summary>
public class MatchDetailManagerTests : IClassFixture<MatchDetailManagerTests.Window>
{
    /// <summary>One window for the whole class, built once on a UI thread and never shown.</summary>
    public sealed class Window : IDisposable
    {
        public Window()
        {
            UiHost.Run(host =>
            {
                Main = new FormMain(
                    new FormMainOptions
                    {
                        StartBackgroundServices = false,
                        StartGameIntegration = false,
                        ShutdownTasks = new NoShutdownTask(),
                    }
                );
                Manager = new MatchDetailManager(Main);
            });
        }

        public FormMain Main { get; private set; } = null!;

        public MatchDetailManager Manager { get; private set; } = null!;

        public void Dispose()
        {
            UiHost.Run(_ => Main.Dispose());
        }
    }

    private readonly MatchDetailManager _manager;

    public MatchDetailManagerTests(Window window) => _manager = window.Manager;

    /// <summary>A store that keeps the plan in memory, so no Windows task is read or written.</summary>
    private sealed class NoShutdownTask : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Read() => null;

        public void Register(string xml) => throw new NotSupportedException("The tests never register a plan.");

        public void Stop() => throw new NotSupportedException("The tests never stop a plan.");

        public void Delete() { }

        public void SetEnabled(bool enabled) => throw new NotSupportedException("The tests never enable a plan.");
    }

    // ---- the queues that have a page ----

    [Theory]
    [InlineData("q_420")] // 单双排
    [InlineData("q_440")] // 灵活排位
    [InlineData("q_400")] // 匹配
    [InlineData("q_430")] // 匹配（经典）
    [InlineData("q_450")] // 极地大乱斗
    [InlineData("q_900")] // 无限火力
    [InlineData("q_1900")] // 无限火力（旧）
    [InlineData("q_480")] // 快速模式
    [InlineData("q_2400")] // 海克斯大乱斗
    [InlineData("q_2450")] // 经典海克斯大乱斗
    [InlineData("q_3270")] // 自定义海克斯大乱斗
    [InlineData("q_830")] // 人机
    [InlineData("q_850")] // 人机
    [InlineData("q_950")] // 末日人机
    [InlineData("q_1020")] // 克隆大作战
    [InlineData("q_1300")] // 极限闪击
    [InlineData("q_1400")] // 终极魔典
    [InlineData("q_3100")] // 自定义峡谷
    [InlineData("q_3140")] // 训练模式
    [InlineData("q_4310")] // 经典
    public void IsSummonersRiftOrAram_AStandardFiveASideQueue_IsSupported(string queueTag)
    {
        Assert.True(_manager.IsSummonersRiftOrAram(queueTag), $"'{queueTag}' should have a detail page.");
    }

    // ---- the queues that do not ----

    [Theory]
    [InlineData("q_1700")] // 斗魂竞技场：双人，没有十人计分板
    [InlineData("q_1710")]
    [InlineData("q_100")] // 旧的一路模式
    [InlineData("q_1200")]
    [InlineData("q_9999")]
    [InlineData("420")]
    [InlineData("")]
    public void IsSummonersRiftOrAram_AnythingElse_IsRefused(string queueTag)
    {
        Assert.False(_manager.IsSummonersRiftOrAram(queueTag), $"'{queueTag}' should have no detail page.");
    }

    [Fact]
    public void IsSummonersRiftOrAram_NoTagAtAll_IsRefused()
    {
        // An empty tag is the answer for a game the list has not been told about yet.
        Assert.False(_manager.IsSummonersRiftOrAram(null!));
    }

    [Fact]
    public void IsSummonersRiftOrAram_TheTagIsMatchedExactly()
    {
        // The list holds the client's own spelling; a differently-cased or padded tag is not the same queue.
        Assert.False(_manager.IsSummonersRiftOrAram("Q_420"));
        Assert.False(_manager.IsSummonersRiftOrAram(" q_420"));
        Assert.False(_manager.IsSummonersRiftOrAram("q_420 "));
    }
}
