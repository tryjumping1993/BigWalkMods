"""The Winter Isle's fixed geometry, ported from the island's C# for the site checker (sitecheck.py).

Everything here is a straight port of unity/Assets/Editor/Puzzles/WinterIslePuzzle.cs (and WinterIsleAirport.cs,
WinterIsleRail.cs, LookoutsPuzzle.cs) or a reader of what the island's build wrote:
  unity/Land/WinterIsle.land                              heights (3 m grid) and the build's 'blocked' mask
  profile/BepInEx/plugins/WinterWalk/WinterIsle.railline  the railway as built (points, kinds, stations)
  profile/BepInEx/plugins/WinterWalk/WinterIsle.mineline  the mine's line and its stops
  unity/Land/WinterIsle.mine                              the mine tower
  unity/Assets/Puzzles/WinterIsle/WinterIsle.unity        the built scene: the lift lines' markers and the cabins
All functions take numpy arrays (or scalars) of world x and z and work element-wise. Unity's Mathf.PerlinNoise is
ported exactly (Ken Perlin's improved noise at z = 0, normalised as Unity does); selftest() checks it against the
ski puzzles' computed sites and the built lift markers.
"""
import math, os, re, struct
import numpy as np

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
LAND_FILE = os.path.join(REPO, "unity", "Land", "WinterIsle.land")
RAIL_FILE = os.path.join(REPO, "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.railline")
MINE_FILE = os.path.join(REPO, "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.mineline")
TOWER_FILE = os.path.join(REPO, "unity", "Land", "WinterIsle.mine")
SCENE_FILE = os.path.join(REPO, "unity", "Assets", "Puzzles", "WinterIsle", "WinterIsle.unity")
ISLE_ROOT = (-869.0, -909.5)        # WinterIslePuzzle.FixedSite: the pack's root (scene positions are relative to it)

# ------------------------------------------------------------------------------------------- Mathf, ported
_PERM = [151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10,
         23, 190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87,
         174, 20, 125, 136, 171, 168, 68, 175, 74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60, 211,
         133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89,
         18, 169, 200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226, 250, 124, 123, 5, 202,
         38, 147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213, 119, 248, 152,
         2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185,
         112, 104, 218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239, 107,
         49, 192, 214, 31, 181, 199, 106, 157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93, 222, 114, 67,
         29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180]
assert sorted(_PERM) == list(range(256))
_PP = np.array(_PERM + _PERM, dtype=np.int64)


def _grad(h, x, y):
    h = h & 15
    u = np.where(h < 8, x, y)
    v = np.where(h < 4, y, np.where((h == 12) | (h == 14), x, 0.0))
    return np.where(h & 1, -u, u) + np.where(h & 2, -v, v)


def perlin(x, y):
    """UnityEngine.Mathf.PerlinNoise(x, y): improved Perlin noise at z = 0, mapped by (n + 0.69) / (0.793 + 0.69)."""
    x = np.asarray(x, np.float64); y = np.asarray(y, np.float64)
    fx = np.floor(x); fy = np.floor(y)
    X = fx.astype(np.int64) & 255; Y = fy.astype(np.int64) & 255
    x = x - fx; y = y - fy
    u = x * x * x * (x * (x * 6 - 15) + 10); v = y * y * y * (y * (y * 6 - 15) + 10)
    A = _PP[X] + Y; AA = _PP[A]; AB = _PP[A + 1]
    B = _PP[X + 1] + Y; BA = _PP[B]; BB = _PP[B + 1]
    g00 = _grad(_PP[AA], x, y); g10 = _grad(_PP[BA], x - 1, y)
    g01 = _grad(_PP[AB], x, y - 1); g11 = _grad(_PP[BB], x - 1, y - 1)
    a = g00 + u * (g10 - g00); b = g01 + u * (g11 - g01)
    return (a + v * (b - a) + 0.69) / (0.793 + 0.69)


def smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def clamp01(t): return np.clip(t, 0.0, 1.0)
def lerp(a, b, t): return a + (b - a) * clamp01(t)        # Mathf.Lerp clamps t


def delta_angle(current, target):
    """Mathf.DeltaAngle in degrees."""
    d = np.mod(np.asarray(target, np.float64) - current, 360.0)
    return np.where(d > 180.0, d - 360.0, d)


def ridged(x, z):
    n = 1.0 - np.abs(perlin(x, z) * 2.0 - 1.0)
    return n * n


def soft_max(a, b, k):
    k = np.maximum(k, 1e-9)
    h = clamp01(0.5 + 0.5 * (a - b) / k)
    return b + (a - b) * h + k * h * (1.0 - h)


# ------------------------------------------------------------------------------------------- WinterIslePuzzle.cs
X0, X1, Z0, Z1, CELL = -3120.0, -360.0, -2700.0, -501.0, 3.0          # the island's box (line 32)
PLAZA = np.array([-705.0, -795.0]); PLAZA_RADIUS = 24.0                # line 40
PEAK = np.array([-2250.0, -1900.0]); PEAK_HEIGHT = 580.0; SUMMIT_FLAT = 26.0    # lines 102-103
SUMMIT = PEAK; SUMMIT_RADIUS = 21.0; SUMMIT_LIFT_REACH = 27.0          # lines 45-47
STEEP_RADIUS, SKI_RADIUS, SKI_BEARING, SKI_HALF_WIDTH = 650.0, 1450.0, 0.32, 0.95    # line 104
LAKE = np.array([-822.0, -1012.0]); LAKE_RADIUS = 32.0                 # lines 87-88
GORGE = np.array([(-868, -712), (-815, -668), (-760, -660), (-718, -632), (-690, -596)], float)    # line 89
CREEK = np.array([(-1010, -905), (-962, -938), (-915, -972), (-868, -998), (-822, -1012), (-790, -1052), (-772, -1100),
                  (-765, -1160)], float)                               # lines 92-96 (x, z of the bed)
