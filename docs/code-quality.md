# 新增代码的规范与检查

本规范只作用于 geezhu 新增的源码。官方原有文件即使在本分支改过，也保持原有格式，避免后续合并产生无关差异。

## 范围

`config/code-quality.json` 记录官方仓库的原有源码路径。初始基线是 `mizi6654/LeagueHistory` 的 `42db0c3c437d9db5bb54fd502dbf5c08e7db975b`，共 98 个文件。依据路径和上游树判断归属，不依据提交作者。

- `upstreamFiles`：始终排除的原有文件；上游后来删除文件也不会自动解除排除。
- `adoptedFiles`：已经整理完的样板，每次检查都验证。
- 默认检查范围：样板，加本次修改或新增、且不在原有文件清单中的源码。
- `-All`：调查全部新增源码的待整理问题，不会扩大到官方原有源码。
- 纯格式整理不能修改原有文件。功能开发必须修改原有文件时，保留它的局部风格。

`AGENTS.md` 规定职责、状态、异常与异步资源管理要求。`.editorconfig` 对原有路径逐项取消设置；根目录文件的规则以 `/` 开头，避免误排除子目录里同名的新增文件。`.csharpierignore` 排除原有文件、生成文件和项目 XML。配置由范围更新脚本生成，不手改生成结果。

## 使用

在仓库根目录执行：

```powershell
# 检查排版，并编译检查选中源码的质量规则
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Check-CodeQuality.ps1

# 只整理选中新增源码的排版，不自动修改业务逻辑
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Check-CodeQuality.ps1 -Fix

# 快速排版检查，不进行编译分析
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Check-CodeQuality.ps1 -SkipAnalysis

# 调查所有 geezhu 新增源码；未整理模块可能不通过
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Check-CodeQuality.ps1 -All
```

显式选择文件时仍会检查归属，传入官方原有路径会直接拒绝。不要直接运行全仓库格式化。

工具按仓库版本还原：CSharpier 1.3.0、Meziantou.Analyzer 3.0.294。分析器通过单独开发工具项目下载，在检查编译时临时注入，不加入 League 的依赖和发布包。C++ 检查使用 Visual Studio 的 clang-format 或 PATH 中的同名工具。

检查规则包括完整花括号（IDE0011）、取消参数传递（MA0040）、异步方法避免阻塞调用（MA0042）、方法长度（MA0051）、包装异常保留原异常（MA0054）。方法长度是梳理职责的提示；如有合理例外，说明理由后局部配置，不为通过检查机械拆分。

编译日志和 SARIF 报告在 `bin/code-quality`。只有选中新增源码的上述规则形成检查失败，不把上游已有警告升级为错误。

## 更新官方基线

当前工作区 `origin` 指向官方仓库。在集成上游之前执行：

```powershell
git fetch origin master
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Update-CodeQualityScope.ps1 -UpstreamRef origin/master
```

如果克隆的是 geezhu 的仓库，`origin` 可能指向自己的分支，应改用指向 `mizi6654/LeagueHistory` 的引用。日常检查只读保存的清单，不依赖远端名称，也不会自动刷新基线。

范围变化后审查 `config/code-quality.json`、`.editorconfig`、`.csharpierignore`。新样板加入 `adoptedFiles` 前先完成整理并验证，删除或重命名样板时同步维护清单。

## 当前样板与验证

样板已扩展到全部新增客户端、模型、英雄分析解析器和图标／名称辅助代码，以及全部新增服务和管理器，并开始覆盖战绩界面、arammeta 页面、阵容分析与基础控件；共 143 个源码文件。

阵容分析的证据提取与结论生成分开，实时对局按玩家和事件解析，时间轴按帧、金币和事件处理。客户端分别处理缓存读取、网络刷新和原子保存；共享初始化不因单个视图取消而中止，等待方仍可取消。玩家加载分离名单核对、请求与结果投递；符文应用分离保存与当前页面校验。

