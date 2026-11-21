#!/bin/bash
# Review shots of the great mountain -> shots/mountain/*.png   (Git Bash, with the game running: bwm.ps1 play)
# Twelve fixed views: four from the air round the mountain, then on the ground from the foot of the pistes to the
# summit. The player is moved near each view first, so the sky and the snow tracks belong to that place.
# It pauses between teleports on purpose: see the CPU note in CLAUDE.md.
cd /z/Programming/BigWalkMods
c(){ curl -s -m 200 "http://127.0.0.1:17361/$1"; }
ground(){ c "ground?x=$1&z=$2" | sed -n 's/^ground: \(-\{0,1\}[0-9.]*\).*/\1/p'; }
py(){ python -c "import math; print(round($1, 2))"; }
PX=-2250; PZ=-1900; B=0.32
sm(){ echo "(lambda t: t*t*(3-2*t))(min(1,max(0,$1)))"; }
swing(){ python -c "import math; print(round(0.045*math.sin($1/260.0+2.1)*$(sm "($1-300)/200.0"), 4))"; }      # as RunOffset(1, d) in WinterIslePuzzle.cs: keep in step
mkdir -p shots/mountain
c "set?path=WinterWalk.GondolaDriver.ForceOpen&value=true" >/dev/null      # the cabins out on the line
c "time?hour=13" >/dev/null
# name, bearing offset from the ski face's (radians), distance from the peak, camera height (a: above the sea,
# g: above the ground), then what it looks at: bearing offset, distance, height (a or g)
views="
air-east      0.00 2500 a520   0.00  300 a260
air-south    -1.35 1700 a430   0.00  100 a240
air-north     1.50 1600 a380   0.00  100 a240
air-west      3.14 1500 a420   0.00    0 a300
foot          0.00 1420 g2.4   0.00  600 a330
lower-run     0.00 1150 g2.0   0.00  800 g2
mid-run-down  0.00  700 g2.2   0.00 1300 g2
mid-run-up    0.00  800 g2.0   0.00  400 g2
forest        0.012 950 g1.8   0.05  880 g4
bowl          0.00  300 g2.5   0.00 1100 g2
summit        0.00  150 g2.5   0.00  900 g2
cliffs        2.40  760 g25    2.60  250 a330
"
echo "$views" | while read -r name b d h tb td th; do
  [ -z "$name" ] && continue
  # on-piste views (offsets under 0.1 rad) follow the middle run's own line, which swings a little (RunCentre)
  rb=$b; [ "$(py "1 if abs($b) < 0.1 and $d > 250 and '${h:0:1}' == 'g' else 0")" = 1 ] && rb=$(py "$b-$(swing $d)")
  rtb=$tb; [ "$(py "1 if abs($tb) < 0.1 and $td > 250 and '${th:0:1}' == 'g' else 0")" = 1 ] && rtb=$(py "$tb-$(swing $td)")
  x=$(py "$PX+math.cos($B+$rb)*$d"); z=$(py "$PZ+math.sin($B+$rb)*$d")
  tx=$(py "$PX+math.cos($B+$rtb)*$td"); tz=$(py "$PZ+math.sin($B+$rtb)*$td")
  if [ "${h:0:1}" = g ]; then g=$(ground $x $z); y=$(py "${g:-0}+${h:1}"); else y=${h:1}; fi
  if [ "${th:0:1}" = g ]; then g=$(ground $tx $tz); ty=$(py "${g:-0}+${th:1}"); else ty=${th:1}; fi
  c "time?hour=13" >/dev/null
  c "teleport?x=$x&y=$(py "$y+3")&z=$z" >/dev/null; sleep 7
  c "shot?file=mountain-$name&cx=$x&cy=$y&cz=$z&tx=$tx&ty=$ty&tz=$tz" >/dev/null; sleep 3
  mv -f "shots/mountain-$name.png" "shots/mountain/$name.png" 2>/dev/null
  echo "$name from $x,$y,$z to $tx,$ty,$tz"
done
