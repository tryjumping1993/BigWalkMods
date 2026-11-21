"""Big Christmas Walk: the fly-over trailer's camera path.

  py.cmd tools\\trailer\\plan.py [--seconds 74] [--no-views]

Builds one continuous camera flight over the Winter Isle from keyframes (centripetal Catmull-Rom through the camera
and look-target keys, paced by a speed per key, eased in at the start and out at the end), checks it against the
island's height grid (unity/Land/WinterIsle.land), the base game's terrain (decoded from world/heightmap.png, which
the island's grid does not cover) and the island's tall structures, picks a ground point for the player every 90
frames (the sky, fog and weather follow the player, not the shot camera) out of every frame's view, and writes:

  tools/trailer/frames.csv          frame, cx, cy, cz, tx, ty, tz, fov, teleport_x, teleport_y, teleport_z (or empty)
  <out>/path.png                    the flight over a shaded map of the island and the mainland
  <out>/profile.png                 heights and clearances along the flight
  <out>/views.jpg                   rough software renders of the height field from 16 frames (framing check)

<out> is Z:/Temp/claude/BigWalkMods/trailer. Nothing here talks to the game: capture.py does that.
"""
import argparse, csv, glob, json, math, os, struct, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT = r"Z:\Temp\claude\BigWalkMods\trailer"
FPS = 30
BLOCK = 90                                   # frames between teleports
MIN_CLEAR = 15.0                             # metres above the terrain, everywhere
SUMMIT = np.array([-2250.0, -1900.0])
SUMMIT_TOP = 601.0
SUMMIT_ZONE = 200.0                          # within this of the summit (horizontally)...
SUMMIT_MIN_Y = SUMMIT_TOP + 25.0             # ...the camera stays this high
PLAZA = np.array([-705.0, -795.0])
ASPECT = 16.0 / 9.0
UP = np.array([0.0, 1.0, 0.0])


def smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def unit(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v, axis=-1, keepdims=True)


def bearing_dir(deg):
    a = math.radians(deg)
    return np.array([math.sin(a), math.cos(a)])


# ------------------------------------------------------------------------------------------------ ground

class Ground:
    """The island's height grid, with the base game's terrain (decoded from world/heightmap.png) where the grid is
    NaN, and the sea at 0. 'peak' sampling takes the highest of a cell's four corners instead of interpolating, which
    stands in for the crags the game's mesh adds on steep ground."""

    def __init__(self):
        d = open(os.path.join(REPO, "unity", "Land", "WinterIsle.land"), "rb").read()
        self.x0, self.z0, self.cell, self.nx, self.nz = struct.unpack("<fffii", d[:20])
        n = (self.nx + 1) * (self.nz + 1)
        self.g = np.frombuffer(d, dtype="<f4", count=n, offset=20).reshape((self.nz + 1, self.nx + 1)).astype(np.float64)
        self.m = self._mainland()

    @staticmethod
    def _mainland():
        # world/heightmap.png (Cli.Survey): the base game's 2048 m square centred on 0,0 at 4 m a pixel, north up.
        # Sea is blue (B/G above 1.29); land runs green -> tan (0..80 m) -> white (80..160 m), darkened (all channels
        # alike) by the 10 m contours and the grid, so the channel ratios give the height back.
        im = np.asarray(Image.open(os.path.join(REPO, "world", "heightmap.png")).convert("RGB")).astype(np.float64) / 255.0
        im = im[::-1]                                                       # row 0 = south
        R, G, B = im[..., 0], im[..., 1], im[..., 2]
        Gs = np.maximum(G, 1e-3)
        bg, rg = B / Gs, R / Gs
        h = np.full(R.shape, np.nan)
        land = bg < 1.1
        upper = land & (bg > 0.64)
        with np.errstate(all="ignore"):
            u = np.clip((0.75 * rg - 0.55) / (0.2 + 0.1 * rg), 0, 1)       # green -> tan
            v = np.clip((0.65 * bg - 0.4) / (0.6 - 0.35 * bg), 0, 1)       # tan -> white
        t = np.where(upper, 0.5 + v / 2, u / 2)
        h[land] = t[land] * 160.0
        h[land & (t > 0.97)] = 230.0                                      # above the colour scale: assume high
        # the survey's marks (red spawn, yellow puzzle sites, magenta holes) are not heights: take the nearest real
        # pixel's instead
        marks = (G < 0.05) | ((R > 0.9) & (G > 0.8) & (B < 0.15))
        if marks.any():
            _, (jj, ii) = ndimage.distance_transform_edt(marks, return_indices=True)
            h = h[jj, ii]
        return h

    def _bilinear(self, grid, gx, gz, peak):
        H, W = grid.shape
        i = np.floor(gx).astype(np.int64)
        j = np.floor(gz).astype(np.int64)
        inside = (i >= 0) & (j >= 0) & (i < W - 1) & (j < H - 1)
        i = np.clip(i, 0, W - 2)
        j = np.clip(j, 0, H - 2)
        a, b, c, e = grid[j, i], grid[j, i + 1], grid[j + 1, i], grid[j + 1, i + 1]
        if peak:
            val = np.fmax(np.fmax(a, b), np.fmax(c, e))
        else:
            u, v = gx - i, gz - j
            val = (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + e * u) * v
        return np.where(inside, val, np.nan)

    def island(self, x, z, peak=False):
        x, z = np.asarray(x, float), np.asarray(z, float)
        return self._bilinear(self.g, (x - self.x0) / self.cell, (z - self.z0) / self.cell, peak)

    def mainland(self, x, z, peak=False):
        x, z = np.asarray(x, float), np.asarray(z, float)
        return self._bilinear(self.m, (x + 1024.0) / 4.0 - 0.5, (z + 1024.0) / 4.0 - 0.5, peak)

    def at(self, x, z, peak=False):
        i = self.island(x, z, peak)
        m = self.mainland(x, z, peak)
        return np.maximum(np.where(np.isfinite(i), i, np.where(np.isfinite(m), m, 0.0)), 0.0)

    def highest(self, x, z, r=8.0):
        """The highest ground (peak sampling) within r of each point."""
        x, z = np.atleast_1d(np.asarray(x, float)), np.atleast_1d(np.asarray(z, float))
        offs = [(0.0, 0.0)]
        for rr, k in ((r * 0.5, 8), (r, 12)):
            offs += [(rr * math.cos(2 * math.pi * q / k), rr * math.sin(2 * math.pi * q / k)) for q in range(k)]
        best = np.full(x.shape, -1e9)
        for ox, oz in offs:
            best = np.maximum(best, self.at(x + ox, z + oz, peak=True))
        return best

    def on_island(self, x, z):
        i = self.island(x, z)
        return np.isfinite(i) & (i > 0.15)


# ------------------------------------------------------------------------------------------------ the island's things

def run_centre(along):
    """Ski run 1 (the middle one) at a distance from the peak (WinterIslePuzzle.RunCentre / RunSwing / RunOffset)."""
    SKI_BEARING, SKI_RADIUS = 0.32, 1450.0
    swing = 0.05 * math.sin(along / 115.0 + 2.1) * float(smooth((along - 300.0) / 200.0))
    main = swing * (1.0 - float(smooth((along - (SKI_RADIUS - 230.0)) / 130.0)))
    ang = SKI_BEARING - main
    return SUMMIT + np.array([math.cos(ang), math.sin(ang)]) * along


def read_rail():
    pts = []
    f = os.path.join(REPO, "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.railline")
    if os.path.exists(f):
        for line in open(f):
            q = line.split()
            if q and q[0] == "P":
                pts.append(((float(q[1]) + float(q[4])) / 2, (float(q[2]) + float(q[5])) / 2, (float(q[3]) + float(q[6])) / 2, int(q[7])))
    return np.array(pts) if pts else np.zeros((0, 4))


