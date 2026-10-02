using System.Globalization;
using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record ChampionMasteryPage(JArray? Data, DateTimeOffset? FetchedAt, string Message = "", bool Stale = false)
{
    public PlayerChampionMastery ForChampion(int champion)
    {
        var record = Data?.OfType<JObject>().FirstOrDefault(r => (int?)r["championId"] == champion);
        return new(champion, (int?)record?["championLevel"], (long?)record?["championPoints"],
            (long?)record?["lastPlayTime"], Data != null, Stale, FetchedAt, Message);
    }
}

/// <summary>Account achievement, separate from a champion's mode statistics.</summary>
public sealed record PlayerChampionMastery(int ChampionId, int? Level, long? Points, long? LastPlayTime,
    bool Available, bool Stale = false, DateTimeOffset? FetchedAt = null, string Message = "")
{
    public static PlayerChampionMastery Unavailable(int hero) => new(hero, null, null, null, false, Message: "玩家身份未公开，无法查询英雄成就。");
    public string Text => !Available ? Message.Contains("未公开") ? "成就未公开" : "成就暂不可用"
        : Points == null ? "暂无成就记录"
        : "成就 " + (Level.HasValue ? $"{Level}级 · " : "") + Compact(Points.Value) + (Stale ? " · 缓存" : "");
    public string Description
    {
        get
        {
            if (!Available) return Message;
            string text = Points.HasValue ? $"英雄成就：{(Level.HasValue ? Level + " 级 · " : "")}{Points:N0} 点" : "客户端未返回该英雄的成就记录。";
            if (LastPlayTime is > 0 && LastPlayTime <= 253402300799999)
                text += "\n最近使用：" + DateTimeOffset.FromUnixTimeMilliseconds(LastPlayTime.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            if (FetchedAt.HasValue) text += "\n数据更新：" + FetchedAt.Value.ToLocalTime().ToString("MM-dd HH:mm");
            if (Stale) text += "\n正在使用旧缓存" + (Message.Length > 0 ? "：" + Message : "，后台检查更新。");
            return text + "\n成就为该玩家的长期积累，不区分游戏模式。";
        }
    }
    private static string Compact(long value) => value >= 100000000 ? (value / 100000000d).ToString("0.#", CultureInfo.InvariantCulture) + "亿"
        : value >= 10000 ? (value / 10000d).ToString("0.#", CultureInfo.InvariantCulture) + "万" : value.ToString("N0", CultureInfo.CurrentCulture);
}
