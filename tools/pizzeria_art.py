"""Cast Iron Pizza's textures for the summit pizzeria (CastIronPizzaPuzzle.cs): every surface of the shop drawn in
code into one atlas, with a normal map in the same layout. Modelled on the user's photos of the restaurant.

    python tools/pizzeria_art.py [--preview DIR] [--only NAME,NAME]

Writes into unity/Assets/Textures/Pizzeria/:
    CastIronAtlas.png    4096 px square, 8 x 8 cells of 512 px. RGB colour (sRGB); ALPHA IS GLOSSINESS (0 matte ..
                         255 mirror-like), not transparency
    CastIronNormals.png  the same layout: tangent-space normals, OpenGL style (green = +v), flat = (128, 128, 255);
                         import it as a normal map (sRGB off)
    CastIronAtlas.json   {"cell": 512, "size": 4096, "names": [...], "rects": [[u0, v0, u1, v1], ...],
                          "tile": [[along u, along v], ...], "metres": [[u, v], ...]}
                         rects are in texture coordinates (u right, v up from the bottom of the image), 3 px inside
                         their cells; "tile" says which ways a surface repeats, "metres" the real size of one repeat
                         (0 for pictures)
--preview DIR also writes sheet.png (every surface, labelled), lit.png (the atlas shaded by its normal map, light
from the upper left), tiles.png (each tileable rect cut from the finished atlas and repeated 2 x 2) and 1:1 crops.
--only NAME,... builds just those surfaces into the preview folder (to work on them); the atlas is not written.

How it works:
- each surface is drawn at the size of its rect (cells x 512 less 6 px) as three arrays in image order (row 0 at the
  top): colour (sRGB, 0..1), gloss (0..1) and a height field in pixels; the normals come from the height field;
- materials tile: they repeat exactly over their rect, so a shader can wrap texture coordinates within the rect
  (the period is the rect, not the cell). The 3 px border round a rect is the material's wrapped continuation,
  so filtering at the edge finds the right neighbours. Pictures get their edge pixels repeated into the border;
- every noise is white noise filtered through an FFT, so it repeats over the tile at any size, and elements
  (bricks, boards, holes) come in whole numbers per tile;
- shapes are drawn with Pillow at 4x and scaled down, so edges are smooth; the paintings, fur, needles and chalk
  are thousands of short strokes; cavities (mortar, seams, grooves) are darkened from the height field.
Cell (0, 0), the bottom-left corner, is plain white with gloss 40 and flat normals: every untextured face samples
texture coordinates (0, 0).
"""
import argparse
import json
import math
import os
import zlib

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage
from scipy.spatial import cKDTree

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "unity", "Assets", "Textures", "Pizzeria")
LOGO = os.path.join(REPO, "tools", "art", "cast-iron-logo.png")      # the restaurant's own logo, from the user
CELL, GRID, PAD = 512, 8, 3
SIZE = CELL * GRID

# where each surface lies: (column, row counted from the BOTTOM, width, height) in cells
LAYOUT = {
    "white": (0, 0, 1, 1), "brick_ext": (1, 0, 1, 1), "plaster_burgundy": (2, 0, 1, 1), "beadboard": (3, 0, 1, 1),
    "trim_white": (4, 0, 1, 1), "floor_hardwood": (5, 0, 1, 1), "ceiling_black": (6, 0, 1, 1),
    "counter_planks": (7, 0, 1, 1),
    "wood_dark": (0, 1, 1, 1), "leather_black": (1, 1, 1, 1), "curtain_sheer": (2, 1, 1, 1), "rug_black": (3, 1, 1, 1),
    "mat_rubber": (4, 1, 1, 1), "concrete": (5, 1, 1, 1), "stainless": (6, 1, 1, 1), "stainless_baffle": (7, 1, 1, 1),
    "copper": (0, 2, 1, 1), "pipe_iron": (1, 2, 1, 1), "paving": (2, 2, 1, 1), "timber": (3, 2, 1, 1),
    "canvas_awning": (4, 2, 1, 1), "glass_plain": (5, 2, 1, 1), "box_side": (6, 2, 1, 1), "logo_box": (7, 2, 1, 1),
    "brick_hall": (0, 3, 1, 2), "door_white": (1, 3, 1, 2), "front_door": (2, 3, 1, 2),
    "chalk_special": (3, 3, 1, 1), "chalk_treats": (4, 3, 1, 1), "cat_pizza": (5, 3, 1, 1), "cat_wine": (6, 3, 1, 1),
    "menu_board": (7, 3, 1, 1),
    "wine_shelf": (3, 4, 1, 1), "bookshelf_items": (4, 4, 1, 1), "exit_sign": (5, 4, 1, 1),
    "fire_canister": (6, 4, 1, 1), "clock": (7, 4, 1, 1),
    "oven_deck": (0, 5, 2, 1), "oven_counter": (2, 5, 2, 1), "transom_1103B": (4, 5, 2, 1),
    "string_bulbs": (6, 5, 1, 1), "wreath": (7, 5, 1, 1),
    "logo_glass": (0, 6, 2, 2), "logo_sign": (2, 6, 2, 2), "pizza_top": (4, 6, 1, 1), "poinsettia": (5, 6, 1, 1),
}

SURFACES = {}      # name: (function, (tiles along u, tiles along v), (metres along u, along v) of one repeat)


def surface(name, tile=(True, True), metres=(0.0, 0.0)):
    def register(fn):
        SURFACES[name] = (fn, tile, metres)
        return fn
    return register


# ---------------------------------------------------------------------------------------------------- helpers

def C(r, g, b):
    """A colour from 0..255 values"""
    return np.array([r, g, b], float) / 255.0


def mix(a, b, t):
    t = np.asarray(t, float)
    if t.ndim == 2:
        t = t[..., None]
    return a + (b - a) * t


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def fill(W, H, colour):
    return np.broadcast_to(np.asarray(colour, float), (H, W, 3)).copy()


def coords(W, H):
    """Pixel centres: X across, Y down, each (H, W)"""
    return np.meshgrid(np.arange(W) + 0.5, np.arange(H) + 0.5)


def wrapd(a, period):
    """Signed distance on a circle of the given period"""
    return (a + period / 2) % period - period / 2


def blur(a, s, wrap=False):
    sig = (s, s) + (0,) * (a.ndim - 2)
    return ndimage.gaussian_filter(a, sig, mode="wrap" if wrap else "nearest")


def to8(a):
    return np.clip(np.asarray(a) * 255.0 + 0.5, 0, 255).astype(np.uint8)


def font(name, size):
    try:
        return ImageFont.truetype(name, max(4, int(size)))
    except OSError:
        return ImageFont.truetype("arial.ttf", max(4, int(size)))


class Noise:
    """Tileable noise on an H x W grid: white noise filtered in the frequency domain, so it repeats exactly."""

    def __init__(self, W, H, rng):
        self.W, self.H, self.rng = W, H, rng
        self.fy = np.fft.fftfreq(H)[:, None]
        self.fx = np.fft.fftfreq(W)[None, :]

    def blob(self, sx, sy=None, angle=0.0):
        """Smooth noise with features about sx pixels along `angle` (0 = along u) and sy across; unit variance"""
        sy = sx if sy is None else sy
        c, s = math.cos(angle), math.sin(angle)
        fa = self.fx * c + self.fy * s
        fb = -self.fx * s + self.fy * c
        g = np.exp(-2.0 * np.pi ** 2 * ((fa * sx) ** 2 + (fb * sy) ** 2))
        f = np.real(np.fft.ifft2(np.fft.fft2(self.rng.standard_normal((self.H, self.W))) * g))
        return (f - f.mean()) / (f.std() + 1e-12)

    def fbm(self, sx, octaves=4, gain=0.5, sy=None, angle=0.0):
        """Several octaves of blob, the largest about sx (by sy) pixels"""
        sy = sx if sy is None else sy
        total, amp, norm = 0.0, 1.0, 0.0
        for o in range(octaves):
            total = total + amp * self.blob(sx / 2 ** o, sy / 2 ** o, angle)
            norm += amp * amp
            amp *= gain
        return total / math.sqrt(norm)

    def blur(self, f, sx, sy=None):
        """Gaussian blur, wrapping round the tile"""
        sy = sx if sy is None else sy
        g = np.exp(-2.0 * np.pi ** 2 * ((self.fx * sx) ** 2 + (self.fy * sy) ** 2))
        if f.ndim == 3:
            return np.stack([self.blur(f[..., k], sx, sy) for k in range(f.shape[2])], axis=-1)
        return np.real(np.fft.ifft2(np.fft.fft2(f) * g))

    def specks(self, density, size=0.6):
        """Scattered small dots (0..1): about `density` of the area covered"""
        n = self.blob(size)
        t = np.quantile(n, 1.0 - density)
        return smooth(t, t + 0.6, n)


def worley(W, H, rng, count, wrap=True):
    """Cellular noise: distance to the nearest and second nearest of `count` random points, and the nearest's index"""
    pts = rng.random((count, 2)) * (W, H)
    if wrap:
        pts_all = np.concatenate([pts + (dx * W, dy * H) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    else:
        pts_all = pts
    X, Y = coords(W, H)
    d, i = cKDTree(pts_all).query(np.column_stack([X.ravel(), Y.ravel()]), k=2)
    return d[:, 0].reshape(H, W), d[:, 1].reshape(H, W), (i[:, 0] % count).reshape(H, W)


class Mask:
    """An antialiased mask (0..1) drawn with Pillow at 4x and read back at size. wrap: every shape is also drawn a
    tile over in each direction, so shapes crossing the edge come back on the other side."""

    def __init__(self, W, H, ss=4, wrap=False):
        self.W, self.H, self.s = W, H, ss
        self.im = Image.new("L", (W * ss, H * ss), 0)
        self.d = ImageDraw.Draw(self.im)
        self.offsets = [(dx * W, dy * H) for dx in (-1, 0, 1) for dy in (-1, 0, 1)] if wrap else [(0, 0)]

    def _p(self, pts, o):
        return [((x + o[0]) * self.s, (y + o[1]) * self.s) for x, y in pts]

    def poly(self, pts, v=255):
        for o in self.offsets:
            self.d.polygon(self._p(pts, o), fill=v)
        return self

    def rect(self, x0, y0, x1, y1, v=255):
        return self.poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], v)

    def ellipse(self, cx, cy, rx, ry=None, v=255):
        ry = rx if ry is None else ry
        for o in self.offsets:
            s = self.s
            self.d.ellipse([(cx - rx + o[0]) * s, (cy - ry + o[1]) * s, (cx + rx + o[0]) * s, (cy + ry + o[1]) * s], fill=v)
        return self

    def line(self, pts, w, v=255):
        r = w / 2.0
        for o in self.offsets:
            self.d.line(self._p(pts, o), fill=v, width=max(1, int(round(w * self.s))), joint="curve")
        for x, y in (pts[0], pts[-1]):
            self.ellipse(x, y, r, r, v)
        return self

    def text(self, x, y, txt, face, size, v=255, anchor="la"):
        f = font(face, size * self.s)
        for o in self.offsets:
            self.d.text(((x + o[0]) * self.s, (y + o[1]) * self.s), txt, font=f, fill=v, anchor=anchor)
        return self

    def get(self):
        return np.asarray(self.im.resize((self.W, self.H), Image.BOX), np.float64) / 255.0


def text_width(face, size, txt):
    return font(face, size).getlength(txt)


def fit_size(face, size, txt, width):
    """The largest size up to `size` at which txt fits in `width` pixels"""
    while size > 6 and text_width(face, size, txt) > width:
        size -= 1
    return size


def leaf(cx, cy, angle, length, width, n=28, bend=0.0):
    """A pointed leaf or petal from its base (cx, cy) out along angle (radians, 0 = +x): polygon points"""
    t = np.linspace(0.0, 1.0, n)
    half = width / 2.0 * np.sin(np.pi * t ** 0.8) ** 0.9
    mid = bend * width * np.sin(np.pi * t)
    side1 = [(a * length, mid[i] + half[i]) for i, a in enumerate(t)]
    side2 = [(a * length, mid[i] - half[i]) for i, a in enumerate(t)][::-1]
    c, s = math.cos(angle), math.sin(angle)
    return [(cx + x * c - y * s, cy + x * s + y * c) for x, y in side1 + side2]


def draw_strokes(colour, segs, opacity=1.0, ss=2):
    """Draws segs [(x0, y0, x1, y1, width, rgb 0..1, relief 0..1), ...] over the picture in order, as round-ended
    lines at ss x; returns the new colour and the strokes' relief (0 where none)."""
    H, W = colour.shape[:2]
    base = Image.fromarray(to8(colour)).resize((W * ss, H * ss), Image.BICUBIC)
    layer = base.copy()
    d = ImageDraw.Draw(layer)
    rel = Image.new("L", (W * ss, H * ss), 0)
    dr = ImageDraw.Draw(rel)
    for x0, y0, x1, y1, w, c, r in segs:
        c8 = tuple(int(v) for v in to8(c))
        r8 = int(np.clip(r, 0, 1) * 255)
        pw = max(1, int(round(w * ss)))
        p = [(x0 * ss, y0 * ss), (x1 * ss, y1 * ss)]
        d.line(p, fill=c8, width=pw)
        dr.line(p, fill=r8, width=pw)
        rr = w * ss / 2.0
        if rr >= 1.0:
            for x, y in p:
                d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=c8)
                dr.ellipse([x - rr, y - rr, x + rr, y + rr], fill=r8)
    out = np.asarray(layer.resize((W, H), Image.LANCZOS), np.float64) / 255.0
    relief = np.asarray(rel.resize((W, H), Image.BOX), np.float64) / 255.0
    return mix(colour, out, opacity), relief


def paint_over(colour, rng, count, length, width, angle=None, protect=None, opacity=0.85, jitter=0.035, scatter=0.35):
    """A painterly pass: `count` short strokes, each in the (slightly varied) colour under its middle, laid along
    the picture's edges (angle None) or at a fixed angle with some scatter. protect: places (0..1) to leave alone."""
    H, W = colour.shape[:2]
    soft = blur(colour, 1.2)
    lum = blur(colour.mean(-1), 2.5)
    gy, gx = np.gradient(lum)
    segs = []
    xs, ys = rng.random(count) * W, rng.random(count) * H
    for x, y in zip(xs, ys):
        ix, iy = min(W - 1, int(x)), min(H - 1, int(y))
        if protect is not None and rng.random() < protect[iy, ix]:
            continue
        c = np.clip(soft[iy, ix] * (1 + rng.normal(0, jitter)) + rng.normal(0, jitter * 0.4, 3), 0, 1)
        if angle is None:
            g = math.hypot(gx[iy, ix], gy[iy, ix])
            a = math.atan2(gy[iy, ix], gx[iy, ix]) + math.pi / 2 if g > 2e-3 else rng.random() * math.pi
        else:
            a = angle
        a += rng.normal(0, scatter)
        L = length * rng.uniform(0.6, 1.35)
        dx, dy = math.cos(a) * L / 2, math.sin(a) * L / 2
        segs.append((x - dx, y - dy, x + dx, y + dy, width * rng.uniform(0.7, 1.3), c, rng.uniform(0.3, 1.0)))
    return draw_strokes(colour, segs, opacity)


def canvas_weave(W, H, period=3.2):
    """A plain-weave canvas: thread relief (0..1) crossing over and under"""
    X, Y = coords(W, H)
    nx, ny = max(1, round(W / period)), max(1, round(H / period))
    u, v = X * nx / W, Y * ny / H
    over = ((np.floor(u) + np.floor(v)) % 2) == 0
    warp = np.sin(np.pi * (u % 1.0)) ** 0.6
    weft = np.sin(np.pi * (v % 1.0)) ** 0.6
    return np.where(over, warp * (0.6 + 0.4 * weft), weft * (0.6 + 0.4 * warp))


def dome(X, Y, cx, cy, r):
    """A round boss: height 0..1 (a hemisphere) and its mask"""
    d2 = ((X - cx) ** 2 + (Y - cy) ** 2) / (r * r)
    return np.sqrt(np.clip(1.0 - d2, 0.0, 1.0)), smooth(1.0, 0.92, np.sqrt(d2))


def bake_light(h, strength=0.5, wrap=False):
    """A little shading from a height field, for colour (light from the upper left), so form reads in flat light"""
    nrm = normal_map(h, wrap, wrap)
    L = np.array([-0.5, 0.5, 0.7])
    L /= np.linalg.norm(L)
    lam = nrm @ L
    return 1.0 + strength * (lam - L[2])


def cavity(h, sigma, wrap=True):
    """How far each point lies below its surroundings (negative in grooves)"""
    return h - blur(h, sigma, wrap)


def periodic_grain(N, X, Y, W, H, rng, spacing, wobble=8.0, depth=1.5):
    """Flat-sawn growth rings for a tileable board running along u: 0 in the earlywood rising to 1 at each latewood
    line. The rings fold about a pith line (|sin| makes it periodic) and drift along the board, so they arch into
    cathedrals where the drift crosses them."""
    rings = max(1, round(H / spacing))
    # a whole number of rings up the tile, pushed about by a field stretched along the board: where the field
    # bulges, the rings close round it in arches and long eyes, as in flat-sawn boards
    swell = N.fbm(W * 0.3, 3, sy=H * 0.07)
    bulge = np.abs(N.fbm(W * 0.22, 2, sy=H * 0.05))
    drift = sum(rng.uniform(0.5, 1.0) * wobble / m * np.sin(2 * np.pi * m * X / W + rng.random() * 6.283) for m in (1, 2))
    ph = rings * Y / H + (wobble * 0.35 * swell + wobble * 0.5 * bulge + drift) / spacing * depth / 1.5
    return ring_profile(ph, wrap=True)


def ring_profile(ph, wrap=False):
    """0 in the earlywood darkening to 1 at the latewood line, then a sharp step to the next ring, softened by a
    pixel so it does not alias"""
    f = ph - np.floor(ph)
    late = smooth(0.35, 0.93, f) * (1 - smooth(0.93, 1.0, f)) + (1 - smooth(0.0, 0.05, f)) * 0.9
    return blur(late, 0.5, wrap)


def board_grain(along, across, width, rng, spacing, warp, slope=0.04):
    """Rings for one board (arrays of any shape): its pith near or beyond its edges, the board cut slightly across
    the trunk so the rings arch into cathedrals along it; along, across and width in the units of spacing.
    Returns 0..1, 1 at the latewood lines."""
    b0 = rng.uniform(-1.0, 2.0) * width
    c = rng.uniform(0.3, 3.0) * spacing
    drift = (rng.uniform(1.0, 3.0) * spacing * np.sin(2 * np.pi * along / (rng.uniform(60, 160) * spacing) + rng.random() * 6.283)
             + rng.uniform(-slope, slope) * along)
    r = np.sqrt((across - b0) ** 2 + c * c) + drift + warp
    f = r / spacing
    f = f - np.floor(f)
    return smooth(0.35, 0.93, f) * (1 - smooth(0.93, 1.0, f)) + (1 - smooth(0.0, 0.05, f)) * 0.9