SHORE_POINT = np.array([-468.0, -700.0])
TREE_LINE = 330.0
CAT_TRACK_ALONG, HALF_PIPE_ALONG = 925.0, 1170.0                       # lines 182-183
GONDOLA_SIDE = 2.0; SKI_LIFT_SIDE = 1.6
CIRQUES = [(1.75, 300.0, 210.0, 195.0), (3.1, 285.0, 190.0, 220.0), (4.3, 280.0, 180.0, 200.0)]
LESSER = [((-330.0, 215.0), 360.0, 400.0), ((95.0, -430.0), 470.0, 350.0), ((-470.0, -250.0), 220.0, 300.0)]
SADDLE_DROP = [0.2, 0.3, 0.2]
COULOIRS = [(1.42, 560, 40), (1.86, 470, 30), (2.62, 600, 46), (2.98, 430, 26), (3.55, 540, 38), (4.12, 500, 34), (4.62, 420, 28),
            (5.08, 610, 44), (5.52, 470, 30)]
SKI_STEPS = [(0, 28, 0), (35, 50, 6), (60, 36, 6), (150, 27, 14), (420, 13, 14), (470, 24, 14), (900, 15, 20), (1320, 6, 20)]
BAND_TOP, BAND_FOOT = 35.0, 150.0
DEG = math.pi / 180.0


def _xz(x, z): return np.asarray(x, np.float64), np.asarray(z, np.float64)


def ski_face(x, z):
    """WinterIslePuzzle.SkiFace: 1 on the middle of the ski face, 0 off it."""
    x, z = _xz(x, z)
    dx, dz = x - PEAK[0], z - PEAK[1]
    off = np.abs(delta_angle(np.degrees(np.arctan2(dz, dx)), SKI_BEARING / DEG)) * DEG
    off = off + (perlin(np.hypot(dx, dz) * 0.0065, 1.3 + np.where(dx * dz > 0.0, 0.0, 5.0)) - 0.5) * 0.24
    return 1.0 - smooth((off - SKI_HALF_WIDTH * 0.5) / (SKI_HALF_WIDTH * 0.5))


def rib(x, z):
    x, z = _xz(x, z)
    dx, dz = x - PEAK[0], z - PEAK[1]
    along = np.hypot(dx, dz)
    off = delta_angle(np.degrees(np.arctan2(dz, dx)), SKI_BEARING / DEG) * DEG
    off = off + (perlin(along * 0.004 + 7.1, 3.3) - 0.5) * 0.3
    r = np.maximum(1.0 - smooth(np.abs(off - 0.27) / 0.075), 1.0 - smooth(np.abs(off + 0.25) / 0.065))
    r = r * smooth((perlin(along * 0.006 + 1.7, np.where(off > 0.0, 9.9, 4.4)) - 0.38) / 0.12) * smooth((along - 540.0) / 80.0)
    return np.where((along < 540.0) | (along > SKI_RADIUS - 120.0), 0.0, r)


def run_swing(run, along):
    return 0.05 * np.sin(along / (115.0 if run == 1 else 100.0 + run * 12.0) + run * 2.1) * smooth((along - 300.0) / 200.0)


def run_offset(run, along):
    along = np.asarray(along, np.float64)
    main = run_swing(1, along) * (1.0 - smooth((along - (SKI_RADIUS - 230.0)) / 130.0))
    if run == 1: return main
    join = smooth((1010.0 - along) / 380.0) if run == 0 else smooth((SKI_RADIUS - 110.0 - along) / 420.0)
    return (-0.3 if run == 0 else 0.36) * join + lerp(main, run_swing(run, along), join)


def run_half(run, along):
    along = np.asarray(along, np.float64)
    return (20.0 if run == 1 else 14.0) * (0.35 + 1.2 * perlin(along * 0.0065 + run * 3.1, 0.5)) + 8.0 * smooth((along - (SKI_RADIUS - 330.0)) / 170.0)


def run_centre(run, along):
    """WinterIslePuzzle.RunCentre: (x, z) of run 0/1/2's middle at 'along' metres from the peak, and its half-width."""
    along = np.asarray(along, np.float64)
    angle = SKI_BEARING - run_offset(run, along)
    return np.stack([PEAK[0] + np.cos(angle) * along, PEAK[1] + np.sin(angle) * along], -1), run_half(run, along)


def _cat_track(along, off):
    lane = 1.0 - smooth((np.abs(along - CAT_TRACK_ALONG) - 3.5) / 2.5)
    left, right = run_offset(0, along), run_offset(2, along)
    return np.where(lane <= 0.0, 0.0, lane * smooth((off - left) / 0.02) * smooth((right - off) / 0.02))


