# 定时关机

设置 → 功能辅助 → 定时关机 · 每日计划。只管理这一条每日关机任务，操作仅保留「创建计划」「删除计划」「查看日志」。设置每日时间与 0～300 秒倒计时，显示当前状态与下次执行时间；没有任务时默认 23:00、5 秒；关机时 Windows 会提示保存未保存的工作。

「创建计划」立即创建并启用。已有计划时替换其时间和倒计时，停止原来的等待进程，从下一次未来时间开始执行，不补跑已错过的一次。打开设置、修改输入框、启动新 EXE 都不会自动启用任务。

「删除计划」确认后停用、停止并删除这一条 Windows 任务，保留运行日志、脚本及原任务备份；即使删除失败也先让已管理脚本失效。「查看日志」打开运行日志，尚未运行时提示暂无日志。不会列出或修改其他 Windows 计划任务；同名任务内容无法识别时拒绝覆盖或删除。

## 计划任务与文件

- 复用 `\ShutdownAfterLoL_2300`，更改时间不创建新任务。首次保存会备份原任务 XML，将动作切换为联盟视界内置脚本；原 Documents 下的脚本保留。
- 脚本、`plan.json`、`status.json`、`cancel.json`、`shutdown.log` 放在 `%LOCALAPPDATA%\LeagueHistory\ScheduledShutdown`。脚本使用 UTF-8 BOM，兼容 Windows PowerShell 5.1。
- 任务以当前登录用户的最高权限运行，后台隐藏窗口。关闭联盟视界、移动新版 EXE 不影响已经保存的计划；用户须保持登录。任务不唤醒电脑，错过时间不补跑，重复触发不创建第二个实例。
- 脚本还会检查本次是否在计划时间后 10 分钟内启动；单次最多等待 6 小时。睡眠恢复或长时间暂停后，重新复查并重新倒计时。
- 关闭任务或保存新设置会使旧工作进程失效。没有有效配置、配置关闭、读文件失败或状态不确定，都不会执行关机。

## 对局保护

不按队列编号限制模式。LCU 的确认对局、选人、加载、游戏和重连阶段均等待；进入结算则视为结束。没有明确结算信号但游戏进程仍存在时继续等待。客户端运行但读不到 LCU 时暂缓；已观察到对局后，游戏和客户端突然消失会保留 120 秒重连缓冲。

确认无对局后连续复查 10 秒，再执行用户设置的倒计时。倒计时期间继续检测，进入新对局或状态变为未知会撤销倒计时。设置页显示进度；脚本也通过 Windows 托盘通知提醒，点击通知或双击托盘图标可取消本次。系统关闭通知时仍可在设置页删除计划以取消关机。

最后调用 `shutdown.exe /s /t 0`，不加 `/f`。倒计时由脚本管理，因为 [Windows shutdown 文档](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown) 说明 `/t` 大于 0 时隐含 `/f`。正常关机可能被未保存的应用阻止；日志的“已请求”不代表电脑已经关闭。

## 验证

```powershell
dotnet run --project Tests/StatisticsChecks/StatisticsChecks.csproj -c Release -- --shutdown-checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests/ShutdownScriptChecks.ps1
dotnet run --project Tests/StatisticsChecks/StatisticsChecks.csproj -c Release -- --settings-checks
```

上述检查使用模拟任务存储或只读状态；覆盖创建即启用、原任务迁移、替换时间、运行中取消、删除失败与重建。脚本测试以 `-LibraryOnly` 加载，调用纯判断函数，均不关机。`--shutdown-validate` 使用 Windows 的 `TASK_VALIDATE_ONLY` 校验 XML 并运行一次只读探测，不注册、启用或启动任务。用户在设置页点击「创建计划」后，实际任务才会生效。

## 状态文件兼容与故障诊断

Windows 任务使用 PowerShell 5.1。替换已有 `status.json` 必须给 `File.Replace` 传表示空引用的字符串值 `NullString.Value`；直接传 `$null` 会被转换为空路径，第二次状态写入抛出 `ArgumentException`，随后退出等待循环。因此不能只测试游戏状态判断，回归还要实际连续写入隔离状态文件，覆盖等待、结算、倒计时和就绪；CI 固定在 Windows PowerShell 5.1 执行此检查。

脚本异常会记录底层异常类型并返回非零退出码；状态文件写入失败时尝试把失败信息直接追加到日志。日志不包含 LCU 密钥。正常退出和主动取消保持原有行为。

已保存的任务执行 `%LOCALAPPDATA%` 内的脚本副本。仅启动新版 EXE 不会更新该副本；使用新版在设置中按原时间重新“创建计划”即可更新，下次计划才执行。无需启动任务进行验证。
