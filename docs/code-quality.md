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

样板已扩展到全部新增客户端、模型、英雄分析解析器和图标／名称辅助代码，以及全部新增服务和管理器，并开始覆盖战绩界面、arammeta 与 OP.GG 页面、阵容分析、英雄成就、主页背景、启动面板、缓存设置与基础控件；共 248 个源码文件。

阵容分析的证据提取与结论生成分开，实时对局按玩家和事件解析，时间轴按帧、金币和事件处理。客户端分别处理缓存读取、网络刷新和原子保存；共享初始化不因单个视图取消而中止，等待方仍可取消。玩家加载分离名单核对、请求与结果投递；符文应用分离保存与当前页面校验。

战绩行按身份、出装与指标展示，响应式布局与指标翻页分开；玩家卡片将摘要、历史导航与绘制分开。详情页导航按需复用视图，队伍构建与排序分别处理；关闭时先取消请求，等待当前工作与取消回调结束后再释放取消源。统计表分别构建列、指标分组与表头；对比图控件独立于指标选择，绘制刻度、玩家和数据条，并保留缺失数据的悬浮说明。

时间线分别处理请求、事件展示和独立金币图表；生涯统计分开读取记录、选择历史兜底和展示汇总。重试、切换玩家或关闭页面后，旧结果不会更新界面；取消源等待旧请求与取消回调结束后释放。

近期战绩分开处理分批历史、角色与成就资料，以及结果通知；通知发出前先登记工作，订阅方在通知中关闭页面也能安全取消。总览的圆环、数值与说明分别绘制；雷达图的网格、已知证据与标签分别绘制，缺失证据保留为未知。

玩家卡片分别展示加载状态、近期统计、身份头像和所选英雄。梯队与图标请求各自记录先后顺序，快速切换 A→B→A 时旧图片也不会覆盖当前图片；每个过期或替换的自有图片都释放。等待槽位、紧凑历史滚动和聊天状态控件也纳入持续检查。

装备与符文选择控件在关闭时等待图标工作及取消回调结束，再释放自己的关联取消源；不会取消父页面。队伍汇总借用主详情页的提示组件，成就海报拥有下载缓存返回的图片副本。

arammeta 页面分开加载概要、完整详情与缓存复用；摘要、页签、强化筛选与统计说明、核心装备与单件备选分别处理。装备加载完成保留已显示的概要、选中页签与强化筛选。页面关闭先取消等待者，所有共享图片工作和取消回调结束后才释放 HTTP 客户端、并发锁与取消源；不会发布晚到的图片。

阵容分析分别生成英雄卡片、能力覆盖、所选技能的说明与依据；换人预览继续保留原来的自己用于比较。旧阵容结果不写入新阵容，取消源等待分析、素材工作与取消回调结束后再释放。客户端为借用资源，由调用方或共享实例管理；页面只释放自己的头像、技能图标、提示组件和计时器。100 次刷新请求仍合并为一次重建，调整窗口不会触发布局循环。

英雄成就页分开读取成就、玩家资料与拥有数量，再展示前五名。资料通知发出前登记两个工作，订阅方在通知中关闭页面也不会继续读取成就；切换玩家后旧图标不会插入新页面。海报复制借用图标；默认资源返回的新图片由页面释放，注入提供方的图标保持可用。全部成就页分别建立页头、海报列表与可见项加载，每批最多四张；关闭后不再启动计时器。击杀地图的坐标边界具有明确名称，战绩亮点的图片加载返回可观察任务。

OP.GG 页面分别处理筛选、浏览历史、排行榜、分路解析与完整攻略；摘要、竞技场搭档和强化、出装及对局列表分别展示。符文选择为独立控件，管理方案预览、选择状态和应用过程；应用由当前页面授权，失败或取消后不显示过期成功状态。符文树按主系、副系和属性碎片构建，过期方案不覆盖当前节点图片。排行榜排序列具有明确名称，图片并发锁和取消源等全部图片工作结束后释放；页面关闭也先等待读取工作再释放自己的客户端。资源缓存的图片解码放在后台，取消的等待者仍负责释放缓存稍后返回的自有图片副本。

