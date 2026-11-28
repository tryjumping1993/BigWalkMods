"""The Winter Isle railway as a closed loop round the island, plus the summit spur (variant "spline").

The loop runs clockwise: Plaza -> East Lowland -> Airport -> (the existing line from South Shore, index 336, by Seal
Point and West Cliffs to Cirque Gate, index 999, kept exactly) -> North Shore -> Northwest -> back to the Plaza.
The two new legs are one smooth curve from existing point 999 to existing point 336, described by a few nodes
(position + heading; a station is a node pair 120 m apart joined by a straight) and joined by cubic Hermite pieces
whose tangent lengths follow the circle through the two headings. The vertical profile is found by dynamic
programming over (distance, height) inside the 5% grade limit (level over every station), then smoothed.
The summit spur is the existing line from just past the loop's Cirque Gate to the summit, unchanged.

  python route.py                 builds from the stored nodes, writes the .railnet, plan.png, profile.png, metrics.json
  python route.py --anneal 6000   first improves the nodes by simulated annealing (and prints them)
Output: Z:/Temp/claude/BigWalkMods/railnet/spline/  (run through Z:/Temp/claude/BigWalkMods/py.cmd)
"""
import argparse, json, math, os, struct, sys, time
import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = "Z:/Temp/claude/BigWalkMods/railnet/spline"

# ---------------------------------------------------------------- terrain
_d = open(os.path.join(REPO, "unity", "Land", "WinterIsle.land"), "rb").read()
X0, Z0, CELL, NX, NZ = struct.unpack("<fffii", _d[:20])
GRID = np.frombuffer(_d, dtype="<f4", count=(NX + 1) * (NZ + 1), offset=20).reshape((NZ + 1, NX + 1)).astype(np.float64)
GRIDF = np.nan_to_num(GRID, nan=-6.0)


def ground(x, z):
    x = np.atleast_1d(np.asarray(x, float)); z = np.atleast_1d(np.asarray(z, float))
    return ndimage.map_coordinates(GRIDF, [(z - Z0) / CELL, (x - X0) / CELL], order=1, mode="nearest")


# ---------------------------------------------------------------- the existing line
RAIL = []
for _l in open(os.path.join(REPO, "unity", "Land", "WinterIsle.rail")):
    _q = _l.split()
    if _q and _q[0] == "P": RAIL.append((float(_q[1]), float(_q[2]), float(_q[3])))
RAIL = np.array(RAIL)      # x, y, z
I_SS, I_CG, I_END = 336, 999, len(RAIL) - 1


def _dir(i):
    v = RAIL[i + 1] - RAIL[i - 1]; h = math.hypot(v[0], v[2])
    return v[0] / h, v[2] / h, v[1] / h


HX_CG, HZ_CG, GR_CG = _dir(I_CG)       # heading and grade leaving Cirque Gate (the loop's leg B starts here)
HX_SS, HZ_SS, GR_SS = _dir(I_SS)       # heading and grade arriving at South Shore (leg A ends here)
TH_CG = math.degrees(math.atan2(HZ_CG, HX_CG)); TH_SS = math.degrees(math.atan2(HZ_SS, HX_SS))

# ---------------------------------------------------------------- the island's furniture
RADII = {"AdventTrail": 10.6, "BaubleHunt": 21.2, "BellRingers": 21.2, "BigAir": 7.1, "BlindCarols": 14.9,
         "BlindSculptor": 18.4, "BlizzardWalk": 22, "Downhill": 9.9, "FairyLights": 25.5, "Footprints": 21.5,
         "GlassBooth": 13.9, "HoldTheStar": 25.5, "LightTheTree": 16.3, "Lookouts": 17, "MidnightFinale": 19.8,
         "RadioSilence": 14.2, "ReindeerCount": 26.2, "SantasList": 17.7, "Slalom": 7.1, "SleighRun": 25.6,
         "SnowballRange": 22.2, "SnowBridge": 20.2, "SnowGlobe": 17, "Snowman": 11.4, "StockingSort": 16.3,
         "SummitShot": 15, "TheTree": 33.9, "ThinIce": 19.8, "WhisperingChimney": 14.9, "WhiteoutMaze": 21.9}
SITES = {}
for _n in RADII:
    _s = json.load(open(os.path.join(REPO, "unity", "Assets", "Puzzles", _n, "site.json")))
    SITES[_n] = (float(_s["x"]), float(_s["z"]), RADII[_n])
PLAZA = (-705.0, -795.0)
# circles the track keeps out of: (x, z, clearance, label, is puzzle)
KEEP = [(x, z, r + 35.0, n, True) for n, (x, z, r) in SITES.items()]
LOOKOUTS = [(-1400.0, -980.0), (-1250.0, -2000.0), (-2750.0, -2120.0)]
KEEP += [(x, z, 37.0, "lookout", False) for x, z in LOOKOUTS]
KEEP += [(-858.0, -1001.0, 20.0, "far board", False), (PLAZA[0], PLAZA[1], 54.0, "plaza", False),
         (-822.0, -1012.0, 47.0, "frozen lake", False)]
