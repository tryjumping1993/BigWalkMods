"""Winter Isle site checker: does a plan of puzzle sites fit the island as it is built?

  py.cmd tools\\sites\\sitecheck.py PLAN.json [options]     check a plan (puzzles it does not list keep their site.json)
  py.cmd tools\\sites\\sitecheck.py --current [options]     check the sites as they are (every site.json)

A plan is JSON: {"sites": {"SnowGlobe": [-1400, -1700], "NewPuzzle": [-1500, -1600, 26, 26]}, "footprints": {...}}
(an entry is [x, z] or [x, z, footprint_x, footprint_z]; a name not in footprints.json needs a footprint). Options:
  --site NAME=x,z[,fx,fz]   move (or add) one site on the command line; repeatable
  --only                    check only the plan's own sites (do not fill the rest from site.json)
  --json PATH               write the whole report (every measure, the full site-to-site distance matrix) as JSON
  --write-plan PATH         write the checked layout as a plan JSON (with --current: the current sites as a plan)
  --map PNG                 render where a 26, 34 and 48 m (square) footprint could go, and list under-served regions
  --sizes 26,34,48          the footprints for --map (square, metres; radius = side / sqrt 2)
  --why PNG                 with --map: each land cell coloured by the first rule a site there fails (middle size)
  --far M                   with --map: under-served = feasible ground at least M m from every puzzle (default 200)
  --region-size N           with --map: the footprint the regions, the station table and --why use (default 26)
  --keep A,B,...            sites the map keeps 70 m from (default: the sites that stay: TheTree, the summit and
                            ski-area packs; Lookouts' far sites are always kept)
  --avoid-plan              the map keeps 70 m from every site of the checked layout instead (room for one more)
  --set KEY=VALUE           change a chosen margin (see --explain); repeatable
  --verbose                 print every measure for every site
  --explain                 print the constraint table (every limit, where it comes from) and exit
  --write-footprints        re-read every PuzzleDef's Footprint, PuzzleType and ExtraSites into footprints.json
  --selftest                check the C# ports against what the build wrote (ski sites, lift markers) and exit
Exit codes: 0 every site passes (warnings allowed), 1 some site fails, 2 bad input or missing files. (py.cmd starts Python
with 'start /wait', so PowerShell's $LASTEXITCODE after '& py.cmd ...' reads 0: read the code inside cmd instead, e.g.
cmd /v:on /c "Z:\\Temp\\claude\\BigWalkMods\\py.cmd tools\\sites\\sitecheck.py plan.json & echo !errorlevel!", or run python directly.)
The text report shows each site's main distances and every FAIL / WARN / WAIVED; --json (or --verbose) holds every
measure: on land and height, cut/fill and slope, the railway by kind, stations, pistes, airport and airstrip, gondola,
pylons, lifts, terrain park, lodge, lake, plaza, summit, Lookouts' far sites, cabins, creek, gorge, mine stations, walking
distances, and every site-to-site distance (centre and edge). footprints.json is rebuilt by itself when a puzzle's .cs
is newer.

Reads only what the island's build wrote (unity/Land/WinterIsle.land, the .railline/.mineline beside the Winter Walk
plugin, the island's scene) and the puzzles' C#. Writes nothing in the repository except footprints.json (on
--write-footprints). Caches walking-distance and piste rasters in Z:/Temp/claude/BigWalkMods/sites/cache (keyed on the
input files' hashes), so a check takes a couple of seconds after the first run.
"""
import argparse, glob, hashlib, json, math, os, re, sys, time
import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import islegeom as G      # noqa: E402

REPO = G.REPO
PUZZLES_CS = os.path.join(REPO, "unity", "Assets", "Editor", "Puzzles")
PUZZLES_OUT = os.path.join(REPO, "unity", "Assets", "Puzzles")
FOOTPRINTS = os.path.join(HERE, "footprints.json")
CACHE = os.environ.get("SITECHECK_CACHE", r"Z:\Temp\claude\BigWalkMods\sites\cache")
VERSION = "sitecheck 1"
T0 = time.time()

# --------------------------------------------------------------------------------------------- the constraint table
# key: (value, source, meaning). 'code' rows are facts read from the C# (changing them only makes sense if the C#
# changes); 'chosen' rows are margins picked for this checker and may be changed with --set KEY=VALUE.
WIP, RAIL, AIR, MINE, CLI = "WinterIslePuzzle.cs", "WinterIsleRail.cs", "WinterIsleAirport.cs", "WinterIsleMine.cs", "Bwm/Cli.cs"
LIMITS = {
    # ---- from the code
    "site_radius":      ("diag/2", "code " + CLI + ":285", "a site's radius is its Footprint's diagonal / 2 (Footprint.magnitude / 2)"),
    "flat_extra":       (2.0, "code " + WIP + ":962", "the island levels a flat of radius r + 2 at each site..."),
    "flat_blend":       (10.0, "code " + WIP + ":962", "...blended into the land over 10 m more (to r + 12)"),
    "on_island":        (0.2, "code " + WIP + ":960", "a site whose centre cell is not above 0.2 m is not on the island: no flat, no path"),
    "path_west":        (-2300.0, "code " + WIP + ":1233", "paths are drawn from the plaza only to sites east of this x"),
    "walk_grade":       (0.5, "code " + WIP + ":1213", "the path search never takes a 3 m step steeper than this (rise/run)"),
    "walk_height":      (0.4, "code " + WIP + ":1211", "the path search only crosses cells above 0.4 m"),
    "mine_clear":       (18.0, "code " + WIP + ":948", "a mine station keeps site.r + 18 from a site's centre (StationAllowed): closer, it moves on rebuild"),
    "airport_blend":    (18.0, "code " + AIR + ":26", "the airport's ground is levelled where Outside < 18 (runway and apron plus blend)"),
    "strip_block":      (4.0, "code " + WIP + ":1386", "the summit airstrip's deck is blocked to StripOutside < 4"),
    "bench_half":       (6.0, "code " + RAIL + ":220", "the rail bed is benched level to BedHalf + 1.2 = 6 m each side of the line..."),
    "bench_half_stn":   (11.0, "code " + RAIL + ":220", "...11 m (StationHalf) within 13 points of a station"),
    "bench_bank":       ("clamp(1.4|gap|,5,24)", "code " + RAIL + ":221", "...then banks out to half + clamp(1.4 |gap|, 5, 24) m (gap estimated from the land 36 m either side)"),
    "tunnel_enter":     (9.5, "code " + RAIL + ":167", "a stretch becomes tunnel where the ground stands 9.5 m over the track (and stays one to 7.5 m)"),
    "portal_inside":    (18.0, "code " + RAIL + ":228", "a tunnel's first 18 m from a mouth is a portal (Keep 11 m, benched into the hillside)"),
    "gondola_corridor": (10.0, "code " + WIP + ":1336", "the gondola's line is cleared 10 m either side"),
    "gondola_station":  (14.0, "code " + WIP + ":1333", "the gondola's bottom station: 14 m clear (its flat is 10 m + 9 m blend, line 930)"),
    "skilift_corridor": (8.0, "code " + WIP + ":1345", "the ski lift's line is cleared 8 m either side"),
    "skilift_bottom":   (16.0, "code " + WIP + ":1337", "the ski lift's bottom station: 16 m clear"),
    "skilift_top":      (13.0, "code " + WIP + ":1338", "the ski lift's top station: 13 m clear"),
    "finish_way":       (6.0, "code " + WIP + ":1348", "a 6 m way from the finish arch to the ski lift"),
    "summitlift_end":   (9.0, "code " + WIP + ":1342", "the summit lift's stations: 9 m clear"),
    "summitlift_line":  (6.0, "code " + WIP + ":1343", "the summit lift's line: 6 m clear"),
    "summit_flat":      ("21 + 14", "code " + WIP + ":930", "the summit's flat: radius 21 (FAIL if a site's flat enters it) + 14 m blend (WARN)"),
    "plaza_flat":       ("24 + 16", "code " + WIP + ":930", "the plaza's flat: radius 24 (FAIL) + 16 m blend (WARN)"),
    "tower_flat":       ("8 + 6", "code " + WIP + ":964", "the train tower's flat beside the Plaza station: radius 8 (FAIL) + 6 m blend (WARN)"),
    "lake_shore":       (40.0, "code " + WIP + ":1371", "the lake (radius 32) and its blocked shore (+8 m)"),
    "lake_hollow":      (50.0, "code " + WIP + ":607", "the lake's hollow: radius 32 + 18 m"),
    "gorge_walls":      (15.0, "code " + WIP + ":599", "the gorge's walls reach 15 m from its line"),
    "creek_banks":      (17.0, "code " + WIP + ":603", "the creek's banks reach 17 m from its bed"),
    "lookout_radius":   (7.0, "code LookoutsPuzzle.cs:24", "Lookouts' far sites (ExtraSites) are sites of radius 7"),
    "cabin":            ("half-diagonal", "code " + WIP + ":1449,2131", "a hamlet cabin's walls (its collider box); cabins are re-placed on every rebuild but only keep r - 3 from a site's centre (WARN, not FAIL)"),
    "lodge_radius":     (16.0, "code " + WIP + ":2833", "the lodge (a 12 x 18 m cabin with a 5.5 m deck in front: 16 m), the hut beside it 5 m"),
    # ---- chosen for this checker
    "spacing":          (70.0, "chosen (CLAUDE.md)", "site centres 70 m or more apart"),
    "track_fail":       (20.0, "chosen", "FAIL: site edge within 20 m of a ground or trestle track or a tunnel portal"),
    "track_warn":       (35.0, "chosen (tools/railnet/graph/route.py:102)", "WARN: within 35 m (the margin the railway was routed with)"),
    "cut_fail":         (10.0, "chosen", "FAIL: the flat (r + 2) needs more than 10 m of cut or fill (over the 10 m blend: a 45 degree bank)"),
    "cut_warn":         (5.0, "chosen", "WARN: more than 5 m (a bank steeper than the paths' 27 degree limit)"),
    "ring_fail":        (15.0, "chosen (worst current site: 14.0 m)", "FAIL: the land at the blend's outer edge (r + 12) is more than 15 m off the flat's level"),
    "ring_warn":        (9.0, "chosen (3/4 of current sites under 8.7 m)", "WARN: more than 9 m"),
    "coast_warn":       (25.0, "chosen", "WARN: site edge within 25 m of the sea (a site near the coast moves the coastline: Guard)"),
    "piste_warn":       (15.0, "chosen", "WARN: site edge within 15 m of a piste (FAIL if the flat overlaps one)"),
    "lines_warn":       (25.0, "chosen", "WARN: site edge within 25 m of a lift or gondola line"),
    "pylon_fail":       (10.0, "chosen", "FAIL: site edge within 10 m of a gondola pylon or lift tower"),
    "park_fail":        (15.0, "chosen", "FAIL: site edge within 15 m of the terrain park's kickers or half-pipe"),
    "lodge_warn":       (40.0, "chosen", "WARN: site edge within 40 m of the lodge"),
    "airport_warn":     (30.0, "chosen", "WARN: site edge within 30 m of the runway or apron (FAIL if the flat reaches the levelled ground)"),
    "strip_warn":       (30.0, "chosen", "WARN: site edge within 30 m of the summit airstrip's deck"),
    "walk_warn":        (300.0, "chosen", "WARN: a site west of the path line more than 300 m on foot from a rail or mine station"),
    "station_walk":     (500.0, "chosen", "WARN: any other site more than 500 m on foot from every rail and mine station; also the reach of the station table and regions"),
    "near_station":     (300.0, "chosen", "spread metric: a station 'has a puzzle' within 300 m"),
    "near_plaza":       (400.0, "chosen", "spread metric: share of puzzles within 400 m of the plaza"),
}

