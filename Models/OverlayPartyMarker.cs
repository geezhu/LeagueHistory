namespace League.Models;

public sealed record OverlayPartyMarker(int Size, int Color)
{
    public string Badge => $"{Size}人";
    public string Label => $"疑似{Size}人组";
    public string Description =>
        Label
        + "。\n根据近期至少两场同队战绩的玩家关联推测，不能确认当前实际组队。\n未标记可能是战绩不足或身份未公开。";
}