def read_mine():
    pts = []
    f = os.path.join(REPO, "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.mineline")
    if os.path.exists(f):
        for line in open(f):
            q = line.split()
            if len(q) == 5 and q[0] not in ("L", "#"):
                pts.append([float(q[0]), float(q[1]), float(q[2]), float(q[3]), float(q[4])])
    return np.array(pts) if pts else np.zeros((0, 5))


def read_sites():
    sites = {}
    for f in glob.glob(os.path.join(REPO, "unity", "Assets", "Puzzles", "*", "site.json")):
        name = os.path.basename(os.path.dirname(f))
        if name in ("WinterIsle", "BwmTutorial", "IslandGear"):
            continue
        s = json.load(open(f))
        sites[name] = (s["x"], s["z"])
    return sites


class Structures:
    """What stands up from the ground and is not in the height grid: vertical cylinders (x, z, radius, top) standing on
    the ground, and 3D segments (cables)."""

    def __init__(self, gr):
        cyl, segs = [], []
        at = lambda x, z: float(gr.at(x, z))
        # The Tree (about 25 m on 9 m ground) and the causeway's gate
        cyl.append((PLAZA[0], PLAZA[1], 13.0, 9.0 + 28.0, "The Tree"))
        cyl.append((-459.6, -744.0, 7.0, 1.0 + 14.0, "gate"))
        # The gondola, rebuilt the way WinterIslePuzzle section 11 builds it: pylons every <= 80 m, cable 21 m over
        # the ground at each, raised until the cable clears the ground by 7.5 m
        d = unit(SUMMIT - PLAZA)
        bottom, top = PLAZA + d * 55.0, SUMMIT - d * 12.0
        length = float(np.linalg.norm(top - bottom))
        spans = max(2, int(math.ceil(length / 80.0)))
        line = []
        for i in range(spans + 1):
            p = bottom + (top - bottom) * i / spans
            y = at(*bottom) + 5.6 if i == 0 else at(*top) + 5.6 if i == spans else at(*p) + 21.0
            line.append(np.array([p[0], y, p[1]]))
        for _ in range(20):
            raised = False
            for i in range(spans):
                for t in np.arange(0.05, 1.0, 0.05):
                    c = line[i] + (line[i + 1] - line[i]) * t
                    need = at(c[0], c[2]) + 7.5 - c[1]
                    if need <= 0:
                        continue
                    if i > 0:
                        line[i] = line[i] + np.array([0, need + 0.5, 0])
                    if i + 1 < spans:
                        line[i + 1] = line[i + 1] + np.array([0, need + 0.5, 0])
                    raised = True
            if not raised:
                break
        self.gondola = np.array(line)
        self.gondola_bottom, self.gondola_top, self.gondola_dir = bottom, top, d
        for i in range(spans):
            a, b = line[i] - np.array([0, 4.0, 0]), line[i + 1] - np.array([0, 4.0, 0])      # cabins hang ~4 m under
            segs.append((a, b, "gondola cable"))
        for p in line[1:-1]:
            cyl.append((p[0], p[2], 4.0, p[1] + 1.5, "gondola pylon"))
        for p in (bottom, top):
            cyl.append((p[0], p[1], 12.0, at(*p) + 12.0, "gondola station"))
        # The chairlift: from beside the lodge (SkiLiftBottom) to the bench at the tree line, chairs ~14 m up
        finish = run_centre(1340.0)
        down = unit(run_centre(1346.0) - finish)
        lift_bottom = finish + np.array([down[1], -down[0]]) * 44.0 - down * 30.0
        lift_top = np.array([-1775.0, -1844.0])
        n = 14
        lift = [np.array([p[0], at(*p) + 14.0, p[1]]) for p in (lift_bottom + (lift_top - lift_bottom) * k / n for k in range(n + 1))]
        for i in range(n):
            segs.append((lift[i], lift[i + 1], "chairlift"))
        self.chairlift = np.array(lift)
        # The railway: track on the ground and on trestles (a train is ~5 m tall); tunnels are underground
        self.rail = read_rail()
        for x, y, z, k in self.rail[::4]:
            if k != 1:
                cyl.append((x, z, 6.0, y + 7.0, "railway viaduct" if k == 2 else "railway"))
        # The summit: plinth ring, pavilion and star; the pizzeria terrace (bearing 235, 10-44 m); the airstrip deck on
        # stilts (bearing 308, 34-168 m) with biplanes; the lookouts; the mine tower; the lodge; the airport
        cyl.append((SUMMIT[0], SUMMIT[1], 24.0, 600.5 + 24.0, "summit pavilion"))
        p = SUMMIT + bearing_dir(235) * 27.0
        cyl.append((p[0], p[1], 19.0, 614.0, "pizzeria"))
        for r in np.arange(34.0, 169.0, 8.0):
            p = SUMMIT + bearing_dir(308) * r
            cyl.append((p[0], p[1], 11.0, 606.0, "airstrip deck"))
        for lx, lz in ((-1400, -980), (-1250, -2000), (-2244.5, -1915), (-2750, -2120)):
            cyl.append((lx, lz, 8.0, at(lx, lz) + 22.0, "lookout"))
        mine_file = os.path.join(REPO, "unity", "Land", "WinterIsle.mine")
        if os.path.exists(mine_file):
            mx, mz = [float(v) for v in open(mine_file).read().split()[:2]]
            cyl.append((mx, mz, 14.0, at(mx, mz) + 40.0, "mine tower"))
        cyl.append((-974.0, -1554.0, 26.0, at(-974, -1554) + 16.0, "lodge"))
        cyl.append((-805.0, -1850.0, 40.0, 21.0 + 22.0, "hangar"))
        cyl.append((-822.0, -1913.0, 8.0, 21.0 + 32.0, "control tower"))
        self.sites = read_sites()
        for name, (sx, sz) in self.sites.items():
            cyl.append((sx, sz, 18.0, at(sx, sz) + 14.0, "puzzle " + name))
        self.cyl = cyl
        self.cyl_arr = np.array([[c[0], c[1], c[2], c[3]] for c in cyl])
        self.segs = segs
        self.mine = read_mine()

    def distance(self, P):
        """Each point's distance to the nearest structure, and which."""
        P = np.atleast_2d(P)
        C = self.cyl_arr
        dh = np.maximum(0.0, np.hypot(P[:, None, 0] - C[None, :, 0], P[:, None, 2] - C[None, :, 1]) - C[None, :, 2])
        dv = np.maximum(0.0, P[:, None, 1] - C[None, :, 3])
        dc = np.hypot(dh, dv)
        best = dc.min(axis=1)
        which = [self.cyl[k][4] for k in dc.argmin(axis=1)]
        for a, b, label in self.segs:
            ab = b - a
            t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
            ds = np.linalg.norm(P - (a + t[:, None] * ab), axis=1)
            closer = ds < best
            best = np.where(closer, ds, best)
            which = [label if c else w for c, w in zip(closer, which)]
        return best, which


# ------------------------------------------------------------------------------------------------ keyframes