def piste(x, z, with_bowl=True):
    """WinterIslePuzzle.Piste (with_bowl) / Groomed (not): 1 on a piste, 0 off it."""
    x, z = _xz(x, z)
    ski = ski_face(x, z)
    dx, dz = x - PEAK[0], z - PEAK[1]
    along = np.hypot(dx, dz)
    off = delta_angle(np.degrees(np.arctan2(dz, dx)), SKI_BEARING / DEG) * DEG
    open_ = (1.0 - smooth((along - 380.0) / 140.0)) if with_bowl else 0.0
    best = np.zeros_like(along)
    for run in range(3):
        across = np.abs(off - run_offset(run, along)) * along
        best = np.maximum(best, 1.0 - smooth((across - run_half(run, along)) / 6.0))
    best = np.maximum(best, _cat_track(along, off))
    out = ski * np.maximum(open_ * (1.0 - rib(x, z)), best)
    return np.where(ski <= 0.0, 0.0, out)


def piste_finish():
    """PisteFinish: the finish arch's middle, the run's direction there, and the run's half-width."""
    mid, half = run_centre(1, SKI_RADIUS - 110.0)
    nxt, _ = run_centre(1, SKI_RADIUS - 104.0)
    d = nxt - mid
    return mid, d / np.linalg.norm(d), float(half)


def ski_lift_ends():
    finish, down, half = piste_finish()
    bottom = finish + np.array([down[1], -down[0]]) * (min(half, 24.0) + 44.0) - down * 30.0
    along = 478.0
    angle = SKI_BEARING - (run_offset(1, along) + run_offset(2, along)) * 0.5
    top = PEAK + np.array([math.cos(angle), math.sin(angle)]) * along
    return bottom, top


def gondola_ends():
    d = (SUMMIT - PLAZA) / np.linalg.norm(SUMMIT - PLAZA)
    return PLAZA + d * 55.0, SUMMIT - d * 12.0


def line_towers(a, b, span):
    """Pylons of a lift built as section 11 / 11b builds them: ceil(length / span) spans, towers between."""
    n = max(2, math.ceil(np.linalg.norm(b - a) / span))
    return np.array([a + (b - a) * (i / n) for i in range(1, n)])


def lodge():
    """Section 11 (piste furniture): the lodge's middle and the hut beside it."""
    mid, half = run_centre(1, SKI_RADIUS - 110.0)
    nxt, _ = run_centre(1, SKI_RADIUS - 104.0)
    d = (nxt - mid) / np.linalg.norm(nxt - mid); side = np.array([d[1], -d[0]])
    at = mid + side * (min(float(half), 24.0) + 17.0) + d * 14.0
    return at, at + d * 16.0 + side * 4.0


def terrain_park():
    """Section 11c: the three kickers' lips and the half-pipe's centre line (on run 2)."""
    pts = []
    for along, offset, lip, length, width in ((985.0, -0.25, 1.0, 6.0, 4.0), (1035.0, 0.25, 1.4, 7.0, 4.5), (1075.0, -0.2, 0.8, 5.0, 3.5)):
        mid, half = run_centre(2, along)
        nxt, _ = run_centre(2, along + 6.0)
        down = (nxt - mid) / np.linalg.norm(nxt - mid); across = np.array([down[1], -down[0]])
        foot = mid + across * (offset * float(half))
        pts.append(foot); pts.append(foot + down * length)
    pipe, _ = run_centre(2, np.arange(HALF_PIPE_ALONG, HALF_PIPE_ALONG + 60.1, 4.0))
    return np.array(pts), pipe


_ski_table = None
def _ski_table_fn(metres):
    global _ski_table
    if _ski_table is None:
        n = int(math.ceil(SKI_RADIUS)) + 2
        drop = np.zeros(n); total = 0.0
        for m in range(n - 1):
            at = m + 0.5; deg = SKI_STEPS[0][1]
            for g in range(1, len(SKI_STEPS)):
                deg += (SKI_STEPS[g][1] - SKI_STEPS[g - 1][1]) * float(smooth((at - SKI_STEPS[g][0] + SKI_STEPS[g][2] * 0.5) / SKI_STEPS[g][2]))
            if m < SKI_RADIUS: total += math.tan(deg * DEG)
            drop[m + 1] = total
        _ski_table = np.maximum(0.0, 1.0 - drop / total)
    f = np.clip(metres, 0.0, len(_ski_table) - 1.001)
    i = f.astype(np.int64)
    return _ski_table[i] + (_ski_table[i + 1] - _ski_table[i]) * (f - i)


def _ski_profile(u, gap):
    metres = clamp01(u) * SKI_RADIUS
    share = _ski_table_fn(metres)
    band = _ski_table_fn(np.full_like(metres, BAND_TOP)) + (_ski_table_fn(np.full_like(metres, BAND_FOOT)) - _ski_table_fn(np.full_like(metres, BAND_TOP))) * clamp01((metres - BAND_TOP) / (BAND_FOOT - BAND_TOP))
    use = (gap > 0.0) & (metres > BAND_TOP) & (metres < BAND_FOOT)
    return np.where(use, share + (band - share) * clamp01(gap), share)


