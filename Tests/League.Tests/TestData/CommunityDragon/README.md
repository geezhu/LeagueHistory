# CommunityDragon 样例

- `annie.bin.json`：`https://raw.communitydragon.org/latest/game/data/characters/annie/annie.bin.json`，2026-10-18 取自 16.20 版本，原样保存。
- `annie.stringtable.json`：同一版本 `game/zh_cn/data/menu/en_us/lol.stringtable.json` 里安妮的五个技能说明，外加一条 `generatedtip_` 和一条技能名，用来验证只保留技能说明。
- `Augments/kiwi.bin.json`：`https://raw.communitydragon.org/latest/game/maps/modespecificdata/kiwi.bin.json`，2026-10-21 取自 16.21 版本，只留下「将他们放倒倒倒」（3116）和「泰坦的坚决」（1001）两个强化的 `AugmentData` 及其技能的数值与公式。
- `Augments/lol.stringtable.json`：同一版本中文文本表里这两个强化的名称、简介和详细提示，其中「将他们放倒倒倒」的三条按 XXH3 哈希键保存，「泰坦的坚决」的按名称保存。

数值和说明随版本变化；测试只断言从这些文件读出的结果，不依赖线上数据。
