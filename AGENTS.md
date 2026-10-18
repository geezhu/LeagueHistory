# LeagueHistory 项目指令

## 适用范围：全部源码

- 格式和结构规范适用于全部源码文件，不区分作者或来源；从上游带进来的文件也一样，没有排除清单。只有生成文件（`*.Designer.cs`、`*.g.cs`）和项目 XML 交给各自的工具。
- 合并或 rebase 上游后，上游带来的新代码同样按本规范整理，并运行 `Scripts/Check-CodeQuality.ps1`。格式整理单独提交，与功能和冲突处理分开。
- 规范配置由 `Scripts/Update-CodeQualityConfig.ps1` 生成 `.editorconfig` 和 `.csharpierignore`，不手改生成结果。

## 可读性与职责

- C# 使用四空格、换行花括号；每行一条语句。控制流使用完整花括号，长调用和初始化交给 CSharpier 换行。
- 类、方法、属性使用 PascalCase；参数、局部变量使用 camelCase；私有实例字段使用 `_camelCase`。名称说明用途，保留原生接口要求的名称。
- 一个文件优先放一个主要类型。业务方法表达一个目的；不要把控件创建、业务操作、状态管理和资源清理挤进一个方法。
- 窗体处理布局、展示和用户操作；服务负责数据与业务；模型表达数据与状态。小功能不为了分层而引入通用框架。
- 用具名状态替代散落的状态数字；有关联的布尔状态应梳理有效组合。不要仅为了缩短方法机械拆分，也不要仅靠 partial/inl 分文件隐藏耦合。
- 删除无意义的注释，保留解释约束、并发顺序、兼容性和失败原因的注释。中文文案保持可读中文。

## 异步、异常与资源

- UI 线程不执行网络请求、模型加载、录音设备释放或同步等待异步任务。传递取消参数；后台计算和 UI 更新边界明确。
- 取消、业务失败和清理失败分别处理。忽略异常必须在附近说明原因；不要吞掉正常业务操作的异常。
- 谁创建资源，谁负责释放。并行工作结束后再释放共享模型、缓冲区和互斥锁；取消后不得发布晚到的结果。
- 不自动发送语音识别内容，不在测试中启用真实麦克风、发送真实聊天或运行关机计划。用已有模拟器验证这些行为。

## 日志与诊断

- 需要在用户机器上留下记录的结果和失败写 `AppLog.Shared`，不要只写 `Debug.WriteLine`：后者在 Release 版里会被去掉。吞掉异常的 catch 至少写一条 `Warn` 并附上异常。
- 轮询类检查用 `State` 只记变化；不写玩家名、Riot ID、聊天正文、令牌和含玩家 ID 的请求地址。类别和格式见 [docs/diagnostics.md](docs/diagnostics.md)。
- 新增日志文件放在 `LogRetention.LogsDirectory`，用 `LogFileWriter` 写入，诊断报告会自动收录并去敏。

## 测试

规范按 .NET 社区通行做法写。老套件 `Tests/StatisticsChecks` 是控制台检查程序（`OutputType=Exe`，无测试框架），覆盖率和冒烟仍由它驱动，**在替代品接管前不得删除**；新测试进 `Tests/League.Tests`（xUnit）。迁移期：新增覆盖优先进新工程，老套件只做「新工程补齐后逐个删除」，不再新增检查类。

### 结构与命名

- 测试工程 `League.Tests`，目录与源码一致：`Services/GameQueues.cs` 对应 `Tests/League.Tests/Services/GameQueuesTests.cs`。
- 一个被测类对应一个测试类，名为 `<类名>Tests`。
- 测试方法名全项目统一为 `被测成员_场景_预期`，例如 `ResolveQueue_CustomKiwi_Returns2400`。断言消息本来就是被测行为的描述，直接搬来当名字；老套件里约三分之二的断言消息已是纯字面量，可以照搬。
- 依赖界面或外部资源的测试加 `[Trait("Category", "UI")]`，便于筛选和单独运行。

### 单个测试

- 三段式（准备、执行、断言），用空行分开。一个测试只验证一个行为；同一逻辑多组数据用 `[Theory]` 加 `[InlineData]`、`[MemberData]`。
- 测试里不写 `if`、循环或复杂逻辑。
- 用框架断言，不手写 `throw`。老套件的 `Check(bool, message)` 迁移时先机械换成 `Assert.True(condition, message)`；有条件时换成能看到期望与实际的 `Assert.Equal(expected, actual)`。

### 隔离与确定性

- 测试之间不依赖执行顺序。新工程必须显式关闭并行：`[assembly: CollectionBehavior(DisableTestParallelization = true)]`。改动 `FormMain.Globals`、`AppLog.Shared`、全局热键、环境变量或数据目录的测试放进同一个 `[Collection]`，准备放构造函数、还原放 `Dispose`，成对出现。
- 不访问真实网络、真实客户端和用户目录。HTTP 一律走桩 `HttpMessageHandler`（`BaseAddress` 指向假主机），目录一律用临时目录。
- 不靠 `Thread.Sleep` 等时间，用可注入的时钟或显式的完成信号。老套件的轮询等待只作为迁移期的临时手段。
- 断言「窗口、进程或端口不存在」的测试必须先把该状态显式钉死（写死相关字段），因为开发机上可能真的开着游戏或客户端；会打到 `127.0.0.1:2999`（对局内 API）的路径同样要先钉死，保证有没有对局结果都确定。
- 上面「异步、异常与资源」里的安全条目继续有效：不启用真实麦克风、不发送真实聊天或按键、不运行关机计划、不改系统计划任务、不起 `powershell.exe` 子进程。以 `MessageBox` 结尾的路径不测，留空并在 `docs/testing-gaps-notes.md` 记录原因。

