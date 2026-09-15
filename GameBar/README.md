# Game Bar 对局速览（验证版）

显示组件使用微软 Xbox Game Bar SDK，主程序保留 WinForms。通过 Win＋G 打开和管理面板；主程序与组件均不注册速览快捷键，也不调整 LOL 的显示模式。全屏覆盖是否可用需要在实际游戏中确认，注册和编译成功不能代替该验证。

## 使用

1. 构建并安装下述开发组件。
2. 启动新版 League.exe，在「设置 → 功能辅助 → 对局速览」选择 **Game Bar（全屏验证版）**。
3. 按 **Win＋G**，从小组件列表打开 **League 对局速览**。需要保留在游戏上方时，点击组件右上角图钉。
4. 保持 League 主程序运行，进入加载 / 游戏阶段后同步十名玩家的英雄、梯队、胜率、选用率和已缓存战绩。通过 Win＋G 查看或关闭组件；关闭后可从小组件列表重新打开。

未固定时，退出 Win＋G 会隐藏组件；固定时，退出 Win＋G 后仍可显示在游戏上方。内容区会显示当前固定状态。点 × 可关闭组件，之后在 Win＋G 中重新打开。

桌面速览仍可在设置中选择。独占全屏时桌面版本暂停显示，避免把游戏切到桌面。「测试桌面显示」仅检查桌面窗口。

应用设置统一保存到 `%LOCALAPPDATA%/LeagueHistory/LeagueConfig.json`。首次启动会导入 exe 同目录的旧配置，其次检查旧启动目录；原文件保留。之后更换 exe 目录仍读取同一份设置，不会重置 Game Bar 选择。首次使用新版并移到新目录时，需一并带上旧 `LeagueConfig.json` 完成首次导入。

## 开发构建

需要 Windows 10 2004+、Xbox Game Bar、VS 2022 C++ 工具和 Windows SDK（包含 C++/WinRT）。不需要安装 UWP XAML 编译工具：界面使用原生 XAML 控件构造。

```powershell
./GameBar/Build.ps1
./GameBar/Install-Development.ps1
```

SDK 固定为 Microsoft.Gaming.XboxGameBar 7.3.2607010，首次构建从 NuGet 下载；原生 DLL、元数据和 SDK 自带资源随组件打包。注册项从同版本元数据生成，防止 SDK 接口变化后使用过期 GUID。应用图标复用项目 favicon。

安装脚本仅在电脑原本已启用开发模式时注册当前用户的组件，不自动开启开发模式、不导入证书。构建产物在 `bin/GameBar`，注册后必须保留 `Package` 目录。生成的 MSIX 尚未签名，不是普通用户直接双击安装的发行包；正式分发需要签名和相应分发流程。

更新构建需提高清单版本号，否则 Windows 可能继续指向旧安装目录。脚本会核对实际安装位置，并在更新后退出 Game Bar 的界面进程，使下次打开时重新加载组件，避免留下空白旧窗口；不会结束游戏或 League 主程序。录制服务运行时跳过重启，也可用 `-KeepGameBarRunning` 跳过，然后自行重开 Game Bar。

卸载开发组件：

```powershell
Get-AppxPackage -Name LeagueHistory.GameBar | Remove-AppxPackage
```

## 数据与生命周期

- 只传本机 JSON，位置为组件的 `%LOCALAPPDATA%/Packages/LeagueHistory.GameBar_*/LocalState/snapshot.json`；不传 LCU 令牌、账户标识或接口地址。
- 主程序复用现有统计和图片缓存。48px PNG 每个英雄只编码一次；写入队列容量为一，合并快速更新，通过临时文件替换发布完整快照。
- 默认约每 3 秒更新数据，组件以 500ms 间隔检查本机快照，单次读取互斥。超过 15 秒未有有效数据即清空旧阵容。没有第二套远程统计请求。
- 显示命令与数据版本分开：刷新胜率、头像不恢复用户手动收起的组件。程序实例 / 对局切换会重置显示命令。
- 对局速览不订阅原始键盘、全局快捷键或 SDK 快捷键事件，旧配置中的速览按键字段会被忽略。聊天快捷键不受影响。
- 新组件忽略旧主程序的按键切换命令。快照仅保留空的 `shortcut` 与 `shortcutOwner=main` 兼容标记，阻止旧组件重新启用 SDK 按键监听；不再发布按键计数或时间。
- `widget-status.json` 记录连接、固定、可见状态与读取错误；`component-error.txt` 记录组件启动异常，`component-diagnostics.txt` 记录激活与固定 / 可见状态变化。
- 回执同时记录实际绘制的玩家数、界面尺寸和读取错误。主程序写出了快照但没有收到组件回执时，设置会明确显示“组件未连接”。
- 大厅、选人和结算阶段继续发送连接回执，分别显示等待对局、选人中、本局结束。主程序数据超过 15 秒未更新时才清空旧阵容并提示数据中断，恢复后自动重连；短暂的单次读取失败不会误报断开。
- 从开始菜单打开组件时，显示使用同一套渲染代码的桌面预览。头像独立解码，不阻塞其他玩家或连接回执。

## 验证

```powershell
dotnet run --project Tests/StatisticsChecks/StatisticsChecks.csproj -c Release -- --loading-overlay-checks
```

自动检查覆盖本机快照原子替换、最新数据优先、双队分组、空缺位置、头像、统计字段、关闭时清空、旧按键配置忽略和显示后端切换。仍需人工检查：Win＋G 组件加载、固定、全屏加载画面中的显示与关闭后重新打开。

原生界面检查：运行 `GameBar/Verify-Preview.ps1`，用当前主程序缓存绘制预览并保存组件自己的 `LocalState/preview-verification.png`。该检查通过应用激活接口传入专用参数，不发送任何游戏输入；正常启动不会保存预览图。检查预览与 Game Bar 使用不同回执文件，避免把桌面预览误判为游戏内连接成功。

参考：[官方接入说明](https://learn.microsoft.com/en-us/xbox/game-bar/quickstart/introduction)、[组件激活](https://learn.microsoft.com/en-us/xbox/game-bar/guide/app-activation)。
