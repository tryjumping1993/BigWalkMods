"""The Winter Isle railway as a closed loop round the island plus the summit spur ("graph" variant).

The loop is: Leg A (Plaza -> East Lowland -> Airport -> existing point 336), the existing line 336..999 kept exactly,
and Leg B (existing point 999, Cirque Gate -> North Shore -> Northwest -> Plaza). The spur is the existing line from a
little past Cirque Gate up to the summit, unchanged.

How the new legs are made:
  1. stations are placed by enumeration (centres on a grid, 16+ headings, a straight level platform) filtered by
     flatness, keep-outs and a height window, scored by distance to the puzzles each one serves;
  2. each stretch between anchors (a join, a station end, a hand-placed junction, via point or tunnel portal) is
     routed by Dijkstra (scipy csgraph) over a 6 m grid with the heading in the state (16 directions); turning moves
     are arcs of 100 m (or a dearer 65 m) radius, so the found path is already drivable; the cost counts length, cross
     slope, the ground's grade along the move beyond 4%, the sea, ground far above or below the track's expected
     height (interpolated between the anchors' target heights), and keep-outs (forbidden). Stretches marked "tunnel"
     ignore the ground and may pass under the ski runs; the junction at Cirque Gate follows a hand-made offset path;
  3. the plan is smoothed by fitting the curvature per 4 m step (bounded by 1/R, R = 95 m, else 80/70/65 m where the
     found path needs it) to the found path, with the end positions and headings held, and a barrier against the
     keep-outs;
  4. the track height is a linear program (HiGHS) over the 4 m points: grade <= 5% (level on stations), a change of
     grade of at most 0.6% per 4 m, the old line's heights and grades at the joins, platforms on the ground, at least
     9.5 m under the spur where within 25 m of it, tunnel only under the ski runs and lift; it minimises cut/fill,
     tunnel depth beyond 9.5 m and trestle height beyond 7 m;
  5. steps 2-4 run three times, each pass routing with the heights and stretch lengths of the pass before.
Why Leg B winds: it falls 118 m from Cirque Gate (128 m) to the Plaza (11 m), which takes at least ~2.8 km at 5% with
its platforms and vertical curves, against ~2.45 km for the direct line; the extra comes from the tunnel through the
north-east massif bending east (via M1) and the loop round the east spur (via DV) before the Plaza.
  py tools/railnet/graph/route.py   writes Z:/Temp/claude/BigWalkMods/railnet/graph/{WinterIsle.railnet, plan.png,
                                    profile.png, metrics.json}
"""
import hashlib, json, math, os, struct, sys, time
for _v in ("OPENBLAS_NUM_THREADS", "OMP_NUM_THREADS", "MKL_NUM_THREADS"): os.environ[_v] = "1"      # same answer every run
import numpy as np
from scipy import ndimage, optimize, sparse
from scipy.sparse import csgraph
from scipy.spatial import cKDTree
from PIL import Image, ImageDraw, ImageFont

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
OUT = os.environ.get("RAILNET_OUT", r"Z:\Temp\claude\BigWalkMods\railnet\graph")
CACHE = os.path.join(OUT, "cache")
os.makedirs(CACHE, exist_ok=True)
T0 = time.time()
def log(*a): print("[%6.1f s]" % (time.time() - T0), *a, flush=True)

# ----------------------------------------------------------------------------------------------------- the island
# The ground to design on. The island's build benches WinterIsle.land to whatever line it was given, so once a railnet
# has been built in, the working file already carries cuttings and banks for that line, and designing on it would only
# confirm the line. By default this reads the file as committed at RAILNET_LAND_REV (HEAD: before any railnet was
# built in); RAILNET_LAND=working reads the working file, RAILNET_LAND=<path> any other grid.
def _load_land():
    import subprocess
    src = os.environ.get("RAILNET_LAND", "")
    if src and src != "working": return open(src, "rb").read(), src
    if src != "working":
        rev = os.environ.get("RAILNET_LAND_REV", "HEAD")
        try:
            data = subprocess.run(["git", "-C", REPO, "show", rev + ":unity/Land/WinterIsle.land"], capture_output=True, check=True).stdout
            if len(data) > 20: return data, "git " + rev
        except (OSError, subprocess.CalledProcessError): pass
    p = os.path.join(REPO, "unity", "Land", "WinterIsle.land"); return open(p, "rb").read(), p
_d, LAND_SRC = _load_land()
LAND_HASH = hashlib.md5(_d).hexdigest()[:10]
X0, Z0, CELL, NX, NZ = struct.unpack("<fffii", _d[:20]); GW, GH = NX + 1, NZ + 1
_g = np.frombuffer(_d, dtype="<f4", count=GW * GH, offset=20).reshape((GH, GW)).astype(np.float64)
OFF = np.isnan(_g)
G = np.where(OFF, -9.0, _g)
GS = ndimage.gaussian_filter(G, 3.0)                 # ground smoothed over ~9 m, for slopes and grades

def _sample(arr, x, z, order=1):
    x = np.asarray(x, float); z = np.asarray(z, float)
    return ndimage.map_coordinates(arr, [np.atleast_1d((z - Z0) / CELL), np.atleast_1d((x - X0) / CELL)], order=order, mode="nearest").reshape(x.shape)
def ground(x, z): return _sample(G, x, z)
def ground_s(x, z): return _sample(GS, x, z)
def offisland(x, z): return _sample(OFF.astype(float), x, z) > 0.5

# ----------------------------------------------------------------------------------------------------- the old line
RP, RS = [], []
for _l in open(os.path.join(REPO, "unity", "Land", "WinterIsle.rail")):
    _q = _l.split()
    if _q and _q[0] == "P": RP.append([float(_q[1]), float(_q[2]), float(_q[3])])
    elif _q and _q[0] == "S": RS.append(int(_q[1]))
RP = np.array(RP)                                    # columns x, y, z
J336, J999 = 336, 999
SPUR0 = int(os.environ.get("SPUR0", 1028))           # first point of the spur line (its own Cirque Gate platform)
SPUR_EXEMPT = 15                                     # its first 15 points (60 m) only need 15 m from the loop
INHERITED = [(336, "South Shore"), (531, "Seal Point"), (778, "West Cliffs"), (999, "Cirque Gate")]
def tangent(i):
    v = RP[i + 1, [0, 2]] - RP[i - 1, [0, 2]]; return v / np.linalg.norm(v)
def grade_at(i): return (RP[i + 1, 1] - RP[i - 1, 1]) / np.linalg.norm(RP[i + 1, [0, 2]] - RP[i - 1, [0, 2]])

# ----------------------------------------------------------------------------------------------------- keep-outs
RADII = dict(AdventTrail=10.6, BaubleHunt=21.2, BellRingers=21.2, BigAir=7.1, BlindCarols=14.9, BlindSculptor=18.4,
             BlizzardWalk=22, Downhill=9.9, FairyLights=25.5, Footprints=21.5, GlassBooth=13.9, HoldTheStar=25.5,
             LightTheTree=16.3, Lookouts=17, MidnightFinale=19.8, RadioSilence=14.2, ReindeerCount=26.2, SantasList=17.7,
             Slalom=7.1, SleighRun=25.6, SnowballRange=22.2, SnowBridge=20.2, SnowGlobe=17, Snowman=11.4,
             StockingSort=16.3, SummitShot=15, TheTree=33.9, ThinIce=19.8, WhisperingChimney=14.9, WhiteoutMaze=21.9)
SITES = {}
for _n, _r in RADII.items():
    _s = json.load(open(os.path.join(REPO, "unity", "Assets", "Puzzles", _n, "site.json")))
    SITES[_n] = (_s["x"], _s["z"], _r)
# circles: x, z, required clearance of the track's middle, label
CIRCLES = [(x, z, r + 35, n) for n, (x, z, r) in SITES.items()]
LOOKOUTS = [(-1400, -980), (-1250, -2000), (-2750, -2120)]
CIRCLES += [(x, z, 37, "Lookouts lookout") for x, z in LOOKOUTS]
CIRCLES += [(-858, -1001, 20, "StockingSort board"), (-705, -795, 54, "plaza circle"), (-822, -1012, 47, "frozen lake")]
GONDOLA0 = np.array([-750.0, -827.0]); _gstep = (np.array([-1787.0, -1569.0]) - GONDOLA0) / 16
PYLONS = [GONDOLA0 + k * _gstep for k in range(1, 24)]
CIRCLES += [(p[0], p[1], 15, "gondola pylon") for p in PYLONS] + [(-750, -827, 35, "gondola bottom station")]
LIFT_A, LIFT_B = np.array([-974.0, -1554.0]), np.array([-1775.0, -1844.0])
CIRCLES += [(LIFT_A[0], LIFT_A[1], 20, "ski lift bottom station")]
CIRC = np.array([[c[0], c[1], c[2]] for c in CIRCLES]); CLAB = [c[3] for c in CIRCLES]
RUNWAY = (-892, -848, -2015, -1695); APRON = (-850, -770, -1930, -1775)
RECTS = [(RUNWAY, 20, "airport runway"), (APRON, 20, "airport apron")]
SKI = np.array([(-2250, -1900), (-1900, -1150), (-1250, -1250), (-1000, -1500), (-1050, -1900), (-1700, -2150)], float)
STATION_CLEAR = 40                                   # a station's platform: 40 m from any puzzle site's centre + radius

def in_poly(x, z, poly=SKI):
    x = np.asarray(x, float); z = np.asarray(z, float); ins = np.zeros(x.shape, bool)
    for k in range(len(poly)):
        (xa, za), (xb, zb) = poly[k], poly[(k + 1) % len(poly)]
        c = ((za > z) != (zb > z)) & (x < (xb - xa) * (z - za) / (zb - za + 1e-12) + xa)
        ins ^= c
    return ins
def seg_dist(x, z, a, b):
    x = np.asarray(x, float); z = np.asarray(z, float); ab = b - a
    t = np.clip(((x - a[0]) * ab[0] + (z - a[1]) * ab[1]) / (ab @ ab), 0, 1)
    return np.hypot(x - (a[0] + t * ab[0]), z - (a[1] + t * ab[1]))
def poly_edge_dist(x, z, poly=SKI):
    return np.min([seg_dist(x, z, poly[k], poly[(k + 1) % len(poly)]) for k in range(len(poly))], axis=0)
