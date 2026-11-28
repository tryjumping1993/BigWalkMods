"""Checks the mine's line as built: each station's open track, other parts of the line near a station, and where the
line crosses the railway (heights)."""
import math, struct
import numpy as np

R = r'Z:\Programming\BigWalkMods' + '\\'
mine = []
for line in open(R + r'profile\BepInEx\plugins\WinterWalk\WinterIsle.mineline'):
    f = line.split()
    if not f or f[0] in ('#', 'L'):
        continue
    mine.append([float(f[0]), float(f[1]), float(f[2]), int(f[3]), int(f[4]) if len(f) > 4 else 0])
n = len(mine)
rail = []
name = None
for line in open(R + r'profile\BepInEx\plugins\WinterWalk\WinterIsle.railline'):
    f = line.split()
    if not f:
        continue
    if f[0] == 'LINE':
        name = f[1]
    if f[0] == 'P':
        rail.append((float(f[1]), float(f[2]), float(f[3]), name, f[4] if len(f) > 4 else '?'))
f = open(R + r'unity\Land\WinterIsle.land', 'rb').read()
x0, z0, c, nx, nz = struct.unpack('<fffii', f[:20])
h = np.frombuffer(f[20:20 + 4 * (nx + 1) * (nz + 1)], dtype='<f4').reshape(nz + 1, nx + 1)


def ground(x, z):
    return h[int(round((z - z0) / c)), int(round((x - x0) / c))]


print(n, 'line points,', sum(1 for p in mine if p[3] == 0), 'in the open')
for s in [i for i, p in enumerate(mine) if p[4]]:
    p = mine[s]
    if p[3] != 0:
        continue
    ys = [mine[(s + d) % n][1] for d in range(-4, 5)]
    gs = [ground(mine[(s + d) % n][0], mine[(s + d) % n][2]) for d in range(-4, 5)]
    near = [(i, q) for i, q in enumerate(mine) if math.hypot(q[0] - p[0], q[2] - p[2]) < 20 and min(abs(i - s), n - abs(i - s)) > 12]
    a = mine[(s - 3) % n]; b = mine[(s + 3) % n]
    turn = []
    for d in (8, 16, 24, 32):
        q0 = mine[(s + d) % n]; q1 = mine[(s + d + 3) % n]
        turn.append(math.degrees(math.atan2(q1[2] - q0[2], q1[0] - q0[0]) - math.atan2(b[2] - a[2], b[0] - a[0])) % 360)
    print('stop %4d (%7.1f, %6.2f, %7.1f): track %.2f..%.2f, ground under it %.2f..%.2f above the rail; %s; heading change after 16/32/48/64 m: %s'
          % (s, p[0], p[1], p[2], min(ys), max(ys), min(g - y for g, y in zip(gs, ys)), max(g - y for g, y in zip(gs, ys)),
             ('%d OTHER LINE POINTS WITHIN 20 m' % len(near)) if near else 'no other line within 20 m', ' '.join('%.0f' % (t if t <= 180 else t - 360) for t in turn)))
found = []
for i, m in enumerate(mine):
    best = None
    for r in rail:
        if abs(m[0] - r[0]) < 9 and abs(m[2] - r[2]) < 9:
            d = math.hypot(m[0] - r[0], m[2] - r[2])
            if d < 6 and (best is None or d < best[0]):
                best = (d, r)
    if best:
        found.append((i, m, best[1]))
groups = []
for i, m, r in found:
    if groups and i - groups[-1][-1][0] <= 3:
        groups[-1].append((i, m, r))
    else:
        groups.append([(i, m, r)])
print(len(groups), 'places where the mine comes within 6 m (in plan) of the railway:')
for g in groups:
    worst = None
    for i, m, r in g:
        floor = m[1] - 0.17
        top = floor + 1.75 + 0.55
        clash = top > r[1] - 0.75 and floor - 0.3 < r[1] + 6.5
        gap = (r[1] - 0.75 - top) if top <= r[1] else (floor - 0.3 - (r[1] + 6.5))
        if worst is None or clash and not worst[0] or gap < worst[1]:
            worst = (clash, gap, i, m, r)
    clash, gap, i, m, r = worst
    print('  mine %4d (%6.0f, %6.0f) floor %.1f roof %.1f | railway %s rails at %.1f | %s' % (i, m[0], m[2], m[1] - 0.17, m[1] - 0.17 + 2.3, r[3], r[1], 'CLASH' if clash else 'clear by %.1f m' % gap))