# Checks waived for the sites that stay where they are by design (reported as WAIVED, not PASS)
SUMMIT_PACKS = {"SummitShot", "CastIronPizza", "SummitDiscGolf"}
SKI_PACKS = {"Slalom", "BigAir", "Downhill"}
WAIVE = {
    "TheTree": {"plaza", "gondola", "gondola_station", "tower", "cabins", "lake"},
    "SummitShot": {"summit", "strip", "summitlift", "gondola", "rail_tunnel", "lookouts", "cut", "ring", "coast", "piste", "reach"},
    "Slalom": {"piste", "park", "lodge", "skilift", "cut", "ring", "reach"},
    "BigAir": {"piste", "park", "lodge", "skilift", "cut", "ring", "reach"},
    "Downhill": {"piste", "park", "lodge", "skilift", "cut", "ring", "reach"},
    "Lookouts": {"lookouts"},
}
DEFAULT_KEEP = ["TheTree", "SummitShot", "Slalom", "BigAir", "Downhill"]
NOT_SITES = {"WinterIsle", "BwmTutorial"}      # the island itself; the mainland smoke test


def log(*a):
    if os.environ.get("SITECHECK_QUIET") != "1": print("[%5.1f s]" % (time.time() - T0), *a, file=sys.stderr, flush=True)


def L(key): return LIMITS[key][0]


# --------------------------------------------------------------------------------------------- footprints from the C#
def write_footprints():
    """Parse every PuzzleDef: Name, Footprint (x, z), PuzzleType, FixedSite literal and ExtraSites into footprints.json."""
    consts = {"WinterIslePuzzle.PlazaRadius": "24", "PlazaRadius": "24"}
    out = {}
    for f in sorted(glob.glob(os.path.join(PUZZLES_CS, "*.cs"))):
        src = open(f, encoding="utf-8").read()
        m = re.search(r"override\s+string\s+Name\s*=>\s*\"(\w+)\"", src)
        if not m: continue
        name = m.group(1)
        fp = (18.0, 14.0)      # PuzzleDef's default
        m = re.search(r"override\s+Vector2\s+Footprint\s*=>\s*new\s+Vector2\(([^;]*)\);", src)
        if m:
            expr = m.group(1)
            for k, v in consts.items(): expr = expr.replace(k, v)
            if "X1 - X0" in expr: fp = (2760.0, 2199.0)      # the island's box
            else:
                parts = [p.strip().rstrip("fF") for p in re.split(r",(?![^(]*\))", expr)]
                parts = [re.sub(r"(\d)f\b", r"\1", p) for p in parts]
                if not all(re.fullmatch(r"[\d.\s*+\-/()]+", p) for p in parts): raise SystemExit("cannot read the Footprint of " + name + ": " + expr)
                fp = tuple(float(eval(p, {"__builtins__": {}})) for p in parts)
        t = re.search(r"override\s+int\s+PuzzleType\s*=>\s*(\d)", src)
        ptype = int(t.group(1)) if t else 0
        entry = {"footprint": list(fp), "radius": round(math.hypot(*fp) / 2.0, 3), "type": ptype, "source": os.path.basename(f)}
        if name == "Lookouts": entry["extra_sites"] = [list(e) for e in G.LOOKOUT_EXTRA]
        out[name] = entry
    json.dump(out, open(FOOTPRINTS, "w"), indent=1)
    print("wrote", FOOTPRINTS, "with", len(out), "puzzles")
    return out


def load_footprints():
    """footprints.json, rebuilt from the C# when it is missing or older than any puzzle's source."""
    newest = max(os.path.getmtime(f) for f in glob.glob(os.path.join(PUZZLES_CS, "*.cs")))
    if not os.path.exists(FOOTPRINTS) or os.path.getmtime(FOOTPRINTS) < newest: return write_footprints()
    return json.load(open(FOOTPRINTS))


def current_sites(fps):
    sites = {}
    for name in fps:
        f = os.path.join(PUZZLES_OUT, name, "site.json")
        if os.path.exists(f) and name not in NOT_SITES:
            s = json.load(open(f)); sites[name] = (float(s["x"]), float(s["z"]))
    return sites


# --------------------------------------------------------------------------------------------- the island's context
def _hash(*paths):
    h = hashlib.md5(VERSION.encode())
    for p in paths: h.update(open(p, "rb").read() if os.path.exists(p) else b"-")
    return h.hexdigest()[:12]


class Island:
    def __init__(self):
        for p in (G.LAND_FILE, G.RAIL_FILE, G.MINE_FILE):
            if not os.path.exists(p): raise FileNotFoundError(p + " (build WinterIsle first)")
        self.land = G.Land(); ld = self.land
        self.rail = G.Rail()
        self.mine, self.hall, self.mine_tower = G.read_mine()
        self.scene = G.read_scene() if os.path.exists(G.SCENE_FILE) else {"Cabin": np.zeros((0, 4))}
        self.key = _hash(G.LAND_FILE, G.RAIL_FILE, G.MINE_FILE, G.__file__) + "-%g-%g" % (L("walk_grade"), L("walk_height"))
        # fixed structures
        self.gb, self.gt = G.gondola_ends()
        self.pylons = G.line_towers(self.gb, self.gt, 80.0)
        self.sb, self.st = G.ski_lift_ends()
        self.lift_towers = G.line_towers(self.sb, self.st, 55.0)
        self.finish, _, _ = G.piste_finish()
        self.slf = self.rail.summit_lift_foot()
        self.slt = G.SUMMIT + (self.slf - G.SUMMIT) / np.linalg.norm(self.slf - G.SUMMIT) * G.SUMMIT_LIFT_REACH
        self.summit_towers = G.line_towers(self.slf, self.slt, 55.0)
        self.lodge, self.hut = G.lodge()
        self.kickers, self.pipe = G.terrain_park()
        self.tower = self.rail.tower_spot()
        self.cabins = self.scene["Cabin"]
        self.towers = np.concatenate([self.pylons, self.lift_towers, self.summit_towers]) if len(self.summit_towers) else np.concatenate([self.pylons, self.lift_towers])
        # stations (Cirque Gate is on both lines: one station for the metrics)
        self.stations = [(n, x, z) for n, x, z, _, _ in self.rail.stations]
        self.station_names = sorted({n for n, _, _ in self.stations}, key=lambda n: [s[0] for s in self.stations].index(n))
        self.stn_xz = np.array([[x, z] for _, x, z in self.stations])
        # rail classes and the bench each ground/portal point carries
        r = self.rail
        self.rail_trees = {c: cKDTree(r.xz[r.cls == c]) for c in (0, 1, 2, 3) if np.any(r.cls == c)}
        self.rail_y = {c: r.y[r.cls == c] for c in (0, 1, 2, 3)}
        plat = np.zeros(len(r.xz), bool)
        off = 0
        for l in r.lines:
            n = len(l["p"])
            for s in l["stops"]:
                ii = np.arange(s - r.STATION_REACH, s + r.STATION_REACH + 1)
                ii = ii % n if l["closed"] else ii[(ii >= 0) & (ii < n)]
                plat[off + ii] = True
            off += n
        self.platform_tree = cKDTree(r.xz[plat])
        benchm = (r.cls == 0) | (r.cls == 3)
        bx = r.xz[benchm]; by = r.y[benchm]; near = r.near[benchm]
        half = np.where(near, L("bench_half_stn"), L("bench_half"))
        # gap estimate: the land 36 m either side of the line (beyond any bench), averaged, minus the track's height
        nrm = self._normals()[benchm]
        s = half + 30.0
        g1 = ld.sample(bx[:, 0] + nrm[:, 0] * s, bx[:, 1] + nrm[:, 1] * s); g2 = ld.sample(bx[:, 0] - nrm[:, 0] * s, bx[:, 1] - nrm[:, 1] * s)
        gap = 0.5 * (g1 + g2) - by
        self.bench_xz = bx; self.bench_reach = half + np.clip(np.abs(gap) * 1.4, 5.0, 24.0)
        self.bench_tree = cKDTree(bx)
        self.tun_xz = r.xz[r.kind == 1]; self.tun_y = r.y[r.kind == 1]
        self.tun_tree = cKDTree(self.tun_xz) if len(self.tun_xz) else None
        self.mine_tree = cKDTree(self.mine) if len(self.mine) else None
        self.lookouts = np.array([[x, z] for x, z, _ in G.LOOKOUT_EXTRA])
        self._rasters = None

    def _normals(self):
        r = self.rail; out = np.zeros_like(r.xz); off = 0
        for l in r.lines:
            p = l["p"][:, [0, 2]]; n = len(p)
            if l["closed"]: fw = np.roll(p, -1, 0) - np.roll(p, 1, 0)
            else: fw = np.gradient(p, axis=0)
            fw /= np.linalg.norm(fw, axis=1, keepdims=True) + 1e-9
            out[off:off + n] = np.c_[fw[:, 1], -fw[:, 0]]; off += n
        return out

    # ---- rasters on the land grid (cached): the piste, the sea, walking distances
    def rasters(self):
        if self._rasters is not None: return self._rasters
        os.makedirs(CACHE, exist_ok=True)
        path = os.path.join(CACHE, "rasters-" + self.key + ".npz")
        if os.path.exists(path):
            d = np.load(path); self._rasters = {k: d[k] for k in d.files}; return self._rasters
        ld = self.land
        X, Z = np.meshgrid(ld.xs, ld.zs)
        log("building rasters (once per island build): piste")
        on_face = G.ski_face(X, Z) > 0.0
        ps = np.zeros(X.shape); ps[on_face] = G.piste(X[on_face], Z[on_face])
        m = ps > 0.3
        mh = np.zeros(X.shape); mh[m] = G.mountain_height(X[m], Z[m])
        piste = m & (mh > 4.0)                       # the island's own test for kept-clear piste (line 1353)
        log("walking distances")
        walk_plaza, walk_rail, walk_mine, rail_src = self._walks()
        self._rasters = dict(piste=piste, walk_plaza=walk_plaza.astype(np.float32), walk_rail=walk_rail.astype(np.float32),
                             walk_mine=walk_mine.astype(np.float32), rail_src=rail_src.astype(np.int16))
        np.savez_compressed(path, **self._rasters)
        return self._rasters

    def _walks(self):
        from scipy.sparse import coo_matrix
        from scipy.sparse.csgraph import dijkstra
        ld = self.land; h = ld.h; Hh, W = h.shape; N = Hh * W
        ok = np.nan_to_num(h, nan=-9.0) > L("walk_height")
        idx = np.arange(N).reshape(Hh, W)
        rows, cols, wts = [], [], []
        for dj, di in ((0, 1), (1, 0), (1, 1), (1, -1)):
            run = ld.cell * math.hypot(dj, di)
            a = idx[:Hh - dj, max(0, -di):W - max(0, di)]; b = idx[dj:, max(0, di):W - max(0, -di)]
            ha = h.ravel()[a]; hb = h.ravel()[b]
            v = ok.ravel()[a] & ok.ravel()[b] & (np.abs(ha - hb) / run <= L("walk_grade"))
            rows.append(a[v]); cols.append(b[v]); wts.append(np.full(int(v.sum()), run))
        g = coo_matrix((np.concatenate(wts), (np.concatenate(rows), np.concatenate(cols))), shape=(N, N)).tocsr()
        def src_cell(x, z):
            """the walkable cell nearest (x, z), within 18 m"""
            j, i = (int(v) for v in ld.cell_of(x, z))
            jj, ii = np.mgrid[max(0, j - 6):min(Hh, j + 7), max(0, i - 6):min(W, i + 7)]
            good = ok[jj, ii]
            if not good.any(): return int(j * W + i)
            d2 = np.where(good, (jj - j) ** 2 + (ii - i) ** 2, 1 << 30)
            q = np.unravel_index(np.argmin(d2), d2.shape)
            return int(jj[q] * W + ii[q])
        plaza = [src_cell(*G.PLAZA)]
        stn = [src_cell(x, z) for _, x, z in self.stations]
        mines = [src_cell(x, z) for x, z in self.mine] if len(self.mine) else plaza
        wp = dijkstra(g, directed=False, indices=plaza, min_only=True)
        wr, _, srcs = dijkstra(g, directed=False, indices=stn, min_only=True, return_predecessors=True)
        wm = dijkstra(g, directed=False, indices=mines, min_only=True)
        lut = {c: k for k, c in enumerate(stn)}
        rs = np.array([lut.get(int(s), -1) for s in srcs]) if len(srcs) else np.full(N, -1)
        return wp.reshape(Hh, W), wr.reshape(Hh, W), wm.reshape(Hh, W), rs.reshape(Hh, W)

    def sea_dist(self):
        """Metres from each land-grid cell to the nearest sea cell (height <= on_island)."""
        if not hasattr(self, "_sea"):
            sea = ~(np.nan_to_num(self.land.h, nan=-9.0) > L("on_island"))
            self._sea = ndimage.distance_transform_edt(~sea) * self.land.cell
        return self._sea