def rect_dist(x, z, r):
    x = np.asarray(x, float); z = np.asarray(z, float)
    dx = np.maximum(np.maximum(r[0] - x, x - r[1]), 0); dz = np.maximum(np.maximum(r[2] - z, z - r[3]), 0)
    return np.hypot(dx, dz)

# the existing loop's points (336..999) and the spur's, for proximity checks
LOOPOLD = RP[J336:J999 + 1]
SPUR = RP[SPUR0:]
_old_tree = cKDTree(LOOPOLD[:, [0, 2]]); _spur_tree = cKDTree(SPUR[:, [0, 2]])

SPUR_GAP = SPUR[:, 1] - ground(SPUR[:, 0], SPUR[:, 2])         # spur track above (+) or below (-) the ground
def spur_penetration(x, z, margin=0.0, junction=False):
    """Ground-level track near the spur: 25 m from spur points that run near the ground (a loop at ground level there
    would be closer than 9 m in height). For the junction stretch: 15 m from the spur's first 15 points, 25 m from the
    next 6, and nothing beyond (the loop is meant to pass under the spur there, at least 9 m below; checked in 3D)."""
    x = np.asarray(x, float).ravel(); z = np.asarray(z, float).ravel()
    if junction:
        req = np.where(np.arange(len(SPUR)) < SPUR_EXEMPT, 15.0, np.where(np.arange(len(SPUR)) < SPUR_EXEMPT + 6, 25.0, -1e9))
        k = 40
    else:
        # a ground-level loop point conflicts with a spur point within 25 m whose track is within 12 m of that ground
        req = np.full(len(SPUR), 25.0); req[:SPUR_EXEMPT] = 15.0
        k = len(SPUR)
    d = np.hypot(x[:, None] - SPUR[None, :k, 0], z[:, None] - SPUR[None, :k, 2]) if k < 200 else None
    if d is None:
        dd, ii = _spur_tree.query(np.c_[x, z], k=16, distance_upper_bound=40.0)
        fin = np.isfinite(dd); ii = np.where(fin, ii, 0)
        gl = ground(x, z)
        close = np.abs(SPUR[ii, 1] - gl[:, None]) < 12
        return np.max(np.where(fin & close, req[ii] + margin - dd, -1e9), axis=1)
    return np.max(req[None, :k] + margin - d, axis=1)

def plan_penetration(x, z, margin=0.0, tunnel=False, skip_old=True):
    """How far each point is inside a keep-out (positive = inside), counting the margin. In a tunnel the ski runs and
    the ski lift's line do not count."""
    x = np.asarray(x, float).ravel(); z = np.asarray(z, float).ravel()
    pen = np.full(x.shape, -1e9)
    if len(CIRC):
        dd = np.hypot(x[:, None] - CIRC[None, :, 0], z[:, None] - CIRC[None, :, 1])
        pen = np.maximum(pen, np.max(CIRC[None, :, 2] + margin - dd, axis=1))
    for r, m, _ in RECTS: pen = np.maximum(pen, m + margin - rect_dist(x, z, r))
    if not tunnel:
        ins = in_poly(x, z); ed = poly_edge_dist(x, z)
        pen = np.maximum(pen, np.where(ins, ed + margin, margin - ed))
        pen = np.maximum(pen, 12 + margin - seg_dist(x, z, LIFT_A, LIFT_B))
    if skip_old:
        d, k = _old_tree.query(np.c_[x, z])
        near_join = (k < 30) | (k > len(LOOPOLD) - 30)
        pen = np.maximum(pen, np.where(near_join, -1e9, 30 + margin - d))
    return pen

# ----------------------------------------------------------------------------------------------------- the search grid
C = 6.0
DIRS = [(1, 0), (2, 1), (1, 1), (1, 2), (0, 1), (-1, 2), (-1, 1), (-2, 1), (-1, 0), (-2, -1), (-1, -1), (-1, -2), (0, -1), (1, -2), (1, -1), (2, -1)]
ANG = np.array([math.atan2(dz, dx) for dx, dz in DIRS])
def nearest_dir(th): return int(np.argmin(np.abs((ANG - th + np.pi) % (2 * np.pi) - np.pi)))
def wrap(a): return (a + np.pi) % (2 * np.pi) - np.pi

def move_set():
    """Moves per heading: (d_new, di, dj, length, samples [(fx, fz, ds)] relative to the start in metres, penalty)."""
    moves = [[] for _ in range(16)]
    for d in range(16):
        dx, dz = DIRS[d]; L = C * math.hypot(dx, dz); n = max(2, int(round(L / 3)))
        moves[d].append((d, dx, dz, L, [(dx * C * t / n, dz * C * t / n, L / n) for t in range(1, n + 1)], 0.0))
        for R, pen in ((100.0, 3.0), (65.0, 30.0)):
            for s in (1, -1):
                d2 = (d + s) % 16; da = wrap(ANG[d2] - ANG[d]); a = ANG[d]
                chord = 2 * R * math.sin(abs(da) / 2); am = a + da / 2
                ex, ez = chord * math.cos(am), chord * math.sin(am)
                di, dj = int(round(ex / C)), int(round(ez / C))
                arc = R * abs(da); n = max(3, int(round(arc / 3)))
                smp = []
                for t in np.arange(1, n + 1) / n:
                    if da > 0: cx, cz = -R * math.sin(a), R * math.cos(a); px, pz = cx + R * math.sin(a + t * da), cz - R * math.cos(a + t * da)
                    else: cx, cz = R * math.sin(a), -R * math.cos(a); px, pz = cx - R * math.sin(a + t * da), cz + R * math.cos(a + t * da)
                    smp.append((px + t * (di * C - ex), pz + t * (dj * C - ez), arc / n))
                moves[d].append((d2, di, dj, arc, smp, pen))
    return moves
MOVES = move_set()

W_GRADE, W_CROSS, W_SEA = float(os.environ.get("W_GRADE", 1.5)), 2.0, 25.0
W_HEIGHT = float(os.environ.get("W_HEIGHT", 3.0))
def route(A, thA, B, thB, mode="normal", margin=8.0, pad=350.0, extra_forbid=None, wgrade=None, label="", spur="normal", hab=None):
    """Shortest path on the 6 m grid from A (heading thA) to B (arriving with heading thB). Returns a polyline.
    hab = (height at A, height at B): the track's expected height, interpolated by straight-line distance from the two
    ends, makes ground far above it (a deep cut or tunnel) or far below it (a tall bank or trestle) dearer."""
    wg = W_GRADE if wgrade is None else wgrade
    if hab is not None: hab = (round(float(hab[0])), round(float(hab[1])))
    key = hashlib.md5(repr((A, thA, B, thB, mode, margin, pad, wg, W_CROSS, W_SEA, extra_forbid is not None and extra_forbid[1], SPUR0, spur, hab, W_HEIGHT, LAND_HASH, 4)).encode()).hexdigest()[:16]
    cf = os.path.join(CACHE, "route_%s.npy" % key)
    if os.path.exists(cf): return np.load(cf)
    xa, xb = min(A[0], B[0]) - pad, max(A[0], B[0]) + pad
    za, zb = min(A[1], B[1]) - pad, max(A[1], B[1]) + pad
    xa, za = max(xa, X0 + 30), max(za, Z0 + 30); xb, zb = min(xb, X0 + NX * CELL - 30), min(zb, Z0 + NZ * CELL - 30)
    BW, BH = int((xb - xa) / C) + 1, int((zb - za) / C) + 1
    gx = xa + C * np.arange(BW); gz = za + C * np.arange(BH)
    MX, MZ = np.meshgrid(gx, gz)
    tunnel = mode == "tunnel"
    pen = plan_penetration(MX, MZ, margin=margin, tunnel=tunnel).reshape(MX.shape)
    gr = ground(MX, MZ); grs = ground_s(MX, MZ)
    forb = (pen > 0) | offisland(MX, MZ)
    if extra_forbid is not None: forb |= extra_forbid[0](MX, MZ)
    if spur: forb |= (spur_penetration(MX, MZ, margin=margin * 0.5, junction=spur == "junction") > 0).reshape(MX.shape)
    if tunnel: forb |= gr < 1.0                       # no tunnels under the sea
    # the ends are checked on their own (with the true clearances); free a few cells round them for the search margin
    free0 = (plan_penetration(MX, MZ, margin=0.0, tunnel=tunnel).reshape(MX.shape) <= 0) & ~offisland(MX, MZ)
    for P_, th_ in ((A, thA), (B, thB)):
        for t_ in np.arange(-24, 25, 3.0):
            q = (P_[0] + t_ * math.cos(th_), P_[1] + t_ * math.sin(th_))
            forb[(np.hypot(MX - q[0], MZ - q[1]) < 7) & free0] = False
    # cost per metre by heading (cross slope depends on the direction of travel)
    sgz, sgx = np.gradient(grs, C)
    base = np.ones(MX.shape)
    if not tunnel: base += np.where(gr < 0.5, W_SEA, 0.0)
    if not tunnel and hab is not None:
        dA = np.hypot(MX - A[0], MZ - A[1]); dB = np.hypot(MX - B[0], MZ - B[1])
        Hx = hab[0] + (hab[1] - hab[0]) * dA / (dA + dB + 1e-9)
        base += W_HEIGHT * (np.maximum(0, gr - Hx - 6.0) + np.maximum(0, Hx - gr - 4.0)) / 5.0
    cell_cost = []
    for d in range(16):
        if tunnel: cell_cost.append(base); continue
        nxv, nzv = -math.sin(ANG[d]), math.cos(ANG[d])
        cross = np.abs(sgx * nxv + sgz * nzv)
        cell_cost.append(base + W_CROSS * np.maximum(0, cross - 0.35))
    N = BW * BH * 16
    src_all, dst_all, w_all = [], [], []
    JJ, II = np.mgrid[0:BH, 0:BW]
    for d in range(16):
        for (d2, di, dj, L, smp, tpen) in MOVES[d]:
            ok = (II + di >= 0) & (II + di < BW) & (JJ + dj >= 0) & (JJ + dj < BH) & ~forb
            cost = np.zeros(MX.shape); dm = d2 if d2 == d else d
            for fx, fz, ds in smp:
                si = np.clip(II + int(round(fx / C)), 0, BW - 1); sj = np.clip(JJ + int(round(fz / C)), 0, BH - 1)
                okk = (II + int(round(fx / C)) >= 0) & (II + int(round(fx / C)) < BW) & (JJ + int(round(fz / C)) >= 0) & (JJ + int(round(fz / C)) < BH)
                ok &= okk & ~forb[sj, si]
                cost += cell_cost[dm][sj, si] * ds
            if not tunnel:
                ti = np.clip(II + di, 0, BW - 1); tj = np.clip(JJ + dj, 0, BH - 1)
                gd = np.abs(grs[tj, ti] - grs) / L
                cost += L * wg * np.maximum(0, gd - 0.04) / 0.01
            cost += tpen
            s = ((JJ * BW + II) * 16 + d)[ok]; t = (((JJ + dj) * BW + (II + di)) * 16 + d2)[ok]
            src_all.append(s); dst_all.append(t); w_all.append(cost[ok])
    src = np.concatenate(src_all); dst = np.concatenate(dst_all); w = np.concatenate(w_all)
    keyv = src.astype(np.int64) * N + dst
    order = np.lexsort((w, keyv)); keyv = keyv[order]; first = np.r_[True, keyv[1:] != keyv[:-1]]
    sel = order[first]
    M = sparse.csr_matrix((w[sel], (src[sel], dst[sel])), shape=(N, N))
    ia, ja = int(round((A[0] - xa) / C)), int(round((A[1] - za) / C))
    ib, jb = int(round((B[0] - xa) / C)), int(round((B[1] - za) / C))
    s0 = (ja * BW + ia) * 16 + nearest_dir(thA)
    dist, pred = csgraph.dijkstra(M, directed=True, indices=s0, return_predecessors=True)
    best, bnode = np.inf, -1
    db = nearest_dir(thB)
    for ddj in (-1, 0, 1):
        for ddi in (-1, 0, 1):
            for dd in (db - 1, db, db + 1):
                nd = ((jb + ddj) * BW + (ib + ddi)) * 16 + dd % 16
                if 0 <= nd < N and dist[nd] + 2 * abs(dd - db) < best: best, bnode = dist[nd] + 2 * abs(dd - db), nd
    if not np.isfinite(best):
        raise RuntimeError("no route %s from %s to %s (start forbidden: %s, goal forbidden: %s)" % (label, A, B, forb[ja, ia], forb[jb, ib]))
    nodes = [bnode]
    while nodes[-1] != s0: nodes.append(pred[nodes[-1]])
    nodes = nodes[::-1]
    pts = [np.array(A, float)]
    for a, b in zip(nodes[:-1], nodes[1:]):
        ca, da = divmod(a, 16); ja_, ia_ = divmod(ca, BW)
        cb, db_ = divmod(b, 16)
        for (d2, di, dj, L, smp, tpen) in MOVES[da]:
            if d2 == db_ and (ca + dj * BW + di) == cb:
                for fx, fz, ds in smp: pts.append(np.array([gx[ia_] + fx, gz[ja_] + fz]))
                break
    pts.append(np.array(B, float))
    pts = np.array(pts)
    log("  route %-28s %5.0f m, cost %.0f, %d nodes" % (label, np.sum(np.hypot(*np.diff(pts, axis=0).T)), best, len(nodes)))
    np.save(cf, pts)
    return pts