def normal_map(h, wrap_x, wrap_y):
    """Unit normals (H, W, 3) from a height field in pixels: x along u, y along v (up the texture), z out"""
    if wrap_x:
        gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) / 2.0
    else:
        p = np.pad(h, ((0, 0), (1, 1)), mode="edge")
        gx = (p[:, 2:] - p[:, :-2]) / 2.0
    if wrap_y:
        gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) / 2.0
    else:
        p = np.pad(h, ((1, 1), (0, 0)), mode="edge")
        gy = (p[2:, :] - p[:-2, :]) / 2.0
    # rows run down the image but v runs up, so dh/dv = -gy and the normal's v part is +gy
    n = np.stack([-gx, gy, np.ones_like(h)], axis=-1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


def shade_preview(colour, gloss, nrm):
    """The surface lit by a lamp at the upper left, with a highlight by its gloss, for looking at"""
    L = np.array([-0.55, 0.55, 0.63])
    L /= np.linalg.norm(L)
    Hv = L + np.array([0.0, 0.0, 1.0])
    Hv /= np.linalg.norm(Hv)
    lam = np.clip(nrm @ L, 0.0, 1.0)
    spec = gloss * np.clip(nrm @ Hv, 0.0, 1.0) ** (6.0 + 90.0 * gloss) * 0.6
    return np.clip(colour * (0.3 + 0.85 * lam)[..., None] + spec[..., None], 0.0, 1.0)


# ---------------------------------------------------------------------------------------------------- materials

@surface("white", metres=(1.0, 1.0))
def white(W, H, rng):
    """Plain white, gloss 40: what every untextured face shows"""
    return fill(W, H, C(255, 255, 255)), np.full((H, W), 40 / 255.0), np.zeros((H, W))


def brick_bond(W, H, cols, rows, joint, N, rough=0.7):
    """Running bond, `cols` bricks by `rows` courses per tile. Per pixel: course, brick, distance (px) to the
    brick's edge less half the joint (negative in the mortar, ragged), position in the brick (fx, fy)."""
    X, Y = coords(W, H)
    bh, bw = H / rows, W / cols
    fyc = Y / bh
    row = np.floor(fyc).astype(int) % rows
    xs = X / bw + 0.5 * (row % 2)
    col = np.floor(xs).astype(int) % cols
    fx, fy = xs - np.floor(xs), fyc - np.floor(fyc)
    ex, ey = np.minimum(fx, 1 - fx) * bw, np.minimum(fy, 1 - fy) * bh
    e = np.minimum(ex, ey) - joint / 2.0 + rough * N.fbm(1.8, 3)
    return row, col, e, fx, fy


def brickwork(W, H, rng, N, rows, cols, palette, weights, mortar, joint=4.0, soot=0.12, bloom=0.3, smear=0.0):
    """Bricks in mortar: colour, gloss and height. palette/weights: the bricks' colours and how common each is;
    smear: old lime and plaster left on the bricks (the back hall's wall)."""
    row, col, e, fx, fy = brick_bond(W, H, cols, rows, joint, N)
    R = rng.random((rows, cols, 6))
    r = R[row, col]
    cum = np.cumsum(weights) / np.sum(weights)
    idx = np.minimum(np.searchsorted(cum, r[..., 0]), len(palette) - 1)
    base = np.array(palette)[idx] * (0.86 + 0.24 * r[..., 1])[..., None]
    # within a brick: clouding, a darker fire-flashed end on some, sand grain, pits
    cloud = N.fbm(9, 3)
    end = np.where(r[..., 2] > 0.5, fx, 1 - fx)
    flash = smooth(0.55, 1.0, end) * (r[..., 3] > 0.55) * 0.28
    grain = N.blob(0.7)
    pits = N.specks(0.025, 0.7)
    brick = base * (1 + 0.08 * cloud + 0.04 * grain - flash - 0.35 * pits)[..., None]
    hue = N.fbm(25, 2)
    brick = brick * (1 + 0.04 * hue[..., None] * np.array([1.0, -0.4, -0.8]))
    # mortar: sandy, a little dirty
    mg = N.fbm(1.4, 3)
    mort = mortar * (1 + 0.09 * mg + 0.05 * N.fbm(14, 2))[..., None]
    face = smooth(-0.4, 1.1, e)
    colour = mix(mort, brick, face)
    # height: faces stand proud of a concave tooled joint, with rounded arrises and lumpy faces
    bevel = smooth(-0.6, 2.4, e)
    joint_depth = np.clip(-e, 0.0, joint / 2.0) / (joint / 2.0)
    h = (3.2 * bevel + face * (0.45 * N.fbm(12, 3) + 0.22 * grain - 0.9 * pits)
         + (1 - face) * (0.3 * mg - 0.7 * joint_depth ** 2))
    # weather: soot running down from the joints, salt bloom in patches
    if soot:
        streak = smooth(0.8, 2.6, N.blob(1.8, 70) + 0.6 * N.fbm(40, 2))
        colour *= (1 - soot * streak)[..., None]
    if bloom:
        salt = smooth(1.1, 2.4, N.fbm(28, 3)) * smooth(-0.5, 1.0, N.blob(2.0))
        colour = mix(colour, C(212, 206, 196), bloom * salt)
    if smear:
        film = smooth(-0.2, 1.8, N.fbm(22, 4, gain=0.6) + 0.6 * (1 - face))
        lime = smooth(-0.6, 1.2, N.fbm(1.6, 3)) * (0.5 + 0.5 * smooth(-1, 1, N.fbm(8, 2)))
        colour = mix(colour, C(214, 208, 200) * (1 + 0.05 * N.blob(1.0))[..., None], smear * film * lime)
        h += 0.6 * film * lime * face
    # grime settles in the joints
    cav = cavity(h, 3.0)
    colour *= (1 + 0.06 * np.clip(cav, -2.5, 1.0))[..., None]
    gloss = 0.03 + face * (0.03 + 0.012 * grain)
    return np.clip(colour, 0, 1), gloss, h, face


@surface("brick_ext", metres=(1.2, 1.2))
def brick_ext(W, H, rng):
    """Red-brown face brick in pale mortar, for the shop's outside: 1.2 m square, 6 bricks by 18 courses"""
    N = Noise(W, H, rng)
    pal = [C(146, 60, 45), C(128, 50, 40), C(158, 76, 56), C(114, 45, 37), C(136, 68, 52), C(100, 44, 38),
           C(164, 98, 74), C(94, 64, 60)]
    colour, gloss, h, _ = brickwork(W, H, rng, N, 18, 6, pal, [20, 20, 12, 14, 14, 8, 6, 6], C(176, 168, 154))
    return colour, gloss, h


@surface("plaster_burgundy", metres=(1.5, 1.5))
def plaster_burgundy(W, H, rng):
    """The dining room's burgundy paint on old plaster, rolled on: 1.5 m square. Low mottling, roller stipple
    and faint lap marks, eggshell sheen."""
    N = Noise(W, H, rng)
    base = C(126, 38, 46)
    low = N.fbm(90, 3)
    mid = N.fbm(18, 3)
    stipple = N.blob(0.9)
    nap = N.blob(10, 1.4, angle=math.pi / 2)            # the roller's nap, drawn up and down
    laps = N.blob(260, 30, angle=math.pi / 2)           # where one roller pass overlapped the last
    colour = base * (1 + 0.045 * low + 0.02 * mid + 0.025 * stipple + 0.012 * nap)[..., None]
    colour = colour * (1 + 0.03 * N.fbm(60, 2)[..., None] * np.array([0.6, -0.5, -0.2]))
    h = 0.16 * stipple + 0.12 * nap + 0.7 * N.fbm(45, 3) + 0.25 * mid
    gloss = 0.17 + 0.03 * laps - 0.02 * stipple
    return np.clip(colour, 0, 1), gloss, h


@surface("beadboard", metres=(0.8, 0.8))
def beadboard(W, H, rng):
    """White beadboard wainscot: vertical boards 50 mm apart, each with a V-groove and a rounded bead beside it,
    in semi-gloss paint: 0.8 m square (16 boards)"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    n = 16
    p = W / n
    k = np.floor(X / p).astype(int) % n
    x = X - np.floor(X / p) * p                       # 0..p across a board, the groove at 0
    groove = np.clip(1.0 - np.abs(wrapd(x, p)) / 2.2, 0.0, 1.0) ** 0.7
    bead_t = (x - 2.2) / 6.0
    bead = np.sqrt(np.clip(1.0 - (2.0 * bead_t - 1.0) ** 2, 0.0, 1.0)) * ((bead_t >= 0) & (bead_t <= 1))
    quirk = np.clip(1.0 - np.abs(x - 8.8) / 1.0, 0.0, 1.0)  # the small groove that sets the bead off
    h = 2.0 - 3.0 * groove + 1.4 * bead - 1.0 * quirk + 0.12 * N.fbm(30, 3, sy=3, angle=math.pi / 2)
    h = blur(h, 0.5, True)
    tone = (rng.random(n) - 0.5) * 0.025
    brush = N.blob(40, 1.0, angle=math.pi / 2)
    colour = C(238, 234, 224) * (1 + tone[k] + 0.012 * brush + 0.01 * N.fbm(50, 3))[..., None]
    cav = cavity(h, 2.5)
    colour *= (1 + 0.07 * np.clip(cav, -2.0, 0.5))[..., None]
    colour = mix(colour, C(190, 182, 166), 0.12 * smooth(1.6, 3.0, N.fbm(20, 3)) * groove)   # dust in the grooves
    gloss = 0.32 - 0.12 * groove + 0.02 * brush
    return np.clip(colour, 0, 1), gloss, h


@surface("trim_white", metres=(0.5, 0.5))
def trim_white(W, H, rng):
    """Painted wood trim (casings, skirting, the bookcase's face): white semi-gloss with brush marks along u and
    the wood's grain showing faintly through: 0.5 m square"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    late = periodic_grain(N, X, Y, W, H, rng, 9.0, wobble=14)
    brush = N.blob(70, 0.9)
    dings = N.specks(0.003, 1.2)
    colour = C(240, 237, 229) * (1 + 0.012 * brush - 0.018 * late + 0.012 * N.fbm(40, 3) - 0.08 * dings)[..., None]
    h = 0.35 * late + 0.25 * brush - 0.8 * dings
    gloss = 0.34 + 0.03 * brush
    return np.clip(colour, 0, 1), gloss, h


@surface("floor_hardwood", metres=(2.0, 2.0))
def floor_hardwood(W, H, rng):
    """The dining room's dark, glossy red oak strip floor: 2 m square, 35 strips of 57 mm running along v, in
    boards 0.3 to 1.5 m long with staggered butt joints"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    strips = 35
    sw = W / strips
    s = np.floor(X / sw).astype(int) % strips
    xl = X / sw - np.floor(X / sw)
    rows = np.arange(H) + 0.5
    board = np.zeros((H, W), int)
    along = np.zeros((H, W))
    blen = np.zeros((H, W))
    for k in range(strips):
        start = rng.random() * H
        lengths = []
        while True:
            L = rng.uniform(75, 380)
            if sum(lengths) + L > H - 75:
                lengths.append(H - sum(lengths))
                break
            lengths.append(L)
        ends = np.cumsum(lengths)
        yrel = (rows - start) % H
        bi = np.minimum(np.searchsorted(ends, yrel, side="right"), len(lengths) - 1)
        a = yrel - np.concatenate([[0.0], ends])[bi]
        cols = s[0] == k
        board[:, cols] = (k * 16 + bi)[:, None]
        along[:, cols] = a[:, None]
        blen[:, cols] = np.array(lengths)[bi][:, None]
    nb = strips * 16
    P = rng.random((nb, 6))
    p = P[board]
    across = xl * sw
    warp = 0.9 * N.blob(45, 2.0, angle=math.pi / 2)
    late = np.zeros((H, W))
    for b in np.unique(board):
        m = board == b
        r = np.random.default_rng(int(b) * 7919 + 13)
        late[m] = board_grain(along[m], across[m], sw, r, r.uniform(2.2, 3.4), warp[m], slope=0.07)
    late = blur(late, 0.45, True)
    tone = 0.25 + 0.5 * p[..., 0]
    base = mix(C(136, 72, 38), C(96, 48, 26), tone)
    amber = (p[..., 1] > 0.93)[..., None]
    base = np.where(amber, base * np.array([1.12, 1.08, 1.0]), base)
    base = np.where((p[..., 1] < 0.05)[..., None], base * 0.8, base)
    pores = smooth(1.1, 2.3, N.blob(5, 0.45, angle=math.pi / 2))
    flecks = smooth(2.3, 3.0, N.blob(0.6, 3.5, angle=math.pi / 2))
    streak = N.blob(30, 1.2, angle=math.pi / 2)
    colour = base * (1.14 - 0.5 * late - 0.18 * pores * (1 - late) + 0.06 * flecks + 0.05 * streak)[..., None]
    # seams between strips and at the butt joints
    de = np.minimum(xl, 1 - xl) * sw
    dj = np.minimum(along, blen - along)
    seam = 1 - smooth(0.15, 1.0, de)
    joint = 1 - smooth(0.15, 1.0, dj)
    gap = np.maximum(seam, joint)
    colour *= (1 - 0.6 * gap)[..., None]
    # traffic: worn paths a little lighter and duller, fine scratches
    wear = smooth(0.2, 1.8, N.fbm(110, 3))
    scratch = sum(smooth(2.7, 3.3, np.abs(N.blob(90, 0.4, angle=a))) for a in rng.uniform(0, math.pi, 4))
    colour = colour * (1 + 0.05 * wear + 0.08 * scratch)[..., None]
    h = -1.4 * gap - 0.35 * (1 - 4 * xl * (1 - xl)) + 0.15 * late - 0.2 * pores - 0.3 * scratch
    gloss = (0.68 + 0.04 * N.blob(3)) * (1 - 0.18 * wear) - 0.12 * scratch - 0.3 * gap
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


@surface("ceiling_black", metres=(1.5, 1.5))
def ceiling_black(W, H, rng):
    """The dining room's ceiling: matte black paint sprayed over old plaster, faintly mottled: 1.5 m square"""
    N = Noise(W, H, rng)
    stipple = N.blob(0.8)
    colour = C(30, 29, 31) * (1 + 0.06 * N.fbm(70, 3) + 0.05 * stipple + 0.04 * N.fbm(8, 2))[..., None]
    h = 0.2 * stipple + 1.6 * N.fbm(50, 3) + 0.2 * N.fbm(6, 2)
    return np.clip(colour, 0, 1), 0.08 + 0.02 * stipple, h


# the reclaimed boards' stains, from the counter in the photo
STAINS = [C(72, 44, 34), C(88, 48, 36), C(124, 88, 60), C(152, 144, 128), C(196, 172, 136), C(168, 126, 82)]


@surface("counter_planks", metres=(2.0, 1.0))
def counter_planks(W, H, rng):
    """The order counter's front: reclaimed boards laid level, 7 rows of 143 mm in five stains, butt-jointed, with
    knots, old nail holes and saw marks: 2 m wide by 1 m tall (the cell is stretched 2:1 along u)"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    pxu, pxv = W / 2.0, H / 1.0             # pixels per metre along u and v
    rows = 7
    bh = H / rows
    row = np.floor(Y / bh).astype(int) % rows
    yb = Y / bh - np.floor(Y / bh)          # 0 at the top of a board, 1 at its bottom
    seg = np.zeros((H, W), int)
    along = np.zeros((H, W))
    seglen = np.zeros((H, W))
    stain_of = {}
    prev = -1
    xs = np.arange(W) + 0.5
    for r in range(rows):
        n = 1 if rng.random() < 0.55 else 2
        j = np.sort(rng.random(n) * W)
        if n == 2 and (j[1] - j[0]) % W < 0.3 * W:
            j[1] = (j[0] + rng.uniform(0.35, 0.65) * W) % W
            j = np.sort(j)
        si = (np.searchsorted(j, xs) - 1) % n
        a = (xs - j[si]) % W
        L = (j[(si + 1) % n] - j[si]) % W
        if n == 1:
            L = np.full(W, float(W))
        m = row[:, 0] == r
        seg[m] = r * 4 + si[None, :]
        along[m] = np.broadcast_to(a, (m.sum(), W))
        seglen[m] = np.broadcast_to(L, (m.sum(), W))
        for k in range(n):
            choice = int(rng.integers(0, len(STAINS)))
            while choice == prev:
                choice = int(rng.integers(0, len(STAINS)))
            stain_of[r * 4 + k] = choice
            prev = choice
    am, bm = along / pxu, yb * 0.143                        # metres along and across each board
    warp = 0.0015 * N.blob(40, 3)
    late = np.zeros((H, W))
    knot_body = np.zeros((H, W))
    knot_ring = np.zeros((H, W))
    colour = np.zeros((H, W, 3))
    weathered = np.zeros((H, W))
    saw = np.zeros((H, W))
    for b in np.unique(seg):
        m = seg == b
        r = np.random.default_rng(int(b) * 104729 + 7)
        a_, b_ = am[m], bm[m]
        sp = r.uniform(0.005, 0.009)
        w = warp[m].copy()
        kb = np.zeros(a_.shape)
        kr = np.zeros(a_.shape)
        for _ in range(int(r.choice([0, 1, 1, 2]))):        # knots: the grain bends round them
            ak, bk, rk = r.uniform(0.1, 1.9), r.uniform(0.03, 0.11), r.uniform(0.008, 0.018)
            dk = np.sqrt(((wrapd(a_ - ak, 2.0)) / 1.5) ** 2 + (b_ - bk) ** 2)
            w += 2.2 * rk * np.exp(-(dk / (2.6 * rk)) ** 2)
            kb = np.maximum(kb, smooth(1.0, 0.75, dk / rk))
            kr = np.maximum(kr, np.exp(-((dk - rk) / (0.3 * rk)) ** 2))
        late[m] = board_grain(a_, b_, 0.143, r, sp, w, slope=0.025)
        knot_body[m], knot_ring[m] = kb, kr
        st = STAINS[stain_of[int(b)]]
        colour[m] = st * r.uniform(0.9, 1.08)
        weathered[m] = 1.0 if stain_of[int(b)] in (3, 4) else r.uniform(0.0, 0.4)
        if r.random() < 0.6:                                # circular saw marks from the mill
            cx, cy, pitch = r.uniform(0, 2), r.uniform(-1.2, -0.6), r.uniform(0.0025, 0.004)
            d = np.sqrt(wrapd(a_ - cx, 2.0) ** 2 + (b_ - cy) ** 2)
            saw[m] = np.sin(2 * np.pi * d / pitch) * r.uniform(0.4, 1.0)
    late = blur(late, 0.5, True)
    pores = smooth(1.2, 2.4, N.blob(5, 0.5))
    rough = N.blob(18, 0.7)                                  # fibres along the board
    streaks = N.fbm(90, 3, sy=6)                             # the stain taken up unevenly
    shade = 1.12 - 0.42 * late - 0.08 * pores + 0.04 * saw * weathered + 0.05 * rough + 0.07 * streaks
    colour = colour * shade[..., None]
    colour = mix(colour, colour * np.array([0.42, 0.36, 0.32]), knot_body)
    colour *= (1 - 0.35 * knot_ring)[..., None]
    # nail holes at the studs (every 0.4 m), two per board, plus older ones from the boards' first life
    holes = Mask(W, H, wrap=True)
    rust = Mask(W, H, wrap=True)
    for r in range(rows):
        for k in range(5):
            for yy in (0.28, 0.72):
                x = (0.2 + 0.4 * k) * pxu + rng.normal(0, 1.2)
                y = (r + yy) * bh + rng.normal(0, 2.0)
                holes.ellipse(x, y, 1.1, 2.0)
                rust.ellipse(x, y, 2.6, 5.0, int(rng.uniform(60, 160)))
        for _ in range(int(rng.integers(0, 3))):
            x, y = rng.random() * W, (r + rng.uniform(0.15, 0.85)) * bh
            holes.ellipse(x, y, 0.8, 1.5, 200)
            rust.ellipse(x, y, 2.0, 4.0, int(rng.uniform(40, 120)))
    hm, rm = holes.get(), blur(rust.get(), 1.0, True)
    colour = mix(colour, C(96, 58, 40) * colour.mean(-1, keepdims=True) * 2.2, 0.35 * rm)
    colour = mix(colour, C(26, 20, 18), hm)
    # gaps between rows and at butt joints, with rounded, worn edges
    edge = np.minimum(yb, 1 - yb) * bh
    ends = np.minimum(along, seglen - along)
    gap = np.maximum(1 - smooth(0.6, 1.8, edge), 1 - smooth(0.3, 1.2, ends))
    round_ = smooth(0.0, 6.0, np.minimum(edge, ends * 2))
    colour = mix(colour, C(20, 15, 12), gap)
    colour *= (0.9 + 0.1 * round_)[..., None]
    h = (2.0 * round_ - 2.4 * gap + (0.2 + 0.4 * weathered) * late + 0.2 * saw * weathered + 0.12 * rough
         - 1.6 * hm - 0.3 * knot_ring)
    gloss = 0.1 + 0.08 * (1 - weathered) + 0.1 * knot_body - 0.08 * gap
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


@surface("wood_dark", metres=(1.0, 1.0))
def wood_dark(W, H, rng):
    """Dark stained wood with a satin finish, grain along u (the counter top, shelves, frames, booth tops): 1 m square"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    late = periodic_grain(N, X, Y, W, H, rng, 6.0, wobble=18)
    pores = smooth(1.2, 2.4, N.blob(6, 0.5))
    streak = N.fbm(60, 3, sy=4)
    colour = C(78, 40, 30) * (1.14 - 0.45 * late - 0.12 * pores + 0.07 * streak)[..., None]
    colour = colour * (1 + 0.04 * N.fbm(90, 2)[..., None])
    h = 0.3 * late - 0.35 * pores + 0.1 * streak
    gloss = 0.4 - 0.1 * pores + 0.03 * streak
    return np.clip(colour, 0, 1), gloss, h


@surface("leather_black", metres=(0.6, 0.6))
def leather_black(W, H, rng):
    """Black vinyl booth upholstery: pebble grain, soft creases, slight button tufting on a diamond (two buttons
    per 0.6 m square, folds running between them)"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    F1, F2, _ = worley(W, H, rng, 5200)
    pebble = smooth(0.0, 2.6, F2 - F1)
    tuft = np.zeros((H, W))
    button = np.zeros((H, W))
    for bx, by in ((0.0, 0.0), (W / 2, H / 2)):
        dx, dy = wrapd(X - bx, W), wrapd(Y - by, H)
        d2 = dx * dx + dy * dy
        tuft += np.exp(-d2 / (2 * (W * 0.12) ** 2))
        button = np.maximum(button, smooth(9.5, 8.0, np.sqrt(d2)))
    # soft folds along the diagonals between buttons, fading away from them
    t1 = (X - Y) / W
    t2 = (X + Y) / W
    d1 = np.abs(wrapd(t1, 1.0)) * W / math.sqrt(2)
    d2_ = np.abs(wrapd(t2, 1.0)) * W / math.sqrt(2)
    fold = np.maximum(np.exp(-(d1 / 4.0) ** 2), np.exp(-(d2_ / 4.0) ** 2)) * (0.35 + 0.65 * tuft)
    crease = np.exp(-(N.blob(30) / 0.05) ** 2) * smooth(0.6, 1.6, N.fbm(60, 2))
    crease = blur(crease, 1.0, True)
    shine = smooth(-0.5, 1.5, -tuft * 3 + N.fbm(30, 2))
    colour = C(33, 31, 31) * (1 + 0.1 * pebble - 0.1 * crease - 0.12 * fold + 0.1 * shine - 0.12 * tuft)[..., None]
    colour = mix(colour, C(18, 17, 17), button * 0.7)
    h = -8.0 * tuft - 0.8 * fold - 0.5 * crease + 0.35 * pebble + 2.5 * button + 0.1 * N.blob(1)
    gloss = 0.52 + 0.08 * pebble - 0.12 * crease - 0.06 * fold
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


def damask(m, cx, cy, s, rng=None):
    """One damask ornament, mirror-symmetric about x = cx and about 2s tall, drawn into Mask m"""
    lw = 0.035 * s

    def spiral(x0, y0, r0, turns, a0, sd):
        pts = []
        for t in np.linspace(0, 1, 60):
            a = a0 + sd * t * turns * 2 * math.pi
            rr = r0 * (1 - 0.85 * t)
            pts.append((x0 + rr * math.cos(a), y0 + rr * math.sin(a)))
        return pts

    m.poly(leaf(cx, cy + 0.35 * s, -math.pi / 2, 0.9 * s, 0.2 * s))        # the centre spray: a tall leaf
    m.poly(leaf(cx, cy + 0.45 * s, math.pi / 2, 0.45 * s, 0.12 * s))
    m.ellipse(cx, cy - 0.68 * s, 0.05 * s)
    for sd in (-1, 1):
        # big C-scrolls: up and out from the base, curling inward at the top and the bottom
        up = spiral(cx + sd * 0.42 * s, cy - 0.38 * s, 0.26 * s, 0.9, math.pi / 2 if sd > 0 else math.pi / 2, -sd)
        m.line([(cx + sd * 0.06 * s, cy + 0.5 * s), (cx + sd * 0.3 * s, cy + 0.2 * s), (cx + sd * 0.52 * s, cy - 0.1 * s)] + up[::-1][-1:], lw)
        m.line(up, lw)
        down = spiral(cx + sd * 0.6 * s, cy + 0.45 * s, 0.2 * s, 0.85, -math.pi / 2, sd)
        m.line([(cx + sd * 0.12 * s, cy + 0.2 * s), (cx + sd * 0.4 * s, cy + 0.22 * s)] + down[:1], lw * 0.9)
        m.line(down, lw * 0.9)
        # leaves and buds along the scrolls
        for (lx, ly, a, L, wd) in ((0.12, -0.15, -1.2, 0.45, 0.11), (0.25, 0.32, 0.5, 0.36, 0.09), (0.72, -0.62, -2.2, 0.3, 0.08),
                                   (0.85, 0.15, 1.2, 0.3, 0.08), (0.35, 0.75, 1.9, 0.28, 0.07)):
            ang = a if sd > 0 else math.pi - a
            m.poly(leaf(cx + sd * lx * s, cy + ly * s, ang, L * s, wd * s, bend=0.2 * sd))
        for (dx, dy, r) in ((0.2, -0.75, 0.035), (0.95, -0.3, 0.03), (0.78, 0.82, 0.03), (0.08, 0.95, 0.03)):
            m.ellipse(cx + sd * dx * s, cy + dy * s, r * s)


@surface("curtain_sheer", metres=(1.0, 1.0))
def curtain_sheer(W, H, rng):
    """The front window's sheer curtains: pale grey-lavender voile with a silvery damask pattern, hanging in soft
    folds (folds run along v): 1 m square, two ornaments per repeat in a half drop"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    m = Mask(W, H, wrap=True)
    damask(m, W * 0.25, H * 0.27, H * 0.21)
    damask(m, W * 0.75, H * 0.77, H * 0.21)
    pat = blur(m.get(), 0.6, True)
    folds =sum(rng.uniform(0.4, 1.0) / k * np.sin(2 * np.pi * k * X / W + rng.random() * 6.283) for k in (3, 5, 8, 13))
    folds += 0.15 * N.blob(300, 25, angle=math.pi / 2)
    fold_shade = 1 + 0.07 * folds
    weave = canvas_weave(W, H, 2.4)
    colour = C(206, 202, 210) * (fold_shade + 0.03 * weave + 0.015 * N.blob(1.0))[..., None]
    colour = mix(colour, C(230, 228, 234) * fold_shade[..., None], 0.55 * pat)
    h = 6.0 * folds + 0.3 * weave + 0.4 * pat
    gloss = 0.18 + 0.2 * pat
    return np.clip(colour, 0, 1), gloss, h


@surface("rug_black", metres=(0.6, 0.6))
def rug_black(W, H, rng):
    """The runner rug by the door: charcoal cut pile, fuzzy, with a slight sheen where the pile lies over: 0.6 m"""
    N = Noise(W, H, rng)
    pile = N.blob(0.6)
    tufts = N.blob(1.6)
    lay = N.fbm(40, 3)
    colour = C(40, 40, 43) * (1 + 0.16 * pile + 0.1 * tufts + 0.07 * lay)[..., None]
    fib = Mask(W, H, ss=2, wrap=True)
    for _ in range(9000):
        x, y, a = rng.random() * W, rng.random() * H, rng.random() * math.pi
        L = rng.uniform(1.5, 4.0)
        fib.line([(x, y), (x + math.cos(a) * L, y + math.sin(a) * L)], 0.6, int(rng.uniform(80, 255)))
    f = fib.get()
    colour = colour * (1 + 0.18 * f)[..., None]
    h = 0.6 * pile + 0.6 * tufts + 0.4 * f + 0.4 * lay
    return np.clip(colour, 0, 1), 0.04 + 0.02 * lay, h


@surface("mat_rubber", metres=(0.6, 0.6))
def mat_rubber(W, H, rng):
    """The kitchen's black anti-fatigue mats: round drain holes on offset rows (16 by 18 per 0.6 m), worn smooth on
    top, a few crumbs"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    cols, rows = 16, 18
    pw, ph = W / cols, H / rows
    r_ = np.floor(Y / ph)
    xs = X / pw + 0.5 * (r_ % 2)
    dx = (xs - np.floor(xs) - 0.5) * pw
    dy = (Y / ph - r_ - 0.5) * ph
    d = np.sqrt(dx * dx + dy * dy)
    rad = 0.36 * pw
    hole = smooth(rad + 0.6, rad - 0.6, d)
    rim = np.exp(-((d - rad - 1.2) / 1.0) ** 2)
    floor_ = smooth(rad * 0.75, 0.0, d)                     # the floor showing at the bottom of each hole
    wear = N.fbm(50, 3)
    colour = C(34, 34, 36) * (1 + 0.08 * wear + 0.06 * N.blob(0.8) + 0.12 * rim)[..., None]
    inside = mix(C(13, 13, 14), C(52, 50, 47), 0.5 * floor_)
    colour = mix(colour, inside, hole)
    crumbs = N.specks(0.0025, 0.7)
    colour = mix(colour, C(150, 120, 80), 0.7 * crumbs)
    h = 3.0 * (1 - hole) - 2.5 * hole + 0.6 * rim + 0.15 * wear + 0.5 * crumbs
    h = blur(h, 0.6, True)
    gloss = (0.22 + 0.05 * wear) * (1 - hole) + 0.06 * hole
    return np.clip(colour, 0, 1), gloss, h


@surface("concrete", metres=(2.0, 2.0))
def concrete(W, H, rng):
    """The kitchen and back hall's worn grey concrete floor, sealed long ago, with dark grease stains, aggregate,
    pits and a hairline crack: 2 m square"""
    N = Noise(W, H, rng)
    low = N.fbm(110, 3)
    mid = N.fbm(14, 3)
    agg_dark = N.specks(0.05, 0.9)
    agg_light = N.specks(0.04, 0.7)
    trowel = N.blob(60, 1.5, angle=rng.random() * math.pi)
    stain = smooth(0.0, 2.2, N.fbm(55, 5, gain=0.6) + 0.4 * low) * (0.7 + 0.3 * smooth(-1, 1, N.fbm(6, 3)))
    drip = smooth(1.5, 2.6, N.fbm(6, 3)) * smooth(0.0, 1.0, N.fbm(40, 2))
    colour = C(140, 138, 132) * (1 + 0.07 * low + 0.04 * mid + 0.015 * trowel)[..., None]
    colour = colour * (1 - 0.18 * agg_dark + 0.12 * agg_light)[..., None]
    colour = mix(colour, C(74, 72, 67) * (1 + 0.1 * mid)[..., None], 0.6 * stain)
    colour = mix(colour, C(52, 50, 48), 0.5 * drip)
    # a hairline crack wandering across (drawn a tile over each way, so it tiles)
    m = Mask(W, H, ss=3, wrap=True)
    x, y, a = rng.random() * W, rng.random() * H, rng.random() * math.pi * 2
    pts = [(x, y)]
    for _ in range(90):
        a += rng.normal(0, 0.35)
        x, y = x + math.cos(a) * 4, y + math.sin(a) * 4
        pts.append((x, y))
    m.line(pts, 0.8)
    crack = m.get()
    pits = N.specks(0.006, 0.8)
    colour = mix(colour, C(40, 39, 37), 0.8 * np.maximum(crack, pits))
    h = 1.2 * low + 0.25 * mid + 0.06 * trowel - 1.5 * crack - 1.0 * pits + 0.15 * (agg_light - agg_dark)
    gloss = 0.2 + 0.18 * stain + 0.1 * drip - 0.05 * agg_dark
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


def brushed_steel(N, W, H, base=C(176, 178, 182), amount=1.0):
    """Brushed stainless along u: colour and height"""
    long_ = N.blob(140, 0.6)
    short = N.blob(35, 0.5)
    smudge = N.fbm(70, 3)
    colour = base * (1 + amount * (0.05 * long_ + 0.035 * short) + 0.035 * smudge)[..., None]
    h = 0.12 * long_ + 0.1 * short + 0.8 * N.fbm(80, 2)
    return colour, h, smudge


@surface("stainless", metres=(1.0, 1.0))
def stainless(W, H, rng):
    """Brushed stainless steel, the brushing along u (walls, shelves, hood, tables): 1 m square, with soft smudges"""
    N = Noise(W, H, rng)
    colour, h, smudge = brushed_steel(N, W, H)
    return np.clip(colour, 0, 1), np.clip(0.78 + 0.05 * smudge, 0, 1), h


@surface("stainless_baffle", metres=(0.5, 0.5))
def stainless_baffle(W, H, rng):
    """The hood's grease filters: one 0.5 m filter per repeat (its frame shared with the next), eight vertical
    baffles, golden with old grease running down"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    fw = 9.0
    frame = np.maximum(smooth(fw, fw - 1.5, np.minimum(X, W - X)), smooth(fw, fw - 1.5, np.minimum(Y, H - Y)))
    n = 8
    p = (W - 2 * fw) / n
    xl = ((X - fw) / p) % 1.0
    # each baffle a curved channel: a crest, a hollow, and its lips casting a shadow line
    prof = np.cos(2 * np.pi * xl)
    lip = np.exp(-((xl - 0.02) / 0.03) ** 2) + np.exp(-((xl - 0.98) / 0.03) ** 2)
    colour, hb, smudge = brushed_steel(N, W, H, C(170, 168, 160), 0.6)
    colour = colour * (1 + 0.16 * prof - 0.3 * lip)[..., None]
    grease = (0.55 + 0.25 * smooth(-1.5, 1.5, N.fbm(60, 3))) * (1 - frame)
    runs = smooth(1.0, 2.5, N.blob(40, 1.2, angle=math.pi / 2)) * (1 - frame)
    colour = mix(colour, C(176, 136, 70) * colour.mean(-1, keepdims=True) * 1.5, 0.5 * grease + 0.25 * runs)
    colour = mix(colour, C(170, 172, 176), frame)
    h = (1 - frame) * (3.0 * prof - 2.0 * lip) + frame * 4.0 + 0.2 * runs + hb * 0.3
    gloss = 0.7 - 0.2 * grease + 0.1 * frame
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


@surface("copper", metres=(0.3, 0.3))
def copper(W, H, rng):
    """The hanging pans' copper: tileable, but with a pan bottom's spun rings round the centre of the cell
    (fading out by the edges, so it still repeats); tarnished brown in patches, 0.3 m"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    r = np.hypot(X - W / 2, Y - H / 2)
    spin = smooth(0.48 * W, 0.36 * W, r)
    rings = np.sin(2 * np.pi * r / 2.2 + 3 * N.blob(20)) * spin
    brush = N.blob(80, 0.6)
    tarnish = smooth(-0.6, 1.8, N.fbm(40, 4, gain=0.6))
    heat = smooth(0.6, 2.0, N.fbm(80, 2))
    colour = C(190, 112, 66) * (1 + 0.05 * rings + 0.04 * brush * (1 - spin) + 0.05 * N.fbm(10, 2))[..., None]
    colour = mix(colour, C(130, 72, 42), 0.4 * tarnish)
    colour = mix(colour, C(150, 92, 110), 0.18 * heat)
    colour = colour * (1 - 0.12 * smooth(0.40 * W, 0.46 * W, r) * smooth(0.5 * W, 0.46 * W, r))[..., None]
    h = 0.15 * rings + 0.1 * brush + 0.4 * N.fbm(50, 2)
    gloss = 0.7 - 0.25 * tarnish
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


@surface("pipe_iron", metres=(0.5, 0.5))
def pipe_iron(W, H, rng):
    """Black iron pipe (the window bar's legs): mill scale mottled grey and a little rusty, oiled; 0.5 m"""
    N = Noise(W, H, rng)
    mott = N.fbm(10, 4)
    scale = smooth(0.4, 1.4, N.fbm(28, 3))
    rust = smooth(1.6, 2.6, N.fbm(8, 3))
    lines = N.blob(60, 0.5)
    colour = C(40, 40, 42) * (1 + 0.12 * mott + 0.03 * lines)[..., None]
    colour = mix(colour, C(78, 76, 74), 0.4 * scale)
    colour = mix(colour, C(84, 54, 36), 0.5 * rust)
    h = 0.35 * mott + 0.25 * scale + 0.1 * lines + 0.3 * rust
    gloss = 0.32 + 0.1 * scale - 0.2 * rust
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


@surface("paving", metres=(2.0, 2.0))
def paving(W, H, rng):
    """The terrace: grey granite flagstones in random-width courses, sanded joints, flamed (rough) faces: 2 m"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    pxm = W / 2.0
    heights = [0.6, 0.4, 0.5, 0.5]
    tops = np.concatenate([[0.0], np.cumsum(heights)]) * pxm
    stone = np.full((H, W), -1)
    edge = np.full((H, W), 99.0)
    for ri, hgt in enumerate(heights):
        y0, y1 = tops[ri], tops[ri + 1]
        widths = []
        while sum(widths) < 2.0 - 1e-6:
            w = float(rng.choice([0.4, 0.5, 0.6, 0.8]))
            widths.append(min(w, 2.0 - sum(widths)))
        if widths[-1] < 0.3 and len(widths) > 1:
            last = widths.pop()
            widths[-1] += last
        off = rng.random() * W
        xs = (np.concatenate([[0.0], np.cumsum(widths)]) * pxm + off)
        mrow = (Y >= y0) & (Y < y1)
        xr = (X - off) % W
        k = np.minimum(np.searchsorted(np.cumsum(widths) * pxm, xr, side="right"), len(widths) - 1)
        x0 = (xs[k] - off)
        x1 = (xs[k + 1] - off)
        ex = np.minimum(xr - x0, x1 - xr)
        ey = np.minimum(Y - y0, y1 - Y)
        stone[mrow] = (ri * 10 + k)[mrow]
        edge[mrow] = np.minimum(ex, ey)[mrow]
    ragged = edge - 1.4 + 0.6 * N.fbm(2.0, 3)
    face = smooth(-0.3, 0.9, ragged)
    tint = rng.random((60, 3))
    t = tint[np.maximum(stone, 0)]
    base = C(148, 148, 146) * (0.92 + 0.14 * t[..., 0:1]) * (1 + 0.03 * (t[..., 1:2] - 0.5) * np.array([1, 0, -1]))
    mica = N.specks(0.11, 0.6)
    feld = N.specks(0.16, 0.7)
    pink = N.specks(0.03, 0.9)
    colour = base * (1 + 0.05 * N.fbm(20, 3) + 0.04 * N.blob(0.5))[..., None]
    colour = mix(colour, C(44, 44, 46), 0.85 * mica)
    colour = mix(colour, C(206, 204, 200), 0.7 * feld)
    colour = mix(colour, C(176, 152, 140), 0.6 * pink)
    joint_c = C(112, 106, 96) * (1 + 0.12 * N.blob(0.8))[..., None]
    colour = mix(joint_c, colour, face)
    h = 3.0 * smooth(-0.3, 2.5, ragged) + face * (0.45 * N.blob(0.8) + 0.5 * N.fbm(15, 2)) + (1 - face) * 0.3 * N.blob(0.7)
    colour *= (1 + 0.05 * np.clip(cavity(h, 3), -2, 1))[..., None]
    gloss = 0.1 + 0.25 * mica * face
    return np.clip(colour, 0, 1), np.clip(gloss, 0, 1), h


@surface("timber", metres=(1.0, 1.0))
def timber(W, H, rng):
    """The terrace railing's weathered wood: silver-grey, grain along u standing proud of the worn earlywood,
    long drying checks: 1 m"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    late = periodic_grain(N, X, Y, W, H, rng, 6.0, wobble=9)
    checks = np.exp(-(N.blob(120, 3.0) / 0.05) ** 2) * smooth(0.3, 1.2, N.fbm(90, 2))
    fibres = N.blob(30, 0.5)
    colour = C(146, 138, 126) * (1.08 - 0.25 * late + 0.06 * fibres + 0.05 * N.fbm(50, 3))[..., None]
    colour = mix(colour, C(122, 104, 84), 0.25 * smooth(0.5, 1.5, N.fbm(60, 3)))     # brown where it's less weathered
    colour = mix(colour, C(40, 36, 32), 0.85 * checks)
    h = 0.9 * late + 0.25 * fibres - 2.5 * checks
    gloss = 0.05 + 0.02 * late
    return np.clip(colour, 0, 1), gloss, h


@surface("canvas_awning", metres=(0.5, 0.5))
def canvas_awning(W, H, rng):
    """The awning's acrylic canvas: one black and one tan stripe per 0.5 m (stripes run along v), a fine basket
    weave, faded and streaked a little by the weather"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    # 1 in the black stripe (the left half of the repeat), 0 in the tan one, the edge a little fuzzy
    stripe = smooth(-0.012, 0.012, np.sin(2 * np.pi * X / W) + 0.006 * N.blob(0.8))
    weave = canvas_weave(W, H, 3.0)
    slub = N.blob(12, 0.5, angle=math.pi / 2)
    fade = smooth(0.5, 1.8, N.fbm(60, 3))
    runs = smooth(1.2, 2.4, N.blob(60, 1.5, angle=math.pi / 2))
    black = C(36, 36, 39) * (1 + 0.12 * fade)[..., None]
    tan = C(188, 160, 108)
    colour = mix(tan, black, stripe) * (1 + 0.07 * weave + 0.04 * slub)[..., None]
    colour = colour * (1 - 0.1 * runs)[..., None]
    seam = np.exp(-((np.abs(wrapd(X, W / 2))) / 1.0) ** 2)            # stitching where the stripes meet
    colour *= (1 - 0.15 * seam)[..., None]
    h = 0.5 * weave + 0.2 * slub - 0.5 * seam
    return np.clip(colour, 0, 1), 0.1 + 0.03 * weave, h


@surface("glass_plain", metres=(1.0, 1.0))
def glass_plain(W, H, rng):
    """A window pane: pale glass with soft diagonal reflections of the sky and faint smudges (tileable)"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    band = 0.5 + 0.5 * np.sin(2 * np.pi * (X / W + Y / H) + 0.8 * N.blob(80))
    band2 = 0.5 + 0.5 * np.sin(2 * np.pi * (2 * X / W + 2 * Y / H) + 1.0)
    smudge = smooth(0.8, 2.0, N.fbm(30, 3))
    colour = mix(C(184, 200, 210), C(222, 232, 238), 0.55 * band + 0.15 * band2)
    colour = mix(colour, C(200, 204, 204), 0.25 * smudge)
    h = 0.05 * N.fbm(60, 2)
    gloss = 0.9 - 0.12 * smudge
    return np.clip(colour, 0, 1), gloss, h


@surface("box_side", metres=(0.3, 0.3))
def box_side(W, H, rng):
    """A pizza box's side: kraft liner over corrugation, the flutes showing faintly through as vertical ribs every
    3 mm, fibres and a few scuffs: 0.3 m"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    n = round(W / 5.0)
    rib = np.sin(2 * np.pi * X * n / W)
    fibres = sum(N.blob(2.5, 0.5, angle=a) for a in (0.2, 1.3, 2.4)) / 1.7
    scuff = smooth(1.5, 2.6, N.fbm(12, 3))
    colour = C(194, 158, 110) * (1 + 0.025 * rib + 0.025 * fibres + 0.025 * N.fbm(40, 3) - 0.06 * scuff)[..., None]
    colour = mix(colour, C(120, 92, 64), 0.25 * N.specks(0.01, 0.6))
    h = 0.4 * rib + 0.12 * fibres - 0.3 * scuff
    return np.clip(colour, 0, 1), 0.06 + 0.01 * fibres, h


# ---------------------------------------------------------------------------------------------------- pictures

class Pic:
    """A picture being painted: colour, gloss and height, laid down shape by shape through masks (0..1)"""

    def __init__(self, W, H, colour, gloss=0.2, height=0.0):
        self.W, self.H = W, H
        self.col = fill(W, H, colour) if np.ndim(colour) == 1 else np.array(colour, float)
        self.gl = np.full((H, W), float(gloss))
        self.h = np.full((H, W), float(height))

    def lay(self, m, colour=None, gloss=None, height=None, add=0.0):
        if colour is not None:
            self.col = mix(self.col, colour, m)
        if gloss is not None:
            self.gl = self.gl + (gloss - self.gl) * m
        if height is not None:
            self.h = self.h + (height - self.h) * m
        if np.any(add):
            self.h = self.h + add * m

    def out(self):
        return np.clip(self.col, 0, 1), np.clip(self.gl, 0, 1), self.h


def sphere_shade(X, Y, cx, cy, r):
    """Light on a ball or a round knob from the upper left: 0.35 at the shadowed edge, about 1.1 at the highlight"""
    nx, ny = (X - cx) / r, (Y - cy) / r
    nz = np.sqrt(np.clip(1 - nx * nx - ny * ny, 0, 1))
    lam = np.clip(-0.5 * nx - 0.5 * ny + 0.7 * nz, 0, 1)
    spec = np.clip(-0.35 * nx - 0.35 * ny + 0.87 * nz, 0, 1) ** 30
    return 0.35 + 0.75 * lam + 0.6 * spec


def cyl_shade(t):
    """Light across a cylinder seen side on (t 0..1 across it), lit from the left: shadowed edges, a highlight"""
    a = (t - 0.5) * np.pi
    lam = np.clip(np.cos(a - (-0.5)), 0, 1)
    spec = np.clip(np.cos(a + 0.45), 0, 1) ** 40
    return 0.3 + 0.75 * lam + 0.7 * spec


@surface("brick_hall", tile=(True, False), metres=(1.2, 2.4))
def brick_hall(W, H, rng):
    """The back hall's exposed brick: dark reds and greys smeared with old lime and plaster, under a band of
    grey-white plaster broken off in a ragged edge (from about 20 to 45 % down), the brown scratch coat showing
    along the break, dirt along the floor. 1.2 m wide (it repeats along u) by 2.4 m tall"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    pal = [C(104, 40, 38), C(92, 36, 36), C(118, 50, 44), C(82, 38, 42), C(104, 94, 92), C(80, 72, 72),
           C(124, 64, 54), C(66, 32, 34)]
    colour, gloss, h, face = brickwork(W, H, rng, N, 36, 6, pal, [18, 16, 12, 12, 8, 6, 8, 10], C(206, 200, 190),
                                       joint=4.6, soot=0.06, bloom=0.12, smear=0.5)
    xs = (np.arange(W) + 0.5) / W
    edge = np.full(W, 0.31 * H)
    for k in range(1, 12):
        edge += rng.uniform(0.3, 1.0) * 0.075 * H / k ** 1.5 * np.sin(2 * np.pi * k * xs + rng.random() * 6.283)
    dist = Y - edge[None, :] + 4.0 * N.fbm(5, 3) + 1.5 * N.blob(1.0)
    band = 7.0 + 6.0 * smooth(-1, 1, N.fbm(30, 2))
    plaster = smooth(0.8, -0.8, dist)
    coat = smooth(band + 0.8, band - 0.8, dist) * (1 - plaster)
    # crumbs of scratch coat left stuck on the bricks just below the break
    crumbs = smooth(0.6, 1.6, N.fbm(5, 3) - dist / 60.0) * (1 - plaster) * (1 - coat) * (dist < 70)
    coat = np.maximum(coat, crumbs * smooth(0.0, 0.5, N.blob(1.2)))
    coat_c = C(150, 138, 120) * (1 + 0.12 * N.fbm(2.0, 3) + 0.06 * N.fbm(12, 2))[..., None]
    plaster_c = C(222, 222, 217) * (1 + 0.025 * N.fbm(60, 3) + 0.01 * N.blob(1.0))[..., None]
    plaster_c = mix(plaster_c, C(196, 192, 184), 0.4 * smooth(25, 0, -dist) * plaster)     # grimy along the break
    colour = mix(colour, coat_c, coat)
    colour = mix(colour, plaster_c, plaster)
    h = h + coat * (4.5 + 0.8 * N.fbm(2, 3)) + plaster * (8.0 + 0.6 * N.fbm(40, 3) + 0.1 * N.blob(1.0) - h - coat * 4.5)
    # the break's lip: plaster edges catch light, a shadow under them
    lip = smooth(-3, 0, dist) * smooth(2, 0, dist)
    colour *= (1 - 0.25 * lip * (1 - plaster))[..., None]
    floor_dirt = smooth(0.88 * H, H, Y) * (0.6 + 0.4 * N.fbm(10, 2))
    colour *= (1 - 0.35 * floor_dirt)[..., None]
    gloss = np.where(plaster > 0.5, 0.12, gloss)
    return np.clip(colour, 0, 1), gloss, h


def panel_profile(X, Y, x0, y0, x1, y1):
    """A raised panel in a door's frame: a cove where it meets the frame, a bevel up to the flat field"""
    d = np.minimum(np.minimum(X - x0, x1 - X), np.minimum(Y - y0, y1 - Y))
    inside = d >= 0
    h = -2.6 * smooth(0, 2.2, d) + 3.6 * smooth(2.2, 17, d)
    return np.where(inside, h, 0.0), smooth(-0.5, 0.5, d)


def door_grain(N, X, Y, vertical):
    """The faint embossed grain of a moulded door, along v (vertical) or u"""
    if vertical:
        return ring_profile(X / 6.0 + 2.0 * N.fbm(14, 3, sy=160))
    return ring_profile(Y / 6.0 + 2.0 * N.fbm(160, 3, sy=14))


@surface("door_white", tile=(False, False))
def door_white(W, H, rng):
    """A white six-panel door (the back hall's), raised panels with a moulded grain, a brushed nickel knob on
    the right: the slab only, 1 x 2 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    st, mun = 0.14 * W, 0.05 * W
    rails = [(0.06, 0.215), (0.27, 0.57), (0.665, 0.895)]
    h = np.zeros((H, W))
    vert = (X < st) | (X > W - st) | (np.abs(X - W / 2) < mun)
    grain = np.where(vert, door_grain(N, X, Y, True), door_grain(N, X, Y, False))
    for (a, b) in rails:
        for (x0, x1) in ((st, W / 2 - mun), (W / 2 + mun, W - st)):
            hp, m = panel_profile(X, Y, x0, a * H, x1, b * H)
            h = h + hp
            grain = np.where(m > 0.5, door_grain(N, X, Y, True), grain)
    h = h + 0.12 * grain + 0.3 * N.fbm(60, 2)
    p = Pic(W, H, C(236, 236, 231), 0.32)
    p.col = p.col * (1 + 0.012 * N.fbm(40, 3) - 0.02 * grain)[..., None]
    p.col = p.col * bake_light(h, 0.35)[..., None] * (1 + 0.05 * np.clip(cavity(h, 3, False), -2, 0.5))[..., None]
    p.h = h
    # scuffs near the bottom and round the knob
    scuff = smooth(0.86 * H, H, Y) * smooth(1.4, 2.6, N.fbm(3, 3, sy=1.5))
    p.col = mix(p.col, C(170, 170, 166), 0.25 * scuff)
    kx, ky = 0.865 * W, 0.615 * H
    smudge = np.exp(-((X - kx) ** 2 + (Y - ky) ** 2) / (2 * 30.0 ** 2)) * smooth(-0.5, 1.5, N.fbm(6, 2))
    p.col = mix(p.col, C(196, 194, 186), 0.3 * smudge)
    # the knob: a rose plate and the knob, brushed nickel
    for r_, hgt in ((0.075 * W, 2.0), (0.052 * W, 9.0)):
        hd, m = dome(X, Y, kx, ky, r_)
        shade = sphere_shade(X, Y, kx, ky, r_ * (1.6 if hgt < 3 else 1.0))
        p.lay(m, C(176, 176, 172) * shade[..., None] * (1 + 0.04 * N.blob(30, 0.6))[..., None], 0.75)
        p.h = p.h + hgt * hd * m
    return p.out()


def blinds(p, X, Y, x0, y0, x1, y1, N, pitch=12.5):
    """Closed horizontal blinds behind glass, daylight showing between the slats, ladder cords"""
    inside = (X >= x0) & (X < x1) & (Y >= y0) & (Y < y1)
    f = ((Y - y0) / pitch) % 1.0
    slat = 0.9 + 0.14 * np.cos(np.pi * (f - 0.35)) - 0.25 * smooth(0.72, 0.84, f)
    gap = smooth(0.84, 0.88, f) * (1 - smooth(0.97, 1.0, f))
    col = mix(C(238, 236, 230) * slat[..., None], C(255, 250, 236), gap)
    col = col * (1 + 0.015 * N.blob(40, 1.0))[..., None]
    for t in (0.22, 0.78):
        cx = x0 + (x1 - x0) * t
        cord = smooth(1.0, 0.3, np.abs(X - cx))
        col = mix(col, C(206, 200, 188), 0.8 * cord)
    sheen = smooth(0.0, 1.0, np.sin(np.pi * ((X - x0) + (Y - y0) * 0.6) / ((x1 - x0) * 1.2)) ** 8) * 0.12
    col = mix(col, C(255, 255, 255), sheen)
    p.lay(inside.astype(float), col, 0.88, height=0.0)
    p.h = p.h + inside * (-1.0 * gap)


@surface("front_door", tile=(False, False))
def front_door(W, H, rng):
    """The front door: white, a glass upper half with closed white blinds and daylight between the slats, a
    brass pull handle and deadbolt on the right, a flat panel below: the slab only, 1 x 2 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    st = 0.15 * W
    gy0, gy1 = 0.065 * H, 0.565 * H
    p = Pic(W, H, C(234, 232, 224), 0.3)
    # the glass's bead: a small moulding round the opening
    d = np.minimum(np.minimum(X - st, W - st - X), np.minimum(Y - gy0, gy1 - Y))
    bead = np.exp(-((d + 3) / 2.5) ** 2) * (d > -7)
    hp, m = panel_profile(X, Y, st, 0.665 * H, W - st, 0.895 * H)
    grain = np.where((X < st) | (X > W - st) | (m > 0.5), door_grain(N, X, Y, True), door_grain(N, X, Y, False))
    p.h = 0.6 * hp + 2.0 * bead + 0.12 * grain + 0.3 * N.fbm(60, 2)
    p.col = p.col * (1 + 0.012 * N.fbm(40, 3) - 0.018 * grain)[..., None] * bake_light(p.h, 0.35)[..., None]
    blinds(p, X, Y, st, gy0, W - st, gy1, N)
    scuff = smooth(0.86 * H, H, Y) * smooth(1.4, 2.6, N.fbm(3, 3, sy=1.5))
    p.col = mix(p.col, C(168, 166, 158), 0.25 * scuff)
    # the deadbolt and the pull handle, brass
    brass = C(186, 158, 96)
    bx, by = 0.88 * W, 0.6 * H
    hd, mm = dome(X, Y, bx, by, 0.045 * W)
    p.lay(mm, brass * sphere_shade(X, Y, bx, by, 0.06 * W)[..., None], 0.8)
    p.h = p.h + 3 * hd * mm
    key = Mask(W, H).rect(bx - 1.2, by - 7, bx + 1.2, by + 7).get()
    p.lay(key, C(40, 34, 22))
    hx0, hx1, hy0, hy1 = 0.865 * W, 0.905 * W, 0.64 * H, 0.8 * H
    t = np.clip((X - hx0) / (hx1 - hx0), 0, 1)
    bar = Mask(W, H).rect(hx0, hy0, hx1, hy1).get()
    for yy in (hy0, hy1):
        post = Mask(W, H).ellipse((hx0 + hx1) / 2, yy, 0.03 * W, 0.022 * W).get()
        p.lay(post, brass * 0.75, 0.75)
        p.h = p.h + 4 * post
    p.lay(bar, brass * cyl_shade(t)[..., None] * (1 + 0.04 * N.blob(1, 40, angle=math.pi / 2))[..., None], 0.85)
    p.h = p.h + bar * 7 * np.sqrt(np.clip(1 - (2 * t - 1) ** 2, 0, 1))
    return p.out()


_LOGO = None


def logo_ink():
    """The logo's ink (0..1: opaque and dark) cropped to its robust bounding box: the rows and columns with more
    than a few inked pixels, so stray edge pixels cannot pull the centre off"""
    global _LOGO
    if _LOGO is None:
        im = np.asarray(Image.open(LOGO).convert("RGBA"), np.float64) / 255.0
        ink = im[..., 3] * (1.0 - im[..., :3].mean(-1))
        m = ink > 0.5
        cols = np.where(m.sum(0) > 6)[0]
        rows = np.where(m.sum(1) > 6)[0]
        _LOGO = ink[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1]
    return _LOGO


def logo_mask(W, H, frac):
    """The logo's ink fitted into frac of the picture and centred on its bounding box"""
    ink = logo_ink()
    ih, iw = ink.shape
    s = frac * min(W / iw, H / ih)
    w, h = int(round(iw * s)), int(round(ih * s))
    small = np.asarray(Image.fromarray(to8(ink)).resize((w, h), Image.LANCZOS), np.float64) / 255.0
    out = np.zeros((H, W))
    x0, y0 = (W - w) // 2, (H - h) // 2
    out[y0:y0 + h, x0:x0 + w] = small
    return out


@surface("logo_glass", tile=(False, False))
def logo_glass(W, H, rng):
    """The shop window with the logo in dark grey vinyl on pale glass, as read from outside (mirror the texture
    coordinates for the inside), centred, 80 % of the picture; 2 x 2 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    sky = mix(C(226, 234, 240), C(196, 210, 220), Y / H)
    refl = smooth(0.4, 1.0, np.sin(2 * np.pi * (X + 0.7 * Y) / (W * 1.4) + 0.5 * N.blob(120)) ** 2)
    p = Pic(W, H, mix(sky, C(236, 242, 246), 0.35 * refl), 0.9)
    smear = smooth(0.8, 2.2, N.fbm(40, 3)) * smooth(0.6 * H, H, Y)
    p.col = mix(p.col, C(200, 202, 200), 0.25 * smear)
    ink = logo_mask(W, H, 0.80)
    vinyl = C(52, 54, 58) * (1 + 0.04 * N.fbm(20, 2) + 0.03 * refl)[..., None]
    p.lay(ink, vinyl, 0.5)
    p.h = 0.9 * blur(ink, 0.6) + 0.04 * N.fbm(60, 2)
    return p.out()


@surface("logo_sign", tile=(False, False))
def logo_sign(W, H, rng):
    """The fascia sign: the logo in cream paint on a black-painted board inside a thin gilded border, centred;
    2 x 2 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    brush = N.blob(80, 0.8)
    grain = door_grain(N, X, Y, False)
    p = Pic(W, H, C(26, 26, 28) * (1 + 0.15 * brush + 0.12 * grain + 0.1 * N.fbm(80, 3))[..., None], 0.35)
    p.h = 0.3 * grain + 0.2 * brush
    d = np.minimum(np.minimum(X, W - X), np.minimum(Y, H - Y))
    gold_m = smooth(26, 27.5, d) * smooth(41, 39.5, d)
    sheen = 0.5 + 0.5 * np.sin((X + Y) / W * 9.0 + 2.0 * N.blob(40))
    gold = mix(C(146, 106, 44), C(236, 204, 128), sheen) * (1 + 0.08 * N.blob(1.0))[..., None]
    across = np.clip((d - 27) / 13.0, 0, 1)
    p.lay(gold_m, gold * (0.8 + 0.4 * np.sin(np.pi * across))[..., None], 0.85)
    p.h = p.h + gold_m * 3.0 * np.sin(np.pi * across)
    ink = logo_mask(W, H, 0.76)
    cream = C(234, 224, 194) * (1 + 0.03 * N.blob(50, 0.8) + 0.03 * N.fbm(10, 2))[..., None]
    p.lay(ink, cream, 0.4)
    p.h = p.h + 0.8 * blur(ink, 0.7)
    edge = np.minimum(d / 8.0, 1.0)
    p.col *= (0.75 + 0.25 * edge)[..., None]
    return p.out()


def kraft(N, W, H, base=C(194, 158, 110)):
    fibres = sum(N.blob(2.5, 0.5, angle=a) for a in (0.2, 1.3, 2.4)) / 1.7
    colour = base * (1 + 0.03 * fibres + 0.05 * N.fbm(40, 3) + 0.02 * N.blob(0.6))[..., None]
    colour = mix(colour, C(120, 92, 64), 0.25 * N.specks(0.01, 0.6))
    return colour, 0.12 * fibres


@surface("logo_box", tile=(False, False))
def logo_box(W, H, rng):
    """A pizza box's lid: the logo printed in red on kraft card, a little uneven where the ink took, centred"""
    N = Noise(W, H, rng)
    colour, h = kraft(N, W, H)
    p = Pic(W, H, colour, 0.06)
    p.h = h
    ink = logo_mask(W, H, 0.74)
    take = smooth(-2.4, -0.8, N.blob(0.7)) * (0.85 + 0.15 * smooth(-1, 1, N.fbm(12, 2)))
    red = C(170, 40, 34) * (1 + 0.06 * N.fbm(8, 2))[..., None]
    p.lay(ink * take, red * (colour / C(194, 158, 110)).mean(-1, keepdims=True), 0.1)
    X, Y = coords(W, H)
    d = np.minimum(np.minimum(X, W - X), np.minimum(Y, H - Y))
    p.col *= (0.9 + 0.1 * smooth(0, 10, d))[..., None]
    return p.out()


@surface("transom_1103B", tile=(False, False))
def transom_1103B(W, H, rng):
    """The transom over the front door: dark glass, the awning's stripes faint beyond it, the street number
    "1103B" in gold leaf with a thin dark outline, as read from outside; 2 x 1 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    base = mix(C(46, 50, 58), C(30, 33, 40), Y / H)
    # the awning's underside beyond: stripes running away, a little perspective
    u = (X - W / 2) / (1.0 + 0.25 * Y / H) + W / 2
    stripe = smooth(-0.15, 0.15, np.sin(2 * np.pi * u / 92.0))
    beyond = mix(C(82, 72, 54), C(34, 34, 38), stripe)
    p = Pic(W, H, mix(base, beyond, 0.28), 0.9)
    refl = smooth(0.5, 1.0, np.sin(2 * np.pi * (X * 0.8 + Y) / (W * 0.9) + 0.6 * N.blob(100)) ** 2)
    p.col = mix(p.col, C(150, 160, 172), 0.18 * refl)
    m = Mask(W, H)
    size = fit_size("timesbd.ttf", 330, "1103B", W * 0.78)
    m.text(W / 2, H / 2 + 8, "1103B", "timesbd.ttf", size, anchor="mm")
    t = m.get()
    outline = np.clip(ndimage.grey_dilation(t, size=(7, 7)) - t, 0, 1)
    p.lay(outline, C(30, 22, 12), 0.6)
    # gold leaf: a burnished gradient, the squares the leaf was laid in, fine crinkles
    leafsq = np.minimum(np.abs(wrapd(X - 0.3 * Y, 70.0)), np.abs(wrapd(Y + 0.3 * X, 70.0)))
    seams = smooth(1.2, 0.0, leafsq)
    shine = 0.5 + 0.5 * np.sin(Y / H * 5.0 - X / W * 2.0 + 1.5 * N.blob(30))
    gold = mix(C(170, 120, 44), C(250, 222, 140), shine) * (1 + 0.07 * N.blob(0.8) - 0.12 * seams)[..., None]
    p.lay(t, gold, 0.88)
    p.h = 0.6 * t + 0.3 * outline + 0.05 * N.fbm(60, 2)
    return p.out()


def wood_frame(p, N, X, Y, W, H, fw, colour, weathered=0.0):
    """A mitred wooden frame round the picture's edge, fw pixels wide, its grain running along each member"""
    d = np.stack([Y, H - Y, X, W - X])
    member = np.argmin(d, axis=0)
    dm = d.min(axis=0)
    frame = smooth(fw + 0.5, fw - 0.5, dm)
    gh = ring_profile(Y / 4.0 + 3.0 * N.fbm(160, 3, sy=12))
    gv = ring_profile(X / 4.0 + 3.0 * N.fbm(12, 3, sy=160))
    g = np.where(member < 2, gh, gv)
    fib = np.where(member < 2, N.blob(30, 0.6), N.blob(0.6, 30))
    col = colour * (1.1 - 0.35 * g + 0.06 * fib + 0.06 * N.fbm(40, 2))[..., None]
    if weathered:
        worn = smooth(0.3, 1.5, N.fbm(10, 3)) * weathered
        col = mix(col, C(120, 92, 66) * (1.05 - 0.3 * g)[..., None], worn)
    across = np.clip(dm / fw, 0, 1)
    prof = np.sin(np.pi * np.clip(across * 1.1, 0, 1)) * 0.8 + 0.4
    col = col * (0.75 + 0.35 * prof)[..., None]
    mitre = np.exp(-(np.abs(np.abs(X - np.where(X < W / 2, 0, W)) - np.abs(Y - np.where(Y < H / 2, 0, H))) / 0.8) ** 2) * (dm < fw)
    col = col * (1 - 0.4 * mitre)[..., None]
    p.lay(frame, col, 0.25)
    p.h = p.h * (1 - frame) + frame * (4.0 * prof + 0.3 * g)
    # the board sits in the frame's shadow
    shadow = smooth(fw + 8, fw, dm) * (1 - frame)
    p.col *= (1 - 0.35 * shadow)[..., None]
    return frame


def chalk_layer(W, H, N, lines):
    """Chalk writing: lines [(x, y, text, face, size, rgb, anchor)], roughened where the chalk skipped over the
    board's grain. Returns coverage (0..1) and colour."""
    cov = np.zeros((H, W))
    col = np.zeros((H, W, 3))
    grain = N.blob(0.55)
    for (x, y, txt, face, size, rgb, anchor) in lines:
        m = Mask(W, H, ss=3).text(x, y, txt, face, size, anchor=anchor).get()
        m = blur(m, 0.45)
        m = m * smooth(-1.6, 0.4, grain) * (0.88 + 0.12 * N.blob(3))
        col = mix(col, np.asarray(rgb), np.clip(m * 3, 0, 1))
        cov = np.maximum(cov, m)
    return cov, col


def chalkboard(W, H, rng, frame_colour, weathered, lines, doodles=None):
    """A chalkboard in a wooden frame: a green-black board with old chalk wiped round on it, and lines of chalk"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    board = C(36, 44, 40) * (1 + 0.06 * N.fbm(50, 3) + 0.03 * N.blob(0.7))[..., None]
    p = Pic(W, H, board, 0.15)
    haze = smooth(0.0, 1.8, N.fbm(45, 4)) * 0.16
    wipes = Mask(W, H, ss=2)
    for _ in range(26):
        cx, cy, r = rng.uniform(40, W - 40), rng.uniform(40, H - 40), rng.uniform(20, 70)
        a0 = rng.uniform(0, 6.28)
        pts = [(cx + r * math.cos(a0 + t), cy + r * 0.6 * math.sin(a0 + t)) for t in np.linspace(0, rng.uniform(1.5, 4), 30)]
        wipes.line(pts, rng.uniform(8, 20), int(rng.uniform(20, 60)))
    haze = haze + 0.5 * blur(wipes.get(), 3)
    p.col = mix(p.col, C(130, 138, 132), np.clip(haze, 0, 0.5))
    dust = smooth(H - 70, H - 36, Y) * (0.5 + 0.5 * N.fbm(6, 2))
    p.col = mix(p.col, C(150, 156, 150), 0.25 * dust)
    cov, col = chalk_layer(W, H, N, lines)
    if doodles is not None:
        dm, dc = doodles(W, H, N)
        col = mix(col, dc, np.clip(dm * 3, 0, 1))
        cov = np.maximum(cov, dm)
    p.lay(cov, col, 0.1)
    p.h = 0.4 * cov + 0.08 * N.blob(0.7)
    wood_frame(p, N, X, Y, W, H, 30, frame_colour, weathered)
    return p.out()


CHALK = (238, 238, 230)
HAND, SCRIPT = "Inkfree.ttf", "segoescb.ttf"


@surface("chalk_special", tile=(False, False))
def chalk_special(W, H, rng):
    """The counter's "Pizza Special" chalkboard in a dark rustic frame, hand-lettered"""
    c = C(*CHALK)
    lines = [(W / 2, 92, "Pizza Special", SCRIPT, fit_size(SCRIPT, 50, "Pizza Special", W - 110), C(250, 214, 120), "ms"),
             (W / 2, 160, "Hot Honey Pepperoni", HAND, 34, c, "ms"),
             (W / 2, 214, "cup & char pepperoni,", HAND, 27, c, "ms"),
             (W / 2, 252, "fresh mozzarella, basil", HAND, 27, c, "ms"),
             (W / 2, 290, "& a drizzle of hot honey", HAND, 27, c, "ms"),
             (W / 2, 346, "on our cast iron crust", HAND, 27, C(236, 170, 160), "ms"),
             (W / 2, 420, '14"   $19', HAND, 40, C(250, 214, 120), "ms")]

    def doodles(W, H, N):
        m = Mask(W, H, ss=3)
        m.line([(120, 104), (200, 110), (300, 106), (390, 100)], 2.6)
        for sx in (95, 410):                               # little stars by the price
            pts = []
            for k in range(10):
                r = 13 if k % 2 == 0 else 5.5
                a = -math.pi / 2 + k * math.pi / 5
                pts.append((sx + r * math.cos(a), 404 + r * math.sin(a)))
            m.poly(pts)
        return m.get() * smooth(-1.6, 0.4, N.blob(0.55)), np.broadcast_to(C(250, 214, 120), (H, W, 3))

    return chalkboard(W, H, rng, C(78, 52, 38), 0.1, lines, doodles)


@surface("chalk_treats", tile=(False, False))
def chalk_treats(W, H, rng):
    """The counter's "Treats" chalkboard in a weathered grey-blue frame, hand-lettered desserts"""
    c = C(*CHALK)
    lines = [(W / 2, 92, "Treats", SCRIPT, 56, C(248, 190, 200), "ms"),
             (60, 160, "Pumpkin Streusel", HAND, 30, c, "ls"),
             (78, 196, "Cheesecake", HAND, 30, c, "ls"),
             (96, 230, "w/ salted caramel", HAND, 23, C(236, 200, 150), "ls"),
             (60, 282, "Warm Apple Pie", HAND, 30, c, "ls"),
             (60, 334, "Peppermint Brownie", HAND, 30, c, "ls"),
             (60, 386, "Hot Mulled Cider", HAND, 30, c, "ls"),
             (W - 60, 440, "ask us!", HAND, 26, C(170, 220, 180), "rs")]

    def doodles(W, H, N):
        m = Mask(W, H, ss=3)
        for y in (152, 274, 326, 378):
            m.ellipse(44, y - 9, 4.5)
        m.line([(150, 104), (250, 110), (360, 104)], 2.4)
        return m.get() * smooth(-1.6, 0.4, N.blob(0.55)), np.broadcast_to(C(248, 190, 200), (H, W, 3))

    return chalkboard(W, H, rng, C(116, 134, 140), 0.55, lines, doodles)


@surface("menu_board", tile=(False, False))
def menu_board(W, H, rng):
    """The wall menu: a framed chalkboard listing the pizzas with prices, hand-lettered"""
    c = C(*CHALK)
    pizzas = [("Cheese", "14"), ("Pepperoni", "16"), ("Margherita", "17"), ("Supreme", "19"), ("White Pie", "17"),
              ("Meat Lovers", "19"), ("Garden Veggie", "17"), ("BBQ Chicken", "18"), ("Hot Honey", "18")]
    lines = [(W / 2, 80, "PIZZAS", SCRIPT, 46, C(250, 200, 110), "ms"),
             (W - 52, 112, '14" pies', HAND, 20, C(200, 220, 200), "rs")]
    for i, (n, pr) in enumerate(pizzas):
        y = 150 + i * 34
        lines.append((54, y, n, HAND, 25, c, "ls"))
        lines.append((W - 54, y, pr, HAND, 25, C(250, 214, 120), "rs"))
        tw = text_width(HAND, 25, n)
        dots = "." * int((W - 140 - tw) / 7.5)
        lines.append((60 + tw, y, dots, HAND, 20, C(170, 176, 170), "ls"))
    lines.append((W / 2, 470, "gluten-free crust +3", HAND, 21, C(236, 170, 160), "ms"))
    return chalkboard(W, H, rng, C(96, 70, 50), 0.35, lines)


# --- the paintings: original pictures in the spirit of the two canvases in the restaurant (a fluffy black cat at
# a table against a muted green wall), painted with strokes

def green_wall(W, H, N):
    X, Y = coords(W, H)
    wall = mix(C(70, 102, 80), C(56, 86, 68), (X / W) * 0.5 + (1 - Y / H) * 0.5)
    return wall * (1 + 0.06 * N.fbm(60, 3) + 0.03 * N.fbm(12, 2))[..., None]


def cat_shape(W, H, head, rh, body, ears):
    """A fluffy black cat's silhouette: a head (centre, radius), a body ellipse (cx, cy, rx, ry), two ears"""
    m = Mask(W, H)
    m.ellipse(head[0], head[1], rh * 1.1, rh * 0.94)
    m.ellipse(body[0], body[1], body[2], body[3])
    m.ellipse((head[0] + body[0]) / 2, (head[1] + body[1]) / 2, rh * 1.05, rh)
    for e in ears:
        m.poly(e)
    return m.get()


def cat_fur(colour, mask, rng, count_edge=2600, count_in=4200, light=(-0.6, -0.8), under=None):
    """Paints a black cat's fur over its silhouette: soft form light from the upper left, fur strokes inside
    following the form, and fluffy strokes crossing its edge outwards"""
    H, W = mask.shape
    soft = blur(mask, 6.0)
    gy, gx = np.gradient(soft)
    g = np.hypot(gx, gy) + 1e-9
    nx, ny = -gx / g, -gy / g                                  # outward
    rim = smooth(0.15, 0.6, soft) * (1 - smooth(0.6, 0.95, soft))
    face = nx * light[0] + ny * light[1]
    lit = np.clip(face, 0, 1) * smooth(0.1, 0.9, blur(mask, 18)) * 0.6 + smooth(0.95, 0.4, blur(mask, 22)) * np.clip(face, 0, 1)
    fur = mix(C(17, 16, 20), C(54, 54, 64), np.clip(lit, 0, 1) * 0.75)
    col = mix(colour, fur, mask)
    segs = []
    pts = np.argwhere((soft > 0.2) & (soft < 0.85))
    for k in rng.choice(len(pts), min(count_edge, len(pts)), replace=False):
        y, x = pts[k]
        a = math.atan2(ny[y, x], nx[y, x]) + rng.normal(0, 0.35)
        L = rng.uniform(5, 15)
        x0, y0 = x - math.cos(a) * L * 0.4, y - math.sin(a) * L * 0.4
        c = fur[y, x] * rng.uniform(0.8, 1.1)
        segs.append((x0, y0, x0 + math.cos(a) * L, y0 + math.sin(a) * L, rng.uniform(1.0, 2.2), c, 1.0))
    inner = np.argwhere(mask > 0.9)
    for k in rng.choice(len(inner), min(count_in, len(inner)), replace=False):
        y, x = inner[k]
        a = math.atan2(ny[y, x], nx[y, x]) * 0.6 + math.pi / 2 * 0.4 + rng.normal(0, 0.4)
        L = rng.uniform(4, 10)
        c = fur[y, x] * rng.uniform(0.75, 1.25)
        segs.append((x, y, x + math.cos(a) * L, y + math.sin(a) * L, rng.uniform(0.9, 1.8), c, 0.8))
    order = rng.permutation(len(segs))
    col, rel = draw_strokes(col, [segs[i] for i in order])
    return col, rel, mask


def cat_eye(p, X, Y, cx, cy, r, sleepy=False, look=(0.0, 0.0)):
    """A cat's eye: a yellow-green iris with a darker ring, a big pupil, a catchlight; sleepy: the lid half down"""
    d = np.hypot(X - cx, Y - cy)
    iris = smooth(r + 0.7, r - 0.7, d)
    ring = smooth(r * 0.65, r, d)
    col = mix(C(222, 210, 104), C(150, 140, 46), ring) * (1 + 0.08 * ((Y - cy) / r))[..., None]
    p.lay(iris, col, 0.5)
    px, py = cx + look[0] * r, cy + look[1] * r
    pr = r * (0.36 if sleepy else 0.56)
    pupil = smooth(pr + 0.7, pr - 0.7, np.hypot((X - px) * (1.4 if sleepy else 1.0), Y - py))
    p.lay(pupil, C(12, 10, 12))
    ch = smooth(r * 0.2, r * 0.1, np.hypot(X - (cx - r * 0.35), Y - (cy - r * 0.35)))
    p.lay(ch, C(250, 250, 240))
    p.lay(smooth(r + 2.5, r + 0.5, d) * smooth(r - 0.5, r + 0.5, d), C(14, 12, 14))      # the dark rim
    if sleepy:
        # the upper lid: a straight, slightly slanted line across the top half
        lid = smooth(-0.8, 0.8, (cy - r * 0.05 + (X - cx) * 0.12) - Y) * smooth(r + 3, r - 1, d)
        p.lay(lid, C(22, 20, 26))
        low = smooth(-0.8, 0.8, Y - (cy + r * 0.72)) * smooth(r + 3, r - 1, d)
        p.lay(low, C(22, 20, 26))


def whiskers(p, W, H, x, y, side, rng):
    m = Mask(W, H, ss=4)
    for k in range(3):
        a = side * (0.15 - 0.17 * k) + (0 if side > 0 else math.pi)
        L = rng.uniform(52, 70)
        pts = [(x + math.cos(a) * L * t + side * 0.0, y + math.sin(a) * L * t + 14 * t * t) for t in np.linspace(0, 1, 12)]
        m.line(pts, 0.8, 170)
    p.lay(m.get(), C(196, 196, 190))


def pizza_pie(p, X, Y, cx, cy, rx, ry, rng, missing=None):
    """A pepperoni pizza seen at a slant (an ellipse): crust, cheese, slices of pepperoni, black olives.
    missing: (angle from, angle to) of a slice that has gone, in degrees, measured on the ellipse"""
    ex, ey = (X - cx) / rx, (Y - cy) / ry
    d = np.hypot(ex, ey)
    ang = np.degrees(np.arctan2(ey, ex)) % 360
    gone = np.zeros_like(d)
    if missing:
        a0, a1 = missing
        rel = (ang - a0) % 360
        span = (a1 - a0) % 360
        edge_px = np.where(rel < span, np.minimum(rel, span - rel), -np.minimum(rel - span, 360 - rel)) * math.pi / 180 * d * rx
        gone = smooth(-1.0, 1.0, edge_px) * smooth(1.04, 1.0, d)
    crust = smooth(1.0, 0.97, d) * (1 - gone)
    p.lay(crust, mix(C(214, 150, 78), C(170, 104, 50), smooth(0.85, 1.0, d)))
    cheese = smooth(0.86, 0.82, d) * (1 - gone)
    N = Noise(X.shape[1], X.shape[0], rng)
    cc = mix(C(244, 194, 92), C(232, 150, 60), smooth(-0.5, 1.5, N.fbm(5, 2)))
    p.lay(cheese, cc)
    for k in range(13):
        a, rr = rng.random() * 2 * math.pi, math.sqrt(rng.random()) * 0.68
        px, py = cx + math.cos(a) * rr * rx, cy + math.sin(a) * rr * ry
        aa = math.degrees(a) % 360
        if missing and (aa - missing[0] + 8) % 360 < (missing[1] - missing[0] + 16) % 360:
            continue
        m = smooth(1.0, 0.85, np.hypot((X - px) / (rx * 0.11), (Y - py) / (ry * 0.11)))
        p.lay(m * (1 - gone), C(176, 50, 36))
        p.lay(m * smooth(0.6, 1.0, np.hypot((X - px) / (rx * 0.11), (Y - py) / (ry * 0.11))) * (1 - gone), C(130, 34, 26))
    for k in range(7):
        a, rr = rng.random() * 2 * math.pi, math.sqrt(rng.random()) * 0.7
        px, py = cx + math.cos(a) * rr * rx, cy + math.sin(a) * rr * ry
        aa = math.degrees(a) % 360
        if missing and (aa - missing[0] + 8) % 360 < (missing[1] - missing[0] + 16) % 360:
            continue
        dd = np.hypot((X - px) / (rx * 0.05), (Y - py) / (ry * 0.05))
        p.lay(smooth(1.0, 0.8, dd) * smooth(0.3, 0.5, dd) * (1 - gone), C(24, 22, 22))


def finish_painting(p, rng, protect):
    """Brushwork over the whole picture, canvas weave, a little varnish sheen"""
    p.col, rel1 = paint_over(p.col, rng, 9000, 16, 4.5, protect=protect, opacity=0.6)
    p.col, rel2 = paint_over(p.col, rng, 14000, 7, 2.2, protect=protect, opacity=0.55)
    W, H = p.W, p.H
    weave = canvas_weave(W, H, 3.0)
    p.col = p.col * (1 + 0.045 * (weave - 0.5))[..., None]
    p.h = p.h + 0.6 * blur(np.maximum(rel1, rel2), 0.7) + 0.45 * weave
    p.gl = np.full((H, W), 0.28) + 0.08 * rel2


@surface("cat_pizza", tile=(False, False))
def cat_pizza(W, H, rng):
    """A painting: a fluffy black cat at a table, wide-eyed, a slice of pepperoni pizza in its paw and the rest of
    the pizza in its box in front, flowers in a dark vase, a muted green wall (original, in the spirit of the
    canvas in the restaurant)"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    p = Pic(W, H, green_wall(W, H, N))
    table_y = 338
    # the vase of flowers on the right
    vase = Mask(W, H).ellipse(448, 300, 27, 40).rect(436, 236, 460, 280).ellipse(448, 236, 14, 5).get()
    t = np.clip((X - 421) / 54, 0, 1)
    p.lay(vase, C(36, 46, 44) * cyl_shade(t)[..., None])
    stems = Mask(W, H, ss=3)
    blooms = []
    for k in range(12):
        bx, by = 446 + rng.uniform(-62, 54), rng.uniform(60, 215)
        stems.line([(448 + rng.uniform(-6, 6), 240), ((448 + bx) / 2 + rng.uniform(-10, 10), (240 + by) / 2), (bx, by)], 1.6)
        blooms.append((bx, by, k % 3 == 0))
    p.lay(stems.get(), C(84, 112, 70))
    for bx, by, orange in blooms:
        if orange:
            for j in range(8):
                a = j * 2 * math.pi / 8 + rng.random() * 0.3
                pm = Mask(W, H).poly(leaf(bx, by, a, 21, 13)).get()
                p.lay(pm, C(238, 154, 66) * rng.uniform(0.85, 1.08))
            p.lay(Mask(W, H).ellipse(bx, by, 6).get(), C(120, 70, 30))
        else:
            for j in range(14):
                dx, dy = rng.normal(0, 9, 2)
                p.lay(Mask(W, H).ellipse(bx + dx, by + dy, rng.uniform(2.4, 4.2)).get(), C(246, 242, 228) * rng.uniform(0.9, 1.0))
    # the cat, behind the table
    head, rh = (205, 186), 76
    ears = [[(140, 158), (146, 84), (196, 128)], [(214, 124), (262, 80), (272, 150)]]
    mask = cat_shape(W, H, head, rh, (210, 330, 128, 120), ears)
    mask = mask * smooth(table_y + 1, table_y - 1, Y)
    p.col, rel, _ = cat_fur(p.col, mask, rng)
    for e in ears:
        cx_ = sum(q[0] for q in e) / 3
        cy_ = sum(q[1] for q in e) / 3
        inner = [(cx_ + (q[0] - cx_) * 0.55, cy_ + (q[1] - cy_) * 0.55 + 4) for q in e]
        p.lay(Mask(W, H).poly(inner).get() * 0.6, C(70, 50, 56))
    p.h = p.h + 0.5 * rel
    # the table: a cream cloth
    cloth = smooth(table_y - 0.8, table_y + 0.8, Y)
    folds = 1 + 0.05 * np.sin(X / 23.0 + 2 * N.blob(30)) * smooth(table_y + 90, H, Y)
    p.lay(cloth, mix(C(232, 222, 190), C(214, 202, 168), (Y - table_y) / (H - table_y))[..., :] * folds[..., None])
    shadow = np.exp(-(((X - 212) / 150) ** 2 + ((Y - table_y - 6) / 14) ** 2)) * cloth
    p.col *= (1 - 0.3 * shadow)[..., None]
    # the pizza box in front, a slice gone
    box = Mask(W, H).poly([(262, 360), (486, 360), (504, 480), (240, 480)]).get()
    p.lay(box, C(200, 162, 112))
    inner = Mask(W, H).poly([(272, 368), (476, 368), (492, 472), (252, 472)]).get()
    p.lay(inner, C(214, 180, 128))
    p.lay(Mask(W, H).poly([(272, 368), (476, 368), (474, 376), (274, 376)]).get(), C(160, 122, 80))
    pizza_pie(p, X, Y, 372, 420, 98, 44, rng, missing=(330, 15))
    # the slice in the cat's paw, tip towards its mouth
    sl = Mask(W, H).poly([(232, 222), (322, 192), (336, 258)]).get()
    p.lay(sl, mix(C(246, 196, 96), C(232, 160, 70), smooth(-1, 1.5, N.fbm(4, 2))))
    p.lay(Mask(W, H, ss=4).line([(322, 192), (329, 225), (336, 258)], 11).get(), C(206, 138, 66))
    for (x, y) in ((296, 214), (306, 240), (270, 226), (318, 216)):
        p.lay(Mask(W, H).ellipse(x, y, 8, 7).get(), C(176, 50, 34))
    paw = Mask(W, H).ellipse(322, 270, 24, 19).ellipse(306, 292, 24, 26).get()
    p.col, rel2, _ = cat_fur(p.col, paw, rng, count_edge=500, count_in=400)
    # the face
    prot = np.zeros((H, W))
    for ex in (178, 236):
        cat_eye(p, X, Y, ex, 182, 17, look=(0.05, 0.05))
        prot = np.maximum(prot, smooth(26, 18, np.hypot(X - ex, Y - 182)))
    p.lay(Mask(W, H).poly([(200, 206), (214, 206), (207, 214)]).get(), C(70, 58, 62))
    whiskers(p, W, H, 194, 214, -1, rng)
    whiskers(p, W, H, 222, 214, 1, rng)
    finish_painting(p, rng, prot)
    return p.out()


@surface("cat_wine", tile=(False, False))
def cat_wine(W, H, rng):
    """A painting: a fluffy black cat at a table looking unimpressed, a paw round the stem of a wine glass that a
    bottle is filling with red wine, its other paw on the table; a muted green wall above a cream table"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    p = Pic(W, H, green_wall(W, H, N))
    table_y = 330
    head, rh = (322, 196), 78
    ears = [[(256, 168), (258, 92), (306, 136)], [(330, 130), (384, 88), (392, 162)]]
    mask = cat_shape(W, H, head, rh, (334, 336, 136, 126), ears)
    mask = mask * smooth(table_y + 1, table_y - 1, Y)
    p.col, rel, _ = cat_fur(p.col, mask, rng)
    p.h = p.h + 0.5 * rel
    cloth = smooth(table_y - 0.8, table_y + 0.8, Y)
    p.lay(cloth, mix(C(234, 222, 176), C(214, 198, 150), (Y - table_y) / (H - table_y)))
    shadow = np.exp(-(((X - 330) / 160) ** 2 + ((Y - table_y - 6) / 14) ** 2)) * cloth
    p.col *= (1 - 0.3 * shadow)[..., None]
    # the glass on the table at the left
    gx = 176
    foot = Mask(W, H).ellipse(gx, 452, 38, 9).get()
    stem = Mask(W, H).rect(gx - 3.2, 330, gx + 3.2, 452).get()
    # a round red-wine bowl, widest a little below the middle, narrowing to the rim and down into the stem
    y_top, y_bot = 196, 334
    side = []
    for t in np.linspace(0, 1, 40):
        hw = 47 * math.sin(math.pi * (0.18 + 0.82 * t)) ** 0.6 * max(0.0, 1 - t ** 6) ** 0.5
        side.append((gx - max(hw, 3.2), y_top + t * (y_bot - y_top)))
    bowl_pts = side + [(2 * gx - x, y) for x, y in side[::-1]]
    bowl = Mask(W, H).poly(bowl_pts).get()
    rim_y = y_top
    wine_top = 272
    wine = bowl * smooth(wine_top - 0.8, wine_top + 0.8, Y)
    glass_tint = mix(p.col, C(214, 222, 220), 0.18)
    p.lay(bowl, glass_tint)
    p.lay(wine, mix(C(118, 16, 30), C(70, 8, 18), np.clip((Y - wine_top) / 60, 0, 1)))
    p.lay(smooth(2.0, 0.0, np.abs(Y - wine_top)) * bowl, C(170, 40, 50))
    p.lay(stem, C(206, 214, 212) * 0.9)
    p.lay(foot, C(214, 220, 218))
    edge = np.clip(blur(bowl, 1.0) * (1 - blur(bowl, 1.0)) * 4, 0, 1)
    p.lay(edge * 0.8, C(236, 240, 238))
    hl = Mask(W, H, ss=4).line([(gx - 30, rim_y + 14), (gx - 38, rim_y + 48), (gx - 33, 300)], 3).get()
    p.lay(hl * 0.8, C(250, 250, 248))
    # the bottle pouring from the upper left
    bm = Mask(W, H)
    a = math.radians(32)
    bx0, by0 = 168, 150
    dx, dy = -math.cos(a), -math.sin(a)
    nx_, ny_ = -dy, dx
    neck = [(bx0 + nx_ * 9, by0 + ny_ * 9), (bx0 - nx_ * 9, by0 - ny_ * 9)]
    sh = (bx0 + dx * 70, by0 + dy * 70)
    body_end = (bx0 + dx * 360, by0 + dy * 360)
    bm.poly([neck[0], neck[1], (sh[0] - nx_ * 9, sh[1] - ny_ * 9), (sh[0] + dx * 40 - nx_ * 32, sh[1] + dy * 40 - ny_ * 32),
             (body_end[0] - nx_ * 32, body_end[1] - ny_ * 32), (body_end[0] + nx_ * 32, body_end[1] + ny_ * 32),
             (sh[0] + dx * 40 + nx_ * 32, sh[1] + dy * 40 + ny_ * 32), (sh[0] + nx_ * 9, sh[1] + ny_ * 9)])
    bott = bm.get()
    across = ((X - bx0) * nx_ + (Y - by0) * ny_) / 32.0
    p.lay(bott, C(36, 52, 36) * cyl_shade(np.clip(0.5 - across * 0.5, 0, 1))[..., None])
    stream = Mask(W, H, ss=4).line([(bx0 + 2, by0 + 4), (gx + 2, 196), (gx + 4, wine_top + 2)], 4).get()
    p.lay(stream, C(128, 18, 32))
    # the cat's paws: one round the stem, one flat on the table
    arm = Mask(W, H).line([(262, 300), (220, 352), (196, 372)], 36).get()
    paw2 = Mask(W, H).ellipse(300, 392, 30, 18).ellipse(296, 360, 26, 30).get()
    p.col, _, _ = cat_fur(p.col, np.maximum(arm, paw2), rng, count_edge=900, count_in=700)
    prot = np.zeros((H, W))
    for ex in (294, 352):
        cat_eye(p, X, Y, ex, 194, 17, sleepy=True, look=(-0.25, 0.1))
        prot = np.maximum(prot, smooth(26, 18, np.hypot(X - ex, Y - 194)))
    p.lay(Mask(W, H).poly([(316, 218), (330, 218), (323, 226)]).get(), C(70, 58, 62))
    whiskers(p, W, H, 308, 226, -1, rng)
    whiskers(p, W, H, 338, 226, 1, rng)
    prot = np.maximum(prot, blur(bowl, 2) * 0.6)
    finish_painting(p, rng, prot)
    return p.out()


# --- kitchen equipment

def knob(p, X, Y, cx, cy, r, colour, angle, gloss=0.6):
    """A round control knob with a pointer, shaded as if lit from the upper left"""
    hd, m = dome(X, Y, cx, cy, r)
    p.lay(m, colour * sphere_shade(X, Y, cx, cy, r * 1.3)[..., None], gloss)
    p.h = p.h + 5 * hd * m
    W, H = p.W, p.H
    ptr = Mask(W, H).line([(cx, cy), (cx + math.cos(angle) * r * 0.8, cy + math.sin(angle) * r * 0.8)], r * 0.22).get()
    p.lay(ptr, C(30, 30, 30) if colour.mean() > 0.4 else C(220, 220, 220))


def ticks(p, cx, cy, r0, r1, a0, a1, n, colour, width=1.2):
    m = Mask(p.W, p.H)
    for a in np.linspace(a0, a1, n):
        m.line([(cx + math.cos(a) * r0, cy + math.sin(a) * r0), (cx + math.cos(a) * r1, cy + math.sin(a) * r1)], width)
    p.lay(m.get(), colour)


def gauge(p, X, Y, cx, cy, r, needle):
    """A round thermometer: chrome bezel, white face, ticks, a needle"""
    d = np.hypot(X - cx, Y - cy)
    bezel = smooth(r + 0.7, r - 0.7, d)
    p.lay(bezel, C(196, 198, 202) * sphere_shade(X, Y, cx, cy, r)[..., None], 0.8)
    p.h = p.h + bezel * 3
    face = smooth(r * 0.82 + 0.7, r * 0.82 - 0.7, d)
    p.lay(face, C(240, 238, 230), 0.85)
    ticks(p, cx, cy, r * 0.62, r * 0.76, math.radians(135), math.radians(405), 21, C(30, 30, 30), 1.0)
    nm = Mask(p.W, p.H).line([(cx, cy), (cx + math.cos(needle) * r * 0.7, cy + math.sin(needle) * r * 0.7)], 1.6).get()
    p.lay(nm, C(200, 30, 30))
    p.lay(Mask(p.W, p.H).ellipse(cx, cy, r * 0.1).get(), C(30, 30, 30))
    glare = smooth(r * 0.5, 0, np.hypot(X - (cx - r * 0.3), Y - (cy - r * 0.35))) * face
    p.col = mix(p.col, C(255, 255, 255), 0.35 * glare)


def rocker(p, X, Y, cx, cy, w, h, lit):
    frame = Mask(p.W, p.H).rect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2).get()
    p.lay(frame, C(24, 24, 26), 0.5)
    inner = Mask(p.W, p.H).rect(cx - w / 2 + 2, cy - h / 2 + 2, cx + w / 2 - 2, cy + h / 2 - 2).get()
    t = np.clip((Y - (cy - h / 2)) / h, 0, 1)
    col = mix(C(255, 90, 70), C(170, 20, 20), t) if lit else mix(C(60, 60, 64), C(20, 20, 22), t)
    p.lay(inner, col, 0.7)
    p.h = p.h + 2.5 * inner * (1 - t)


def oven_window(p, X, Y, x0, y0, x1, y1, N, rng):
    """A glowing oven window: the hot chamber behind dirty glass, the elements bright at the top, a stone below"""
    inside = Mask(p.W, p.H).rect(x0, y0, x1, y1).get()
    u, v = (X - x0) / (x1 - x0), (Y - y0) / (y1 - y0)
    glow = np.exp(-((u - 0.5) ** 2 / 0.25 + (v - 0.45) ** 2 / 0.35))
    col = mix(C(80, 24, 8), C(255, 148, 52), glow)
    elem = np.exp(-((v - 0.12) / 0.04) ** 2) * (0.6 + 0.4 * np.sin(u * 40) ** 2)
    col = mix(col, C(255, 214, 120), 0.8 * elem)
    stone = smooth(0.74, 0.78, v)
    col = mix(col, C(70, 34, 18) * (1 + 0.2 * N.fbm(6, 2))[..., None], stone)
    pz = smooth(1.0, 0.9, np.hypot((u - 0.42) / 0.26, (v - 0.74) / 0.07)) * (v < 0.76)
    col = mix(col, C(232, 168, 80), pz)
    dirt = smooth(-1.0, 2.0, N.fbm(30, 3)) * (0.6 + 0.4 * v)
    col = mix(col, C(90, 50, 24), 0.3 * dirt)
    streak = smooth(0.6, 1.0, np.sin(2 * np.pi * (u * 0.8 + v * 0.5) * 1.2) ** 6) * 0.25
    col = mix(col, C(255, 230, 200), streak)
    p.lay(inside, col, 0.88)
    p.h = p.h - 1.5 * inside
    d = np.minimum(np.minimum(X - x0, x1 - X), np.minimum(Y - y0, y1 - Y))
    frame = smooth(-5, -4, d) * smooth(0.5, -0.5, d)
    p.lay(frame, C(40, 40, 42), 0.5)


@surface("oven_deck", tile=(False, False))
def oven_deck(W, H, rng):
    """One deck of the stacked pizza ovens: a brushed stainless door with a glowing window and two handles with
    black ball grips, heat-stained, and the red control panel on its right (timer, thermometer, temperature
    dial, rocker switches). No maker's name; 2 x 1 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    steel, hs, smudge = brushed_steel(N, W, H, C(170, 170, 168), 0.5)
    heat = smooth(-0.5, 2.0, N.fbm(120, 3, sy=40)) * 0.3 + smooth(0.3 * H, 0, Y) * 0.45
    steel = mix(steel, C(150, 120, 80) * steel.mean(-1, keepdims=True) * 1.4, heat * 0.5)
    p = Pic(W, H, steel, 0.65)
    p.h = hs
    dx0, dy0, dx1, dy1 = 0.035 * W, 0.12 * H, 0.79 * W, 0.9 * H
    door = Mask(W, H).rect(dx0, dy0, dx1, dy1).get()
    dsteel, dh, _ = brushed_steel(N, W, H, C(182, 182, 180), 0.5)
    grime = smooth(0.0, 2.0, N.blob(2.0, 40, angle=math.pi / 2) + N.fbm(60, 2)) * smooth(0.3 * H, 0.15 * H, Y) * 0.6
    grime = grime + 0.35 * np.exp(-((Y - 0.4 * H) / 14.0) ** 2) * smooth(0.0, 1.5, N.fbm(20, 2))
    dsteel = mix(dsteel, C(120, 88, 52), np.clip(grime, 0, 1) * 0.45)
    p.lay(door, dsteel, 0.7)
    d = np.minimum(np.minimum(X - dx0, dx1 - X), np.minimum(Y - dy0, dy1 - Y))
    p.h = p.h + door * (3.0 * smooth(0, 6, d) + dh)
    p.col *= (1 - 0.3 * smooth(4, 0, np.abs(d)))[..., None]
    oven_window(p, X, Y, 0.13 * W, 0.4 * H, 0.7 * W, 0.76 * H, N, rng)
    # the hot-surface sign embossed on the door: a triangle with three wavy lines
    tri = Mask(W, H).poly([(0.415 * W, 0.17 * H), (0.45 * W, 0.3 * H), (0.38 * W, 0.3 * H)]).get()
    tri_in = Mask(W, H).poly([(0.415 * W, 0.2 * H), (0.44 * W, 0.285 * H), (0.39 * W, 0.285 * H)]).get()
    p.h = p.h + 1.2 * (tri - tri_in)
    p.col *= (1 - 0.25 * (tri - tri_in))[..., None]
    # handles: angled rods from the door's top corners, black balls on their ends
    for sx, x0 in ((1, 0.03 * W), (-1, 0.79 * W)):
        x1 = x0 + sx * 0.06 * W
        rod = Mask(W, H).line([(x0 + sx * 0.035 * W, 0.42 * H), (x0 + sx * 0.005 * W, 0.2 * H)], 7).get()
        rs = 0.75 + 0.45 * smooth(-2, 2, (X - (x0 + sx * 0.02 * W)) * -sx)
        p.lay(rod, C(196, 196, 196) * rs[..., None], 0.8)
        p.h = p.h + 4 * rod
        bx, by = x0 + sx * 0.005 * W, 0.17 * H
        hd, m = dome(X, Y, bx, by, 17)
        p.lay(m, C(30, 30, 32) * sphere_shade(X, Y, bx, by, 17)[..., None], 0.8)
        p.h = p.h + 8 * hd * m
    # the red control panel
    px0, px1 = 0.82 * W, 0.975 * W
    panel = Mask(W, H).rect(px0, 0.05 * H, px1, 0.95 * H).get()
    red = C(196, 34, 48) * (1 + 0.05 * N.fbm(30, 2) + 0.03 * N.blob(0.8))[..., None]
    p.lay(panel, red, 0.45)
    p.h = p.h * (1 - panel) + panel * 1.5
    cx = (px0 + px1) / 2
    knob(p, X, Y, cx, 0.17 * H, 22, C(214, 214, 212), math.radians(-60))
    ticks(p, cx, 0.17 * H, 27, 32, math.radians(140), math.radians(400), 9, C(240, 230, 230))
    gauge(p, X, Y, cx, 0.4 * H, 34, math.radians(20))
    knob(p, X, Y, cx, 0.64 * H, 20, C(214, 214, 212), math.radians(200))
    ticks(p, cx, 0.64 * H, 25, 30, math.radians(140), math.radians(400), 9, C(240, 230, 230))
    rocker(p, X, Y, cx - 18, 0.84 * H, 18, 28, True)
    rocker(p, X, Y, cx + 18, 0.84 * H, 18, 28, False)
    return p.out()


@surface("oven_counter", tile=(False, False))
def oven_counter(W, H, rng):
    """A countertop pizza oven: stainless case, black door with a glowing window and a black bar handle, the
    control panel on its right, red above and black below (three knobs, a green power light); 2 x 1 cells"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    steel, hs, _ = brushed_steel(N, W, H, C(168, 168, 166), 0.5)
    p = Pic(W, H, steel, 0.65)
    p.h = hs
    dx0, dy0, dx1, dy1 = 0.03 * W, 0.1 * H, 0.75 * W, 0.92 * H
    door = Mask(W, H).rect(dx0, dy0, dx1, dy1).get()
    black = C(34, 34, 36) * (1 + 0.06 * N.fbm(40, 3) + 0.05 * N.blob(0.8))[..., None]
    wear = smooth(1.6, 2.8, N.fbm(5, 3, sy=2))
    black = mix(black, C(80, 78, 74), 0.25 * wear)
    p.lay(door, black, 0.45)
    d = np.minimum(np.minimum(X - dx0, dx1 - X), np.minimum(Y - dy0, dy1 - Y))
    p.h = p.h * (1 - door) + door * (3.0 * smooth(0, 5, d) + 0.15 * N.fbm(20, 2))
    oven_window(p, X, Y, 0.13 * W, 0.4 * H, 0.65 * W, 0.78 * H, N, rng)
    hx0, hx1, hy = 0.2 * W, 0.58 * W, 0.22 * H
    bar = Mask(W, H).rect(hx0, hy - 9, hx1, hy + 9).get()
    t = np.clip((Y - (hy - 9)) / 18, 0, 1)
    p.lay(bar, C(26, 26, 28) * cyl_shade(t)[..., None] * 1.3, 0.7)
    p.h = p.h + 6 * bar * np.sqrt(np.clip(1 - (2 * t - 1) ** 2, 0, 1))
    for xx in (hx0 + 12, hx1 - 12):
        p.lay(Mask(W, H).rect(xx - 7, hy + 6, xx + 7, hy + 22).get(), C(20, 20, 22))
    px0, px1 = 0.78 * W, 0.97 * W
    panel = Mask(W, H).rect(px0, 0.06 * H, px1, 0.94 * H).get()
    split = smooth(-1, 1, (0.55 * H - Y) + (X - px0) * 0.6)
    pc = mix(C(26, 26, 28), C(186, 30, 42), split) * (1 + 0.04 * N.fbm(20, 2))[..., None]
    p.lay(panel, pc, 0.45)
    p.h = p.h * (1 - panel) + panel * 1.5
    cx = (px0 + px1) / 2
    for k, (yy, ang) in enumerate(((0.2, -40), (0.44, 100), (0.68, 210))):
        knob(p, X, Y, cx, yy * H, 19, C(200, 200, 204), math.radians(ang))
        ticks(p, cx, yy * H, 24, 29, math.radians(140), math.radians(400), 7, C(236, 236, 236))
    led = smooth(5, 2, np.hypot(X - (cx - 22), Y - 0.87 * H))
    p.lay(led, C(80, 255, 120), 0.9)
    p.col = mix(p.col, C(80, 255, 120), 0.3 * smooth(14, 2, np.hypot(X - (cx - 22), Y - 0.87 * H)) * panel)
    rocker(p, X, Y, cx + 14, 0.87 * H, 16, 22, False)
    return p.out()


# --- shelves, signs and decorations

def bottle(p, X, Y, cx, base, h, w, glass, label, capsule, rng, N):
    """A wine bottle standing at (cx, base): body, shoulders, neck, foil capsule, a label"""
    W, H = p.W, p.H
    bw, nw = w / 2, w * 0.17
    sh_y = base - h * 0.62
    top = base - h
    pts = [(cx - bw, base), (cx - bw, sh_y), (cx - nw, sh_y - h * 0.16), (cx - nw, top), (cx + nw, top),
           (cx + nw, sh_y - h * 0.16), (cx + bw, sh_y), (cx + bw, base)]
    m = Mask(W, H).poly(pts).get()
    t = np.clip((X - (cx - bw)) / (2 * bw), 0, 1)
    p.lay(m, glass * cyl_shade(t)[..., None], 0.85)
    p.h = p.h + 4 * m * np.sqrt(np.clip(1 - (2 * t - 1) ** 2, 0, 1))
    cap = Mask(W, H).rect(cx - nw - 0.5, top, cx + nw + 0.5, top + h * 0.13).get()
    tn = np.clip((X - (cx - nw)) / (2 * nw), 0, 1)
    p.lay(cap, capsule * cyl_shade(tn)[..., None], 0.7)
    if label is not None:
        ly0, ly1 = base - h * 0.5, base - h * 0.12
        lm = Mask(W, H).rect(cx - bw + 1.5, ly0, cx + bw - 1.5, ly1).get()
        lc, ink = label
        p.lay(lm, lc * (0.6 + 0.45 * cyl_shade(t) * 0.8)[..., None], 0.3)
        mm = Mask(W, H)
        mm.ellipse(cx, ly0 + (ly1 - ly0) * 0.32, bw * 0.32)
        for k, yy in enumerate((0.62, 0.74, 0.84)):
            ww = bw * (0.7 if k == 0 else 0.5)
            mm.rect(cx - ww, ly0 + (ly1 - ly0) * yy, cx + ww, ly0 + (ly1 - ly0) * yy + 2.2)
        p.lay(mm.get() * lm, ink)


@surface("wine_shelf", tile=(False, False))
def wine_shelf(W, H, rng):
    """Two dark wooden shelves of wine bottles against the burgundy wall: greens, browns, a pale white, foil
    capsules and labels without words"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    wall = C(116, 34, 42) * (1 + 0.05 * N.fbm(60, 3))[..., None]
    p = Pic(W, H, wall, 0.17)
    glasses = [C(34, 56, 36), C(58, 38, 22), C(40, 18, 22), C(26, 40, 30), C(190, 186, 120), C(64, 70, 40)]
    labels = [(C(238, 230, 210), C(120, 20, 30)), (C(24, 24, 26), C(212, 180, 100)), (C(230, 224, 214), C(30, 30, 30)),
              (C(176, 40, 44), C(240, 230, 210)), (C(214, 200, 160), C(60, 40, 30))]
    caps = [C(150, 20, 30), C(200, 170, 90), C(26, 26, 28), C(110, 20, 40), C(220, 216, 210)]
    for shelf_y in (240, 494):
        x = 22
        while x < W - 40:
            w = rng.uniform(46, 54)
            h = rng.uniform(176, 204)
            cx = x + w / 2
            sh = np.exp(-(((X - cx - 12) / (w * 0.6)) ** 2)) * (Y < shelf_y) * (Y > shelf_y - h)
            p.col *= (1 - 0.35 * sh)[..., None]
            bottle(p, X, Y, cx, shelf_y - 2, h, w, glasses[rng.integers(len(glasses))],
                   labels[rng.integers(len(labels))], caps[rng.integers(len(caps))], rng, N)
            x += w + rng.uniform(4, 12)
        board = Mask(W, H).rect(0, shelf_y - 2, W, shelf_y + 16).get()
        grain = ring_profile(Y / 3.5 + 2.0 * N.fbm(160, 3, sy=10))
        p.lay(board, C(70, 40, 28) * (1.1 - 0.35 * grain)[..., None] * (1 + 0.25 * smooth(shelf_y + 2, shelf_y - 2, Y))[..., None], 0.35)
        p.h = p.h + 5 * board
        under = smooth(shelf_y + 34, shelf_y + 16, Y) * (Y > shelf_y + 16)
        p.col *= (1 - 0.4 * under)[..., None]
    return p.out()


@surface("bookshelf_items", tile=(False, False))
def bookshelf_items(W, H, rng):
    """The inside of the dark wood bookcase by the door: rows of books, a trailing pothos, a poinsettia in a pot,
    a red can, folded shirts, three shelves"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    back = C(52, 32, 24) * (1 + 0.08 * N.fbm(40, 3) + 0.05 * door_grain(N, X, Y, True))[..., None]
    p = Pic(W, H, back, 0.2)
    shelves = [168, 336, 500]
    book_cols = [C(120, 30, 36), C(36, 52, 92), C(44, 76, 52), C(196, 160, 90), C(222, 212, 190), C(30, 30, 32),
                 C(150, 80, 40), C(90, 60, 96), C(60, 100, 110)]

    def books(x0, x1, base, hmax):
        x = x0
        while x < x1:
            w = rng.uniform(10, 24)
            h = rng.uniform(0.72, 1.0) * hmax
            col = book_cols[rng.integers(len(book_cols))] * rng.uniform(0.85, 1.1)
            t = np.clip((X - x) / w, 0, 1)
            m = Mask(W, H).rect(x, base - h, x + w - 1, base).get()
            p.lay(m, col * (0.75 + 0.3 * np.sin(np.pi * t))[..., None], 0.3)
            p.h = p.h + 3 * m * np.sin(np.pi * t)
            bands = Mask(W, H)
            for yy in (0.12, 0.16, 0.84, 0.88):
                bands.rect(x + 1, base - h * yy, x + w - 2, base - h * yy + 1.6)
            bands.rect(x + w * 0.3, base - h * 0.65, x + w * 0.7, base - h * 0.32)
            p.lay(bands.get() * m * 0.8, C(214, 186, 110) if col.mean() < 0.5 else C(60, 40, 30))
            x += w + rng.uniform(0, 2)

    books(14, 330, shelves[0] - 2, 132)
    # a pothos at the top right, trailing over the shelf edge
    p.lay(Mask(W, H).rect(372, 116, 452, 166).get(), C(196, 186, 170) * (1 + 0.05 * N.fbm(10, 2))[..., None], 0.4)
    vines = Mask(W, H, ss=3)
    leaves = []
    for k in range(6):
        x, y = rng.uniform(380, 444), 118
        pts = [(x, y)]
        for j in range(10):
            x += rng.normal(0, 6) + (k - 2.5) * 1.5
            y += rng.uniform(6, 14) if j > 2 else -rng.uniform(4, 10)
            pts.append((x, y))
            leaves.append((x, y))
        vines.line(pts, 1.4)
    p.lay(vines.get(), C(60, 90, 40))
    for (x, y) in leaves:
        a = rng.uniform(0, 6.28)
        lm = Mask(W, H).poly(leaf(x, y, a, rng.uniform(13, 19), rng.uniform(10, 13))).get()
        p.lay(lm, C(70, 130, 56) * rng.uniform(0.75, 1.15), 0.4)
    # middle shelf: the poinsettia, books lying flat, a red can
    pot = Mask(W, H).poly([(40, 330), (130, 330), (122, 270), (48, 270)]).get()
    p.lay(pot, C(214, 206, 196) * cyl_shade(np.clip((X - 40) / 90, 0, 1))[..., None], 0.5)
    for k in range(26):
        a = rng.uniform(0, 2 * math.pi)
        L = rng.uniform(26, 50)
        cx, cy = 86 + rng.normal(0, 8), 240 + rng.normal(0, 6)
        col = C(196, 26, 34) if k > 7 else C(40, 90, 44)
        lm = Mask(W, H).poly(leaf(cx, cy, a, L, L * 0.42)).get()
        shade = 0.75 + 0.35 * rng.random()
        p.lay(lm, col * shade, 0.35)
        p.h = p.h + 2 * lm
    for k in range(8):
        p.lay(Mask(W, H).ellipse(86 + rng.normal(0, 7), 240 + rng.normal(0, 6), 3.2).get(), C(226, 210, 70))
    for k, (hh, col) in enumerate(((22, C(120, 30, 36)), (18, C(36, 52, 92)), (26, C(222, 212, 190)), (20, C(44, 76, 52)))):
        y1 = shelves[1] - 2 - sum([22, 18, 26, 20][:k])
        m = Mask(W, H).rect(170 + k * 4, y1 - hh, 300 - k * 3, y1).get()
        p.lay(m, col * (0.8 + 0.25 * np.sin(np.pi * np.clip((Y - (y1 - hh)) / hh, 0, 1)))[..., None], 0.3)
        p.h = p.h + 3 * m
    can = Mask(W, H).rect(340, 250, 382, 334).get()
    tc = np.clip((X - 340) / 42, 0, 1)
    p.lay(can, C(196, 30, 40) * cyl_shade(tc)[..., None], 0.7)
    p.lay(Mask(W, H).rect(340, 282, 382, 296).get(), C(236, 232, 226) * cyl_shade(tc)[..., None])
    p.lay(Mask(W, H).rect(342, 246, 380, 252).get(), C(190, 190, 194) * cyl_shade(tc)[..., None], 0.8)
    books(400, 492, shelves[1] - 2, 118)
    # bottom shelf: folded shirts and more books
    for k, col in enumerate((C(52, 54, 58), C(220, 212, 196), C(110, 28, 34), C(52, 54, 58))):
        y1 = shelves[2] - 2 - k * 30
        m = Mask(W, H).poly([(26, y1), (220, y1), (216, y1 - 28), (30, y1 - 28)]).get()
        sh = 0.8 + 0.3 * smooth(y1 - 28, y1 - 10, Y) * smooth(y1, y1 - 6, Y)
        p.lay(m, col * sh[..., None] * (1 + 0.05 * N.blob(1.0))[..., None], 0.1)
        p.h = p.h + 3 * m
    books(250, 492, shelves[2] - 2, 140)
    for sy in shelves:
        board = Mask(W, H).rect(0, sy - 2, W, sy + 12).get()
        p.lay(board, C(64, 38, 26) * (1.05 + 0.2 * smooth(sy + 2, sy - 2, Y))[..., None], 0.35)
        p.h = p.h + 5 * board
        under = smooth(sy + 40, sy + 12, Y) * (Y > sy + 12)
        p.col *= (1 - 0.45 * under)[..., None]
    side = np.minimum(X, W - X)
    p.col *= (0.7 + 0.3 * smooth(0, 30, side))[..., None]
    return p.out()


@surface("exit_sign", tile=(False, False))
def exit_sign(W, H, rng):
    """An exit sign: red EXIT lit on a white face, chevrons each side, in a white housing (the face is square
    here: for a 2:1 sign use the middle half of the rect's height)"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    face = mix(C(250, 250, 246), C(232, 232, 228), np.hypot(X - W / 2, Y - H / 2) / W)
    p = Pic(W, H, face, 0.5)
    d = np.minimum(np.minimum(X, W - X), np.minimum(Y, H - Y))
    housing = smooth(18, 16, d)
    p.lay(housing, C(226, 226, 222) * (0.85 + 0.2 * smooth(0, 16, d))[..., None], 0.45)
    p.h = 4 * smooth(0, 16, d) * housing + 4 * (1 - housing)
    m = Mask(W, H)
    size = fit_size("arialbd.ttf", 200, "EXIT", W * 0.62)
    m.text(W / 2, H / 2, "EXIT", "arialbd.ttf", size, anchor="mm")
    for sx in (-1, 1):
        x = W / 2 + sx * W * 0.4
        m.poly([(x - sx * 14, H / 2 - 24), (x + sx * 6, H / 2), (x - sx * 14, H / 2 + 24), (x - sx * 4, H / 2)])
    t = m.get()
    glow = blur(t, 6)
    p.col = mix(p.col, C(255, 120, 110), 0.35 * glow)
    p.lay(t, C(214, 28, 30), 0.5)
    p.h = p.h + 0.5 * t
    return p.out()


@surface("fire_canister", tile=(False, False))
def fire_canister(W, H, rng):
    """The fire suppression canister's red paint and its white label (a blue band, lines of small print, a
    hazard diamond, a barcode); wrap it round the cylinder"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    red = C(196, 32, 38) * (1 + 0.05 * N.fbm(30, 3) + 0.03 * N.blob(0.8))[..., None]
    p = Pic(W, H, red, 0.55)
    p.h = 0.2 * N.blob(0.8)
    lx0, lx1, ly0, ly1 = 0.24 * W, 0.76 * W, 0.18 * H, 0.82 * H
    label = Mask(W, H).rect(lx0, ly0, lx1, ly1).get()
    p.lay(label, C(240, 238, 232) * (1 + 0.02 * N.fbm(20, 2))[..., None], 0.25)
    p.h = p.h + 0.6 * label
    p.lay(Mask(W, H).rect(lx0, ly0, lx1, ly0 + 46).get(), C(30, 64, 140))
    m = Mask(W, H)
    m.text((lx0 + lx1) / 2, ly0 + 23, "FIRE SUPPRESSION", "arialbd.ttf", fit_size("arialbd.ttf", 24, "FIRE SUPPRESSION", lx1 - lx0 - 20), anchor="mm")
    p.lay(m.get(), C(250, 250, 250))
    lines = Mask(W, H)
    y = ly0 + 64
    while y < ly1 - 90:
        w = rng.uniform(0.5, 1.0) * (lx1 - lx0 - 30)
        lines.rect(lx0 + 14, y, lx0 + 14 + w, y + 3.2)
        y += rng.choice([8, 8, 8, 14])
    p.lay(lines.get(), C(60, 60, 64))
    cx, cy = lx1 - 50, ly1 - 50
    dia = Mask(W, H).poly([(cx, cy - 30), (cx + 30, cy), (cx, cy + 30), (cx - 30, cy)]).get()
    p.lay(dia, C(210, 40, 36))
    dia2 = Mask(W, H).poly([(cx, cy - 18), (cx + 18, cy), (cx, cy + 18), (cx - 18, cy)]).get()
    p.lay(dia2, C(250, 250, 250))
    bars = Mask(W, H)
    x = lx0 + 16
    while x < lx0 + 120:
        w = rng.choice([1.2, 1.2, 2.4, 3.2])
        bars.rect(x, ly1 - 70, x + w, ly1 - 26)
        x += w + rng.choice([1.2, 2.4])
    p.lay(bars.get(), C(20, 20, 20))
    scratches = sum(smooth(2.6, 3.2, np.abs(N.blob(40, 0.4, angle=a))) for a in rng.uniform(0, 3.14, 3))
    p.col = mix(p.col, C(150, 140, 140), 0.4 * scratches * (1 - label))
    return p.out()


