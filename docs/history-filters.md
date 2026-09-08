# 战绩筛选数据调查

当前项目通过 SGP `/match-history-query/v1/products/lol/player/{puuid}/SUMMARY` 获取记录，现有查询参数是 `startIndex`、`count`、模式 `tag=q_队列ID`。未确认服务端支持胜负条件，因此胜负在本地筛选。

- 比赛模式：`queueId`，可用现有服务端模式查询。
- 胜负：目标玩家 `puuid` 对应的 `win`，兼容 `participants[].stats.win` 和 `participants[].win`。缺失或无法匹配玩家时不算失败。
- 英雄：目标玩家 `championId`，可扩展英雄筛选。
- 时间、时长：`gameCreation`、`gameDuration`，可扩展时间范围及局长筛选。
- 表现：击杀、死亡、助攻、经济、伤害等，来自目标玩家数据；这不等于 WeGame 的 MVP/SVP 评价。

界面提供模式、全部/胜利/失败、每页场数。胜负模式按需读取，每次只请求当前页尚缺的场数，匹配够一页立即停止。下一页从上次原始记录偏移继续，上一页和胜负切换复用已读记录。末页不足一页仍显示；读到结尾后关闭下一页，不预先宣称总页数。仍沿用项目最近最多 200 条的浏览上限（并非接口宣称的上限）。面板展示实际读取数量和命中数量；不是只过滤当前页，也不是完整生涯。强制刷新清除游标和记录。重开若返回明确 win 字段按该结果归类，暂不单独筛选。

公开 LCU schema 的 LOL 历史查询只列出分页参数，未列出胜负参数：
https://lcu.kebs.dev/ （`/lol-match-history/v1/products/lol/{puuid}/matches`）

接口字段和队列可用性仍取决于客户端/地区。当前改动通过离线数据测试，实际联网筛选需在客户端中验证。