# ----------------------------------------------------------------------------------------------------- plan smoothing
def resample(poly, n):
    seg = np.hypot(*np.diff(poly, axis=0).T); L = np.r_[0, np.cumsum(seg)]
    t = np.linspace(0, L[-1], n)
    return np.c_[np.interp(t, L, poly[:, 0]), np.interp(t, L, poly[:, 1])]
def integrate(p0, th0, kap, h):
    cs = np.cumsum(kap) * h
    phi = th0 + np.r_[0, cs[:-1]] + kap * h / 2
    steps = h * np.c_[np.cos(phi), np.sin(phi)]
    return np.vstack([p0, p0 + np.cumsum(steps, axis=0)]), th0 + cs[-1]

def smooth(A, thA, B, thB, ref, rmin=95.0, tunnel=False, label="", spur="normal"):
    """Fit the curvature; if the ends cannot be met at this radius, try tighter ones (never under 60 m)."""
    best = None
    for r, lim in ((rmin, MAXDEV * 1.4), (80.0, MAXDEV * 2.3), (70.0, MAXDEV * 3.3), (65.0, 1e9)):
        if r > rmin: continue
        P, ok, dev = smooth1(A, thA, B, thB, ref, r, tunnel, label, spur)
        if ok and dev <= lim: return P
        if ok and (best is None or dev < best[1]): best = (P, dev)
    if best is None: raise RuntimeError("smoothing %s: no radius met the end conditions" % label)
    return best[0]

MAXDEV = float(os.environ.get("MAXDEV", 6.0))
def smooth1(A, thA, B, thB, ref, rmin, tunnel, label, spur):
    P, ok, dev = smooth2(A, thA, B, thB, ref, rmin, tunnel, label, spur, float(os.environ.get("SM_WP", 1.5)))
    if not ok: P, ok, dev = smooth2(A, thA, B, thB, ref, rmin, tunnel, label, spur, 0.3)      # a looser fit
    return P, ok, dev

def clean_ref(A, thA, B, thB, ref):
    """Drop path points that lie behind the start or past the end (the search's cells are up to a cell off the ends)."""
    ref = [np.asarray(p, float) for p in ref]
    uA = np.array([math.cos(thA), math.sin(thA)]); uB = np.array([math.cos(thB), math.sin(thB)])
    while len(ref) > 2 and (ref[1] - ref[0]) @ uA < 4.0: del ref[1]
    while len(ref) > 2 and (ref[-1] - ref[-2]) @ uB < 4.0: del ref[-2]
    return np.array(ref)

def smooth2(A, thA, B, thB, ref, rmin, tunnel, label, spur, wfit):
    ref = clean_ref(A, thA, B, thB, ref)
    key = hashlib.md5(repr((A, thA, B, thB, ref.round(2).tobytes(), rmin, tunnel, spur, SPUR0, wfit, os.environ.get("SM_WS", 1200.0), 11)).encode()).hexdigest()[:16]
    cf = os.path.join(CACHE, "smooth_%s.npz" % key)
    if os.path.exists(cf): z_ = np.load(cf); return z_["P"], bool(z_["ok"]), float(z_["dev"])
    A = np.array(A, float); B = np.array(B, float)
    L = np.sum(np.hypot(*np.diff(ref, axis=0).T)); N = max(6, int(round(L / 4.0))); h0 = L / N
    R = resample(ref, N + 1)
    # initial curvature from the reference's vertex headings, pinned to the end headings
    sh = np.unwrap(np.arctan2(*np.diff(R, axis=0).T[::-1]))
    sh += 2 * np.pi * np.round((thA - sh[0]) / (2 * np.pi))
    psi = np.r_[thA, (sh[:-1] + sh[1:]) / 2, 0.0]
    psi[-1] = thB + 2 * np.pi * np.round((sh[-1] - thB) / (2 * np.pi))
    psi = ndimage.gaussian_filter1d(psi, 2.0, mode="nearest"); psi[0] = thA; psi[-1] = thB + 2 * np.pi * np.round((sh[-1] - thB) / (2 * np.pi))
    turn_total = psi[-1] - thA
    km = 1.0 / rmin
    x0 = np.r_[np.clip(np.diff(psi) / h0, -km * 0.98, km * 0.98), h0]
    WP_, WS_, WK_, WKO_ = wfit, float(os.environ.get("SM_WS", 1200.0)), 4.0, 40.0
    def pen_fn(P):
        pen = plan_penetration(P[:, 0], P[:, 1], margin=3.0, tunnel=tunnel)
        if spur: pen = np.maximum(pen, spur_penetration(P[:, 0], P[:, 1], margin=2.0, junction=spur == "junction"))
        return pen
    def geom(v):
        kap, h = v[:-1], v[-1]
        C_ = np.r_[0, np.cumsum(kap)]
        phi = thA + h * (C_[:-1] + kap / 2)
        e = np.c_[np.cos(phi), np.sin(phi)]; t = np.c_[-np.sin(phi), np.cos(phi)]
        P = np.vstack([A, A + h * np.cumsum(e, axis=0)])
        return kap, h, C_, phi, e, t, P
    def fun_jac(v, WE):
        kap, h, C_, phi, e, t, P = geom(v)
        thN = thA + h * C_[-1]
        T = np.vstack([[0, 0], np.cumsum(t, axis=0)])           # T[j] = sum_{k<j} t_k
        jj = np.arange(N + 1)[:, None]; ii = np.arange(N)[None, :]
        low = ii < jj
        dPx = np.where(low, h * h * (T[:, 0:1] - T[1:, 0][None, :] + t[:, 0][None, :] / 2), 0.0)
        dPz = np.where(low, h * h * (T[:, 1:2] - T[1:, 1][None, :] + t[:, 1][None, :] / 2), 0.0)
        E = np.vstack([[0, 0], np.cumsum(e, axis=0)])
        TW = np.vstack([[0, 0], np.cumsum(t * (C_[:-1] + kap / 2)[:, None], axis=0)])
        dPh = E + h * TW                                         # d p_j / d h
        res, jac = [], []
        # follow the reference
        res.append(((P[1:-1] - R[1:-1]) * WP_).ravel())
        Jp = np.zeros((2 * (N - 1), N + 1))
        Jp[0::2, :N] = dPx[1:-1] * WP_; Jp[1::2, :N] = dPz[1:-1] * WP_
        Jp[0::2, N] = dPh[1:-1, 0] * WP_; Jp[1::2, N] = dPh[1:-1, 1] * WP_
        jac.append(Jp)
        # smooth curvature, small curvature, straight-ish at both ends (stations, platforms, portals)
        Js = np.zeros((N - 1, N + 1)); Js[np.arange(N - 1), np.arange(N - 1)] = -WS_; Js[np.arange(N - 1), np.arange(1, N)] = WS_
        res.append(np.diff(kap) * WS_); jac.append(Js)
        Jk = np.zeros((N, N + 1)); Jk[np.arange(N), np.arange(N)] = WK_
        res.append(kap * WK_); jac.append(Jk)
        Je = np.zeros((2, N + 1)); Je[0, 0] = Je[1, N - 1] = 600.0
        res.append(np.r_[kap[0], kap[-1]] * 600.0); jac.append(Je)
        # the end: position and heading
        Jn = np.zeros((3, N + 1))
        Jn[0, :N] = dPx[N] * WE; Jn[1, :N] = dPz[N] * WE; Jn[0, N] = dPh[N, 0] * WE; Jn[1, N] = dPh[N, 1] * WE
        Jn[2, :N] = h * WE * 30; Jn[2, N] = C_[-1] * WE * 30
        res.append(np.r_[(P[-1] - B) * WE, (thN - (thA + turn_total)) * WE * 30]); jac.append(Jn)
        # keep-outs
        pen = pen_fn(P); act = pen > 0
        rko = np.maximum(pen, 0) * WKO_
        Jko = np.zeros((N + 1, N + 1))
        if act.any():
            eps = 0.5; Pa = P[act]
            gx_ = (pen_fn(Pa + [eps, 0]) - pen_fn(Pa - [eps, 0])) / (2 * eps)
            gz_ = (pen_fn(Pa + [0, eps]) - pen_fn(Pa - [0, eps])) / (2 * eps)
            Jko[act, :N] = WKO_ * (gx_[:, None] * dPx[act] + gz_[:, None] * dPz[act])
            Jko[act, N] = WKO_ * (gx_ * dPh[act, 0] + gz_ * dPh[act, 1])
        res.append(rko); jac.append(Jko)
        return np.concatenate(res), np.vstack(jac)
    lb = np.r_[np.full(N, -km), h0 * 0.85]; ub = np.r_[np.full(N, km), h0 * 1.15]
    v = x0
    for WE in (20.0, 200.0, 2000.0):
        cache = {}
        def f(v_):
            key_ = v_.tobytes()
            if key_ not in cache: cache.clear(); cache[key_] = fun_jac(v_, WE)
            return cache[key_][0]
        def j(v_):
            key_ = v_.tobytes()
            if key_ not in cache: cache.clear(); cache[key_] = fun_jac(v_, WE)
            return cache[key_][1]
        res_ = optimize.least_squares(f, np.clip(v, lb + 1e-12, ub - 1e-12), jac=j, bounds=(lb, ub), method="trf", x_scale=np.r_[np.full(N, km), h0], max_nfev=400)
        v = res_.x
    kap, h, C_, phi, e, t, P = geom(v)
    thN = thA + h * C_[-1]
    pen = pen_fn(P)
    log("  smooth %-27s %5.0f m, end err %.3f m / %.3f deg, max dev %.1f m, min R %.0f m, keep-out %.1f m" % (label, N * h, np.linalg.norm(P[-1] - B), math.degrees(abs(wrap(thN - thB))),
        np.max(cKDTree(resample(ref, int(L) + 2)).query(P)[0]), 1 / max(np.max(np.abs(kap)), 1e-9), max(0.0, pen.max() - 3.0)))
    # close the last few centimetres smoothly (spread the end error over the last 25 points)
    # success: the ends met and no true keep-out entered (the spur's 2D proxy is only a push here: the spur rule is
    # checked in 3D on the final line)
    ok = np.linalg.norm(P[-1] - B) < 0.05 and abs(wrap(thN - thB)) < math.radians(0.1) and plan_penetration(P[:, 0], P[:, 1], tunnel=tunnel).max() < 0.0
    err = B - P[-1]; w_ = np.clip((np.arange(N + 1) - (N - 25)) / 25.0, 0, 1) ** 2
    P = P + w_[:, None] * err[None, :]
    Rd = resample(ref, int(L) + 2); dev = float(np.max(cKDTree(Rd).query(P)[0]))      # farthest from the found path
    np.savez(cf, P=P, ok=ok, dev=dev)
    return P, ok, dev

