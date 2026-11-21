"""The Winter Isle's map, drawn from the island's own height grid (unity/Land/WinterIsle.land) and the puzzles'
sites, in the manner of the game's folding map: deep blue sea, shaded relief, a fine white grid, coloured markers.
  python tools/islemap.py            writes unity/Assets/Textures/Map/WinterIsleMap.png (the inset Winter Walk stamps
                                     onto the game's map) and docs/winter-isle-map.png (the same with a numbered key)
"""
import glob, json, math, os, struct
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
d = open(os.path.join(REPO, "unity", "Land", "WinterIsle.land"), "rb").read()
x0, z0, cell, nx, nz = struct.unpack("<fffii", d[:20]); W, H = nx + 1, nz + 1
g = np.nan_to_num(np.frombuffer(d, dtype="<f4", count=W * H, offset=20), nan=-6).reshape((H, W))[::-1]      # north up
SCALE = 2
g = ndimage.zoom(g, SCALE, order=1)
h, w = g.shape
land = g > 0.15
gy, gx = np.gradient(ndimage.gaussian_filter(g, 1.2), cell / SCALE)
slope = np.hypot(gx, gy)
shade = np.clip(0.8 + (-gx * 0.6 + gy * 0.8) * 0.32, 0.55, 1.2)      # lit from the north-west
rng = np.random.default_rng(3)
img = np.zeros((h, w, 3), np.float32)
depth = np.clip(-g / 6.0, 0, 1)
img[:] = np.array([0.10, 0.26, 0.42]) * (1 - depth[..., None] * 0.25)
shallow = (~land) & (g > -2.5)
img[shallow] = np.array([0.20, 0.42, 0.55])
snow = np.array([0.93, 0.95, 0.98]); rock = np.array([0.42, 0.43, 0.47]); wood = np.array([0.20, 0.33, 0.27])
c = snow[None, None, :] * shade[..., None]
rocky = np.clip((slope - 0.75) / 0.4, 0, 1)[..., None]
c = c * (1 - rocky) + rock * shade[..., None] * rocky
speck = ndimage.gaussian_filter(rng.random((h, w)), 2.0)
speck = (speck - speck.min()) / (speck.max() - speck.min())
forest = ((g > 6) & (g < 330) & (slope < 0.75) & (speck > 0.5))[..., None]
c = np.where(forest, c * 0.45 + wood * 0.55, c)
img[land] = c[land]
im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
dr = ImageDraw.Draw(im)
def P(x, z): return ((x - x0) / cell * SCALE, h - 1 - (z - z0) / cell * SCALE)
def font(size):
    for f in ("courbd.ttf", "cour.ttf", "arialbd.ttf"):
        try: return ImageFont.truetype(f, size)
        except OSError: pass
    return ImageFont.load_default()
