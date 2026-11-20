# Big Walk Mods command line.
#   tools\bwm.ps1 build <Puzzle> [-Resite] [-NoPreview] [-NoExport] [-AllowIssues]
#   tools\bwm.ps1 build-all | list | dump-world | setup
#   tools\bwm.ps1 launch        start the game with mods (a normal Steam launch stays vanilla)
#   tools\bwm.ps1 log           the mod loader's lines about puzzles from the last game run
# Unity commands run the editor headless and print only the framework's own "[BWM]" lines; the full Unity log
# is logs\unity-<command>.log. Exit code 0 means the command succeeded.
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('build', 'build-all', 'list', 'dump-world', 'part-metrics', 'survey', 'setup', 'launch', 'log', 'companion', 'game', 'play')]
    [string]$Command,
    [Parameter(Position = 1)][string]$Puzzle,
    [switch]$Resite,
    [switch]$NoPreview,
    [switch]$NoExport,
    [switch]$AllowIssues
)

$Repo = Split-Path $PSScriptRoot -Parent
$Unity = 'Z:\Unity\6000.3.17f1\Editor\Unity.exe'
$HubApp = 'shell:AppsFolder\UnityTechnologies.UnityHub_2vrhnee42bhxm!UnityHub'

function Invoke-BwmUnity([string]$Method, [string[]]$Extra) {
    if (-not (Test-Path $Unity)) { Write-Output "Unity editor not found at $Unity"; return 2 }
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
        if (Get-Process 'Big Walk' -ErrorAction SilentlyContinue) { Write-Output 'Big Walk is already running. In game: Ctrl+F2 reloads puzzles, Ctrl+F1 teleports to the dev puzzle.'; exit 0 }
        Start-Process -FilePath 'C:\Program Files (x86)\Steam\steam.exe' -ArgumentList '-applaunch', '1478500', '--doorstop-enabled', 'true'
        Write-Output 'Launched Big Walk with mods. Log: profile\BepInEx\LogOutput.log'
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
        $deadline = (Get-Date).AddSeconds(180); $state = ''
        while ((Get-Date) -lt $deadline) {
            try { $state = (Invoke-WebRequest -Uri 'http://127.0.0.1:17361/menu' -UseBasicParsing -TimeoutSec 5).Content; if ($state -match 'title|in a walk') { break } } catch { }
            Start-Sleep -Seconds 2
        }
        if ($state -notmatch 'title|in a walk') { Write-Output 'The game did not reach its menu (is Steam signed in? is the companion built?).'; exit 1 }
        (Invoke-WebRequest -Uri 'http://127.0.0.1:17361/host' -UseBasicParsing -TimeoutSec 240).Content
        exit 0
    }
    'game' {
        # Sends one command to the running game's companion, e.g.  bwm.ps1 game status   or   bwm.ps1 game 'teleport?puzzle=BwmTutorial'
        $cmd = if ($Puzzle) { $Puzzle } else { 'help' }
        try { (Invoke-WebRequest -Uri ("http://127.0.0.1:17361/" + $cmd) -UseBasicParsing -TimeoutSec 200).Content; exit 0 }
        catch { Write-Output ("The companion did not answer (" + $_.Exception.Message + "). Is the game running with mods?"); exit 1 }
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
