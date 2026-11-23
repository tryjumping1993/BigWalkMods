# Times a start of Big Walk from Steam: the launch, the game's process appearing, its menu (the game's own "Starting
# base manager initialization" in Player.log), and with -Walk (dev mode: the companion hosts a solo walk on the
# "BWM Test" save) on until every puzzle is built and the island is dressed. Every half second it samples the physical
# disks' reads (C:, Z:, G:), the game's own file reads, its CPU time and memory, and Windows Defender's CPU, so a slow
# start shows what it waited on: a disk (physical reads), the CPU (the game's CPU time) or neither.
#   startup.ps1 [-Mode vanilla|dev|install] [-Walk] [-Label text] [-Keep]
#     vanilla  Doorstop switched off on the command line: no mods at all
#     dev      the dev profile, as bwm.ps1 launch
#     install  a plain Steam start, which runs a players' install if one is in the game folder
# Quits the game afterwards (the companion in dev mode, else the window's close) unless -Keep. Writes every sample to
# logs\startup-<time>.csv and appends a summary to logs\perf-measure.log. Leave the PC otherwise quiet.
param([ValidateSet('vanilla', 'dev', 'install')][string]$Mode = 'dev', [switch]$Walk, [string]$Label = '', [switch]$Keep, [int]$TimeoutSec = 400)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$local = Join-Path $repo 'bwm.local.ps1'
if (Test-Path $local) { . $local }
$inv = [Globalization.CultureInfo]::InvariantCulture
function N([double]$v, [string]$f = '0.0') { $v.ToString($f, $inv) }
if ($Walk -and $Mode -ne 'dev') { throw '-Walk needs -Mode dev (the companion hosts the walk).' }
if (Get-Process 'Big Walk' -ErrorAction SilentlyContinue) { throw 'Big Walk is already running: quit it first (bwm.ps1 game quit).' }

$steam = 'C:\Program Files (x86)\Steam\steam.exe'
. (Join-Path $PSScriptRoot '..\install\Install-Common.ps1')      # Find-Game: Big Walk's folder, from Steam's library list
$gameDir = Find-Game
if (-not $gameDir) { throw 'Big Walk is not in any Steam library.' }
$steamArgs = switch ($Mode) {
    'vanilla' { @('-applaunch', '1478500', '--doorstop-enabled', 'false') }
    'install' { @('-applaunch', '1478500') }
    # (as bwm.ps1's $SteamGameArgs)
    'dev' {
        @('-applaunch', '1478500', '--doorstop-enabled', 'true',
            '--doorstop-target-assembly', (Join-Path $repo 'profile\BepInEx\core\BepInEx.Unity.IL2CPP.dll'),
            '--doorstop-clr-runtime-coreclr-path', (Join-Path $repo 'profile\dotnet\coreclr.dll'),
            '--doorstop-clr-corlib-dir', (Join-Path $repo 'profile\dotnet'))
    }
}
$localLow = Join-Path $env:USERPROFILE 'AppData\LocalLow\House House\Big Walk'
$playerLog = Join-Path $localLow 'Player.log'

# Cumulative counters (RawValue): bytes read from each physical disk, Defender's CPU time in 100 ns units
$disks = [ordered]@{}
foreach ($i in (New-Object Diagnostics.PerformanceCounterCategory 'PhysicalDisk').GetInstanceNames()) {
    foreach ($d in (@('C:', 'Z:', 'G:', (Split-Path -Qualifier $gameDir)) | Select-Object -Unique)) { if ($i -match ('(^|\s)' + $d)) { $disks[$d] = New-Object Diagnostics.PerformanceCounter 'PhysicalDisk', 'Disk Read Bytes/sec', $i, $true } }
}
$defender = $null
try { $defender = New-Object Diagnostics.PerformanceCounter 'Process', '% Processor Time', 'MsMpEng', $true; $null = $defender.RawValue } catch { $defender = $null }
function DiskBytes { $o = [ordered]@{}; foreach ($k in $disks.Keys) { $o[$k] = [double]$disks[$k].RawValue }; $o }

