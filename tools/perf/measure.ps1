# Frame rate and memory at the review viewpoints, from the running game (bwm.ps1 play first).
#   measure.ps1 [-Island orig|winter|both] [-Seconds 4] [-Quality 4] [-Label text]
# At each point in tools\review\review-viewpoints.txt: sets the player on the ground facing the planned heading,
# sets the hour to 13, waits, measures (the companion's perf). Uncapped while it runs, back to 30 fps after.
# Prints one line a point, then the mean, the process's memory (private, in RAM) and its video memory (Windows'
# "GPU Process Memory" counters: dedicated = VRAM). Appends the summary to logs\perf-measure.log.
param([ValidateSet('orig', 'winter', 'both')][string]$Island = 'both', [double]$Seconds = 4, [int]$Quality = 4, [string]$Label = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function C([string]$q, [int]$timeout = 60) { (Invoke-WebRequest -Uri ("http://127.0.0.1:17361/" + $q) -UseBasicParsing -TimeoutSec $timeout).Content }
function Memory {
    $p = Get-Process | Where-Object { $_.ProcessName -eq 'Big Walk' } | Select-Object -First 1
    if (-not $p) { return $null }
    $vram = 0; $shared = 0
    try {
        $c = Get-Counter -Counter '\GPU Process Memory(*)\Dedicated Usage', '\GPU Process Memory(*)\Shared Usage' -ErrorAction Stop
        foreach ($s in $c.CounterSamples) {
            if ($s.InstanceName -notmatch ("pid_" + $p.Id + "_")) { continue }
            if ($s.Path -match 'dedicated') { $vram += $s.CookedValue } else { $shared += $s.CookedValue }
        }
    } catch { }
    [pscustomobject]@{ Private = $p.PrivateMemorySize64 / 1GB; Ram = $p.WorkingSet64 / 1GB; Vram = $vram / 1GB; Shared = $shared / 1GB }
}
$inv = [Globalization.CultureInfo]::InvariantCulture
$points = Get-Content (Join-Path $repo 'tools\review\review-viewpoints.txt') | Where-Object { $_ -match '^\S' } | ForEach-Object {
    $f = $_ -split '\s+'
    [pscustomobject]@{ Island = $f[0]; Name = $f[1]; X = [double]::Parse($f[2], $inv); Z = [double]::Parse($f[3], $inv); Yaw = [math]::Atan2([double]::Parse($f[4], $inv), [double]::Parse($f[5], $inv)) * 180 / [math]::PI }
}
if ($Island -ne 'both') { $points = $points | Where-Object { $_.Island -eq $Island } }
C ("render?quality=$Quality&awake=1") | Out-Null
C 'set?path=UnityEngine.Application.targetFrameRate&value=-1' | Out-Null
$rows = @()
try {
    foreach ($pt in $points) {
        $x = $pt.X.ToString($inv); $z = $pt.Z.ToString($inv)
        C "teleport?x=$x&y=150&z=$z" | Out-Null
        Start-Sleep -Milliseconds 2500
        $g = $null
        if ((C "ground?x=$x&z=$z&all=1") -match 'ground: (-?[0-9.]+)') { $g = [double]::Parse($Matches[1], $inv) }
        if ($null -eq $g -or $g -lt 0.5) { Write-Output ("{0,-4} no ground to stand on, skipped" -f $pt.Name); continue }
        C ("teleport?x=$x&y=" + ($g + 1.3).ToString($inv) + "&z=$z&yaw=" + $pt.Yaw.ToString('0.0', $inv)) | Out-Null
        C 'time?hour=13' | Out-Null
        Start-Sleep -Milliseconds 2500
        $r = C ("perf?s=" + $Seconds.ToString($inv)) 60
        if ($r -match 'fps ([0-9.]+) \(1% low ([0-9.]+), worst ([0-9.]+) ms') {
            $rows += [pscustomobject]@{ Island = $pt.Island; Name = $pt.Name; Fps = [double]::Parse($Matches[1], $inv); Low = [double]::Parse($Matches[2], $inv); Worst = [double]::Parse($Matches[3], $inv) }
            Write-Output ("{0,-4} fps {1,6:0.0}  1% low {2,6:0.0}  worst {3,5:0.0} ms" -f $pt.Name, $rows[-1].Fps, $rows[-1].Low, $rows[-1].Worst)
        } else { Write-Output ("{0,-4} {1}" -f $pt.Name, $r) }
    }
} finally {
    try { C 'set?path=UnityEngine.Application.targetFrameRate&value=30' | Out-Null } catch { }
}
$m = Memory
$summary = @()
foreach ($isl in ($rows | Select-Object -ExpandProperty Island -Unique)) {
    $set = $rows | Where-Object { $_.Island -eq $isl }
    $summary += ("{0}: mean {1:0.0} fps, mean 1% low {2:0.0}, lowest point {3:0.0} ({4} points)" -f $isl, ($set | Measure-Object Fps -Average).Average, ($set | Measure-Object Low -Average).Average, ($set | Measure-Object Fps -Minimum).Minimum, $set.Count)
}
if ($m) { $summary += ("memory: {0:0.00} GB private, {1:0.00} GB in RAM, {2:0.00} GB video memory (dedicated), {3:0.00} GB shared" -f $m.Private, $m.Ram, $m.Vram, $m.Shared) }
$summary | ForEach-Object { Write-Output $_ }
$log = Join-Path $repo 'logs\perf-measure.log'
Add-Content -Path $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " quality $Quality $Label") -Encoding utf8
$summary | ForEach-Object { Add-Content -Path $log -Value ("  " + $_) -Encoding utf8 }
