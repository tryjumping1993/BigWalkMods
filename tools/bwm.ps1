# Big Walk Mods command line.
#   tools\bwm.ps1 build <Puzzle> [-Resite] [-NoPreview] [-NoExport] [-AllowIssues]
#   tools\bwm.ps1 build-all | list | dump-world | setup
#   tools\bwm.ps1 launch        start the game with mods (a normal Steam launch stays vanilla)
#   tools\bwm.ps1 package [<version>]   the players' download and installer (tools\install\Package.ps1)
#   tools\bwm.ps1 log           the mod loader's lines about puzzles from the last game run
#   tools\bwm.ps1 logs [n] [-Full]   Winter Walk's debug log of the latest session (n = 2 the one before...)
# Unity commands run the editor headless and print only the framework's own "[BWM]" lines; the full Unity log
# is logs\unity-<command>.log. Exit code 0 means the command succeeded.
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('build', 'build-all', 'list', 'dump-world', 'part-metrics', 'survey', 'setup', 'launch', 'log', 'logs', 'companion', 'game', 'play', 'package')]
    [string]$Command,
    [Parameter(Position = 1)][string]$Puzzle,
    [switch]$Full,
    [switch]$Resite,
    [switch]$NoPreview,
    [switch]$NoExport,
    [switch]$AllowIssues
)

$Repo = Split-Path $PSScriptRoot -Parent
$Unity = 'Z:\Unity\6000.3.17f1\Editor\Unity.exe'
$HubApp = 'shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub'

# Settings for this machine only (not committed), e.g.  $env:BWM_AFFINITY = 'FFFFFFCF'
$local = Join-Path $Repo 'bwm.local.ps1'
if (Test-Path $local) { . $local }

# Optional, for a machine with a faulty CPU core: BWM_AFFINITY is a hex mask of the logical CPUs Unity and the game
# may use (FFFFFFCF leaves out CPUs 4 and 5); processes they start inherit it. Unset, nothing changes.
function Set-BwmAffinity($process) {
    if (-not $env:BWM_AFFINITY -or -not $process) { return }
    try { $process.ProcessorAffinity = [IntPtr][Convert]::ToInt64($env:BWM_AFFINITY, 16) }
    catch { Write-Warning ("Could not set the CPU affinity: " + $_.Exception.Message) }
}

# The snow surface maps are generated, not committed: make them when they are missing or older than their generator
# (needs Python with numpy, scipy and Pillow:  pip install --user numpy scipy pillow)
function Update-BwmSnowMaps {
    $script = Join-Path $Repo 'tools\snowtex.py'
    $dir = Join-Path $Repo 'unity\Assets\Textures\Snow'
    $stale = $false
    foreach ($f in 'SnowWind_N.png', 'SnowWind_H.png', 'SnowGrain_N.png', 'SnowGrain_H.png', 'SnowTrack_N.png', 'SnowTrack_H.png') {
        $file = Join-Path $dir $f
        if (-not (Test-Path $file) -or (Get-Item $file).LastWriteTimeUtc -lt (Get-Item $script).LastWriteTimeUtc) { $stale = $true }
    }
    if (-not $stale) { return }
    $p = Start-Process -FilePath python -ArgumentList ('"' + $script + '"') -PassThru -NoNewWindow
    $null = $p.Handle      # (without the handle held, ExitCode reads empty and a good run is reported as failed)
    Set-BwmAffinity $p
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { Write-Warning 'tools\snowtex.py failed; the snow will be drawn without its surface maps.' }
}

function Update-BwmTreeMaps {
    $script = Join-Path $Repo 'tools\treetex.py'
    $dir = Join-Path $Repo 'unity\Assets\Textures\Trees'
    $stale = $false
    foreach ($f in 'FirFronds_C.png', 'FirFronds_N.png', 'FirFronds_M.png', 'FirBark_C.png', 'FirBark_N.png', 'BirchBark_C.png', 'BirchBark_N.png', 'BirchTwigs_C.png', 'BirchTwigs_N.png', 'BirchTwigs_M.png') {
        $file = Join-Path $dir $f
        if (-not (Test-Path $file) -or (Get-Item $file).LastWriteTimeUtc -lt (Get-Item $script).LastWriteTimeUtc) { $stale = $true }
    }
    if (-not $stale) { return }
    $p = Start-Process -FilePath python -ArgumentList ('"' + $script + '"') -PassThru -NoNewWindow
    $null = $p.Handle      # (without the handle held, ExitCode reads empty and a good run is reported as failed)
    Set-BwmAffinity $p
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { Write-Warning 'tools\treetex.py failed; the trees will be drawn without their textures.' }
}

