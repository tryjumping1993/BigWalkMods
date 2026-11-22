# Captures every island puzzle from the running game for review (bwm.ps1 play first):
#   puzzle-capture.ps1 [-Names A,B,...] [-Out shots\puzzles]
# Per puzzle: an aerial view and four eye-level views from outside its footprint (N, E, S, W, looking at its middle),
# then it is started (a view while running, and the companion's status), stopped, and won; the game's log lines
# from that time go to <Out>\<Name>.log. Sites and footprints come from each puzzle's site.json and
# tools\sites\footprints.json. Pauses between teleports on purpose (CLAUDE.md: this PC's CPU).
param([string[]]$Names = @(), [string]$Out = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Out) { $Out = Join-Path $repo 'shots\puzzles' }
New-Item -ItemType Directory -Force $Out | Out-Null
$inv = [Globalization.CultureInfo]::InvariantCulture
function C([string]$q, [int]$t = 60) { (Invoke-WebRequest -Uri ("http://127.0.0.1:17361/" + $q) -UseBasicParsing -TimeoutSec $t).Content }
function N([double]$v) { $v.ToString('0.##', $inv) }
$foot = Get-Content (Join-Path $repo 'tools\sites\footprints.json') -Raw | ConvertFrom-Json
$status = C 'status'
$roots = @{}
foreach ($m in [regex]::Matches($status, 'puzzle: (\S+) at (-?[0-9.]+), (-?[0-9.]+), (-?[0-9.]+)')) { $roots[$m.Groups[1].Value] = @([double]::Parse($m.Groups[2].Value, $inv), [double]::Parse($m.Groups[3].Value, $inv), [double]::Parse($m.Groups[4].Value, $inv)) }
$Names = @($Names | ForEach-Object { $_ -split ',' } | Where-Object { $_ })      # (-File passes "A,B" as one string)
$skip = @('WinterIsle', 'TheTree', 'CastIronPizza', 'Dinosaurs', 'IslandGear', 'SummitDiscGolf')
if (-not $Names -or $Names.Count -eq 0) { $Names = $roots.Keys | Where-Object { $skip -notcontains $_ } | Sort-Object }
$log = Get-ChildItem "$env:USERPROFILE\AppData\LocalLow\House House\Big Walk\WinterWalk-logs\session-*.log" | Sort-Object Name | Select-Object -Last 1
C 'render?awake=1' | Out-Null
foreach ($name in $Names) {
    if (-not $roots.ContainsKey($name)) { Write-Output "${name}: not loaded"; continue }
    $r = $roots[$name]; $x = $r[0]; $y = $r[1]; $z = $r[2]
    $f = $foot.$name; $fx = 30.0; $fz = 30.0
    if ($f -and $f.footprint) { $fx = [double]$f.footprint[0]; $fz = [double]$f.footprint[1] }
    $R = [math]::Max($fx, $fz) / 2
    $lines0 = (Get-Content $log.FullName).Count
    C "teleport?puzzle=$name" | Out-Null; Start-Sleep 4
    C 'time?hour=13' | Out-Null; Start-Sleep 1
    $g = C ("ground?x=" + (N $x) + "&z=" + (N $z) + "&all=1"); $gy = $y; if ($g -match 'ground: (-?[0-9.]+)') { $gy = [double]::Parse($Matches[1], $inv) }
    $ty = $gy + 1.5
    C ("shot?file=$name-aerial&cx=" + (N ($x + $R * 0.9)) + "&cy=" + (N ($gy + $R * 1.6 + 12)) + "&cz=" + (N ($z - $R * 1.4)) + "&tx=" + (N $x) + "&ty=" + (N $gy) + "&tz=" + (N $z) + "&fov=60") | Out-Null; Start-Sleep 2
    foreach ($d in @(@('n', 0, 1), @('e', 1, 0), @('s', 0, -1), @('w', -1, 0))) {
        $cx = $x + $d[1] * ($R + 5); $cz = $z + $d[2] * ($R + 5)
        $cg = C ("ground?x=" + (N $cx) + "&z=" + (N $cz) + "&all=1"); $cy = $gy + 1.7; if ($cg -match 'ground: (-?[0-9.]+)') { $cy = [double]::Parse($Matches[1], $inv) + 1.7 }
        C ("shot?file=$name-$($d[0])&cx=" + (N $cx) + "&cy=" + (N $cy) + "&cz=" + (N $cz) + "&tx=" + (N $x) + "&ty=" + (N $ty) + "&tz=" + (N $z) + "&fov=70") | Out-Null; Start-Sleep 2
    }
    $start = C "puzzle?name=$name&do=start"; Start-Sleep 4
    C ("shot?file=$name-running&cx=" + (N ($x - $R * 0.8)) + "&cy=" + (N ($gy + $R * 1.2 + 8)) + "&cz=" + (N ($z - $R * 1.2)) + "&tx=" + (N $x) + "&ty=" + (N $gy) + "&tz=" + (N $z) + "&fov=60") | Out-Null; Start-Sleep 2
    $run = ((C 'status') -split "`n" | Where-Object { $_ -match "puzzle: $name " }) -join ' '
    $stop = C "puzzle?name=$name&do=stop"; Start-Sleep 3
    $after = ((C 'status') -split "`n" | Where-Object { $_ -match "puzzle: $name " }) -join ' '
    $win = C "puzzle?name=$name&do=win"; Start-Sleep 3
    $won = ((C 'status') -split "`n" | Where-Object { $_ -match "puzzle: $name " }) -join ' '
    C "puzzle?name=$name&do=stop" | Out-Null
    # (Calm Air boxes are only looked for while a jetpack is worn)
    C 'set?path=WinterWalk.JetpackDriver.Worn&value=true' | Out-Null; C 'set?path=WinterWalk.JetpackDriver.RescanCalm&value=true' | Out-Null; Start-Sleep 1
    $calm = (C 'get?path=WinterWalk.JetpackDriver.CalmBoxes') + ', inside one here: ' + (C 'get?path=WinterWalk.JetpackDriver.InCalmAir')
    C 'set?path=WinterWalk.JetpackDriver.Worn&value=false' | Out-Null
    $lines = Get-Content $log.FullName | Select-Object -Skip $lines0
    $report = @("$name at ($(N $x), $(N $y), $(N $z)), footprint $fx x $fz, ground $gy",
        "start: $start | running: $run", "stop: $stop | after: $after", "win: $win | after: $won", "jetpack Calm Air boxes known: $calm",
        "--- the game's log in this time (warnings, errors, the SDK and our scripts):") +
        ($lines | Where-Object { $_ -match 'Warning|Error|Exception|Puzzle|BlindWatch|Shuffle|Warden|Deal|Range|LobHouse|TileReset|Calm' -and $_ -notmatch '\[hb\]|\[states\]|enviro server time|LogEOS' })
    $report | Set-Content -Path (Join-Path $Out "$name.log") -Encoding utf8
    # (the companion writes shots to shots\; move this puzzle's into the review folder)
    Get-ChildItem (Join-Path $repo 'shots') -Filter "$name-*.png" -File | Move-Item -Destination $Out -Force
    Write-Output ("{0,-18} start/stop/win done; {1} log lines" -f $name, ($report.Count - 6))
    Start-Sleep 2
}
