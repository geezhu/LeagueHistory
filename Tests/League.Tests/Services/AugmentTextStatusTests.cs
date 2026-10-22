using League.Services;

namespace League.Tests.Services;

public class AugmentTextStatusTests
{
    [Fact]
    public void Describe_Downloading_ShowsThePatchAndPercent()
    {
        var status = new AugmentTextStatus();

        status.Downloading("16.21", 0.42);

        Assert.Equal("正在下载 16.21 版海克斯说明（42%），完成前显示内置说明", status.Describe());
    }

    [Fact]
    public void Describe_Ready_ShowsThePatchAndCount()
    {
        var status = new AugmentTextStatus();

        status.Ready("16.21", 248);

        Assert.Equal("16.21 版 · 248 条 · 已就绪", status.Describe());
    }

    [Fact]
    public void Failed_WithoutAPatch_KeepsTheLastOne()
    {
        var status = new AugmentTextStatus();
        status.Downloading("16.21", 0.5);

        status.Failed(null);

        Assert.Equal("16.21", status.Patch);
    }

    [Fact]
    public void Downloading_RaisesChanged()
    {
        var status = new AugmentTextStatus();
        int raised = 0;
        status.Changed += () => raised++;

        status.Downloading("16.21", 0);

        Assert.Equal(1, raised);
    }
}