# --------------------------------------------------------------------------------------------- measures
def seg_dist(x, z, a, b):
    x = np.asarray(x, float); z = np.asarray(z, float); ab = b - a
    t = np.clip(((x - a[0]) * ab[0] + (z - a[1]) * ab[1]) / max(ab @ ab, 1e-9), 0, 1)
    return np.hypot(x - (a[0] + t * ab[0]), z - (a[1] + t * ab[1]))


def pts_dist(x, z, pts):
    if len(pts) == 0: return np.full(np.shape(x), np.inf), np.zeros(np.shape(x), int)
    d, i = cKDTree(pts).query(np.c_[np.ravel(x), np.ravel(z)])
    return d.reshape(np.shape(x)), i.reshape(np.shape(x))


def disc(x, z, rho, rings=(0.0, 0.2, 0.4, 0.6, 0.8, 1.0)):
    """Sample points over discs of radius rho about each (x, z): arrays (n_sites, n_samples)."""
    xs, zs = [np.zeros((len(x), 1))], [np.zeros((len(x), 1))]
    for f in rings[1:]:
        n = max(8, int(round(2 * math.pi * f * 12)))
        a = np.arange(n) * 2 * math.pi / n
        xs.append(np.outer(rho * f, np.cos(a))); zs.append(np.outer(rho * f, np.sin(a)))
    return x[:, None] + np.concatenate(xs, 1), z[:, None] + np.concatenate(zs, 1)


def circle(x, z, rho, n=64):
    a = np.arange(n) * 2 * math.pi / n
    return x[:, None] + np.outer(rho, np.cos(a)), z[:, None] + np.outer(rho, np.sin(a))


def measure(isl, x, z, r):
    """Every measure for sites at (x, z) of radius r (arrays). Distances are metres; '*_edge' = from the site's circle."""
    x = np.asarray(x, float); z = np.asarray(z, float); r = np.asarray(r, float)
    ld = isl.land; ras = isl.rasters(); M = {}
    fe, fb = L("flat_extra"), L("flat_blend")
    j, i = ld.cell_of(x, z)
    hc = ld.h[j, i]
    M["inbox"] = (x >= G.X0) & (x <= G.X1) & (z >= G.Z0) & (z <= G.Z1)
    M["height"] = np.where(np.isnan(hc), -99.0, hc)
    M["level"] = np.round(np.nan_to_num(hc, nan=0.0) * 2.0) / 2.0
    # the flat: cut and fill, sea, slope; the blend's outer edge
    dx, dz = disc(x, z, r + fe)
    hs = ld.sample(dx, dz); sea = ~(ld.sample(dx, dz, np.nan_to_num(ld.h, nan=-9.0)) > 0.0)
    land_h = np.where(sea, np.nan, hs)
    M["cut"] = np.nanmax(np.abs(land_h - M["level"][:, None]), 1, initial=0.0)
    M["flat_range"] = np.nanmax(land_h, 1, initial=-99) - np.nanmin(land_h, 1, initial=99)
    M["flat_sea_share"] = sea.mean(1)
    A = np.stack([dx - x[:, None], dz - z[:, None], np.ones_like(dx)], -1)
    slope = np.zeros(len(x))
    for k in range(len(x)):
        ok = ~sea[k]
        if ok.sum() >= 3: slope[k] = float(np.hypot(*np.linalg.lstsq(A[k, ok], hs[k, ok], rcond=None)[0][:2]))
    M["slope_pct"] = slope * 100.0
    cx, cz = circle(x, z, r + fe + fb)
    hr = ld.sample(cx, cz); sear = ~(ld.sample(cx, cz, np.nan_to_num(ld.h, nan=-9.0)) > 0.0)
    M["ring"] = np.nanmax(np.abs(np.where(sear, np.nan, hr) - M["level"][:, None]), 1, initial=0.0)
    M["ring_sea_share"] = sear.mean(1)
    M["coast_edge"] = ld.sample(x, z, isl.sea_dist()) - r
    bl = ld.blocked[ld.cell_of(dx, dz)]
    M["blocked_share"] = bl.mean(1)
    # the railway
    for c, key in ((0, "rail_ground"), (2, "rail_trestle"), (3, "rail_portal"), (1, "rail_tunnel")):
        if c in isl.rail_trees:
            d, _ = isl.rail_trees[c].query(np.c_[x, z]); M[key + "_edge"] = d - r
        else: M[key + "_edge"] = np.full(len(x), np.inf)
    M["track_edge"] = np.minimum(np.minimum(M["rail_ground_edge"], M["rail_trestle_edge"]), M["rail_portal_edge"])
    dd, ii = isl.bench_tree.query(np.c_[x, z], k=min(64, len(isl.bench_xz)))
    M["bench_margin"] = np.min(dd - isl.bench_reach[ii], 1) - (r + fe)      # < 0: the flat overlaps the benched bed or banks
    cov_f = np.full(len(x), np.inf); cov_b = np.full(len(x), np.inf)
    if isl.tun_tree is not None:
        for k in range(len(x)):
            for rho, arr in ((r[k] + fe + G.Rail.TUNNEL_HALF, cov_f), (r[k] + fe + fb + G.Rail.TUNNEL_HALF, cov_b)):
                near = isl.tun_tree.query_ball_point([x[k], z[k]], rho)
                if near: arr[k] = float(np.min(M["level"][k] - isl.tun_y[near]))
    M["tunnel_cover_flat"] = cov_f; M["tunnel_cover_blend"] = cov_b
    d, k = pts_dist(x, z, isl.stn_xz)
    M["station"] = np.array([isl.stations[q][0] for q in k]); M["station_dist"] = d
    d, _ = isl.platform_tree.query(np.c_[x, z]); M["platform_edge"] = d - r
    # pistes, airport, airstrip (the flat sampled with the ported functions; edges from rasters / rectangles)
    piste_d = ndimage.distance_transform_edt(~ras["piste"]) * ld.cell if "piste_edt" not in ras else ras["piste_edt"]
    ras["piste_edt"] = piste_d
    M["piste_edge"] = ld.sample(x, z, piste_d) - r
    pf = G.piste(dx, dz); on = pf > 0.3
    if np.any(on): on[on] = G.mountain_height(dx[on], dz[on]) > 4.0
    M["piste_in_flat"] = on.any(1)
    M["airport_edge"] = G.airport_distance(x, z) - r
    M["airport_in_flat"] = (G.airport_outside(dx, dz) < L("airport_blend")).any(1)
    M["strip_edge"] = G.strip_distance(x, z) - r
    M["strip_in_flat"] = (G.strip_outside(dx, dz) < L("strip_block")).any(1)
    # lifts, the lodge, the park
    M["gondola_line_edge"] = seg_dist(x, z, isl.gb, isl.gt) - r
    M["gondola_station_edge"] = np.hypot(x - isl.gb[0], z - isl.gb[1]) - r
    M["tower_edge"] = pts_dist(x, z, isl.towers)[0] - r
    M["skilift_line_edge"] = seg_dist(x, z, isl.sb, isl.st) - r
    M["skilift_bottom_edge"] = np.hypot(x - isl.sb[0], z - isl.sb[1]) - r
    M["skilift_top_edge"] = np.hypot(x - isl.st[0], z - isl.st[1]) - r
    M["finish_way_edge"] = seg_dist(x, z, isl.sb, isl.finish) - r
    M["summitlift_line_edge"] = seg_dist(x, z, isl.slf, isl.slt) - r
    M["summitlift_end_edge"] = np.minimum(np.hypot(x - isl.slf[0], z - isl.slf[1]), np.hypot(x - isl.slt[0], z - isl.slt[1])) - r
    M["park_edge"] = np.minimum(pts_dist(x, z, isl.kickers)[0], G.to_polyline(x, z, isl.pipe)) - r
    M["lodge_edge"] = np.minimum(np.hypot(x - isl.lodge[0], z - isl.lodge[1]) - L("lodge_radius"), np.hypot(x - isl.hut[0], z - isl.hut[1]) - 5.0) - r
    # places
    M["lake_edge"] = np.hypot(x - G.LAKE[0], z - G.LAKE[1]) - G.LAKE_RADIUS - r
    M["plaza_edge"] = np.hypot(x - G.PLAZA[0], z - G.PLAZA[1]) - G.PLAZA_RADIUS - r
    M["plaza_dist"] = np.hypot(x - G.PLAZA[0], z - G.PLAZA[1])
    M["summit_edge"] = np.hypot(x - G.SUMMIT[0], z - G.SUMMIT[1]) - G.SUMMIT_RADIUS - r
    M["trainTower_edge"] = (np.hypot(x - isl.tower[0], z - isl.tower[1]) - 8.0 - r) if isl.tower is not None else np.full(len(x), np.inf)
    d, k = pts_dist(x, z, isl.lookouts); M["lookout_edge"] = d - L("lookout_radius") - r
    if len(isl.cabins):
        cd = np.hypot(x[:, None] - isl.cabins[None, :, 0], z[:, None] - isl.cabins[None, :, 1]) - (np.hypot(isl.cabins[:, 2], isl.cabins[:, 3]) / 2)[None, :]
        M["cabin_edge"] = cd.min(1) - r      # < 0: a cabin's walls reach inside the site's circle
    else: M["cabin_edge"] = np.full(len(x), np.inf)
    M["creek_edge"] = G.to_polyline(x, z, G.CREEK) - r
    M["gorge_edge"] = G.to_polyline(x, z, G.GORGE) - r
    if isl.mine_tree is not None:
        d, k = isl.mine_tree.query(np.c_[x, z]); M["mine_station"] = np.array(["M%d%s" % (q + 1, " (tower)" if q == 0 else "") for q in k]); M["mine_dist"] = d
    else: M["mine_station"] = np.array(["-"] * len(x)); M["mine_dist"] = np.full(len(x), np.inf)
    # walking: the best cell of the flat (the flat will be level and walkable)
    for key in ("walk_plaza", "walk_rail", "walk_mine"):
        w = ras[key][ld.cell_of(dx, dz)]
        M[key] = np.where(np.isfinite(w), w, np.inf).min(1)
    src = ras["rail_src"][ld.cell_of(dx, dz)]
    best = np.argmin(np.where(np.isfinite(ras["walk_rail"][ld.cell_of(dx, dz)]), ras["walk_rail"][ld.cell_of(dx, dz)], np.inf), 1)
    M["walk_rail_station"] = np.array([isl.stations[s][0] if s >= 0 else "-" for s in src[np.arange(len(x)), best]])
    return M


