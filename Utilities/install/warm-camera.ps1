<#
  Invoke-CameraWarmUp warms the camera before NINA stops, so the cooler never drops while the sensor is cold.
  NINA turns the cooler off when it exits; on 2026-10-16 that took the sensor from -16 C to +4 C in about a minute.

  It asks the Observatory Scheduler plugin (5.8.2.234 or later) by creating camera-warmup.request in the plugin
  folder and touching it every poll. The plugin holds imaging, ramps the setpoint from the sensor to the ambient at
  1.5 C per minute and reports in camera-warmup.status.json. The request stays in place after 'done' so imaging stays
  held until NINA exits; Remove-CameraWarmUpRequest removes it.

  Returns 'not-connected', 'no-cooler', 'warm' or 'warmed'. Throws when the plugin does not answer, reports
  'failed', or the deadline passes; the request is removed then, so imaging resumes and NINA keeps running.
#>

function Get-WarmUpApi([string]$Url) {
  $r = Invoke-RestMethod -UseBasicParsing -Uri $Url -TimeoutSec 10
  if (-not $r.Success) { return $null }
  return $r.Response
}

function ConvertTo-WarmUpNumber($v) {
  $d = 0.0
  if ($null -ne $v -and [double]::TryParse([string]$v, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$d) -and -not [double]::IsNaN($d)) { return $d }
  return $null
}

function Get-WarmUpAmbient([string]$Api) {
  try {
    $w = Get-WarmUpApi "$Api/equipment/weather/info"
    if ($null -ne $w -and $w.Connected) { return ConvertTo-WarmUpNumber $w.Temperature }
  } catch {
  }
  return $null
}

function Remove-CameraWarmUpRequest([string]$PluginHome = 'C:\Users\NINA\AppData\Local\NINA\SchedulerPlugin') {
  Remove-Item (Join-Path $PluginHome 'camera-warmup.request') -Force -ErrorAction SilentlyContinue
}

function Invoke-CameraWarmUp {
  param(
    [string]$Api = 'http://localhost:1888/v2/api',
    [string]$PluginHome = 'C:\Users\NINA\AppData\Local\NINA\SchedulerPlugin',
    [int]$DeadlineMinutes = 90,
    [int]$AnswerSeconds = 60,
    [int]$PollSeconds = 10,
    [double]$NearC = 2
  )
  $cam = Get-WarmUpApi "$Api/equipment/camera/info"
  if ($null -eq $cam -or -not $cam.Connected) {
    Write-Host "warm-up $(Get-Date -Format 'HH:mm:ss'): the camera is not connected, so NINA cannot drop its cooler"
    return 'not-connected'
  }
  if (-not $cam.CanSetTemperature) {
    Write-Host "warm-up $(Get-Date -Format 'HH:mm:ss'): the camera has no cooler"
    return 'no-cooler'
  }
  $sensor = ConvertTo-WarmUpNumber $cam.Temperature
  $ambient = Get-WarmUpAmbient $Api
  $target = 20.0
  if ($null -ne $ambient) { $target = [math]::Floor($ambient) }
  if ($null -ne $sensor -and $sensor -ge $target - $NearC) {
    Write-Host ("warm-up {0}: the sensor is at {1} C, within {2} C of {3} C (ambient {4}); no warm-up needed" -f (Get-Date -Format 'HH:mm:ss'), $sensor, $NearC, $target, $ambient)
    return 'warm'
  }

  $req = Join-Path $PluginHome 'camera-warmup.request'
  $st = Join-Path $PluginHome 'camera-warmup.status.json'
  Remove-Item $st -Force -ErrorAction SilentlyContinue
  $id = "requested $(Get-Date -Format o) by install-plugin.ps1"
  Set-Content -Path $req -Value $id -NoNewline
  Write-Host ("warm-up {0}: asked the plugin to warm the camera from {1} C (cooler {2}%, setpoint {3} C) to {4} C (ambient {5})" -f (Get-Date -Format 'HH:mm:ss'), $sensor, $cam.CoolerPower, $cam.TemperatureSetPoint, $target, $ambient)
  $start = Get-Date
  $deadline = $start.AddMinutes($DeadlineMinutes)
  $lastLog = [datetime]::MinValue
  $prev = $null
  try {
    while ($true) {
      (Get-Item $req).LastWriteTime = Get-Date
      $s = $null
      if (Test-Path $st) { try { $s = Get-Content $st -Raw | ConvertFrom-Json } catch { $s = $null } }
      if ($null -ne $s -and ([string]$s.Request).Trim() -ne $id) { $s = $null }
      $c = $null
      try { $c = Get-WarmUpApi "$Api/equipment/camera/info" } catch { $c = $null }
      $now = Get-Date
      $t = $null
      if ($null -ne $c) { $t = ConvertTo-WarmUpNumber $c.Temperature }
      if ($null -ne $prev -and $null -ne $t) {
        $rate = ($t - $prev.T) / [math]::Max(1, ($now - $prev.At).TotalMinutes)
        if ($rate -gt 3) { Write-Host ("warm-up {0}: WARNING the sensor rose {1:0.0} C per minute" -f ($now.ToString('HH:mm:ss')), $rate) }
      }
      if ($null -ne $t) { $prev = @{ T = $t; At = $now } }
      $state = 'no answer yet'
      if ($null -ne $s) { $state = "$($s.State): $($s.Reason)" }
      if (($now - $lastLog).TotalSeconds -ge 60 -or ($null -ne $s -and $s.State -ne 'warming')) {
        if ($null -ne $c) {
          Write-Host ("warm-up {0}: sensor {1} C, setpoint {2} C, cooler {3}%; {4}" -f ($now.ToString('HH:mm:ss')), $c.Temperature, $c.TemperatureSetPoint, $c.CoolerPower, $state)
        } else {
          Write-Host ("warm-up {0}: camera info unavailable; {1}" -f ($now.ToString('HH:mm:ss')), $state)
        }
        $lastLog = $now
      }
      if ($null -eq $s -and ($now - $start).TotalSeconds -gt $AnswerSeconds) {
        throw "warm-up: the plugin did not answer within $AnswerSeconds s (it needs Observatory Scheduler 5.8.2.234 or later) and the sensor is at $sensor C; not stopping NINA"
      }
      if ($null -ne $s -and $s.State -eq 'failed') { throw "warm-up failed: $($s.Reason); not stopping NINA" }
      if ($null -ne $s -and $s.State -eq 'done') {
        Write-Host ("warm-up {0}: done after {1:0.0} min: {2}" -f ($now.ToString('HH:mm:ss')), ($now - $start).TotalMinutes, $s.Reason)
        return 'warmed'
      }
      if ($now -gt $deadline) { throw "warm-up: not done within $DeadlineMinutes min (last: $state); not stopping NINA" }
      Start-Sleep -Seconds $PollSeconds
    }
  } catch {
    Remove-Item $req -Force -ErrorAction SilentlyContinue
    throw
  }
}

function Update-CameraWarmUpRequest([string]$PluginHome = 'C:\Users\NINA\AppData\Local\NINA\SchedulerPlugin') {
  $req = Join-Path $PluginHome 'camera-warmup.request'
  if (Test-Path $req) { (Get-Item $req).LastWriteTime = Get-Date }
}