@surface("clock", tile=(False, False))
def clock(W, H, rng):
    """A round wall clock: black rim, white face, numerals, hands at ten past ten, a red second hand, glass"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    cx, cy = W / 2, H / 2
    d = np.hypot(X - cx, Y - cy)
    R = W * 0.48
    p = Pic(W, H, C(24, 24, 26), 0.6)
    rim = smooth(R + 0.8, R - 0.8, d)
    t = np.clip((R - d) / (R * 0.1), 0, 1)
    p.lay(rim, C(30, 30, 32) * (0.7 + 0.8 * np.sin(np.pi * t))[..., None] * (1 + 0.25 * ((cx - X) + (cy - Y)) / W)[..., None], 0.6)
    p.h = rim * 8 * np.sin(np.pi * np.clip(t, 0, 1))
    fr = R * 0.9
    face = smooth(fr + 0.8, fr - 0.8, d)
    p.lay(face, C(244, 242, 236) * (1 - 0.06 * (d / fr) ** 2)[..., None], 0.85)
    p.h = p.h * (1 - face)
    for k in range(60):
        a = k * math.pi / 30
        big = k % 5 == 0
        r0 = fr * (0.86 if big else 0.91)
        ticks(p, cx, cy, r0, fr * 0.96, a, a, 1, C(26, 26, 26), 3.2 if big else 1.3)
    nm = Mask(W, H)
    for k in range(1, 13):
        a = -math.pi / 2 + k * math.pi / 6
        nm.text(cx + math.cos(a) * fr * 0.72, cy + math.sin(a) * fr * 0.72 + 2, str(k), "georgia.ttf", 40, anchor="mm")
    p.lay(nm.get(), C(26, 26, 26))
    for ang, L, w, col in ((-math.pi / 2 + (10 + 10 / 60) * math.pi / 6, 0.5, 9, C(20, 20, 20)),
                           (-math.pi / 2 + 10 * math.pi / 30, 0.78, 6, C(20, 20, 20)),
                           (-math.pi / 2 + 37 * math.pi / 30, 0.84, 2.2, C(200, 30, 30))):
        hm = Mask(W, H).line([(cx - math.cos(ang) * fr * 0.12, cy - math.sin(ang) * fr * 0.12), (cx + math.cos(ang) * fr * L, cy + math.sin(ang) * fr * L)], w).get()
        p.lay(hm, col)
        p.h = p.h + 1.0 * hm
    p.lay(Mask(W, H).ellipse(cx, cy, 10).get(), C(30, 30, 30))
    glare = smooth(0.4, 1.0, np.sin(np.pi * np.clip((X + Y - W * 0.25) / (W * 0.7), 0, 1)) ** 2) * face * smooth(fr, fr * 0.6, d)
    p.col = mix(p.col, C(255, 255, 255), 0.18 * glare)
    return p.out()


@surface("string_bulbs", tile=(True, False), metres=(1.2, 0.0))
def string_bulbs(W, H, rng):
    """A string of orange bulbs glowing against the dark ceiling: a sagging dark wire, six sockets per 1.2 m,
    teardrop bulbs with their glow; repeats along u"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    p = Pic(W, H, C(24, 22, 24) * (1 + 0.05 * N.fbm(40, 2))[..., None], 0.1)
    n = 6
    sag = 0.3 * H + 18 * (1 - np.cos(2 * np.pi * X * n / W)) / 2
    wire = smooth(2.2, 1.2, np.abs(Y - sag))
    p.lay(wire, C(14, 18, 14), 0.4)
    glow = np.zeros((H, W))
    for k in range(n):
        bx = (k + 0.5) * W / n
        by = 0.3 * H + 18
        dx = wrapd(X - bx, W)
        sock = smooth(1, -1, np.maximum(np.abs(dx) - 7, np.abs(Y - by - 12) - 13))
        p.lay(sock, C(20, 30, 22) * (1 + 0.4 * smooth(7, -7, dx))[..., None], 0.5)
        # the bulb: a teardrop pointing down
        v = (Y - by - 24) / 70.0
        width = 15 * np.sin(np.pi * np.clip(v, 0, 1) ** 0.6) * (1 - 0.6 * np.clip(v, 0, 1))
        bulb = smooth(0.8, -0.8, np.abs(dx) - width) * (v > 0) * (v < 1)
        core = np.exp(-((dx / 7) ** 2 + ((v - 0.35) / 0.25) ** 2))
        col = mix(C(236, 96, 20), C(255, 214, 140), core)
        p.lay(bulb, col, 0.8)
        p.h = p.h + 5 * bulb * np.sqrt(np.clip(1 - (dx / (width + 1e-3)) ** 2, 0, 1))
        glow += np.exp(-((dx / 40) ** 2 + ((Y - by - 50) / 50) ** 2))
    p.col = p.col + (C(255, 120, 30) * 0.35 * np.clip(glow, 0, 1)[..., None]) * (1 - p.col)
    return p.out()