# --------------------------------------------------------------------------------------------- rules
def rules(M, k, r, x, waived, name=""):
    """(status, check, message) for site k; status FAIL / WARN / WAIVED. A check with nothing to report adds nothing."""
    out = []
    def add(check, status, msg):
        out.append(("WAIVED" if check in waived and status in ("FAIL", "WARN") else status, check, msg))
    v = lambda key: M[key][k]
    fe, fb = L("flat_extra"), L("flat_blend")
    if not v("inbox") or v("height") <= L("on_island"):
        add("land", "FAIL", "not on the island (centre at %.1f m): the island would neither level it nor draw a path" % v("height"))
    if v("flat_sea_share") > 0: add("land", "FAIL", "%.0f%% of the flat (r + 2) is sea" % (100 * v("flat_sea_share")))
    elif v("ring_sea_share") > 0: add("coast", "WARN", "the blend ring (r + 12) reaches the sea")
    elif v("coast_edge") < L("coast_warn"): add("coast", "WARN", "edge %.0f m from the sea" % v("coast_edge"))
    if v("cut") > L("cut_fail"): add("cut", "FAIL", "the flat needs %.1f m of cut/fill (level %.1f m)" % (v("cut"), v("level")))
    elif v("cut") > L("cut_warn"): add("cut", "WARN", "the flat needs %.1f m of cut/fill (level %.1f m)" % (v("cut"), v("level")))
    if v("ring") > L("ring_fail"): add("ring", "FAIL", "the land at r + 12 is %.1f m off the flat's level" % v("ring"))
    elif v("ring") > L("ring_warn"): add("ring", "WARN", "the land at r + 12 is %.1f m off the flat's level" % v("ring"))
    # railway
    if v("bench_margin") < 0: add("rail", "FAIL", "the flat overlaps the railway's benched bed/banks by %.0f m" % -v("bench_margin"))
    te = v("track_edge")
    kinds = [(v("rail_ground_edge"), "ground track"), (v("rail_trestle_edge"), "trestle"), (v("rail_portal_edge"), "tunnel portal")]
    kd, kn = min(kinds)
    if te < L("track_fail"): add("rail", "FAIL", "edge %.0f m from a %s (< %.0f)" % (kd, kn, L("track_fail")))
    elif te < L("track_warn"): add("rail", "WARN", "edge %.0f m from a %s (< %.0f)" % (kd, kn, L("track_warn")))
    if v("platform_edge") < L("track_fail"): add("rail", "FAIL", "edge %.0f m from %s station's platform" % (v("platform_edge"), v("station")))
    if v("tunnel_cover_flat") < L("tunnel_enter"): add("rail_tunnel", "FAIL", "a tunnel runs under the flat with only %.1f m between the flat's level and the track (the line would become a cutting)" % v("tunnel_cover_flat"))
    elif v("tunnel_cover_blend") < L("tunnel_enter"): add("rail_tunnel", "WARN", "a tunnel runs under the blend ring with %.1f m of cover" % v("tunnel_cover_blend"))
    # ski area and lifts
    if v("piste_in_flat"): add("piste", "FAIL", "the flat overlaps a piste")
    elif v("piste_edge") < L("piste_warn"): add("piste", "WARN", "edge %.0f m from a piste" % v("piste_edge"))
    if v("park_edge") < L("park_fail"): add("park", "FAIL", "edge %.0f m from the terrain park" % v("park_edge"))
    if v("lodge_edge") < fe: add("lodge", "FAIL", "the flat reaches the lodge")
    elif v("lodge_edge") < L("lodge_warn"): add("lodge", "WARN", "edge %.0f m from the lodge" % v("lodge_edge"))
    if v("airport_in_flat"): add("airport", "FAIL", "the flat reaches the airport's levelled ground (Outside < 18)")
    elif v("airport_edge") < L("airport_warn"): add("airport", "WARN", "edge %.0f m from the runway/apron" % v("airport_edge"))
    if v("strip_in_flat"): add("strip", "FAIL", "the flat reaches the summit airstrip's deck")
    elif v("strip_edge") < L("strip_warn"): add("strip", "WARN", "edge %.0f m from the summit airstrip" % v("strip_edge"))
    if v("gondola_line_edge") < L("gondola_corridor"): add("gondola", "FAIL", "edge %.0f m from the gondola's line (corridor %.0f)" % (v("gondola_line_edge"), L("gondola_corridor")))
    elif v("gondola_line_edge") < L("lines_warn"): add("gondola", "WARN", "edge %.0f m from the gondola's line" % v("gondola_line_edge"))
    if v("gondola_station_edge") < max(L("gondola_station"), 10.0 + fe): add("gondola_station", "FAIL", "edge %.0f m from the gondola's bottom station (its flat is 10 m, kept clear 14 m)" % v("gondola_station_edge"))
    elif v("gondola_station_edge") < 10.0 + 9.0 + fe: add("gondola_station", "WARN", "the flat reaches the gondola station's blend (edge %.0f m from the station)" % v("gondola_station_edge"))
    if v("tower_edge") < L("pylon_fail"): add("gondola", "FAIL", "edge %.0f m from a pylon or lift tower" % v("tower_edge"))
    sl = min(v("skilift_line_edge") - L("skilift_corridor"), v("skilift_bottom_edge") - L("skilift_bottom"), v("skilift_top_edge") - L("skilift_top"), v("finish_way_edge") - L("finish_way"))
    if sl < 0: add("skilift", "FAIL", "inside the ski lift's cleared ground by %.0f m" % -sl)
    elif v("skilift_line_edge") < L("lines_warn"): add("skilift", "WARN", "edge %.0f m from the ski lift" % v("skilift_line_edge"))
    su = min(v("summitlift_line_edge") - L("summitlift_line"), v("summitlift_end_edge") - L("summitlift_end"))
    if su < 0: add("summitlift", "FAIL", "inside the summit lift's cleared ground")
    elif v("summitlift_line_edge") < L("lines_warn"): add("summitlift", "WARN", "edge %.0f m from the summit lift" % v("summitlift_line_edge"))
    # places
    if v("plaza_edge") < fe: add("plaza", "FAIL", "the flat reaches the plaza (edge %.0f m from the plaza's circle)" % v("plaza_edge"))
    elif v("plaza_edge") < fe + 16.0: add("plaza", "WARN", "the flat reaches the plaza's 16 m blend (edge %.0f m from the plaza's circle)" % v("plaza_edge"))
    if v("summit_edge") < fe: add("summit", "FAIL", "the flat reaches the summit's flat")
    elif v("summit_edge") < fe + 14.0: add("summit", "WARN", "the flat reaches the summit's 14 m blend")
    if v("trainTower_edge") < fe: add("tower", "FAIL", "the flat reaches the train tower's ground by the Plaza station")
    elif v("trainTower_edge") < fe + 6.0: add("tower", "WARN", "the flat reaches the train tower's 6 m blend")
    if v("lake_edge") < fe + 8.0: add("lake", "FAIL", "the flat reaches the lake's shore (edge %.0f m from the ice)" % v("lake_edge"))
    elif v("lake_edge") < fe + 18.0: add("lake", "WARN", "the flat reaches the lake's hollow (edge %.0f m from the ice)" % v("lake_edge"))
    if v("creek_edge") < fe + L("creek_banks"): add("creek", "FAIL", "edge %.0f m from the creek's bed (banks %.0f)" % (v("creek_edge"), L("creek_banks")))
    elif v("creek_edge") < fe + fb + L("creek_banks"): add("creek", "WARN", "edge %.0f m from the creek's bed" % v("creek_edge"))
    if v("gorge_edge") < fe + L("gorge_walls"): add("gorge", "FAIL", "edge %.0f m from the gorge (walls %.0f)" % (v("gorge_edge"), L("gorge_walls")))
    elif v("gorge_edge") < fe + fb + L("gorge_walls"): add("gorge", "WARN", "edge %.0f m from the gorge" % v("gorge_edge"))
    if v("cabin_edge") < 0: add("cabins", "WARN", "a hamlet cabin stands %.0f m inside the site's circle (the build lets cabins come within r - 3 of a site's centre: Free(p, -6f), WinterIslePuzzle.cs:1449)" % -v("cabin_edge"))
    if v("mine_dist") < r[k] + L("mine_clear"): add("mine", "WARN", "mine station %s is %.0f m away: it will be moved on rebuild" % (v("mine_station"), v("mine_dist")))
    # reach
    if x[k] > L("path_west"):
        if not np.isfinite(v("walk_plaza")): add("reach", "FAIL", "no walkable route from the plaza: the island cannot draw its path")
        elif min(v("walk_rail"), v("walk_mine")) > L("station_walk"): add("reach", "WARN", "%.0f m on foot from the nearest station (rail %.0f, mine %.0f); the path from the plaza is %.0f m"
                                                                          % (min(v("walk_rail"), v("walk_mine")), v("walk_rail"), v("walk_mine"), v("walk_plaza")))
    else:
        walk = min(v("walk_rail"), v("walk_mine"))
        if not np.isfinite(walk): add("reach", "FAIL", "west of x = %.0f (no path) and not walkable from any station" % L("path_west"))
        elif walk > L("walk_warn"): add("reach", "WARN", "west of x = %.0f (no path); nearest station %.0f m on foot" % (L("path_west"), walk))
    return out


def spacing(names, xs, zs, rs, fixed_extra):
    """Pairwise centre and edge distances; FAIL under 70 m centre to centre or flats overlapping; WARN blends overlapping."""
    allx = np.r_[xs, [e[1] for e in fixed_extra]]; allz = np.r_[zs, [e[2] for e in fixed_extra]]
    allr = np.r_[rs, [e[3] for e in fixed_extra]]; alln = list(names) + [e[0] for e in fixed_extra]
    C = np.hypot(allx[:, None] - allx[None, :], allz[:, None] - allz[None, :])
    E = C - allr[:, None] - allr[None, :]
    probs = {n: [] for n in names}
    for a in range(len(names)):
        for b in range(len(alln)):
            if a == b: continue
            na, nb = alln[a], alln[b]
            if na in SUMMIT_PACKS and nb in SUMMIT_PACKS: continue
            if (na == "Lookouts" and nb.startswith("Lookouts ")) or (nb == "Lookouts" and na.startswith("Lookouts ")): continue
            fe, fb = L("flat_extra"), L("flat_blend")
            if E[a, b] < 2 * fe: probs[na].append(("FAIL", "spacing", "flats overlap %s (edge to edge %.0f m)" % (nb, E[a, b])))
            elif C[a, b] < L("spacing"): probs[na].append(("FAIL", "spacing", "%.0f m from %s (centres; edge to edge %.0f m)" % (C[a, b], nb, E[a, b])))
            elif E[a, b] < 2 * (fe + fb): probs[na].append(("WARN", "spacing", "blends overlap %s (edge to edge %.0f m)" % (nb, E[a, b])))
    return alln, C, E, probs