# ----------------------------------------------------------------------------------------------------- stations
def station_points(c, th, half=55.0, step=4.0):
    t = np.arange(-half, half + 0.01, step)
    return np.c_[c[0] + t * math.cos(th), c[1] + t * math.sin(th)]

def enumerate_station(name, box, serves, headings, hwin, maxdev=4.0, prefer=None, half=55.0, top=6):
    """Candidate platforms: centres on a 10 m grid in box, headings (radians) given; returns the best few."""
    cands = []
    xs = np.arange(box[0], box[1] + 0.1, 10.0); zs = np.arange(box[2], box[3] + 0.1, 10.0)
    PX = np.array([[s[0], s[1], s[2]] for n, s in SITES.items()])
    for th in headings:
        for cx in xs:
            for cz in zs:
                p = station_points((cx, cz), th, half)
                g = ground(p[:, 0], p[:, 1])
                if np.min(g) < 1.0: continue
                hl = float(np.median(g)); dev = float(np.max(np.abs(g - hl)))
                if dev > maxdev or not (hwin[0] <= hl <= hwin[1]): continue
                if np.max(plan_penetration(p[:, 0], p[:, 1], margin=3.0)) > 0: continue
                dd = np.hypot(p[:, 0, None] - PX[None, :, 0], p[:, 1, None] - PX[None, :, 1]) - PX[None, :, 2]
                if np.min(dd) < STATION_CLEAR: continue
                score = np.mean([math.hypot(cx - SITES[s][0] if s in SITES else cx - s[0], cz - SITES[s][1] if s in SITES else cz - s[1]) for s in serves])
                score += 6.0 * dev
                if prefer is not None: score += 0.5 * math.hypot(cx - prefer[0], cz - prefer[1])
                cands.append((score, cx, cz, th, hl, dev))
    cands.sort()
    if not cands: raise RuntimeError("no station site for " + name)
    log("station %-13s %d candidates; best: " % (name, len(cands)) + "; ".join("(%.0f,%.0f) %.0f deg h %.1f dev %.1f s %.0f" % (c[1], c[2], math.degrees(c[3]), c[4], c[5], c[0]) for c in cands[:top]))
    return cands[0]

def degs(*a): return [math.radians(x) for x in a]

# ----------------------------------------------------------------------------------------------------- the design
def along(p, th, s): return (p[0] + s * math.cos(th), p[1] + s * math.sin(th))
HALF = 55.0                                           # a platform: 110 m straight and level
PLAT = 48.0                                           # the inherited platform halves past 999 and before 336

def design():
    d = degs
    st = {}
    st["Plaza"] = enumerate_station("Plaza", (-860, -740, -990, -930), ["TheTree"], d(-15, -10, -5, 0, 5, 10), (0, 30), maxdev=6.0, prefer=(-705, -795))
    st["East Lowland"] = enumerate_station("East Lowland", (-900, -800, -1380, -1220), ["Lookouts", "SnowGlobe", "WhiteoutMaze"], d(-100, -95, -90, -85, -80), (0, 30))
    st["Airport"] = enumerate_station("Airport", (-945, -912, -1950, -1760), ["BlindSculptor", "FairyLights", "ThinIce"], d(-95, -90), (0, 60), prefer=(-913, -1860))
    st["North Shore"] = enumerate_station("North Shore", tuple(map(float, os.environ.get("NS_BOX", "-1720,-1560,-900,-790").split(","))), ["ReindeerCount", "Footprints"],
                                          d(-30, -20, -10, 0, 10, 20, 30), tuple(map(float, os.environ.get("NS_H", "54,64").split(","))))
    st["Northwest"] = enumerate_station("Northwest", tuple(map(float, os.environ.get("NW_BOX", "-1460,-1320,-980,-840").split(","))), ["SleighRun", "BlizzardWalk", (-1400, -980)],
                                        d(-60, -50, -40, -30, -20), tuple(map(float, os.environ.get("NW_H", "44,52").split(","))))
    return st

