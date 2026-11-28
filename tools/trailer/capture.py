"""Big Christmas Walk trailer: shoots every frame of tools/trailer/frames.csv with the running game's companion.

  py.cmd tools\\trailer\\capture.py                 all frames into shots\\trailer\\f00001.png ... (resumes: frames
                                                  whose PNG is already there and complete are skipped)
  py.cmd tools\\trailer\\capture.py --preview       every 150th frame plus a 60-frame burst, into shots\\trailer-preview\\
  py.cmd tools\\trailer\\capture.py --find-hud      lists objects that may be the HUD (reticle, warnings) for --hide
  py.cmd tools\\trailer\\capture.py --hide NAME     hides that object (companion 'active') while shooting, then shows it

The game must be running with the companion and hosting a walk on the Winter Isle (bwm.ps1 play), the window not
minimised. Only the game's own frames are recorded: nothing is drawn over them.

What it does, and why:
  - time: sets the hour (--hour, 15.5) at the start and again before every shot (--pin-every 1). A game day lasts
    15 real minutes and the capture takes tens of minutes, so setting it once would turn the trailer into a day.
  - weather: holds the blizzard at 0 (WeatherDriver.ForceBlizzard = 0, which WeatherDriver.Update clamps and uses
    as the blizzard's strength) and lets it run again (-1) at the end, also after an error or Ctrl+C.
  - rides: sets GondolaDriver.ForceOpen and TrainDriver.ForceOpen (Winter Walk's testing switches) so the gondola,
    the ski lift and the trains run even if the save has not opened them, and puts both back afterwards
    (--no-open-rides leaves them alone).
  - stale frames: the output folder keeps the hash of the frames.csv its frames came from (frames.sha1); if the
    plan has changed since, it stops instead of mixing two flights (move the folder away, or --force-resume).
  - teleports: moves the player to the frame's teleport point (every 90 frames: the sky, fog and weather follow the
    player, not the shot camera), then waits at least 3 s before the next shot, and never teleports twice within 3 s.
    During the wait the player is walked a metre or two (companion 'walk') so it does not fall asleep (an idle
    player's screen goes red, then dark); --wake-every walks it again between teleports.
  - shots: the companion replaces '/' in a file name with '_' (Plugin.cs Shot), so 'trailer/f00001' is saved as
    shots\\trailer_f00001.png; this script reads the path from the reply, checks the PNG is complete (IEND), and
    moves it to shots\\trailer\\f00001.png. A failed shot is retried up to 3 times; then it stops (resume later).
    With Pillow, a frame much darker or redder than the one before it (the sleep screen) is retaken once after a walk.
  - at least --sleep (0.05 s or more) between shots.
"""
import argparse, csv, hashlib, os, sys, time, urllib.error, urllib.parse, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PNG_END = b"\x00\x00\x00\x00IEND\xaeB`\x82"
TELEPORT_GAP = 3.0           # seconds: after a teleport before the next shot, and between teleports


class Companion:
    def __init__(self, base, dry=False):
        self.base = base.rstrip("/")
        self.dry = dry

    def call(self, command, timeout=200.0, **params):
        query = "&".join("%s=%s" % (k, urllib.parse.quote(str(v), safe="")) for k, v in params.items())
        url = self.base + "/" + command + ("?" + query if query else "")
        if self.dry:
            print("  GET", url)
            return "dry run"
        with urllib.request.urlopen(url, timeout=timeout) as r:
            return r.read().decode("utf-8", "replace")


def png_complete(path):
    try:
        if os.path.getsize(path) < 67:              # signature, IHDR and IEND at the least
            return False
        with open(path, "rb") as fh:
            fh.seek(-12, os.SEEK_END)
            return fh.read(12) == PNG_END
    except OSError:
        return False


def fmt(v, places=3):
    return ("%%.%df" % places) % float(v)


def load_frames(path):
    rows = []
    with open(path, newline="") as fh:
        for r in csv.DictReader(fh):
            tp = None
            if r["teleport_x"].strip():
                tp = (float(r["teleport_x"]), float(r["teleport_y"]), float(r["teleport_z"]))
            rows.append(dict(frame=int(r["frame"]), c=(r["cx"], r["cy"], r["cz"]), t=(r["tx"], r["ty"], r["tz"]), fov=r["fov"], tp=tp))
    rows.sort(key=lambda r: r["frame"])
    # each frame's teleport: the last one at or before it
    current = None
    for r in rows:
        if r["tp"] is not None:
            current = r["tp"]
        r["block_tp"] = current
    return rows


