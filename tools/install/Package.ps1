# Makes the players' download: Z:\Temp\claude\BigWalkMods\dist\BigChristmasWalk-<version>.zip (BWM_DIST overrides the
# folder). Run through  tools\bwm.ps1 package [-Puzzle <version>]  after building the packs and the plugins.
#
# The zip holds Install.cmd / Uninstall.cmd (and their scripts), README.txt, version.txt and payload\, which mirrors the
# game folder: the Doorstop loader (winhttp.dll, enabled, paths relative to the game folder), BepInEx's core and .NET
# runtime from the dev profile, BepInEx.cfg (console off), the Big Puzzle SDK mod, every island pack and
# WinterWalkScripts.dll (in plugins\BigChristmasWalk), and the Winter Walk plugin with its data files. Left out: the companion and the other dev
# helpers, the tutorial pack, BepInEx's generated folders (interop, cache, unity-libs: each player's first start makes
# them) and our own plugin configs (the plugins write their defaults).
param([string]$Version = '')

$ErrorActionPreference = 'Stop'
$Repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Profile = Join-Path $Repo 'profile'
$Game = 'G:\SteamLibrary\steamapps\common\Big Walk'
$Dist = if ($env:BWM_DIST) { $env:BWM_DIST } else { 'Z:\Temp\claude\BigWalkMods\dist' }

if (-not $Version) {
    $commit = (& git -C $Repo rev-parse --short HEAD).Trim()
    $Version = (Get-Date -Format 'yyyy.MM.dd') + '-' + $commit
}
$dirty = & git -C $Repo status --porcelain -- unity winterwalk tools/install
if ($dirty) { Write-Warning 'There are uncommitted changes in unity\, winterwalk\ or tools\install\: the version stamp names the last commit.' }

$name = "BigChristmasWalk-$Version"
$stage = Join-Path $Dist $name
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$payload = Join-Path $stage 'payload'
New-Item -ItemType Directory -Force -Path $payload | Out-Null

function Copy-Into($from, $to) {
    if (-not (Test-Path -LiteralPath $from)) { throw "Missing: $from" }
    $dest = Join-Path $payload $to
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $from -Destination $dest -Recurse -Force
}

# the loader: Doorstop's proxy DLL and version marker, from the game folder this PC set up
Copy-Into (Join-Path $Game 'winhttp.dll') 'winhttp.dll'
Copy-Into (Join-Path $Game '.doorstop_version') '.doorstop_version'
$ini = Get-Content (Join-Path $Game 'doorstop_config.ini')
$ini = $ini -replace '^enabled\s*=.*$', 'enabled = true' `
            -replace '^target_assembly\s*=.*$', 'target_assembly = BepInEx\core\BepInEx.Unity.IL2CPP.dll' `
            -replace '^coreclr_path\s*=.*$', 'coreclr_path = dotnet\coreclr.dll' `
            -replace '^corlib_dir\s*=.*$', 'corlib_dir = dotnet'
Set-Content -Path (Join-Path $payload 'doorstop_config.ini') -Value $ini -Encoding ASCII
Copy-Into (Join-Path $Profile 'dotnet') 'dotnet'
Copy-Into (Join-Path $Profile 'BepInEx\core') 'BepInEx\core'

# BepInEx's settings, without the console window players do not need
$cfg = (Get-Content (Join-Path $Profile 'BepInEx\config\BepInEx.cfg') -Raw)
$cfg = [regex]::Replace($cfg, '(\[Logging\.Console\][\s\S]*?\r?\nEnabled = )true', '${1}false')
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'BepInEx\config') | Out-Null
Set-Content -Path (Join-Path $payload 'BepInEx\config\BepInEx.cfg') -Value $cfg -NoNewline -Encoding UTF8

# the mods
Copy-Into (Join-Path $Profile 'BepInEx\plugins\markviews-BigPuzzleSDK') 'BepInEx\plugins\markviews-BigPuzzleSDK'
# Packs go in their own folder, not the SDK's BigPuzzleSDK folder: packs there count as being made on this PC (the SDK's
# creator keys come on, Ctrl+F1 teleports to one); the SDK finds packs anywhere under plugins
$packs = Join-Path $Profile 'BepInEx\plugins\BigPuzzleSDK'
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'BepInEx\plugins\BigChristmasWalk') | Out-Null
foreach ($f in Get-ChildItem -Path $packs -File) {
    if ($f.Name -like 'BwmTutorial*') { continue }
    if ($f.Extension -notin '.puzzlepack', '.dll') { continue }
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $payload ('BepInEx\plugins\BigChristmasWalk\' + $f.Name))
}
$ww = Join-Path $Profile 'BepInEx\plugins\WinterWalk'
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'BepInEx\plugins\WinterWalk') | Out-Null
foreach ($f in Get-ChildItem -Path $ww -File) {
    if ($f.Extension -in '.pdb', '.cfg') { continue }
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $payload ('BepInEx\plugins\WinterWalk\' + $f.Name))
}

# the installer
foreach ($f in 'Install.cmd', 'Install.ps1', 'Install-Common.ps1', 'Uninstall.cmd', 'Uninstall.ps1', 'README.txt') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $f) -Destination (Join-Path $stage $f)
}
Set-Content -Path (Join-Path $stage 'version.txt') -Value $Version -Encoding ASCII
# the shipped components' licences (LGPL, MIT, Apache, BSD), as their licences require
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination (Join-Path $stage 'licenses') -Recurse

# zip it (System.IO.Compression copes with files over 2 GB in total; Compress-Archive in PowerShell 5 does not)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $Dist "$name.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

$files = @(Get-ChildItem -Path $payload -Recurse -File)
$size = ($files | Measure-Object Length -Sum).Sum
Write-Output ("Packaged {0}: {1} files, {2:N0} MB unpacked, zip {3:N0} MB" -f $name, $files.Count, ($size / 1MB), ((Get-Item $zip).Length / 1MB))
Write-Output "  folder: $stage"
Write-Output "  zip:    $zip"