@surface("wreath", tile=(False, False))
def wreath(W, H, rng):
    """A round evergreen wreath with pine cones and red berries and a red velvet bow at the bottom, on a dark
    background"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    p = Pic(W, H, C(26, 30, 28) * (1 + 0.06 * N.fbm(40, 2))[..., None], 0.2)
    cx, cy, r0, r1 = W / 2, H * 0.47, W * 0.2, W * 0.42
    rm = (r0 + r1) / 2
    d = np.hypot(X - cx, Y - cy)
    ring = smooth(r1 - 4, r1 - 26, d) * smooth(r0 + 4, r0 + 26, d)
    p.col = mix(p.col, C(14, 32, 20) * (1 + 0.2 * N.fbm(4, 2))[..., None], ring)
    greens = [C(26, 70, 40), C(40, 96, 52), C(58, 122, 68), C(20, 52, 32), C(70, 120, 60)]
    segs = []
    for k in range(16000):
        a = rng.uniform(0, 2 * math.pi)
        rr = rm + np.clip(rng.normal(0, (r1 - r0) * 0.24), -(r1 - r0) * 0.55, (r1 - r0) * 0.55)
        x, y = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        ang = a + math.pi / 2 + rng.normal(0, 0.6)
        L = rng.uniform(5, 11)
        outer = (rr - rm) / ((r1 - r0) * 0.5)
        lit = 0.65 + 0.45 * max(0.0, -math.cos(a) * 0.5 - math.sin(a) * 0.6) + rng.uniform(-0.2, 0.15) - 0.15 * abs(outer)
        c = greens[rng.integers(len(greens))] * lit
        segs.append((x, y, x + math.cos(ang) * L, y + math.sin(ang) * L, rng.uniform(1.1, 1.9), c, rng.uniform(0.4, 1)))
    p.col, rel = draw_strokes(p.col, segs)
    p.h = 3 * blur(ring, 6) + 1.5 * rel
    for k in range(7):
        a = k * 2 * math.pi / 7 + 0.4
        px, py = cx + math.cos(a) * rm * 1.02, cy + math.sin(a) * rm * 1.02
        hd, m = dome(X, Y, px, py, 15)
        cone = C(110, 70, 40) * (0.75 + 0.35 * np.sin((Y - py) * 0.9 + (X - px) * 0.3) ** 2)[..., None]
        p.lay(m, cone * sphere_shade(X, Y, px, py, 15)[..., None], 0.2)
        p.h = p.h + 4 * hd * m
    for k in range(40):
        a = rng.uniform(0, 2 * math.pi)
        rr = rm + rng.normal(0, 20)
        bx, by = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        hd, m = dome(X, Y, bx, by, 5.5)
        p.lay(m, C(200, 24, 30) * sphere_shade(X, Y, bx, by, 5.5)[..., None], 0.75)
        p.h = p.h + 2.5 * hd * m
    # the bow: two loops, a knot, two tails
    bx, by = cx, cy + rm + 8
    bow = Mask(W, H)
    loops = [leaf(bx, by, math.radians(200), 82, 54), leaf(bx, by, math.radians(-20), 82, 54)]
    tails = [[(bx - 8, by), (bx - 52, by + 92), (bx - 34, by + 96), (bx - 26, by + 84), (bx + 4, by)],
             [(bx + 8, by), (bx + 52, by + 92), (bx + 34, by + 96), (bx + 26, by + 84), (bx - 4, by)]]
    for t in tails:
        bow.poly(t)
    for l in loops:
        bow.poly(l)
    bm = bow.get()
    velvet = C(176, 18, 30) * (0.7 + 0.5 * blur(bm, 8))[..., None] * (1 + 0.05 * N.blob(1.0))[..., None]
    p.lay(bm, velvet, 0.25)
    for l in loops:
        inner = Mask(W, H).poly([(bx + (x - bx) * 0.6, by + (y - by) * 0.45) for x, y in l]).get()
        p.lay(inner, C(110, 10, 20), 0.2)
    knot = Mask(W, H).ellipse(bx, by, 17, 14).get()
    p.lay(knot, C(150, 14, 26) * sphere_shade(X, Y, bx, by, 17)[..., None], 0.3)
    p.h = p.h + 4 * blur(bm, 3) + 3 * knot
    return p.out()


@surface("pizza_top", tile=(False, False))
def pizza_top(W, H, rng):
    """A pepperoni pizza seen from above in its cast iron skillet: blistered crust, browned mozzarella with an oily
    sheen, cupped pepperoni charred at the edges, flecks of oregano"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    cx, cy = W / 2, H / 2
    d = np.hypot(X - cx, Y - cy)
    R = W * 0.44
    iron = C(34, 32, 32) * (1 + 0.15 * N.fbm(6, 3) + 0.1 * N.blob(0.8))[..., None]
    p = Pic(W, H, iron, 0.45)
    p.h = 0.4 * N.fbm(6, 3)
    wall = smooth(R + 6, R + 2, d) * smooth(W * 0.5, W * 0.47, d)
    p.col *= (0.8 + 0.5 * wall * ((cx - X) + (cy - Y) > 0))[..., None]
    F1, F2, _ = worley(W, H, rng, 260, wrap=False)           # big bubbles in the crust and the cheese
    f1, f2, _ = worley(W, H, rng, 1400, wrap=False)          # small ones
    crust_m = smooth(R + 0.8, R - 0.8, d)
    rim = smooth(R * 0.8, R * 0.9, d)
    across = np.clip((d - R * 0.8) / (R * 0.2), 0, 1)
    puff = np.sin(np.pi * across)                            # the rim's rounded top
    bubble = smooth(1.0, 0.0, F1 / 10.0) * smooth(-0.3, 1.0, N.fbm(6, 2))
    crust = mix(C(222, 170, 92), C(168, 102, 44), np.clip(0.3 + 0.7 * across * (1 - 0.5 * puff) + 0.5 * bubble, 0, 1))
    crust = crust * (1 + 0.06 * N.blob(1.0) - 0.05 * smooth(1.5, 0.0, f1))[..., None]
    char = smooth(0.35, 0.0, F1 / 10.0) * smooth(0.8, 1.8, N.fbm(5, 2))
    crust = mix(crust, C(54, 32, 20), 0.75 * char * rim)
    flour = N.specks(0.02, 0.6) * rim
    crust = mix(crust, C(236, 222, 196), 0.5 * flour)
    p.lay(crust_m, crust, 0.18)
    p.h = p.h + crust_m * (3 + 6 * puff * rim + 2.0 * bubble * rim)
    sauce = smooth(R * 0.86, R * 0.84, d)
    p.lay(sauce, C(170, 40, 24) * (1 + 0.1 * N.fbm(4, 2))[..., None], 0.5)
    wob = 5 * N.fbm(10, 2)
    cheese_m = smooth(R * 0.84 + wob, R * 0.81 + wob, d)
    # leopard spots where the cheese bubbled and browned in the oven
    spots = smooth(0.55, 0.0, F1 / 14.0) * smooth(-0.2, 0.8, N.fbm(16, 2))
    brown = np.clip(0.75 * spots + 0.35 * smooth(1.2, 2.2, N.fbm(9, 3)), 0, 1)
    cheese = mix(C(242, 202, 112), C(200, 124, 46), brown) * (1 + 0.05 * N.fbm(3, 2) - 0.05 * smooth(1.5, 0, f1))[..., None]
    p.lay(cheese_m, cheese, 0.55)
    p.h = p.h + cheese_m * (1.5 + 2.0 * spots + 0.6 * smooth(0, 4, f1))
    placed = []
    for k in range(60):
        a, rr = rng.random() * 2 * math.pi, math.sqrt(rng.random()) * R * 0.72
        px, py = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        pr = rng.uniform(0.075, 0.1) * W / 2
        if any(math.hypot(px - qx, py - qy) < pr + qr - 4 for qx, qy, qr in placed):
            continue
        placed.append((px, py, pr))
        if len(placed) >= 17:
            break
    for px, py, pr in placed:                                # orange grease spread round each slice
        dd = np.hypot(X - px, Y - py) / pr
        p.col = mix(p.col, C(226, 128, 40), 0.35 * smooth(1.8, 1.0, dd) * cheese_m)
    for px, py, pr in placed:
        dd = np.hypot(X - px, Y - py) / pr
        m = smooth(1.0, 0.95, dd)
        cupped = np.clip(dd, 0, 1)
        col = mix(C(176, 52, 34), C(120, 30, 20), smooth(0.6, 1.0, dd))
        col = mix(col, C(60, 22, 14), smooth(0.86, 1.0, dd) * smooth(-0.5, 1.0, N.blob(1.5)))
        col = mix(col, C(220, 96, 34), smooth(0.45, 0.0, dd) * 0.6)                     # the pool of grease
        p.lay(m, col * (1 + 0.06 * N.blob(0.8))[..., None], 0.6)
        p.gl = p.gl + m * smooth(0.5, 0.0, dd) * 0.3
        p.h = p.h + m * (2.0 + 3.0 * cupped ** 3)
    flecks = N.specks(0.006, 0.6) * cheese_m
    p.lay(flecks, C(70, 80, 40))
    p.gl = p.gl + 0.15 * cheese_m * smooth(0, 1.5, N.fbm(12, 2))
    return p.out()