# The pizzeria's atlas, normal map and logo decal (tools/pizzeria_art.py, tools/pizzeria_logo.py) are generated too:
# about 30 MB of PNG for each regeneration, so they are not committed
function Update-BwmPizzeriaArt {
    $dir = Join-Path $Repo 'unity\Assets\Textures\Pizzeria'
    foreach ($job in @(@('pizzeria_art.py', @('CastIronAtlas.png', 'CastIronNormals.png', 'CastIronAtlas.json')), @('pizzeria_logo.py', @('CastIronLogoInk.png')))) {
        $script = Join-Path (Join-Path $Repo 'tools') $job[0]
        $stale = $false
        foreach ($f in $job[1]) {
            $file = Join-Path $dir $f
            if (-not (Test-Path $file) -or (Get-Item $file).LastWriteTimeUtc -lt (Get-Item $script).LastWriteTimeUtc) { $stale = $true }
        }
        if (-not $stale) { continue }
        $p = Start-Process -FilePath python -ArgumentList ('"' + $script + '"') -PassThru -NoNewWindow
        $null = $p.Handle
        Set-BwmAffinity $p
        $p.WaitForExit()
        if ($p.ExitCode -ne 0) { Write-Warning ($job[0] + ' failed; the pizzeria cannot be built without it.') }
    }
}

