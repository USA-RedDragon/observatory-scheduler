<#
  Wait-SafeStop blocks until NINA's sequence can be cancelled without losing a frame or its
  Target Scheduler acquiredimage row. Safe means, held for StableSeconds:
    1. the camera is not Waiting, Exposing, Reading or Download;
    2. no save is pending: the newest image-history entry was saved after the camera's
       ExposureEndTime, or ExposureEndTime is more than SaveGraceSeconds old;
    3. if the newest LIGHT in image-history was saved in the last TsWindowSeconds, the
       schedulerdb has an acquiredimage row with the same filter whose acquireddate lies
       between (save time - exposure - slack) and the save time.
  Returns 'safe' or 'nina-not-running'. Throws when the deadline passes.
#>

$script:SafeStopTicksOffset = 621355968000000000

function ConvertTo-SafeStopUnix($v) {
  if ($null -eq $v -or "$v" -eq '') { return $null }
  if ($v -is [datetime]) { $dto = [DateTimeOffset]$v }
  else { $dto = [DateTimeOffset]::Parse([string]$v, [Globalization.CultureInfo]::InvariantCulture) }
  if ($dto.Year -lt 2000) { return $null }
  return $dto.ToUnixTimeMilliseconds() / 1000.0
}

function Invoke-SafeStopApi([string]$Url) {
  $r = Invoke-RestMethod -UseBasicParsing -Uri $Url -TimeoutSec 5
  if (-not $r.Success) { return $null }
  return $r.Response
}

function Get-SafeStopTsRows([string]$Sqlite, [string]$Db, [string]$Filter, [double]$Lo, [double]$Hi) {
  $f = $Filter -replace "'", "''"
  $d = "(CASE WHEN acquireddate > 100000000000 THEN (acquireddate - $script:SafeStopTicksOffset) / 10000000 ELSE acquireddate END)"
  $where = "$d BETWEEN $([math]::Floor($Lo)) AND $([math]::Ceiling($Hi))"
  if ($Filter) { $where += " AND filtername = '$f'" }
  $out = & $Sqlite -batch -noheader -cmd '.timeout 10000' $Db "SELECT count(*) FROM acquiredimage WHERE $where;" 2>&1
  if ($LASTEXITCODE -ne 0) { throw "sqlite3 exit ${LASTEXITCODE}: $out" }
  return [int](("$out").Trim())
}

