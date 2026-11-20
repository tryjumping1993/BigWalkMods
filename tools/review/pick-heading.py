# usage: pick-heading.py px pz dx dz "open: d0 d1 ..."  ->  "tx tz turned"
# Keeps the planned heading unless the view that way is blocked within 6 m; then turns to the nearest heading
# that is open for 6 m or more (or the most open one). The same rule for both islands.
import math, sys
px, pz, dx, dz = map(float, sys.argv[1:5])
d = [float(v) for v in sys.argv[5].split(":", 1)[1].split()]
n = len(d); step = 2 * math.pi / n
plan = math.atan2(dx, dz)
def diff(a): return abs((a - plan + math.pi) % (2 * math.pi) - math.pi)
i0 = min(range(n), key=lambda i: diff(i * step))
if d[i0] >= 6:
    a = plan
else:
    open_ = [i for i in range(n) if d[i] >= 6]
    a = min(open_, key=lambda i: diff(i * step)) * step if open_ else max(range(n), key=lambda i: d[i]) * step
print(round(px + math.sin(a) * 60, 2), round(pz + math.cos(a) * 60, 2), round(math.degrees(diff(a))))
