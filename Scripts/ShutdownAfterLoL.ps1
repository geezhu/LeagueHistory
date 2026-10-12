param([switch]$Run, [switch]$Probe, [switch]$LibraryOnly)
# Windows PowerShell 5.1. Loading the script without -Run only reports game state.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Resolve-GameObservation([string]$Phase, [bool]$GameRunning, [bool]$ClientRunning) {
    if ($Phase -in @('ReadyCheck','ChampSelect','GameStart','InProgress','Reconnect','WatchInProgress')) {
        return [pscustomobject]@{ State='Protected'; Reason='选人、加载或对局进行中，等待本局结束'; Phase=$Phase; Absent=$false }
    }
    if ($Phase -in @('PreEndOfGame','WaitingForStats','EndOfGame')) {
        return [pscustomobject]@{ State='Safe'; Reason='已进入结算阶段'; Phase=$Phase; Absent=$false }
    }
    if ($GameRunning) {
        return [pscustomobject]@{ State='Protected'; Reason='游戏进程仍在运行，等待对局结束'; Phase=$Phase; Absent=$false }
    }
    if ($Phase -in @('None','Lobby','Matchmaking')) {
        return [pscustomobject]@{ State='Safe'; Reason='客户端未进入对局'; Phase=$Phase; Absent=$false }
    }
    if ($ClientRunning) {
        return [pscustomobject]@{ State='Unknown'; Reason='客户端状态暂不可用，暂缓关机'; Phase=$Phase; Absent=$false }
    }
    return [pscustomobject]@{ State='Safe'; Reason='游戏和客户端均未运行'; Phase=$Phase; Absent=$true }
}