def keyframes(gr, st):
    """The flight, as camera keys, each with a look target, a speed (v: the mean speed in m/s of the stretch that
    arrives at the key; all are scaled together to fit the trailer's length) and a field of view (vertical, degrees,
    as Unity's Camera.fieldOfView)."""
    K = []

    def key(c, t, v, fov=58.0, label=""):
        K.append(dict(c=np.array(c, float), t=np.array(t, float), v=float(v), fov=float(fov), label=label))

    g = lambda x, z: float(gr.at(x, z))
    hi = lambda x, z, r=30.0: float(gr.highest(x, z, r)[0])
    tree = (PLAZA[0], 24.0, PLAZA[1])

    # 1. Over the sea east of the island, just beyond the fog veil (its outer ribbon runs at x = -170), looking in at
    #    the lowland with the mountain behind; north along the channel, then in over the mainland's shore and down
    #    the causeway to its candy-striped gate
    key((-120, 135, -1075), (-700, 60, -900), 0, 56, "sea")
    key((-240, 110, -930), (-640, 40, -840), 58, 56)
    key((-330, 85, -800), (-560, 20, -770), 64, 56)
    key((-385, 62, -705), (-480, 6, -740), 58, 56)
    key((-440, 46, -695), (-462, 3, -748), 50, 56, "causeway")
    key((-492, 40, -742), (-640, 22, -790), 48, 56, "gate")
    # 2. Round The Tree on its north side, looking at it
    ring = lambda b, r: (PLAZA[0] + r * math.sin(math.radians(b)), PLAZA[1] + r * math.cos(math.radians(b)))
    for b, r, y in ((62, 105, 50), (25, 88, 52), (345, 85, 54), (305, 92, 58)):
        x, z = ring(b, r)
        key((x, y, z), tree, 48, 56, "The Tree" if b == 345 else "")
    # 3. A slow turn away to the gondola, then up the line on its north-west side, looking up it at the cabins ahead
    d, top, bottom = st.gondola_dir, st.gondola_top, st.gondola_bottom
    right = np.array([d[1], -d[0]])                 # right of the way up: north-west of the line (d points south-west)
    L = float(np.linalg.norm(top - bottom))
    cable = st.gondola
    cs = np.linalg.norm(cable[:, [0, 2]] - cable[0, [0, 2]], axis=1)
    cable_y = lambda s: float(np.interp(s, cs, cable[:, 1]))
    p, q = bottom + d * 120 + right * 85, bottom + d * 450
    key((p[0], max(100.0, hi(p[0], p[1], 60.0) + 50.0), p[1]), (q[0], cable_y(450) - 4, q[1]), 40, 58, "gondola")
    ss = list(np.arange(320.0, L - 330.0, 190.0))
    ys = []
    for s in ss:
        p = bottom + d * s + right * 60
        ys.append(max(cable_y(s) + 50.0, hi(p[0], p[1], 60.0) + 50.0))
    ys = np.maximum.accumulate(np.array(ys))       # the line only climbs: so does the camera
    for j, (s, y) in enumerate(zip(ss, ys)):
        p = bottom + d * s + right * 60
        ahead = min(s + 300.0, L - 20.0)
        q = bottom + d * ahead
        key((p[0], y, p[1]), (q[0], cable_y(ahead) - 4.0, q[1]), (75, 105)[j] if j < 2 else 132, 58)
    # 4. Round the summit, anticlockwise seen from above: the airstrip out to the north-west, the pizzeria's terrace
    #    to the south-west, and away to the east
    summit_t = (SUMMIT[0], 606.0, SUMMIT[1])
    for b, r, y, tgt, v in (
            (32, 215, 668, summit_t, 100),
            (2, 175, 672, summit_t, 88),
            (338, 215, 682, (*(SUMMIT + bearing_dir(318) * 60),), 82),
            (306, 255, 684, (*(SUMMIT + bearing_dir(308) * 95),), 80),
            (272, 195, 674, (*(SUMMIT + bearing_dir(290) * 35),), 78),
            (240, 150, 666, (*(SUMMIT + bearing_dir(235) * 26),), 70),
            (208, 150, 664, (*(SUMMIT + bearing_dir(235) * 18),), 72),
            (170, 165, 664, summit_t, 82),
            (132, 185, 660, summit_t, 95)):
        x, z = SUMMIT + bearing_dir(b) * r
        if len(tgt) == 2:
            tgt = (tgt[0], 603.0, tgt[1])
        key((x, y, z), tgt, v, 56, {306: "airstrip", 240: "pizzeria"}.get(b, "summit" if b == 2 else ""))
    # 5. Down the ski runs (beside the middle run, on its right), looking down the run ahead
    for along, agl, v in ((420, 110, 100), (600, 105, 120), (800, 100, 132), (1000, 92, 132), (1180, 85, 122)):
        p = run_centre(along)
        side = unit(run_centre(along + 5) - p)
        p = p + np.array([side[1], -side[0]]) * 35.0
        q = run_centre(min(along + 300.0, 1380.0))
        key((p[0], hi(p[0], p[1], 45) + agl, p[1]), (q[0], g(*q) + 4.0, q[1]), v, 58, "ski runs" if along == 600 else "")
    # 6. Past the lodge and down the runway (north to south), the hangar on the left
    key((-955, 92, -1585), (-880, 22, -1760), 105, 58, "lodge")
    key((-878, 82, -1700), (-865, 21, -1905), 95, 58, "airport")
    key((-882, 80, -1880), (-900, 21, -2060), 95, 58)
    # 7. West over the south shelf and along the viaduct on its seaward side, looking across it at the mountain
    rail = st.rail
    via = rail[rail[:, 3] == 2] if len(rail) else np.zeros((0, 4))
    via = via[(via[:, 0] < -1400) & (via[:, 0] > -2030) & (via[:, 2] < -2340)]
    if len(via) > 10:
        vs = np.concatenate([[0], np.cumsum(np.hypot(np.diff(via[:, 0]), np.diff(via[:, 2])))])
        vat = lambda s: np.array([np.interp(s, vs, via[:, k]) for k in range(3)])
        vlen = float(vs[-1])
    else:
        a, b = np.array([-1417.0, 37.0, -2354.0]), np.array([-2017.0, 57.0, -2586.0])
        vlen = float(np.linalg.norm((b - a)[[0, 2]]))
        vat = lambda s: a + (b - a) * float(np.clip(s / vlen, 0, 1))
    vdir = unit((vat(vlen) - vat(0))[[0, 2]])
    sea_side = np.array([-vdir[1], vdir[0]])         # left of the way west: south, the sea
    for x, z, tgt in ((-905, -2300, (-1560.0, 90.0, -2440.0)), (-1150, -2470, (-1560.0, 60.0, -2430.0))):
        # on south off the runway's end to the coast, then west over the bay, the viaduct ahead
        key((x, max(95.0, hi(x, z, 50) + 60.0), z), tgt, 115, 58)
    for j, s in enumerate((0.0, 0.33 * vlen, 0.66 * vlen, vlen)):
        p = vat(s)
        c = p[[0, 2]] + sea_side * 115.0 - vdir * 40.0
        q = vat(s + 100.0) if s + 100.0 <= vlen else np.array([-2110.0, 110.0, -2470.0])
        key((c[0], max(p[1] + 62.0, hi(c[0], c[1], 40) + 45.0), c[1]), (q[0], q[1] - 4.0, q[2]), 95, 58, "viaduct" if j == 0 else "")
    # 8. Up and back to the south over the sea, looking up at the summit over the mountain's south face, then through
    #    the fog veil to the whole island in its ring of fog
    key((-1990, 270, -2760), (SUMMIT[0], 612.0, SUMMIT[1]), 75, 58, "pull-back")
    key((-1870, 620, -3120), (-1950, 470, -1800), 115, 56)
    key((-1760, 1010, -3360), (-1660, 360, -1560), 95, 55, "whole island")
    return K


# ------------------------------------------------------------------------------------------------ the spline