# --------------------------------------------------------------------------------------------- spread
def spread(isl, names, xs, zs, puzzle_mask):
    px, pz = xs[puzzle_mask], zs[puzzle_mask]; pn = [n for n, m in zip(names, puzzle_mask) if m]
    S = {}
    # regions: the nearest railway station's catchment (straight line), and compass quarters about the land's middle
    ld = isl.land
    land = np.nan_to_num(ld.h, nan=-9) > L("on_island")
    X, Z = np.meshgrid(ld.xs, ld.zs)
    cx, cz = float(X[land].mean()), float(Z[land].mean())
    S["land_centre"] = [round(cx), round(cz)]
    d, k = pts_dist(px, pz, isl.stn_xz)
    catch = {n: [] for n in isl.station_names}
    for name, q in zip(pn, k): catch[isl.stations[q][0]].append(name)
    S["by_station_catchment"] = catch
    quad = {"NE": [], "SE": [], "SW": [], "NW": []}
    for name, x, z in zip(pn, px, pz): quad[("N" if z >= cz else "S") + ("E" if x >= cx else "W")].append(name)
    S["by_quarter"] = quad
    land_q = {q: 0 for q in quad}
    for q in quad:
        m = land & ((Z >= cz) if q[0] == "N" else (Z < cz)) & ((X >= cx) if q[1] == "E" else (X < cx))
        land_q[q] = round(float(m.mean() / land.mean()), 3)
    S["land_share_by_quarter"] = land_q
    pd_ = np.hypot(px - G.PLAZA[0], pz - G.PLAZA[1])
    S["within_400m_of_plaza"] = [int((pd_ <= L("near_plaza")).sum()), len(pn), round(float((pd_ <= L("near_plaza")).mean()), 3)]
    C = np.hypot(px[:, None] - px[None, :], pz[:, None] - pz[None, :]); np.fill_diagonal(C, np.inf)
    nn = C.min(1)
    S["nearest_neighbour"] = {n: [pn[int(np.argmin(C[i]))], round(float(nn[i]), 1)] for i, n in enumerate(pn)}
    S["nearest_neighbour_stats"] = dict(min=round(float(nn.min()), 1), median=round(float(np.median(nn)), 1), mean=round(float(nn.mean()), 1),
                                        under_70=int((nn < 70).sum()), under_150=int((nn < 150).sum()))
    near = {}
    for name in isl.station_names:
        pts = np.array([[x, z] for n, x, z in isl.stations if n == name])
        dd = np.min(np.hypot(px[:, None] - pts[None, :, 0], pz[:, None] - pts[None, :, 1]), 1)
        near[name] = [pn[int(np.argmin(dd))], round(float(dd.min()))]
    S["stations_nearest_puzzle"] = near
    S["stations_with_puzzle_within_300m"] = [sum(1 for v in near.values() if v[1] <= L("near_station")), len(near)]
    # coverage: walkable land's straight distance to the nearest puzzle
    walk = np.isfinite(isl.rasters()["walk_plaza"]) | np.isfinite(isl.rasters()["walk_rail"])
    sub = (slice(None, None, 3), slice(None, None, 3))
    wx, wz = X[sub][walk[sub]], Z[sub][walk[sub]]
    dd, _ = cKDTree(np.c_[px, pz]).query(np.c_[wx, wz])
    S["coverage"] = dict(mean_m=round(float(dd.mean())), p90_m=round(float(np.percentile(dd, 90))), share_over_500m=round(float((dd > 500).mean()), 3))
    return S


# --------------------------------------------------------------------------------------------- feasibility map
def feasible(isl, sizes, keep_xy):
    """For each square footprint side: a boolean grid (6 m, every second land cell) of centres passing every FAIL rule
    (dynamic things - cabins, mine stations, paths, woods - left out: they are re-laid round the sites)."""
    ld = isl.land; ras = isl.rasters()
    s = 2
    xs, zs = ld.xs[::s], ld.zs[::s]
    X, Z = np.meshgrid(xs, zs)
    h6 = ld.h[::s, ::s]
    hh = np.nan_to_num(h6, nan=-9.0)
    level = np.round(np.nan_to_num(h6, nan=0.0) * 2) / 2
    cell = ld.cell * s
    sea6 = ~(hh > 0.0)
    dsea = ndimage.distance_transform_edt(~sea6) * cell
    piste_d = (ndimage.distance_transform_edt(~ras["piste"]) * ld.cell)[::s, ::s]
    air = G.airport_outside(X, Z) < L("airport_blend"); dair = ndimage.distance_transform_edt(~air) * cell
    strip = G.strip_outside(X, Z) < L("strip_block"); dstrip = ndimage.distance_transform_edt(~strip) * cell
    P = np.c_[X.ravel(), Z.ravel()]
    def tree_d(tree):
        return tree.query(P)[0].reshape(X.shape) if tree is not None else np.full(X.shape, np.inf)
    track = np.minimum.reduce([tree_d(isl.rail_trees.get(c)) for c in (0, 2, 3)])
    plat = tree_d(isl.platform_tree)
    dd, ii = isl.bench_tree.query(P, k=24, distance_upper_bound=80.0)
    reach = np.where(np.isfinite(dd), isl.bench_reach[np.minimum(ii, len(isl.bench_reach) - 1)], 0.0)
    bench = np.min(np.where(np.isfinite(dd), dd - reach, np.inf), 1).reshape(X.shape)
    walkable = (np.isfinite(ras["walk_plaza"]) | np.isfinite(ras["walk_rail"]) | np.isfinite(ras["walk_mine"]))[::s, ::s]
    westwalk = np.minimum(ras["walk_rail"], ras["walk_mine"])[::s, ::s]
    gdl = seg_dist(X, Z, isl.gb, isl.gt); towers = tree_d(cKDTree(isl.towers))
    sl = np.minimum.reduce([seg_dist(X, Z, isl.sb, isl.st) - L("skilift_corridor"), np.hypot(X - isl.sb[0], Z - isl.sb[1]) - L("skilift_bottom"),
                            np.hypot(X - isl.st[0], Z - isl.st[1]) - L("skilift_top"), seg_dist(X, Z, isl.sb, isl.finish) - L("finish_way")])
    su = np.minimum(seg_dist(X, Z, isl.slf, isl.slt) - L("summitlift_line"), np.minimum(np.hypot(X - isl.slf[0], Z - isl.slf[1]), np.hypot(X - isl.slt[0], Z - isl.slt[1])) - L("summitlift_end"))
    park = np.minimum(tree_d(cKDTree(isl.kickers)), G.to_polyline(X, Z, isl.pipe))
    lodge = np.minimum(np.hypot(X - isl.lodge[0], Z - isl.lodge[1]) - L("lodge_radius"), np.hypot(X - isl.hut[0], Z - isl.hut[1]) - 5.0)
    lake = np.hypot(X - G.LAKE[0], Z - G.LAKE[1]) - G.LAKE_RADIUS
    plaza = np.hypot(X - G.PLAZA[0], Z - G.PLAZA[1]) - G.PLAZA_RADIUS
    summit = np.hypot(X - G.SUMMIT[0], Z - G.SUMMIT[1]) - G.SUMMIT_RADIUS
    tower = np.hypot(X - isl.tower[0], Z - isl.tower[1]) - 8.0 if isl.tower is not None else np.full(X.shape, np.inf)
    gst = np.hypot(X - isl.gb[0], Z - isl.gb[1])
    creek = G.to_polyline(X, Z, G.CREEK); gorge = G.to_polyline(X, Z, G.GORGE)
    keep = np.min(np.hypot(X[..., None] - keep_xy[None, None, :, 0], Z[..., None] - keep_xy[None, None, :, 1]) - keep_xy[None, None, :, 2], -1) if len(keep_xy) else np.full(X.shape, np.inf)
    keepc = np.min(np.hypot(X[..., None] - keep_xy[None, None, :, 0], Z[..., None] - keep_xy[None, None, :, 1]), -1) if len(keep_xy) else np.full(X.shape, np.inf)
    out, why, whymap, clean = {}, {}, {}, {}
    fe, fb = L("flat_extra"), L("flat_blend")
    for side in sizes:
        r = side / math.sqrt(2.0)
        rad = int(math.ceil((r + fe) / cell)); yy, xx = np.mgrid[-rad:rad + 1, -rad:rad + 1]
        foot = (np.hypot(yy, xx) * cell <= r + fe)
        hmax = ndimage.maximum_filter(np.where(sea6, -1e9, hh), footprint=foot, mode="nearest")
        hmin = ndimage.minimum_filter(np.where(sea6, 1e9, hh), footprint=foot, mode="nearest")
        cut = np.maximum(hmax - level, level - hmin)
        radr = int(math.ceil((r + fe + fb) / cell)); yy, xx = np.mgrid[-radr:radr + 1, -radr:radr + 1]
        ringfp = np.abs(np.hypot(yy, xx) * cell - (r + fe + fb)) <= cell * 0.75
        rmax = ndimage.maximum_filter(np.where(sea6, -1e9, hh), footprint=ringfp, mode="nearest")
        rmin = ndimage.minimum_filter(np.where(sea6, 1e9, hh), footprint=ringfp, mode="nearest")
        ring = np.maximum(rmax - level, level - rmin)
        C = {}
        C["land"] = (hh > L("on_island")) & (dsea > r + fe)
        C["cut"] = cut <= L("cut_fail"); C["ring"] = ring <= L("ring_fail")
        C["rail"] = (bench > r + fe) & (track - r >= L("track_fail")) & (plat - r >= L("track_fail"))
        C["piste"] = piste_d > r + fe
        C["airport"] = (dair > r + fe) & (dstrip > r + fe)
        C["lifts"] = (gdl - r >= L("gondola_corridor")) & (towers - r >= L("pylon_fail")) & (sl - r >= 0) & (su - r >= 0) & (gst - r >= max(L("gondola_station"), 10 + fe))
        C["park_lodge"] = (park - r >= L("park_fail")) & (lodge - r >= fe)
        C["places"] = (lake - r >= fe + 8) & (plaza - r >= fe) & (summit - r >= fe) & (tower - r >= fe) & (creek - r >= fe + L("creek_banks")) & (gorge - r >= fe + L("gorge_walls"))
        C["spacing"] = (keepc >= L("spacing")) & (keep - r >= 2 * fe)
        C["reach"] = np.where(X > L("path_west"), np.isfinite(ras["walk_plaza"][::s, ::s]), np.isfinite(westwalk))
        ok = np.logical_and.reduce(list(C.values()))
        # tunnels under the flat: the flat's level must stay 9.5 m over the track
        if isl.tun_tree is not None:
            cand = np.argwhere(ok)
            pts = np.c_[X[ok], Z[ok]]
            lv = level[ok]
            bad = np.zeros(len(pts), bool)
            for q, near in enumerate(cKDTree(pts).query_ball_tree(isl.tun_tree, r + fe + G.Rail.TUNNEL_HALF)):
                if near and np.min(lv[q] - isl.tun_y[near]) < L("tunnel_enter"): bad[q] = True
            okk = ok.copy(); okk[tuple(cand[bad].T)] = False; ok = okk
        out[side] = ok
        # ...and with no warnings either: gentle ground, clear of the coast, the track, pistes, lines, lodge and airport
        clean[side] = ok & (cut <= L("cut_warn")) & (ring <= L("ring_warn")) & (dsea - r >= L("coast_warn")) & (track - r >= L("track_warn")) \
            & (piste_d - r >= L("piste_warn")) & (gdl - r >= L("lines_warn")) & (seg_dist(X, Z, isl.sb, isl.st) - r >= L("lines_warn")) \
            & (seg_dist(X, Z, isl.slf, isl.slt) - r >= L("lines_warn")) & (lodge - r >= L("lodge_warn")) & (G.airport_distance(X, Z) - r >= L("airport_warn")) \
            & (G.strip_distance(X, Z) - r >= L("strip_warn")) & (lake - r >= fe + 18) & (creek - r >= fe + fb + L("creek_banks")) & (gorge - r >= fe + fb + L("gorge_walls")) \
            & (plaza - r >= fe + 16) & (gst - r >= 10 + 9 + fe) & (np.where(X > L("path_west"), True, westwalk <= L("walk_warn")))
        land = hh > L("on_island")
        why[side] = {k: round(float((land & ~v).sum() * cell * cell / 1e4)) for k, v in C.items()}
        first = np.full(X.shape, -1)
        for q, (k, v) in enumerate(C.items()): first = np.where((first < 0) & land & ~v, q, first)
        whymap[side] = (list(C.keys()), first)
    return xs, zs, out, dict(westwalk=westwalk, h=h6, why=why, whymap=whymap, clean=clean, land_ha=round(float((hh > L("on_island")).sum() * cell * cell / 1e4)))


