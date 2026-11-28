"""The Winter Isle railway's route: from a station on the plaza's side of the lowland, clockwise round the great
mountain twice, climbing at up to 8% (the line found by dynamic programming over the angle round the peak: the
radius at each step chosen so the track keeps near the ground, tunnelling where the ground stands above it and on
trestles where it falls away, never benched across the pistes, never over the sea), then into the summit's rock in
a rack-railway spiral at 14% up to the summit station on the crest.
  python tools/railroute.py      writes unity/Land/WinterIsle.rail (points every 4 m: x y z, and the stations)
Run it only when the mountain's shape changes: the island's build benches the ground along the line it reads."""
import math, os, struct
import numpy as np
from scipy import ndimage

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
d = open(os.path.join(REPO, "unity", "Land", "WinterIsle.land"), "rb").read()
x0, z0, cell, nx, nz = struct.unpack("<fffii", d[:20]); W, H = nx + 1, nz + 1
g = np.nan_to_num(np.frombuffer(d, dtype="<f4", count=W * H, offset=20), nan=-9).reshape((H, W)).astype(np.float64)
def ground(x, z): return ndimage.map_coordinates(g, [np.atleast_1d((np.asarray(z, float) - z0) / cell), np.atleast_1d((np.asarray(x, float) - x0) / cell)], order=1, mode="constant", cval=-9)
PX, PZ, SKI = -2250.0, -1900.0, 0.32
LAPS, EXTRA, H_SPIRAL, GMAX = 2, 1.5, 380.0, 0.08
import sys
TUN = float(sys.argv[1]) if len(sys.argv) > 1 else 2.6
start = (-780.0, -880.0)
phi0 = math.atan2(start[1] - PZ, start[0] - PX); span = 2 * math.pi * LAPS + EXTRA
dphi = 0.003; N = int(span / dphi); phis = phi0 - np.arange(N) * dphi
h0 = float(ground(start[0], start[1])[0]) + 1.5
ht = np.linspace(h0, H_SPIRAL, N)
rmin = ((H_SPIRAL - h0) / span) / GMAX
delta = 0.5 * dphi * 0.9
R = rmin * np.exp(np.arange(int(math.log(1950 / rmin) / delta)) * delta)
X = PX + np.cos(phis)[:, None] * R[None, :]; Z = PZ + np.sin(phis)[:, None] * R[None, :]
GR = ground(X.ravel(), Z.ravel()).reshape(X.shape).astype(np.float32)
off = np.abs((phis[:, None] - SKI + np.pi) % (2 * np.pi) - np.pi)
onSki = (off < 0.55) & (R[None, :] < 1470)
gap = GR - ht[:, None].astype(np.float32)
cost = np.where(gap > 4, TUN, 0) + np.where(gap < -4, (-gap - 4) * 0.08 + np.where(gap < -18, (-gap - 18) * 1.0, 0), 0)
cost = cost + np.where(GR < 1, 600.0, 0) + np.where(onSki & (np.abs(gap) < 7), 10.0, 0)
cost = (cost * (R[None, :] * dphi)).astype(np.float32)
S = len(R)
# states: the radius, and the last move (in, level, out); turning back the other way costs, so the line runs in long
# smooth curves along the contours instead of zigzagging
best = np.full((3, S), 1e18); best[1, (R > 1150) & (R < 1950) & (np.abs(gap[0]) < 3) & (GR[0] > 1)] = 0      # the start: open ground on the plaza's side
back = np.zeros((N, 3, S), np.int8)
turn = np.array([[0, 0.6, 3.0], [0.6, 0, 0.6], [3.0, 0.6, 0]])      # [from move][to move], per metre-ish
for k in range(1, N):
    step = (R * dphi).astype(np.float64)
    nb = np.full((3, S), 1e18); arg = np.zeros((3, S), np.int8)
    for to in range(3):
        mv = to - 1
        for fr in range(3):
            prev = best[fr]
            if mv == 1: prev = np.concatenate([[1e18], prev[:-1]])
            elif mv == -1: prev = np.concatenate([prev[1:], [1e18]])
            c = prev + step * (0.15 * abs(mv) + turn[fr, to])
            m = c < nb[to]; nb[to][m] = c[m]; arg[to][m] = fr
    best = nb + cost[k][None, :]; back[k] = arg
fr = int(np.argmin(best.min(axis=1))); i = int(np.argmin(best[fr])); idx = [i]
for k in range(N - 1, 0, -1):
    mv = fr - 1; pf = int(back[k][fr][i]); i = i - mv; fr = pf; idx.append(i)
