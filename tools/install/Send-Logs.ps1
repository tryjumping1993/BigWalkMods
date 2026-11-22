# Big Christmas Walk: gathers what is needed to look into a problem (crash, stuck player, slowness...) into one
# zip on the Desktop, to send to whoever looks after the mod. Logs only: no saves, no game files.
#   - Winter Walk's debug logs (the last five sessions) and the screenshots taken with F8
#   - the game's own logs (Player.log, bigwalk-1..5.log) and the mod loader's (BepInEx\LogOutput.log)
#   - which version is installed, and Winter Walk's settings
#   - what Windows recorded about Big Walk in the last two weeks (crashes, hangs) and this PC's CPU, GPU and memory
param([switch]$Yes)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Common.ps1')

Say 'Big Christmas Walk: collecting logs' Cyan
$local = Join-Path $env:USERPROFILE 'AppData\LocalLow\House House\Big Walk'
$stage = Join-Path $env:TEMP ('BigChristmasWalk-logs-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
function Take($from, $to) {
    # (read alongside a running game, which keeps its logs open for writing)
    try {
        if (-not (Test-Path -LiteralPath $from)) { return }
        $in = New-Object IO.FileStream($from, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
        try { $out = [IO.File]::Create((Join-Path $stage $to)); try { $in.CopyTo($out) } finally { $out.Dispose() } } finally { $in.Dispose() }
    } catch { Say ("  (could not copy " + $from + ": " + $_.Exception.Message + ")") }
}

# the debug logs and marks (the newest first)
$wl = Join-Path $local 'WinterWalk-logs'
$sessions = @(Get-ChildItem -Path $wl -Filter 'session-*.log' -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 5)
foreach ($f in $sessions) { Take $f.FullName $f.Name }
foreach ($f in @(Get-ChildItem -Path $wl -Filter '*-mark*.png' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 10)) { Take $f.FullName $f.Name }
Say ("  " + $sessions.Count + " debug log(s)")
# the game's own logs
foreach ($n in 'Player.log', 'Player-prev.log', 'bigwalk-1.log', 'bigwalk-2.log', 'bigwalk-3.log', 'bigwalk-4.log', 'bigwalk-5.log') { Take (Join-Path $local $n) $n }
# the mod loader's log, the installed version and Winter Walk's settings
$game = Find-Game
if ($game) {
    Take (Join-Path $game 'BepInEx\LogOutput.log') 'LogOutput.log'
    Take (Join-Path $game 'BepInEx\BigChristmasWalk.installed.txt') 'installed.txt'
    Take (Join-Path $game 'BepInEx\config\bwm.winterwalk.cfg') 'bwm.winterwalk.cfg'
    Say "  the game is at $game"
} else { Say '  (Big Walk was not found through Steam: the mod loader''s log is not included)' }

# what Windows recorded, and this PC
$info = New-Object System.Text.StringBuilder
[void]$info.AppendLine('Collected ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'))
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    [void]$info.AppendLine('Windows ' + $os.Version + ' (' + $os.Caption + '), RAM ' + [math]::Round($os.TotalVisibleMemorySize / 1MB, 1) + ' GB')
    [void]$info.AppendLine('CPU ' + $cpu.Name)
    foreach ($g in Get-CimInstance Win32_VideoController) { [void]$info.AppendLine('GPU ' + $g.Name + ', driver ' + $g.DriverVersion + ' (' + $g.DriverDate + ')') }
} catch { [void]$info.AppendLine('(no system details: ' + $_.Exception.Message + ')') }
$since = (Get-Date).AddDays(-14)
try {
    $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error', 'Application Hang', '.NET Runtime', 'Windows Error Reporting'; StartTime = $since } -ErrorAction SilentlyContinue | Where-Object { $_.Message -match 'Big Walk' })
    [void]$info.AppendLine('')
    [void]$info.AppendLine('Windows records about Big Walk since ' + $since.ToString('yyyy-MM-dd') + ': ' + $events.Count)
    foreach ($e in $events) { [void]$info.AppendLine('---- ' + $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + ' ' + $e.ProviderName + ' ' + $e.Id); [void]$info.AppendLine($e.Message) }
} catch { [void]$info.AppendLine('(no Windows records: ' + $_.Exception.Message + ')') }
try {
    $whea = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-WHEA-Logger'; StartTime = $since } -ErrorAction SilentlyContinue)
    [void]$info.AppendLine('')
    [void]$info.AppendLine('CPU and memory hardware errors (WHEA) since then: ' + $whea.Count)
    foreach ($e in ($whea | Select-Object -First 20)) { [void]$info.AppendLine('  ' + $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + ' ' + (($e.Message -split "`n" | Where-Object { $_ -match 'Error Type|APIC|component' }) -join ' ').Trim()) }
} catch { }
Set-Content -LiteralPath (Join-Path $stage 'windows-and-pc.txt') -Value $info.ToString() -Encoding UTF8

# one zip on the Desktop
$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop -or -not (Test-Path $desktop)) { $desktop = $env:USERPROFILE }
$zip = Join-Path $desktop ('BigChristmasWalk-logs-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.zip')
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -LiteralPath $stage -Recurse -Force
try { Start-Process explorer.exe -ArgumentList ('/select,"' + $zip + '"') } catch { }
Quit 0 ("Done. Send this file: " + $zip + "`n(It holds logs only, no saves. They include your Steam name and PC's name.)")