def catmull_rom(points, samples=240, alpha=0.5):
    """Centripetal Catmull-Rom through 'points' (n x k); returns (dense points, segment index, local t)."""
    P = np.asarray(points, float)
    P = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]])
    out, seg, loc = [], [], []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        t0 = 0.0
        t1 = t0 + max(np.linalg.norm(p1 - p0), 1e-6) ** alpha
        t2 = t1 + max(np.linalg.norm(p2 - p1), 1e-6) ** alpha
        t3 = t2 + max(np.linalg.norm(p3 - p2), 1e-6) ** alpha
        last = i == len(P) - 3
        ts = np.linspace(0.0, 1.0, samples + (1 if last else 0), endpoint=last)
        t = (t1 + ts * (t2 - t1))[:, None]
        A1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1
        A2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2
        A3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3
        B1 = (t2 - t) / (t2 - t0) * A1 + (t - t0) / (t2 - t0) * A2
        B2 = (t3 - t) / (t3 - t1) * A2 + (t - t1) / (t3 - t1) * A3
        C = (t2 - t) / (t2 - t1) * B1 + (t - t1) / (t2 - t1) * B2
        out.append(C)
        seg.append(np.full(len(ts), i - 1))
        loc.append(ts)
    return np.vstack(out), np.concatenate(seg), np.concatenate(loc)


def limit_turns(dirs, max_deg, iterations=80):
    """Spreads any turn of the view faster than max_deg a frame over the frames round it."""
    d = dirs.copy()
    for _ in range(iterations):
        ang = np.degrees(np.arccos(np.clip(np.sum(d[1:] * d[:-1], axis=1), -1, 1)))
        bad = ang > max_deg
        if not bad.any():
            break
        w = np.zeros(len(d))
        w[1:][bad] = 1.0
        w[:-1][bad] = 1.0
        w = np.clip(ndimage.gaussian_filter1d(w, 6.0) * 3.0, 0, 1)[:, None]
        sm = unit(ndimage.gaussian_filter1d(d, 4.0, axis=0, mode="nearest"))
        d = unit(d * (1 - w) + sm * w)
    return d


def build_frames(K, seconds, look_sigma=14.0, max_turn=1.4, start_frac=0.4, end_frac=0.15, accel_sigma=0.9):
    """Frames along the keys. Each key's 'v' is the mean speed of the stretch that arrives at it, which gives every
    key a time (all scaled so the flight lasts 'seconds'); distance along the path against time is a monotone cubic
    through the keys (no stops or reversals, no corners in the speed), starting at start_frac and ending at end_frac
    of the first and last stretches' mean speeds."""
    from scipy.interpolate import CubicHermiteSpline, PchipInterpolator
    cams = np.array([k["c"] for k in K])
    tgts = np.array([k["t"] for k in K])
    C, seg, loc = catmull_rom(cams)
    T, _, _ = catmull_rom(tgts)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(C, axis=0), axis=1))])
    total = s[-1]
    key_s = np.array([s[np.searchsorted(seg, i)] for i in range(len(K) - 1)] + [total])
    stretch = np.diff(key_s)
    key_t = np.concatenate([[0], np.cumsum(stretch / np.array([k["v"] for k in K[1:]]))])
    key_t *= seconds / key_t[-1]
    m = PchipInterpolator(key_t, key_s).derivative()(key_t)
    m[0] = start_frac * stretch[0] / (key_t[1] - key_t[0])
    m[-1] = end_frac * stretch[-1] / (key_t[-1] - key_t[-2])
    curve = CubicHermiteSpline(key_t, key_s, m)
    n = int(round(seconds * FPS))
    ft = np.linspace(0.0, seconds, n)
    fs = np.clip(np.maximum.accumulate(curve(ft)), 0, total)
    # the speed smoothed over time (about 2 s either way), so it never lurches from one stretch's pace to the next;
    # then distance along the path integrated back from it, scaled to end at the path's end on the last frame
    dt = ft[1] - ft[0]
    v = ndimage.gaussian_filter1d(np.gradient(fs, dt), accel_sigma * FPS, mode="nearest")
    v = v * (0.35 + 0.65 * smooth(ft / 3.5)) * (0.12 + 0.88 * smooth((seconds - ft) / 6.5))      # eased in over 3.5 s, out over 6.5 s
    v = np.maximum(v, 0.5)
    fs = np.concatenate([[0.0], np.cumsum((v[1:] + v[:-1]) / 2 * dt)])
    fs *= total / fs[-1]
    speed = np.gradient(fs, dt)
    cam = np.stack([np.interp(fs, s, C[:, k]) for k in range(3)], axis=1)
    tgt = np.stack([np.interp(fs, s, T[:, k]) for k in range(3)], axis=1)
    fov_key = np.array([k["fov"] for k in K])
    w = smooth(loc)
    fov = fov_key[seg] * (1 - w) + fov_key[np.minimum(seg + 1, len(K) - 1)] * w
    fv = ndimage.gaussian_filter1d(np.interp(fs, s, fov), 12.0, mode="nearest")
    # the look direction smoothed over time (not the target point: near the camera a small move of the point turns
    # the view a lot), turns kept under max_turn degrees a frame, and the target put back along it
    look = tgt - cam
    dist = ndimage.gaussian_filter1d(np.linalg.norm(look, axis=1), look_sigma, mode="nearest")
    dirs = unit(ndimage.gaussian_filter1d(unit(look), look_sigma, axis=0, mode="nearest"))
    dirs = limit_turns(dirs, max_turn)
    tgt = cam + dirs * dist[:, None]
    key_t = np.interp(key_s, fs, ft)                  # when the camera passes each key, after the smoothing
    return dict(cam=cam, tgt=tgt, fov=fv, speed=speed, length=total, key_t=key_t, stretch=stretch, dense=C, dt=dt)



def lift_floor(F, gr, floor=30.0, radius=25.0):
    """Raises the camera smoothly wherever the spline sags to within 'floor' of the highest ground near it (trees on
    the island's hills are 15 to 25 m tall), moving the look target with it so the view's direction is kept."""
    cam = F["cam"]
    deficit = np.maximum(0.0, floor - (cam[:, 1] - gr.highest(cam[:, 0], cam[:, 2], radius)))
    if deficit.max() <= 0:
        return 0.0
    lift = ndimage.gaussian_filter1d(ndimage.maximum_filter1d(deficit, 61), 12.0, mode="nearest")
    lift = np.maximum(lift, deficit)
    F["cam"] = cam + np.outer(lift, UP)
    F["tgt"] = F["tgt"] + np.outer(lift, UP)
    return float(lift.max())


# ------------------------------------------------------------------------------------------------ views

def basis(cam, tgt):
    f = unit(tgt - cam)
    r = unit(np.cross(UP, f))
    u = np.cross(f, r)
    return f, r, u


def project(cam, tgt, fov, P):
    """Pixel coordinates (0..1, 0..1 from the top left) of world points, and whether each is in front."""
    f, r, u = basis(cam, tgt)
    d = np.atleast_2d(P) - cam
    z = d @ f
    th = math.tan(math.radians(fov) / 2)
    tw = th * ASPECT
    zs = np.where(np.abs(z) < 1e-6, 1e-6, z)
    x = (d @ r) / zs / tw
    y = (d @ u) / zs / th
    return (x + 1) / 2, (1 - y) / 2, z > 0


def in_view(cams, tgts, fovs, P, margin=1.15, far=600.0):
    """For each frame (rows) and point (columns): whether the point is in the frame (with a margin) and nearer than
    'far' (beyond that the player is a speck of two or three pixels)."""
    vis = np.zeros((len(cams), len(P)), bool)
    for i in range(len(cams)):
        f, r, u = basis(cams[i], tgts[i])
        d = P - cams[i]
        z = d @ f
        th = math.tan(math.radians(fovs[i]) / 2) * margin
        tw = math.tan(math.radians(fovs[i]) / 2) * ASPECT * margin
        ok = z > 0.5
        zs = np.where(ok, z, 1.0)
        vis[i] = ok & (np.abs((d @ r) / zs) < tw) & (np.abs((d @ u) / zs) < th) & (np.linalg.norm(d, axis=1) < far)
    return vis


# ------------------------------------------------------------------------------------------------ teleports

