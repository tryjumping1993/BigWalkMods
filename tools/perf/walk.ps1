# Walks the test player over the ground as a player would, with the game's own input (the companion's "press":
# forward, sprint, jump), and reports how it went: distance, speed, frame rate along the way, where it got stuck.
#   walk.ps1 -Route "x,z;x,z;..." [-Sprint] [-Start "x,y,z"] [-Label text] [-Capped]
# The player is set down at -Start (the only teleport: a teleported player is seated, so a jump stands them up),
# then everything is the game's input: the look stick turns them towards each point in turn (about 40 degrees a
# second at full tilt), forward walks them in 2 s stretches, and a jump stands them up whenever they are found
# seated. A stretch that covers under 0.6 m a second is "stuck": it jumps forward, then turns aside and tries round
# one side or the other. Uncapped frame rate unless -Capped.
# Appends a summary to logs\perf-measure.log.
param([Parameter(Mandatory)][string]$Route, [string]$Start = '', [switch]$Sprint, [switch]$Capped, [string]$Label = '', [int]$MaxSeconds = 600)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$inv = [Globalization.CultureInfo]::InvariantCulture
function C([string]$q, [int]$timeout = 60) { (Invoke-WebRequest -Uri ("http://127.0.0.1:17361/" + $q) -UseBasicParsing -TimeoutSec $timeout).Content }
function N([double]$v) { $v.ToString('0.##', $inv) }
function State {
    $s = C 'status'
    $o = [pscustomobject]@{ X = 0.0; Y = 0.0; Z = 0.0; Ms = 0.0; Worst = 0.0 }
    if ($s -match 'player: (-?[0-9.]+), (-?[0-9.]+), (-?[0-9.]+)') { $o.X = [double]::Parse($Matches[1], $inv); $o.Y = [double]::Parse($Matches[2], $inv); $o.Z = [double]::Parse($Matches[3], $inv) }
    if ($s -match 'frame: ([0-9.]+) ms average .* worst lately ([0-9.]+) ms') { $o.Ms = [double]::Parse($Matches[1], $inv); $o.Worst = [double]::Parse($Matches[2], $inv) }
    $o
}
function CamYaw { $s = C 'status'; if ($s -match 'facing (-?[0-9.]+), (-?[0-9.]+), (-?[0-9.]+)') { [math]::Atan2([double]::Parse($Matches[1], $inv), [double]::Parse($Matches[3], $inv)) * 180 / [math]::PI } else { 0.0 } }
function Seated { (C 'player') -match 'isSittingLocal=True' }
function Stand { if (Seated) { C 'press?action=jump&ms=200' | Out-Null; Start-Sleep -Milliseconds 700; $script:stands++ } }
# turn with the look stick until the camera faces 'yaw' (within 8 degrees)
function Face([double]$yaw) {
    for ($k = 0; $k -lt 4; $k++) {
        $d = (($yaw - (CamYaw)) % 360 + 540) % 360 - 180
        if ([math]::Abs($d) -lt 8) { return }
        $ms = [int][math]::Min(4500, [math]::Max(80, [math]::Abs($d) / 40 * 1000))
        C ("press?action=stickLookX&value=" + $(if ($d -gt 0) { '1' } else { '-1' }) + "&ms=$ms") | Out-Null
    }
}
# (in @( ): with one point the pipeline would hand back that point's two numbers as the list)
$points = @($Route -split ';' | ForEach-Object { $f = $_ -split ','; , @([double]::Parse($f[0].Trim(), $inv), [double]::Parse($f[1].Trim(), $inv)) })
C 'render?awake=1' | Out-Null
if (-not $Capped) { C 'set?path=UnityEngine.Application.targetFrameRate&value=-1' | Out-Null }
if ($Start) { $f = $Start -split ','; C ("teleport?x=" + $f[0].Trim() + "&y=" + $f[1].Trim() + "&z=" + $f[2].Trim()) | Out-Null; Start-Sleep 4 }
C 'time?hour=13' | Out-Null
$action = if ($Sprint) { 'moveY,sprint' } else { 'moveY' }
$stands = 0; $paces = @()
$began = Get-Date; $walked = 0.0; $stuck = 0; $stretches = 0; $frames = @(); $worst = 0.0; $stuckAt = @(); $reached = 0
try {
    foreach ($pt in $points) {
        $tries = 0
        while ($true) {
            if (((Get-Date) - $began).TotalSeconds -gt $MaxSeconds) { break }
            $a = State
            $dx = $pt[0] - $a.X; $dz = $pt[1] - $a.Z; $left = [math]::Sqrt($dx * $dx + $dz * $dz)
            if ($left -lt 4) { $reached++; Write-Output ("reached {0}, {1} (at {2}, {3}, {4})" -f (N $pt[0]), (N $pt[1]), (N $a.X), (N $a.Y), (N $a.Z)); break }
            $yaw = [math]::Atan2($dx, $dz) * 180 / [math]::PI
            Stand
            Face $yaw
            $ms = [int][math]::Min(2000, [math]::Max(500, $left / $(if ($Sprint) { 3.5 } else { 2.0 }) * 1000))
            C ("press?action=$action&ms=$ms") | Out-Null
            $b = State
            $step = [math]::Sqrt(($b.X - $a.X) * ($b.X - $a.X) + ($b.Z - $a.Z) * ($b.Z - $a.Z))
            $walked += $step; $stretches++; $paces += $step / ($ms / 1000.0)
            if ($b.Ms -gt 0) { $frames += $b.Ms; if ($b.Worst -gt $worst) { $worst = $b.Worst } }
            if ($step -lt 0.6 * $ms / 2000) {
                $stuck++; $tries++
                $stuckAt += ("{0}, {1}, {2}" -f (N $b.X), (N $b.Y), (N $b.Z))
                if ($tries -gt 8) { Write-Output ("gave up on {0}, {1}: stuck at {2}" -f (N $pt[0]), (N $pt[1]), $stuckAt[-1]); break }
                # a jump forward, then round it: to one side, then the other, wider each time
                C "press?action=jump,moveY&ms=600" | Out-Null
                $side = $(if ($tries % 2 -eq 1) { 1 } else { -1 }) * (50 + 15 * [math]::Floor($tries / 2))
                Face ($yaw + $side)
                C ("press?action=$action&ms=1800") | Out-Null
                $c2 = State
                $walked += [math]::Sqrt(($c2.X - $b.X) * ($c2.X - $b.X) + ($c2.Z - $b.Z) * ($c2.Z - $b.Z))
            } else { $tries = 0 }
        }
    }
} finally {
    if (-not $Capped) { try { C 'set?path=UnityEngine.Application.targetFrameRate&value=30' | Out-Null } catch { } }
}
$secs = ((Get-Date) - $began).TotalSeconds
$end = State
$p = Get-Process | Where-Object { $_.ProcessName -eq 'Big Walk' } | Select-Object -First 1
$meanMs = if ($frames.Count) { ($frames | Measure-Object -Average).Average } else { 0 }
$summary = @(
    ("walk {0}: reached {1} of {2} points, {3:0} m on foot in {4:0} s ({5:0.0} m/s with turning and getting round things), {6} stretches, stuck {7} times, stood up {8} times" -f $(if ($Sprint) { '(sprinting)' } else { '' }), $reached, $points.Count, $walked, $secs, ($walked / [math]::Max($secs, 1)), $stretches, $stuck, $stands),
    ("  frame time while walking: mean {0:0.0} ms ({1:0} fps), worst frame {2:0} ms; ended at {3}, {4}, {5}" -f $meanMs, $(if ($meanMs -gt 0) { 1000 / $meanMs } else { 0 }), $worst, (N $end.X), (N $end.Y), (N $end.Z))
)
if ($paces.Count) { $good = $paces | Where-Object { $_ -gt 0.6 }; if ($good) { $summary += ("  pace on stretches that were not stuck: median {0:0.0} m/s" -f (($good | Sort-Object)[[int]($good.Count / 2)])) } }
if ($p) { $summary += ("  memory at the end: {0:0.00} GB private, {1:0.00} GB in RAM" -f ($p.PrivateMemorySize64 / 1GB), ($p.WorkingSet64 / 1GB)) }
if ($stuckAt.Count) { $summary += ("  stuck at: " + (($stuckAt | Select-Object -First 12) -join ' | ')) }
$summary | ForEach-Object { Write-Output $_ }
$log = Join-Path $repo 'logs\perf-measure.log'
Add-Content -Path $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + " walk $Label") -Encoding utf8
$summary | ForEach-Object { Add-Content -Path $log -Value ("  " + $_) -Encoding utf8 }
