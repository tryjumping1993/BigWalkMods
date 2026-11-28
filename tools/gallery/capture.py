"""Shoots every island puzzle for the gallery: from above, four raised views from outside its footprint, the start
corner from both sides, and one while it runs. HUD hidden. Resumable (a puzzle whose 'running' picture exists is skipped)."""
import json, math, os, re, shutil, sys, time, urllib.parse, urllib.request

REPO = r"Z:\Programming\BigWalkMods"
SHOTS = os.path.join(REPO, "shots")
RAW = os.path.join(os.path.dirname(os.path.abspath(__file__)), "raw")
SKIP = {"WinterIsle", "TheTree", "CastIronPizza", "Dinosaurs", "IslandGear", "SummitDiscGolf"}


def g(q, t=90):
    try:
        return urllib.request.urlopen("http://127.0.0.1:17361/" + q, timeout=t).read().decode()
    except Exception as e:
        return "ERR " + str(e)


def ground(x, z, fallback):
    m = re.search(r"ground: (-?[0-9.]+)", g("ground?x=%.2f&z=%.2f&all=1" % (x, z)))
    return float(m.group(1)) if m else fallback


def shot(name, view, cam, tgt, fov):
    r = g("shot?file=%s-%s&cx=%.2f&cy=%.2f&cz=%.2f&tx=%.2f&ty=%.2f&tz=%.2f&fov=%d" % (name, view, cam[0], cam[1], cam[2], tgt[0], tgt[1], tgt[2], fov))
    src = os.path.join(SHOTS, "%s-%s.png" % (name, view))
    if r.startswith("saved") and os.path.exists(src):
        shutil.move(src, os.path.join(RAW, "%s-%s.png" % (name, view)))
    else:
        print("  %s-%s: %s" % (name, view, r.strip()[:100]))


os.makedirs(RAW, exist_ok=True)
foot = json.load(open(os.path.join(REPO, "tools", "sites", "footprints.json")))
roots = {m.group(1): [float(m.group(i)) for i in (2, 3, 4)] for m in re.finditer(r"puzzle: (\S+) at (-?[0-9.]+), (-?[0-9.]+), (-?[0-9.]+)", g("status"))}
names = sys.argv[1:] or sorted(n for n in roots if n not in SKIP)
g("render?awake=1")
print("hide HUD:", g("active?name=GameOverlay&on=0").strip()[:80])
try:
    for name in names:
        if os.path.exists(os.path.join(RAW, name + "-running.png")) and not sys.argv[1:]:
            continue
        x, y, z = roots[name]
        f = foot.get(name, {}).get("footprint", [30.0, 30.0])
        R = max(f) / 2
        t0 = time.time()
        g("teleport?puzzle=" + name); time.sleep(4)
        g("time?hour=13"); time.sleep(0.5)
        gy = ground(x, z, y)
        mid = [x, gy + 1.0, z]
        shot(name, "aerial", [x + R * 0.35, gy + R * 1.5 + 9, z - R * 0.95], [x, gy, z], 58)
        for key, dx, dz in (("s", 0, -1), ("e", 1, 0), ("n", 0, 1), ("w", -1, 0)):
            cx, cz = x + dx * (R * 0.95 + 4), z + dz * (R * 0.95 + 4)
            cy = max(ground(cx, cz, gy), gy) + R * 0.32 + 3.2
            shot(name, key, [cx, cy, cz], mid, 62)
        # the start corner (start switch, gourd vice, how-to board), from the middle's side and from outside
        sw = None
        for line in g("find?name=VanillaStartSwitch&max=200").splitlines():
            if line.startswith(name + "/") and " | " in line and "inactive" not in line:
                here = [float(v) for v in line.split(" | ")[1].split(",")]
                if sw is None or "Switch Start/" in line: sw = here
        if sw:
            dx, dz = sw[0] - x, sw[2] - z
            d = math.hypot(dx, dz) or 1.0
            dx, dz = dx / d, dz / d
            for key, sgn in (("start-in", -1), ("start-out", 1)):
                shot(name, key, [sw[0] + dx * sgn * 6.0, sw[1] + 1.6, sw[2] + dz * sgn * 6.0], [sw[0], sw[1] + 0.9, sw[2]], 60)
        else:
            print("  %s: no start switch found" % name)
        g("puzzle?name=%s&do=start" % name); time.sleep(4)
        g("time?hour=13")
        shot(name, "running", [x - R * 0.35, gy + R * 1.5 + 9, z - R * 0.95], [x, gy, z], 58)
        g("puzzle?name=%s&do=stop" % name); time.sleep(1.5)
        open(os.path.join(RAW, name + ".log"), "w", encoding="utf-8").write("%s at (%.1f, %.1f, %.1f)\n" % (name, x, y, z))
        print("%-18s %.0f s" % (name, time.time() - t0), flush=True)
finally:
    print("show HUD:", g("active?name=GameOverlay&on=1").strip()[:80])