@surface("poinsettia", tile=(False, False))
def poinsettia(W, H, rng):
    """A poinsettia seen from above: red bracts with darker veins in rings round the little yellow flowers, green
    leaves beneath"""
    N = Noise(W, H, rng)
    X, Y = coords(W, H)
    p = Pic(W, H, C(30, 56, 34) * (1 + 0.15 * N.fbm(20, 3))[..., None], 0.3)
    cx, cy = W / 2, H / 2
    layers = [(9, 236, 120, C(40, 96, 48)), (8, 205, 104, C(170, 18, 28)), (7, 155, 84, C(196, 26, 34)), (6, 104, 60, C(214, 40, 44))]
    for li, (n, L, wd, col) in enumerate(layers):
        off = rng.uniform(0, 1)
        for k in range(n):
            a = (k + off) * 2 * math.pi / n + rng.normal(0, 0.12)
            ll = L * rng.uniform(0.85, 1.05)
            pts = leaf(cx, cy, a, ll, wd * rng.uniform(0.62, 0.74), bend=rng.normal(0, 0.1))
            m = Mask(W, H).poly(pts).get()
            along = ((X - cx) * math.cos(a) + (Y - cy) * math.sin(a)) / ll
            across = (-(X - cx) * math.sin(a) + (Y - cy) * math.cos(a))
            vein = np.exp(-(across / 1.6) ** 2) * (along > 0.1)
            side = np.exp(-(np.abs(wrapd(np.abs(across) * 0.9 - along * ll * 0.55, 26.0)) / 1.4) ** 2) * (along > 0.15)
            shade = 0.7 + 0.45 * np.clip(along, 0, 1) + 0.15 * smooth(0, 6, np.abs(across))
            c = col * shade[..., None] * (1 - 0.25 * vein - 0.12 * side)[..., None] * (1 + 0.05 * N.blob(1.2))[..., None]
            p.col = p.col * (1 - 0.35 * blur(m, 4) * (1 - m))[..., None]           # its shadow on what is below
            p.lay(m, c, 0.35)
            p.h = p.h * (1 - m) + m * (li * 3 + 2 * np.clip(along, 0, 1) - 0.8 * vein - 0.4 * side)
    for k in range(14):
        bx, by = cx + rng.normal(0, 13), cy + rng.normal(0, 13)
        hd, m = dome(X, Y, bx, by, 6)
        p.lay(m, mix(C(214, 200, 60), C(170, 40, 30), 0.3 * rng.random()) * sphere_shade(X, Y, bx, by, 6)[..., None], 0.5)
        p.h = p.h + 3 * hd * m
    return p.out()


