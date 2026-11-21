"""Cast Iron Pizza's pictures for the summit pizzeria (CastIronPizzaPuzzle.cs), drawn into one atlas: a 2048 px
square of 4 x 4 cells of 512 px. The pack's mesh points its faces at these cells (CastIronPizzaPuzzle.Cell).
  python tools/pizzeria_art.py      writes unity/Assets/Textures/Pizzeria/CastIronAtlas.png
Cells, left to right from the top row: 0 window logo, 1 cat with pizza, 2 cat with wine, 3 "Pizza Special"
chalkboard / 4 "Treats" chalkboard, 5 transom "1103B", 6 exposed brick, 7 reclaimed planks / 8 deck oven front,
9 countertop oven front, 10 pizza box lid, 11 fascia sign / 12 plain white (the bottom-left cell: every untextured face
has texture coordinates 0, 0, its corner), 13 beadboard, 14 awning stripes, 15 hardwood floor.
The paintings are simple versions in the spirit of the two canvases in the restaurant, not copies of them.
"""
import math, os, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
C = 512
atlas = Image.new("RGB", (4 * C, 4 * C), (255, 255, 255))
rng = random.Random(7)

def font(size, names=("georgiab.ttf", "timesbd.ttf", "arialbd.ttf")):
    for n in names:
        try: return ImageFont.truetype(n, size)
        except OSError: pass
    return ImageFont.load_default()