function Get-SafeStopState {
  param(
    [string]$Api,
    [string]$Sqlite,
    [string]$Db,
    [int]$SaveGraceSeconds,
    [int]$TsWindowSeconds,
    [double]$Now
  )
  $s = [ordered]@{ Safe = $false; Reason = '' }
  $cam = Invoke-SafeStopApi "$Api/equipment/camera/info"
  if ($null -eq $cam) { $s.Reason = 'camera info unavailable'; return $s }
  $state = [string]$cam.CameraState
  $s.Camera = $state
  if ($cam.Connected -and @('Waiting', 'Exposing', 'Reading', 'Download') -contains $state) {
    if ($state -eq 'Exposing') {
      $just = @(Invoke-SafeStopApi "$Api/image-history?index=-1")
      if ($just.Count -eq 1 -and $null -ne $just[0] -and @('DARK', 'BIAS') -contains [string]$just[0].ImageType) {
        $justAt = ConvertTo-SafeStopUnix $just[0].Date
        if ($null -ne $justAt -and $Now - $justAt -lt 20) {
          $s.Safe = $true
          $s.Reason = ('{0} saved {1:0}s ago, the next one just started and is dropped' -f $just[0].ImageType, ($Now - $justAt))
          return $s
        }
      }
    }
    $s.Reason = "camera $state"; return $s
  }
  $count = Invoke-SafeStopApi "$Api/image-history?count=true"
  if ($null -eq $count) { $s.Reason = 'image-history count unavailable'; return $s }
  $s.History = [int]$count
  $newestAt = $null
  if ([int]$count -gt 0) {
    $newest = @(Invoke-SafeStopApi "$Api/image-history?index=-1")
    if ($newest.Count -ne 1 -or $null -eq $newest[0]) { $s.Reason = 'image-history index=-1 unavailable'; return $s }
    $newestAt = ConvertTo-SafeStopUnix $newest[0].Date
  }
  $endAt = ConvertTo-SafeStopUnix $cam.ExposureEndTime
  if ($null -ne $endAt -and $Now -lt $endAt + $SaveGraceSeconds -and ($null -eq $newestAt -or $newestAt -lt $endAt)) {
    $s.Reason = ('save pending for exposure ended {0:0}s ago' -f ($Now - $endAt)); return $s
  }
  $lights = Invoke-SafeStopApi "$Api/image-history?count=true&imageType=LIGHT"
  if ($null -eq $lights) { $s.Reason = 'LIGHT count unavailable'; return $s }
  if ([int]$lights -gt 0) {
    $light = @(Invoke-SafeStopApi "$Api/image-history?index=-1&imageType=LIGHT")
    if ($light.Count -ne 1 -or $null -eq $light[0]) { $s.Reason = 'newest LIGHT unavailable'; return $s }
    $savedAt = ConvertTo-SafeStopUnix $light[0].Date
    if ($null -ne $savedAt -and $Now - $savedAt -lt $TsWindowSeconds) {
      $exp = [double]$light[0].ExposureTime
      $slack = [math]::Min(120, [math]::Max(15, $exp / 2))
      $rows = Get-SafeStopTsRows $Sqlite $Db ([string]$light[0].Filter) ($savedAt - $exp - $slack) ($savedAt + 2)
      $s.TsRows = $rows
      if ($rows -lt 1) {
        $s.Reason = ('no TS acquiredimage row yet for the {0} {1}s light saved {2:0}s ago' -f $light[0].Filter, $exp, ($Now - $savedAt)); return $s
      }
    }
  }
  $s.Safe = $true
  $s.Reason = 'idle after save'
  return $s
}

function Wait-SafeStop {
  param(
    [string]$Api = 'http://localhost:1888/v2/api',
    [string]$Sqlite = 'C:\ProgramData\write-tuning\bin\sqlite3.exe',
    [string]$Db = 'C:\Users\NINA\AppData\Local\NINA\SchedulerPlugin\schedulerdb.sqlite',
    [string]$NinaProcess = 'NINA',
    [int]$DeadlineSeconds = 1800,
    [int]$SaveGraceSeconds = 60,
    [int]$TsWindowSeconds = 900,
    [int]$StableSeconds = 2,
    [double]$PollSeconds = 1
  )
  if (-not (Test-Path $Sqlite)) { throw "safe-stop: $Sqlite missing, cannot confirm Target Scheduler rows" }
  if (-not (Test-Path $Db)) { throw "safe-stop: $Db missing" }
  $deadline = (Get-Date).AddSeconds($DeadlineSeconds)
  $since = $null
  $last = ''
  while ($true) {
    if (-not (Get-Process $NinaProcess -ErrorAction SilentlyContinue)) {
      Write-Host "safe-stop $(Get-Date -Format 'HH:mm:ss'): $NinaProcess is not running"
      return 'nina-not-running'
    }
    $now = [DateTimeOffset]::Now.ToUnixTimeMilliseconds() / 1000.0
    try {
      $st = Get-SafeStopState -Api $Api -Sqlite $Sqlite -Db $Db -SaveGraceSeconds $SaveGraceSeconds -TsWindowSeconds $TsWindowSeconds -Now $now
    } catch {
      $st = @{ Safe = $false; Reason = "error: $($_.Exception.Message)" }
    }
    if ($st.Reason -ne $last) { Write-Host "safe-stop $(Get-Date -Format 'HH:mm:ss'): $($st.Reason)"; $last = $st.Reason }
    if ($st.Safe) {
      if ($null -eq $since) { $since = $now }
      if ($now - $since -ge $StableSeconds) { return 'safe' }
    } else {
      $since = $null
    }
    if ((Get-Date) -gt $deadline) { throw "safe-stop: no safe point within $DeadlineSeconds s (last: $($st.Reason)); not stopping NINA" }
    Start-Sleep -Milliseconds ([int]($PollSeconds * 1000))
  }
}