def draw_map(isl, path, xs, zs, feas, sizes, layout, keep_names, regions, clean=None):
    from PIL import Image, ImageDraw, ImageFont
    ld = isl.land
    S = 2      # pixels per 6 m cell
    hh = np.nan_to_num(ld.h[::2, ::2], nan=-9.0)
    Hh, W = hh.shape
    land = hh > 0.2
    gy, gx = np.gradient(ndimage.gaussian_filter(hh, 1.0), 6.0)
    shade = np.clip(0.85 + (-gx * 0.5 + gy * 0.7) * 0.35, 0.6, 1.15)
    img = np.zeros((Hh, W, 3)); img[:] = (0.12, 0.25, 0.38)
    base = np.array([0.80, 0.82, 0.86]) * shade[..., None]
    img[land] = base[land]
    blk = ld.blocked[::2, ::2]
    img[land & blk] = img[land & blk] * 0.86      # what the build blocks now (paths, structures, rocks): darker, for reference
    cols = {sizes[0]: (0.98, 0.80, 0.25), sizes[1] if len(sizes) > 1 else None: (0.35, 0.80, 0.40), sizes[-1]: (0.20, 0.55, 0.95)}
    for side in sizes:      # smallest first, so the biggest that fits is drawn on top; pale where it fits only with warnings
        c = np.array(cols.get(side, (0.9, 0.4, 0.9)))
        m = feas[side]; img[m] = img[m] * 0.6 + c * 0.4
        if clean is not None:
            m = clean[side]; img[m] = img[m] * 0.25 + c * 0.75 * 1.0
    im = Image.fromarray((np.clip(img[::-1], 0, 1) * 255).astype(np.uint8)).resize((W * S, Hh * S), Image.NEAREST).convert("RGBA")
    dr = ImageDraw.Draw(im, "RGBA")
    def P(x, z): return ((x - xs[0]) / 6.0 * S, (Hh - 1 - (z - zs[0]) / 6.0) * S)
    def font(sz):
        for f in ("arialbd.ttf", "arial.ttf", "DejaVuSans.ttf"):
            try: return ImageFont.truetype(f, sz)
            except OSError: pass
        return ImageFont.load_default()
    for gxm in range(-3000, -300, 250): dr.line([P(gxm, G.Z0), P(gxm, G.Z1)], fill=(255, 255, 255, 50))
    for gzm in range(-2500, -450, 250): dr.line([P(G.X0, gzm), P(G.X1, gzm)], fill=(255, 255, 255, 50))
    f12 = font(12); f14 = font(14); f18 = font(18)
    for gxm in range(-3000, -300, 500): dr.text(P(gxm + 4, G.Z1 - 4), str(gxm), fill=(255, 255, 255, 160), font=f12)
    for gzm in range(-2500, -450, 500): dr.text(P(G.X0 + 4, gzm + 14), str(gzm), fill=(255, 255, 255, 160), font=f12)
    dr.line([P(L("path_west"), G.Z0), P(L("path_west"), G.Z1)], fill=(255, 255, 255, 120), width=1)
    dr.text(P(L("path_west") + 6, G.Z0 + 60), "no paths west of x = -2300", fill=(255, 255, 255, 170), font=f12)
    # pistes (outline), airport, airstrip, lifts
    pm = isl.rasters()["piste"][::2, ::2]
    edge = pm & ~ndimage.binary_erosion(pm)
    for j, i in np.argwhere(edge): x, y = P(xs[i], zs[j]); dr.point((x, y), fill=(60, 110, 230, 255))
    def poly(pts, **kw): dr.polygon([P(*p) for p in pts], **kw)
    u = G.RUNWAY_HALF_LENGTH; v = G.RUNWAY_HALF_WIDTH
    at = lambda uu, vv: G.RUNWAY + G._F * uu + G._R * vv
    poly([at(-u, -v), at(u, -v), at(u, v), at(-u, v)], fill=(70, 70, 80, 200))
    poly([at(G.APRON_U0, G.APRON_V0), at(G.APRON_U1, G.APRON_V0), at(G.APRON_U1, G.APRON_V1), at(G.APRON_U0, G.APRON_V1)], fill=(90, 90, 100, 180))
    sat = lambda rr, ss: G.SUMMIT + G._SD * rr + G._SR * ss
    poly([sat(G.STRIP_FROM, -G.STRIP_HALF), sat(G.STRIP_TO, -G.STRIP_HALF), sat(G.STRIP_TO, G.STRIP_HALF), sat(G.STRIP_FROM, G.STRIP_HALF)], fill=(110, 80, 50, 220))
    dr.line([P(*isl.gb), P(*isl.gt)], fill=(200, 30, 30, 230), width=2)
    dr.line([P(*isl.sb), P(*isl.st)], fill=(230, 120, 20, 230), width=2)
    dr.line([P(*isl.slf), P(*isl.slt)], fill=(230, 120, 20, 230), width=2)
    for p in isl.pylons: x, y = P(*p); dr.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(200, 30, 30, 255))
    x, y = P(*isl.lodge); dr.rectangle([x - 4, y - 4, x + 4, y + 4], fill=(120, 60, 20, 255))
    # the railway by kind
    kcol = {0: (130, 20, 30, 255), 1: (40, 40, 40, 150), 2: (240, 130, 0, 255), 3: (40, 40, 40, 255)}
    r = isl.rail; off = 0
    for l in r.lines:
        n = len(l["p"]); pts = l["p"][:, [0, 2]]; cls = r.cls[off:off + n]
        for q in range(n - (0 if l["closed"] else 1)):
            a, b = pts[q], pts[(q + 1) % n]
            dr.line([P(*a), P(*b)], fill=kcol[int(cls[q])], width=3 if cls[q] != 1 else 2)
        off += n
    labelled = set()
    for name, x0, z0 in isl.stations:
        x, y = P(x0, z0); dr.rectangle([x - 6, y - 6, x + 6, y + 6], fill=(255, 255, 255, 255), outline=(130, 20, 30, 255), width=2)
        if name not in labelled: dr.text((x + 8, y + 4), name, fill=(255, 235, 200, 255), font=f14, stroke_width=2, stroke_fill=(30, 30, 30, 255))
        labelled.add(name)
    for q, (x0, z0) in enumerate(isl.mine):
        x, y = P(x0, z0); dr.rectangle([x - 5, y - 5, x + 5, y + 5], fill=(30, 30, 35, 255), outline=(240, 240, 240, 255), width=1)
        dr.text((x + 6, y + 2), "M%d" % (q + 1), fill=(230, 230, 230, 255), font=f12, stroke_width=1, stroke_fill=(20, 20, 20, 255))
    for x0, z0 in isl.lookouts:
        x, y = P(x0, z0); dr.regular_polygon((x, y, 7), 4, fill=(250, 250, 250, 255), outline=(40, 100, 220, 255))
    # sites: kept ones gold, the rest red (radius drawn to scale, with the 70 m spacing ring dashed for kept ones)
    for name, (x0, z0, rr) in layout.items():
        x, y = P(x0, z0); pr = rr / 6.0 * S
        kept = name in keep_names
        dr.ellipse([x - pr, y - pr, x + pr, y + pr], fill=(240, 190, 40, 230) if kept else (220, 40, 40, 230), outline=(255, 255, 255, 255), width=2)
        if kept:
            sr = L("spacing") / 6.0 * S; dr.ellipse([x - sr, y - sr, x + sr, y + sr], outline=(240, 190, 40, 200), width=1)
        dr.text((x + pr + 2, y - 7), name, fill=(255, 255, 255, 255), font=f12, stroke_width=2, stroke_fill=(40, 10, 10, 255))
    x, y = P(*G.PLAZA); dr.regular_polygon((x, y, 10), 3, fill=(20, 120, 50, 255), outline=(255, 255, 255, 255))
    for k, reg in enumerate(regions):
        x, y = P(*reg["best"]); dr.ellipse([x - 9, y - 9, x + 9, y + 9], outline=(255, 0, 255, 255), width=3)
        dr.text((x - 5, y - 8), chr(65 + k), fill=(255, 0, 255, 255), font=f14, stroke_width=2, stroke_fill=(255, 255, 255, 255))
    # key
    kw = 330
    sheet = Image.new("RGBA", (im.width + kw, im.height), (24, 28, 36, 255)); sheet.paste(im, (0, 0))
    kd = ImageDraw.Draw(sheet); xk = im.width + 16; yk = 16
    kd.text((xk, yk), "Where a site could go", fill=(255, 255, 255, 255), font=f18); yk += 30
    for side in sorted(sizes, reverse=True):
        c = tuple(int(v * 255) for v in cols.get(side, (0.9, 0.4, 0.9)))
        kd.rectangle([xk, yk, xk + 18, yk + 14], fill=c + (255,))
        kd.text((xk + 26, yk - 1), "%d x %d m (r %.1f): %d ha, %d clean" % (side, side, side / math.sqrt(2), int(feas[side].sum() * 36 / 1e4), int(clean[side].sum() * 36 / 1e4) if clean else 0), fill=(230, 230, 230, 255), font=f14); yk += 22
    yk += 6
    for txt, col in (("strong colour: passes every rule, no warnings", None), ("pale: passes, with warnings (steep blend,", None), ("   near coast, track, piste, lines...)", None),
                     ("all 70 m from the sites that stay", None), ("   (gold; ring = 70 m)", None), ("red: sites that may move", (220, 40, 40)),
                     ("dark red / orange / grey: rail on the ground /", (130, 20, 30)), ("   trestle / tunnel; white squares: stations", None),
                     ("black squares M1..: mine stations (move", (30, 30, 35)), ("   on rebuild: not a constraint)", None),
                     ("blue outline: pistes; grey: airport", (60, 110, 230)), ("brown: summit airstrip; red line: gondola", (110, 80, 50)),
                     ("orange lines: ski lift, summit lift", (230, 120, 20)), ("white diamonds: Lookouts' far sites", (250, 250, 250)),
                     ("darker land: blocked now (paths, rocks,", None), ("   structures) - re-laid round new sites", None),
                     ("round a built site the colour shows its own", None), ("   levelled flat: room there is overstated", None),
                     ("magenta A..: under-served regions", (255, 0, 255))):
        if col: kd.rectangle([xk, yk + 2, xk + 12, yk + 12], fill=col + (255,))
        kd.text((xk + 18, yk), txt, fill=(210, 215, 225, 255), font=f12); yk += 18
    yk += 8
    for k, reg in enumerate(regions):
        kd.text((xk, yk), "%s  (%d, %d)  %s %.0f m" % (chr(65 + k), reg["best"][0], reg["best"][1], reg["station"], reg["station_dist"]), fill=(255, 150, 255, 255), font=f12); yk += 16
    sheet.convert("RGB").save(path)


