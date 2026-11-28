"""Records gameplay scenes from the running game as frame sequences (the companion's 'shot') and encodes MP4s.
The PC may be locked (a screen recorder would see the lock screen), so every frame is the game's own screenshot.
Fast things (skis, planes, sitting down) are shot in slow motion (Time.timeScale) so the frames come out smooth;
rides that run on the network clock (trains, mantrips, lifts) are shot in real time and played back faster.

  python rec.py <scene> [<scene> ...]        scenes: ski plane sit trainseat train mantrip lift
"""
import glob, math, os, re, subprocess, sys, time, urllib.parse, urllib.request

REPO = r"Z:\Programming\BigWalkMods"
SHOTS = os.path.join(REPO, "shots")
OUT = r"Z:\Temp\claude\BigWalkMods\videos"
FFMPEG = r"Z:\Tools\ffmpeg\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe"


def g(q, t=60):
    try:
        return urllib.request.urlopen("http://127.0.0.1:17361/" + q, timeout=t).read().decode()
    except Exception as e:
        return "ERR " + str(e)


def setv(path, value):
    return g("set?path=" + path + "&value=" + str(value))


def getv(path):
    return g("get?path=" + path).strip()


def player():
    line = g("player").splitlines()[0]
    n = [float(v) for v in re.findall(r"-?\d+\.\d+", line)]
    return n[0:3], n[3:6]


def pose():
    for l in g("player").splitlines():
        if l.startswith("pose"):
            return l.strip()
    return "?"


def find(name):
    r = g("find?name=" + urllib.parse.quote(name) + "&max=1").splitlines()
    if not r or " | " not in r[0]:
        raise SystemExit("not found: " + name)
    parts = r[0].split(" | ")
    return parts[0], [float(v) for v in parts[1].split(",")]