# ---------------------------------------------------------------------------------------------------- the atlas

def rect_of(name):
    c, r, w, h = LAYOUT[name]
    return [(c * CELL + PAD) / SIZE, (r * CELL + PAD) / SIZE, ((c + w) * CELL - PAD) / SIZE, ((r + h) * CELL - PAD) / SIZE]


def build(name):
    """One surface at its rect's size: colour, gloss and height arrays, and its normals"""
    fn, tile, _ = SURFACES[name]
    c, r, w, h = LAYOUT[name]
    W, H = w * CELL - 2 * PAD, h * CELL - 2 * PAD
    rng = np.random.default_rng(zlib.crc32(name.encode()))
    colour, gloss, height = fn(W, H, rng)
    gloss = np.broadcast_to(np.asarray(gloss, float), (H, W))
    colour = np.clip(colour, 0.0, 1.0)
    # normals from the height softened by about a pixel, so sub-pixel grain does not turn to glittering noise
    soft = ndimage.gaussian_filter1d(height, 0.65, axis=1, mode="wrap" if tile[0] else "nearest")
    soft = ndimage.gaussian_filter1d(soft, 0.65, axis=0, mode="wrap" if tile[1] else "nearest")
    nrm = normal_map(soft, tile[0], tile[1])
    return colour, np.clip(gloss, 0.0, 1.0), height, nrm