STATION_WALK = 500.0      # (LIMITS["station_walk"], read at run time below)


def occupied(xs, zs, current_xyr, r_new):
    """Centres whose flat (r_new + 2) would reach a current site's flat or blend (r + 12): there the land grid holds that
    site's levelled ground, so 'room' would be overstated (it slopes again once the site moves)."""
    X, Z = np.meshgrid(xs, zs); occ = np.zeros(X.shape, bool)
    for x, z, r in current_xyr: occ |= np.hypot(X - x, Z - z) < r + L("flat_extra") + L("flat_blend") + r_new + L("flat_extra")
    return occ


def station_room(isl, xs, zs, feas, sizes, puzzle_xy, nearest, clean, occ):
    """For each railway station: how much free feasible ground lies within STATION_WALK m on foot of it (cells whose
    nearest station on foot it is; ground held by a current site's flat left out), and the spot (smallest footprint)
    farthest from every puzzle there."""
    ras = isl.rasters()
    wr = ras["walk_rail"][::2, ::2]; src = ras["rail_src"][::2, ::2]
    X, Z = np.meshgrid(xs, zs)
    dpz = cKDTree(puzzle_xy).query(np.c_[X.ravel(), Z.ravel()])[0].reshape(X.shape)
    out = {}
    for name in isl.station_names:
        ids = [q for q, (n, _, _) in enumerate(isl.stations) if n == name]
        within = np.isin(src, ids) & (wr <= L("station_walk")) & ~occ
        rm = dict(nearest_puzzle=nearest[name], ha={str(s): round(float((feas[s] & within).sum() * 36 / 1e4), 1) for s in sizes},
                  clean_ha={str(s): round(float((clean[s] & within).sum() * 36 / 1e4), 1) for s in sizes}, best=None)
        m = clean[sizes[0]] & within
        rm["best_clean"] = bool(m.any())
        if not m.any(): m = feas[sizes[0]] & within
        if m.any():
            q = np.unravel_index(np.argmax(np.where(m, dpz, -1)), m.shape)
            j, i = isl.land.cell_of(X[q], Z[q])
            rm.update(best=(round(float(X[q])), round(float(Z[q]))), best_walk=round(float(wr[q])), best_far=round(float(dpz[q])), best_h=round(float(isl.land.h[j, i]), 1))
        out[name] = rm
    return out


def draw_why(isl, path, xs, zs, whymap, side):
    """For one footprint: each land cell coloured by the first rule it fails (feasible cells white)."""
    from PIL import Image, ImageDraw, ImageFont
    names, first = whymap
    pal = [(60, 60, 60), (230, 120, 40), (240, 200, 60), (150, 20, 30), (60, 110, 230), (110, 110, 120), (200, 30, 200), (120, 70, 30), (40, 160, 160), (240, 190, 40), (120, 200, 90)]
    hh = np.nan_to_num(isl.land.h[::2, ::2], nan=-9.0)
    img = np.zeros(first.shape + (3,), np.uint8); img[:] = (25, 50, 80)
    img[hh > 0.2] = (255, 255, 255)
    for q in range(len(names)): img[first == q] = pal[q % len(pal)]
    im = Image.fromarray(img[::-1]).resize((first.shape[1] * 2, first.shape[0] * 2), Image.NEAREST)
    sheet = Image.new("RGB", (im.width + 220, im.height), (24, 28, 36)); sheet.paste(im, (0, 0))
    d = ImageDraw.Draw(sheet)
    try: f = ImageFont.truetype("arial.ttf", 14)
    except OSError: f = ImageFont.load_default()
    d.text((im.width + 12, 12), "%d m footprint: first rule failed" % side, fill=(255, 255, 255), font=f)
    for q, n in enumerate(names + ["fits"]):
        c = pal[q % len(pal)] if q < len(names) else (255, 255, 255)
        d.rectangle([im.width + 12, 40 + q * 22, im.width + 30, 54 + q * 22], fill=c); d.text((im.width + 38, 39 + q * 22), n, fill=(230, 230, 230), font=f)
    sheet.save(path)


def regions_of(isl, xs, zs, feas_mask, extra, puzzle_xy, min_far=200.0, min_area_ha=0.3, clean=None, occ=None):
    """Under-served regions: free feasible ground (for the given footprint) at least min_far from every puzzle of the
    layout, reachable on foot, grouped into connected patches; those within STATION_WALK m on foot of a railway station
    first (the others are flagged far)."""
    X, Z = np.meshgrid(xs, zs)
    dpz, _ = cKDTree(puzzle_xy).query(np.c_[X.ravel(), Z.ravel()]); dpz = dpz.reshape(X.shape)
    if occ is not None: feas_mask = feas_mask & ~occ
    m = feas_mask & (dpz >= min_far)
    lab, n = ndimage.label(ndimage.binary_closing(m, iterations=1) & feas_mask, structure=np.ones((3, 3)))
    ras = isl.rasters(); out = []
    for k in range(1, n + 1):
        cells = lab == k
        area = cells.sum() * 36.0 / 1e4
        if area < min_area_ha: continue
        pick = cells & clean if clean is not None and (cells & clean).any() else cells
        far = np.where(pick, dpz, -1)
        q = np.unravel_index(np.argmax(far), far.shape)
        bx, bz = float(X[q]), float(Z[q])
        d, s = pts_dist(np.array([bx]), np.array([bz]), isl.stn_xz)
        j, i = isl.land.cell_of(bx, bz)
        # how many sites of this size fit at 70 m spacing (greedy, farthest from puzzles first)
        pts = np.c_[X[cells], Z[cells]][::2]; order = np.argsort(-dpz[cells][::2]); chosen = []
        for o in order:
            if all(math.hypot(pts[o, 0] - c[0], pts[o, 1] - c[1]) >= L("spacing") for c in chosen): chosen.append(pts[o])
        out.append(dict(best=(round(bx), round(bz)), best_clean=bool(clean is not None and clean[q]), area_ha=round(float(area), 1),
                        clean_ha=round(float((cells & clean).sum() * 36 / 1e4), 1) if clean is not None else None, far_from_puzzles=round(float(dpz[q])),
                        x_range=(round(float(X[cells].min())), round(float(X[cells].max()))), z_range=(round(float(Z[cells].min())), round(float(Z[cells].max()))),
                        height=round(float(isl.land.h[j, i]), 1), station=isl.stations[int(s[0])][0], station_dist=round(float(d[0])),
                        walk_plaza=round(float(ras["walk_plaza"][j, i])) if np.isfinite(ras["walk_plaza"][j, i]) else None,
                        walk_station=round(float(ras["walk_rail"][j, i])) if np.isfinite(ras["walk_rail"][j, i]) else None,
                        fits_at_70m=len(chosen), path=bool(bx > L("path_west"))))
    for r in out: r["near_station"] = r["walk_station"] is not None and r["walk_station"] <= L("station_walk")
    out.sort(key=lambda r: (not r["near_station"], -r["area_ha"] * r["far_from_puzzles"]))
    return out


# --------------------------------------------------------------------------------------------- main
def explain():
    print("%-17s %-22s %-40s %s" % ("key", "value", "source", "meaning"))
    for k, (v, src, txt) in LIMITS.items(): print("%-17s %-22s %-40s %s" % (k, v, src, txt))
    print("\nWaived (reported WAIVED, not PASS):")
    for n, w in WAIVE.items(): print("  %-12s %s" % (n, ", ".join(sorted(w))))
    print("  summit packs: spacing among themselves; Lookouts: spacing to its own far sites")
    print("Scenery packs (PuzzleType 2: CastIronPizza, SummitDiscGolf, IslandGear) are not sites (the island neither levels nor avoids them): listed, not checked.")