### 准备与共享资源

- 每个测试各自的准备放构造函数，清理放 `Dispose`；多个测试共用且准备昂贵的（样例对局 JSON、图片、窗体宿主）用 `IClassFixture<T>`；整个程序集只做一次的（数据目录、环境变量、`Application.SetHighDpiMode`）放模块初始化器。
- 样例数据用构建器（例如 `new MatchBuilder().WithQueue(3270).Build()`）或放在 `TestData/`，不要每个测试手拼一大段 JSON。

### 测什么，不测什么

- 只通过公开接口测行为；私有方法通过调用它的公开路径验证。
- **不要为测试加 `InternalsVisibleTo`，也不要把私有成员改成 `internal` 图方便。** 老套件用反射读私有成员，多数是在钉死环境状态或替换依赖；后者说明该处缺少注入点，正确做法是新增代码留出接缝（构造函数参数、工厂委托），而不是放宽可见性。反射只作为存量代码的临时手段，新增代码不允许新增反射访问。
- 界面测试只验证状态和布局计算（例如 `Placement` 返回的位置、`CellBounds` 的行列），不比对像素。
- **交互用 `Support/WindowMessages`**：`Click`/`DoubleClick`/`Move`/`Drag`/`Drop`/`Wheel`/`Key`/`Paint` 把真实窗口消息发给控件。窗口消息是 Win32 控件的公开接口，所以点击自绘按钮、拖动、滚轮、键盘都可以测，不必反射进 `protected OnXxx`。
- **绘制用 `Control.DrawToBitmap`（WM_PRINT）；要真实重绘用 `WindowMessages.Paint`（WM_PAINT）**，两者是窗口过程里的不同路径。屏幕外的窗口不会被 Windows 排队重绘，所以 `Refresh()` 拿不到 `OnPaint`。
- 断言「某个钩子没有被调用」时，**必须同时有正向对照**（例如一起数 `OnPaint`），否则分不清「没画」和「画了但没走那条路」。
- 断言一个 record 时，**只比较这条规则涉及的字段**，不要整记录相等：应用新增一个属性就会让整记录比较变红，而那不是回归。`HealthEstimateChecks` 就这么误报过一次。

### 异步

- 不涉及界面的测试方法直接写成 `async Task`，不用 `.GetAwaiter().GetResult()`。
- **界面测试用 `Support/UiHost.Run`**：它在专用 STA 线程上起一个真正的 `Application.Run` 消息循环，body 拿到一个已显示的宿主窗体，可以往里加控件。不这么做的话，xUnit 把测试方法放在 MTA 线程池线程上，没有消息循环；应用代码的异步续体经 `WindowsFormsSynchronizationContext` 投递后**没人泵**，测试会挂住而不是失败。
- `UiHost.Run` 的 body 可以是 `async`，循环在它等待期间继续泵消息，await 之后仍回到同一条 STA 线程——`Support/UiHostTests.cs` 就是钉这三件事的。界面测试方法本身保持 `void`，异步体现在 body 里。
- 应用代码的异步方法没有 `ConfigureAwait(false)`，所以**不要**在界面线程上用 `.GetAwaiter().GetResult()` 等一个会回到界面的任务。确实需要等一个与界面无关的任务时，在 `UiHost` 的 body 里 `await` 它。
- 取消单独写测试，验证「取消后不发布晚到的结果」。

## 工具与验证

- C# 排版使用仓库固定版本的 CSharpier；Game Bar C++ 使用 `.clang-format`。
- 老套件：`dotnet Tests/StatisticsChecks/bin/Debug/net8.0-windows/StatisticsChecks.dll`，不带参数跑默认套件，带 `--xxx-checks` 跑单项。新工程：`dotnet test Tests/League.Tests/League.Tests.csproj`。
- `Scripts/Run-Coverage.ps1` 分两遍采集再合并：一遍跑老套件，一遍跑 `dotnet test Tests/League.Tests`，两遍都只统计 `League.dll`。所以把一条检查从老套件搬到新工程时，**两边先都留着**；新工程确实覆盖到同样的代码之后，再删老的那一份。
- 运行 `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Check-CodeQuality.ps1`。默认检查全部源码；`-Fix` 修复选中源码的排版；`-BaseRef` 只检查某个提交以来改动的源码。编译分析输出到 `bin/code-quality/build`，不影响正在运行的 `bin/Release`。
- 编译检查中的 Meziantou 规则对选中的源码形成检查门槛；不向主项目永久添加分析器依赖。
- 测试覆盖率运行 `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Run-Coverage.ps1`；`-Checks` 只跑指定检查，`-Open` 打开报告。结果输出到 `bin/coverage`，只统计 `League.dll`。
- 语音改动编译 `Tests/StatisticsChecks/StatisticsChecks.csproj`，然后运行 `--sensevoice-checks`、`--assistant-voice-checks`、`--gamebar-voice-checks`。
- 格式整理与行为改变分开审查。保留已有测试，必要时补充取消、错误和资源释放的回归验证。用户要求提交时再提交。