def cell(i):
    return Image.new("RGB", (C, C), (255, 255, 255)), (i % 4) * C, (i // 4) * C

def put(i, img):
    atlas.paste(img, ((i % 4) * C, (i // 4) * C))

def arc_text(img, text, cx, cy, radius, f, colour, top=True):
    """Letters on an arc round (cx, cy): over the top (reading left to right) or under the bottom"""
    d = ImageDraw.Draw(img)
    widths = [d.textlength(ch, font=f) for ch in text]
    total = sum(widths) * 1.08
    angle = total / radius
    a = -math.pi / 2 - angle / 2 if top else math.pi / 2 + angle / 2
    for ch, w in zip(text, widths):
        step = w * 1.08 / radius
        mid = a + step / 2 if top else a - step / 2
        x, y = cx + math.cos(mid) * radius, cy + math.sin(mid) * radius
        tile = Image.new("RGBA", (int(w * 2 + 20), int(f.size * 1.6)), (0, 0, 0, 0))
        ImageDraw.Draw(tile).text((tile.width / 2 - w / 2, 0), ch, font=f, fill=colour)
        rot = -math.degrees(mid) - 90 if top else -math.degrees(mid) + 90
        tile = tile.rotate(rot, resample=Image.BICUBIC, expand=True)
        img.paste(tile, (int(x - tile.width / 2), int(y - tile.height / 2)), tile)
        a = a + step if top else a - step

def skillet(d, cx, cy, r, ink, pizza=True):
    """A cast iron skillet seen from above with a pizza in it, its handle to the lower right"""
    d.line([(cx + r * 0.8, cy + r * 0.25), (cx + r * 1.55, cy + r * 0.62)], fill=ink, width=int(r * 0.24))
    d.ellipse([cx + r * 1.42, cy + r * 0.48, cx + r * 1.72, cy + r * 0.76], fill=ink)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ink)
    d.ellipse([cx - r * 0.84, cy - r * 0.84, cx + r * 0.84, cy + r * 0.84], fill=(250, 250, 250))
    if pizza:
        d.ellipse([cx - r * 0.76, cy - r * 0.76, cx + r * 0.76, cy + r * 0.76], outline=ink, width=4)
        for k in range(26):
            a, rr = rng.random() * math.tau, math.sqrt(rng.random()) * r * 0.62
            px, py, s = cx + math.cos(a) * rr, cy + math.sin(a) * rr, r * (0.05 + rng.random() * 0.06)
            d.ellipse([px - s, py - s, px + s, py + s], outline=ink, width=3)
        d.line([(cx - r * 0.76, cy), (cx + r * 0.76, cy)], fill=ink, width=3)
        d.line([(cx, cy - r * 0.76), (cx, cy + r * 0.76)], fill=ink, width=3)

LOGO = os.path.join(REPO, "tools", "art", "cast-iron-logo.png")      # the restaurant's own logo (black on white), from the user
def logo(img, ink, size=0.86):
    """The restaurant's logo in 'ink', fitted into the middle of the cell"""
    rgba = Image.open(LOGO).convert("RGBA")
    side = int(C * size)
    scale = side / max(rgba.size)
    rgba = rgba.resize((int(rgba.width * scale), int(rgba.height * scale)), Image.LANCZOS)
    lum, opacity = rgba.convert("L"), rgba.getchannel("A")
    # ink where the logo is dark and opaque (its background may be white or transparent)
    alpha = Image.eval(Image.merge("LA", (lum, opacity)).split()[0], lambda v: 255 - v)
    alpha = Image.composite(alpha, Image.new("L", rgba.size, 0), opacity)
    mark = rgba
    ink_img = Image.new("RGB", mark.size, ink)
    img.paste(ink_img, ((C - mark.width) // 2, (C - mark.height) // 2), alpha)

# 0: the window, pale glass with the logo in dark grey (seen from inside it reads mirrored, as in the restaurant)
img, _, _ = cell(0)
ImageDraw.Draw(img).rectangle([0, 0, C, C], fill=(222, 232, 238))
logo(img, (52, 54, 58))
put(0, img)

def cat(d, cx, cy, s, eyes="round"):
    """A fluffy black cat's head and shoulders, centred at (cx, cy), about s across"""
    black = (22, 20, 24)
    d.ellipse([cx - s * 0.5, cy + s * 0.05, cx + s * 0.5, cy + s * 1.15], fill=black)            # body
    d.ellipse([cx - s * 0.42, cy - s * 0.42, cx + s * 0.42, cy + s * 0.32], fill=black)         # head
    for sx in (-1, 1):
        d.polygon([(cx + sx * s * 0.38, cy - s * 0.12), (cx + sx * s * 0.3, cy - s * 0.58), (cx + sx * s * 0.1, cy - s * 0.36)], fill=black)      # ears
        ex, ey = cx + sx * s * 0.16, cy - s * 0.06
        if eyes == "round":
            d.ellipse([ex - s * 0.09, ey - s * 0.09, ex + s * 0.09, ey + s * 0.09], fill=(236, 214, 120))
            d.ellipse([ex - s * 0.04, ey - s * 0.04, ex + s * 0.04, ey + s * 0.04], fill=black)
        else:      # unimpressed: half-closed
            d.chord([ex - s * 0.08, ey - s * 0.06, ex + s * 0.08, ey + s * 0.06], 0, 180, fill=(214, 206, 120))
            d.line([(ex - s * 0.09, ey - s * 0.005), (ex + s * 0.09, ey - s * 0.005)], fill=black, width=3)
    for k in range(40):      # fur at the edges
        a = rng.random() * math.tau
        r = s * (0.6 + rng.random() * 0.06)
        x, y = cx + math.cos(a) * r, cy + s * 0.5 + math.sin(a) * r * 0.9
        d.line([(x, y), (x + math.cos(a) * 8, y + math.sin(a) * 8)], fill=black, width=3)

def canvas(painting):
    img = Image.new("RGB", (C, C), (64, 96, 76))
    d = ImageDraw.Draw(img)
    for y in range(C):      # the green wall, mottled
        t = y / C
        d.line([(0, y), (C, y)], fill=(int(58 + 14 * t), int(92 + 10 * t), int(72 + 8 * t)))
    painting(img, d)
    for k in range(5000):      # canvas grain
        x, y = rng.randrange(C), rng.randrange(C)
        p = img.getpixel((x, y)); j = rng.randint(-8, 8)
        img.putpixel((x, y), tuple(max(0, min(255, v + j)) for v in p))
    ImageDraw.Draw(img).rectangle([0, 0, C - 1, C - 1], outline=(30, 34, 30), width=10)
    return img

def pizza_cat(img, d):
    d.rectangle([0, 330, 300, C], fill=(232, 224, 192))                          # the table's cloth
    d.polygon([(0, 330), (300, 330), (320, C), (0, C)], fill=(232, 224, 192))
    d.rectangle([70, 380, 270, 470], fill=(206, 166, 110))                       # the pizza box
    d.ellipse([90, 388, 250, 462], fill=(230, 140, 60)); d.ellipse([100, 394, 240, 456], fill=(236, 186, 92))
    for x, y in ((130, 410), (170, 404), (205, 418), (150, 438), (195, 442), (120, 432)):
        d.ellipse([x - 9, y - 7, x + 9, y + 7], fill=(150, 40, 30)); d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(30, 30, 30))
    d.pieslice([90, 388, 250, 462], -20, 25, fill=(206, 166, 110))               # a slice gone
    d.rectangle([28, 220, 66, 340], fill=(36, 40, 36))                            # the vase and flowers
    for k in range(12):
        x, y = 30 + rng.random() * 70, 60 + rng.random() * 150
        d.line([(47, 220), (x, y)], fill=(90, 110, 70), width=2)
        d.ellipse([x - 9, y - 9, x + 9, y + 9], fill=(240, 180, 70) if k % 3 == 0 else (245, 240, 225))
    cat(d, 370, 190, 230, "round")
    d.ellipse([272, 300, 318, 340], fill=(22, 20, 24))                           # a paw
    d.polygon([(262, 268), (318, 250), (300, 318)], fill=(236, 186, 92))         # the slice in it
    d.ellipse([284, 268, 298, 282], fill=(150, 40, 30)); d.ellipse([294, 290, 306, 302], fill=(150, 40, 30))

def wine_cat(img, d):
    d.rectangle([0, 300, C, C], fill=(232, 220, 176))                            # the table
    cat(d, 175, 190, 230, "sleepy")
    d.ellipse([200, 420, 250, 440], fill=(22, 20, 24))                           # its paw on the table
    gx = 330
    d.polygon([(gx - 45, 150), (gx + 45, 150), (gx + 34, 250), (gx - 34, 250)], fill=(220, 226, 230))      # the glass
    d.polygon([(gx - 40, 185), (gx + 40, 185), (gx + 33, 248), (gx - 33, 248)], fill=(120, 18, 30))       # the wine
    d.line([(gx, 250), (gx, 400)], fill=(210, 216, 220), width=6)
    d.ellipse([gx - 40, 395, gx + 40, 412], fill=(210, 216, 220))
    d.polygon([(390, 40), (C, 0), (C, 70), (420, 90)], fill=(44, 40, 26))         # the bottle
    d.line([(400, 70), (gx + 5, 170)], fill=(120, 18, 30), width=7)              # the pour

put(1, canvas(pizza_cat))
put(2, canvas(wine_cat))

def chalkboard(title, lines, frame):
    img = Image.new("RGB", (C, C), frame)
    d = ImageDraw.Draw(img)
    d.rectangle([28, 28, C - 28, C - 28], fill=(34, 38, 38))
    for k in range(1500):      # chalk dust
        x, y = rng.randrange(30, C - 30), rng.randrange(30, C - 30)
        d.point((x, y), fill=(70, 74, 74))
    hand = font(52, ("segoesc.ttf", "comic.ttf", "arial.ttf"))
    small = font(30, ("segoesc.ttf", "comic.ttf", "arial.ttf"))
    d.text((C / 2 - d.textlength(title, font=hand) / 2, 50), title, font=hand, fill=(240, 240, 232))
    for i, l in enumerate(lines):
        d.text((60, 150 + i * 56), l, font=small, fill=(228, 228, 220))
    return img

put(3, chalkboard("Pizza Special", ["Pepperoni & hot honey", "on a cast iron crust", "with extra mozzarella", "Snowed-in deal!"], (98, 66, 40)))
put(4, chalkboard("Treats", ["Pumpkin cheesecake", "Hot cocoa", "Homemade apple pie", "Mulled cider"], (122, 140, 146)))

# 5: the transom over the door: dark glass, the awning's stripes beyond, "1103B" in gold
img = Image.new("RGB", (C, C), (40, 44, 50))
d = ImageDraw.Draw(img)
for x in range(0, C, 40): d.rectangle([x, 0, x + 18, C], fill=(70, 66, 58))
f = font(150)
t = "1103B"
d.text((C / 2 - d.textlength(t, font=f) / 2, C / 2 - 95), t, font=f, fill=(214, 176, 90))
put(5, img)

# 6: exposed brick, with plaster left on the upper part in a broken edge
img = Image.new("RGB", (C, C), (210, 204, 196))
d = ImageDraw.Draw(img)
bh, bw = 32, 96
for row in range(C // bh + 1):
    off = (row % 2) * bw // 2
    for col in range(-1, C // bw + 2):
        x, y = col * bw + off, row * bh
        r = rng.random()
        colour = (int(120 + 40 * r), int(46 + 22 * r), int(40 + 20 * r)) if rng.random() > 0.12 else (int(90 + 30 * r), int(84 + 30 * r), int(84 + 30 * r))
        d.rectangle([x + 3, y + 3, x + bw - 3, y + bh - 3], fill=colour)
edge = [(0, 150)]
x = 0
while x < C:
    x += rng.randint(12, 40); edge.append((x, 150 + rng.randint(-30, 50)))
edge += [(C, 0), (0, 0)]
d.polygon(edge, fill=(226, 224, 220))
put(6, img)

# 7: reclaimed planks, laid level, in four stains
img = Image.new("RGB", (C, C))
d = ImageDraw.Draw(img)
stains = [(92, 56, 40), (150, 120, 88), (186, 164, 130), (70, 46, 36), (120, 86, 60)]
for k in range(8):
    base = stains[rng.randrange(len(stains))]
    d.rectangle([0, k * 64, C, k * 64 + 61], fill=base)
    for g in range(30):
        y = k * 64 + rng.randrange(2, 60)
        d.line([(rng.randrange(C), y), (rng.randrange(C), y)], fill=tuple(max(0, v - 18) for v in base), width=1)
    d.line([(0, k * 64 + 62), (C, k * 64 + 62)], fill=(30, 22, 18), width=3)
put(7, img)

def oven(door_dark):
    img = Image.new("RGB", (C, C), (180, 182, 186))
    d = ImageDraw.Draw(img)
    for y in range(0, C, 3): d.line([(0, y), (C, y)], fill=(176 + (y * 7) % 12, 178 + (y * 7) % 12, 182 + (y * 7) % 12))
    d.rectangle([20, 60, 390, 450], fill=(40, 40, 42) if door_dark else (196, 198, 202), outline=(120, 122, 126), width=6)
    d.rectangle([70, 170, 340, 330], fill=(232, 110, 50))                         # the window, glowing
    d.rectangle([90, 190, 320, 310], fill=(250, 160, 80))
    d.rectangle([90, 90, 320, 120], fill=(20, 20, 22))                            # the handle
    d.rectangle([410, 20, C - 20, C - 20], fill=(190, 30, 40))                    # the red control panel
    for k in range(3):
        y = 90 + k * 130
        d.ellipse([430, y, 490, y + 60], fill=(230, 230, 232), outline=(40, 40, 40), width=4)
        d.line([(460, y + 30), (475, y + 10)], fill=(30, 30, 30), width=5)
    return img

put(8, oven(False))
put(9, oven(True))

# 10: a pizza box's lid: kraft with the logo in red
img = Image.new("RGB", (C, C), (196, 160, 112))
d = ImageDraw.Draw(img)
logo(img, (150, 40, 34))
put(10, img)

# 11: the fascia sign over the shop front: black board, a gold rule, the logo in cream
img = Image.new("RGB", (C, C), (24, 24, 26))
d = ImageDraw.Draw(img)
d.rectangle([12, 12, C - 12, C - 12], outline=(214, 190, 120), width=8)
logo(img, (232, 224, 196), 0.84)
put(11, img)

# 15: hardwood floor, dark and glossy
img = Image.new("RGB", (C, C))
d = ImageDraw.Draw(img)
for k in range(16):
    base = (int(96 + rng.random() * 30), int(52 + rng.random() * 16), int(30 + rng.random() * 10))
    d.rectangle([k * 32, 0, k * 32 + 30, C], fill=base)
    for g in range(40):
        x = k * 32 + rng.randrange(2, 30)
        d.line([(x, rng.randrange(C)), (x, rng.randrange(C))], fill=tuple(max(0, v - 20) for v in base), width=1)
    y = rng.randrange(C); d.line([(k * 32, y), (k * 32 + 30, y)], fill=(40, 22, 14), width=2)
put(15, img)

# 13: beadboard: white boards with grooves
img = Image.new("RGB", (C, C), (238, 236, 230))
d = ImageDraw.Draw(img)
for x in range(0, C, 32): d.line([(x, 0), (x, C)], fill=(196, 194, 188), width=3)
put(13, img)

# 14: the awning's stripes, black and tan
img = Image.new("RGB", (C, C))
d = ImageDraw.Draw(img)
for k in range(8): d.rectangle([k * 64, 0, k * 64 + 63, C], fill=(30, 30, 32) if k % 2 == 0 else (196, 170, 120))
put(14, img)

# 12 stays white: texture coordinates 0, 0 are the atlas's bottom-left corner, which every untextured face samples
out = os.path.join(REPO, "unity", "Assets", "Textures", "Pizzeria")
os.makedirs(out, exist_ok=True)
atlas.save(os.path.join(out, "CastIronAtlas.png"))
print("wrote the pizzeria's atlas")
