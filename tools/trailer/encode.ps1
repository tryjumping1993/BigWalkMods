# Big Christmas Walk trailer: turns the frames capture.py shot (shots\trailer\f00001.png ...) into MP4s.
#   powershell -ExecutionPolicy Bypass -File tools\trailer\encode.ps1 [-FramesDir dir] [-OutDir dir] [-Crf 22]
# Writes <OutDir>\big-christmas-walk-trailer.mp4 (1920x1080) and big-christmas-walk-trailer-720p.mp4 (1280x720):
# H.264 (high profile), yuv420p, 30 fps, lanczos scaling, +faststart, no audio track, nothing drawn over the frames.
# The 1080p file is held under -MaxMB (14 MB) whatever the length: a constant-quality encode (CRF) capped by a peak
# rate, and encoded again at a higher CRF if it still comes out over. ffmpeg runs off the faulty CPU cores
# ($env:BWM_AFFINITY, else FFFFFFCF: not logical CPUs 4 and 5) at below-normal priority, with few threads.
param(
    [string]$FramesDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'shots\trailer'),
    [string]$OutDir = 'Z:\Temp\claude\BigWalkMods\trailer',
    [string]$Name = 'big-christmas-walk-trailer',
    [int]$Crf = 22,
    [double]$MaxMB = 14.0,
    [int]$Fps = 30,
    [int]$Threads = 8,
    [string]$Ffmpeg = 'Z:\Tools\ffmpeg\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe'
)
$ErrorActionPreference = 'Stop'
$ffprobe = Join-Path (Split-Path -Parent $Ffmpeg) 'ffprobe.exe'
if (-not (Test-Path $Ffmpeg)) { throw "ffmpeg not found at $Ffmpeg" }
if (-not (Test-Path $FramesDir)) { throw "no frames folder $FramesDir (run capture.py first)" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# The frames: f00001.png ... with no gaps (ffmpeg's image reader stops at the first missing number)
$numbers = @(Get-ChildItem -LiteralPath $FramesDir -Filter 'f*.png' | Where-Object { $_.Name -match '^f(\d{5})\.png$' } |
    ForEach-Object { [int]$Matches[1] } | Sort-Object)
if ($numbers.Count -eq 0) { throw "no f#####.png frames in $FramesDir" }
$first = $numbers[0]; $last = $numbers[-1]
$gaps = @()
for ($i = 1; $i -lt $numbers.Count; $i++) { if ($numbers[$i] -ne $numbers[$i - 1] + 1) { $gaps += ($numbers[$i - 1] + 1) } }
if ($gaps.Count -gt 0) { throw ("frames missing after a gap at {0} ({1} gaps): run capture.py again to fill them" -f $gaps[0], $gaps.Count) }
$csv = Join-Path $PSScriptRoot 'frames.csv'
if (Test-Path $csv) {
    $expected = (Get-Content $csv | Measure-Object -Line).Lines - 1
    if ($numbers.Count -ne $expected) { Write-Warning ("{0} frames in {1}, but frames.csv has {2}" -f $numbers.Count, $FramesDir, $expected) }
}
# frames shot from an older frames.csv (capture.py records the hash of the plan it shot from)
$stamp = Join-Path $FramesDir 'frames.sha1'
if ((Test-Path $csv) -and (Test-Path $stamp)) {
    $now = (Get-FileHash -Algorithm SHA1 $csv).Hash.ToLower()
    if ((Get-Content $stamp -TotalCount 1).Trim().ToLower() -ne $now) { Write-Warning "these frames were shot from a different frames.csv than tools\trailer\frames.csv" }
}
$seconds = $numbers.Count / $Fps
Write-Host ("frames {0}..{1}: {2} at {3} fps = {4:N2} s" -f $first, $last, $numbers.Count, $Fps, $seconds)

$mask = 'FFFFFFCF'
if ($env:BWM_AFFINITY) { $mask = $env:BWM_AFFINITY }

function Invoke-Ffmpeg([string]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Ffmpeg
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        $p.ProcessorAffinity = [IntPtr][Convert]::ToInt64($mask, 16)
        $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
    } catch { Write-Warning ("could not set ffmpeg's affinity: " + $_.Exception.Message) }
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "ffmpeg failed (exit code $($p.ExitCode))" }
}

function Encode-Trailer([int]$width, [int]$height, [int]$crf, [string]$maxrate, [string]$bufsize, [string]$out) {
    $pattern = Join-Path $FramesDir 'f%05d.png'
    $vf = "scale=${width}:${height}:flags=lanczos,format=yuv420p"
    $arguments = ('-hide_banner -loglevel warning -stats -y -framerate {0} -start_number {1} -i "{2}" -vf "{3}" ' +
        '-c:v libx264 -preset slow -profile:v high -crf {4} -maxrate {5} -bufsize {6} -g {7} -r {0} -pix_fmt yuv420p ' +
        '-threads {8} -an -movflags +faststart "{9}"') -f $Fps, $first, $pattern, $vf, $crf, $maxrate, $bufsize, ($Fps * 2), $Threads, $out
    Write-Host ("encoding {0}x{1} at CRF {2} (peak {3}) -> {4}" -f $width, $height, $crf, $maxrate, $out)
    Invoke-Ffmpeg $arguments
}

function Describe([string]$file) {
    $info = & $ffprobe -v error -show_entries 'stream=codec_type,codec_name,profile,width,height,pix_fmt,r_frame_rate,nb_frames:format=duration,size' -of compact $file
    $info | ForEach-Object { Write-Host ("  " + $_) }
}

# 1080p, held under MaxMB: the peak rate is set so the whole length fits, with room for the container
$limit = [long]($MaxMB * 1000 * 1000)
$kbps = [int][Math]::Floor($limit * 8 / 1000 / [Math]::Max($seconds, 1) * 0.9)
$kbps = [Math]::Min($kbps, 12000)
$out1080 = Join-Path $OutDir "$Name.mp4"
$crf1080 = $Crf
for ($try = 0; $try -lt 4; $try++) {
    Encode-Trailer 1920 1080 $crf1080 "${kbps}k" ("{0}k" -f (2 * $kbps)) $out1080
    $size = (Get-Item $out1080).Length
    Write-Host ("  {0:N2} MB" -f ($size / 1e6))
    if ($size -le $limit) { break }
    $crf1080 += 2
    $kbps = [int]($kbps * 0.88)
    Write-Host "  over $MaxMB MB: again at CRF $crf1080, peak ${kbps}k"
}
if ((Get-Item $out1080).Length -gt $limit) { Write-Warning "the 1080p trailer is still over $MaxMB MB" }
Describe $out1080

# 720p fallback (about half the 1080p peak rate)
$out720 = Join-Path $OutDir "$Name-720p.mp4"
$kbps720 = [int]($kbps * 0.55)
Encode-Trailer 1280 720 ($Crf + 1) "${kbps720}k" ("{0}k" -f (2 * $kbps720)) $out720
Write-Host ("  {0:N2} MB" -f ((Get-Item $out720).Length / 1e6))
Describe $out720
Write-Host "wrote $out1080 and $out720"