def mountain_height(x, z):
    """WinterIslePuzzle.MountainHeight, line for line."""
    x, z = _xz(x, z)
    x = np.atleast_1d(x).astype(np.float64); z = np.atleast_1d(z).astype(np.float64)
    ski = ski_face(x, z)
    fromPeak = np.hypot(x - PEAK[0], z - PEAK[1])
    push = 230.0 * (1.0 - ski) * smooth(fromPeak / 260.0)
    qx = x + (perlin(x * 0.0021 + 5.5, z * 0.0021 + 1.1) - 0.5) * push
    qz = z + (perlin(x * 0.0021 + 9.2, z * 0.0021 + 6.6) - 0.5) * push
    dqx, dqz = qx - PEAK[0], qz - PEAK[1]
    cdx, cdz = -math.sin(SKI_BEARING), math.cos(SKI_BEARING)
    alongCrest = np.clip(dqx * cdx + dqz * cdz, -95.0, 85.0)
    dqcx, dqcz = dqx - cdx * alongCrest, dqz - cdz * alongCrest
    crestDrop = 26.0 * (alongCrest / 90.0) ** 2 + 9.0 * np.abs(np.sin(alongCrest * 0.07 + 1.1)) * smooth(np.abs(alongCrest) / 30.0)
    d = np.maximum(0.0, np.hypot(dqcx, dqcz) - SUMMIT_FLAT)
    radius = STEEP_RADIUS + (SKI_RADIUS - STEEP_RADIUS) * clamp01(ski)
    t = clamp01(1.0 - d / radius)
    h = (PEAK_HEIGHT - crestDrop) * np.power(t, 1.45)
    skiOff = delta_angle(np.degrees(np.arctan2(z - PEAK[1], x - PEAK[0])), SKI_BEARING / DEG) * DEG
    # the ski face
    u = clamp01(d / SKI_RADIUS + 0.02 * np.sin(skiOff * 5.0 + 1.0) * np.sin(clamp01(d / SKI_RADIUS) * math.pi))
    alongFace = np.hypot(dqx, dqz)
    gap = 1.0 - smooth((np.abs(skiOff * alongFace) - 16.0) / 12.0)
    skiH = PEAK_HEIGHT * _ski_profile(u, gap)
    benchHere = smooth((alongFace - 440.0) / 20.0) * (1.0 - smooth((alongFace - 500.0) / 20.0))
    skiH = skiH + 1.0 * np.sin(alongFace * (6.2831853 / 85.0) + (perlin(alongFace * 0.004, skiOff * 2.0 + 3.0) - 0.5) * 4.0) \
        * smooth((alongFace - 160.0) / 60.0) * (1.0 - smooth((alongFace - 1290.0) / 60.0)) * (1.0 - 0.85 * benchHere) \
        * (0.6 + 0.8 * perlin(skiOff * 4.0 + 1.7, alongFace * 0.003))
    armRise = smooth((alongFace - 50.0) / 110.0) * (1.0 - smooth((alongFace - 950.0) / 320.0)) * (1.0 - 0.55 * smooth((alongFace - 450.0) / 450.0))
    skiH = skiH + 110.0 * np.exp(-((skiOff + 0.55) / 0.11) ** 2) * armRise * (0.72 + 0.5 * perlin(alongFace * 0.011, 4.2))
    skiH = skiH + 80.0 * np.exp(-((skiOff - 0.57) / 0.1) ** 2) * armRise * (0.72 + 0.5 * perlin(alongFace * 0.011, 9.6))
    h = np.where(ski > 0.0, h + (skiH - h) * clamp01(ski), h)
    # aretes and cirques
    angle = np.arctan2(dqz, dqx)
    spur = np.power(0.5 + 0.5 * np.cos(angle * 5.0 + 1.3 + (perlin(qx * 0.003, qz * 0.003) - 0.5) * 3.0), 2.2)
    carve = np.sin(clamp01(t) * math.pi)
    h = h * (1.0 - (1.0 - spur) * 0.16 * carve * (1.0 - ski))
    # couloirs (only where ski < 1 and h > 40, as the C# tests once before the loop)
    doC = (ski < 1.0) & (h > 40.0)
    r = np.hypot(dqx, dqz)
    for bearing, end, depth in COULOIRS:
        wander = bearing + 0.07 * np.sin(r / 95.0 + bearing * 3.0)
        lateral = delta_angle(np.degrees(angle), np.degrees(wander)) * DEG * r
        width = 8.0 + r * 0.045
        m = doC & (np.abs(lateral) <= width * 4.0)
        cut = depth * smooth((r - 70.0) / 150.0) * (1.0 - smooth((r - end) / 80.0))
        h2 = h - cut * np.exp(-(lateral / width) ** 2) * (1.0 - ski)
        fan = 20.0 * np.exp(-(lateral / (width * 2.6)) ** 2) * np.exp(-((r - end - 60.0) / 75.0) ** 2)
        h2 = h2 + fan * (1.0 - ski) * smooth(h2 / 60.0)
        h = np.where(m, h2, h)
    for bearing, dist, cr, floor in CIRQUES:
        ax, az = PEAK[0] + math.cos(bearing) * dist, PEAK[1] + math.sin(bearing) * dist
        cd = np.hypot(qx - ax, qz - az)
        bowl = floor + np.power(cd / cr, 2.4) * 420.0 + (perlin(qx * 0.02, qz * 0.02) - 0.5) * 18.0
        h = np.where(cd < cr * 1.4, h + (np.minimum(h, bowl) - h) * clamp01(1.0 - ski), h)
    for lp, ((ox, oz), lh, lr) in enumerate(LESSER):
        lx, lz = qx - PEAK[0] - ox, qz - PEAK[1] - oz
        on = math.hypot(ox, oz); outx, outz = ox / on, oz / on; acx, acz = -outz, outx
        do_ = lx * outx + lz * outz; dc = lx * acx + lz * acz
        if lp == 0: ld = np.hypot(do_, dc / 2.3)
        elif lp == 2: ld = np.hypot(do_ * np.where(do_ > 0.0, 1.8, 0.85), dc)
        else: ld = np.hypot(lx, lz)
        xx = clamp01(ld / lr)
        if lp == 1: shape = np.power(1.0 - xx, 1.3) * (1.0 - 0.06 * np.exp(-xx * 14.0))
        elif lp == 0: shape = 1.0 - np.power(xx, 0.85) * (0.75 + 0.25 * xx)
        else: shape = np.power(1.0 - xx, 1.1) * (1.0 - 0.12 * np.exp(-xx * 9.0))
        lesserH = lh * shape * (1.0 - ski); lesserSoft = 45.0 * smooth(lesserH / 60.0)
        hl = np.where(lesserSoft > 0.01, soft_max(h, lesserH, lesserSoft), np.maximum(h, lesserH))
        h = np.where(xx < 1.0, hl, h)
        relx, relz = qx - PEAK[0], qz - PEAK[1]
        f = clamp01((relx * ox + relz * oz) / (ox * ox + oz * oz))
        beside = np.hypot(relx - ox * f, relz - oz * f)
        crest = lh * (1.0 - SADDLE_DROP[lp] * np.sin(f * math.pi)) + (PEAK_HEIGHT - lh) * (1.0 - f) * (1.0 - f)
        crest = crest + (perlin(f * 7.0 + lh, 3.7) - 0.5) * 56.0 * np.sin(f * math.pi)
        ridgeH = (crest - beside * 1.15) * (1.0 - ski)
        ridgeSoft = 30.0 * smooth(ridgeH / 40.0)
        h = np.where(ridgeSoft > 0.01, soft_max(h, ridgeH, ridgeSoft), np.maximum(h, ridgeH))
    low = h <= 0.5
    rough = (ridged(x * 0.006 + 11.3, z * 0.006 + 4.9) - 0.4) * 42.0 + (ridged(x * 0.017 + 2.1, z * 0.017 + 7.7) - 0.45) * 4.0 + (ridged(x * 0.05 + 6.3, z * 0.05 + 1.2) - 0.5) * 1.5
    roll = (perlin(x * 0.004 + 3.3, z * 0.004 + 8.8) - 0.5) * 6.0
    body = smooth(h / 60.0) * smooth(fromPeak / 150.0)
    rb = rib(x, z) * smooth((h - TREE_LINE + 40.0) / 60.0)
    clear = np.full_like(x, np.inf)
    for run in range(3):
        c, half = run_centre(run, fromPeak)
        clear = np.minimum(clear, np.hypot(x - c[..., 0], z - c[..., 1]) - half)
    spurH = 18.0 * smooth((clear - 8.0) / 55.0) * (0.6 + 0.8 * perlin(x * 0.006 + 3.1, z * 0.006 + 7.9))
    spurH = spurH * (1.0 - (1.0 - smooth((np.abs(fromPeak - CAT_TRACK_ALONG) - 6.0) / 14.0)))
    h = np.where(ski > 0.0, h + ski * spurH * smooth((fromPeak - 480.0) / 160.0) * (1.0 - smooth((fromPeak - 1250.0) / 120.0)), h)
    mix = ski * (1.0 - rb)
    out = np.maximum(0.0, h + (rough + (roll - rough) * clamp01(mix)) * body + rb * ski * 16.0 * body)
    return np.where(low, np.maximum(0.0, h), out)


