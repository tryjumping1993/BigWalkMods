#!/bin/bash
# Matched review shots of the original island and the Winter Isle -> shots/review/<island>-*.png
# Each eye-level view is taken from where the player actually lands, at the game camera's own height, with a level
# gaze; landings in water are skipped. usage: review-shots.sh [orig|winter|both]
here=$(dirname "$0"); cd /z/Programming/BigWalkMods
which=${1:-both}
c(){ curl -s -m 200 "http://127.0.0.1:17361/$1"; }
cam(){ c status | sed -n 's/^camera: \([^,]*\), \([^,]*\), \([^ ]*\) facing.*/\1 \2 \3/p'; }
settle(){ local last=x now; for i in $(seq 1 40); do sleep 0.5; now=$(cam); [ "$now" = "$last" ] && break; last=$now; done; echo "$now"; }
L="/z/Programming/BigWalkMods/logs/review-activity.log"; lg(){ echo "$(date '+%F %T') $*" >> "$L"; }
mkdir -p shots/review
lg "START review shots ($which)"
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
  n=0
  while read isl name x z dx dz; do
    [ $n -ge 8 ] && break
    c "teleport?x=$x&y=150&z=$z" >/dev/null; sleep 3; read px py pz <<< "$(settle)"; sleep 1.5
    if python -c "import sys; sys.exit(0 if $py > 0.6 else 1)"; then
      n=$((n+1)); read tx tz turned <<< "$(python -I "$here/pick-heading.py" $px $pz $dx $dz "$(c "look?x=$px&y=$py&z=$pz&n=24")")"
      c 'time?hour=13' >/dev/null
      c "shot?file=rv-$isl-eye-$n&cx=$px&cy=$py&cz=$pz&tx=$tx&ty=$py&tz=$tz&fov=70" >/dev/null; sleep 4
      echo "$isl eye-$n ($name) asked $x,$z  stood $px,$py,$pz  turned $turned deg"
    else echo "$isl $name asked $x,$z  landed in water ($py), skipped"; fi
  done < <(grep "^$1 " "$here/review-viewpoints.txt")
}
[ "$which" != winter ] && shoot_island orig
[ "$which" != orig ] && shoot_island winter
c 'teleport?puzzle=TheTree' >/dev/null
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