异步界面检查使用持久的 WinForms 消息循环，避免只在同步完成时通过、真正异步返回时却落到错误线程。模拟检查覆盖快速换英雄、重复查询缓存、关闭时的图片与客户端清理、所选方案应用和失效页面禁止应用；不会写入真实符文页。

玩家主页背景分开读取皮肤与成就兜底、载入原画和更新子控件颜色。切换或关闭时等待旧工作与取消回调结束再释放取消源；晚到原画会释放，重复设置同一张原画不会误释放当前图片，颜色缓存也不再保留已销毁控件。客户端启动面板把目录选择、保存、检测与启动拆为具名操作；手动选择优先于晚到的自动检测，关闭后不更新控件。战绩亮点在玩家数据为空时不生成虚假的最高项；缺失指标省略，并列最高保留。

玩家战绩页将身份、段位、筛选弹窗、分页与历史列表的构建分别处理。详情准备完成后才切换布局，过期、已移除或已关闭的战绩不再发布；返回主页和既有清理入口都释放待显示详情。结果筛选缓存同时区分玩家和比赛模式，每次读取固定筛选条件，条件改变后不继续扫描、缓存或发布旧数据。页面自行创建的字体随所属控件释放。

桌面对局速览按双队伍槽位构建、玩家数据填充和当前玩家详情分别处理，卡片使用具名行对象。刷新保留十个卡片控件；一名隐藏玩家只影响其自己的成就，其他英雄的成就不会显示在当前英雄上。图片从上层缓存借用，换局和关闭都不释放借用图片；关闭后的晚到更新不再访问控件。

缓存设置页共用有边界的分钟输入控件，数据统计、平衡、阵容分析和成就缓存各自保存；初始化控件不误触发配置保存。桌面对局速览窗口分别构建内容与页头，并用具名常量说明不激活窗口的原生消息与样式；多屏坐标和小屏幕限制保持原有行为。

梯队聊天检查分别验证来源与缺失数据、五人消息长度和分段、实际模式及阵营选择。日志检查分开前台进程身份、持久化与轮转和禁止输入的保护逻辑；快捷键检查使用隔离的接收器直接派发原生消息，验证后释放注册，并在断言失败时也恢复调用方界面上下文。不模拟用户键盘输入、不发送聊天。

数据回归按明确场景组织：平衡样本与单位、持久化及离线刷新；配置默认值、原目录迁移、换目录升级和损坏配置保留；英雄样本读取、证据路径、技能范围与时长、未确认护盾公式和未知节点递归。原有断言与样本导出保留，取消检查等待取消回调结束再释放测试取消源。

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
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --profile-presentation-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --opgg-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --opgg-lifecycle-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --profile-surface-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --history-navigation-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --loading-view-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --settings-surface-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --tier-chat-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --balance-checks
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

实时对局分别构建队伍列、保留的玩家卡片、所选英雄摘要和推荐区；请求取消后等待工作结束，再释放取消源、客户实例和图标缓存。关闭后的晚到图标与原画副本会释放，旧模式请求不会覆盖当前提示。英雄识别、连续死亡和速览身份验证按场景组织，覆盖同英雄玩家、隐藏或待确认身份、取消共享等待、缓存重连与新对局重置。

Game Bar 的四个原生文件已纳入持续检查。组件入口分开读取和校验快照、更新十个玩家卡片、写入心跳与处理窗口事件；详情按技能选择、装备／海克斯图块和通用信息行构建；语音编辑区与聊天协议各自组织。原生构建仅生成未安装的组件包，桥接回归使用临时文件，不开启麦克风或向游戏发送消息。

Game Bar 桥接测试按连接状态、快照生命周期、玩家字段、请求形状、前台就绪、模式推荐和确认语音分场景组织。临时文件的异步等待统一带五秒超时；测试失败时也先结束桥接写入再清理目录，保留全部原有断言。

测试入口按数据、UI、助手、桥接和显式诊断分类；原有命令与断言全部保留。原入口的数据、时间线、主页、房间和启动路径检查分场景保存，新增独立命令便于单模块验证。UI 检查共用持久消息循环；设置页测试不再销毁共享同步上下文，初始关机计划读取完成后才结束布局预览。

