# Shared by Install.ps1 and Uninstall.ps1 (dot-sourced; expects $Yes from the caller's parameters).
$AppId = '1478500'
$ManifestName = 'BepInEx\BigChristmasWalk.installed.txt'
$BackupName = 'BepInEx\BigChristmasWalk.backup'

function Say($text, $colour) { if ($colour) { Write-Host $text -ForegroundColor $colour } else { Write-Host $text } }
function Ask($question) {
    if ($Yes) { return $true }
    $a = Read-Host ($question + ' [y/n]')
    return $a -match '^(y|yes)$'
}
function Quit($code, $text) {
    if ($text) { Write-Host ''; Write-Host $text -ForegroundColor $(if ($code -eq 0) { 'Green' } else { 'Red' }) }
    if (-not $Yes) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
    exit $code
}

# Big Walk's folder, from Steam's registry entry and its library list; $null when not found
function Find-Game {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $k = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in 'SteamPath', 'InstallPath') { if ($k.$name) { $roots += ($k.$name -replace '/', '\') } }
        } catch { }
    }
    $roots += 'C:\Program Files (x86)\Steam'
    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = [IO.Path]::Combine($root, 'steamapps', 'libraryfolders.vdf')
        if ([IO.File]::Exists($vdf)) {
            foreach ($m in [regex]::Matches([IO.File]::ReadAllText($vdf),'"path"\s+"([^"]+)"')) { $libraries += ($m.Groups[1].Value -replace '\\\\', '\') }
        }
    }
    foreach ($lib in ($libraries | Select-Object -Unique)) {
        # (a library on a drive that is not plugged in: Join-Path would throw, so plain strings)
        try {
            $acf = [IO.Path]::Combine($lib, 'steamapps', "appmanifest_$AppId.acf")
            if (-not [IO.File]::Exists($acf)) { continue }
            $m = [regex]::Match([IO.File]::ReadAllText($acf), '"installdir"\s+"([^"]+)"')
            $dir = [IO.Path]::Combine($lib, 'steamapps', 'common', $(if ($m.Success) { $m.Groups[1].Value } else { 'Big Walk' }))
            if ([IO.File]::Exists([IO.Path]::Combine($dir, 'Big Walk.exe'))) { return $dir }
        } catch { }
    }
    return $null
}

# The game folder from -GameDir, Steam, or the player; quits when there is no Big Walk.exe there
function Resolve-Game($given) {
    $dir = $given
    if (-not $dir) { $dir = Find-Game }
    if (-not $dir) {
        Say 'Big Walk was not found through Steam.'
        $dir = (Read-Host 'Paste the folder that holds "Big Walk.exe" (Steam: right-click Big Walk > Manage > Browse local files)').Trim('"', ' ')
    }
    if (-not $dir -or -not (Test-Path (Join-Path $dir 'Big Walk.exe'))) { Quit 1 "No Big Walk.exe in '$dir'." }
    if (Get-Process -Name 'Big Walk' -ErrorAction SilentlyContinue) { Quit 1 'Big Walk is running. Close it, then try again.' }
    return (Resolve-Path $dir).Path
}