def to_polyline(x, z, line):
    """Distance from points to a polyline of (x, z) (WinterIslePuzzle.ToLine)."""
    x, z = _xz(x, z)
    best = np.full(np.broadcast(x, z).shape, np.inf)
    for (ax, az), (bx, bz) in zip(line[:-1], line[1:]):
        abx, abz = bx - ax, bz - az
        t = np.clip(((x - ax) * abx + (z - az) * abz) / max(abx * abx + abz * abz, 1e-4), 0.0, 1.0)
        best = np.minimum(best, np.hypot(x - (ax + abx * t), z - (az + abz * t)))
    return best


# ------------------------------------------------------------------------------------------- WinterIsleAirport.cs
RUNWAY = np.array([-870.0, -1855.0]); HEADING = 180.0; RUNWAY_HALF_LENGTH, RUNWAY_HALF_WIDTH = 160.0, 22.0
APRON_U0, APRON_U1, APRON_V0, APRON_V1 = -80.0, 75.0, -100.0, -20.0
AIRPORT_BLEND = 18.0; AIRPORT_LEVEL = 21.0
STRIP_BEARING, STRIP_FROM, STRIP_TO, STRIP_HALF = 308.0, 34.0, 168.0, 10.0
_F = np.array([math.sin(HEADING * DEG), math.cos(HEADING * DEG)]); _R = np.array([_F[1], -_F[0]])
_SD = np.array([math.sin(STRIP_BEARING * DEG), math.cos(STRIP_BEARING * DEG)]); _SR = np.array([_SD[1], -_SD[0]])


def _uv(x, z):
    dx, dz = x - RUNWAY[0], z - RUNWAY[1]
    return dx * _F[0] + dz * _F[1], dx * _R[0] + dz * _R[1]


def airport_outside(x, z):
    """WinterIsleAirport.Outside: how far outside the levelled runway and apron (negative inside; a Chebyshev measure)."""
    u, v = _uv(*_xz(x, z))
    runway = np.maximum(np.abs(u) - RUNWAY_HALF_LENGTH, np.abs(v) - RUNWAY_HALF_WIDTH)
    apron = np.maximum(np.maximum(APRON_U0 - u, u - APRON_U1), np.maximum(APRON_V0 - v, v - APRON_V1))
    return np.minimum(runway, apron)


def _box_dist(a, b, h0, h1):
    """Euclidean distance outside an axis box [-?]: a in [lo..hi] etc. (helper for airport_distance)."""
    da = np.maximum(np.maximum(h0[0] - a, a - h1[0]), 0.0); db = np.maximum(np.maximum(h0[1] - b, b - h1[1]), 0.0)
    return np.hypot(da, db)