助手窗口验证按附着／层级、最小化／句柄重建和客户端替换分组。速览验证分开持久配置、显示状态、身份解析、窗口契约和重复更新；组队标记验证分开分组稳定性、账号边界和历史阈值。成就在线诊断仍显式启用，只输出字段可用性，整理检查不连接客户端。

主窗口导航按攻略入口、房间详情、玩家资料和搜索历史分组。攻略入口公开保留 async void 边界，内部使用可等待方法并在窗口关闭或请求取消时退出；房间详情等待期间替换旧页面并释放迟到解析结果；搜索布局、输入行为和历史菜单分开。

设置布局按功能区创建卡片：战绩、自动化、选人助手、速览、客户端维护、消息和已有附加页面。速览状态轮询独立于偏好保存，计时器仍由显示控件释放。现有绑定控件保持原实例、分组和保存行为，已验证正常／窄窗口布局。

关机设置卡片由专用控制器管理显示、编辑状态和计时器，读取／修改操作互斥，失败恢复保留未保存输入，控件销毁后的迟到结果不再发布。计划验证按时钟、任务归属、迁移创建、注册回滚、替换和删除分场景组织；文件检查使用异步读取。模拟测试覆盖等待刷新时点击创建和关闭卡片，未修改任何系统任务。

语音设置控制器分开设备选择、列表刷新与模型准备；刷新不会覆盖保存值，页面关闭后忽略迟到列表，下载源由完成的工作释放。语音弹窗用准备／录音／停止／识别／发送／关闭具名状态，分别构建布局与动作，关闭后等待工作结束再释放取消源及自建字体。助手打开流程绑定可见状态、当前客户端和捕获的房间，模拟回归保留全部原断言并覆盖列表恢复、关闭后晚到结果及下载取消。

助手推荐分片按选择控件、文案、資料与可选图片加载组织；符文切换先取消旧图标工作，工作结束再释放取消源。技能图片的失败与迟到结果均可观察并回收，筛选事件抑制使用 finally 恢复，平衡提示的缓存尝试状态属于单个问号控件。配置提示使用唯一选人房间与阶段校验，原有文案、去重和拒绝处理保持。

推荐与出装回归分开数据解析、模式选择、缓存／取消、符文写入验证和页面展示。出装保存通过独立模拟账户检查个人方案保留、失败与重复应用；推荐页面恢复共享符文样式，UI 等待带超时。

arammeta 回归分开内存、条件请求、持久缓存、过期恢复、共享等待与渐进展示；可选英雄和海克斯助手验证分开解析、列表、翻页和详情动作，保留全套断言与截图。UI 回归使用持久消息循环和有界等待，未修改上游源码。

语音桥接测试使用拥有临时传输文件和模拟会话的专用夹具，按启动、协议校验、就绪／录音、确认清理、租约和重试组织；会话退出后再删除目录并释放取消注册。详情预览分开推荐、资源编码、样例元数据和玩家标记，继续只生成独立样例文件。

助手测试按功能动作和页面布局分组，阵容分析测试按缓存、能力短板、换人预览、雷达和刷新合并分组；网络夹具支持取消并使用异步文件读取，UI 检查继续使用隐藏窗口。

玩家加载回归分别验证并行进度、换队／模式、排队超时、重试、迟到 UI、隐私身份与卡片保留；成就回归分开共享缓存、持久化、失败／隐私恢复、选人发送和双方卡片。全部断言保持，使用本地模拟账号与响应。

助手成就分片分开选择身份解析、发送有效性和状态展示，保留缺失玩家、英雄切换、房间查询期间变化与服务器阶段校验。

助手主窗体按布局、轮询、选人状态、推荐和应用动作组织，异步工作统一记录；替换请求的取消源等待工作与取消回调结束后释放，客户端关闭清理在后台完成。模拟回归覆盖关闭时的延迟响应和取消回调，不向游戏发请求。

装备编辑器为独立窗体，分别构建选择、后续图标、保存反馈与观察计时器；助手只保留入口及客户端居中定位。预览切换和关闭等待图标工作及取消回调结束后释放源，模拟回归验证关闭后的迟到图标回收。

海克斯浏览器使用独立展示控制器管理分类、分页、详情等待和卡片图标，使用具名标签状态；宿主关闭与列表替换均等待异步工作后释放取消源。原有布局及翻页断言保留，并验证关闭后的迟到详情不会发布。
