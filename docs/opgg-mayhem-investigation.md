# OP.GG 海克斯大乱斗接口调查

调查日期：2026-09-08。仅调查，未改动模式映射。

## 已验证

- 官方页面：https://op.gg/lol/modes/aram-mayhem/ahri/build ，HTTP 200，页面数据版本 16.17。
- 页面公开 JavaScript 的模式映射为 `aram-mayhem -> aram_mayhem`，另有独立的 `aram_mayhem_classic`，不可混用。
- 页面 HTML 内的 Next.js Flight 数据含英雄 ID、梯队、排名、装备 ID，以及强化符文的 metaId、名称和图标。排名数据出现 173 个 champion_id。
- 页面源码对 `aram_mayhem` 隐藏部分胜率/选用率组件；不能假定页面提供标准模式的全部统计字段。
- 旧 JSON 接口 `https://lol-api-champion.op.gg/api/global/champions/aram_mayhem?tier=all` 返回 HTTP 422，正文 `{"message":"Mode was invalid"}`。
- 同模式英雄路径 `/api/global/champions/aram_mayhem/103/none?tier=all` 也未成功。

## 结论

OP.GG 有此模式的攻略数据，但尚未找到可直接替换现有客户端请求路径的独立 JSON API。浏览器公开脚本没有暴露对应后端地址，页面以服务器渲染数据下发。

可实施备选是专门读取官方页面的结构化渲染数据，转换为独立模型；这种方式依赖页面内部结构，应加缓存、样本回归和字段缺失处理。不可将普通 ARAM 数据标成海克斯，也不可把缺失胜率填成 0。

本地调查样本在忽略目录 bin：opgg-mayhem-page.html、mayhem-flight.txt、opgg-script-*.js。未将第三方整页脚本加入源代码。
