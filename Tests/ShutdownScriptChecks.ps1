param([string]$ScriptPath = (Join-Path $PSScriptRoot '..\Scripts\ShutdownAfterLoL.ps1'))
$ErrorActionPreference='Stop'
. $ScriptPath -LibraryOnly
function Assert([bool]$value,[string]$message) { if (-not $value) { throw $message } }
$clock=[datetime]'2026-09-17T23:00:00'
foreach ($phase in @('ReadyCheck','ChampSelect','GameStart','InProgress','Reconnect')) {
    Assert ((Resolve-GameObservation $phase $false $true).State -eq 'Protected') "Protect $phase even before the process exists"
}
foreach ($phase in @('PreEndOfGame','WaitingForStats','EndOfGame')) {
    Assert ((Resolve-GameObservation $phase $true $true).State -eq 'Safe') "Finish on $phase even while the client is exiting"
}
Assert ((Resolve-GameObservation 'Lobby' $false $true).State -eq 'Safe') 'Lobby does not delay shutdown'
Assert ((Resolve-GameObservation 'Lobby' $true $true).State -eq 'Protected') 'Live game wins over a conflicting lobby phase'
Assert ((Resolve-GameObservation '' $false $true).State -eq 'Unknown') 'LCU failure does not mean no game'
Assert ((Resolve-GameObservation '' $true $false).State -eq 'Protected') 'A standalone game process remains protected'
$absent=Resolve-GameObservation '' $false $false
$playing=Resolve-GameObservation 'InProgress' $true $true
$ended=Resolve-GameObservation 'EndOfGame' $true $true
$gate=New-ShutdownGate
Assert ((Get-ShutdownDecision $gate $absent $clock 30).State -eq 'Confirming') 'First absence must be rechecked'
Assert ((Get-ShutdownDecision $gate $absent $clock.AddSeconds(10) 30).Remaining -eq 30) 'Countdown starts after a stable safe state'
Assert ((Get-ShutdownDecision $gate $absent $clock.AddSeconds(39) 30).Remaining -eq 1) 'Countdown retains its last second'
Assert ((Get-ShutdownDecision $gate $absent $clock.AddSeconds(40) 30).State -eq 'Ready') 'No-game schedule reaches shutdown decision'
Assert ((Get-ShutdownDecision $gate $playing $clock.AddSeconds(41) 30).State -eq 'WaitingGame') 'New game cancels a ready countdown'
Assert ((Get-ShutdownDecision $gate $absent $clock.AddSeconds(42) 30).State -eq 'WaitingState') 'A crash is not treated as immediate completion'
Assert ((Get-ShutdownDecision $gate $absent $clock.AddSeconds(160) 30).State -eq 'WaitingState') 'Reconnect grace remains active'
Assert ((Get-ShutdownDecision $gate $ended $clock.AddSeconds(161) 30).State -eq 'Confirming') 'Actual end begins a new stable check'
Assert ((Get-ShutdownDecision $gate $ended $clock.AddSeconds(171) 30).Remaining -eq 30) 'New countdown uses the full duration'
# Test the fix: after reaching Safe state (settlement), client exit should proceed to countdown, not stay in WaitingState.
$gate2=New-ShutdownGate
Assert ((Get-ShutdownDecision $gate2 $playing $clock 30).State -eq 'WaitingGame') 'Game in progress is protected'
Assert ((Get-ShutdownDecision $gate2 $ended $clock.AddSeconds(5) 30).State -eq 'Confirming') 'Settlement detected, entering confirmation'
Assert ((Get-ShutdownDecision $gate2 $ended $clock.AddSeconds(15) 30).Remaining -eq 30) 'Countdown starts after confirmation'
Assert ((Get-ShutdownDecision $gate2 $absent $clock.AddSeconds(20) 30).Remaining -gt 0) 'Client exit after settlement proceeds to countdown, not stuck in WaitingState'
Assert ((Get-ShutdownDecision $gate2 $absent $clock.AddSeconds(50) 30).State -eq 'Ready') 'Countdown completes successfully after client exit'
$unknown=Resolve-GameObservation '' $false $true
Assert ((Get-ShutdownDecision $gate $unknown $clock.AddSeconds(180) 30).State -eq 'WaitingState') 'Uncertain state cancels countdown'
Assert ((Get-ShutdownDecision $gate $ended $clock.AddSeconds(181) 0).State -eq 'Confirming') 'Zero delay still checks safe state'
Assert ((Get-ShutdownDecision $gate $ended $clock.AddSeconds(191) 0).State -eq 'Ready') 'Zero delay has no extra countdown'
Assert ((Get-ScheduledOccurrence '23:59' ([datetime]'2026-09-18T00:01:00')) -eq [datetime]'2026-09-17T23:59:00') 'Midnight still belongs to the previous scheduled occurrence'
$plan=[pscustomobject]@{Version=1;Enabled=$true;Generation=('a'*32);Time='23:00';CountdownSeconds=30}
Assert (Test-ShutdownPlan $plan) 'Valid plan accepted'
$plan.Enabled=$false; Assert (-not (Test-ShutdownPlan $plan)) 'Disabled plan cannot run'
$plan.Enabled=$true; $plan.CountdownSeconds=-1; Assert (-not (Test-ShutdownPlan $plan)) 'Negative delay rejected'
$plan.CountdownSeconds=30; $plan.Time='25:00'; Assert (-not (Test-ShutdownPlan $plan)) 'Invalid clock time rejected'
Assert (-not (Test-ShutdownPlan $null)) 'Missing configuration cannot run'
$helper=[regex]::Match([IO.File]::ReadAllText($ScriptPath),"(?s)Add-Type -TypeDefinition @'\r?\n(.*?)\r?\n'@").Groups[1].Value
Assert ($helper.Length -gt 0) 'Loopback probe helper is present'
Add-Type -TypeDefinition $helper
Assert ([LeagueShutdown.Loopback]::Phase(0,'unused') -eq '') 'Loopback probe compiles and rejects an invalid port without requests'
'PASS: PowerShell phase classification, new-game interruption, reconnect grace, countdown, zero delay, midnight and invalid plans. No shutdown executed.'