def build(state):
    st = design()
    u999 = tangent(J999); th999 = math.atan2(u999[1], u999[0])
    u336 = tangent(J336); th336 = math.atan2(u336[1], u336[0])
    p999 = RP[J999, [0, 2]]; p336 = RP[J336, [0, 2]]
    S1 = along(p999, th999, PLAT)                     # the end of Cirque Gate's platform on the loop
    E1 = along(p336, th336, -PLAT)                    # the start of South Shore's platform
    # the junction: the loop bears right of the spur into the hill, then passes under it to the north face
    def spur_off(k, off):
        i = k - SPUR0; sp = SPUR[i]; v = SPUR[i + 1, [0, 2]] - SPUR[i - 1, [0, 2]]; th = math.atan2(v[1], v[0])
        return (sp[0] - off * math.sin(th), sp[2] + off * math.cos(th)), th
    K2I, K2OFF = int(os.environ.get("K2_I", 1080)), float(os.environ.get("K2_OFF", 14.0))
    K2, spth = spur_off(K2I, K2OFF)
    def anchor(name, default, th):
        p = tuple(map(float, os.environ.get(name, default).split(","))); return p, math.radians(float(os.environ.get(name + "_TH", th)))
    WP, thWP = anchor("WP", "-2060,-1421", 20)
    M1, thM1 = anchor("M1", "-1770,-1345", 30)
    M2, thM2 = anchor("M2", "-1795,-1185", 110)
    NP, thNP = anchor("NP", "-1800,-1035", 70)
    def ends(name):
        c = st[name]; return along((c[1], c[2]), c[3], -HALF), along((c[1], c[2]), c[3], HALF), c[3]
    # pieces in running order from 999 to 336: (kind, data)
    plan = []
    plan.append(("fixed", np.array([p999, S1]), "Cirque Gate platform"))
    # junction reference: offsets (left +, right -) from the old line / spur by point index, from S1 (point 1011) to K2
    JOFF = [(1011, 0.0), (1018, -8.0), (1025, -22.0), (1032, -27.0), (1040, -29.0), (1046, -26.0), (1053, -12.0), (1060, 0.0), (1068, 12.0), (K2I, K2OFF)]
    jref = []
    for k in np.arange(1011, K2I + 0.01, 0.5):
        i0 = int(k); fr = k - i0
        p = RP[i0, [0, 2]] * (1 - fr) + RP[min(i0 + 1, len(RP) - 1), [0, 2]] * fr
        v = RP[i0 + 1, [0, 2]] - RP[i0 - 1, [0, 2]]; th = math.atan2(v[1], v[0])
        off = np.interp(k, [a for a, b in JOFF], [b for a, b in JOFF])
        jref.append(p + off * np.array([-math.sin(th), math.cos(th)]))
    jref = np.array(jref); jref[0] = S1; jref[-1] = K2
    segs = [(S1, th999, K2, spth, "tunnel", "junction", "junction", jref)]
    if os.environ.get("GA", "-2290,-1394") != "none":
        GA, thGA = anchor("GA", "-2290,-1394", -38)
        segs.append((K2, spth, GA, thGA, "normal", "north face", "normal"))
        segs.append((GA, thGA, WP, thWP, "normal", "across the bay head", "normal"))
    else:
        segs.append((K2, spth, WP, thWP, "normal", "north face", "normal"))
    segs.append((WP, thWP, M1, thM1, "tunnel", "massif tunnel", None))
    if os.environ.get("M2"):
        segs.append((M1, thM1, M2, thM2, "tunnel", "massif tunnel 2", None))
        segs.append((M2, thM2, NP, thNP, "tunnel", "massif tunnel 3", None))
    else:
        segs.append((M1, thM1, NP, thNP, "tunnel", "massif tunnel 2", None))
    a, b, th = ends("North Shore"); segs.append((NP, thNP, a, th, "normal", "to North Shore", "normal")); prev = (b, th)
    a, b, th = ends("Northwest"); segs.append((prev[0], prev[1], a, th, "normal", "to Northwest", "normal")); prev = (b, th)
    if os.environ.get("DV", "-1110,-1215") != "none":
        DV, thDV = anchor("DV", "-1110,-1215", 0)
        segs.append((prev[0], prev[1], DV, thDV, "normal", "round the east spur", "normal")); prev = (DV, thDV)
    a, b, th = ends("Plaza"); segs.append((prev[0], prev[1], a, th, "normal", "to Plaza", "normal")); prev = (b, th)
    a, b, th = ends("East Lowland"); segs.append((prev[0], prev[1], a, th, "normal", "to East Lowland", "normal")); prev = (b, th)
    a, b, th = ends("Airport"); segs.append((prev[0], prev[1], a, th, "normal", "to Airport", "normal")); prev = (b, th)
    if os.environ.get("SP", "-1000,-2215") != "none":
        SP, thSP = anchor("SP", "-1000,-2215", -165)
        segs.append((prev[0], prev[1], SP, thSP, "normal", "to the south portal", "normal")); prev = (SP, thSP)
    segs.append((prev[0], prev[1], E1, th336, "tunnel", "south tunnel", None))
    # target heights at the anchors: the joins, the stations at their ground, and in between by the length of the
    # stretches (from the last run where known): they steer the search towards ground at the height the track will have
    seglen = state.get("seglen", {})                  # the previous pass's stretch lengths (none on the first)
    fixed_h = {0: RP[J999, 1] + PLAT * grade_at(J999)}
    for k, sg in enumerate(segs):
        for name in NAMES:
            a_, b_, th_ = ends(name)
            if np.allclose(sg[2], a_): fixed_h[k + 1] = st[name][4]
            if np.allclose(sg[0], b_): fixed_h[k] = st[name][4]
    fixed_h[len(segs)] = RP[J336, 1] - PLAT * grade_at(J336)
    Ls_ = [seglen.get(sg[5], 1.2 * math.dist(sg[0], sg[2])) for sg in segs]
    cum = np.r_[0, np.cumsum(Ls_)]
    ks = sorted(fixed_h)
    anchor_h = np.interp(cum, cum[ks], [fixed_h[k] for k in ks])
    prev_h = state.get("anchor_h", {})                # the previous pass's track heights at the anchors
    for k, sg in enumerate(segs):
        if (k + 1) not in fixed_h and sg[5] in prev_h: anchor_h[k + 1] = prev_h[sg[5]]
    newlen = {}
    for k, sg in enumerate(segs):
        (A, thA, B, thB, mode, label, spur) = sg[:7]
        ref = sg[7] if len(sg) > 7 else route(A, thA, B, thB, mode=mode, label=label, spur=spur, hab=(anchor_h[k], anchor_h[k + 1]))
        P = smooth(A, thA, B, thB, ref, rmin=float(os.environ.get("RMIN", 95)) if label != "junction" else 80.0, tunnel=(mode == "tunnel"), label=label, spur=spur)
        plan.append(("seg", P, label, mode))
        newlen[label] = float(np.sum(np.hypot(*np.diff(P, axis=0).T)))
        for name in ("North Shore", "Northwest", "Plaza", "East Lowland", "Airport"):
            a_, b_, th_ = ends(name)
            if np.allclose(B, a_): plan.append(("station", np.array([a_, b_]), name))
    plan.append(("fixed", np.array([E1, p336]), "South Shore platform"))
    state["seglen"] = newlen
    log("anchor target heights: " + ", ".join("%s %.0f" % (sg[5], anchor_h[k + 1]) for k, sg in enumerate(segs)))
    # one polyline 999 -> 336 with the kind of each stretch
    poly, tags = [np.array(p999)], [""]
    for item in plan:
        P = item[1]
        dense = [P[0]]
        for q0, q1 in zip(P[:-1], P[1:]):
            n = max(1, int(math.ceil(np.linalg.norm(q1 - q0) / 1.0)))
            for t in np.arange(1, n + 1) / n: dense.append(q0 + t * (q1 - q0))
        dense = np.array(dense)
        tag = item[2] if item[0] == "station" else ("T:" + item[2] if item[0] == "seg" and item[3] == "tunnel" else item[2])
        poly.extend(dense[1:]); tags.extend([tag] * (len(dense) - 1))
    poly = np.array(poly)
    seg = np.hypot(*np.diff(poly, axis=0).T); Ls = np.r_[0, np.cumsum(seg)]
    n = int(round(Ls[-1] / 4.0)); step = Ls[-1] / n
    s = np.arange(n + 1) * step
    xs, zs = np.interp(s, Ls, poly[:, 0]), np.interp(s, Ls, poly[:, 1])
    xs[0], zs[0], xs[-1], zs[-1] = p999[0], p999[1], p336[0], p336[1]
    tag_idx = np.searchsorted(Ls, s, side="left").clip(0, len(tags) - 1)
    ptags = [tags[i] for i in tag_idx]
    log("new track 999 -> 336: %.1f m, %d steps of %.4f m" % (Ls[-1], n, step))
    return dict(st=st, xs=xs, zs=zs, s=s, step=step, tags=ptags, ends=ends, segs=segs, anchor_h=anchor_h, K2=K2, WP=WP, M1=M1, M2=M2, NP=NP)

# ----------------------------------------------------------------------------------------------------- track height
GMAX, DGMAX = 0.05, 0.006
def profile(D):
    xs, zs, s, step, tags = D["xs"], D["zs"], D["s"], D["step"], D["tags"]
    n = len(xs) - 1
    g = ground(xs, zs)
    y999, y336 = RP[J999, 1], RP[J336, 1]
    g999, g336 = grade_at(J999), grade_at(J336)
    sta = np.array([t in ("North Shore", "Northwest", "Plaza", "East Lowland", "Airport") for t in tags])
    tun = np.array([t.startswith("T:") for t in tags])
    # variables: h[0..n+2] (index 0 = point 998, 1 = 999, ..., n+1 = 336, n+2 = 337), then u, v, t, r, c+, c-, q (per point 0..n)
    NH = n + 3; m = n + 1
    iu, iv, it, ir, icp, icm, iq = [NH + k * m for k in range(7)]
    NV = NH + 7 * m
    lb = np.full(NV, 0.0); ub = np.full(NV, np.inf); lb[:NH] = -50; ub[:NH] = 700
    def fix(k, val): lb[k] = ub[k] = val
    fix(0, RP[J999 - 1, 1]); fix(1, y999); fix(n + 1, y336); fix(n + 2, RP[J336 + 1, 1])
    for i in range(1, n):
        if s[i] <= PLAT + 0.1: fix(i + 1, y999 + s[i] * g999)
        if s[n] - s[i] <= PLAT + 0.1: fix(i + 1, y336 - (s[n] - s[i]) * g336)
    rows, cols, vals, rhs_ub = [], [], [], []
    erows, ecols, evals, rhs_eq = [], [], [], []
    def ub_row(entries, b):
        r = len(rhs_ub); rhs_ub.append(b)
        for c_, v_ in entries: rows.append(r); cols.append(c_); vals.append(v_)
    def eq_row(entries, b):
        r = len(rhs_eq); rhs_eq.append(b)
        for c_, v_ in entries: erows.append(r); ecols.append(c_); evals.append(v_)
    d998 = np.linalg.norm(RP[J999, [0, 2]] - RP[J999 - 1, [0, 2]]); d337 = np.linalg.norm(RP[J336 + 1, [0, 2]] - RP[J336, [0, 2]])
    sp = np.r_[d998, np.full(n, step), d337]          # spacing between h[k] and h[k+1]
    for k in range(NH - 1):
        gm = 0.0 if (1 <= k <= n and sta[k - 1] and sta[min(k, n)]) else GMAX
        if 1 <= k <= n:
            ub_row([(k + 1, 1), (k, -1)], gm * sp[k] + 1e-6); ub_row([(k + 1, -1), (k, 1)], gm * sp[k] + 1e-6)
    for k in range(1, NH - 1):                        # change of grade, and its absolute value (c+ - c-)
        i = k - 1
        e = [(k + 1, 1 / sp[k]), (k, -1 / sp[k] - 1 / sp[k - 1]), (k - 1, 1 / sp[k - 1])]
        ub_row(e, DGMAX); ub_row([(c_, -v_) for c_, v_ in e], DGMAX)
        if 0 <= i <= n: eq_row(e + [(icp + i, -1), (icm + i, 1)], 0.0)
    WDEV, WT, WR, WC, WQ = 0.05, float(os.environ.get("WT", 0.25)), 2.0, 30.0, 25.0
    cobj = np.zeros(NV)
    inside = in_poly(xs, zs); nearlift = seg_dist(xs, zs, LIFT_A, LIFT_B) < 12
    # the spur: within 25 m (past its first points) the loop must be at least 9 m below it
    dd, kk = _spur_tree.query(np.c_[xs, zs], k=12, distance_upper_bound=26.0)
    for i in range(m):
        k = i + 1
        eq_row([(k, 1), (iu + i, -1), (iv + i, 1)], g[i])                 # h - g = u - v
        ub_row([(k, -1), (it + i, -1)], 9.5 - g[i])                        # t >= g - h - 9.5
        ub_row([(k, 1), (ir + i, -1)], g[i] + 7.0)                         # r >= h - g - 7
        w_dev, w_t = (0.0, 0.0) if tun[i] else (WDEV, WT)
        if sta[i]: w_dev = 2.0                                               # platforms on the ground
        cobj[iu + i] = w_dev * 1.5; cobj[iv + i] = w_dev; cobj[it + i] = w_t; cobj[ir + i] = WR
        cobj[icp + i] = cobj[icm + i] = WC * step
        need = None
        if inside[i] or nearlift[i]: need = g[i] - 10.0                     # under the ski runs / lift: tunnel only
        ok = np.isfinite(dd[i]) & (kk[i] >= SPUR_EXEMPT)
        if np.any(ok):
            ys = SPUR[kk[i][ok], 1].min() - 9.5
            need = ys if need is None else min(need, ys)
        if need is not None:
            ub_row([(k, 1), (iq + i, -1)], need); cobj[iq + i] = WQ
    for name in ("North Shore", "Northwest", "Plaza", "East Lowland", "Airport"):
        idx = [i for i in range(m) if tags[i] == name]
        for a_, b_ in zip(idx[:-1], idx[1:]): eq_row([(a_ + 1, 1), (b_ + 1, -1)], 0.0)
    A_ub = sparse.csr_matrix((vals, (rows, cols)), shape=(len(rhs_ub), NV))
    A_eq = sparse.csr_matrix((evals, (erows, ecols)), shape=(len(rhs_eq), NV))
    res = optimize.linprog(cobj, A_ub=A_ub, b_ub=np.array(rhs_ub), A_eq=A_eq, b_eq=np.array(rhs_eq), bounds=np.c_[lb, ub], method="highs")
    if res.status != 0: raise RuntimeError("profile LP: " + res.message)
    h = res.x[1:n + 2]
    log("profile LP: objective %.1f, penalty q %.1f" % (res.fun, res.x[iq:iq + m].sum()))
    return h, g