def airport_distance(x, z):
    """Euclidean distance to the runway or apron rectangles (0 inside)."""
    u, v = _uv(*_xz(x, z))
    return np.minimum(_box_dist(u, v, (-RUNWAY_HALF_LENGTH, -RUNWAY_HALF_WIDTH), (RUNWAY_HALF_LENGTH, RUNWAY_HALF_WIDTH)),
                      _box_dist(u, v, (APRON_U0, APRON_V0), (APRON_U1, APRON_V1)))


def strip_outside(x, z):
    """WinterIsleAirport.StripOutside: the summit airstrip's deck (Chebyshev, negative inside)."""
    x, z = _xz(x, z)
    dx, dz = x - SUMMIT[0], z - SUMMIT[1]
    r = dx * _SD[0] + dz * _SD[1]; s = dx * _SR[0] + dz * _SR[1]
    return np.maximum(np.maximum(STRIP_FROM - r, r - STRIP_TO), np.abs(s) - STRIP_HALF)


def strip_distance(x, z):
    x, z = _xz(x, z)
    dx, dz = x - SUMMIT[0], z - SUMMIT[1]
    r = dx * _SD[0] + dz * _SD[1]; s = dx * _SR[0] + dz * _SR[1]
    return _box_dist(r, s, (STRIP_FROM, -STRIP_HALF), (STRIP_TO, STRIP_HALF))


def airport_tower():
    return RUNWAY + _F * 58.0 + _R * -48.0


# ------------------------------------------------------------------------------------------- LookoutsPuzzle.cs
LOOKOUT_STATIONS = np.array([(-1400.0, -980.0), (-1250.0, -2000.0), (-2244.5, -1915.0), (-2750.0, -2120.0)])
LOOKOUT_RADIUS = 7.0
LOOKOUT_EXTRA = [(-1400.0, -980.0, 7.0), (-1250.0, -2000.0, 7.0), (-2750.0, -2120.0, 7.0)]    # ExtraSites: all but the third


# ------------------------------------------------------------------------------------------- files the build wrote
class Land:
    """WinterIsle.land: '<fffii' x0 z0 cell nx nz, (nx+1)*(nz+1) float32 heights (row-major by z, NaN off the island),
    then the same count of bytes: the build's 'blocked' mask (paths, structures, rail bed, airport, rocks)."""
    def __init__(self, path=LAND_FILE):
        d = open(path, "rb").read()
        self.x0, self.z0, self.cell, self.nx, self.nz = struct.unpack("<fffii", d[:20])
        self.W, self.H = self.nx + 1, self.nz + 1
        n = self.W * self.H
        self.h = np.frombuffer(d, dtype="<f4", count=n, offset=20).reshape((self.H, self.W)).astype(np.float64)
        self.blocked = np.frombuffer(d, dtype=np.uint8, count=n, offset=20 + 4 * n).reshape((self.H, self.W)).astype(bool) if len(d) >= 20 + 5 * n else np.zeros((self.H, self.W), bool)
        self.g = np.where(np.isnan(self.h), -6.0, self.h)       # sea and off-grid as -6 for sampling
        self.xs = self.x0 + np.arange(self.W) * self.cell
        self.zs = self.z0 + np.arange(self.H) * self.cell

    def sample(self, x, z, arr=None):
        """Bilinear sample (the build's GridHeight); outside the grid = -6."""
        a = self.g if arr is None else arr
        x, z = _xz(x, z)
        fx = (x - self.x0) / self.cell; fz = (z - self.z0) / self.cell
        out = (fx < 0) | (fz < 0) | (fx > self.nx) | (fz > self.nz)
        fx = np.clip(fx, 0, self.nx - 0.001); fz = np.clip(fz, 0, self.nz - 0.001)
        i = fx.astype(np.int64); j = fz.astype(np.int64); tx = fx - i; tz = fz - j
        v = (a[j, i] * (1 - tx) + a[j, i + 1] * tx) * (1 - tz) + (a[j + 1, i] * (1 - tx) + a[j + 1, i + 1] * tx) * tz
        return np.where(out, -6.0, v)

    def cell_of(self, x, z):
        i = np.clip(np.rint((np.asarray(x) - self.x0) / self.cell).astype(np.int64), 0, self.nx)
        j = np.clip(np.rint((np.asarray(z) - self.z0) / self.cell).astype(np.int64), 0, self.nz)
        return j, i


