# 统计与时间线

## 数据范围

- 主页首先请求 `GET /lol-career-stats/v1/summoner-games/{puuid}`。当前只解析带完整比赛记录的数组、`games` 或 `games.games`，不猜测未知聚合字段的含义。
- 若接口未提供可识别的记录，读取 LCU 最近 100 条历史记录；LCU 历史接口不可用时，可使用项目原有的 SGP 查询回调。该请求独立于右侧列表分页与筛选。
- 接口返回 401/403 时不尝试其他数据源。未连接、超时、无记录和不支持的结构保留提示和重试入口。
- 不宣称终身统计：即使来自 career-stats，也标注“接口返回范围”；历史回退明确标注“最近最多 100 条”。尚未确认国服提供 WeGame 的终身总场次、MVP/SVP 或终身纪录。
- 按比赛 ID 去重，排除明确标记的重开；使用 PUUID/participantIdentities 定位玩家。没有身份的单玩家 LCU 历史记录按所查询玩家处理，明确属于其他玩家的记录不采用。
- KDA 为累计（击杀＋助攻）/ max(1, 累计死亡)，缺少字段则显示 `—`。伤害、经济最高值与五杀次数同样要求当前范围的相关字段完整。

## 时间线

`GET /lol-match-history/v1/game-timelines/{gameId}` 按需加载。支持 frames 及 json/info 包装，读取 participantFrames 和 events。

- 经济使用比赛参与者的 participantId → teamId 映射，不按 ID 大小猜测阵营。映射或某帧金钱不完整时不连该区间。
- 展示击杀英雄、史诗野怪、防御塔和水晶事件。未知击杀者 ID 保留玩家编号；0 显示环境。
- 滑块按帧选择时间，点击事件定位到最近的前一帧；事件支持击杀/地图目标筛选，提供接口返回的位置坐标。
- 切换比赛或销毁控件取消请求；失败不缓存成空数据，可重新加载。

## 验证

公开结构参考：https://www.mingweisamuel.com/lcu-schema/lcu/openapi.json

本机调查未能读取有效 LCU 认证参数，因此接口在当前国服版本的实时可用性尚未验证。自动检查使用模拟响应，不能替代客户端实测。

运行：`dotnet run --project Tests/StatisticsChecks/StatisticsChecks.csproj -c Release`

覆盖去重、重开、身份匹配、缺失字段、SGP 包装、队伍映射、缺帧、事件、请求取消、403、统计范围提示和 WinForms 展示。生成的预览在测试项目 bin 目录，不提交。
