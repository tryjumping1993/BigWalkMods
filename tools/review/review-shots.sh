#!/bin/bash
# Matched review shots of the original island and the Winter Isle -> shots/review/<island>-*.png
#   review-shots.sh [orig|winter|both]        (Git Bash, with the game running: bwm.ps1 play)
# Per island: an aerial, a mid shot, eight eye-level shots and one shore shot.
# - Eye level: the player is set down on the ground at each point in review-viewpoints.txt (the Winter Isle's are
#   taken afresh from world/winter-viewpoints.tsv, twelve spread along its trails), and the shot is taken from the
#   game camera's own position, looking level along the planned heading. Points under water are skipped.
# - Shore: from the even eye points, due south until the sea; the player stands 10 m back from the water, looking
#   south-east, over the water's edge and along the shore. Taken at the first one where a player can stand within 6 m of sea level (not a cliff).
# - pick-heading.py turns the camera to the nearest open heading when the view is blocked within 6 m.
# It pauses between teleports on purpose: see the CPU note in CLAUDE.md.
here=$(dirname "$0"); cd /z/Programming/BigWalkMods
which=${1:-both}
c(){ curl -s -m 200 "http://127.0.0.1:17361/$1"; }
cam(){ c status | sed -n 's/^camera: \([^,]*\), \([^,]*\), \([^ ]*\) facing.*/\1 \2 \3/p'; }
settle(){ local last=x now; for i in $(seq 1 40); do sleep 0.5; now=$(cam); [ "$now" = "$last" ] && break; last=$now; done; echo "$now"; }
ground(){ c "ground?x=$1&z=$2&all=1" | sed -n 's/^ground: \(-\{0,1\}[0-9.]*\).*/\1/p'; }
# sets the player down on the ground at x z; prints "px py pz" of the camera, or nothing if there is no ground or it is under water
stand(){
  c "teleport?x=$1&y=150&z=$2" >/dev/null; sleep 2.5
  local g; g=$(ground $1 $2)
  if [ -z "$g" ]; then
    # nothing to stand on is loaded there yet (or it is open water): let the player come down where they will, and
    # take that if it is land
    local at; at=$(settle); read -r _ fy _ <<< "$at"
    [ -n "$fy" ] && python -c "import sys; sys.exit(0 if $fy > 0.6 else 1)" && echo "$at"
    return
  fi
  python -c "import sys; sys.exit(0 if $g > 0.5 else 1)" || return
  c "teleport?x=$1&y=$(python -c "print($g+1.3)")&z=$2" >/dev/null; sleep 1; settle
}
L="/z/Programming/BigWalkMods/logs/review-activity.log"; mkdir -p logs; lg(){ echo "$(date '+%F %T') $*" >> "$L"; }
mkdir -p shots/review
lg "START review shots ($which)"
# the Winter Isle's points: twelve spread evenly through the list its build writes
winter_points(){ python - <<'PY'
rows=[l.split() for l in open('world/winter-viewpoints.tsv').read().splitlines()[1:] if l.strip()]
n=len(rows); pick=[rows[round(i*(n-1)/11)] for i in range(12)] if n>=12 else rows
for i,r in enumerate(pick): print('winter','w%d'%(i+1),*r)
PY
}
points(){ if [ $1 = winter ]; then winter_points; else grep "^$1 " "$here/review-viewpoints.txt"; fi; }
shoot_island(){
  rm -f shots/review/$1-*.png
  if [ $1 = orig ]; then
    c 'teleport?x=-100&y=70&z=-560' >/dev/null; settle >/dev/null; sleep 3
    c 'time?hour=13' >/dev/null; c 'shot?file=rv-orig-aerial&cx=-450&cy=330&cz=-1350&tx=250&ty=10&tz=-300&fov=55' >/dev/null; sleep 4
    c 'time?hour=13' >/dev/null; c 'shot?file=rv-orig-mid&cx=-60&cy=95&cz=-640&tx=150&ty=60&tz=-380&fov=70' >/dev/null; sleep 4
  else
    c 'teleport?puzzle=TheTree' >/dev/null; settle >/dev/null; sleep 3
    c 'time?hour=13' >/dev/null; c 'shot?file=rv-winter-aerial&cx=-300&cy=330&cz=-480&tx=-1100&ty=10&tz=-1100&fov=60' >/dev/null; sleep 4
    c 'time?hour=13' >/dev/null; c 'shot?file=rv-winter-mid&cx=-640&cy=55&cz=-700&tx=-950&ty=40&tz=-850&fov=70' >/dev/null; sleep 4
  fi
  n=0; shore=0
  while read isl name x z dx dz; do
    [ $n -ge 8 ] && break
    read px py pz <<< "$(stand $x $z)"
    if [ -n "$py" ]; then
      sleep 1.5
      n=$((n+1)); read tx tz turned <<< "$(python -I "$here/pick-heading.py" $px $pz $dx $dz "$(c "look?x=$px&y=$py&z=$pz&n=24")")"
      c 'time?hour=13' >/dev/null
      c "shot?file=rv-$isl-eye-$n&cx=$px&cy=$py&cz=$pz&tx=$tx&ty=$py&tz=$tz&fov=70" >/dev/null; sleep 4
      echo "$isl eye-$n ($name) at $x,$z  camera $px,$py,$pz  turned $turned deg"
      if [ $shore -lt 1 ] && [ $((n % 2)) = 0 ]; then
        # due south to the sea, in 8 m steps (moving the player along so the land there is loaded)
        sz=$z; found=
        for step in $(seq 1 150); do
          sz=$(python -c "print($sz-8)")
          [ $((step % 12)) = 0 ] && { c "teleport?x=$x&y=150&z=$sz" >/dev/null; sleep 2; }
          g=$(ground $x $sz)
          if [ -z "$g" ] || python -c "import sys; sys.exit(0 if $g < 0.3 else 1)"; then found=1; break; fi
        done
        if [ -n "$found" ]; then
          back=$(python -c "print($sz+10)")
          read px py pz <<< "$(stand $x $back)"
          # only where a player can stand: not slid off a cliff into the sea
          if [ -n "$py" ] && python -c "import sys, math; sys.exit(0 if 0.5 < $py < 6.5 and math.hypot($px-($x), $pz-($back)) < 8 else 1)"; then
            sleep 1.5; shore=$((shore+1))
            # looking south-east, so the water and the shoreline are both in the frame (due east put the sea out of it)
            read tx tz turned <<< "$(python -I "$here/pick-heading.py" $px $pz 0.45 -0.89 "$(c "look?x=$px&y=$py&z=$pz&n=24")")"
            c 'time?hour=13' >/dev/null
            c "shot?file=rv-$isl-shore&cx=$px&cy=$py&cz=$pz&tx=$tx&ty=$py&tz=$tz&fov=70" >/dev/null; sleep 4
            echo "$isl shore south of $name at $x,$back  camera $px,$py,$pz  turned $turned deg"
          else echo "$isl no standing shore south of $name (cliff, water or too high)"; fi
        fi
      fi
    else echo "$isl $name at $x,$z  no ground above water, skipped"; fi
  done < <(points $1)
}
[ "$which" != winter ] && shoot_island orig
[ "$which" != orig ] && shoot_island winter
c 'teleport?puzzle=TheTree' >/dev/null
# The Winter Isle's shore: if no viewpoint found one, stand on the beach nearest the plaza that has sea a few metres
# to the south (found from the island's height grid) and look east along it, as above
if [ "$which" != orig ] && [ ! -e shots/rv-winter-shore.png ]; then
  read -r sx sz sh < <(python -X utf8 - <<'PY' | tr -d '
'
import struct, numpy as np
d = open("unity/Land/WinterIsle.land", "rb").read()
x0, z0, cell, nx, nz = struct.unpack("<fffii", d[:20]); W, H = nx + 1, nz + 1
g = np.nan_to_num(np.frombuffer(d, dtype="<f4", count=W * H, offset=20), nan=-1).reshape((H, W))
best = None
for j in range(8, H - 8):
    for i in range(8, W - 8):
        h = g[j, i]
        if not (1.0 < h < 1.8) or g[j - 4, i] > 0.2 or g[j - 2, i] > 1.2: continue
        strip = g[j, i - 6:i + 7]
        if strip.min() < 0.6 or strip.max() > 3: continue
        x, z = x0 + i * cell, z0 + j * cell
        dd = (x + 705) ** 2 + (z + 795) ** 2
        if best is None or dd < best[0]: best = (dd, x, z, h)
print(best[1], best[2], round(float(best[3]), 2))
PY
)
  if [ -n "$sx" ]; then
    c "teleport?x=$sx&y=$(python -c "print($sh+1.5)")&z=$sz" >/dev/null; sleep 20
    c "shot?file=rv-winter-shore&cx=$sx&cy=$(python -c "print($sh+0.75)")&cz=$sz&tx=$(python -c "print($sx+9)")&ty=$(python -c "print($sh+0.6)")&tz=$(python -c "print($sz-8)")&fov=70" >/dev/null; sleep 4
    echo "winter shore from the beach nearest the plaza at $sx,$sz"
  fi
fi
for f in shots/rv-*.png; do [ -e "$f" ] || continue; b=$(basename "$f" .png); mv "$f" "shots/review/${b#rv-}.png"; done
lg "END review shots ($which)"
python -X utf8 - <<'PY'
from PIL import Image, ImageDraw
import glob, os
for isl in ("orig", "winter"):
    fs = sorted(glob.glob(f"shots/review/{isl}-*.png"))
    if not fs: continue
    w, h = 640, 360; cols = 4; rows = (len(fs) + cols - 1) // cols
    sheet = Image.new("RGB", (w * cols, h * rows), "black"); d = ImageDraw.Draw(sheet)
    for i, f in enumerate(fs):
        sheet.paste(Image.open(f).convert("RGB").resize((w, h)), ((i % cols) * w, (i // cols) * h))
        d.text(((i % cols) * w + 8, (i // cols) * h + 8), os.path.basename(f), fill="yellow")
    os.makedirs("shots/sheets", exist_ok=True); sheet.save(f"shots/sheets/{isl}.jpg", quality=85)
PY