class Rail:
    """WinterIsle.railline: per line its points (track middles), kinds (0 ground, 1 tunnel, 2 trestle), track heights,
    stations; and per point the derived 'inside' (tunnel metres from the nearest mouth) and near-station flag."""
    STATION_REACH = 10; STATION_HALF = 11.0; BED_HALF = 4.8; TUNNEL_HIGH = 6.4; TUNNEL_HALF = 5.2

    def __init__(self, path=RAIL_FILE):
        self.lines = []
        cur = None
        for raw in open(path):
            q = raw.split()
            if not q or q[0].startswith("#"): continue
            if q[0] == "LINE":
                cur = dict(name=q[1], closed=len(q) > 2 and q[2] == "closed", p=[], kind=[], stops=[], names=[]); self.lines.append(cur)
            elif q[0] == "P" and cur is not None:
                if len(q) >= 8:
                    cur["p"].append(((float(q[1]) + float(q[4])) / 2, (float(q[2]) + float(q[5])) / 2, (float(q[3]) + float(q[6])) / 2)); cur["kind"].append(int(q[7]))
                else:
                    cur["p"].append((float(q[1]), float(q[2]), float(q[3]))); cur["kind"].append(int(q[4]))
            elif q[0] == "S" and cur is not None:
                cur["stops"].append(int(q[1])); cur["names"].append(" ".join(q[2:]))
        P, K, Y, INS, NEAR, LID = [], [], [], [], [], []
        self.stations = []          # (name, x, z, line name, index)
        for li, l in enumerate(self.lines):
            p = np.array(l["p"]); k = np.array(l["kind"]); n = len(p)
            l["p"] = p; l["kind"] = k
            idx = np.arange(n)
            # metres inside a tunnel from its nearest mouth (WinterIsleRail.Classify's Inside, 4 m per point)
            ins = np.where(k == 1, 1e6, 0.0)
            for _ in range(2 if l["closed"] else 1):
                for i in range(1, n + (1 if l["closed"] else 0)):
                    a, b = i % n, (i - 1) % n
                    if k[a] == 1: ins[a] = min(ins[a], ins[b] + 4.0)
                for i in range(n - 2 + (1 if l["closed"] else 0), -1, -1):
                    a, b = i % n, (i + 1) % n
                    if k[a] == 1: ins[a] = min(ins[a], ins[b] + 4.0)
            near = np.zeros(n, bool)
            for s in l["stops"]:
                d = np.abs(idx - s)
                if l["closed"]: d = np.minimum(d, n - d)
                near |= d <= self.STATION_REACH + 3
            l["inside"] = ins; l["near"] = near
            P.append(p[:, [0, 2]]); Y.append(p[:, 1]); K.append(k); INS.append(ins); NEAR.append(near); LID.append(np.full(n, li))
            for s, name in zip(l["stops"], l["names"]):
                self.stations.append((name, p[s, 0], p[s, 2], l["name"], s))
        self.xz = np.concatenate(P); self.y = np.concatenate(Y); self.kind = np.concatenate(K)
        self.inside = np.concatenate(INS); self.near = np.concatenate(NEAR); self.line_id = np.concatenate(LID)
        # segments (each point to the next on its line; a closed line joins its last point to its first)
        A, B, SK, SI = [], [], [], []
        off = 0
        for l in self.lines:
            n = len(l["p"]); i = np.arange(n - (0 if l["closed"] else 1)); j = (i + 1) % n
            A.append(self.xz[off + i]); B.append(self.xz[off + j]); SI.append(off + i)
            off += n
        self.sa = np.concatenate(A); self.sb = np.concatenate(B); self.si = np.concatenate(SI)
        # classes for clearances: ground (0), deep tunnel (1, inside >= 18 m: Keep() 0), portal (1, inside < 18 m: Keep() 11), trestle (2)
        self.cls = np.where(self.kind == 1, np.where(self.inside < 18.0, 3, 1), self.kind)      # 3 = portal
        self.cls_names = {0: "ground", 1: "tunnel (deep)", 2: "trestle", 3: "tunnel portal"}

    def summit_lift_foot(self):
        """WinterIsleRail.SummitLiftFoot: 8 m beyond the open line's last point."""
        l = [x for x in self.lines if not x["closed"]][-1]
        p = l["p"][:, [0, 2]]
        d = p[-1] - p[-4]
        return p[-1] + d / np.linalg.norm(d) * 8.0

    def tower_spot(self):
        """WinterIsleRail.TowerSpot: beside the Plaza station, on the plaza's side, StationHalf + 9 out."""
        for l in self.lines:
            for s, name in zip(l["stops"], l["names"]):
                if name.startswith("Plaza"):
                    p = l["p"][:, [0, 2]]; n = len(p)
                    fw = p[(s + 1) % n] - p[(s - 1) % n]; side = np.array([fw[1], -fw[0]]) / np.linalg.norm(fw)     # Cross(up, Fw) = (fz, -fx)
                    c = p[s]
                    if np.dot(side, PLAZA - c) < 0: side = -side
                    return p[(s + self.STATION_REACH // 2) % n] + side * (self.STATION_HALF + 9.0)
        return None


def read_mine(path=MINE_FILE, tower=TOWER_FILE):
    """The mine's stops from WinterIsle.mineline ('x y z kind stop'): returns (stations [(x, z)], hall (x, z)); the first
    station is the mine tower's (WinterIsleMine.Stations[0]; the tower itself is in WinterIsle.mine)."""
    pts = []
    for raw in open(path):
        q = raw.split()
        if len(q) == 5 and q[0] not in ("#", "L"): pts.append((float(q[0]), float(q[2]), int(q[3]), int(q[4])))
    st = [(x, z) for x, z, k, s in pts if s == 1 and k != 2]
    hall = [(x, z) for x, z, k, s in pts if s == 1 and k == 2]
    tw = None
    if os.path.exists(tower):
        v = open(tower).read().split(); tw = (float(v[0]), float(v[1]))
    return np.array(st), (hall[0] if hall else tuple(SUMMIT)), tw


def read_scene(path=SCENE_FILE, root=ISLE_ROOT):
    """World (x, z) of named objects in the island's built scene: the lift lines' markers and the cabins.
    Returns {"Gondola Line": [(x, z) P0..], "Ski Lift Line": [...], "Summit Lift Line": [...], "Cabin": [(x, z, sx, sz)]}."""
    text = open(path, encoding="utf-8", errors="replace").read()
    docs = re.split(r"^--- !u!(\d+) &(\d+).*$", text, flags=re.M)
    go_name, tr = {}, {}
    box = {}
    for k in range(1, len(docs) - 2, 3):
        cls, fid, body = docs[k], docs[k + 1], docs[k + 2]
        if cls == "1":
            m = re.search(r"m_Name: (.*)", body)
            if m: go_name[fid] = m.group(1).strip()
        elif cls == "4":
            g = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
            pos = re.search(r"m_LocalPosition: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}", body)
            fa = re.search(r"m_Father: \{fileID: (\d+)\}", body)
            if g and pos: tr[fid] = (g.group(1), (float(pos.group(1)), float(pos.group(3))), fa.group(1) if fa else "0")
        elif cls == "65":      # BoxCollider
            g = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
            sz = re.search(r"m_Size: \{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}", body)
            if g and sz: box[g.group(1)] = (float(sz.group(1)), float(sz.group(3)))
    by_go = {v[0]: fid for fid, v in tr.items()}
    out = {"Gondola Line": {}, "Ski Lift Line": {}, "Summit Lift Line": {}, "Cabin": []}
    for fid, (g, pos, fa) in tr.items():
        name = go_name.get(g, "")
        parent = go_name.get(tr[fa][0], "") if fa in tr else ""
        if parent in out and parent != "Cabin" and re.fullmatch(r"P\d+", name):
            out[parent][int(name[1:])] = (root[0] + pos[0], root[1] + pos[1])      # (the line's parent sits at the root's origin)
        elif name == "Cabin" and parent == "Cabin Walls":
            sx, sz = box.get(g, (7.0, 8.0))
            out["Cabin"].append((root[0] + pos[0], root[1] + pos[1], sx, sz))
    for k in ("Gondola Line", "Ski Lift Line", "Summit Lift Line"):
        out[k] = np.array([out[k][i] for i in sorted(out[k])]) if out[k] else np.zeros((0, 2))
    out["Cabin"] = np.array(out["Cabin"]) if out["Cabin"] else np.zeros((0, 4))
    return out


# ------------------------------------------------------------------------------------------- self test
def selftest(verbose=True):
    """Checks the noise and run ports against what the C# computed: the ski puzzles' FixedSites (saved in their site.json
    as float32) and the lift markers in the scene. Returns the list of (what, error in metres)."""
    import json
    res = []
    def site(name):
        s = json.load(open(os.path.join(REPO, "unity", "Assets", "Puzzles", name, "site.json"))); return np.array([s["x"], s["z"]])
    # SlalomPuzzle: Run 0, StartAlong 610
    mid, half = run_centre(0, 580.0); nxt, _ = run_centre(0, 586.0); d = (nxt - mid) / np.linalg.norm(nxt - mid)
    res.append(("Slalom FixedSite", float(np.linalg.norm(mid + np.array([d[1], -d[0]]) * (half + 9.0) - site("Slalom")))))
    # BigAirPuzzle: Run 2, KickerAlong 1120
    mid, half = run_centre(2, 1110.0); nxt, _ = run_centre(2, 1116.0); d = (nxt - mid) / np.linalg.norm(nxt - mid)
    res.append(("BigAir FixedSite", float(np.linalg.norm(mid + np.array([d[1], -d[0]]) * (half + 9.0) - site("BigAir")))))
    # DownhillPuzzle: the finish + direction * 34 - side * (half * 0.2)
    mid, d, half = piste_finish(); side = np.array([d[1], -d[0]])
    res.append(("Downhill FixedSite", float(np.linalg.norm(mid + d * 34.0 - side * (half * 0.2) - site("Downhill")))))
    sc = read_scene()
    gb, gt = gondola_ends(); sb, st = ski_lift_ends()
    if len(sc["Gondola Line"]):
        res.append(("Gondola bottom (scene P0)", float(np.linalg.norm(gb - sc["Gondola Line"][0]))))
        res.append(("Gondola top (scene last)", float(np.linalg.norm(gt - sc["Gondola Line"][-1]))))
        res.append(("Gondola pylons (scene)", float(np.max(np.linalg.norm(line_towers(gb, gt, 80.0) - sc["Gondola Line"][1:-1], axis=1)))))
    if len(sc["Ski Lift Line"]):
        res.append(("Ski lift bottom (scene P0)", float(np.linalg.norm(sb - sc["Ski Lift Line"][0]))))
        res.append(("Ski lift top (scene last)", float(np.linalg.norm(st - sc["Ski Lift Line"][-1]))))
        res.append(("Ski lift towers (scene)", float(np.max(np.linalg.norm(line_towers(sb, st, 55.0) - sc["Ski Lift Line"][1:-1], axis=1)))))
    if len(sc["Summit Lift Line"]):
        rail = Rail(); foot = rail.summit_lift_foot(); top = SUMMIT + (foot - SUMMIT) / np.linalg.norm(foot - SUMMIT) * SUMMIT_LIFT_REACH
        res.append(("Summit lift foot (scene P0)", float(np.linalg.norm(foot - sc["Summit Lift Line"][0]))))
        res.append(("Summit lift top (scene last)", float(np.linalg.norm(top - sc["Summit Lift Line"][-1]))))
    if verbose:
        for what, err in res: print("  %-30s %9.4f m %s" % (what, err, "ok" if err < 0.05 else "MISMATCH"))
    return res


if __name__ == "__main__":
    print("perlin(0, 0) =", float(perlin(0.0, 0.0)), "(Unity: 0.4652731)")
    selftest()
