# Big Christmas Walk: installs the mod into Big Walk (Steam). Run Install.cmd (double-click) rather than this file.
#
# What it does:
#  1. finds Big Walk through Steam (or asks where it is), and stops if the game is running;
#  2. copies the mod (the BepInEx loader, its .NET runtime, the Big Puzzle SDK, the Winter Isle packs and the Winter Walk
#     plugin) from the "payload" folder beside this script into the game's folder;
#  3. remembers every file it put there (BepInEx\BigChristmasWalk.installed.txt), and keeps a copy of any file it
#     replaced (BepInEx\BigChristmasWalk.backup\), so Uninstall.cmd can put the game back exactly as it was;
#  4. installing a newer version over an older one updates it, and removes files the new version no longer has.
# After installing, start Big Walk from Steam as usual. The first start takes a few minutes (BepInEx prepares itself, and
# needs the internet once); later starts are quick.
param([string]$GameDir = '', [switch]$Yes, [switch]$Force)

$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $Here 'Install-Common.ps1')
$Payload = Join-Path $Here 'payload'

Say ''
Say '=== Big Christmas Walk: install ==='
$versionFile = Join-Path $Here 'version.txt'
$version = if (Test-Path $versionFile) { (Get-Content $versionFile -TotalCount 1).Trim() } else { 'unknown' }
Say "Version $version"
if (-not (Test-Path $Payload)) { Quit 1 'The "payload" folder is missing. Unzip the whole download first, then run Install.cmd from inside it.' }

$GameDir = Resolve-Game $GameDir
Say "Big Walk is in: $GameDir"

# The modder's own machine launches from a profile elsewhere (doorstop pointing outside the game folder): leave it alone
$ini = Join-Path $GameDir 'doorstop_config.ini'
if ((Test-Path $ini) -and -not $Force) {
    $target = (Select-String -Path $ini -Pattern '^\s*target_assembly\s*=\s*(.+)$' | Select-Object -First 1)
    if ($target -and [IO.Path]::IsPathRooted($target.Matches[0].Groups[1].Value.Trim())) {
        Quit 1 ("This game folder loads mods from another folder ($($target.Matches[0].Groups[1].Value.Trim())): it is a modding setup. " +
            'Nothing was changed. (Run with -Force to install over it anyway.)')
    }
}

# ---- another BepInEx there already? ----
$manifestPath = Join-Path $GameDir $ManifestName
$previous = @()
if (Test-Path $manifestPath) {
    $previous = @(Get-Content $manifestPath | Where-Object { $_ -and -not $_.StartsWith('#') })
    $old = (Get-Content $manifestPath -TotalCount 1) -replace '^# version ', ''
    Say "Updating the installed version ($old) to $version."
} elseif (Test-Path (Join-Path $GameDir 'BepInEx\core')) {
    Say ''
    Say 'This game folder already has BepInEx (another mod or mod manager put it there).' Yellow
    Say 'Installing replaces its loader files (they are backed up, and Uninstall.cmd puts them back).'
    if (-not (Ask 'Install anyway?')) { Quit 1 'Nothing was changed.' }
}
$wasThere = @{}
foreach ($rel in $previous) { $wasThere[$rel.ToLowerInvariant()] = $true }

# ---- copy ----
$files = @(Get-ChildItem -Path $Payload -Recurse -File -Force)
$backupDir = Join-Path $GameDir $BackupName
$installed = New-Object System.Collections.Generic.List[string]
$count = 0
Say ''
Say ("Copying {0} files ({1:N0} MB)..." -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
foreach ($f in $files) {
    $rel = $f.FullName.Substring($Payload.Length).TrimStart('\')
    $dest = Join-Path $GameDir $rel
    $destDir = Split-Path -Parent $dest
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
    # a file that was there before us (not one of ours from an earlier version): keep a copy
    if ((Test-Path -LiteralPath $dest) -and -not $wasThere.ContainsKey($rel.ToLowerInvariant())) {
        $keep = Join-Path $backupDir $rel
        if (-not (Test-Path -LiteralPath $keep)) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $keep) | Out-Null
            Copy-Item -LiteralPath $dest -Destination $keep -Force
        }
    }
    Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
    $installed.Add($rel)
    $count++
    if ($count % 10 -eq 0) { Write-Progress -Activity 'Installing Big Christmas Walk' -Status $rel -PercentComplete ([int](100 * $count / $files.Count)) }
}
Write-Progress -Activity 'Installing Big Christmas Walk' -Completed

# files an older version installed that this one no longer has
$now = @{}
foreach ($rel in $installed) { $now[$rel.ToLowerInvariant()] = $true }
foreach ($rel in $previous) {
    if ($now.ContainsKey($rel.ToLowerInvariant())) { continue }
    $path = Join-Path $GameDir $rel
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}

$lines = @("# version $version", "# installed $(Get-Date -Format 'yyyy-MM-dd HH:mm')") + $installed
Set-Content -Path $manifestPath -Value $lines -Encoding UTF8

Quit 0 ("Installed Big Christmas Walk $version.`n`nStart Big Walk from Steam as usual. The first start takes a few minutes while BepInEx " +
    "prepares itself (it needs the internet that once).`nEveryone you play with needs this same version. To remove it, run Uninstall.cmd.")