def csv_hash(path):
    with open(path, "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()


def frame_stats(path):
    """Mean brightness (0..1) and red/green ratio of a frame, or None without Pillow."""
    try:
        from PIL import Image
    except ImportError:
        return None
    try:
        with Image.open(path) as im:
            im = im.convert("RGB").reduce(16)
            px = list(im.getdata())
    except Exception:
        return None
    n = max(1, len(px))
    r = sum(p[0] for p in px) / n / 255.0
    g = sum(p[1] for p in px) / n / 255.0
    b = sum(p[2] for p in px) / n / 255.0
    return (0.299 * r + 0.587 * g + 0.114 * b, r / max(g, 1e-3))


class Capture:
    def __init__(self, a):
        self.a = a
        self.co = Companion(a.base, a.dry_run)
        self.shots = a.shots_dir
        self.last_teleport = -1e9
        self.player = None             # where the player was last set down (x, y, z)
        self.wake_sign = 1.0
        self.since_wake = 0
        self.prev_stats = None

    # --- the player
    def teleport(self, tp):
        wait = self.last_teleport + TELEPORT_GAP - time.monotonic()
        if wait > 0:
            time.sleep(wait)
        reply = self.co.call("teleport", x=fmt(tp[0], 2), y=fmt(tp[1], 2), z=fmt(tp[2], 2))
        self.last_teleport = time.monotonic()
        if reply.startswith("ERROR"):
            raise RuntimeError("teleport failed: " + reply.strip())
        self.player = tp
        time.sleep(1.2)                  # let the player land
        self.wake(2.0)
        left = self.last_teleport + max(TELEPORT_GAP, self.a.teleport_wait) - time.monotonic()
        if left > 0:
            time.sleep(left)

    def wake(self, metres=1.0):
        """Walks the player a little (companion 'walk'), so it does not doze off."""
        self.since_wake = 0
        if self.a.no_walk or self.player is None:
            return
        x, y, z = self.player
        nx = x + self.wake_sign * metres
        self.wake_sign = -self.wake_sign
        try:
            reply = self.co.call("walk", timeout=30.0, x=fmt(nx, 2), z=fmt(z, 2), speed="2")
            if reply.startswith("ERROR"):
                print("  (walk: %s)" % reply.strip())
        except (urllib.error.URLError, OSError) as e:
            print("  (walk failed: %s)" % e)

    # --- shots
    def shoot(self, r, out):
        name = "%s/f%05d" % (self.a.prefix, r["frame"])
        reply = self.co.call("shot", file=name, cx=fmt(r["c"][0]), cy=fmt(r["c"][1]), cz=fmt(r["c"][2]),
                             tx=fmt(r["t"][0]), ty=fmt(r["t"][1]), tz=fmt(r["t"][2]), fov=fmt(r["fov"], 2))
        if self.a.dry_run:
            return True
        if not reply.startswith("saved "):
            raise RuntimeError("shot failed: " + reply.strip()[:300])
        saved = reply[len("saved "):].strip()
        candidates = [saved, os.path.join(self.shots, name.replace("/", "_") + ".png"), os.path.join(self.shots, name.replace("/", os.sep) + ".png")]
        src = next((p for p in candidates if os.path.exists(p)), None)
        if src is None:
            raise RuntimeError("the companion said '%s' but no file is there" % saved)
        for _ in range(30):                 # the PNG may still be being written
            if png_complete(src):
                break
            time.sleep(0.1)
        else:
            raise RuntimeError("incomplete PNG: " + src)
        if os.path.abspath(src) != os.path.abspath(out):
            os.replace(src, out)
        return True

    def suspicious(self, out, record_only=False):
        """A frame much darker or redder than the last good one (the idle player's sleep screen)."""
        st = frame_stats(out)
        if st is None:
            return False
        prev = self.prev_stats
        bad = False
        if not record_only:
            bad = st[0] < 0.04 if prev is None else (st[0] < prev[0] * 0.55) or (st[1] > max(1.6, prev[1] * 1.5))
        if not bad:
            self.prev_stats = st
        return bad

    def run(self, rows):
        a = self.a
        outdir = os.path.join(self.shots, a.prefix.replace("/", os.sep))
        os.makedirs(outdir, exist_ok=True)
        # frames shot from an older frames.csv must not be mixed into this flight: the folder records the hash of
        # the frames.csv its frames came from
        stamp = os.path.join(outdir, "frames.sha1")
        digest = csv_hash(a.csv)
        old = open(stamp).read().strip() if os.path.exists(stamp) else None
        has_frames = any(n.startswith("f") and n.endswith(".png") for n in os.listdir(outdir))
        if has_frames and old != digest and not a.force_resume and not a.dry_run:
            print("%s holds frames from %s frames.csv: move or delete that folder (or pass --force-resume to keep them)"
                  % (outdir, "an unrecorded" if old is None else "a different"))
            return 4
        if not a.dry_run:
            with open(stamp, "w") as fh:
                fh.write(digest + "\n")
        todo = [r for r in rows if a.start <= r["frame"] <= (a.end or 10 ** 9)]
        if a.preview:
            burst = set(range(a.burst_start, a.burst_start + 60))
            todo = [r for r in todo if r["frame"] % 150 == 1 or r["frame"] in burst or r["frame"] == rows[-1]["frame"]]
        total = len(todo)
        done = skipped = 0
        t0 = time.monotonic()
        current_tp = None
        print("frames to shoot: %d (of %d in %s), into %s" % (total, len(rows), a.csv, outdir))
        for k, r in enumerate(todo):
            out = os.path.join(outdir, "f%05d.png" % r["frame"])
            if not a.dry_run and png_complete(out):
                skipped += 1
                continue
            if r["block_tp"] is not None and r["block_tp"] != current_tp:
                print("  teleport to %.1f, %.1f, %.1f (frame %d)" % (r["block_tp"] + (r["frame"],)))
                self.teleport(r["block_tp"])
                current_tp = r["block_tp"]
            elif a.wake_every and self.since_wake >= a.wake_every:
                self.wake(1.0)
            attempts, retook, failed = 0, False, False
            started = time.monotonic()
            while True:
                try:
                    if a.pin_every and (done % a.pin_every == 0):
                        self.co.call("time", hour=fmt(a.hour, 2))
                    started = time.monotonic()
                    self.shoot(r, out)
                    if not a.dry_run:
                        if not retook and self.suspicious(out):
                            print("  frame %d looks dark or red (sleep screen?): waking the player and taking it again" % r["frame"])
                            retook = True
                            self.wake(2.0)
                            time.sleep(1.0)
                            continue
                        if retook:
                            self.suspicious(out, record_only=True)
                    break
                except (urllib.error.URLError, OSError, RuntimeError) as e:
                    attempts += 1
                    print("  frame %d: %s (attempt %d of 4)" % (r["frame"], e, attempts))
                    if attempts > 3:
                        if a.keep_going:
                            failed = True
                            break
                        print("STOPPED at frame %d: run again to resume from there" % r["frame"])
                        return 2
                    time.sleep(1.5 * attempts)
            done += 1
            self.since_wake += 1
            elapsed = time.monotonic() - t0
            left = (total - k - 1) * elapsed / max(1, k + 1 - skipped)
            print("[%d/%d] f%05d %s %.2f s  elapsed %d:%02d  left ~%d:%02d" % (k + 1, total, r["frame"], "FAILED" if failed else "ok", time.monotonic() - started,
                                                                         elapsed // 60, elapsed % 60, left // 60, left % 60), flush=True)
            time.sleep(max(0.05, a.sleep))
        print("done: %d shot, %d already there, %.0f s" % (done, skipped, time.monotonic() - t0))
        missing = [r["frame"] for r in todo if not png_complete(os.path.join(outdir, "f%05d.png" % r["frame"]))]
        if missing and not a.dry_run:
            print("missing frames: %d (first %d): run again to fill them" % (len(missing), missing[0]))
            return 3
        return 0


def find_hud(co):
    for word in ("reticle", "crosshair", "cursor", "hud", "warning", "reconnect", "overlay"):
        print("== find?name=%s" % word)
        print(co.call("find", name=word, max="25").rstrip())


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--csv", default=os.path.join(HERE, "frames.csv"))
    ap.add_argument("--base", default="http://127.0.0.1:17361", help="the companion's address")
    ap.add_argument("--shots-dir", default=os.path.join(REPO, "shots"), help="where the companion saves shots (the repo's shots folder)")
    ap.add_argument("--start", type=int, default=1, help="first frame number")
    ap.add_argument("--end", type=int, default=0, help="last frame number (0: the last)")
    ap.add_argument("--hour", type=float, default=15.5, help="time of day for every frame")
    ap.add_argument("--pin-every", type=int, default=1, help="set the hour again every N shots (0: only at the start)")
    ap.add_argument("--sleep", type=float, default=0.05, help="seconds between shots (at least 0.05)")
    ap.add_argument("--teleport-wait", type=float, default=3.5, help="seconds after a teleport before shooting (at least 3)")
    ap.add_argument("--wake-every", type=int, default=45, help="walk the player a metre every N shots between teleports (0: never)")
    ap.add_argument("--no-walk", action="store_true", help="never walk the player")
    ap.add_argument("--no-hold-weather", action="store_true", help="leave the blizzards to the clock")
    ap.add_argument("--no-open-rides", action="store_true", help="do not force the gondola, ski lift and trains to run")
    ap.add_argument("--force-resume", action="store_true", help="keep frames already in the folder even if they came from another frames.csv")
    ap.add_argument("--hide", action="append", default=[], help="object name to hide while shooting (repeatable; see --find-hud)")
    ap.add_argument("--find-hud", action="store_true", help="list objects that may be the HUD, and exit")
    ap.add_argument("--preview", action="store_true", help="every 150th frame and a 60-frame burst into shots/trailer-preview/")
    ap.add_argument("--burst-start", type=int, default=600, help="first frame of the preview's burst")
    ap.add_argument("--keep-going", action="store_true", help="after 4 failed attempts at a frame, go on to the next")
    ap.add_argument("--dry-run", action="store_true", help="print the requests instead of sending them")
    a = ap.parse_args()
    a.sleep = max(0.05, a.sleep)
    a.teleport_wait = max(TELEPORT_GAP, a.teleport_wait)
    a.prefix = "trailer-preview" if a.preview else "trailer"

    co = Companion(a.base, a.dry_run)
    try:
        status = co.call("status", timeout=20.0)
    except (urllib.error.URLError, OSError) as e:
        print("the companion does not answer at %s (%s): start the game with bwm.ps1 play" % (a.base, e))
        return 1
    print(status.rstrip())
    if a.find_hud:
        find_hud(co)
        return 0
    if not a.dry_run:
        if "player: none" in status:
            print("not in a walk: host one first (bwm.ps1 play)")
            return 1
        net = [l for l in status.splitlines() if l.startswith("network:")]
        if net and "client True" not in net[0]:
            print("warning: the network line looks unhealthy:", net[0])
    rows = load_frames(a.csv)
    if not rows:
        print("no frames in", a.csv)
        return 1

    cap = Capture(a)
    hidden, held, opened = [], False, []
    code = 1
    try:
        print(co.call("time", hour=fmt(a.hour, 2)).strip())
        if not a.no_hold_weather:
            reply = co.call("set", path="WinterWalk.WeatherDriver.ForceBlizzard", value="0")
            held = True
            print("weather:", reply.strip())
            if not a.dry_run and "ForceBlizzard" not in reply:
                print("warning: the blizzard could not be held off; a blizzard may come during the capture")
        if not a.no_open_rides:
            # the gondola (and with it the ski lift) runs only once The Tree's tower is complete, the trains once
            # theirs is: on a test save the cable would hang empty, so open them for the capture (Winter Walk's own
            # testing switches) and put them back as they were afterwards
            for driver in ("GondolaDriver", "TrainDriver"):
                path = "WinterWalk.%s.ForceOpen" % driver
                before = co.call("get", path=path).strip()
                reply = co.call("set", path=path, value="true").strip()
                print("%s: %s (was: %s)" % (path, reply, before))
                if reply.startswith("set "):
                    opened.append((path, "true" if before.endswith("True") else "false"))
        for name in a.hide:
            reply = co.call("active", name=name, on="0")
            print("hide %s: %s" % (name, reply.strip()[:200]))
            if not reply.startswith("ERROR"):
                hidden.append(name)
        code = cap.run(rows)
    except KeyboardInterrupt:
        print("interrupted: run again to resume")
        code = 130
    finally:
        for path, value in opened:
            try:
                print("%s back to %s: %s" % (path, value, co.call("set", path=path, value=value).strip()))
            except (urllib.error.URLError, OSError) as e:
                print("could not put %s back to %s: %s" % (path, value, e))
        for name in hidden:
            try:
                co.call("active", name=name, on="1")
            except (urllib.error.URLError, OSError) as e:
                print("could not show %s again: %s" % (name, e))
        if held:
            try:
                print("weather back to the clock:", co.call("set", path="WinterWalk.WeatherDriver.ForceBlizzard", value="-1").strip())
            except (urllib.error.URLError, OSError) as e:
                print("could not let the weather run again (set ForceBlizzard to -1 by hand): %s" % e)
    if code == 0 and not a.preview:
        print("next: powershell -ExecutionPolicy Bypass -File tools\\trailer\\encode.ps1")
    return code


if __name__ == "__main__":
    sys.exit(main())
