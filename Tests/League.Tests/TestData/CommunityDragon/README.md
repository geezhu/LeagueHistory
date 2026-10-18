# CommunityDragon 样例

- `annie.bin.json`：`https://raw.communitydragon.org/latest/game/data/characters/annie/annie.bin.json`，2026-10-18 取自 16.20 版本，原样保存。
- `annie.stringtable.json`：同一版本 `game/zh_cn/data/menu/en_us/lol.stringtable.json` 里安妮的五个技能说明，外加一条 `generatedtip_` 和一条技能名，用来验证只保留技能说明。

数值和说明随版本变化；测试只断言从这两个文件读出的结果，不依赖线上数据。