def teleport_candidates(gr, st):
    """Ground points a player can be set down on: land well above the sea, gentle slopes, away from structures,
    puzzle sites, the railway, the mine's open cuts, the airport's runway and apron, the lake and the plaza."""
    g = gr.g
    step = 2
    jj, ii = np.mgrid[1:g.shape[0] - 1:step, 1:g.shape[1] - 1:step]
    h = g[jj, ii]
    nb = np.nan_to_num(np.stack([g[jj + dj, ii + di] for dj in (-1, 0, 1) for di in (-1, 0, 1)]), nan=-99.0)
    ok = np.isfinite(h) & (h >= 2.0) & (nb.min(axis=0) >= 1.2)
    slope = (nb.max(axis=0) - nb.min(axis=0)) / (2 * gr.cell)
    ok &= slope <= 0.45
    x = gr.x0 + ii * gr.cell
    z = gr.z0 + jj * gr.cell
    x, z, h, slope, peak = x[ok], z[ok], h[ok], slope[ok], nb.max(axis=0)[ok]
    keep = np.ones(len(x), bool)
    for cx, cz, r, top, label in st.cyl:
        keep &= np.hypot(x - cx, z - cz) > r + (30.0 if label.startswith("puzzle") else 12.0)
    for cx, cz, r in ((PLAZA[0], PLAZA[1], 48.0), (-822.0, -1012.0, 45.0), (-468.0, -700.0, 30.0)):
        keep &= np.hypot(x - cx, z - cz) > r
    # the airport's runway and apron (WinterIsleAirport.Outside < 15)
    u, v = -(z - (-1855.0)), -(x - (-870.0))
    runway = np.maximum(np.abs(u) - 160.0, np.abs(v) - 22.0)
    apron = np.maximum(np.maximum(-80.0 - u, u - 75.0), np.maximum(-100.0 - v, v - (-20.0)))
    keep &= np.minimum(runway, apron) > 15.0
    # the gondola's corridor, the chairlift's
    for line in (st.gondola, st.chairlift):
        a, b = line[0, [0, 2]], line[-1, [0, 2]]
        ab = b - a
        t = np.clip(((x - a[0]) * ab[0] + (z - a[1]) * ab[1]) / (ab @ ab), 0, 1)
        keep &= np.hypot(x - (a[0] + t * ab[0]), z - (a[1] + t * ab[1])) > 16.0
    # the railway (trains), where it is on or near the surface
    if len(st.rail):
        R = st.rail
        cover = gr.at(R[:, 0], R[:, 2]) - R[:, 1]
        R = R[(R[:, 3] != 1) | (cover < 10.0)]
        keep &= _far_from(x, z, R[:, 0], R[:, 2], 16.0)
    # the mine, where it runs open or under thin cover (the island's mesh leaves the ground out over its cuts)
    if len(st.mine):
        M = st.mine
        cover = gr.at(M[:, 0], M[:, 2]) - M[:, 1]
        M = M[(M[:, 3] == 0) | (cover < 10.0) | (M[:, 4] > 0)]
        keep &= _far_from(x, z, M[:, 0], M[:, 2], 16.0)
    return dict(x=x[keep], z=z[keep], h=h[keep], peak=peak[keep], slope=slope[keep])


def _far_from(x, z, px, pz, r):
    from scipy.spatial import cKDTree
    if len(px) == 0:
        return np.ones(len(x), bool)
    tree = cKDTree(np.stack([px, pz], axis=1))
    d, _ = tree.query(np.stack([x, z], axis=1), k=1)
    return d > r


def choose_teleports(F, cand):
    cams, tgts, fovs = F["cam"], F["tgt"], F["fov"]
    n = len(cams)
    out = {}
    report = []
    for start in range(0, n, BLOCK):
        end = min(n, start + BLOCK)
        bc = cams[start:end]
        centre = bc[:, [0, 2]].mean(axis=0)
        pick = None
        for radius in (500.0, 900.0, 1600.0, 4000.0):
            sel = np.hypot(cand["x"] - centre[0], cand["z"] - centre[1]) < radius
            if not sel.any():
                continue
            P = np.stack([cand["x"][sel], cand["peak"][sel] + 1.0, cand["z"][sel]], axis=1)
            seen = in_view(bc, tgts[start:end], fovs[start:end], P).sum(axis=0)
            dist = np.hypot(P[:, 0][None, :] - bc[:, 0][:, None], P[:, 2][None, :] - bc[:, 2][:, None]).mean(axis=0)
            score = dist + 40.0 * cand["slope"][sel] + np.where(seen > 0, 1e6 + seen * 1e3, 0.0)
            k = int(np.argmin(score))
            if seen[k] == 0 or radius == 4000.0:
                idx = np.nonzero(sel)[0][k]
                pick = (float(cand["x"][idx]), float(cand["peak"][idx] + 2.0), float(cand["z"][idx]), int(seen[k]), float(dist[k]))
                break
        out[start] = pick[:3]
        report.append((start + 1, pick))
    return out, report


# ------------------------------------------------------------------------------------------------ drawing

