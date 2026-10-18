param(
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$Zip = '',
  [int]$DeadlineSeconds = 1800,
  [switch]$NinaApi
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'safe-stop.ps1')
. (Join-Path $PSScriptRoot 'warm-camera.ps1')
$plug = 'C:\Users\NINA\AppData\Local\NINA\Plugins\3.0.0\Observatory Scheduler'
$dbdir = 'C:\Users\NINA\AppData\Local\NINA\SchedulerPlugin'
$props = 'C:\symmetric-ds\engines\observatory.properties'
$main = 'C:\Users\NINA\Documents\N.I.N.A\Main.json'
if (-not $Zip) { $Zip = Join-Path $PSScriptRoot "observatory-scheduler-$Version.zip" }
$tag = 'r' + ($Version -split '\.')[-1]
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$bk = "C:\ProgramData\observatory-scheduler\backup\$tag-$ts"
$stage = "C:\ProgramData\observatory-scheduler\stage\$Version"
if (-not (Test-Path "$plug\NINA.Plugin.TargetScheduler.dll")) { throw "plugin folder missing" }
if (-not (Test-Path "$dbdir\schedulerdb.sqlite")) { throw "db missing" }
if (-not (Test-Path $props)) { throw "observatory.properties missing" }
if (-not (Test-Path $Zip)) { throw "$Zip missing" }
"current plugin: " + (Get-Item "$plug\NINA.Plugin.TargetScheduler.dll").VersionInfo.FileVersion
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Expand-Archive $Zip -DestinationPath $stage
$new = Join-Path $stage 'Observatory Scheduler'
$staged = (Get-Item "$new\NINA.Plugin.TargetScheduler.dll").VersionInfo.FileVersion
"staged: $staged"
if ($staged -ne $Version) { throw "staged version is $staged" }
$result = Wait-SafeStop -DeadlineSeconds $DeadlineSeconds
"safe point: $result $(Get-Date -Format 'HH:mm:ss')"
Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'powershell.exe' -and $_.CommandLine -match 'Startup\.ps1' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force; "killed watchdog $($_.ProcessId)" }
if ($result -eq 'safe') {
  try {
    $warm = Invoke-CameraWarmUp -PluginHome $dbdir
    "camera: $warm $(Get-Date -Format 'HH:mm:ss')"
    $result = Wait-SafeStop -DeadlineSeconds 120 -StableSeconds 0
  } catch {
    Remove-CameraWarmUpRequest $dbdir
    schtasks /run /tn NINA | Out-Null
    throw
  }
}
if ($result -eq 'safe') {
  try { Invoke-WebRequest -UseBasicParsing http://localhost:1888/v2/api/sequence/stop -TimeoutSec 10 | Out-Null; "sequence stop sent $(Get-Date -Format 'HH:mm:ss')" } catch { "stop api failed: $_" }
}
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Process NINA -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep 2 }
if (Get-Process NINA -ErrorAction SilentlyContinue) { "NINA did not exit, closing window"; Get-Process NINA | ForEach-Object { $_.CloseMainWindow() | Out-Null }; Start-Sleep 15 }
if (Get-Process NINA -ErrorAction SilentlyContinue) { "forcing NINA"; Stop-Process -Name NINA -Force; Start-Sleep 3 }
Remove-CameraWarmUpRequest $dbdir
New-Item -ItemType Directory -Force "$bk\db" | Out-Null
Copy-Item "$dbdir\schedulerdb.sqlite*" "$bk\db\"
if (Test-Path "$dbdir\observatory-scheduler-darks.json") { Copy-Item "$dbdir\observatory-scheduler-darks.json" "$bk\" }
Copy-Item $plug "$bk\plugin-previous" -Recurse
Copy-Item $props "$bk\"
Copy-Item $main "$bk\Main.json"
New-Item -ItemType Directory -Force "A:\observatory-scheduler-backup\$tag-$ts" | Out-Null
Copy-Item "$bk\*" "A:\observatory-scheduler-backup\$tag-$ts\" -Recurse
"backup: $bk"
Get-ChildItem $bk -Recurse -File | Select-Object FullName, Length | Format-Table -AutoSize | Out-String
Remove-Item $plug -Recurse -Force
Copy-Item $new $plug -Recurse
"installed: " + (Get-Item "$plug\NINA.Plugin.TargetScheduler.dll").VersionInfo.FileVersion
if ($NinaApi) { try { & 'A:\ninaapi\install-ninaapi.ps1' } catch { "ninaapi install failed: $_" } }
schtasks /run /tn NINA
Start-Sleep 30
Get-Process NINA,phd2 -ErrorAction SilentlyContinue | Select-Object Name,Id,StartTime | Format-Table -AutoSize | Out-String