# Player.log is replaced at each start: read what was added since the last look
$logPos = 0L
function NewLogLines {
    if (-not (Test-Path $playerLog)) { return @() }
    $item = Get-Item $playerLog
    if ($item.LastWriteTime -lt $script:launch) { return @() }
    if ($item.Length -lt $script:logPos) { $script:logPos = 0L }
    $fs = [IO.File]::Open($playerLog, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $null = $fs.Seek($script:logPos, 'Begin')
        $text = (New-Object IO.StreamReader($fs)).ReadToEnd()
        $script:logPos = $fs.Length
    } finally { $fs.Dispose() }
    if ($text) { $text -split "`n" } else { @() }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force (Join-Path $repo 'logs') | Out-Null
$csv = Join-Path $repo "logs\startup-$stamp.csv"
$rows = New-Object Collections.Generic.List[string]
$rows.Add('t_s,phase,' + (($disks.Keys | ForEach-Object { $_.TrimEnd(':') + '_read_MB' }) -join ',') + ',game_read_MB,game_cpu_s,defender_cpu_s,working_set_GB,private_GB')

$launch = Get-Date
$disk0 = DiskBytes
$def0 = if ($defender) { [double]$defender.RawValue } else { 0 }
Start-Process -FilePath $steam -ArgumentList $steamArgs
$game = $null; $ioRead = $null; $phase = 'launch'; $marks = [ordered]@{}; $hostTask = $null; $http = $null
$peakWs = 0.0; $deadline = $launch.AddSeconds($TimeoutSec)
$modLog = if ($Mode -eq 'dev') { Join-Path $repo 'profile\BepInEx\LogOutput.log' } else { Join-Path $gameDir 'BepInEx\LogOutput.log' }
$modPos = 0L

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $now = Get-Date; $t = ($now - $launch).TotalSeconds
    if (-not $game) {
        $game = Get-Process 'Big Walk' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($game) {
            $null = $game.Handle
            $marks['process'] = ($game.StartTime - $launch).TotalSeconds
            if ($env:BWM_AFFINITY) { try { $game.ProcessorAffinity = [IntPtr][Convert]::ToInt64($env:BWM_AFFINITY, 16) } catch { } }
            $phase = 'loading'
        }
    }
    $gameRead = 0.0; $cpu = 0.0; $ws = 0.0; $priv = 0.0
    if ($game) {
        if ($game.HasExited) { $marks['exited'] = $t; break }
        $game.Refresh()
        $cpu = $game.TotalProcessorTime.TotalSeconds; $ws = $game.WorkingSet64 / 1GB; $priv = $game.PrivateMemorySize64 / 1GB
        if ($ws -gt $peakWs) { $peakWs = $ws }
        if (-not $ioRead) { try { $ioRead = New-Object Diagnostics.PerformanceCounter 'Process', 'IO Read Bytes/sec', 'Big Walk', $true; $null = $ioRead.RawValue } catch { $ioRead = $null } }
        if ($ioRead) { try { $gameRead = $ioRead.RawValue / 1MB } catch { } }
        foreach ($line in (NewLogLines)) {
            if (-not $marks.Contains('menu') -and $line -match 'Starting base manager initialization') { $marks['menu'] = $t; $phase = 'menu' }
        }
    }
    $d = DiskBytes
    $def = if ($defender) { ([double]$defender.RawValue - $def0) / 1e7 } else { 0 }
    $rows.Add((N $t '0.0') + ',' + $phase + ',' + (($disks.Keys | ForEach-Object { N (($d[$_] - $disk0[$_]) / 1MB) '0' }) -join ',') + ',' + (N $gameRead '0') + ',' + (N $cpu '0.0') + ',' + (N $def '0.0') + ',' + (N $ws '0.00') + ',' + (N $priv '0.00'))

    if ($marks.Contains('menu') -and -not $Walk) {
        if (-not $marks.Contains('settled')) { $marks['settled'] = $t + 5 }
        elseif ($t -ge $marks['settled']) { break }        # five seconds of the menu, then stop
    }
    if ($Walk -and $marks.Contains('menu')) {
        if (-not $hostTask) {
            # the companion answers once the menu takes input; ask it to host (it returns when the walk has loaded)
            try { $state = (Invoke-WebRequest -Uri 'http://127.0.0.1:17361/menu' -UseBasicParsing -TimeoutSec 2).Content } catch { $state = '' }
            if ($state -match 'title') {
                Add-Type -AssemblyName System.Net.Http
                $http = New-Object Net.Http.HttpClient; $http.Timeout = [TimeSpan]::FromSeconds(300)
                $hostTask = $http.GetStringAsync('http://127.0.0.1:17361/host'); $marks['host'] = $t; $phase = 'joining'
            }
        }
        if (Test-Path $modLog) {
            $fs = [IO.File]::Open($modLog, 'Open', 'Read', 'ReadWrite, Delete')
            try { if ($fs.Length -lt $modPos) { $modPos = 0L }; $null = $fs.Seek($modPos, 'Begin'); $text = (New-Object IO.StreamReader($fs)).ReadToEnd(); $modPos = $fs.Length } finally { $fs.Dispose() }
            if ($marks.Contains('host')) {
                if (-not $marks.Contains('puzzles') -and $text -match 'puzzle\(s\) loaded') { $marks['puzzles'] = $t; $phase = 'building' }
                if (-not $marks.Contains('dressed') -and $text -match 'dressed the island') { $marks['dressed'] = $t; $phase = 'walk'; break }
            }
        }
    }
}
$rows | Set-Content -Encoding utf8 $csv