def font(size):
    for f in ("arial.ttf", "segoeui.ttf", "cour.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def colour_ramp(t):
    """Time 0..1 -> a colour (blue -> green -> yellow -> red)."""
    stops = np.array([[40, 90, 230], [30, 190, 120], [240, 210, 40], [230, 60, 40]], float)
    t = np.clip(t, 0, 1) * (len(stops) - 1)
    i = np.minimum(t.astype(int), len(stops) - 2)
    f = (t - i)[..., None]
    return (stops[i] * (1 - f) + stops[i + 1] * f).astype(np.uint8)


SIGHTS = {
    "causeway": (-463.0, -723.0), "gate": (-459.6, -744.0), "The Tree": (-705.0, -795.0), "lake": (-822.0, -1012.0),
    "gondola foot": (-750.0, -827.0), "summit": (-2250.0, -1900.0), "pizzeria": tuple(SUMMIT + bearing_dir(235) * 27),
    "airstrip": tuple(SUMMIT + bearing_dir(308) * 100), "ski runs": tuple(run_centre(800)), "lodge": (-974.0, -1554.0),
    "chairlift top": (-1775.0, -1844.0), "viaduct": (-1717.0, -2470.0), "Plaza Halt": (-1045.0, -1064.0),
    "airport": (-870.0, -1855.0), "mine": (-1088.0, -1242.0),
}


def draw_map(gr, st, F, teleports, path_png):
    X0, X1, Z0, Z1, mpp = -3300.0, 250.0, -3700.0, 50.0, 3.0
    W, H = int((X1 - X0) / mpp), int((Z1 - Z0) / mpp)
    xs = X0 + (np.arange(W) + 0.5) * mpp
    zs = Z1 - (np.arange(H) + 0.5) * mpp
    XX, ZZ = np.meshgrid(xs, zs)
    isl = gr.island(XX, ZZ)
    mnl = gr.mainland(XX, ZZ)
    hgt = gr.at(XX, ZZ)
    gy, gx = np.gradient(ndimage.gaussian_filter(hgt, 1.0), mpp)
    shade = np.clip(0.85 + (-gx * 0.6 - gy * 0.8) * 0.5, 0.5, 1.25)          # lit from the north-west (gy: rows go south)
    img = np.zeros((H, W, 3))
    img[:] = [0.10, 0.25, 0.42]
    on_isl = np.isfinite(isl) & (isl > 0.15)
    on_main = ~on_isl & np.isfinite(mnl) & (mnl > 0.3)
    slope = np.hypot(gx, gy)
    snow = np.array([0.93, 0.95, 0.98])
    rock = np.array([0.45, 0.46, 0.50])
    rocky = np.clip((slope - 0.75) / 0.4, 0, 1)[..., None]
    c = (snow * (1 - rocky) + rock * rocky) * shade[..., None]
    img[on_isl] = c[on_isl]
    mainc = np.array([0.55, 0.62, 0.42]) * shade[..., None]
    img[on_main] = mainc[on_main]
    shallow = ~on_isl & ~on_main & np.isfinite(isl) & (isl > -3)
    img[shallow] = [0.22, 0.42, 0.56]
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
    over = Image.new("RGBA", im.size, (0, 0, 0, 0))
    dr = ImageDraw.Draw(over)
    P = lambda x, z: ((x - X0) / mpp, (Z1 - z) / mpp)
    f14, f18, f24 = font(14), font(18), font(26)
    for gxm in range(-3250, 251, 250):
        dr.line([P(gxm, Z0), P(gxm, Z1)], fill=(255, 255, 255, 40), width=1)
        dr.text(P(gxm + 4, Z1 - 4), str(gxm), fill=(255, 255, 255, 140), font=f14)
    for gzm in range(-3500, 51, 250):
        dr.line([P(X0, gzm), P(X1, gzm)], fill=(255, 255, 255, 40), width=1)
        dr.text(P(X0 + 4, gzm - 2), str(gzm), fill=(255, 255, 255, 140), font=f14)
    # the fog veil's three ribbons (a rounded rectangle round the island's box, 40, 110 and 190 m out)
    for out_by in (40.0, 110.0, 190.0):
        bx0, bx1, bz0, bz1, r = -3120 - out_by, -360 + out_by, -2700 - out_by, -501 + out_by, 260.0
        pts = []
        for cx, cz, a0 in ((bx1 - r, bz1 - r, 0), (bx0 + r, bz1 - r, 90), (bx0 + r, bz0 + r, 180), (bx1 - r, bz0 + r, 270)):
            pts += [P(cx + r * math.cos(math.radians(a0 + 9 * k)), cz + r * math.sin(math.radians(a0 + 9 * k))) for k in range(11)]
        dr.line(pts + [pts[0]], fill=(220, 220, 235, 90), width=2)
    # the gondola, the chairlift, the railway, the runway
    dr.line([P(p[0], p[2]) for p in st.gondola], fill=(200, 30, 30, 255), width=3)
    dr.line([P(p[0], p[2]) for p in st.chairlift], fill=(240, 120, 30, 255), width=2)
    if len(st.rail):
        R = st.rail
        for i in range(len(R) - 1):
            if R[i, 3] == 1 and (i // 3) % 2:
                continue
            dr.line([P(R[i, 0], R[i, 2]), P(R[i + 1, 0], R[i + 1, 2])], fill=(120, 20, 30, 255) if R[i, 3] != 1 else (120, 20, 30, 150), width=4 if R[i, 3] == 2 else 2)
    rw = [P(-892, -1695), P(-848, -1695), P(-848, -2015), P(-892, -2015)]
    dr.polygon(rw, fill=(70, 70, 80, 255))
    # the flight, coloured by time; a look line every 2 s; the keys; the teleports
    cam, tgt = F["cam"], F["tgt"]
    n = len(cam)
    cols = colour_ramp(np.arange(n) / max(1, n - 1))
    for i in range(n - 1):
        dr.line([P(cam[i, 0], cam[i, 2]), P(cam[i + 1, 0], cam[i + 1, 2])], fill=tuple(int(c) for c in cols[i]) + (255,), width=4)
    for i in range(0, n, FPS * 2):
        a = cam[i]
        dvec = unit((tgt[i] - a)[[0, 2]]) * 120.0
        dr.line([P(a[0], a[2]), P(a[0] + dvec[0], a[2] + dvec[1])], fill=(255, 255, 255, 170), width=1)
        dr.ellipse([P(a[0], a[2])[0] - 4, P(a[0], a[2])[1] - 4, P(a[0], a[2])[0] + 4, P(a[0], a[2])[1] + 4], fill=(255, 255, 255, 255))
        dr.text((P(a[0], a[2])[0] + 6, P(a[0], a[2])[1] - 8), "%ds" % (i // FPS), fill=(255, 255, 255, 230), font=f14)
    for start, tp in teleports.items():
        if tp is None:
            continue
        px, pz = P(tp[0], tp[2])
        mid = cam[min(n - 1, start + BLOCK // 2)]
        dr.line([P(mid[0], mid[2]), (px, pz)], fill=(255, 170, 40, 160), width=1)
        dr.rectangle([px - 5, pz - 5, px + 5, pz + 5], fill=(255, 150, 20, 255), outline=(0, 0, 0, 255))
    for name, (sx, sz) in SIGHTS.items():
        px, pz = P(sx, sz)
        dr.ellipse([px - 6, pz - 6, px + 6, pz + 6], fill=(255, 255, 255, 255), outline=(20, 20, 20, 255), width=2)
        dr.text((px + 8, pz + 2), name, fill=(255, 255, 255, 255), font=f18, stroke_width=2, stroke_fill=(0, 0, 0, 255))
    dr.text((20, H - 120), "Big Christmas Walk trailer: camera path (colour = time, blue -> red), white ticks every 2 s with the look direction,", fill=(255, 255, 255, 255), font=f18)
    dr.text((20, H - 94), "orange squares = player teleports (one per 90 frames), grey rings = fog veil, red = gondola, orange = chairlift, dark red = railway", fill=(255, 255, 255, 255), font=f18)
    dr.text((20, H - 64), "%.1f s, %d frames, %.0f m flown" % (n / FPS, n, F["length"]), fill=(255, 255, 255, 255), font=f24)
    im = Image.alpha_composite(im, over).convert("RGB")
    im.save(path_png)


def draw_profile(F, clear, sdist, swhich, sights_t, png):
    cam = F["cam"]
    n = len(cam)
    t = np.arange(n) / FPS
    W, H, L, Rm = 1800, 900, 80, 30
    im = Image.new("RGB", (W, H), (250, 250, 252))
    dr = ImageDraw.Draw(im)
    f14, f16, f20 = font(14), font(16), font(20)
    T = t[-1] if n > 1 else 1.0
    X = lambda s: L + (W - L - Rm) * s / T
    # top panel: heights (0..ymax m)
    top0, top1 = 40, 470
    ymax = max(700.0, float(np.max(cam[:, 1])) * 1.05)
    Y1 = lambda y: top1 - (top1 - top0) * y / ymax
    terr = cam[:, 1] - clear
    for y in range(0, int(ymax) + 1, 100):
        dr.line([(L, Y1(y)), (W - Rm, Y1(y))], fill=(225, 225, 230))
        dr.text((8, Y1(y) - 8), "%d m" % y, fill=(80, 80, 90), font=f14)
    dr.text((L, 12), "height: camera (blue), highest ground within 8 m below it (brown), summit floor %d m inside %d m of the summit (red, shaded)" % (SUMMIT_MIN_Y, SUMMIT_ZONE), fill=(30, 30, 40), font=f16)
    zone = np.hypot(cam[:, 0] - SUMMIT[0], cam[:, 2] - SUMMIT[1]) < SUMMIT_ZONE
    for i in range(n):
        if zone[i]:
            dr.line([(X(t[i]), top0), (X(t[i]), top1)], fill=(255, 228, 228))
            dr.point((X(t[i]), Y1(SUMMIT_MIN_Y)), fill=(220, 30, 30))
    dr.line([(X(t[i]), Y1(terr[i])) for i in range(n)], fill=(140, 90, 40), width=2)
    dr.line([(X(t[i]), Y1(cam[i, 1])) for i in range(n)], fill=(30, 80, 220), width=2)
    # bottom panel: clearances (0..250 m)
    b0, b1, cmax = 520, 850, 250.0
    Y2 = lambda y: b1 - (b1 - b0) * min(y, cmax) / cmax
    for y in (0, 15, 50, 100, 150, 200, 250):
        dr.line([(L, Y2(y)), (W - Rm, Y2(y))], fill=(225, 225, 230) if y != 15 else (230, 60, 60), width=1 if y != 15 else 2)
        dr.text((8, Y2(y) - 8), "%d m" % y, fill=(80, 80, 90) if y != 15 else (200, 30, 30), font=f14)
    dr.text((L, b0 - 26), "clearance: above the ground (green, min %.1f m), to the nearest structure (purple, min %.1f m); red line = 15 m" % (clear.min(), sdist.min()), fill=(30, 30, 40), font=f16)
    dr.line([(X(t[i]), Y2(sdist[i])) for i in range(n)], fill=(150, 60, 190), width=2)
    dr.line([(X(t[i]), Y2(clear[i])) for i in range(n)], fill=(30, 150, 70), width=2)
    for s in range(0, int(T) + 1, 5):
        dr.line([(X(s), b1), (X(s), b1 + 6)], fill=(60, 60, 70))
        dr.text((X(s) - 8, b1 + 8), "%ds" % s, fill=(60, 60, 70), font=f14)
    for k, (name, st_) in enumerate(sights_t):
        dr.line([(X(st_), top0), (X(st_), b1)], fill=(170, 170, 180))
        dr.text((X(st_) + 3, top0 + 4 + 16 * (k % 3)), name, fill=(60, 60, 80), font=f14)
    im.save(png)


def render_view(gr, cam, tgt, fov, W=256, H=144):
    """A rough picture of what the frame sees of the height field (no trees, buildings or sky effects): sea, snow,
    rock and the base game's land, lit from the south-west."""
    f, r, u = basis(cam, tgt)
    th = math.tan(math.radians(fov) / 2)
    tw = th * ASPECT
    px = ((np.arange(W) + 0.5) / W * 2 - 1) * tw
    py = (1 - (np.arange(H) + 0.5) / H * 2) * th
    PX, PY = np.meshgrid(px, py)
    D = unit(f[None, None, :] + PX[..., None] * r[None, None, :] + PY[..., None] * u[None, None, :]).reshape(-1, 3)
    nrays = len(D)
    thit = np.full(nrays, np.inf)
    alive = np.ones(nrays, bool)
    t, prev = 1.0, 0.0
    while t < 7500.0 and alive.any():
        step = max(2.0, t * 0.01)
        prev, t = t, t + step
        idx = np.nonzero(alive)[0]
        P = cam + D[idx] * t
        hit = P[:, 1] <= gr.at(P[:, 0], P[:, 2])
        if hit.any():
            hi_ = idx[hit]
            lo, hi2 = np.full(len(hi_), prev), np.full(len(hi_), t)
            for _ in range(5):
                mid = (lo + hi2) / 2
                Q = cam + D[hi_] * mid[:, None]
                below = Q[:, 1] <= gr.at(Q[:, 0], Q[:, 2])
                hi2 = np.where(below, mid, hi2)
                lo = np.where(below, lo, mid)
            thit[hi_] = hi2
            alive[hi_] = False
    img = np.zeros((nrays, 3))
    sky = np.clip(D[:, 1], -0.2, 1)[:, None]
    img[:] = np.array([0.80, 0.86, 0.94]) * (1 - sky) + np.array([0.42, 0.58, 0.85]) * sky
    hitm = np.isfinite(thit)
    Q = cam + D[hitm] * thit[hitm][:, None]
    x, y, z = Q[:, 0], Q[:, 1], Q[:, 2]
    e = 2.5
    nx_ = gr.at(x - e, z) - gr.at(x + e, z)
    nz_ = gr.at(x, z - e) - gr.at(x, z + e)
    nrm = unit(np.stack([nx_, np.full(len(x), 2 * e), nz_], axis=1))
    sun = unit(np.array([-0.6, 0.45, -0.6]))
    lam = np.clip(nrm @ sun, 0, 1) * 0.65 + 0.42
    isl = gr.island(x, z)
    on_isl = np.isfinite(isl) & (isl > 0.15)
    sea = y < 0.3
    slope = np.hypot(nx_, nz_) / (2 * e)
    rockf = np.clip((slope - 0.8) / 0.5, 0, 1)[:, None]
    col = (np.array([0.95, 0.96, 0.99]) * (1 - rockf) + np.array([0.46, 0.46, 0.5]) * rockf)
    col = np.where(on_isl[:, None], col, np.array([0.62, 0.62, 0.45]))
    col = np.where(sea[:, None], np.array([0.16, 0.30, 0.48]), col * lam[:, None])
    fog = (1 - np.exp(-thit[hitm] / 5000.0))[:, None]
    img[hitm] = col * (1 - fog) + np.array([0.80, 0.86, 0.94]) * fog
    return Image.fromarray((np.clip(img.reshape(H, W, 3), 0, 1) * 255).astype(np.uint8))


def draw_views(gr, st, F, teleports, png, pick):
    cam, tgt, fov = F["cam"], F["tgt"], F["fov"]
    n = len(cam)
    W, H = 384, 216
    sheet = Image.new("RGB", (W * 4 + 50, (H + 26) * ((len(pick) + 3) // 4) + 10), (24, 26, 32))
    f12, f14 = font(12), font(14)
    sight3 = {k: np.array([v[0], float(gr.at(*v)) + 3.0, v[1]]) for k, v in SIGHTS.items()}
    for k, i in enumerate(pick):
        im = render_view(gr, cam[i], tgt[i], fov[i], 256, 144).resize((W, H), Image.BILINEAR)
        dr = ImageDraw.Draw(im)

        def line3(points, colour, width=2):
            u_, v_, front = project(cam[i], tgt[i], fov[i], np.asarray(points))
            for a in range(len(points) - 1):
                if front[a] and front[a + 1] and -1 < u_[a] < 2 and -1 < v_[a] < 2:
                    dr.line([(u_[a] * W, v_[a] * H), (u_[a + 1] * W, v_[a + 1] * H)], fill=colour, width=width)
        line3(st.gondola, (220, 30, 30))
        if len(st.rail):
            R = st.rail[st.rail[:, 3] == 2]
            for j in range(len(R) - 1):
                if np.hypot(*(R[j + 1, [0, 2]] - R[j, [0, 2]])) < 20:
                    line3(R[j:j + 2, :3], (150, 30, 40), 2)
        for name, p in sight3.items():
            u_, v_, front = project(cam[i], tgt[i], fov[i], p[None, :])
            if front[0] and 0 <= u_[0] <= 1 and 0 <= v_[0] <= 1:
                dr.ellipse([u_[0] * W - 3, v_[0] * H - 3, u_[0] * W + 3, v_[0] * H + 3], fill=(255, 220, 0))
                dr.text((u_[0] * W + 5, v_[0] * H - 7), name, fill=(255, 255, 255), font=f12, stroke_width=1, stroke_fill=(0, 0, 0))
        u_, v_, front = project(cam[i], tgt[i], fov[i], tgt[i][None, :])
        if front[0]:
            cx, cy = u_[0] * W, v_[0] * H
            dr.line([(cx - 6, cy), (cx + 6, cy)], fill=(255, 40, 40), width=2)
            dr.line([(cx, cy - 6), (cx, cy + 6)], fill=(255, 40, 40), width=2)
        blk = (i // BLOCK) * BLOCK
        tp = teleports.get(blk)
        if tp is not None:
            u_, v_, front = project(cam[i], tgt[i], fov[i], np.array([tp])[:, :3])
            if front[0] and 0 <= u_[0] <= 1 and 0 <= v_[0] <= 1:
                dr.rectangle([u_[0] * W - 4, v_[0] * H - 4, u_[0] * W + 4, v_[0] * H + 4], outline=(255, 140, 0), width=2)
        x0 = 10 + (k % 4) * (W + 10)
        y0 = 10 + (k // 4) * (H + 26)
        sheet.paste(im, (x0, y0))
        ImageDraw.Draw(sheet).text((x0, y0 + H + 4), "frame %d  (%.1f s)  fov %.0f  cam %.0f, %.0f, %.0f" % (i + 1, i / FPS, fov[i], *cam[i]), fill=(220, 220, 230), font=f14)
    sheet.save(png, quality=88)


# ------------------------------------------------------------------------------------------------ main

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seconds", type=float, default=75.0, help="length of the trailer (60..75)")
    ap.add_argument("--csv", default=os.path.join(HERE, "frames.csv"))
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--no-views", action="store_true", help="skip the software renders (views.jpg)")
    ap.add_argument("--view-frames", default="", help="comma-separated frame numbers to render in views.jpg instead of the keys")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    gr = Ground()
    st = Structures(gr)
    K = keyframes(gr, st)
    F = build_frames(K, a.seconds)
    lifted = lift_floor(F, gr)
    cam, tgt, fov = F["cam"], F["tgt"], F["fov"]
    n = len(cam)

    # --- checks
    ground = gr.highest(cam[:, 0], cam[:, 2], 8.0)
    clear = cam[:, 1] - ground
    sdist, swhich = st.distance(cam)
    zone = np.hypot(cam[:, 0] - SUMMIT[0], cam[:, 2] - SUMMIT[1]) < SUMMIT_ZONE
    look = unit(tgt - cam)
    turn = np.degrees(np.arccos(np.clip(np.sum(look[1:] * look[:-1], axis=1), -1, 1)))
    pitch = np.degrees(np.arcsin(look[:, 1]))
    # line of sight to the look target (does the terrain hide it?)
    hidden = np.zeros(n, bool)
    for i in range(n):
        d = tgt[i] - cam[i]
        ts = np.linspace(0.03, 0.9, 60)
        P = cam[i] + ts[:, None] * d
        hidden[i] = bool(np.any(P[:, 1] < gr.at(P[:, 0], P[:, 2]) - 2.0))

    cand = teleport_candidates(gr, st)
    teleports, treport = choose_teleports(F, cand)

    # --- frames.csv
    with open(a.csv, "w", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["frame", "cx", "cy", "cz", "tx", "ty", "tz", "fov", "teleport_x", "teleport_y", "teleport_z"])
        for i in range(n):
            tp = teleports.get(i)
            row = [i + 1] + ["%.2f" % v for v in cam[i]] + ["%.2f" % v for v in tgt[i]] + ["%.2f" % fov[i]]
            row += ["%.1f" % v for v in tp] if tp is not None else ["", "", ""]
            w.writerow(row)

    # --- the order of the sights: when the camera passes nearest each
    sights_t = []
    for name, (sx, sz) in SIGHTS.items():
        dd = np.hypot(cam[:, 0] - sx, cam[:, 2] - sz)
        i = int(np.argmin(dd))
        sights_t.append((name, i / FPS, float(dd[i])))
    sights_t.sort(key=lambda q: q[1])
    key_marks = [(k["label"], F["key_t"][j]) for j, k in enumerate(K) if k["label"]]

    draw_map(gr, st, F, teleports, os.path.join(a.out, "path.png"))
    draw_profile(F, clear, sdist, swhich, key_marks, os.path.join(a.out, "profile.png"))
    if not a.no_views:
        # the frame at every labelled key, the first and last, and between them where the gaps are long
        pick = sorted({0, n - 1} | {min(n - 1, int(round(kt * FPS))) for lab, kt in key_marks})
        if a.view_frames:
            pick = sorted({min(n - 1, max(0, int(v) - 1)) for v in a.view_frames.split(",") if v.strip()})
        while not a.view_frames and len(pick) < 24:
            gaps = np.diff(pick)
            g = int(np.argmax(gaps))
            pick.insert(g + 1, pick[g] + gaps[g] // 2)
        draw_views(gr, st, F, teleports, os.path.join(a.out, "views-picked.jpg" if a.view_frames else "views.jpg"), pick)

    # --- report
    i_c = int(np.argmin(clear))
    i_s = int(np.argmin(sdist))
    print("frames: %d at %d fps = %.2f s; flight %.0f m; speed %.0f..%.0f m/s (mean %.0f)" % (n, FPS, n / FPS, F["length"], F["speed"].min(), F["speed"].max(), F["speed"].mean()))
    print("design floor: the camera was lifted by up to %.1f m to stay 30 m over the ground within 25 m" % lifted)
    acc = np.gradient(F["speed"], F["dt"])
    mid_ = slice(4 * FPS, n - 7 * FPS)
    print("acceleration between the ease-in and the ease-out: max %.1f m/s2 at %.1f s" % (np.abs(acc[mid_]).max(), (4 * FPS + np.argmax(np.abs(acc[mid_]))) / FPS))
    print("acceleration: max %.1f m/s2 (at %.1f s), 99th pct %.1f m/s2; speed at the start %.0f m/s, at the end %.0f m/s" % (np.abs(acc).max(), np.argmax(np.abs(acc)) / FPS, np.percentile(np.abs(acc), 99), F["speed"][0], F["speed"][-1]))
    print("terrain clearance: min %.1f m at frame %d (%.1f s, camera %.0f, %.0f, %.0f); frames under %.0f m: %d" % (clear[i_c], i_c + 1, i_c / FPS, *cam[i_c], MIN_CLEAR, int(np.sum(clear < MIN_CLEAR))))
    if zone.any():
        print("summit zone (%d m): lowest camera %.1f m (floor %.0f); frames below: %d" % (SUMMIT_ZONE, cam[zone, 1].min(), SUMMIT_MIN_Y, int(np.sum(cam[zone, 1] < SUMMIT_MIN_Y))))
    print("structures: nearest %.1f m (%s) at frame %d (%.1f s); frames nearer than 15 m: %d" % (sdist[i_s], swhich[i_s], i_s + 1, i_s / FPS, int(np.sum(sdist < 15.0))))
    print("turn rate: max %.2f deg/frame at frame %d (%.1f s), 99th pct %.2f; pitch %.0f..%.0f deg" % (turn.max(), int(np.argmax(turn)) + 2, (np.argmax(turn) + 1) / FPS, np.percentile(turn, 99), pitch.min(), pitch.max()))
    runs, i = [], 0
    while i < n:
        if hidden[i]:
            j = i
            while j + 1 < n and hidden[j + 1]:
                j += 1
            runs.append("%.1f-%.1f s" % (i / FPS, j / FPS))
            i = j + 1
        else:
            i += 1
    print("look target hidden by terrain: %d frames %s" % (int(hidden.sum()), ", ".join(runs)))
    kt = F["key_t"]
    print("stretches (key -> key: seconds, mean m/s): " + ", ".join("%d %.1fs %.0f" % (j + 1, kt[j + 1] - kt[j], st_len / (kt[j + 1] - kt[j])) for j, st_len in enumerate(F["stretch"])))
    print("teleports: %d" % len(teleports))
    for frame, p in treport:
        print("  frame %5d -> %.1f, %.1f, %.1f   seen in %d frames of its block, %.0f m from the camera on average" % (frame, p[0], p[1], p[2], p[3], p[4]))
    print("keys (time, label):")
    for j, k in enumerate(K):
        print("  %2d %5.1f s  %s" % (j, F["key_t"][j], k["label"]))
    print("sights by time of nearest pass:")
    for name, ts_, dd in sights_t:
        print("  %5.1f s  %-14s %.0f m" % (ts_, name, dd))
    ok = clear.min() >= MIN_CLEAR and (not zone.any() or cam[zone, 1].min() >= SUMMIT_MIN_Y)
    print("CHECK", "OK" if ok else "FAILED")
    print("wrote", a.csv, "and", a.out)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