def main():
    ap = argparse.ArgumentParser(description="Winter Isle site checker", formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    ap.add_argument("plan", nargs="?")
    ap.add_argument("--current", action="store_true"); ap.add_argument("--only", action="store_true")
    ap.add_argument("--site", action="append", default=[]); ap.add_argument("--set", action="append", default=[])
    ap.add_argument("--json"); ap.add_argument("--write-plan"); ap.add_argument("--map"); ap.add_argument("--sizes", default="26,34,48")
    ap.add_argument("--keep"); ap.add_argument("--avoid-plan", action="store_true"); ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--why"); ap.add_argument("--far", type=float, default=200.0); ap.add_argument("--region-size", type=int, default=26)
    ap.add_argument("--explain", action="store_true"); ap.add_argument("--write-footprints", action="store_true"); ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    for kv in a.set:
        k, v = kv.split("=", 1)
        if k not in LIMITS or not isinstance(LIMITS[k][0], float): print("cannot set", k, "(see --explain)"); return 2
        LIMITS[k] = (float(v),) + LIMITS[k][1:]
    if a.explain: explain(); return 0
    if a.write_footprints: write_footprints(); return 0
    if a.selftest:
        res = G.selftest(); return 0 if all(e < 0.05 for _, e in res) else 1
    if not a.plan and not a.current and not a.site and not a.map: ap.print_usage(); return 2
    try:
        fps = load_footprints()
        cur = current_sites(fps)
        sites, extra_fp = {}, {}
        if a.plan:
            pj = json.load(open(a.plan))
            for n, v in pj.get("footprints", {}).items(): extra_fp[n] = (float(v[0]), float(v[1]))
            for n, v in pj.get("sites", pj).items():
                if n == "footprints": continue
                if isinstance(v, dict): v = [v["x"], v["z"]] + list(v.get("footprint", []))
                sites[n] = (float(v[0]), float(v[1]))
                if len(v) >= 4: extra_fp[n] = (float(v[2]), float(v[3]))
        for s in a.site:
            n, v = s.split("=", 1); v = [float(t) for t in v.split(",")]
            sites[n] = (v[0], v[1])
            if len(v) >= 4: extra_fp[n] = (v[2], v[3])
        if not a.only:
            for n, xy in cur.items(): sites.setdefault(n, xy)
        for n in sites:
            if n not in fps and n not in extra_fp: raise ValueError("no footprint for %s: give [x, z, fx, fz]" % n)
        isl = Island()
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as e:
        print("error:", e, file=sys.stderr); return 2
    log("island loaded")
    isl.rasters(); log("rasters ready")
    info = {}
    for n in sites:
        fp = extra_fp.get(n, tuple(fps[n]["footprint"]) if n in fps else None)
        t = fps[n]["type"] if n in fps else 0
        info[n] = dict(footprint=fp, radius=math.hypot(*fp) / 2.0, type=t, moved=(n not in cur or (abs(cur[n][0] - sites[n][0]) > 0.01 or abs(cur[n][1] - sites[n][1]) > 0.01)))
    checked = sorted([n for n in sites if info[n]["type"] != 2], key=lambda n: (-sites[n][1], sites[n][0]))
    scenery = [n for n in sites if info[n]["type"] == 2]
    xs = np.array([sites[n][0] for n in checked]); zs = np.array([sites[n][1] for n in checked]); rs = np.array([info[n]["radius"] for n in checked])
    M = measure(isl, xs, zs, rs); log("measured")
    fixed_extra = [("Lookouts far %d" % (q + 1), ex, ez, er) for q, (ex, ez, er) in enumerate(G.LOOKOUT_EXTRA)]      # fixed: always kept clear
    alln, C, E, sp = spacing(checked, xs, zs, rs, fixed_extra)
    report = {"version": VERSION, "island_key": isl.key, "sites": {}, "scenery": {n: list(sites[n]) for n in scenery}}
    nfail = nwarn = 0
    lines = []
    for k, n in enumerate(checked):
        res = rules(M, k, rs, xs, WAIVE.get(n, set()), n) + sp[n]
        st = "FAIL" if any(s == "FAIL" for s, _, _ in res) else "WARN" if any(s == "WARN" for s, _, _ in res) else "PASS"
        nfail += st == "FAIL"; nwarn += st == "WARN"
        meas = {}
        for key, arr in M.items():
            val = arr[k]
            meas[key] = (val.item() if hasattr(val, "item") else val)
            if isinstance(meas[key], float): meas[key] = round(meas[key], 2) if np.isfinite(meas[key]) else None
        others = sorted(((alln[b], round(float(C[k, b]), 1), round(float(E[k, b]), 1)) for b in range(len(alln)) if b != k), key=lambda t: t[1])
        report["sites"][n] = dict(x=sites[n][0], z=sites[n][1], footprint=info[n]["footprint"], radius=round(info[n]["radius"], 2), moved=info[n]["moved"],
                                  status=st, results=[dict(status=s, check=c, message=m) for s, c, m in res], measures=meas,
                                  others=[dict(name=o, centre=c, edge=e) for o, c, e in others])
        m = report["sites"][n]["measures"]
        ws = [w for w in (m["walk_rail"], m["walk_mine"]) if w is not None]
        if xs[k] > L("path_west"): walk = ("path from plaza %.0f m on foot" % m["walk_plaza"]) if m["walk_plaza"] is not None else "NOT walkable from the plaza"
        else: walk = ("no path (west of -2300); station %.0f m on foot" % min(ws)) if ws else "no path (west of -2300); NOT walkable from any station"
        near3 = ", ".join("%s %.0f/%.0f" % o for o in others[:3])
        lines.append("%-4s %-17s (%7.1f, %7.1f) %2.0fx%-2.0f r%4.1f%s" % (st, n, xs[k], zs[k], info[n]["footprint"][0], info[n]["footprint"][1], rs[k], "  [moved]" if info[n]["moved"] and n in cur else ("  [new]" if n not in cur else "")))
        lines.append("      %s, cut/fill %.1f m (at r+12: %.1f), slope %.0f%%, coast %.0f m from edge | track %.0f m from edge, station %s %.0f m from centre (%s m on foot), mine %s %.0f m from centre | %s"
                     % ("ground %.1f m" % m["height"] if m["height"] > -50 else "SEA", m["cut"], m["ring"], m["slope_pct"], m["coast_edge"], m["track_edge"], m["station"], m["station_dist"],
                        "%.0f" % m["walk_rail"] if m["walk_rail"] is not None else "-", m["mine_station"], m["mine_dist"], walk))
        lines.append("      nearest sites (centre to centre / edge to edge, m): " + near3)
        for s, c, msg in res: lines.append("      %-6s %-15s %s" % (s, c, msg))
        if a.verbose:
            lines.append("      " + "; ".join("%s=%s" % (kk, vv) for kk, vv in m.items()))
    puzzle_mask = np.array([n not in ("TheTree",) for n in checked])
    S = spread(isl, checked, xs, zs, puzzle_mask)
    report["spread"] = S
    report["matrix"] = dict(names=alln, centre=np.round(C, 1).tolist(), edge=np.round(E, 1).tolist())
    print("Winter Isle site check: %d sites (%d moved/new), island build %s" % (len(checked), sum(1 for n in checked if info[n]["moved"]), isl.key))
    print("\n".join(lines))
    if scenery: print("scenery (not sites; not checked): " + ", ".join("%s (%.0f, %.0f)" % (n, *sites[n]) for n in scenery))
    print("\nSpread (%d puzzles; TheTree left out):" % int(puzzle_mask.sum()))
    print("  within %.0f m of the plaza: %d of %d (%.0f%%)" % (L("near_plaza"), S["within_400m_of_plaza"][0], S["within_400m_of_plaza"][1], 100 * S["within_400m_of_plaza"][2]))
    print("  by quarter about the land's middle %s: %s   (land share: %s)" % (S["land_centre"], ", ".join("%s %d" % (q, len(v)) for q, v in S["by_quarter"].items()),
                                                                         ", ".join("%s %.0f%%" % (q, 100 * v) for q, v in S["land_share_by_quarter"].items())))
    print("  by station catchment: " + ", ".join("%s %d" % (q, len(v)) for q, v in S["by_station_catchment"].items()))
    nn = S["nearest_neighbour_stats"]
    print("  nearest neighbour: min %.0f, median %.0f, mean %.0f m; %d under 70 m, %d under 150 m" % (nn["min"], nn["median"], nn["mean"], nn["under_70"], nn["under_150"]))
    print("  stations with a puzzle within %.0f m: %d of %d (%s)" % (L("near_station"), S["stations_with_puzzle_within_300m"][0], S["stations_with_puzzle_within_300m"][1],
                                                                ", ".join("%s %s %d m" % (s, v[0], v[1]) for s, v in S["stations_nearest_puzzle"].items())))
    cv = S["coverage"]
    print("  walkable land to the nearest puzzle: mean %d m, 90%% within %d m, %.0f%% farther than 500 m" % (cv["mean_m"], cv["p90_m"], 100 * cv["share_over_500m"]))
    print("\n%d PASS, %d WARN, %d FAIL" % (len(checked) - nfail - nwarn, nwarn, nfail))
    if a.map:
        sizes = [int(s) for s in a.sizes.split(",")]
        keep_names = set(a.keep.split(",")) if a.keep else set(DEFAULT_KEEP)
        if a.avoid_plan: keep_names = set(checked)
        keep_xy = np.array([[sites[n][0], sites[n][1], info[n]["radius"]] for n in checked if n in keep_names] + [[e[1], e[2], e[3]] for e in fixed_extra] or np.zeros((0, 3)))
        xg, zg, feas, extra = feasible(isl, sizes, keep_xy); log("feasibility done")
        mid = a.region_size if a.region_size in sizes else sorted(sizes)[len(sizes) // 2]
        built = [(cur[n][0], cur[n][1], math.hypot(*fps[n]["footprint"]) / 2.0) for n in cur if n in fps and fps[n]["type"] != 2]
        occ = occupied(xg, zg, built, mid / math.sqrt(2.0))      # ground the built sites' flats hold: not free room
        regs = regions_of(isl, xg, zg, feas[mid], extra, np.c_[xs[puzzle_mask], zs[puzzle_mask]], min_far=a.far, clean=extra["clean"][mid], occ=occ)
        layout = {n: (sites[n][0], sites[n][1], info[n]["radius"]) for n in checked}
        draw_map(isl, a.map, xg, zg, feas, sorted(sizes), layout, keep_names, regs[:12], extra["clean"])
        report["feasible_ha"] = {str(s): round(float(feas[s].sum() * 36 / 1e4), 1) for s in sizes}
        report["clean_ha"] = {str(s): round(float(extra["clean"][s].sum() * 36 / 1e4), 1) for s in sizes}
        report["regions"] = regs
        print("\nFeasible ground (passes every FAIL rule, 70 m from %s): %s" % (", ".join(sorted(keep_names)), ", ".join("%d m: %.0f ha (%.0f ha with no warnings)" % (s, feas[s].sum() * 36 / 1e4, extra["clean"][s].sum() * 36 / 1e4) for s in sizes)))
        for sz in sizes: print("  %d m: land %d ha; ha of land failing each rule: %s" % (sz, extra["land_ha"], ", ".join("%s %d" % kv for kv in extra["why"][sz].items())))
        print("Under-served regions (%d x %d m fits on free ground, %d m or more from every puzzle of the layout); within %d m on foot of a station first:" % (mid, mid, a.far, L("station_walk")))
        for k, rg in enumerate(regs[:12]):
            print("  %s best (%d, %d)%s h %.0f m, %.1f ha (%.1f clean) over x %d..%d z %d..%d, %d m from the nearest puzzle, %d fit at 70 m; station %s %d m (walk %s); %s"
                  % (chr(65 + k), rg["best"][0], rg["best"][1], "" if rg["best_clean"] else " [with warnings]", rg["height"], rg["area_ha"], rg["clean_ha"] or 0, rg["x_range"][0], rg["x_range"][1], rg["z_range"][0], rg["z_range"][1],
                     rg["far_from_puzzles"], rg["fits_at_70m"], rg["station"], rg["station_dist"], rg["walk_station"],
                     ("path from plaza %s m" % rg["walk_plaza"]) if rg["path"] else "no path (west of -2300)") + ("" if rg["near_station"] else "  [FAR: over %d m on foot from any station]" % L("station_walk")))
        room = station_room(isl, xg, zg, feas, sorted(sizes), np.c_[xs[puzzle_mask], zs[puzzle_mask]], S["stations_nearest_puzzle"], extra["clean"], occ)
        report["station_room"] = room
        print("Room by station (free feasible ground - not on a built site's flat - whose nearest station on foot is this one, within %d m walk):" % L("station_walk"))
        for name, rm in room.items():
            print("  %-13s nearest puzzle %-14s %4d m | feasible (clean) ha %s | best spot %s" % (name, rm["nearest_puzzle"][0], rm["nearest_puzzle"][1],
                  ", ".join("%s m %.1f (%.1f)" % (k, v, rm["clean_ha"][k]) for k, v in rm["ha"].items()),
                  ("(%d, %d)%s, %d m walk, %d m from the nearest puzzle, h %.0f m" % (rm["best"][0], rm["best"][1], "" if rm["best_clean"] else " [with warnings]", rm["best_walk"], rm["best_far"], rm["best_h"])) if rm["best"] else "none"))
        print("wrote", a.map)
        if a.why: draw_why(isl, a.why, xg, zg, extra["whymap"][mid], mid); print("wrote", a.why)
    if a.json:
        json.dump(report, open(a.json, "w"), indent=1, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o)); print("wrote", a.json)
    if a.write_plan:
        json.dump({"sites": {n: [round(sites[n][0], 3), round(sites[n][1], 3)] for n in sorted(sites)}}, open(a.write_plan, "w"), indent=1); print("wrote", a.write_plan)
    log("done")
    return 1 if nfail else 0


if __name__ == "__main__":
    sys.exit(main())