def pad(a, tile):
    """The 3 px border: wrapped where the surface tiles, its edge repeated where it doesn't"""
    extra = ((0, 0),) * (a.ndim - 2)
    a = np.pad(a, ((0, 0), (PAD, PAD)) + extra, mode="wrap" if tile[0] else "edge")
    return np.pad(a, ((PAD, PAD), (0, 0)) + extra, mode="wrap" if tile[1] else "edge")


def check_layout():
    used = np.zeros((GRID, GRID), int)
    for name, (c, r, w, h) in LAYOUT.items():
        assert 0 <= c and c + w <= GRID and 0 <= r and r + h <= GRID, name
        used[r:r + h, c:c + w] += 1
    assert used.max() == 1, "surfaces overlap in LAYOUT"
    assert set(LAYOUT) == set(SURFACES), set(LAYOUT) ^ set(SURFACES)
    assert LAYOUT["white"] == (0, 0, 1, 1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", default=None)
    parser.add_argument("--only", default=None)
    args = parser.parse_args()
    if args.only:
        out = args.preview or os.path.join("Z:\\", "Temp", "claude", "BigWalkMods", "tex")
        os.makedirs(out, exist_ok=True)
        for name in args.only.split(","):
            colour, gloss, height, nrm = build(name)
            tile = SURFACES[name][1]
            lit = shade_preview(colour, gloss, nrm)
            row = np.concatenate([colour, lit], axis=1)
            if tile[0] and tile[1]:
                row = np.concatenate([row, np.tile(lit, (2, 2, 1))[:colour.shape[0], :] * 1.0], axis=1)
            Image.fromarray(to8(row)).save(os.path.join(out, "only-%s.png" % name))
            print(name, colour.shape, "gloss %.2f..%.2f" % (gloss.min(), gloss.max()), "height %.1f px" % (height.max() - height.min()))
        return
    check_layout()
    atlas = np.zeros((SIZE, SIZE, 4), np.uint8)
    normals = np.zeros((SIZE, SIZE, 3), np.uint8)
    normals[...] = (128, 128, 255)
    sources = {}
    names = sorted(LAYOUT, key=lambda n: (LAYOUT[n][1], LAYOUT[n][0]))
    for name in names:
        colour, gloss, height, nrm = build(name)
        tile = SURFACES[name][1]
        c, r, w, h = LAYOUT[name]
        x0, y0 = c * CELL, SIZE - (r + h) * CELL
        rgba = np.concatenate([to8(colour), to8(gloss)[..., None]], axis=-1)
        n8 = to8(nrm * 0.5 + 0.5)
        if name == "white":
            n8[...] = (128, 128, 255)
        atlas[y0:y0 + h * CELL, x0:x0 + w * CELL] = pad(rgba, tile)
        normals[y0:y0 + h * CELL, x0:x0 + w * CELL] = pad(n8, tile)
        sources[name] = rgba
        print("  %-18s %d x %d cells%s" % (name, w, h, "  tiles" if tile[0] or tile[1] else ""))

    # checks: the white corner, and each rect's centre landing on its own surface's centre (catches a v flip)
    corner = atlas[SIZE - CELL:, :CELL]
    assert (corner[..., :3] == 255).all() and (corner[..., 3] == 40).all(), "cell (0, 0) is not white, gloss 40"
    assert (normals[SIZE - CELL:, :CELL] == (128, 128, 255)).all(), "cell (0, 0)'s normals are not flat"
    for name in names:
        u0, v0, u1, v1 = rect_of(name)
        px = int((u0 + u1) / 2 * SIZE)
        py = int((1 - (v0 + v1) / 2) * SIZE)
        src = sources[name]
        assert (atlas[py, px] == src[src.shape[0] // 2, src.shape[1] // 2]).all(), "rect of %s is misplaced" % name

    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(os.path.join(OUT, "CastIronAtlas.png"), optimize=True)
    Image.fromarray(normals, "RGB").save(os.path.join(OUT, "CastIronNormals.png"), optimize=True)
    meta = {"cell": CELL, "size": SIZE, "names": names, "rects": [[round(v, 6) for v in rect_of(n)] for n in names],
            "tile": [list(SURFACES[n][1]) for n in names], "metres": [list(SURFACES[n][2]) for n in names]}
    with open(os.path.join(OUT, "CastIronAtlas.json"), "w") as f:
        json.dump(meta, f, indent=1)
    print("wrote the pizzeria's atlas, normals and rects: %d surfaces" % len(names))
    if args.preview:
        previews(args.preview, atlas, normals, names)


def previews(out, atlas, normals, names):
    """sheet.png, lit.png, tiles.png and 1:1 crops, to look at"""
    os.makedirs(out, exist_ok=True)
    colour = atlas[..., :3].astype(np.float64) / 255.0
    gloss = atlas[..., 3].astype(np.float64) / 255.0
    nrm = normals.astype(np.float64) / 255.0 * 2.0 - 1.0
    lit = shade_preview(colour, gloss, nrm)
    k = 2000 / SIZE
    sheet = Image.fromarray(atlas[..., :3]).resize((2000, 2000), Image.LANCZOS)
    d = ImageDraw.Draw(sheet)
    f = font("arialbd.ttf", 15)
    for name in names:
        c, r, w, h = LAYOUT[name]
        x, y = c * CELL * k + 4, (SIZE - (r + h) * CELL) * k + 4
        tw = f.getlength(name)
        d.rectangle([x - 2, y - 2, x + tw + 3, y + 18], fill=(0, 0, 0))
        d.text((x, y), name, font=f, fill=(255, 255, 0))
    for i in range(1, GRID):
        d.line([(i * CELL * k, 0), (i * CELL * k, 2000)], fill=(255, 0, 255), width=1)
        d.line([(0, i * CELL * k), (2000, i * CELL * k)], fill=(255, 0, 255), width=1)
    sheet.save(os.path.join(out, "sheet.png"))
    Image.fromarray(to8(lit)).resize((2000, 2000), Image.LANCZOS).save(os.path.join(out, "lit.png"))
    # tileable rects cut from the finished atlas and repeated 2 x 2 (half size), to see seams as the game would
    tiles = [n for n in names if SURFACES[n][1][0] and SURFACES[n][1][1] and n != "white"]
    side = 506
    cols = 6
    sheet2 = Image.new("RGB", (cols * side, ((len(tiles) + cols - 1) // cols) * side), (0, 0, 0))
    for i, name in enumerate(tiles):
        u0, v0, u1, v1 = rect_of(name)
        x0, x1 = int(round(u0 * SIZE)), int(round(u1 * SIZE))
        y0, y1 = int(round((1 - v1) * SIZE)), int(round((1 - v0) * SIZE))
        t = np.tile(to8(lit[y0:y1, x0:x1]), (2, 2, 1))
        sheet2.paste(Image.fromarray(t).resize((side, side), Image.LANCZOS), ((i % cols) * side, (i // cols) * side))
    sheet2.save(os.path.join(out, "tiles.png"))
    for name in names:
        c, r, w, h = LAYOUT[name]
        x0, y0 = c * CELL, SIZE - (r + h) * CELL
        both = np.concatenate([atlas[y0:y0 + h * CELL, x0:x0 + w * CELL, :3], to8(lit[y0:y0 + h * CELL, x0:x0 + w * CELL])], axis=1)
        Image.fromarray(both).save(os.path.join(out, "crop-%s.png" % name))


if __name__ == "__main__":
    main()