function Invoke-BwmUnity([string]$Method, [string[]]$Extra) {
    if (-not (Test-Path $Unity)) { Write-Output "Unity editor not found at $Unity"; return 2 }
    if ($Method -like 'Build*') { Update-BwmSnowMaps; Update-BwmTreeMaps; Update-BwmPizzeriaArt }
    if (Get-Process Unity -ErrorAction SilentlyContinue) { Write-Output 'A Unity editor is already running; close it first (two editors cannot open one project).'; return 2 }
    # The editor only finds its licence through Unity Hub's licensing service, so the Hub must be running and signed in.
    if (-not (Get-Process 'Unity.Licensing.Client' -ErrorAction SilentlyContinue)) {
        Start-Process $HubApp
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline -and -not (Get-Process 'Unity.Licensing.Client' -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
        Start-Sleep -Seconds 3
    }
    $env:UPM_CACHE_ROOT = 'Z:\Unity\cache\upm'      # keep Unity's package cache off C:
    New-Item -ItemType Directory -Force (Join-Path $Repo 'logs') | Out-Null
    $log = Join-Path $Repo "logs\unity-$Command.log"
    $unityArgs = @('-batchmode', '-projectPath', (Join-Path $Repo 'unity'), '-executeMethod', "Bwm.Cli.$Method", '-logFile', $log) + $Extra
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # Not -Wait: that also waits for Unity's compiler server (VBCSCompiler), which lingers for minutes after the editor exits
    $p = Start-Process -FilePath $Unity -ArgumentList $unityArgs -PassThru
    Set-BwmAffinity $p
    $p.WaitForExit()
    $seen = @{}
    Select-String -Path $log -Pattern '\[BWM\]|error CS\d+|\[PuzzleExporter\]|No valid Unity Editor license|Scripts have compiler errors' |
        ForEach-Object { $_.Line.Trim() } | Where-Object { -not $seen.ContainsKey($_) } | ForEach-Object { $seen[$_] = 1; $_ }
    Write-Output ("unity exit code {0} after {1}s (log: {2})" -f $p.ExitCode, [int]$sw.Elapsed.TotalSeconds, $log)
    return $p.ExitCode
}

$code = 0
switch ($Command) {
    'build' {
        if (-not $Puzzle) { Write-Output 'Usage: bwm.ps1 build <Puzzle>'; exit 2 }
        $extra = @('-bwmPuzzle', $Puzzle)
        if ($Resite) { $extra += '-bwmResite' }
        if ($NoPreview) { $extra += '-bwmNoPreview' }
        if ($NoExport) { $extra += '-bwmNoExport' }
        if ($AllowIssues) { $extra += '-bwmAllowIssues' }
        $out = Invoke-BwmUnity 'Build' $extra
    }
    'build-all' { $out = Invoke-BwmUnity 'BuildAll' @() }
    'list' { $out = Invoke-BwmUnity 'List' @() }
    'dump-world' { $out = Invoke-BwmUnity 'DumpWorld' @() }
    'part-metrics' { $out = Invoke-BwmUnity 'PartMetrics' @() }
    'survey' { $out = Invoke-BwmUnity 'Survey' @() }
    'setup' { $out = Invoke-BwmUnity 'SetupProject' @() }
    'launch' {
        if (Get-Process 'Big Walk' -ErrorAction SilentlyContinue) { Set-BwmAffinity (Get-Process 'Big Walk' | Select-Object -First 1); Write-Output 'Big Walk is already running. In game: Ctrl+F2 reloads puzzles, Ctrl+F1 teleports to the dev puzzle.'; exit 0 }
        Start-Process -FilePath 'C:\Program Files (x86)\Steam\steam.exe' -ArgumentList '-applaunch', '1478500', '--doorstop-enabled', 'true'
        # keep the game off a faulty core (BWM_AFFINITY), as play does: Steam starts it, so wait for its process.
        # (2026-11-21: a launch without this crashed with a one-bit-wrong code address after a hardware error on CPU 4)
        if ($env:BWM_AFFINITY) {
            $deadline = (Get-Date).AddSeconds(120); $game = $null
            while ((Get-Date) -lt $deadline -and -not $game) { $game = Get-Process 'Big Walk' -ErrorAction SilentlyContinue | Select-Object -First 1; if (-not $game) { Start-Sleep -Seconds 1 } }
            if ($game) { Set-BwmAffinity $game; Write-Output ('Kept Big Walk to the CPUs in mask ' + $env:BWM_AFFINITY + '.') } else { Write-Output 'Big Walk did not start within 2 minutes (is Steam signed in?); its CPU affinity is not set.' }
        }
        Write-Output 'Launched Big Walk with mods. Logs: bwm.ps1 logs (the debug log), profile\BepInEx\LogOutput.log'
        exit 0
    }
    'companion' {
        # Builds the test companion plugin into the profile. The game must be closed (Windows locks a loaded DLL).
        if (Get-Process 'Big Walk' -ErrorAction SilentlyContinue) { Write-Output 'Close Big Walk first (bwm.ps1 game quit).'; exit 2 }
        & 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $Repo 'companion') -c Release -nologo -v q
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        # ...and Winter Walk, the edition's own plugin (sky and light), which every player needs
        & 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $Repo 'winterwalk') -c Release -nologo -v q
        exit $LASTEXITCODE
    }
    'play' {
        # Starts the game with mods (if it is not running) and hosts a walk on the dedicated 'BWM Test' save
        if (-not (Get-Process 'Big Walk' -ErrorAction SilentlyContinue)) {
            Start-Process -FilePath 'C:\Program Files (x86)\Steam\steam.exe' -ArgumentList '-applaunch', '1478500', '--doorstop-enabled', 'true'
        }
        $deadline = (Get-Date).AddSeconds(180); $state = ''; $pinned = $false
        while ((Get-Date) -lt $deadline) {
            if (-not $pinned) { $game = Get-Process 'Big Walk' -ErrorAction SilentlyContinue | Select-Object -First 1; if ($game) { Set-BwmAffinity $game; $pinned = $true } }
            try { $state = (Invoke-WebRequest -Uri 'http://127.0.0.1:17361/menu' -UseBasicParsing -TimeoutSec 5).Content; if ($state -match 'title|in a walk') { break } } catch { }
            Start-Sleep -Seconds 2
        }
        if ($state -notmatch 'title|in a walk') { Write-Output 'The game did not reach its menu (is Steam signed in? is the companion built?).'; exit 1 }
        (Invoke-WebRequest -Uri 'http://127.0.0.1:17361/host' -UseBasicParsing -TimeoutSec 240).Content
        # Test runs are capped at 30 fps (BWM_FPS overrides; 0 leaves the game's own limit): less heat and power
        # for screenshots that do not need more
        $fps = if ($env:BWM_FPS) { [int]$env:BWM_FPS } else { 30 }
        if ($fps -gt 0) {
            foreach ($q in @('set?path=UnityEngine.QualitySettings.vSyncCount&value=0', "set?path=UnityEngine.Application.targetFrameRate&value=$fps")) {
                try { (Invoke-WebRequest -Uri ("http://127.0.0.1:17361/" + $q) -UseBasicParsing -TimeoutSec 30).Content } catch { Write-Output ("frame cap not set: " + $_.Exception.Message) }
            }
        }
        exit 0
    }
    'game' {
        # Sends one command to the running game's companion, e.g.  bwm.ps1 game status   or   bwm.ps1 game 'teleport?puzzle=BwmTutorial'
        $cmd = if ($Puzzle) { $Puzzle } else { 'help' }
        try { (Invoke-WebRequest -Uri ("http://127.0.0.1:17361/" + $cmd) -UseBasicParsing -TimeoutSec 200).Content; exit 0 }
        catch { Write-Output ("The companion did not answer (" + $_.Exception.Message + "). Is the game running with mods?"); exit 1 }
    }
    'package' {
        # The players' zip with its installer (tools\install\README.txt is what they read)
        & (Join-Path $PSScriptRoot 'install\Package.ps1') -Version $Puzzle
        exit $LASTEXITCODE
    }
    'logs' {
        # Winter Walk's debug log (winterwalk\Diag.cs): the header, then what matters from a session: what became of
        # the one before, warnings and errors, snapshots, marks, slow frames, walk events, the last heartbeat.
        # bwm.ps1 logs [n]: n = 1 the latest session (default), 2 the one before...  Add -Full for every line.
        $dir = Join-Path $env:USERPROFILE 'AppData\LocalLow\House House\Big Walk\WinterWalk-logs'
        $files = @(Get-ChildItem -Path $dir -Filter 'session-*.log' -ErrorAction SilentlyContinue | Sort-Object Name -Descending)
        if ($files.Count -eq 0) { Write-Output "No debug logs in $dir yet."; exit 1 }
        $n = if ($Puzzle -match '^\d+$') { [int]$Puzzle } else { 1 }
        if ($n -lt 1 -or $n -gt $files.Count) { Write-Output ("There are " + $files.Count + " session logs."); exit 1 }
        $file = $files[$n - 1].FullName
        Write-Output "== $file"
        # (the game keeps the latest open for writing: read it alongside)
        $fsr = New-Object IO.FileStream($file, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
        $reader = New-Object IO.StreamReader($fsr)
        $lines = $reader.ReadToEnd() -split "`r?`n"
        $reader.Dispose()
        if ($Full) { $lines; exit 0 }
        $pattern = '\[(Warning|Error  |Fatal  ):|\[slow\]|\[perf\]|\[mark|\[states\]|SNAPSHOT|previous session|session ended|Winter Walk: (in a walk|left the walk|players in|came onto|left the Winter|dressing check|how this PC|debug log|dressed the island|.* moving the player|CPU affinity|back on the ground)'
        $part = 'header'; $show = $false; $lastBeat = $null
        foreach ($line in $lines) {
            if ($line.StartsWith('---- BepInEx')) { $part = 'startup'; continue }
            if ($line.StartsWith('---- live')) { $part = 'live'; continue }
            if ($part -eq 'header') { $line; continue }
            if ($part -eq 'startup') { continue }
            if ($line -match '^\d\d:\d\d:\d\d\.\d{3} ') {
                $show = $line -match $pattern
                if ($line.Contains('[hb]')) { $lastBeat = $line }
                if ($show -and $line.Contains('[Warning:') -and $line -notmatch 'Winter Walk|Big Puzzle SDK') { $show = $line -notmatch 'LogEOS|Dissonance|TickTracker|already has echo|SetGameName|BWM Test' }
            }
            if ($show) { $line }
        }
        if ($lastBeat) { Write-Output '-- last heartbeat:'; $lastBeat }
        exit 0
    }
    'log' {
        $log = Join-Path $Repo 'profile\BepInEx\LogOutput.log'
        if (-not (Test-Path $log)) { Write-Output 'No game log yet.'; exit 1 }
        Select-String -Path $log -Pattern 'Big Puzzle SDK\]|\[Creator\]|\[Error|\[Fatal' | ForEach-Object { $_.Line }
        exit 0
    }
}
# Invoke-BwmUnity writes its lines and then its exit code; the code is the last item
if ($out -is [array]) { $code = $out[-1]; $out[0..($out.Count - 2)] } else { $code = $out }
exit $code