GONDOLA_BOTTOM = (-750.0, -827.0); SUMMIT = (-2250.0, -1900.0)
_gd = np.array(SUMMIT) - np.array(GONDOLA_BOTTOM); _gl = float(np.hypot(*_gd)); _gu = _gd / _gl
PYLONS = [tuple(np.array(GONDOLA_BOTTOM) + _gu * 79.74 * k) for k in range(1, int(_gl // 79.74) + 1)]
KEEP += [(x, z, 15.0, "pylon", False) for x, z in PYLONS] + [(GONDOLA_BOTTOM[0], GONDOLA_BOTTOM[1], 35.0, "gondola station", False)]
LIFT_A, LIFT_B = (-974.0, -1554.0), (-1775.0, -1844.0)
KEEP += [(LIFT_A[0], LIFT_A[1], 20.0, "lift station", False)]
KC = np.array([(k[0], k[1]) for k in KEEP]); KR = np.array([k[2] for k in KEEP])
PUZ = np.array([(x, z) for x, z, r in SITES.values()]); PUZR = np.array([r for x, z, r in SITES.values()]) + 40.0
RUNWAY = (-892.0, -848.0, -2015.0, -1695.0); APRON = (-850.0, -770.0, -1930.0, -1775.0)      # x0, x1, z0, z1
SKI = np.array([(-2250, -1900), (-1900, -1150), (-1250, -1250), (-1000, -1500), (-1050, -1900), (-1700, -2150)], float)


def rect_dist(x, z, r):
    dx = np.maximum(np.maximum(r[0] - x, x - r[1]), 0); dz = np.maximum(np.maximum(r[2] - z, z - r[3]), 0)
    return np.hypot(dx, dz)


def in_poly(x, z, poly):
    ins = np.zeros(len(x), bool); n = len(poly)
    for i in range(n):
        ax, az = poly[i]; bx, bz = poly[(i + 1) % n]
        c = ((az > z) != (bz > z)) & (x < (bx - ax) * (z - az) / (bz - az + 1e-12) + ax)
        ins ^= c
    return ins


def seg_dist(x, z, a, b):
    ax, az = a; bx, bz = b; vx, vz = bx - ax, bz - az
    t = np.clip(((x - ax) * vx + (z - az) * vz) / (vx * vx + vz * vz), 0, 1)
    return np.hypot(x - ax - t * vx, z - az - t * vz)


# ---------------------------------------------------------------- stations
# name: (served puzzle points, weight on mean distance to them)
STATIONS = {
    "Plaza": ([PLAZA], 2.0),
    "East Lowland": ([SITES["Lookouts"][:2], SITES["SnowGlobe"][:2], SITES["WhiteoutMaze"][:2]], 1.0),
    "Airport": ([SITES["BlindSculptor"][:2], SITES["FairyLights"][:2], SITES["ThinIce"][:2]], 1.0),
    "North Shore": ([SITES["ReindeerCount"][:2], SITES["Footprints"][:2]], 1.0),
    "Northwest": ([SITES["SleighRun"][:2], SITES["BlizzardWalk"][:2], LOOKOUTS[0]], 1.0),
    "North Bay": ([], 0.0),
}
ST_LEN = 120.0      # straight per station (the platform needs 100 m)
FLAT = 150.0        # level stretch per station before smoothing (100 m platform + the smoothing window + margin)
SMOOTH = 40.0       # smoothing window of the vertical profile (m)
TUNNEL_M = 7.0      # cost of a metre of tunnel, in metres of extra length
MARGIN = 1.0        # the optimiser keeps this much clear of every keep-out on top of the rule

# ---------------------------------------------------------------- the stored route: the new section from 999 to 336
# ("fix", x, z, heading) ; ("free", x, z, heading) ; ("st", name, x, z, heading, target x, target z)
NODES = None      # filled below (see SEED)


def expand(nodes):
    """Node list -> Hermite nodes [(x, z, hx, hz)] and station segments (segment index, name)."""
    out, sts = [], []
    for n in nodes:
        if n[0] in ("fix", "free"):
            t = math.radians(n[3]); out.append((n[1], n[2], math.cos(t), math.sin(t)))
        else:
            t = math.radians(n[4]); c, s = math.cos(t), math.sin(t)
            out.append((n[2] - c * ST_LEN / 2, n[3] - s * ST_LEN / 2, c, s)); sts.append((len(out) - 1, n[1]))
            out.append((n[2] + c * ST_LEN / 2, n[3] + s * ST_LEN / 2, c, s))
    return out, sts


def hermite(nodes, step=2.0):
    """Dense samples of the curve: x, z, cumulative length, signed curvature, segment index."""
    X, Zs, K, SEG = [], [], [], []
    for i in range(len(nodes) - 1):
        x0, z0, a0, b0 = nodes[i]; x1, z1, a1, b1 = nodes[i + 1]
        ch = math.hypot(x1 - x0, z1 - z0)
        phi = math.acos(max(-1.0, min(1.0, a0 * a1 + b0 * b1)))
        k = 1.0 if phi < 1e-3 else 2 * math.tan(phi / 4) / math.sin(phi / 2)
        m = k * ch
        n = max(8, int(ch / step) + 1)
        t = np.linspace(0, 1, n + 1)[:-1] if i < len(nodes) - 2 else np.linspace(0, 1, n + 1)
        h00 = 2 * t**3 - 3 * t**2 + 1; h10 = t**3 - 2 * t**2 + t; h01 = -2 * t**3 + 3 * t**2; h11 = t**3 - t**2
        d00 = 6 * t**2 - 6 * t; d10 = 3 * t**2 - 4 * t + 1; d01 = -6 * t**2 + 6 * t; d11 = 3 * t**2 - 2 * t
        e00 = 12 * t - 6; e10 = 6 * t - 4; e01 = -12 * t + 6; e11 = 6 * t - 2
        px = h00 * x0 + h10 * m * a0 + h01 * x1 + h11 * m * a1
        pz = h00 * z0 + h10 * m * b0 + h01 * z1 + h11 * m * b1
        dx = d00 * x0 + d10 * m * a0 + d01 * x1 + d11 * m * a1
        dz = d00 * z0 + d10 * m * b0 + d01 * z1 + d11 * m * b1
        ex = e00 * x0 + e10 * m * a0 + e01 * x1 + e11 * m * a1
        ez = e00 * z0 + e10 * m * b0 + e01 * z1 + e11 * m * b1
        sp = np.maximum(np.hypot(dx, dz), 1e-9)
        X.append(px); Zs.append(pz); K.append((dx * ez - dz * ex) / sp**3); SEG.append(np.full(len(t), i))
    X = np.concatenate(X); Zs = np.concatenate(Zs); K = np.concatenate(K); SEG = np.concatenate(SEG)
    L = np.concatenate([[0], np.cumsum(np.hypot(np.diff(X), np.diff(Zs)))])
    return X, Zs, L, K, SEG


# ---------------------------------------------------------------- the summit spur
SPUR_START = 1036      # first existing point of the spur (its own Cirque Gate platform, past the loop's)
SPUR_PLATFORM = 25     # the spur's first 25 points are its Cirque Gate platform
_spur = RAIL[SPUR_START:]
SPUR_TREE = cKDTree(_spur[:, [0, 2]])


def kind_of(gap):
    return np.where(gap > 9.5, 1, np.where(gap < -7.0, 2, 0))      # 0 ground (benched), 1 tunnel, 2 trestle


# ---------------------------------------------------------------- vertical profile
def vertical(s, g, flats, xz, ds, dy, y0, g0, y1, g1, stmask, want_detail=False):
    """Best heights along the section: DP over levels dy apart, |dy/ds| <= 5%, level where flats, ends fixed."""
    N = len(s)
    ylo = y0 - math.floor((y0 - 0.0) / dy) * dy
    yhi = max(y0, y1, float(np.max(g))) + 15.0
    yhi = min(yhi, max(y0, y1) + 40.0)
    nl = int((yhi - ylo) / dy) + 1
    Y = ylo + dy * np.arange(nl)
    gap = g[:, None] - Y[None, :]
    fill = -gap
    C = 0.05 * np.clip(np.abs(gap), 0, 9.5)
    C += np.where(gap > 9.5, TUNNEL_M + 0.005 * (gap - 9.5), 0.0)
    C += np.where(fill > 7.0, 3.0 + 0.25 * (fill - 7.0) + 1.0 * np.maximum(fill - 15.0, 0.0) + 3.0 * np.maximum(fill - 25.0, 0.0), 0.0)
    C += np.where(g[:, None] < 0.5, 4.0, 0.0)
    # stations on or near the ground
    if stmask.any():
        sm = stmask[:, None]
        C += np.where(sm, 60.0 * (np.maximum(gap - 6.0, 0) + np.maximum(-gap - 5.0, 0)), 0.0)
    # the ski runs: only in tunnel
    x, z = xz
    ski = in_poly(x, z, SKI)
    if ski.any(): C[ski] += np.where(gap[ski] > 9.5, 0.0, 400.0)
    # the ski lift line: under it in tunnel, else 12 m clear of it
    near = seg_dist(x, z, LIFT_A, LIFT_B) < 12.0
    if near.any(): C[near] += np.where(gap[near] > 9.5, 0.0, 400.0)
    # the summit spur: 25 m clear, or 9 m above or below it
    near = SPUR_TREE.query_ball_point(np.c_[x, z], 25.0 + MARGIN)
    for k in range(N):
        if near[k]:
            ys = _spur[near[k], 1]
            C[k] += np.where((Y > ys.min() - 9.0 - MARGIN) & (Y < ys.max() + 9.0 + MARGIN), 400.0, 0.0)
    C *= ds
    D = int(round(0.05 * ds / dy + 1e-9))
    BIG = 1e18
    nfix0 = int(round(24.0 / ds)); nfix1 = int(round(24.0 / ds))
    fixlev = {}
    for k in range(nfix0 + 1): fixlev[k] = int(round((y0 + g0 * s[k] - ylo) / dy))
    for k in range(N - 1 - nfix1, N): fixlev[k] = int(round((y1 - g1 * (s[-1] - s[k]) - ylo) / dy))
    TUN = gap > 9.5; TRS = gap < -7.0
    PORTAL, ABUT = 60.0, 15.0
    arg = np.zeros((N, nl), np.int8)
    best = np.full(nl, BIG); best[fixlev[0]] = C[0, fixlev[0]]
    for k in range(1, N):
        if flats[k]:
            nb = best + (TUN[k - 1] != TUN[k]) * PORTAL + (TRS[k - 1] != TRS[k]) * ABUT
            arg[k] = 0
        else:
            nb = np.full(nl, BIG); ch = np.zeros(nl, np.int8)
            for dd in range(-D, D + 1):
                # level j at k comes from level j - dd at k - 1
                if dd >= 0: src_ = slice(0, nl - dd); dst = slice(dd, nl)
                else: src_ = slice(-dd, nl); dst = slice(0, nl + dd)
                c = best[src_] + (TUN[k - 1][src_] != TUN[k][dst]) * PORTAL + (TRS[k - 1][src_] != TRS[k][dst]) * ABUT
                m = c < nb[dst]
                nb[dst] = np.where(m, c, nb[dst]); ch[dst] = np.where(m, dd, ch[dst])
            arg[k] = ch
        best = nb + C[k]
        if k in fixlev:
            lv = fixlev[k]; keep = best[lv]; best = np.full(nl, BIG); best[lv] = keep
    i = fixlev[N - 1]; total = best[i]; lev = np.empty(N, int); lev[-1] = i
    for k in range(N - 1, 0, -1):
        i = i - int(arg[k, i]); lev[k - 1] = i
    y = Y[lev]
    y = y + (y1 - y[-1]) * (s / s[-1])      # the end level onto the exact height
    # smooth: a moving average, the ends extended along their fixed grades
    taps = 2 * int(round(SMOOTH / (2 * ds))) + 1
    if taps > 1:
        pad = taps
        ypad = np.concatenate([y0 + g0 * (np.arange(-pad, 0) * ds), y, y1 + g1 * (np.arange(1, pad + 1) * ds)])
        y = np.convolve(ypad, np.ones(taps) / taps, mode="same")[pad:-pad]
    return total, y


def self_cross(s, x, z, y, ds, report=False, period=None):
    """Where the new section passes near itself (a spiral), it must be 9 m above or below."""
    pairs = cKDTree(np.c_[x, z]).query_pairs(15.0, output_type="ndarray")
    if len(pairs) == 0: return [] if report else 0.0
    sep = np.abs(s[pairs[:, 0]] - s[pairs[:, 1]])
    if period: sep = np.minimum(sep, period - sep)
    far = sep > 60.0
    pairs = pairs[far]
    dv = np.abs(y[pairs[:, 0]] - y[pairs[:, 1]])
    if report: return [(int(a), int(b), float(v)) for (a, b), v in zip(pairs, dv) if v < 9.0]
    return float(np.sum(ds * 2e3 * np.maximum(9.0 + MARGIN - dv, 0)))


# ---------------------------------------------------------------- evaluation of a node set
def evaluate(nodes, ds=8.0, dy=0.2, detail=False):
    hn, sts = expand(nodes)
    X, Zs, L, K, SEG = hermite(hn)
    Ltot = L[-1]
    n = max(2, int(round(Ltot / ds)))
    s = np.linspace(0, Ltot, n + 1)
    dsr = Ltot / n
    x = np.interp(s, L, X); z = np.interp(s, L, Zs); kap = np.interp(s, L, K)
    seg = SEG[np.minimum(np.searchsorted(L, s, side="right") - 1, len(SEG) - 1)]
    g = ground(x, z)
    cost = {}
    cost["length"] = Ltot
    ak = np.abs(kap)
    cost["curve"] = float(np.sum(dsr * (1e9 * np.maximum(ak - 1 / 62.0, 0) ** 2 + 2e4 * np.maximum(ak - 1 / 120.0, 0) ** 2)))
    # horizontal keep-outs
    d = np.hypot(x[:, None] - KC[None, :, 0], z[:, None] - KC[None, :, 1])
    pen = np.maximum(KR[None, :] + MARGIN - d, 0)
    cost["keepout"] = float(np.sum(dsr * (5e3 * pen**2 + 200.0 * (pen > 0))))
    ra = np.minimum(rect_dist(x, z, RUNWAY), rect_dist(x, z, APRON))
    pa = np.maximum(20.0 + MARGIN - ra, 0)
    cost["airport"] = float(np.sum(dsr * (5e3 * pa**2 + 200.0 * (pa > 0))))
    # stations: platform samples, clearance from puzzles, distance to what they serve
    stmask = np.zeros(len(s), bool); flats = np.zeros(len(s), bool)
    st_info = []
    cst = 0.0
    for segi, name in sts:
        a = L[np.searchsorted(SEG, segi)]; b = L[np.searchsorted(SEG, segi + 1)] if segi + 1 <= SEG.max() else Ltot
        mid = 0.5 * (a + b)
        plat = np.abs(s - mid) <= 50.0 + 1e-6
        stmask |= plat
        flats |= np.abs(s - mid) <= FLAT / 2
        dp = np.hypot(x[plat, None] - PUZ[None, :, 0], z[plat, None] - PUZ[None, :, 1])
        pp = np.maximum(PUZR[None, :] + MARGIN - dp, 0)
        cst += float(np.sum(dsr * (5e3 * pp**2 + 200.0 * (pp > 0))))
        cx, cz = float(np.interp(mid, s, x)), float(np.interp(mid, s, z))
        served, wgt = STATIONS[name]
        if served:
            cst += wgt * float(np.mean([math.hypot(cx - px, cz - pz) for px, pz in served]))
        st_info.append((name, mid, cx, cz))
    # targets: each station within 150 m of its target
    for nd in nodes:
        if nd[0] == "st" and len(nd) > 5:
            dd = math.hypot(nd[2] - nd[5], nd[3] - nd[6])
            cst += 50.0 * max(0.0, dd - 150.0) ** 2
    cost["stations"] = cst
    flats[0] = False
    vtot, y = vertical(s, g, flats, (x, z), dsr, dy, RAIL[I_CG, 1], GR_CG, RAIL[I_SS, 1], GR_SS, stmask)
    cost["vertical"] = vtot
    cost["crossing"] = self_cross(s, x, z, y, dsr)
    total = sum(cost.values())
    if not detail: return total
    return dict(total=total, cost=cost, s=s, x=x, z=z, y=y, g=g, kap=kap, sts=st_info, ds=dsr, stmask=stmask)


# ---------------------------------------------------------------- annealing
def anneal(nodes, iters, seed=1, log=None, T0=30.0, T1=0.3, step=1.0):
    rng = np.random.default_rng(seed)
    cur = [list(n) for n in nodes]
    ccost = evaluate(cur)
    best, bcost = [list(n) for n in cur], ccost
    free = [i for i, n in enumerate(cur) if n[0] in ("free", "st")]
    t0 = time.time()
    for it in range(iters):
        T = T0 * (T1 / T0) ** (it / max(1, iters - 1))
        frac = 1.0 - it / iters
        i = free[rng.integers(len(free))]
        cand = [list(n) for n in cur]
        nd = cand[i]
        off = 1 if nd[0] == "free" else 2
        r = rng.random()
        if r < 0.4: nd[off] += rng.normal(0, step * (3 + 16 * frac))
        elif r < 0.8: nd[off + 1] += rng.normal(0, step * (3 + 16 * frac))
        else: nd[off + 2] += rng.normal(0, step * (1.0 + 6 * frac))
        c = evaluate(cand)
        if c < ccost or rng.random() < math.exp(-(c - ccost) / T):
            cur, ccost = cand, c
            if c < bcost: best, bcost = [list(n) for n in cand], c
        if log and it % 250 == 0:
            print("  it %5d  T %6.2f  cur %10.1f  best %10.1f  (%.0f s)" % (it, T, ccost, bcost, time.time() - t0), flush=True)
    return best, bcost




# ---------------------------------------------------------------- drawing
from PIL import Image, ImageDraw, ImageFont


def font(size, bold=False):
    for f in (("arialbd.ttf",) if bold else ()) + ("arial.ttf", "DejaVuSans.ttf"):
        try: return ImageFont.truetype(f, size)
        except OSError: pass
    return ImageFont.load_default()


def relief(bx0, bx1, bz0, bz1, sc):
    w = int((bx1 - bx0) * sc); h = int((bz1 - bz0) * sc)
    xs = bx0 + (np.arange(w) + 0.5) / sc; zs = bz1 - (np.arange(h) + 0.5) / sc
    XX, ZZ = np.meshgrid(xs, zs)
    G = ground(XX.ravel(), ZZ.ravel()).reshape(XX.shape)
    gz, gx = np.gradient(ndimage.gaussian_filter(G, 0.8), 1 / sc)
    gz = -gz      # rows run north to south
    shade = np.clip(0.82 + (-gx * 0.55 + gz * 0.75) * 0.45, 0.45, 1.15)
    img = np.zeros((h, w, 3))
    land = G > 0.15
    img[:] = np.array([0.36, 0.52, 0.68])
    img[(~land) & (G > -2.5)] = np.array([0.50, 0.66, 0.78])
    t = np.clip(G / 600.0, 0, 1)[..., None]
    base = np.array([0.80, 0.86, 0.76]) * (1 - t) + np.array([0.97, 0.97, 0.99]) * t
    mid = np.clip((G - 120) / 250, 0, 1)[..., None] * np.clip((420 - G) / 200, 0, 1)[..., None]
    base = base * (1 - 0.35 * mid) + np.array([0.86, 0.82, 0.76]) * 0.35 * mid
    c = base * shade[..., None]
    img[land] = c[land]
    for step, dark in ((25, 0.90), (100, 0.72)):
        f = np.floor(G / step)
        cont = ((f != np.roll(f, 1, 0)) | (f != np.roll(f, 1, 1))) & land
        img[cont] *= dark
    return Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).convert("RGB")


def kinds_for(x, z, y):
    return kind_of(ground(x, z) - y)


KCOL = {0: (205, 0, 35), 1: (25, 25, 25), 2: (30, 80, 220)}


def draw_line(dr, T, x, z, kinds, width, closed=False, colmap=KCOL, dash_tunnel=True):
    n = len(x); m = n if closed else n - 1
    for i in range(m):
        j = (i + 1) % n; k = int(kinds[i])
        if k == 1 and dash_tunnel and (i // 3) % 2 == 1: continue
        dr.line([T(x[i], z[i]), T(x[j], z[j])], fill=colmap[k], width=width)


def draw_map(path, loop, spur, stations, bbox, sc, title=None, labels=True, legend=True):
    bx0, bx1, bz0, bz1 = bbox
    im = relief(bx0, bx1, bz0, bz1, sc); dr = ImageDraw.Draw(im, "RGBA")
    def T(x, z): return ((x - bx0) * sc, (bz1 - z) * sc)
    def circ(x, z, r, col, w=1, fill=None):
        a, b = T(x - r, z + r); c, e = T(x + r, z - r); dr.ellipse([a, b, c, e], outline=col, width=w, fill=fill)
    f9, f11, f13, f16 = font(max(9, int(11 * sc / 0.6))), font(max(10, int(12 * sc / 0.6))), font(max(11, int(14 * sc / 0.6)), True), font(max(14, int(18 * sc / 0.6)), True)
    step = 250 if sc < 1.0 else 100
    for gx in range(int(math.ceil(bx0 / step) * step), int(bx1), step):
        dr.line([T(gx, bz0), T(gx, bz1)], fill=(255, 255, 255, 70), width=1); dr.text((T(gx, bz1)[0] + 3, 2), str(gx), fill=(20, 40, 90), font=f9)
    for gz in range(int(math.ceil(bz0 / step) * step), int(bz1), step):
        dr.line([T(bx0, gz), T(bx1, gz)], fill=(255, 255, 255, 70), width=1); dr.text((3, T(bx0, gz)[1] + 2), str(gz), fill=(20, 40, 90), font=f9)
    # ski runs, ski lift, gondola, airport
    pts = [T(*p) for p in SKI] + [T(*SKI[0])]
    for i in range(len(pts) - 1):
        (ax, az), (bx, bz) = pts[i], pts[i + 1]; L = math.hypot(bx - ax, bz - az); nn = max(1, int(L / 8))
        for k in range(0, nn, 2):
            dr.line([(ax + (bx - ax) * k / nn, az + (bz - az) * k / nn), (ax + (bx - ax) * min(nn, k + 1) / nn, az + (bz - az) * min(nn, k + 1) / nn)], fill=(0, 140, 150, 230), width=2)
    dr.line([T(*LIFT_A), T(*LIFT_B)], fill=(0, 120, 40, 255), width=2)
    for k in range(1, int(math.hypot(LIFT_B[0] - LIFT_A[0], LIFT_B[1] - LIFT_A[1]) // 50) + 1):
        t = 50.0 * k / math.hypot(LIFT_B[0] - LIFT_A[0], LIFT_B[1] - LIFT_A[1])
        px, pz = LIFT_A[0] + (LIFT_B[0] - LIFT_A[0]) * t, LIFT_A[1] + (LIFT_B[1] - LIFT_A[1]) * t
        circ(px, pz, 2.5, (0, 120, 40, 255), 1, (0, 120, 40, 255))
    circ(LIFT_A[0], LIFT_A[1], 20, (0, 120, 40, 200), 1)
    GC = (110, 70, 40)
    dr.line([T(*GONDOLA_BOTTOM), T(*SUMMIT)], fill=GC + (220,), width=2)
    for px, pz in PYLONS: circ(px, pz, 15, GC + (150,), 1); circ(px, pz, 3, GC + (255,), 1, GC + (255,))
    circ(GONDOLA_BOTTOM[0], GONDOLA_BOTTOM[1], 35, GC + (200,), 1)
    for r in (RUNWAY, APRON):
        dr.rectangle([T(r[0], r[3]), T(r[1], r[2])], fill=(120, 120, 125, 200), outline=(80, 80, 80, 255))
    dr.rectangle([T(RUNWAY[0] - 20, max(RUNWAY[3], APRON[3]) + 20), T(APRON[1] + 20, min(RUNWAY[2], APRON[2]) - 20)], outline=(90, 90, 90, 200), width=1)
    # keep-outs and puzzles
    for (x, z, r, name, puz) in KEEP:
        if name in ("pylon", "gondola station", "lift station"): continue
        if puz:
            circ(x, z, r, (230, 110, 0, 200), 1); circ(x, z, r - 35, (230, 110, 0, 140), 1, (255, 170, 60, 120))
            circ(x, z, 3, (200, 80, 0, 255), 1, (200, 80, 0, 255))
            if labels: dr.text((T(x, z)[0] + 5, T(x, z)[1] - 6), name, fill=(90, 40, 0, 255), font=f9)
        else:
            col = (0, 110, 220, 220) if name == "frozen lake" else (160, 0, 160, 200)
            circ(x, z, r, col, 1)
    # the spur
    if spur is not None:
        sx, sy, sz = spur[:, 0], spur[:, 1], spur[:, 2]
        draw_line(dr, T, sx, sz, kinds_for(sx, sz, sy), 3, colmap={0: (120, 60, 160), 1: (60, 30, 80), 2: (150, 110, 220)})
    # the loop
    lx, ly, lz = loop[:, 0], loop[:, 1], loop[:, 2]
    draw_line(dr, T, lx, lz, kinds_for(lx, lz, ly), 4, closed=True)
    # stations
    for (line, idx, name, x, z) in stations:
        a, b = T(x, z)
        dr.rectangle([a - 6, b - 6, a + 6, b + 6], fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=2)
        if labels:
            if line != "loop": name = name + " (spur)" if name == "Cirque Gate" else name
            tw = dr.textlength(name, font=f13)
            ox, oy = (10, -22) if line == "loop" else (10, 6)
            dr.rectangle([a + ox - 3, b + oy - 2, a + ox + tw + 3, b + oy + 17 * sc / 0.6], fill=(255, 255, 255, 215))
            dr.text((a + ox, b + oy), name, fill=(0, 0, 0, 255), font=f13)
    if title: dr.text((12, im.height - 30), title, fill=(10, 20, 60), font=f16)
    if legend:
        lx0, ly0 = 14, 30
        items = [("loop on the ground (benched)", KCOL[0], False), ("loop in tunnel (ground > 9.5 m above)", KCOL[1], True),
                 ("loop on trestle (ground > 7 m below)", KCOL[2], False), ("summit spur", (120, 60, 160), False),
                 ("gondola + pylons (15 m)", (110, 70, 40), False), ("ski lift + towers", (0, 120, 40), False),
                 ("ski runs (tunnel only)", (0, 140, 150), True), ("puzzle site, keep-out r+35 m", (230, 110, 0), False),
                 ("lookouts, far board, plaza", (160, 0, 160), False), ("frozen lake", (0, 110, 220), False)]
        dr.rectangle([lx0 - 6, ly0 - 6, lx0 + 330, ly0 + 22 * len(items) + 4], fill=(255, 255, 255, 225), outline=(0, 0, 0, 255))
        for i, (t, c, dash) in enumerate(items):
            yy = ly0 + 22 * i + 9
            if dash:
                for k in range(0, 40, 12): dr.line([(lx0 + k, yy), (lx0 + k + 7, yy)], fill=c, width=4)
            else: dr.line([(lx0, yy), (lx0 + 40, yy)], fill=c, width=4)
            dr.text((lx0 + 50, yy - 8), t, fill=(0, 0, 0), font=f11)
    im.save(path)


def draw_profile(path, loop_s, loop_y, loop_g, loop_k, loop_st, spur_s, spur_y, spur_g, spur_k, spur_st, notes=""):
    W, H = 1900, 980
    im = Image.new("RGB", (W, H), (255, 255, 255)); dr = ImageDraw.Draw(im)
    f10, f12, f14 = font(12), font(13), font(16, True)
    def panel(top, height, s, y, g, k, st, title, ymax):
        x0p, x1p = 70, W - 20; L = s[-1]
        def T(sv, yv): return (x0p + (x1p - x0p) * sv / L, top + height - height * yv / ymax)
        dr.rectangle([x0p, top, x1p, top + height], outline=(0, 0, 0))
        for yv in range(0, int(ymax) + 1, 50):
            dr.line([T(0, yv), T(L, yv)], fill=(225, 225, 225)); dr.text((x0p - 40, T(0, yv)[1] - 7), "%d" % yv, fill=(0, 0, 0), font=f10)
        stp = 500 if L > 3000 else 250
        for sv in range(0, int(L) + 1, stp):
            dr.line([T(sv, 0), T(sv, ymax)], fill=(235, 235, 235)); dr.text((T(sv, 0)[0] - 12, top + height + 3), "%d" % sv, fill=(0, 0, 0), font=f10)
        gp = [T(s[i], max(0.0, g[i])) for i in range(len(s))]
        dr.polygon([T(0, 0)] + gp + [T(L, 0)], fill=(205, 200, 190))
        dr.line(gp, fill=(120, 110, 100), width=1)
        for i in range(len(s) - 1):
            dr.line([T(s[i], y[i]), T(s[i + 1], y[i + 1])], fill=KCOL[int(k[i])], width=3)
        for (sv, name) in st:
            a, b = T(sv, 0); dr.line([(a, top), (a, top + height)], fill=(80, 80, 80))
            dr.text((a + 3, top + 4 + 16 * (st.index((sv, name)) % 3)), name, fill=(0, 0, 0), font=f12)
        dr.text((x0p, top - 22), title, fill=(0, 0, 0), font=f14)
    ym1 = 50 * math.ceil((max(np.max(loop_y), np.max(np.minimum(loop_g, np.max(loop_y) + 60))) + 10) / 50)
    panel(40, 520, loop_s, loop_y, loop_g, loop_k, loop_st, "The loop, from the Plaza clockwise (m along; heights m above sea). Track: red on the ground, black in tunnel, blue on trestle; ground shaded", ym1)
    ym2 = 50 * math.ceil((np.max(spur_y) + 40) / 50)
    panel(640, 280, spur_s, spur_y, spur_g, spur_k, spur_st, "The summit spur (existing points %d..%d)" % (SPUR_START, I_END), ym2)
    if notes: dr.text((70, H - 24), notes, fill=(60, 60, 60), font=f10)
    im.save(path)


# ---------------------------------------------------------------- the finished line, its checks and its files
EXISTING_ST = [("South Shore", 336), ("Seal Point", 531), ("West Cliffs", 778), ("Cirque Gate", 999)]


def radius3(x, z, k=2):
    """Radius of the circle through points i-k, i, i+k (inf when straight)."""
    n = len(x); r = np.full(n, np.inf)
    a = np.c_[x[:-2 * k], z[:-2 * k]]; b = np.c_[x[k:-k], z[k:-k]]; c = np.c_[x[2 * k:], z[2 * k:]]
    ab = np.hypot(*(b - a).T); bc = np.hypot(*(c - b).T); ca = np.hypot(*(a - c).T)
    cross = np.abs((b - a)[:, 0] * (c - a)[:, 1] - (b - a)[:, 1] * (c - a)[:, 0])
    r[k:-k] = np.where(cross > 1e-9, ab * bc * ca / (2 * np.maximum(cross, 1e-12)), np.inf)
    return r


def runs(mask, s):
    out = []; i = 0; n = len(mask)
    while i < n:
        if mask[i]:
            j = i
            while j + 1 < n and mask[j + 1]: j += 1
            out.append((i, j)); i = j + 1
        else: i += 1
    return out


def assemble(nodes):
    ev = evaluate(nodes, ds=4.0, dy=0.05, detail=True)
    s, x, z, y = ev["s"], ev["x"], ev["z"], ev["y"]
    n = len(s) - 1
    sts = {name: (mid, cx, cz) for name, mid, cx, cz in ev["sts"]}
    p = int(np.argmin(np.abs(s - sts["Plaza"][0])))
    q = np.c_[x, y, z]
    nA = n - p
    loop = np.vstack([q[p:n], RAIL[I_SS:I_CG], q[0:p]])
    off_ex = nA; off_B = nA + (I_CG - I_SS)
    st = [("loop", 0, "Plaza")]
    for name, (mid, cx, cz) in sts.items():
        if name == "Plaza": continue
        j = int(np.argmin(np.abs(s - mid)))
        st.append(("loop", j - p if j >= p else off_B + j, name))
    for name, i in EXISTING_ST: st.append(("loop", off_ex + (i - I_SS), name))
    st.sort(key=lambda t: t[1])
    spur = RAIL[SPUR_START:].copy()
    sst = [("summit", 12, "Cirque Gate"), ("summit", 1646 - SPUR_START, "High Shelf"), ("summit", len(spur) - 1 - 12, "Summit")]
    newmask = np.ones(len(loop), bool); newmask[off_ex:off_B] = False      # the new legs (A then B)
    legA = np.zeros(len(loop), bool); legA[:nA] = True
    return dict(ev=ev, loop=loop, spur=spur, st=st, sst=sst, newmask=newmask, legA=legA, p=p, n=n, off_ex=off_ex, off_B=off_B)


def checks(A):
    loop, spur = A["loop"], A["spur"]
    x, y, z = loop[:, 0], loop[:, 1], loop[:, 2]
    g = ground(x, z); gap = g - y; kind = kind_of(gap)
    new = A["newmask"]
    viol = []
    d = np.hypot(x[:, None] - KC[None, :, 0], z[:, None] - KC[None, :, 1])
    for j, (kx, kz, r, name, puz) in enumerate(KEEP):
        bad = np.nonzero(new & (d[:, j] < r - 1e-6))[0]
        if len(bad): viol.append("%s (%.0f, %.0f): %d points inside its %.1f m, nearest %.1f m" % (name, kx, kz, len(bad), r, d[bad, j].min()))
    ra = np.minimum(rect_dist(x, z, RUNWAY), rect_dist(x, z, APRON))
    bad = np.nonzero(new & (ra < 20.0))[0]
    if len(bad): viol.append("airport: %d points within 20 m, nearest %.1f m" % (len(bad), ra[bad].min()))
    ski = in_poly(x, z, SKI) & (kind != 1) & new
    if ski.any(): viol.append("ski runs: %d points inside the runs not in tunnel" % ski.sum())
    nl = (seg_dist(x, z, LIFT_A, LIFT_B) < 12.0) & (kind != 1) & new
    if nl.any(): viol.append("ski lift: %d points within 12 m of its line not in tunnel" % nl.sum())
    tree = cKDTree(spur[:, [0, 2]])
    for i in np.nonzero(new)[0]:
        for j in tree.query_ball_point((x[i], z[i]), 25.0):
            dh = math.hypot(spur[j, 0] - x[i], spur[j, 2] - z[i]); dv = abs(spur[j, 1] - y[i])
            if dv < 9.0:
                viol.append("spur: loop point %d %.1f m from spur point %d, only %.1f m apart in height" % (i, dh, j, dv)); break
    sl = np.r_[0, np.cumsum(np.hypot(np.diff(x), np.diff(z)))]
    Lc = sl[-1] + math.hypot(x[0] - x[-1], z[0] - z[-1])
    for a, b, dv in self_cross(sl, x, z, y, 4.0, report=True, period=Lc)[:5]:
        if new[a] or new[b]: viol.append("loop crosses itself at points %d/%d only %.1f m apart in height" % (a, b, dv))
    # station clearance from puzzles (r + 40 m) over each platform
    for line, idx, name in A["st"]:
        if line != "loop": continue
        ii = (np.arange(idx - 12, idx + 13)) % len(loop)
        dp = np.hypot(x[ii, None] - PUZ[None, :, 0], z[ii, None] - PUZ[None, :, 1]) - PUZR[None, :]
        if dp.min() < 0:
            j = int(np.argmin(dp.min(axis=0))); viol.append("station %s: platform %.1f m inside %s's r+40" % (name, -dp.min(), list(SITES)[j]))
    return viol, gap, kind


def write_all(nodes, label=""):
    os.makedirs(OUT, exist_ok=True)
    A = assemble(nodes)
    loop, spur = A["loop"], A["spur"]
    viol, gap, kind = checks(A)
    x, y, z = loop[:, 0], loop[:, 1], loop[:, 2]
    N = len(loop)
    seg = np.hypot(np.diff(np.r_[x, x[0]]), np.diff(np.r_[z, z[0]]))
    sl = np.r_[0, np.cumsum(seg)][:N]
    Lloop = float(seg.sum())
    nA, off_ex, off_B = A["n"] - A["p"], A["off_ex"], A["off_B"]
    legA_len = float(seg[:nA].sum()); ex_len = float(seg[off_ex:off_B].sum()); legB_len = float(seg[off_B:].sum())
    grade = np.diff(np.r_[y, y[0]]) / seg
    stat = np.zeros(N, bool)
    for line, idx, name in A["st"]:
        stat[(np.arange(idx - 12, idx + 13)) % N] = True
    segnew = A["newmask"] & np.roll(A["newmask"], -1)
    segnew_all = A["newmask"].copy()
    gnew = np.abs(grade[segnew_all & ~stat])
    gex = np.abs(grade[(~A["newmask"]) & ~stat])
    # radius on the loop (wrapping)
    xx = np.r_[x[-4:], x, x[:4]]; zz = np.r_[z[-4:], z, z[:4]]
    R = radius3(xx, zz, 2)[4:-4]
    Rn = np.where(A["newmask"], R, np.inf); iR = int(np.argmin(Rn))
    Re = np.where(~A["newmask"], R, np.inf); iRe = int(np.argmin(Re))
    # tunnels and trestles on the new legs
    def spans(k):
        out = []
        for a, b in runs((kind == k) & A["newmask"], sl):
            ln = float(seg[a:b + 1].sum()) if b + 1 < N else float(seg[a:b].sum()) + 4.0
            depth = float(gap[a:b + 1].max()) if k == 1 else float(-gap[a:b + 1].min())
            out.append(dict(start_m=round(float(sl[a]), 1), length_m=round(ln, 1), max_m=round(depth, 1),
                            at=[round(float(x[(a + b) // 2]), 1), round(float(z[(a + b) // 2]), 1)],
                            leg="A" if a < nA else "B"))
        return out
    tun = spans(1); tre = spans(2)
    sea = float(seg[(ground(x, z) < 0.5) & A["newmask"]].sum())
    # stations
    stj = []
    for line, idx, name in A["st"]:
        ii = (np.arange(idx - 12, idx + 13)) % N
        gr = (y[ii[-1]] - y[ii[0]]) / float(np.sum(seg[ii[:-1]]))
        grmax = float(np.max(np.abs(grade[ii[:-1]])))
        dp = np.hypot(x[idx] - PUZ[:, 0], z[idx] - PUZ[:, 1]) - (PUZR - 40.0)
        j = int(np.argmin(dp))
        dpl = np.hypot(x[ii, None] - PUZ[None, :, 0], z[ii, None] - PUZ[None, :, 1]) - (PUZR[None, :] - 40.0)
        heads = np.degrees(np.arctan2(z[ii[-1]] - z[ii[0]], x[ii[-1]] - x[ii[0]]))
        straight = float(np.max(np.abs(np.degrees(np.unwrap(np.arctan2(np.diff(z[ii]), np.diff(x[ii])))) - heads)))
        served = STATIONS.get(name, ([], 0))[0]
        stj.append(dict(name=name, index=int(idx), x=round(float(x[idx]), 1), z=round(float(z[idx]), 1), y=round(float(y[idx]), 2),
                        ground=round(float(ground(x[idx], z[idx])[0]), 1), grade_over_100m=round(float(gr), 4), max_grade_on_platform=round(grmax, 4),
                        max_heading_dev_on_platform_deg=round(straight, 2),
                        nearest_puzzle=list(SITES)[j], nearest_puzzle_edge_m=round(float(dp[j]), 1), nearest_puzzle_edge_from_platform_m=round(float(dpl.min()), 1),
                        served={(k if k else "plaza"): round(math.hypot(x[idx] - px, z[idx] - pz), 1) for k, (px, pz) in
                                zip([nm for nm, v in SITES.items() if v[:2] in served] or [""] * len(served), served)} if served else {},
                        existing=name in [e[0] for e in EXISTING_ST]))
    # joins
    def ang(v): return math.degrees(math.atan2(v[1], v[0]))
    def dd(a, b): return (a - b + 180) % 360 - 180
    i999 = off_B
    t_ex999 = ang((RAIL[I_CG + 1, 0] - RAIL[I_CG - 1, 0], RAIL[I_CG + 1, 2] - RAIL[I_CG - 1, 2]))
    t_new999 = ang((x[(i999 + 1) % N] - x[i999 - 1], z[(i999 + 1) % N] - z[i999 - 1]))
    k999 = dd(ang((x[(i999 + 1) % N] - x[i999], z[(i999 + 1) % N] - z[i999])), ang((x[i999] - x[i999 - 1], z[i999] - z[i999 - 1])))
    i336 = off_ex
    t_ex336 = ang((RAIL[I_SS + 1, 0] - RAIL[I_SS - 1, 0], RAIL[I_SS + 1, 2] - RAIL[I_SS - 1, 2]))
    t_new336 = ang((x[i336 + 1] - x[i336 - 1], z[i336 + 1] - z[i336 - 1]))
    k336 = dd(ang((x[i336 + 1] - x[i336], z[i336 + 1] - z[i336])), ang((x[i336] - x[i336 - 1], z[i336] - z[i336 - 1])))
    def gr_at(i): return ((y[(i + 1) % N] - y[i]) / seg[i], (y[i] - y[i - 1]) / seg[i - 1])
    # spur clearance summary
    tree = cKDTree(spur[:, [0, 2]])
    dmin, _ = tree.query(np.c_[x, z]); 
    sp0 = float(np.min(np.hypot(x - spur[0, 0], z - spur[0, 2])))
    pairs = cKDTree(np.c_[x, z]).query_pairs(15.0, output_type="ndarray")
    cross = None
    if len(pairs):
        sep = np.abs(sl[pairs[:, 0]] - sl[pairs[:, 1]]); sep = np.minimum(sep, Lloop - sep)
        pairs = pairs[sep > 60.0]
        if len(pairs):
            dv = np.abs(y[pairs[:, 0]] - y[pairs[:, 1]]); j = int(np.argmin(dv)); a, b = pairs[j]
            cross = dict(min_vertical_m=round(float(dv[j]), 2), at=[round(float(x[a]), 1), round(float(z[a]), 1)],
                         heights=[round(float(y[a]), 2), round(float(y[b]), 2)], lower_in_tunnel=bool(kind[a if y[a] < y[b] else b] == 1))
    metrics = dict(
        variant="spline", label=label,
        self_crossing=cross,
        loop_length_m=round(Lloop, 1), loop_points=N, point_spacing_m=[round(float(seg.min()), 3), round(float(seg.max()), 3)],
        leg_lengths_m={"A (Plaza -> South Shore, new)": round(legA_len, 1), "existing 336..999": round(ex_len, 1), "B (Cirque Gate -> Plaza, new)": round(legB_len, 1)},
        max_grade_outside_stations={"new legs": round(float(gnew.max()), 4), "existing part": round(float(gex.max()), 4)},
        min_curve_radius_m={"new legs": round(float(Rn[iR]), 1), "where": dict(index=iR, x=round(float(x[iR]), 1), z=round(float(z[iR]), 1)),
                            "existing part": round(float(Re[iRe]), 1), "existing where": dict(index=iRe, x=round(float(x[iRe]), 1), z=round(float(z[iRe]), 1))},
        tunnel_m_new={"total": round(sum(t["length_m"] for t in tun), 1), "spans": tun},
        trestle_m_new={"total": round(sum(t["length_m"] for t in tre), 1), "spans": tre},
        over_sea_m_new=round(sea, 1),
        stations=stj,
        spur=dict(points=len(spur), first_existing_index=SPUR_START, last_existing_index=I_END,
                  stations=[dict(name=nm, index=i, existing_index=SPUR_START + i, x=round(float(spur[i, 0]), 1), z=round(float(spur[i, 2]), 1), y=round(float(spur[i, 1]), 1)) for _, i, nm in A["sst"]],
                  loop_distance_from_spur_start_m=round(sp0, 1)),
        keepout_violations=viol,
        joins={"999 Cirque Gate": dict(tangent_error_deg=round(dd(t_new999, t_ex999), 3), segment_kink_deg=round(k999, 3), typical_segment_turn_deg=round(float(np.degrees(4.0 / max(Rn[iR], 1))), 3),
                                       height=round(float(y[i999]), 2), grade_in_out=[round(v, 4) for v in gr_at(i999)[::-1]]),
               "336 South Shore": dict(tangent_error_deg=round(dd(t_new336, t_ex336), 3), segment_kink_deg=round(k336, 3),
                                       height=round(float(y[i336]), 2), grade_in_out=[round(v, 4) for v in gr_at(i336)[::-1]])},
        notes=[
            "Classification as asked: tunnel where the ground is more than 9.5 m above the track, trestle where it is more than 7 m below, otherwise benched.",
            "The loop's Cirque Gate stop is existing point 999; its platform (987..1011) runs on the existing line and on leg B's first 48 m, which keeps to the old alignment (a fixed node at old point 1011). The existing line climbs at about 3.7% there, so that platform is not level (the other existing stops are not level either).",
            "The summit spur starts at existing point %d (its own Cirque Gate platform, spur index 12 = old %d); leg B keeps 25 m clear of every spur point or 9 m above/below it." % (SPUR_START, SPUR_START + 12),
            "Ski lift read strictly: within 12 m of its line the track must be in tunnel (the loop never comes near it; it passes east of the bottom station).",
            "Keep-outs are horizontal and apply in tunnels too (puzzles, pylons); only the ski runs and the lift line are lifted for tunnels.",
            "No extra station between Cirque Gate and North Shore: that stretch is long, but it runs on steep coast, in the ski-run polygon (tunnel) and through the spiral, with no puzzle within 350 m, and every level stretch there would cost descent the line does not have.",
            "Leg B has to fall from 128 m at Cirque Gate to about 33 m at North Shore: it does so at the 5% limit all the way, with a 360-degree spiral (in tunnel) in the hill north of the summit massif to gain the length; it passes under itself there with the vertical clearance checked.",
        ],
        nodes=nodes,
    )
    json.dump(metrics, open(os.path.join(OUT, "metrics.json"), "w"), indent=1)
    # the railnet file
    with open(os.path.join(OUT, "WinterIsle.railnet"), "w") as f:
        f.write("# the Winter Isle railway as a network, from tools/railnet/spline/route.py (variant \"spline\")\n")
        f.write("# P x y z: track level (top of the ballast), a point every 4 m, in running order; S index name: a station's middle\n")
        f.write("# loop: clockwise from the Plaza; points %d..%d are the existing line's 336..998 unchanged\n" % (off_ex, off_B - 1))
        f.write("LINE loop closed\n")
        for p in loop: f.write("P %.2f %.2f %.2f\n" % (p[0], p[1], p[2]))
        for line, idx, name in A["st"]: f.write("S %d %s\n" % (idx, name))
        f.write("# summit spur: the existing line's points %d..%d unchanged\n" % (SPUR_START, I_END))
        f.write("LINE summit open\n")
        for p in spur: f.write("P %.2f %.2f %.2f\n" % (p[0], p[1], p[2]))
        for line, idx, name in A["sst"]: f.write("S %d %s\n" % (idx, name))
    # plots
    stations = [(l, i, nm, loop[i, 0], loop[i, 2]) for l, i, nm in A["st"]] + [(l, i, nm, spur[i, 0], spur[i, 2]) for l, i, nm in A["sst"]]
    draw_map(os.path.join(OUT, "plan.png"), loop, spur, stations, (-3120, -360, -2700, -520), 0.62,
             title="Winter Isle railway (variant spline): loop %.2f km clockwise, summit spur %.2f km" % (Lloop / 1000, float(np.sum(np.hypot(np.diff(spur[:, 0]), np.diff(spur[:, 2])))) / 1000))
    spl = np.r_[0, np.cumsum(np.hypot(np.diff(spur[:, 0]), np.diff(spur[:, 2])))]
    draw_profile(os.path.join(OUT, "profile.png"), np.r_[sl, Lloop], np.r_[y, y[0]], np.r_[ground(x, z), ground(x[:1], z[:1])], np.r_[kind, kind[:1]],
                 [(float(sl[i]), nm) for _, i, nm in A["st"]],
                 spl, spur[:, 1], ground(spur[:, 0], spur[:, 2]), kind_of(ground(spur[:, 0], spur[:, 2]) - spur[:, 1]),
                 [(float(spl[i]), nm) for _, i, nm in A["sst"]],
                 notes="new legs: %.0f m (A %.0f, B %.0f); tunnel %.0f m, trestle %.0f m on them; existing 336..999 %.0f m" % (legA_len + legB_len, legA_len, legB_len, sum(t["length_m"] for t in tun), sum(t["length_m"] for t in tre), ex_len))
    return metrics, A


def debug_map(nodes, path, bbox, sc):
    A = assemble(nodes)
    loop, spur = A["loop"], A["spur"]
    stations = [(l, i, nm, loop[i, 0], loop[i, 2]) for l, i, nm in A["st"]] + [(l, i, nm, spur[i, 0], spur[i, 2]) for l, i, nm in A["sst"]]
    draw_map(path, loop, spur, stations, bbox, sc, legend=False)
    hn, _ = expand(nodes)
    im = Image.open(path); dr = ImageDraw.Draw(im)
    bx0, bx1, bz0, bz1 = bbox
    for (hx, hz, a, b) in hn:
        px, pz = (hx - bx0) * sc, (bz1 - hz) * sc
        dr.ellipse([px - 3, pz - 3, px + 3, pz + 3], outline=(0, 160, 0), width=2)
        dr.line([(px, pz), (px + a * 12, pz - b * 12)], fill=(0, 160, 0), width=2)
    im.save(path)


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--anneal", type=int, default=0)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--nodes", default=None, help="a JSON file of nodes to start from (default: the stored NODES)")
    ap.add_argument("--save", default=os.path.join(OUT, "nodes.json"))
    ap.add_argument("--debug", action="store_true", help="also write zoomed debug maps")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    nodes = json.load(open(a.nodes)) if a.nodes else [list(n) for n in NODES]
    t0 = time.time()
    if a.anneal:
        print("start cost %.1f  (one evaluation %.3f s)" % (evaluate(nodes), time.time() - t0), flush=True)
        nodes, c = anneal(nodes, a.anneal, a.seed, log=True)
        print("annealed cost %.1f" % c)
        json.dump(nodes, open(a.save, "w"), indent=0)
        print(json.dumps(nodes))
    ev = evaluate(nodes, detail=True)
    print("cost terms:", {k: round(v, 1) for k, v in ev["cost"].items()})
    m, A = write_all(nodes)
    print("loop %.0f m (A %.0f, existing %.0f, B %.0f)" % (m["loop_length_m"], *m["leg_lengths_m"].values()))
    print("max grade new %.4f, min radius new %.1f at %s; tunnel %.0f m, trestle %.0f m, sea %.0f m" % (
        m["max_grade_outside_stations"]["new legs"], m["min_curve_radius_m"]["new legs"], m["min_curve_radius_m"]["where"],
        m["tunnel_m_new"]["total"], m["trestle_m_new"]["total"], m["over_sea_m_new"]))
    for t in m["tunnel_m_new"]["spans"]: print("   tunnel", t)
    for t in m["trestle_m_new"]["spans"]: print("   trestle", t)
    for s_ in m["stations"]: print("   %-13s idx %4d (%7.1f, %7.1f) y %6.2f ground %6.1f grade %.4f  nearest %s %.1f m" % (s_["name"], s_["index"], s_["x"], s_["z"], s_["y"], s_["ground"], s_["grade_over_100m"], s_["nearest_puzzle"], s_["nearest_puzzle_edge_from_platform_m"]))
    print("violations:", m["keepout_violations"] or "none")
    print("joins:", m["joins"])
    if a.debug:
        debug_map(nodes, os.path.join(OUT, "dbg-north.png"), (-2800, -1050, -1600, -600), 0.8)
        debug_map(nodes, os.path.join(OUT, "dbg-east.png"), (-1450, -550, -2400, -700), 0.8)
    print("done in %.1f s" % (time.time() - t0))


NODES = [
    ["fix", float(RAIL[I_CG, 0]), float(RAIL[I_CG, 2]), TH_CG],
    ["fix", float(RAIL[I_SS, 0]), float(RAIL[I_SS, 2]), TH_SS],
]

if __name__ == "__main__":
    main()