class Scene:
    def __init__(self, name, fps):
        self.name, self.fps, self.n = name, fps, 0
        for f in glob.glob(os.path.join(SHOTS, "v_%s_*.png" % name)):
            os.remove(f)
        self.t0 = time.time()

    def shot(self, cam=None, target=None, fov=62):
        self.n += 1
        if self.n % 40 == 1:
            g("time?hour=13")
        q = "shot?file=v_%s_%04d" % (self.name, self.n)
        if cam is not None:
            q += "&cx=%.2f&cy=%.2f&cz=%.2f&tx=%.2f&ty=%.2f&tz=%.2f&fov=%d" % (cam[0], cam[1], cam[2], target[0], target[1], target[2], fov)
        r = g(q)
        if not r.startswith("saved"):
            print("  shot %d: %s" % (self.n, r.strip()[:120]))

    def encode(self):
        out = os.path.join(OUT, self.name + ".mp4")
        cmd = [FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-framerate", str(self.fps), "-i", os.path.join(SHOTS, "v_%s_%%04d.png" % self.name),
               "-vf", "scale=1280:720", "-c:v", "libx264", "-preset", "slow", "-crf", "25", "-pix_fmt", "yuv420p", "-movflags", "+faststart", out]
        subprocess.run(cmd, check=True)
        keep = os.path.join(OUT, "frames", self.name)
        os.makedirs(keep, exist_ok=True)
        files = sorted(glob.glob(os.path.join(SHOTS, "v_%s_*.png" % self.name)))
        for i, f in enumerate(files):          # keep a few stills to look at, small
            if i % max(1, len(files) // 6) == 0:
                subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", f, "-vf", "scale=1280:720", os.path.join(keep, "%04d.jpg" % (i + 1))])
            os.remove(f)
        print("%s: %d frames in %.0f s -> %s (%.1f MB, %.1f s at %d fps)" % (self.name, self.n, time.time() - self.t0, out, os.path.getsize(out) / 1e6, self.n / self.fps, self.fps))


def stand_at(x, y, z, yaw):
    g("teleport?x=%.2f&y=%.2f&z=%.2f&yaw=%d" % (x, y, z, yaw)); time.sleep(3.5)
    g("press?action=jump&ms=200"); time.sleep(2.2)
    g("render?awake=1")


def pitch():
    m = re.search(r"\((-?[0-9.]+),", getv("UnityEngine.Camera.main.transform.eulerAngles"))
    x = float(m.group(1)) if m else 0.0
    return x - 360 if x > 180 else x          # degrees down


def look_to(down):
    # turn the view to 'down' degrees below level, by the look stick, reading the camera each time
    sign = -1
    for i in range(12):
        now = pitch()
        if abs(now - down) < 4: break
        ms = max(120, min(1500, int(abs(down - now) / 41.0 * 1000)))
        g("press?action=stickLookY&value=%d&ms=%d" % (sign if down > now else -sign, ms)); time.sleep(ms / 1000 + 0.5)
        after = pitch()
        if (after - now) * (down - now) < -1: sign = -sign      # it went the other way: the stick's sign is the other one
    return pitch()


def slow(scale):
    setv("UnityEngine.Time.timeScale", scale)


def chase(p, d, back, up, side=0.0):
    # camera behind 'p' along direction d (x, z unit), 'side' metres to its right
    return [p[0] - d[0] * back + d[1] * side, p[1] + up, p[2] - d[1] * back - d[0] * side]


def heading(v, last):
    s = math.hypot(v[0], v[2])
    return (v[0] / s, v[2] / s) if s > 0.7 else last


def ski():
    setv("WinterWalk.SkiDriver.Gear", 0)
    stand_at(-1775.8, 320.5, -1772.9, 0)          # the rack by Downhill's start: gear goes on here
    setv("WinterWalk.SkiDriver.Straight", "true")
    sc = Scene("ski", 20)
    d = (0.0, 1.0)
    p, v = player()
    for i in range(8):                            # at the rack, skis on
        sc.shot(chase(p, d, 4.2, 1.6, 1.2), [p[0], p[1] - 0.2, p[2]])
    setv("WinterWalk.SkiDriver.OffPisteSeconds", 600)
    g("press?action=moveY&value=1&ms=9000")       # walk onto the piste (real time)
    for i in range(40):
        p, v = player(); sc.shot(chase(p, d, 4.2, 1.6, 1.2), [p[0], p[1] - 0.2, p[2]])
        if "skiing" in getv("WinterWalk.SkiDriver.State"): break
    print("  ski after the walk:", getv("WinterWalk.SkiDriver.State"), p)
    slow(0.15)
    for i in range(330):
        p, v = player(); d = heading(v, d)
        sc.shot(chase(p, d, 5.0, 1.9, 0.8), [p[0] + d[0] * 2, p[1] - 0.3, p[2] + d[1] * 2])
        if i == 150: setv("WinterWalk.SkiDriver.Straight", "false")
        if i % 60 == 0: print("  ski", i, getv("WinterWalk.SkiDriver.State"))
    slow(1)
    setv("WinterWalk.SkiDriver.Gear", 0); setv("WinterWalk.SkiDriver.Straight", "false"); setv("WinterWalk.SkiDriver.OffPisteSeconds", 20)
    sc.encode()


def plane():
    P = "WinterWalk.PlaneDriver."
    stand_at(-828, 22.5, -1885, 270)
    setv(P + "TestPitch", 0); setv(P + "TestYaw", 270); setv(P + "TestThrottle", 0.01)
    sc = Scene("plane", 24)
    d = (-1.0, 0.0)
    p, v = player()
    for i in range(10):                           # walking up to the cockpit
        sc.shot(chase(p, d, 9, 3.0, 5), [p[0] - 4, p[1], p[2]])
    setv(P + "TestBoard", "true"); time.sleep(0.6)
    for i in range(10):                           # aboard
        p, v = player(); sc.shot(chase(p, d, 9, 3.0, 5), [p[0], p[1] - 0.3, p[2]])
    slow(0.2)
    setv(P + "TestThrottle", 1)
    for i in range(420):
        p, v = player(); d = heading(v, d)
        sc.shot(chase(p, d, 11, 3.2, 4.5), [p[0] + d[0] * 2, p[1] - 0.2, p[2] + d[1] * 2])
        if i == 40: setv(P + "TestYaw", 180)       # onto the runway
        if i == 110: setv(P + "TestPitch", 14)     # lift off
        if i == 260: setv(P + "TestYaw", 100); setv(P + "TestPitch", 4)      # a banked turn
        if i % 60 == 0: print("  plane", i, getv(P + "State"))
    slow(1)
    # down in the sea just off the runway's end: the pilot is put out to swim and the plane goes back to its stand
    setv(P + "TestPitch", -30)
    for i in range(60):
        if "parked" in getv(P + "State"): break
        time.sleep(1)
    print("  plane after:", getv(P + "State"))
    setv(P + "TestThrottle", 0); setv(P + "TestYaw", "NaN"); setv(P + "TestPitch", "NaN")
    sc.encode()


def sit_scene(name, seat_name, d, side_cam, up_cam=1.3):
    path, s = find(seat_name)
    stand_at(s[0], s[1] + 0.9, s[2] + d, 180)
    print("  ", name, "view %.0f degrees down" % look_to(32))
    sc = Scene(name, 24)
    cam = [s[0] + side_cam[0], s[1] + up_cam, s[2] + side_cam[1]]
    tgt = [s[0], s[1] + 0.35, s[2] + d * 0.4]
    for i in range(14): sc.shot(cam, tgt, 55)
    slow(0.25)
    g("press?action=use&ms=200")
    for i in range(70): sc.shot(cam, tgt, 55)
    print("  ", name, "after use:", pose(), "| cast:", g("use").split(",")[0], "|", getv("WinterWalk.SeatAssist.State"))
    for i in range(6): sc.shot()                  # the sitter's own view
    g("press?action=jump&ms=200")
    for i in range(50): sc.shot(cam, tgt, 55)
    slow(1)
    print("  ", name, "after jump:", pose())
    sc.encode()


def sit():
    sit_scene("sit", "Table Chair", 1.1, (2.3, 1.6))


def trainseat():
    path, s = find("Train 7 Car 2")
    # (the aisle between the second row's seats, as the earlier test stood)
    stand_at(-2292.54, 539.4, -1854.09, 120)
    print("   trainseat view %.0f degrees down" % look_to(5))
    sc = Scene("trainseat", 24)
    for i in range(14): sc.shot()
    slow(0.25)
    g("press?action=use&ms=200")
    for i in range(60): sc.shot()
    print("   trainseat after use:", pose(), "|", getv("WinterWalk.SeatAssist.State"))
    p, v = player()
    for i in range(30): sc.shot([p[0] + 1.6, p[1] + 0.9, p[2] + 1.9], [p[0], p[1] + 0.1, p[2]], 60)      # from across the coach
    g("press?action=jump&ms=200")
    for i in range(40): sc.shot([p[0] + 1.6, p[1] + 0.9, p[2] + 1.9], [p[0], p[1] + 0.1, p[2]], 60)
    slow(1)
    sc.encode()


def ride(name, seat_path, open_sets, frames, fps, back, up, side, first_every=0, after=None):
    for k, v in open_sets: setv(k, v)
    time.sleep(1)
    r = g("sit?path=" + urllib.parse.quote(seat_path)); print("  ", name, "sit:", r.strip()[:100])
    time.sleep(2); g("render?awake=1")
    sc = Scene(name, fps)
    last, _ = player(); d = (0.0, 1.0)
    for i in range(frames):
        p, _ = player()
        mv = (p[0] - last[0], 0, p[2] - last[2]); s = math.hypot(mv[0], mv[2])
        if s > 0.15: d = (mv[0] / s, mv[2] / s)
        last = p
        if first_every and (i // first_every) % 2 == 1: sc.shot()          # the rider's own view
        else: sc.shot(chase(p, d, back, up, side), [p[0] + d[0] * 2, p[1], p[2] + d[1] * 2])
        if i % 40 == 0: print("  ", name, i, "at %.0f, %.0f, %.0f" % tuple(p), pose()[:60])
    g("sit?stand"); g("press?action=jump&ms=200")
    if after:
        for k, v in after: setv(k, v)
    sc.encode()


def train():
    path, s = find("Train 1 Car 1")
    setv("WinterWalk.TrainDriver.ForceOpen", "true"); time.sleep(2)
    setv("WinterWalk.TrainDriver.TestThrottle", 1)
    ride("train", "WinterIsle/Trains/Train 1 Car 1/Seat 1R", [], 150, 10, 16, 5.5, 9, first_every=30)
    print("   train:", getv("WinterWalk.TrainDriver.State")[:160])


def mantrip():
    ride("mantrip", "WinterIsle/Mine Trips/Mantrip 1/Seat 1", [], 130, 10, 6, 1.6, 0.6, first_every=26)


def lift():
    setv("WinterWalk.GondolaDriver.ForceOpen", "true"); time.sleep(1)
    print("   lift sit:", g("sit?path=" + urllib.parse.quote("WinterIsle/Gondola Cabins/Gondola Cabin 3/Seat Left")).strip()[:60])
    time.sleep(2); g("render?awake=1")
    sc = Scene("lift", 10)
    last, _ = player(); d = (0.0, 1.0)
    for i in range(150):
        p, _ = player()
        mv = (p[0] - last[0], p[2] - last[2]); m = math.hypot(*mv)
        if m > 0.15: d = (mv[0] / m, mv[1] / m)
        last = p
        if (i // 25) % 2 == 1: sc.shot()
        else:
            sc.n += 1
            if sc.n % 40 == 1: g("time?hour=13")
            cx, cz = p[0] + d[1] * 15 - d[0] * 6, p[2] - d[0] * 15 - d[1] * 6
            g("shot?file=v_lift_%04d&cx=%.2f&ch=7&cz=%.2f&tx=%.2f&ty=%.2f&tz=%.2f&fov=50" % (sc.n, cx, cz, p[0], p[1] - 0.5, p[2]))
        if i % 50 == 0: print("   lift", i, "at %.0f, %.0f, %.0f" % tuple(p))
    g("sit?stand"); g("press?action=jump&ms=200")
    sc.encode()


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    g("render?awake=1"); g("time?hour=13")
    setv("UnityEngine.Application.targetFrameRate", 60)
    g("active?name=GameOverlay&on=0")
    try:
        for name in sys.argv[1:]:
            globals()[name]()
    finally:
        slow(1)
        g("active?name=GameOverlay&on=1")
        setv("UnityEngine.Application.targetFrameRate", 30)