战绩行按身份、出装与指标展示，响应式布局与指标翻页分开；玩家卡片将摘要、历史导航与绘制分开。详情页导航按需复用视图，队伍构建与排序分别处理；关闭时先取消请求，等待当前工作与取消回调结束后再释放取消源。统计表分别构建列、指标分组与表头；对比图控件独立于指标选择，绘制刻度、玩家和数据条，并保留缺失数据的悬浮说明。

时间线分别处理请求、事件展示和独立金币图表；生涯统计分开读取记录、选择历史兜底和展示汇总。重试、切换玩家或关闭页面后，旧结果不会更新界面；取消源等待旧请求与取消回调结束后释放。

近期战绩分开处理分批历史、角色与成就资料，以及结果通知；通知发出前先登记工作，订阅方在通知中关闭页面也能安全取消。总览的圆环、数值与说明分别绘制；雷达图的网格、已知证据与标签分别绘制，缺失证据保留为未知。

玩家卡片分别展示加载状态、近期统计、身份头像和所选英雄。梯队与图标请求各自记录先后顺序，快速切换 A→B→A 时旧图片也不会覆盖当前图片；每个过期或替换的自有图片都释放。等待槽位、紧凑历史滚动和聊天状态控件也纳入持续检查。

装备与符文选择控件在关闭时等待图标工作及取消回调结束，再释放自己的关联取消源；不会取消父页面。队伍汇总借用主详情页的提示组件，成就海报拥有下载缓存返回的图片副本。

arammeta 页面分开加载概要、完整详情与缓存复用；摘要、页签、强化筛选与统计说明、核心装备与单件备选分别处理。装备加载完成保留已显示的概要、选中页签与强化筛选。页面关闭先取消等待者，所有共享图片工作和取消回调结束后才释放 HTTP 客户端、并发锁与取消源；不会发布晚到的图片。

阵容分析分别生成英雄卡片、能力覆盖、所选技能的说明与依据；换人预览继续保留原来的自己用于比较。旧阵容结果不写入新阵容，取消源等待分析、素材工作与取消回调结束后再释放。客户端为借用资源，由调用方或共享实例管理；页面只释放自己的头像、技能图标、提示组件和计时器。100 次刷新请求仍合并为一次重建，调整窗口不会触发布局循环。

回归可独立运行，避免受界面测试上下文影响：

```powershell
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --model-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --client-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --overlay-lifecycle-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --scoreboard-layout-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --history-presentation-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --player-champion-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --selection-control-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --arammeta-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --composition-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --composition-lifecycle-checks
```

全部 204 个原待整理文件已经完成工具排版，并分模块提交；控制流括号也已补齐。未列入样板的模块仍需处理过长方法、异步调用和资源边界，不能把排版完成视为全部结构整理完成。

语音核心已分为会话编排、模型识别、麦克风采集和 PCM 缓冲，接口分别保存，状态使用具名枚举。保持模型与麦克风并行准备、设备返回音频后才提示开口、取消后不发布结果，以及等待工作结束再释放模型和录音锁。

旧语音引擎的停止／销毁状态也使用具名枚举，轮询停止和清理等待不会被调用方的取消提前打断。模型下载拆出单文件流传输，大小、哈希、原子替换及进度行为保持一致。Game Bar 将会话准备、请求接纳、识别更新和状态发布分开；传输仍可在识别器结束后处理确认，不能使用已经释放的录音取消源。

```powershell
dotnet build Tests/StatisticsChecks/StatisticsChecks.csproj -c Release
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --sensevoice-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --assistant-voice-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --gamebar-voice-checks
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CodeQualityChecks.ps1
```

模拟验证不打开真实麦克风，不发送真实聊天，不运行关机任务。

## 参考项目

- [dotnet/runtime C# 规范](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)：采用常见命名、换行和缩进，保留中文文案。
- [CSharpier](https://github.com/belav/csharpier)：统一源码排版。
- [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer)：选择与本项目异步和可读性问题相关的规则。
- [microsoft/wil 的 clang-format 配置](https://github.com/microsoft/wil/blob/master/.clang-format)：参考 Windows C++ 排版，采用简化配置。
