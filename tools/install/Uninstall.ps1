# Big Christmas Walk: removes the mod from Big Walk and puts back anything it replaced. Run Uninstall.cmd rather than
# this file. It removes exactly the files Install.cmd recorded (BepInEx\BigChristmasWalk.installed.txt), plus what
# BepInEx itself made while the mod ran (its interop and cache folders and logs) if nothing else uses BepInEx there.
param([string]$GameDir = '', [switch]$Yes)

$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $Here 'Install-Common.ps1')

Write-Host ''
Write-Host '=== Big Christmas Walk: uninstall ==='
$GameDir = Resolve-Game $GameDir

$manifestPath = Join-Path $GameDir $ManifestName
if (-not (Test-Path $manifestPath)) { Quit 1 "Big Christmas Walk is not installed in $GameDir (no $ManifestName)." }
$files = Get-Content $manifestPath | Where-Object { $_ -and -not $_.StartsWith('#') }
$removed = 0
foreach ($rel in $files) {
    $path = Join-Path $GameDir $rel
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force; $removed++ }
}
# put back what was there before
$backupDir = Join-Path $GameDir $BackupName
$restored = 0
if (Test-Path $backupDir) {
    foreach ($f in Get-ChildItem -Path $backupDir -Recurse -File -Force) {
        $rel = $f.FullName.Substring($backupDir.Length).TrimStart('\')
        $dest = Join-Path $GameDir $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
        $restored++
    }
    Remove-Item -LiteralPath $backupDir -Recurse -Force
}
Remove-Item -LiteralPath $manifestPath -Force
# what BepInEx generated while running, if BepInEx itself is gone now (nobody else's install left behind)
$bep = Join-Path $GameDir 'BepInEx'
if (-not (Test-Path (Join-Path $bep 'core\*.dll'))) {
    foreach ($generated in 'interop', 'cache', 'unity-libs', 'config', 'plugins', 'patchers', 'LogOutput.log', 'ErrorLog.log') {
        $p = Join-Path $bep $generated
        if (Test-Path $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
}
# empty folders the install left
foreach ($dir in @((Join-Path $GameDir 'BepInEx'), (Join-Path $GameDir 'dotnet'))) {
    if (Test-Path $dir) {
        Get-ChildItem -Path $dir -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { if (-not (Get-ChildItem -LiteralPath $_.FullName -Force)) { Remove-Item -LiteralPath $_.FullName -Force } }
        if (-not (Get-ChildItem -LiteralPath $dir -Force)) { Remove-Item -LiteralPath $dir -Force }
    }
}
Quit 0 "Removed $removed files$(if ($restored) { " and put back $restored that were there before" }). Big Walk is back to how it was."