function Get-GameObservation {
    try {
        $all = @(Get-Process -ErrorAction Stop)
        try {
            $game = @($all | Where-Object ProcessName -eq 'League of Legends').Count -gt 0
            $client = @($all | Where-Object ProcessName -in @('LeagueClient','LeagueClientUx')).Count -gt 0
            $ux = @($all | Where-Object ProcessName -eq 'LeagueClientUx' | ForEach-Object Id)
        } finally { foreach ($p in $all) { $p.Dispose() } }
        $phase = ''
        if ($ux.Count -gt 0) {
            try {
                if (-not ('LeagueShutdown.Loopback' -as [type])) {
                    Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.IO;
using System.Text;
namespace LeagueShutdown {
    public static class Loopback {
        public static string Phase(int port, string token) {
            if(port<1 || port>65535 || String.IsNullOrEmpty(token)) return "";
            var request=(HttpWebRequest)WebRequest.Create("https://127.0.0.1:"+port+"/lol-gameflow/v1/gameflow-phase");
            request.Proxy=null;request.AllowAutoRedirect=false;request.Timeout=2500;request.ReadWriteTimeout=2500;
            request.ServerCertificateValidationCallback=(sender,certificate,chain,errors)=>
                ((HttpWebRequest)sender).RequestUri.Host=="127.0.0.1" && ((HttpWebRequest)sender).RequestUri.Port==port;
            request.Headers["Authorization"]="Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+token));
            using(var response=(HttpWebResponse)request.GetResponse()) {
                if(response.StatusCode!=HttpStatusCode.OK) return "";
                using(var reader=new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
            }
        }
    }
}
'@
                }
                # Credentials are used only in memory for this loopback request, never written to logs.
                foreach ($clientProcessId in $ux) {
                    $row = Get-CimInstance Win32_Process -Filter "ProcessId=$clientProcessId" -OperationTimeoutSec 3 -ErrorAction Stop
                    $line = [string]$row.CommandLine
                    $portMatch = [regex]::Match($line,'--app-port=(\d+)')
                    $tokenMatch = [regex]::Match($line,'--remoting-auth-token=([^\s"]+)')
                    if ($portMatch.Success -and $tokenMatch.Success) {
                        $raw = [LeagueShutdown.Loopback]::Phase([int]$portMatch.Groups[1].Value,$tokenMatch.Groups[1].Value)
                        if ($raw) { $phase = [string]($raw | ConvertFrom-Json); break }
                    }
                }
            } catch { $phase = '' }
        }
        return Resolve-GameObservation $phase $game $client
    } catch {
        return [pscustomobject]@{ State='Unknown'; Reason='无法确认进程状态，暂缓关机'; Phase=''; Absent=$false }
    }
}

function New-ShutdownGate {
    return @{ SafeSince=$null; CountdownSince=$null; LastProtected=$null; SafeAfterProtected=$false }
}
function Get-ShutdownDecision($Gate, $Observation, [datetime]$Now, [int]$CountdownSeconds) {
    if ($Observation.State -eq 'Protected') {
        $Gate.LastProtected=$Now; $Gate.SafeSince=$null; $Gate.CountdownSince=$null; $Gate.SafeAfterProtected=$false
        return [pscustomobject]@{ State='WaitingGame'; Message=$Observation.Reason; Remaining=0 }
    }
    # 120-second reconnect buffer only applies when game disappears WITHOUT reaching Safe state after protection.
    # Once we've reached Safe after protection with client still present (settlement or lobby), future client exit proceeds to countdown.
    $needReconnectBuffer = $Observation.Absent -and $null -ne $Gate.LastProtected -and -not $Gate.SafeAfterProtected -and ($Now-$Gate.LastProtected).TotalSeconds -lt 120
    if ($Observation.State -ne 'Safe' -or $needReconnectBuffer) {
        $Gate.SafeSince=$null; $Gate.CountdownSince=$null
        return [pscustomobject]@{ State='WaitingState'; Message='正在确认对局是否结束；断连或异常退出不会立即关机'; Remaining=0 }
    }
    # Mark that we've seen Safe state after being Protected, but only when the client is still present (not Absent).
    # This prevents the reconnect buffer from triggering on future client exits after settlement.
    if ($Observation.State -eq 'Safe' -and -not $Observation.Absent -and $null -ne $Gate.LastProtected) {
        $Gate.SafeAfterProtected=$true
    }
    if ($null -eq $Gate.SafeSince) { $Gate.SafeSince=$Now }
    if (($Now-$Gate.SafeSince).TotalSeconds -lt 10) {
        return [pscustomobject]@{ State='Confirming'; Message='已无进行中的对局，正在复查'; Remaining=0 }
    }
    if ($null -eq $Gate.CountdownSince) { $Gate.CountdownSince=$Now }
    $remaining = [Math]::Max(0,[Math]::Ceiling($CountdownSeconds-($Now-$Gate.CountdownSince).TotalSeconds))
    if ($remaining -gt 0) {
        return [pscustomobject]@{ State='Countdown'; Message="将在 $remaining 秒后关机，可在联盟视界设置中删除计划"; Remaining=$remaining }
    }
    return [pscustomobject]@{ State='Ready'; Message='已确认没有进行中的对局，准备关机'; Remaining=0 }
}

function Get-ScheduledOccurrence([string]$Time, [datetime]$Now) {
    $clock = [datetime]::ParseExact($Time,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
    $occurrence=$Now.Date.AddHours($clock.Hour).AddMinutes($clock.Minute)
    if ($occurrence -gt $Now) { $occurrence=$occurrence.AddDays(-1) }
    return $occurrence
}
function Test-ShutdownPlan($Plan) {
    try {
        return $Plan.Version -eq 1 -and $Plan.Enabled -is [bool] -and $Plan.Enabled -eq $true -and
            $Plan.Generation -match '^[a-f0-9]{32}$' -and $Plan.Time -cmatch '^([01][0-9]|2[0-3]):[0-5][0-9]$' -and
            $Plan.CountdownSeconds -is [int] -and $Plan.CountdownSeconds -ge 0 -and $Plan.CountdownSeconds -le 300
    } catch { return $false }
}
function Read-LocalJson([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    if ((Get-Item -LiteralPath $Path).Length -gt 16384) { throw 'Invalid configuration size' }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Replace-ShutdownStateFile([string]$Temporary,[string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        # Windows PowerShell 5.1 converts $null to an empty backup path; use an explicit null string.
        [IO.File]::Replace($Temporary,$Path,[System.Management.Automation.Language.NullString]::Value)
    }
    else {
        [IO.File]::Move($Temporary,$Path)
    }
}
function Write-ShutdownState([string]$State,[string]$Message,[string]$Directory=$PSScriptRoot) {
    $data=@{ State=$State; Message=$Message; Occurrence=$script:occurrenceKey; UpdatedAt=[datetimeoffset]::UtcNow.ToString('o') }
    $path=Join-Path $Directory 'status.json'; $temporary=$path+'.tmp'
    [IO.File]::WriteAllText($temporary,($data | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    Replace-ShutdownStateFile $temporary $path
    if ($script:lastLogged -ne $State) {
        Add-Content -LiteralPath (Join-Path $Directory 'shutdown.log') -Encoding UTF8 -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss')+" [$State] $Message")
        $script:lastLogged=$State
    }
}
function Write-ShutdownFailure([Exception]$Failure,[string]$Directory=$PSScriptRoot) {
    $message='状态检测或文件操作失败，已停止本次关机：'+$Failure.GetBaseException().GetType().Name
    try { Write-ShutdownState 'Failed' $message $Directory }
    catch {
        # A broken state file must not hide the failure from the durable log or Task Scheduler.
        try {
            Add-Content -LiteralPath (Join-Path $Directory 'shutdown.log') -Encoding UTF8 -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss')+' [Failed] '+$message)
        } catch { } # Stderr and a nonzero exit code remain if even the log directory is unavailable.
    }
    [Console]::Error.WriteLine($message)
}
function Test-RunCancelled($Initial) {
    if ($script:notificationCancelled) { return $true }
    $current=Read-LocalJson (Join-Path $PSScriptRoot 'plan.json')
    if (-not (Test-ShutdownPlan $current) -or $current.Generation -ne $Initial.Generation) { return $true }
    $cancel=Read-LocalJson (Join-Path $PSScriptRoot 'cancel.json')
    return $null -ne $cancel -and $cancel.Occurrence -eq $script:occurrenceKey
}
function Show-ShutdownNotice([int]$Seconds) {
    if ($null -eq $script:notice) {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $script:notice=New-Object Windows.Forms.NotifyIcon
        $script:notice.Icon=[Drawing.SystemIcons]::Information
        $script:notice.Text='联盟视界 · 定时关机（双击取消本次）'
        $script:notice.add_BalloonTipClicked({$script:notificationCancelled=$true})
        $script:notice.add_DoubleClick({$script:notificationCancelled=$true})
        $script:notice.Visible=$true
    }
    $script:notice.ShowBalloonTip(30000,'联盟视界 · 定时关机',"将在 $Seconds 秒后关机。点击此提示或双击托盘图标取消本次。",[Windows.Forms.ToolTipIcon]::Info)
}

if ($LibraryOnly) { return }
if ($Probe -or -not $Run) { Get-GameObservation | ConvertTo-Json -Compress; return }

$script:lastLogged=''; $script:occurrenceKey=''; $script:notificationCancelled=$false; $script:notice=$null
$mutex=New-Object Threading.Mutex($false,'Local\LeagueHistory.ScheduledShutdown')
$ownsMutex=$false
try {
    try { $ownsMutex=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex=$true }
    if (-not $ownsMutex) { return }
    $plan=Read-LocalJson (Join-Path $PSScriptRoot 'plan.json')
    if (-not (Test-ShutdownPlan $plan)) { return }
    $started=Get-Date; $occurrence=Get-ScheduledOccurrence $plan.Time $started
    $script:occurrenceKey=$occurrence.ToString('yyyy-MM-ddTHH:mm:ss')
    # Never perform a missed-night shutdown when Windows starts the task late.
    if (($started-$occurrence).TotalMinutes -gt 10) { Write-ShutdownState 'Expired' '已超过计划启动时段，本次不执行关机'; return }
    $log=Join-Path $PSScriptRoot 'shutdown.log'
    if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).Length -gt 524288) { Move-Item -LiteralPath $log -Destination ($log+'.previous') -Force }
    $gate=New-ShutdownGate; $notified=$false; $lastSample=$started
    while ($true) {
        if (Test-RunCancelled $plan) { Write-ShutdownState 'Cancelled' '本次关机已取消；之后仍按每日计划执行'; break }
        $now=Get-Date
        if (($now-$started).TotalHours -ge 6 -or $now -lt $started) { Write-ShutdownState 'Expired' '等待已超时或系统时间发生变化，本次不执行关机'; break }
        if (($now-$lastSample).TotalSeconds -gt 45) { $gate.SafeSince=$null; $gate.CountdownSince=$null; $notified=$false }
        $lastSample=$now
        $observation=Get-GameObservation
        $decision=Get-ShutdownDecision $gate $observation (Get-Date) $plan.CountdownSeconds
        # Replacing or deleting a plan during an API request must still cancel it.
        if (Test-RunCancelled $plan) { Write-ShutdownState 'Cancelled' '本次关机已取消'; break }
        Write-ShutdownState $decision.State $decision.Message
        if ($decision.State -eq 'Ready') {
            # Countdown runs in this worker, not shutdown.exe: /t > 0 would imply /f.
            $shutdownProcess=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\shutdown.exe') -ArgumentList @('/s','/t','0') -WindowStyle Hidden -PassThru -Wait
            if ($shutdownProcess.ExitCode -eq 0) { Write-ShutdownState 'Requested' '已请求 Windows 正常关机；若被未保存的应用阻止，请处理系统提示' }
            else { Write-ShutdownState 'Failed' ('Windows 未接受关机请求，错误码 '+$shutdownProcess.ExitCode) }
            $shutdownProcess.Dispose(); break
        }
        if ($decision.State -eq 'Countdown' -and -not $notified) {
            try { Show-ShutdownNotice ([int]$decision.Remaining) } catch { } # Notifications may be disabled; the settings status remains available.
            $notified=$true
        }
        if ($decision.State -in @('WaitingGame','WaitingState')) { $notified=$false }
        $pause=if ($decision.State -in @('WaitingGame','WaitingState')) { 5 } else { 1 }
        for ($i=0;$i -lt $pause*5;$i++) {
            if ($null -ne $script:notice) { [Windows.Forms.Application]::DoEvents() }
            Start-Sleep -Milliseconds 200
        }
    }
} catch {
    # No exception path requests shutdown, and no API credentials are included in diagnostics.
    Write-ShutdownFailure $_.Exception
    exit 1
} finally {
    if ($null -ne $script:notice) { $script:notice.Visible=$false; $script:notice.Dispose() }
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