# The summary: when each mark came, and what the disks, the game and Defender did up to it
function At([double]$t) { foreach ($r in $rows | Select-Object -Skip 1) { $f = $r -split ','; if ([double]::Parse($f[0], $inv) -ge $t) { return $f } }; ($rows[$rows.Count - 1] -split ',') }
$names = @($disks.Keys)
$parts = @()
foreach ($m in 'process', 'menu', 'host', 'puzzles', 'dressed', 'exited') { if ($marks.Contains($m)) { $parts += ($m + ' ' + (N $marks[$m]) + ' s') } }
$summary = "startup $stamp mode=$Mode" + $(if ($Label) { " ($Label)" } else { '' }) + ', game in ' + $gameDir + ': ' + ($parts -join ', ')
foreach ($m in 'menu', 'dressed') {
    if (-not $marks.Contains($m)) { continue }
    $f = At $marks[$m]
    $reads = for ($k = 0; $k -lt $names.Count; $k++) { $names[$k] + ' ' + $f[2 + $k] + ' MB' }
    $o = 2 + $names.Count
    $summary += "`n  up to $m" + ': disks read ' + ($reads -join ', ') + '; the game read ' + $f[$o] + ' MB, used ' + $f[$o + 1] + ' s of CPU; Defender ' + $f[$o + 2] + ' s of CPU; working set ' + $f[$o + 3] + ' GB'
}
$summary += "`n  peak working set " + (N $peakWs '0.00') + " GB; samples in $csv"
if (-not $marks.Contains('menu')) { $summary += "`n  the menu never came within $TimeoutSec s" }
Add-Content -Path (Join-Path $repo 'logs\perf-measure.log') -Value $summary
$summary

if ($http) { $http.Dispose() }
if (-not $Keep -and $game -and -not $game.HasExited) {
    if ($Mode -eq 'dev') { try { Invoke-WebRequest -Uri 'http://127.0.0.1:17361/quit' -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { } }
    else { $null = $game.CloseMainWindow() }
    if (-not $game.WaitForExit(40000)) { Stop-Process -Id $game.Id -Force; 'the game did not close within 40 s: stopped' }
}
