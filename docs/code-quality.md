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

样板已扩展到全部新增客户端、模型、英雄分析解析器和图标／名称辅助代码，以及语音会话、模型下载、Game Bar 语音和阵容快照桥接；共 62 个源码文件。阵容分析的证据提取与结论生成分开，实时对局按玩家和事件解析，时间轴按帧、金币和事件处理。客户端分别处理缓存读取、网络刷新和原子保存；共享初始化不因单个视图取消而中止，等待方仍可取消。玩家加载分离名单核对、请求与结果投递；符文应用分离保存与当前页面校验。回归可独立运行，避免受界面测试上下文影响：

```powershell
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --model-checks
dotnet Tests/StatisticsChecks/bin/Release/net8.0-windows/StatisticsChecks.dll --client-checks
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