for gxm in range(-3000, -300, 250): dr.line([P(gxm, z0), P(gxm, z0 + nz * cell)], fill=(255, 255, 255, 70), width=1)
for gzm in range(-2700, -450, 250): dr.line([P(x0, gzm), P(x0 + nx * cell, gzm)], fill=(255, 255, 255, 70), width=1)
# the lines: the causeway, the gondola, the ski lift
plaza, summit = (-705, -795), (-2250, -1900)
dr.line([P(-468, -700), P(-459, -747)], fill=(250, 250, 250, 255), width=5)
dr.line([P(*plaza), P(*summit)], fill=(200, 30, 30, 255), width=4)
# the railway (WinterIsle.railline beside the Winter Walk plugin): dark red on the ground and trestles, dashed in
# tunnels; its stations as white squares
line_file = os.path.join(REPO, "profile", "BepInEx", "plugins", "WinterWalk", "WinterIsle.railline")
rail_stations = []
if os.path.exists(line_file):
    nets = []      # each line: its points (track middles), kinds, station indices
    for l in open(line_file):
        q = l.split()
        if not q: continue
        if q[0] == "LINE": nets.append(([], [], [], len(q) > 2 and q[2] == "closed"))
        elif q[0] == "P":
            if not nets: nets.append(([], [], [], False))
            if len(q) >= 8: nets[-1][0].append(((float(q[1]) + float(q[4])) / 2, (float(q[3]) + float(q[6])) / 2)); nets[-1][1].append(int(q[7]))
            elif len(q) >= 5: nets[-1][0].append((float(q[1]), float(q[3]))); nets[-1][1].append(int(q[4]))
        elif q[0] == "S" and nets: nets[-1][2].append(int(q[1]))
    for pts, kinds, stops, closed in nets:
        if closed and pts: pts.append(pts[0]); kinds.append(kinds[-1])      # a loop joins its last point to its first
        for i in range(len(pts) - 1):
            if kinds[i] == 1 and (i // 3) % 2: continue
            dr.line([P(*pts[i]), P(*pts[i + 1])], fill=(120, 20, 30, 255) if kinds[i] != 1 else (120, 20, 30, 170), width=5 if kinds[i] != 1 else 3)
        for i in stops:
            px, pz = P(*pts[i]); dr.rectangle([px - 8, pz - 8, px + 8, pz + 8], fill=(255, 255, 255, 255), outline=(120, 20, 30, 255), width=3)
# the airport's runway and the summit's airstrip (WinterIsleAirport.cs)
def strip(cx, cz, bearing, u0, u1, half, colour):
    a = math.radians(bearing); fx, fz = math.sin(a), math.cos(a); rx, rz = fz, -fx
    dr.polygon([P(cx + fx * u + rx * v, cz + fz * u + rz * v) for u, v in ((u0, -half), (u1, -half), (u1, half), (u0, half))], fill=colour)
strip(-870, -1855, 180, -160, 160, 22, (70, 70, 80, 255))
strip(-2250, -1900, 308, 34, 168, 10, (110, 80, 50, 255))
# the Lookouts' four stations (LookoutsPuzzle.cs)
for lx, lz in ((-1400, -980), (-1250, -2000), (-2244.5, -1915), (-2750, -2120)):
    px, pz = P(lx, lz); dr.regular_polygon((px, pz, 10), 4, fill=(250, 250, 250, 255), outline=(40, 100, 220, 255))
sites = {}
for f in glob.glob(os.path.join(REPO, "unity", "Assets", "Puzzles", "*", "site.json")):
    name = os.path.basename(os.path.dirname(f))
    if name in ("WinterIsle", "BwmTutorial", "IslandGear"): continue
    s = json.load(open(f)); sites[name] = (s["x"], s["z"])
order = sorted((n for n in sites if n != "TheTree"), key=lambda n: (-sites[n][1], sites[n][0]))
colours = [(220, 40, 40), (30, 150, 70), (40, 100, 220), (240, 190, 40), (160, 60, 170), (240, 120, 30)]
def marker(x, z, text, colour, r=15):
    px, pz = P(x, z)
    dr.ellipse([px - r, pz - r, px + r, pz + r], fill=colour + (255,), outline=(255, 255, 255, 255), width=3)
    f = font(int(r * 1.15)); tw = dr.textlength(text, font=f)
    dr.text((px - tw / 2, pz - r * 0.68), text, fill=(255, 255, 255, 255), font=f)
for i, n in enumerate(order): marker(*sites[n], str(i + 1), colours[i % len(colours)])
px, pz = P(*plaza)
dr.regular_polygon((px, pz, 22), 3, fill=(20, 120, 50, 255), outline=(255, 255, 255, 255))      # The Tree
px, pz = P(*summit)
dr.regular_polygon((px, pz, 20), 5, rotation=180, fill=(240, 200, 40, 255), outline=(255, 255, 255, 255))
if os.path.exists(os.path.join(REPO, "unity", "Land", "WinterIsle.mine")):
    mx, mz = [float(v) for v in open(os.path.join(REPO, "unity", "Land", "WinterIsle.mine")).read().split()[:2]]
    px, pz = P(mx, mz); dr.rectangle([px - 13, pz - 13, px + 13, pz + 13], fill=(40, 40, 45, 255), outline=(255, 255, 255, 255), width=3)
f = font(64); dr.text((40, 30), "WINTER ISLE", fill=(255, 255, 255, 255), font=f)
border = 10
dr.rectangle([0, 0, w - 1, h - 1], outline=(214, 190, 60, 255), width=border)
out = os.path.join(REPO, "unity", "Assets", "Textures", "Map"); os.makedirs(out, exist_ok=True)
im.resize((1536, int(1536 * h / w))).save(os.path.join(out, "WinterIsleMap.png"))
# the key, for people: the same map with the puzzles named beside it
keyw = 620
sheet = Image.new("RGBA", (w + keyw, h), (24, 28, 36, 255)); sheet.paste(im, (0, 0))
kd = ImageDraw.Draw(sheet); f = font(24)
kd.text((w + 24, 20), "Puzzles", fill=(255, 255, 255, 255), font=font(40))
for i, n in enumerate(order):
    y = 84 + i * 34
    kd.ellipse([w + 24, y, w + 52, y + 28], fill=colours[i % len(colours)] + (255,))
    t = str(i + 1); kd.text((w + 38 - kd.textlength(t, font=font(17)) / 2, y + 5), t, fill=(255, 255, 255, 255), font=font(17))
    kd.text((w + 66, y + 1), n, fill=(235, 235, 235, 255), font=f)
y = 84 + len(order) * 34 + 14
for label in ("green triangle: The Tree (plaza)", "gold star: the summit", "dark square: mine tower", "red line: gondola", "white line: causeway",
              "dark red line: railway (dashed in tunnels)", "white squares: railway stations", "grey strip: airport runway", "brown strip: summit airstrip", "white diamonds: lookouts"):
    kd.text((w + 24, y), label, fill=(200, 205, 215, 255), font=font(20)); y += 26
sheet.convert("RGB").save(os.path.join(REPO, "docs", "winter-isle-map.png"))
print("wrote the map:", len(order), "puzzles")