rr = R[np.array(idx[::-1])]
rr = np.exp(ndimage.gaussian_filter1d(np.log(rr), 8, mode="nearest"))      # smooth: no curve tighter than a train takes
# the rack spiral: a straight tunnel in towards the peak, then round inside the summit's rock at 45 m from its middle,
# climbing at 14%, and out at the last onto the crest's north side, where the summit station stands
rEnd, phiS = rr[-1], phis[-1]
hel_phi, hel_r, hel_h = [], [], []
phi = phiS; r = rEnd; hgt = H_SPIRAL; HTOP = 538.0; GH = 0.14; RH = 45.0
while r > RH + 1:
    r -= 4.0; hgt += 0.04 * 4.0; hel_phi.append(phi); hel_r.append(r); hel_h.append(hgt)
STATION = math.radians(100.0)
while hgt < HTOP:
    phi -= 4.0 / RH; hgt = min(HTOP, hgt + GH * 4.0)
    hel_phi.append(phi); hel_r.append(RH); hel_h.append(hgt)
remaining = (phi - STATION) % (2 * math.pi)      # clockwise from here round to the station's bearing
if remaining < 1.5: remaining += 2 * math.pi
done = 0.0
while done < remaining:
    stepA = 4.0 / max(r, RH); phi -= stepA; done += stepA
    left = remaining - done
    r = RH + (75.0 - RH) * min(1.0, max(0.0, 1 - left / 1.4))
    hel_phi.append(phi); hel_r.append(r); hel_h.append(HTOP)
xs = np.concatenate([PX + np.cos(phis) * rr, PX + np.cos(hel_phi) * np.array(hel_r)])
zs = np.concatenate([PZ + np.sin(phis) * rr, PZ + np.sin(hel_phi) * np.array(hel_r)])
# resample every 4 m, with the height following the length (8% on the spiral, 14% on the rack)
seg = np.hypot(np.diff(xs), np.diff(zs)); L = np.concatenate([[0], np.cumsum(seg)])
nS = len(phis); Ls = L[nS - 1]
hs = np.concatenate([ht, hel_h])      # (on the spiral the height rises evenly with the angle, as the search assumed: steeper on the inner laps)
ts = np.arange(0, L[-1], 4.0)
px, pz, py = np.interp(ts, L, xs), np.interp(ts, L, zs), np.interp(ts, L, hs)
gy = ground(px, pz); gapv = gy - py
# stations: the lower terminus, then on level stretches (bench) every 900 m or more, then the summit
stations = [0]
bench = np.abs(gapv) < 4
run = 0
for k in range(len(ts)):
    run = run + 1 if bench[k] else 0
    if run >= 10 and ts[k] - ts[stations[-1]] > 700 and ts[k] < ts[-1] - 600 and py[k] < H_SPIRAL - 5: stations.append(k - 6)
stations.append(len(ts) - 1)
out = os.path.join(REPO, "unity", "Land", "WinterIsle.rail")
with open(out, "w") as f:
    f.write("# the Winter Isle railway, from tools/railroute.py: P x y z (track level), every 4 m; S index (a station)\n")
    for k in range(len(ts)): f.write("P %.2f %.2f %.2f\n" % (px[k], py[k], pz[k]))
    for s in stations: f.write("S %d\n" % s)
seg4 = np.full(len(ts) - 1, 4.0)
print("railway: %.0f m, %d stations, top %.0f m; tunnel %.0f m, trestle >4 m %.0f m, >18 m %.0f m (max %.0f m), sea %.0f m"
      % (ts[-1], len(stations), py[-1], 4 * np.sum(gapv > 4), 4 * np.sum(gapv < -4), 4 * np.sum(gapv < -18), -gapv.min(), 4 * np.sum(gy < 1)))
hd = np.unwrap(np.arctan2(np.diff(pz), np.diff(px))); k = 10
print("tightest curve over 40 m: %.0f m" % np.min(40.0 / np.maximum(np.abs(hd[k:] - hd[:-k]), 1e-6)))
for s_ in stations: print("  station at %.0f m: (%.0f, %.0f) track %.0f ground %.0f" % (ts[s_], px[s_], pz[s_], py[s_], gy[s_]))
np.save(os.path.join("Z:/Temp/claude/BigWalkMods", "rail4.npy"), np.stack([px, pz, py, gy], 1))
