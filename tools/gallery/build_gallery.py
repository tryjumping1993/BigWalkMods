"""Builds the puzzle gallery artifact: gallery/site/index.html + img/<Name>-<view>.jpg from gallery/raw/*.png
and docs/puzzle-variety/howto.json (each puzzle's name, players and how-to lines)."""
import json, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = os.path.join(HERE, "raw")
SITE = os.path.join(HERE, "site")
IMG = os.path.join(SITE, "img")
REPO = r"Z:\Programming\BigWalkMods"
FFMPEG = r"Z:\Tools\ffmpeg\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe"
VIEWS = [("aerial", "From above"), ("start-out", "Start corner"), ("start-in", "Start corner, other side"), ("s", "From the south"), ("e", "From the east"), ("n", "From the north"), ("w", "From the west"), ("running", "While running")]

os.makedirs(IMG, exist_ok=True)
howto = json.load(open(os.path.join(REPO, "docs", "puzzle-variety", "howto.json"), encoding="utf-8"))
sites = {}
puzzles = []
for name in sorted(k for k in howto if not k.startswith("_")):
    views = []
    for key, label in VIEWS:
        src = os.path.join(RAW, "%s-%s.png" % (name, key))
        if not os.path.exists(src):
            continue
        dst = os.path.join(IMG, "%s-%s.jpg" % (name, key))
        if "--no-images" not in sys.argv:
            subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", src, "-vf", "scale=1280:720", "-q:v", "4", dst], check=True)
        views.append(key)
    if not views:
        print("no pictures for", name)
        continue
    if "--no-images" not in sys.argv:
        subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", os.path.join(RAW, "%s-%s.png" % (name, views[0])), "-vf", "scale=400:225", "-q:v", "6", os.path.join(IMG, "%s-thumb.jpg" % name)], check=True)
    where = ""
    log = os.path.join(RAW, name + ".log")
    if os.path.exists(log):
        first = open(log, encoding="utf-8-sig").readline()
        if " at (" in first:
            xyz = first.split(" at (")[1].split(")")[0].split(",")
            where = "%s, %s" % (xyz[0].strip().split(".")[0], xyz[2].strip().split(".")[0])
    h = howto[name]
    puzzles.append({"id": name, "title": h["title"], "players": h["players"], "lines": h["lines"], "views": views, "where": where})

page = open(os.path.join(HERE, "template.html"), encoding="utf-8").read()
page = page.replace("/*DATA*/[]", json.dumps(puzzles, ensure_ascii=False)).replace("/*VIEWS*/{}", json.dumps(dict(VIEWS)))
open(os.path.join(SITE, "index.html"), "w", encoding="utf-8").write(page)
files = {"img/" + f: "img/" + f for f in sorted(os.listdir(IMG))}
json.dump(files, open(os.path.join(HERE, "files.json"), "w"))
total = sum(os.path.getsize(os.path.join(IMG, f)) for f in os.listdir(IMG))
print("%d puzzles, %d pictures, %.1f MB" % (len(puzzles), len(files), total / 1e6))