# ----------------------------------------------------------------------------------------------------- checks and output
NAMES = ("North Shore", "Northwest", "Plaza", "East Lowland", "Airport")
def radius3(x, z, k=3):
    """Radius of the circle through points i-k, i, i+k (k*4 m either side); inf where straight."""
    R = np.full(len(x), np.inf)
    for i in range(k, len(x) - k):
        a = np.array([x[i - k], z[i - k]]); b = np.array([x[i], z[i]]); c = np.array([x[i + k], z[i + k]])
        ab, bc, ca = np.linalg.norm(b - a), np.linalg.norm(c - b), np.linalg.norm(a - c)
        cr = abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]))
        if cr > 1e-9: R[i] = ab * bc * ca / (2 * cr)
    return R

def finish(D, h, g):
    xs, zs, s, step, tags = D["xs"], D["zs"], D["s"], D["step"], D["tags"]
    n = len(xs) - 1
    kind = np.where(g - h > 9.5, "tunnel", np.where(h - g > 7.0, "trestle", "ground"))
    kind = np.where((g < 0.5) & (kind != "tunnel"), np.where(h - g > 7, "viaduct", "sea"), kind)
    # the loop list: from the Plaza station's middle round to just before it
    sidx = {}
    for name in NAMES:
        idx = [i for i in range(n + 1) if tags[i] == name]; sidx[name] = idx[len(idx) // 2]
    ip = sidx["Plaza"]
    NEW = np.c_[xs, h, zs]
    loop = np.vstack([NEW[ip:n], RP[J336:J999 + 1], NEW[1:ip]])
    kinds_new = list(kind)
    gold = ground(RP[J336:J999 + 1, 0], RP[J336:J999 + 1, 2]); hold = RP[J336:J999 + 1, 1]
    kold = np.where(gold - hold > 9.5, "tunnel", np.where(hold - gold > 7.0, "trestle", "ground"))
    lkind = kinds_new[ip:n] + list(kold) + kinds_new[1:ip]
    lnew = [True] * (n - ip) + [False] * (J999 - J336 + 1) + [True] * (ip - 1)
    def lidx_new(i): return i - ip if i >= ip else (n - ip) + (J999 - J336 + 1) + (i - 1)
    def lidx_old(k): return (n - ip) + (k - J336)
    stations = [(0, "Plaza")]
    for name in ("East Lowland", "Airport"): stations.append((lidx_new(sidx[name]), name))
    for k, name in INHERITED: stations.append((lidx_old(k), name))
    for name in ("North Shore", "Northwest"): stations.append((lidx_new(sidx[name]), name))
    stations.sort()
    spur = RP[SPUR0:]; spur_st = [(12, "Cirque Gate"), (1646 - SPUR0, "High Shelf"), (len(spur) - 1 - 12, "Summit")]
    with open(os.path.join(OUT, "WinterIsle.railnet"), "w") as f:
        f.write("# the Winter Isle railway network, from tools/railnet/graph/route.py (graph variant)\n")
        f.write("# LINE name closed|open; P x y z (track level, the top of the ballast), every 4 m in running order; S index Name (the station's middle)\n")
        f.write("# loop: clockwise; Leg A (Plaza -> East Lowland -> Airport -> South Shore) and Leg B (Cirque Gate -> North Shore -> Northwest -> Plaza) are new,\n")
        f.write("# South Shore .. Cirque Gate are the old line's points %d..%d unchanged; the spur is the old line's points %d..%d unchanged\n" % (J336, J999, SPUR0, len(RP) - 1))
        f.write("LINE loop closed\n")
        for p in loop: f.write("P %.2f %.2f %.2f\n" % (p[0], p[1], p[2]))
        for i, name in stations: f.write("S %d %s\n" % (i, name))
        f.write("LINE summit open\n")
        for p in spur: f.write("P %.2f %.2f %.2f\n" % (p[0], p[1], p[2]))
        for i, name in spur_st: f.write("S %d %s\n" % (i, name))
    # ---- metrics
    L = np.hypot(*np.diff(np.r_[loop, loop[:1]][:, [0, 2]], axis=0).T)
    gr_new = np.diff(h) / np.hypot(np.diff(xs), np.diff(zs))
    sta = np.array([t in NAMES for t in tags])
    stseg = sta[:-1] & sta[1:]
    plat = (s[:-1] < PLAT - 0.1) | (s[n] - s[1:] < PLAT - 0.1)
    out_g = np.abs(gr_new[~stseg])
    seg_old = np.hypot(*np.diff(RP[J336:J999 + 1][:, [0, 2]], axis=0).T); gr_old = np.diff(RP[J336:J999 + 1, 1]) / seg_old
    Rn = radius3(xs, zs); Ro = radius3(RP[J336 - 5:J999 + 6, 0], RP[J336 - 5:J999 + 6, 2])[5:-5]
    # radius across the joins too
    jx = np.r_[RP[J999 - 8:J999, 0], xs[:9]]; jz = np.r_[RP[J999 - 8:J999, 2], zs[:9]]; Rj999 = radius3(jx, jz)
    jx = np.r_[xs[-9:], RP[J336 + 1:J336 + 9, 0]]; jz = np.r_[zs[-9:], RP[J336 + 1:J336 + 9, 2]]; Rj336 = radius3(jx, jz)
    i_rn = int(np.argmin(Rn)); i_ro = int(np.argmin(Ro))
    def stretches(mask):
        out, i = [], 0
        while i < len(mask):
            if mask[i]:
                j = i
                while j + 1 < len(mask) and mask[j + 1]: j += 1
                out.append((i, j)); i = j + 1
            else: i += 1
        return out
    new_legs = {"Leg B (999 -> Plaza)": (0, ip), "Leg A (Plaza -> 336)": (ip, n)}
    tt = {}
    for kname in ("tunnel", "trestle", "viaduct", "sea"):
        m_ = kind == kname
        tt[kname] = {"total_m": round(float(np.sum(m_[1:] | m_[:-1]) * step if m_.any() else 0.0), 1),
                     "stretches": [{"from_m": round(float(s[a]), 0), "to_m": round(float(s[b]), 0), "length_m": round(float(s[b] - s[a] + step), 0),
                                    "at": [round(float(xs[(a + b) // 2]), 0), round(float(zs[(a + b) // 2]), 0)],
                                    "max_m": round(float(np.max((g - h)[a:b + 1] if kname == "tunnel" else (h - g)[a:b + 1])), 1),
                                    "leg": "B" if b <= ip else ("A" if a >= ip else "B/A")} for a, b in stretches(m_)]}
    # stations
    PX = np.array([[v[0], v[1], v[2]] for v in SITES.values()]); PN = list(SITES)
    st_out = []
    for name in NAMES:
        idx = [i for i in range(n + 1) if tags[i] == name]
        a, b = idx[0], idx[-1]
        dd = np.hypot(xs[idx, None] - PX[None, :, 0], zs[idx, None] - PX[None, :, 1]) - PX[None, :, 2]
        j = np.unravel_index(np.argmin(dd), dd.shape)
        c = D["st"][name]
        st_out.append({"name": name, "index": lidx_new(sidx[name]), "x": round(float(xs[sidx[name]]), 1), "z": round(float(zs[sidx[name]]), 1),
                       "heading_deg_from_east": round(math.degrees(c[3]), 1), "height": round(float(h[sidx[name]]), 2),
                       "platform_m": round(float(s[b] - s[a]), 1), "grade_over_platform": round(float((h[b] - h[a]) / (s[b] - s[a])), 4),
                       "ground_dev_max_m": round(float(np.max(np.abs(g[a:b + 1] - h[a:b + 1]))), 1),
                       "nearest_puzzle_edge_m": round(float(dd[j]), 1), "nearest_puzzle": PN[j[1]],
                       "serves": {k: round(float(math.hypot(xs[sidx[name]] - SITES[k][0], zs[sidx[name]] - SITES[k][1])), 0) for k in SERVES[name]}})
    for k, name in INHERITED:
        a, b = max(J336, k - 12), min(J999, k + 12)
        dd = np.hypot(RP[k, 0] - PX[:, 0], RP[k, 2] - PX[:, 1]) - PX[:, 2]
        if k == J999:   # half of its platform is on the new track
            gpl = (h[12] - RP[k - 12, 1]) / (s[12] + np.sum(seg_old[k - 12 - J336:k - J336]))
        elif k == J336:
            gpl = (RP[k + 12, 1] - h[n - 12]) / ((s[n] - s[n - 12]) + np.sum(seg_old[0:12]))
        else: gpl = (RP[b, 1] - RP[a, 1]) / np.sum(seg_old[a - J336:b - J336])
        st_out.append({"name": name, "index": lidx_old(k), "inherited": True, "x": round(float(RP[k, 0]), 1), "z": round(float(RP[k, 2]), 1),
                       "height": round(float(RP[k, 1]), 2), "grade_over_platform": round(float(gpl), 4),
                       "nearest_puzzle_edge_m": round(float(dd.min()), 1), "nearest_puzzle": PN[int(np.argmin(dd))]})
    # violations on the new track
    viol = []
    tunnel_new = kind == "tunnel"
    dd = np.hypot(xs[:, None] - CIRC[None, :, 0], zs[:, None] - CIRC[None, :, 1]) - CIRC[None, :, 2]
    for j in range(len(CIRC)):
        bad = np.where(dd[:, j] < 0)[0]
        if len(bad): viol.append({"rule": CLAB[j] + " keep-out %.1f m" % CIRC[j, 2], "points": len(bad), "worst_m": round(float(-dd[bad, j].min()), 1), "at_index": lidx_new(int(bad[np.argmin(dd[bad, j])]))})
    for r, mrg, lab in RECTS:
        dr = rect_dist(xs, zs, r) - mrg; bad = np.where(dr < 0)[0]
        if len(bad): viol.append({"rule": lab + " + %d m" % mrg, "points": len(bad), "worst_m": round(float(-dr.min()), 1)})
    ins = in_poly(xs, zs) & ~tunnel_new
    if ins.any(): viol.append({"rule": "ski runs outside a tunnel", "points": int(ins.sum()), "at_index": lidx_new(int(np.where(ins)[0][0]))})
    nl = (seg_dist(xs, zs, LIFT_A, LIFT_B) < 12) & ~tunnel_new
    if nl.any(): viol.append({"rule": "ski lift line at grade (12 m)", "points": int(nl.sum())})
    for i, name in [(i, nm) for nm in NAMES for i in range(n + 1) if tags[i] == nm]:
        dd2 = np.hypot(xs[i] - PX[:, 0], zs[i] - PX[:, 1]) - PX[:, 2]
        if dd2.min() < STATION_CLEAR: viol.append({"rule": "station %s within %d m of %s" % (name, STATION_CLEAR, PN[int(np.argmin(dd2))]), "worst_m": round(float(STATION_CLEAR - dd2.min()), 1)}); break
    sp_d, sp_k = _spur_tree.query(np.c_[xs, zs], k=20, distance_upper_bound=25.0)
    spur_bad, spur_jx = [], []
    for i in range(n + 1):
        for d_, k_ in zip(sp_d[i], sp_k[i]):
            if not np.isfinite(d_): continue
            dy = abs(h[i] - SPUR[k_, 1])
            if dy >= 9: continue
            if k_ < SPUR_EXEMPT:
                if d_ < 15: spur_jx.append((i, int(k_), d_, dy))
            else: spur_bad.append((i, int(k_), d_, dy))
    if spur_bad:
        w = min(spur_bad, key=lambda t: t[3])
        viol.append({"rule": "spur closer than 25 m and 9 m", "points": len(set(t[0] for t in spur_bad)), "worst": {"index": lidx_new(w[0]), "spur_point": SPUR0 + w[1], "horizontal_m": round(float(w[2]), 1), "vertical_m": round(float(w[3]), 1)}})
    if spur_jx: viol.append({"rule": "spur's platform within 15 m (junction)", "points": len(spur_jx)})
    od, ok_ = _old_tree.query(np.c_[xs, zs])
    farjoin = (s > 150) & (s < s[n] - 150)
    bad = np.where((od < 30) & farjoin)[0]
    if len(bad): viol.append({"rule": "within 30 m of the old loop 336..999", "points": len(bad), "worst_m": round(float(od[bad].min()), 1)})
    # leg A against leg B (away from the plaza)
    tA = cKDTree(np.c_[xs[ip:], zs[ip:]]); dAB, kAB = tA.query(np.c_[xs[:ip], zs[:ip]])
    farp = (np.abs(s[:ip] - s[ip]) > 200)
    sepAB = float(np.min(dAB[farp])) if farp.any() else None
    # joins
    def ang(v): return math.atan2(v[1], v[0])
    # tangent continuity: the new first (last) 4 m step against the old line's tangent at the join (central difference)
    e999 = math.degrees(abs(wrap(ang(np.array([xs[1] - xs[0], zs[1] - zs[0]])) - ang(tangent(J999)))))
    e336 = math.degrees(abs(wrap(ang(np.array([xs[n] - xs[n - 1], zs[n] - zs[n - 1]])) - ang(tangent(J336)))))
    # and the bend at the join point (old step on one side, new step on the other), beside the old line's own bend there
    t999 = math.degrees(abs(wrap(ang(np.array([xs[1] - xs[0], zs[1] - zs[0]])) - ang(RP[J999, [0, 2]] - RP[J999 - 1, [0, 2]]))))
    t336 = math.degrees(abs(wrap(ang(RP[J336 + 1, [0, 2]] - RP[J336, [0, 2]]) - ang(np.array([xs[n] - xs[n - 1], zs[n] - zs[n - 1]])))))
    o999 = math.degrees(abs(wrap(ang(RP[J999 + 1, [0, 2]] - RP[J999, [0, 2]]) - ang(RP[J999, [0, 2]] - RP[J999 - 1, [0, 2]]))))
    o336 = math.degrees(abs(wrap(ang(RP[J336 + 1, [0, 2]] - RP[J336, [0, 2]]) - ang(RP[J336, [0, 2]] - RP[J336 - 1, [0, 2]]))))
    gj999 = (gr_new[0], grade_at(J999)); gj336 = (gr_new[-1], grade_at(J336))
    # the sharpest bend between consecutive 4 m steps (a 60 m radius is about 3.8 degrees per step), on the new track
    # including the steps across both joins, and on the whole loop
    jx = np.r_[RP[J999 - 1, 0], xs, RP[J336 + 1, 0]]; jz = np.r_[RP[J999 - 1, 2], zs, RP[J336 + 1, 2]]
    hd = np.arctan2(np.diff(jz), np.diff(jx)); dturn = np.degrees(np.abs(wrap(np.diff(hd))))
    lp = np.r_[loop, loop[:1]]; hl = np.arctan2(np.diff(lp[:, 2]), np.diff(lp[:, 0])); lturn = np.degrees(np.abs(wrap(np.diff(np.r_[hl, hl[:1]]))))
    i_dt = int(np.argmax(dturn))
    M = {"ground_from": LAND_SRC, "loop_length_m": round(float(L.sum()), 1),
         "max_bend_per_4m_step_deg": {"new_track_and_joins": round(float(dturn.max()), 2), "at": [round(float(jx[i_dt + 1]), 0), round(float(jz[i_dt + 1]), 0)],
                                      "whole_loop": round(float(lturn.max()), 2), "note": "60 m radius = 3.82 deg per 4 m step"}, "loop_points": len(loop), "spacing_new_m": round(float(step), 4),
         "legs_m": {"Leg A (Plaza middle -> 336)": round(float(s[n] - s[ip]), 1), "existing 336 -> 999": round(float(np.sum(seg_old)), 1), "Leg B (999 -> Plaza middle)": round(float(s[ip]), 1)},
         "max_grade_new_outside_stations": round(float(out_g.max()), 4), "max_grade_existing_336_999": round(float(np.abs(gr_old).max()), 4),
         "min_radius_new_m": {"radius": round(float(Rn[i_rn]), 1), "index": lidx_new(i_rn), "at": [round(float(xs[i_rn]), 0), round(float(zs[i_rn]), 0)], "chord": "points i-3, i, i+3 (24 m)"},
         "min_radius_existing_336_999_m": {"radius": round(float(Ro[i_ro]), 1), "existing_index": J336 + i_ro, "at": [round(float(RP[J336 + i_ro, 0]), 0), round(float(RP[J336 + i_ro, 2]), 0)]},
         "min_radius_across_joins_m": {"999": round(float(np.min(Rj999)), 1), "336": round(float(np.min(Rj336)), 1)},
         "tunnel_new": tt["tunnel"], "trestle_new": tt["trestle"], "viaduct_or_sea_new": {"viaduct": tt["viaduct"], "low over sea": tt["sea"]},
         "stations": st_out, "keepout_violations": viol,
         "joins": {"999": {"tangent_error_deg": round(e999, 3), "bend_at_join_deg": round(t999, 3), "old_line_bend_there_deg": round(o999, 3), "grade_new": round(float(gj999[0]), 4), "grade_existing": round(float(gj999[1]), 4)},
                   "336": {"tangent_error_deg": round(e336, 3), "bend_at_join_deg": round(t336, 3), "old_line_bend_there_deg": round(o336, 3), "grade_new": round(float(gj336[0]), 4), "grade_existing": round(float(gj336[1]), 4)},
                   "note": "tangent_error: the new first/last 4 m step against the old line's tangent at the join (central difference); bend_at_join: the angle between the 4 m steps either side of the join point, beside the old line's own bend there; heights match exactly (the join points are the old points)"},
         "leg_A_B_min_separation_m": round(sepAB, 1) if sepAB is not None else None,
         "spur": {"start_existing_index": SPUR0, "points": len(spur), "loop_to_spur_start_m": round(float(np.min(np.hypot(xs - SPUR[0, 0], zs - SPUR[0, 2]))), 1),
                  "junction_exemption": "the spur's first %d points (60 m) need only 15 m horizontal clearance from the loop" % SPUR_EXEMPT,
                  "stations": [{"index": i, "name": nm} for i, nm in spur_st]},
         "segments": [{"name": sg[5], "mode": sg[4], "from": [round(float(sg[0][0]), 1), round(float(sg[0][1]), 1)], "to": [round(float(sg[2][0]), 1), round(float(sg[2][1]), 1)],
                       "target_height_at_end": round(float(D["anchor_h"][k + 1]), 1)} for k, sg in enumerate(D["segs"])]}
    json.dump(M, open(os.path.join(OUT, "metrics.json"), "w"), indent=1)
    log("loop %.0f m; leg B %.0f, leg A %.0f; max grade %.3f; min R new %.0f m at %s; tunnel %.0f m, trestle %.0f m; violations %d"
        % (M["loop_length_m"], s[ip], s[n] - s[ip], out_g.max(), Rn[i_rn], M["min_radius_new_m"]["at"], tt["tunnel"]["total_m"], tt["trestle"]["total_m"], len(viol)))
    for v in viol: log("  VIOLATION", v)
    for so in st_out: log("  station", so)
    plots(D, h, g, kind, loop, lkind, stations, spur, spur_st, ip)
    return M

SERVES = {"Plaza": ["TheTree"], "East Lowland": ["Lookouts", "SnowGlobe", "WhiteoutMaze"], "Airport": ["BlindSculptor", "FairyLights", "ThinIce"],
          "North Shore": ["ReindeerCount", "Footprints"], "Northwest": ["SleighRun", "BlizzardWalk"]}

def font(size):
    for f in ("arialbd.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try: return ImageFont.truetype(f, size)
        except OSError: pass
    return ImageFont.load_default()
KCOL = {"ground": (170, 20, 30), "tunnel": (25, 25, 25), "trestle": (255, 140, 0), "viaduct": (30, 110, 255), "sea": (30, 200, 255)}

def plots(D, h, g, kind, loop, lkind, stations, spur, spur_st, ip):
    S = 1.6                                            # pixels per 3 m cell
    gg = G[::-1]; Hh, Ww = gg.shape
    gy, gx = np.gradient(ndimage.gaussian_filter(gg, 1.0), CELL)
    shade = np.clip(0.85 + (-gx * 0.5 + gy * 0.7) * 0.35, 0.55, 1.2)
    img = np.zeros((Hh, Ww, 3)); img[:] = (0.12, 0.25, 0.40)
    land = gg > 0.3
    v = np.clip(gg / 600, 0, 1)
    base = np.stack([0.80 + 0.15 * v, 0.84 + 0.12 * v, 0.80 + 0.18 * v], -1) * shade[..., None]
    img[land] = base[land]
    for lv in range(50, 600, 50):
        img[np.abs(gg - lv) < 0.8] *= 0.82
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).resize((int(Ww * S), int(Hh * S)), Image.BILINEAR)
    dr = ImageDraw.Draw(im, "RGBA")
    def P(x, z): return ((x - X0) / CELL * S, (Hh - 1 - (z - Z0) / CELL) * S)
    def circ(x, z, r, **kw): a = P(x - r, z + r); b = P(x + r, z - r); dr.ellipse([a, b], **kw)
    f10, f12, f14, f20 = font(10), font(12), font(15), font(22)
    for gxm in range(-3000, -300, 250): dr.line([P(gxm, Z0), P(gxm, Z0 + NZ * CELL)], fill=(255, 255, 255, 60))
    for gzm in range(-2500, -450, 250): dr.line([P(X0, gzm), P(X0 + NX * CELL, gzm)], fill=(255, 255, 255, 60))
    # ski runs, airport, gondola, ski lift
    dr.polygon([P(*p) for p in SKI], outline=(0, 160, 200, 255), fill=(0, 160, 220, 28))
    for r, mrg, lab in RECTS:
        dr.rectangle([P(r[0], r[3]), P(r[1], r[2])], fill=(120, 120, 120, 200), outline=(60, 60, 60, 255))
        dr.rectangle([P(r[0] - mrg, r[3] + mrg), P(r[1] + mrg, r[2] - mrg)], outline=(60, 60, 60, 160))
    dr.line([P(*GONDOLA0), P(-2250, -1900)], fill=(220, 30, 30, 200), width=2)
    for p in PYLONS: circ(p[0], p[1], 15, outline=(220, 30, 30, 160))
    dr.line([P(*LIFT_A), P(*LIFT_B)], fill=(0, 140, 140, 255), width=2)
    for x, z, rr, lab in CIRCLES:
        if lab in ("gondola pylon",): continue
        circ(x, z, rr, outline=(150, 0, 150, 150))
    for nm, (x, z, r) in SITES.items():
        circ(x, z, r, fill=(200, 0, 200, 110), outline=(120, 0, 120, 255))
        dr.text((P(x, z)[0] + 6, P(x, z)[1] - 6), nm, fill=(90, 0, 90, 255), font=f10)
    for x, z in LOOKOUTS: circ(x, z, 7, fill=(200, 0, 200, 110))
    # the spur, then the loop coloured by kind
    for a, b in zip(spur[:-1], spur[1:]): dr.line([P(a[0], a[2]), P(b[0], b[2])], fill=(120, 60, 200, 255), width=3)
    for i in range(len(loop)):
        a, b = loop[i], loop[(i + 1) % len(loop)]
        k = lkind[i]
        col = KCOL[k]
        if k == "tunnel" and (i // 3) % 2: col = (200, 200, 200)
        dr.line([P(a[0], a[2]), P(b[0], b[2])], fill=col + (255,), width=5 if k != "tunnel" else 4)
    for i, nm in stations:
        x, y = P(loop[i, 0], loop[i, 2]); dr.rectangle([x - 6, y - 6, x + 6, y + 6], fill=(255, 255, 255), outline=(0, 0, 0), width=2)
        dr.text((x + 9, y - 22), nm, fill=(0, 0, 0), font=f14, stroke_width=3, stroke_fill=(255, 255, 255))
    for i, nm in spur_st:
        x, y = P(spur[i, 0], spur[i, 2]); dr.rectangle([x - 5, y - 5, x + 5, y + 5], fill=(220, 200, 255), outline=(60, 0, 120), width=2)
        dr.text((x + 8, y + 2), nm + " (spur)", fill=(60, 0, 120), font=f12, stroke_width=3, stroke_fill=(255, 255, 255))
    # legend
    lx, ly = 20, 20
    dr.rectangle([lx - 8, ly - 8, lx + 330, ly + 190], fill=(255, 255, 255, 225), outline=(0, 0, 0))
    dr.text((lx, ly), "Winter Isle railway: loop + summit spur (graph)", fill=(0, 0, 0), font=f14)
    for j, (k, lab) in enumerate([("ground", "loop on the ground"), ("tunnel", "loop in tunnel (ground > 9.5 m above)"), ("trestle", "loop on trestle (ground > 7 m below)"), ("viaduct", "over the sea")]):
        dr.line([(lx, ly + 30 + 22 * j), (lx + 40, ly + 30 + 22 * j)], fill=KCOL[k], width=5); dr.text((lx + 50, ly + 22 + 22 * j), lab, fill=(0, 0, 0), font=f12)
    dr.line([(lx, ly + 120), (lx + 40, ly + 120)], fill=(120, 60, 200), width=3); dr.text((lx + 50, ly + 112), "summit spur (unchanged)", fill=(0, 0, 0), font=f12)
    dr.text((lx, ly + 135), "magenta: puzzle sites and keep-out rings; red: gondola", fill=(0, 0, 0), font=f10)
    dr.text((lx, ly + 150), "teal: ski lift; blue area: ski runs; grey: airport (+20 m)", fill=(0, 0, 0), font=f10)
    dr.text((lx, ly + 165), "grid 250 m; x east, z north", fill=(0, 0, 0), font=f10)
    im.save(os.path.join(OUT, "plan.png"))
    # profile of the loop from the Plaza
    Ll = np.r_[0, np.cumsum(np.hypot(*np.diff(loop[:, [0, 2]], axis=0).T))]
    gl = ground(loop[:, 0], loop[:, 2])
    Wp, Hp, ml, mr, mt, mb = 2200, 700, 70, 20, 40, 60
    pim = Image.new("RGB", (Wp, Hp), (255, 255, 255)); pd = ImageDraw.Draw(pim)
    ymax = 150.0
    def Q(sv, y): return (ml + sv / Ll[-1] * (Wp - ml - mr), Hp - mb - (y + 5) / (ymax + 5) * (Hp - mt - mb))
    for yv in range(0, int(ymax) + 1, 10):
        pd.line([Q(0, yv), Q(Ll[-1], yv)], fill=(225, 225, 225) if yv % 50 else (180, 180, 180)); pd.text((5, Q(0, yv)[1] - 7), "%d m" % yv, fill=(0, 0, 0), font=f12)
    for sv in range(0, int(Ll[-1]) + 1, 500):
        pd.line([Q(sv, -5), Q(sv, ymax)], fill=(235, 235, 235)); pd.text((Q(sv, 0)[0] - 10, Hp - mb + 5), "%.1f km" % (sv / 1000), fill=(0, 0, 0), font=f12)
    poly = [Q(Ll[i], max(gl[i], -5)) for i in range(len(loop))]
    pd.polygon(poly + [Q(Ll[-1], -5), Q(0, -5)], fill=(215, 225, 215))
    pd.line(poly, fill=(110, 140, 110), width=1)
    pd.line([Q(0, 0), Q(Ll[-1], 0)], fill=(60, 120, 220), width=1)
    for i in range(len(loop) - 1):
        pd.line([Q(Ll[i], loop[i, 1]), Q(Ll[i + 1], loop[i + 1, 1])], fill=KCOL[lkind[i]], width=3)
    for i, nm in stations:
        x, y = Q(Ll[i], loop[i, 1]); pd.line([(x, y - 8), (x, y - 40)], fill=(0, 0, 0)); pd.text((x - 20, y - 58), nm, fill=(0, 0, 0), font=f12)
    pd.text((ml, 8), "Loop height profile from the Plaza (clockwise): track coloured by kind (dark red ground, black tunnel, orange trestle, blue over sea), ground shaded; existing 336..999 between South Shore and Cirque Gate", fill=(0, 0, 0), font=f12)
    pim.save(os.path.join(OUT, "profile.png"))

PASSES = int(os.environ.get("PASSES", 3))
def main():
    # a few passes: each routes with the target heights and stretch lengths the previous pass's track had (the first
    # pass interpolates them from the stations), so a single run from nothing settles on the same line
    state = {}
    for it in range(PASSES):
        log("---- pass %d" % (it + 1))
        D = build(state)
        h, g = profile(D)
        ah = {}
        for sg in D["segs"]:
            k = int(np.argmin(np.hypot(D["xs"] - sg[2][0], D["zs"] - sg[2][1]))); ah[sg[5]] = round(float(h[k]), 2)
        state["anchor_h"] = ah
    if os.environ.get("DUMP"):
        with open(os.path.join(OUT, "work_dump.txt"), "w") as f:
            for i in range(0, len(h), int(os.environ.get("DUMP"))):
                f.write("%5d s %6.0f  x %7.1f z %7.1f  g %6.1f h %6.1f  d %+6.1f  poly %d  %s\n" % (i, D["s"][i], D["xs"][i], D["zs"][i], g[i], h[i], h[i] - g[i], in_poly(D["xs"][i], D["zs"][i]), D["tags"][i]))
    finish(D, h, g)
    log("done")

if __name__ == "__main__":
    main()
