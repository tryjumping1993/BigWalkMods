"""Tree textures for the Winter Isle, generated in code: the firs' snow-laden sprays and bark, and birch bark.

    python tools/treetex.py [--out DIR] [--preview DIR] [--only fronds|firbark|birchbark]

Writes into unity/Assets/Textures/Trees/ (not committed; tools/bwm.ps1 regenerates them when they are missing or
older than this script):
    FirFronds_C.png  4096 px  RGBA: needle colour, alpha the cut-out (needles, twigs, and snow lying across gaps)
    FirFronds_N.png  4096 px  normal map (needles round in section, twigs, snow clumps)
    FirFronds_M.png  4096 px  R snow cover, G occlusion (dark down in the spray), B per-needle shade
    FirBark_C.png    2048 px  tileable bark colour, one repeat about 1.1 m: grey-brown scales and dark furrows
    FirBark_N.png    2048 px  tileable bark normal map
    BirchBark_C.png  2048 px  tileable birch bark colour: chalk white, dark lenticels, peeling bands, black scars
    BirchBark_N.png  2048 px  tileable birch bark normal map
    BirchTwigs_C/N/M 2048 px  a spray of fine birch twigs with a little snow, laid out as the fir sprays are

The fronds atlas holds four sprays in a 2 x 2 grid, each 2048 px, drawn as fir sprays grow: a stem, side twigs
in alternate pairs, second twigs off those, and flat two-ranked needles on every twig, so the spray is a flat
fan seen from above. Snow lies on it in clumps, deepest along the stem and the larger twigs, bridging some gaps
between twigs. In a tile u runs along the spray (0 at the trunk end, 1 at the tip) and v across it (0.5 on the
stem), as the firs' cards are mapped (Kit.AddFir).

A spray is about 2.2 m long on a grown fir, so the atlas gives about 900 px a metre: a needle is some 20 px long
and 2 px wide. The base game's trees carry 2048 px colour and normal atlases for a whole tree; this is finer
where the eye is, on the boughs at arm's length.

Bark is a height field from cells stretched up the trunk (plates and scales) with furrows at their borders,
tiling exactly (the cells are found on a wrapped grid), and colour, occlusion and normals made from it.
"""
import argparse
import os

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))


def smooth(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def periodic_noise(n, rng, shortest, longest, slope=1.0):
    """Tileable noise of wavelengths between shortest and longest (in pixels), zero mean, unit spread."""
    fx = np.fft.fftfreq(n)[None, :]
    fy = np.fft.fftfreq(n)[:, None]
    f = np.sqrt(fx * fx + fy * fy) + 1e-9
    band = smooth(1.0 / longest * 0.7, 1.0 / longest, f) * (1.0 - smooth(1.0 / shortest, 1.0 / shortest * 1.4, f))
    spectrum = (rng.standard_normal((n, n)) + 1j * rng.standard_normal((n, n))) * band / f ** slope
    out = np.real(np.fft.ifft2(spectrum))
    return (out - out.mean()) / (out.std() + 1e-9)


def normal_map(height, strength):
    """Tangent-space normal map (OpenGL convention, +y up the texture) from a height field in pixels."""
    gy, gx = np.gradient(height)
    nx, ny, nz = -gx * strength, gy * strength, np.ones_like(height)      # image rows run down: v up is -row
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / length, ny / length, nz / length], -1) * 0.5 + 0.5


def save(path, array01, mode):
    data = (np.clip(array01, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
    Image.fromarray(data, mode).save(path)
    print("wrote", path, data.shape)


# ---------------------------------------------------------------------------------------------------------------
# Fir sprays
# ---------------------------------------------------------------------------------------------------------------

class Canvas:
    """One spray tile: colour, coverage, height, shade, all as float arrays."""

    def __init__(self, n):
        self.n = n
        self.color = np.zeros((n, n, 3), np.float32)
        self.alpha = np.zeros((n, n), np.float32)
        self.height = np.zeros((n, n), np.float32)
        self.shade = np.ones((n, n), np.float32)

    def capsule(self, a, b, r0, r1, c0, c1, top, shade=1.0):
        """A tapering round rod from a to b (pixels), radii r0..r1, colour c0..c1, its crown at height 'top'."""
        n = self.n
        pad = max(r0, r1) + 2.0
        x0, x1 = int(max(0, min(a[0], b[0]) - pad)), int(min(n - 1, max(a[0], b[0]) + pad)) + 1
        y0, y1 = int(max(0, min(a[1], b[1]) - pad)), int(min(n - 1, max(a[1], b[1]) + pad)) + 1
        if x1 <= x0 or y1 <= y0:
            return
        ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        ab = np.array(b, np.float32) - np.array(a, np.float32)
        length2 = float(ab @ ab) + 1e-6
        t = np.clip(((xs - a[0]) * ab[0] + (ys - a[1]) * ab[1]) / length2, 0.0, 1.0)
        px, py = a[0] + ab[0] * t, a[1] + ab[1] * t
        d = np.sqrt((xs - px) ** 2 + (ys - py) ** 2)
        r = r0 + (r1 - r0) * t
        cover = np.clip(r + 0.5 - d, 0.0, 1.0)
        inside = np.clip(1.0 - (d / np.maximum(r, 0.3)) ** 2, 0.0, 1.0)
        h = top + np.sqrt(inside) * np.maximum(r, 0.6) * 0.9
        region = (slice(y0, y1), slice(x0, x1))
        win = (h > self.height[region]) & (cover > 0.02)
        if not win.any():
            return
        tt = t[..., None]
        col = np.array(c0, np.float32) * (1.0 - tt) + np.array(c1, np.float32) * tt
        old = self.color[region]
        w = (cover * win)[..., None]
        self.color[region] = old * (1.0 - w) + col * w
        self.height[region] = np.where(win, np.maximum(self.height[region], h), self.height[region])
        self.shade[region] = np.where(win, shade, self.shade[region])
        self.alpha[region] = np.maximum(self.alpha[region], cover)


def spray(n, seed, snow_amount):
    """One fir spray, 'n' px square: (rgba, normal, mask)."""
    rng = np.random.default_rng(seed)
    cv = Canvas(n)
    margin = n * 0.012
    cy = n * 0.5
    length = n - margin * 2.0

    def half_width(u):
        # the spray's outline: broad behind the middle, narrowing to a rounded tip; slimmer at the trunk end
        return n * 0.45 * np.sin(np.pi * np.clip(0.04 + u * 0.96, 0.0, 1.0)) ** 0.95 * (1.0 - 0.2 * u)

    twig_brown = (0.17, 0.12, 0.08)
    young_twig = (0.28, 0.22, 0.13)

    def needle_colors():
        tone = rng.uniform(0.8, 1.15)
        hue = rng.uniform(-0.02, 0.02)
        base = np.array([0.055 + hue, 0.15, 0.10 - hue], np.float32) * tone
        tip = np.array([0.13 + hue, 0.27, 0.16], np.float32) * tone
        return base, tip, tone

    def needles_on(a, b, depth, along_density, needle_len, top):
        """Flat two-ranked needles along a twig from a to b: pairs angled forward, alternating slightly."""
        a = np.array(a, np.float32)
        b = np.array(b, np.float32)
        d = b - a
        L = float(np.hypot(*d))
        if L < 2.0:
            return
        fwd = d / L
        side = np.array([-fwd[1], fwd[0]], np.float32)
        count = int(L / along_density)
        for i in range(count):
            t = (i + rng.uniform(0.0, 0.8)) / max(count, 1)
            at = a + d * t
            taper = 1.0 - 0.45 * t ** 1.5           # shorter needles towards the twig's tip
            for s in (-1.0, 1.0):
                ang = rng.uniform(0.55, 1.05)         # forward of square to the twig
                nl = needle_len * taper * rng.uniform(0.75, 1.2)
                direction = fwd * np.cos(ang) + side * s * np.sin(ang)
                tip = at + direction * nl
                base, tipc, tone = needle_colors()
                cv.capsule(at, tip, 1.35, 0.7, base, tipc, top + rng.uniform(0.0, 0.6), shade=tone)
            if rng.random() < 0.35:                   # a few lie along the top of the twig
                ang = rng.uniform(0.15, 0.4) * rng.choice([-1.0, 1.0])
                direction = fwd * np.cos(ang) + side * np.sin(ang)
                base, tipc, tone = needle_colors()
                cv.capsule(at, at + direction * needle_len * 0.8 * taper, 1.3, 0.7, base * 1.1, tipc * 1.1, top + 1.4, shade=tone)

    needle_len = n * 0.019
    # the stem
    stem_a = (margin, cy)
    stem_b = (margin + length * 0.965, cy + rng.uniform(-n * 0.01, n * 0.01))
    twigs = []                                       # (a, b, radius) for snow
    # side twigs in alternate pairs, each with second twigs, all carrying needles
    u = 0.03
    side_sign = 1.0
    while u < 0.93:
        x = margin + length * u
        hw = half_width(u)
        ang = rng.uniform(0.62, 0.88)                # forward angle from the stem
        reach = hw * rng.uniform(0.55, 1.0) ** 0.7 / np.sin(ang)      # ragged: some twigs fall well short of the outline
        reach = min(reach, (n - margin * 3.0 - x - needle_len) / max(np.cos(ang - 0.16), 0.2))      # and none runs off the tile
        reach = max(reach, needle_len)
        a = np.array([x, cy], np.float32)
        b = a + np.array([np.cos(ang), np.sin(ang) * side_sign], np.float32) * reach
        # a slight curve: draw it in three pieces bending forward
        bend = rng.uniform(0.05, 0.16)
        pts = [a]
        for k in range(1, 4):
            t = k / 3.0
            angk = ang - bend * t
            pts.append(pts[-1] + np.array([np.cos(angk), np.sin(angk) * side_sign], np.float32) * reach / 3.0)
        r_twig = n * 0.0028 * (1.0 - 0.5 * u)
        for k in range(3):
            needles_on(pts[k], pts[k + 1], 1, 2.4, needle_len, 4.0)
        for k in range(3):
            cv.capsule(pts[k], pts[k + 1], r_twig * (1.0 - k * 0.25), r_twig * (1.0 - (k + 1) * 0.25) + 0.6, twig_brown, young_twig, 5.0)
        twigs.append((pts[0], pts[-1], r_twig * 3.0))
        # second twigs off this one, on its outer side and a few on the inner, angled forward
        tlen = reach
        j = 0.18
        while j < 0.86:
            at = pts[0] + (pts[-1] - pts[0]) * j
            direction = (pts[-1] - pts[0]) / (np.hypot(*(pts[-1] - pts[0])) + 1e-6)
            for s2 in (1.0, -1.0):
                a2 = rng.uniform(0.55, 0.8)
                perp = np.array([-direction[1], direction[0]], np.float32) * s2
                d2 = direction * np.cos(a2) + perp * np.sin(a2)
                l2 = tlen * rng.uniform(0.17, 0.27) * (1.0 - j * 0.55)
                c = at + d2 * l2
                needles_on(at, c, 2, 2.6, needle_len * 0.9, 2.0)
                cv.capsule(at, c, r_twig * 0.45, 0.5, twig_brown, young_twig, 3.5)
            j += rng.uniform(0.07, 0.1)
        u += rng.uniform(0.028, 0.04)
        side_sign = -side_sign
    # the stem last, on top of all
    needles_on(stem_a, stem_b, 0, 3.0, needle_len * 0.8, 6.0)
    cv.capsule(stem_a, stem_b, n * 0.0055, n * 0.0018, twig_brown, young_twig, 7.0)

    # ---- snow: lying along the stem and the twigs in lumpy ridges, broken into lengths, its edge frayed
    snow_f = np.zeros((n, n), np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)

    def lump(cx_, cy_, r, h):
        x0, x1 = int(max(0, cx_ - r * 2.2)), int(min(n, cx_ + r * 2.2))
        y0, y1 = int(max(0, cy_ - r * 2.2)), int(min(n, cy_ + r * 2.2))
        if x1 <= x0 or y1 <= y0:
            return
        X, Y = xs[y0:y1, x0:x1], ys[y0:y1, x0:x1]
        g = np.exp(-(((X - cx_) ** 2 + (Y - cy_) ** 2) / (r * r))) * h
        snow_f[y0:y1, x0:x1] = np.maximum(snow_f[y0:y1, x0:x1], g)

    def ridge(a, b, r_base, keep, phase):
        a = np.array(a, np.float32)
        b = np.array(b, np.float32)
        L = float(np.hypot(*(b - a)))
        t = 0.0
        while t < 1.0:
            # lengths of snow and gaps along the twig, from a slow wave with a random phase
            on = np.sin(t * L / (n * 0.05) + phase) + rng.normal(0, 0.35) > (1.0 - keep * 2.0)
            r = r_base * rng.uniform(0.6, 1.25) * (1.0 - 0.35 * t)
            if on:
                p = a + (b - a) * t + rng.normal(0, r * 0.25, 2)
                lump(p[0], p[1], r, rng.uniform(0.7, 1.0))
            t += max(r * 0.45, 2.0) / max(L, 1.0)

    ridge(stem_a, stem_b, n * 0.026 * (0.7 + snow_amount * 0.5), min(1.0, snow_amount + 0.2), rng.uniform(0, 6))
    for a, b, r in twigs:
        ridge(a + (b - a) * 0.04, a + (b - a) * 0.85, n * 0.022 * (0.6 + snow_amount * 0.6), min(1.0, snow_amount + 0.1), rng.uniform(0, 6))
    # and blankets: broad soft drifts lying across several twigs at once, so from a few metres the snow is masses,
    # not stripes
    for _ in range(int(14 * snow_amount)):
        u_ = rng.uniform(0.15, 0.85)
        px = stem_a[0] + (stem_b[0] - stem_a[0]) * u_
        py = cy + rng.normal(0, half_width(u_) * 0.45)
        r = n * rng.uniform(0.05, 0.1)
        for _k in range(6):
            lump(px + rng.normal(0, r * 0.6), py + rng.normal(0, r * 0.4), r * rng.uniform(0.5, 0.9), rng.uniform(0.6, 0.85))
    lumpy = periodic_noise(n, rng, 30, 160) * 0.2 + periodic_noise(n, rng, 6, 20) * 0.08 + periodic_noise(n, rng, 2, 5) * 0.04
    snow_mask = smooth(0.42, 0.52, snow_f + lumpy)
    # snow only where it rests on the spray: on needles, or bridging a gap narrower than a few needles
    support = ndimage.maximum_filter(cv.alpha, size=int(n * 0.005) * 2 + 1)
    support = ndimage.gaussian_filter(support, 2.0)
    snow_mask *= smooth(0.35, 0.8, support)
    snow_surface = 9.0 + snow_f * 16.0 + periodic_noise(n, rng, 12, 60) * 1.2 + periodic_noise(n, rng, 1.5, 4) * 0.35

    # ---- combine
    height = cv.height * (1.0 - snow_mask) + np.maximum(cv.height, snow_surface) * snow_mask
    alpha = np.maximum(cv.alpha, snow_mask * smooth(0.4, 0.9, support))
    occlusion = 0.45 + 0.55 * smooth(0.0, 9.0, cv.height)
    occlusion = np.where(snow_mask > 0.5, np.maximum(occlusion, 0.9), occlusion)
    # colour bleeds into the cut-out, so mipmaps do not fringe dark
    color = cv.color.copy()
    empty = cv.alpha < 0.05
    if empty.any():
        idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
        color = color[idx[0], idx[1]]
    nrm = normal_map(ndimage.gaussian_filter(height, 0.6), 0.55)
    nrm[empty & (snow_mask < 0.05)] = (0.5, 0.5, 1.0)
    shade_b = np.clip(cv.shade * 0.5, 0.0, 1.0)
    rgba = np.concatenate([color, alpha[..., None]], -1)
    mask = np.stack([snow_mask, occlusion, shade_b], -1)
    return rgba, nrm, mask


def fronds(out, n_tile=2048, preview=None):
    atlas = np.zeros((n_tile * 2, n_tile * 2, 4), np.float32)
    atlas_n = np.zeros((n_tile * 2, n_tile * 2, 3), np.float32)
    atlas_m = np.zeros((n_tile * 2, n_tile * 2, 3), np.float32)
    snow_by_tile = [0.6, 0.85, 0.4, 0.72]
    for k in range(4):
        rgba, nrm, mask = spray(n_tile, 900 + k * 17, snow_by_tile[k])
        r, c = divmod(k, 2)
        # image rows run down while v runs up: tile k sits at u0 = c/2, v0 = r/2 (row block 1 - r)
        rows = slice((1 - r) * n_tile, (2 - r) * n_tile)
        cols = slice(c * n_tile, (c + 1) * n_tile)
        atlas[rows, cols] = rgba[::-1]
        atlas_n[rows, cols] = nrm[::-1] * np.array([1, -1, 1]) + np.array([0, 1, 0])      # flipping rows flips v
        atlas_m[rows, cols] = mask[::-1]
        print("spray", k, "done")
    save(os.path.join(out, "FirFronds_C.png"), atlas, "RGBA")
    save(os.path.join(out, "FirFronds_N.png"), atlas_n, "RGB")
    save(os.path.join(out, "FirFronds_M.png"), atlas_m, "RGB")
    if preview:
        # a crop of one spray at 1:1 over snow-blue, with snow drawn in, to look at
        k = slice(n_tile // 2, n_tile // 2 + 1024)
        c = atlas[k, k, :3]
        a = atlas[k, k, 3:4]
        s = atlas_m[k, k, 0:1]
        light = np.clip(0.4 + 0.8 * (atlas_n[k, k, 1:2] - 0.5) + 0.5 * (atlas_n[k, k, 2:3] - 0.5), 0, 1.4)
        col = c * light * 1.6
        col = col * (1 - s) + np.array([0.93, 0.95, 1.0]) * light * s
        bg = np.array([0.55, 0.65, 0.8])
        save(os.path.join(preview, "fronds_preview.png"), col * a + bg * (1 - a), "RGB")


# ---------------------------------------------------------------------------------------------------------------
# Bark
# ---------------------------------------------------------------------------------------------------------------

def periodic_cells(n, rng, count, stretch, warp=0.0):
    """Distance to nearest and second nearest of 'count' points on a wrapped square, cells stretched 'stretch' times
    taller than wide, their borders wandering by 'warp' (a share of the tile). Returns (f1, f2, id) per pixel."""
    pts = rng.random((count, 2))
    scaled = pts * np.array([1.0, 1.0 / stretch])
    tiles = []
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            tiles.append(scaled + np.array([dx, dy / stretch]))
    allp = np.concatenate(tiles)
    ids = np.tile(np.arange(count), 9)
    tree = cKDTree(allp)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64) / n
    if warp > 0.0:
        xs = np.mod(xs + periodic_noise(n, rng, 120, 700) * warp + periodic_noise(n, rng, 20, 90) * warp * 0.15, 1.0)
        ys = np.mod(ys + periodic_noise(n, rng, 120, 700) * warp * 1.5, 1.0)
    q = np.stack([xs.ravel(), ys.ravel() / stretch], -1)
    d, i = tree.query(q, k=2)
    return d[:, 0].reshape(n, n), d[:, 1].reshape(n, n), ids[i[:, 0]].reshape(n, n)


def fir_bark(out, n=2048, preview=None):
    rng = np.random.default_rng(31)
    # scales: cells about 6 x 14 cm on a 1.1 m tile
    f1, f2, cid = periodic_cells(n, rng, 300, 3.0, warp=0.008)
    edge = (f2 - f1) * n                         # px from the border between two plates
    edge = edge + periodic_noise(n, rng, 6, 40) * 1.2      # slightly ragged edges
    furrow = smooth(1.5, 10.0, edge)             # 0 in a furrow .. 1 on a plate
    # each plate is itself broken into thin scales, shallower
    g1, g2, _ = periodic_cells(n, rng, 2600, 1.6, warp=0.004)
    scale_edge = smooth(0.5, 3.5, (g2 - g1) * n)
    furrow = furrow * (0.75 + 0.25 * scale_edge)
    dome = np.clip(1.0 - f1 * n / 30.0, 0.0, 1.0)
    # big vertical fissures, wandering
    fiss_noise = periodic_noise(n, rng, 40, 600)
    fiss = np.abs(np.sin((np.arange(n)[None, :] / n * 7.0 + fiss_noise * 0.08) * np.pi))
    fissure = smooth(0.02, 0.18, fiss)
    flakes = periodic_noise(n, rng, 3, 30)
    lumps = periodic_noise(n, rng, 60, 900)
    cell_tone = rng.uniform(0.75, 1.2, 600)[cid % 600]
    height = furrow * 10.0 + dome * 3.0 + flakes * 0.7 + lumps * 2.0 - (1.0 - fissure) * 9.0
    occl = 0.35 + 0.65 * furrow * (0.4 + 0.6 * fissure)
    grey = np.array([0.36, 0.33, 0.30])
    warm = np.array([0.42, 0.29, 0.21])           # freshly shed scales are red-brown
    dark = np.array([0.09, 0.07, 0.06])
    warmth = np.clip((periodic_noise(n, rng, 20, 200) + 0.6) * 0.5, 0, 1)[..., None] * (dome[..., None] * 0.8)
    color = (grey * (1 - warmth) + warm * warmth) * cell_tone[..., None] * (0.85 + 0.15 * flakes[..., None])
    color = color * occl[..., None] + dark * (1.0 - occl[..., None]) * 0.6
    # lichen: pale grey-green patches on the scales
    lichen = smooth(1.6, 2.4, periodic_noise(n, rng, 10, 160) + periodic_noise(n, rng, 2, 12) * 0.3) * furrow
    color = color * (1 - lichen[..., None] * 0.35) + np.array([0.5, 0.53, 0.47]) * lichen[..., None] * 0.35
    save(os.path.join(out, "FirBark_C.png"), color, "RGB")
    save(os.path.join(out, "FirBark_N.png"), normal_map(ndimage.gaussian_filter(height, 0.8), 0.6), "RGB")
    if preview:
        save(os.path.join(preview, "firbark_preview.png"), color[:1024, :1024], "RGB")


def birch_bark(out, n=2048, preview=None):
    rng = np.random.default_rng(57)
    ys = np.arange(n)[:, None] / n
    xs = np.arange(n)[None, :] / n
    white = np.array([0.88, 0.87, 0.82])
    # peeling bands: horizontal strips a little darker or warmer, with a curled edge
    band_noise = periodic_noise(n, rng, 30, 400)
    bands = np.sin((ys * 7.0 + band_noise * 0.06) * np.pi * 2.0) + periodic_noise(n, rng, 60, 500) * 0.35
    peel = smooth(0.9, 1.15, bands) * 0.7
    tone = 0.93 + 0.035 * periodic_noise(n, rng, 20, 300) + 0.025 * periodic_noise(n, rng, 2, 10)
    color = white * tone[..., None]
    color = color * (1 - peel[..., None] * 0.25) + np.array([0.75, 0.62, 0.5]) * peel[..., None] * 0.25
    height = peel * 3.0 + periodic_noise(n, rng, 3, 40) * 0.4
    # lenticels: short dark horizontal dashes, wrapped
    lent = np.zeros((n, n), np.float32)
    for _ in range(520):
        cx, cy = rng.integers(n), rng.integers(n)
        w, h = int(rng.uniform(8, 90) * rng.uniform(0.5, 1.0)), int(rng.uniform(1.5, 4.5))
        x = (np.arange(cx - w, cx + w) % n)
        y = (np.arange(cy - h, cy + h) % n)
        prof_x = 1.0 - (np.abs(np.arange(-w, w)) / w) ** 2
        prof_y = 1.0 - (np.abs(np.arange(-h, h)) / max(h, 1)) ** 2
        lent[np.ix_(y, x)] = np.maximum(lent[np.ix_(y, x)], np.outer(prof_y, prof_x))
    lent = smooth(0.2, 0.6, lent)
    color = color * (1 - lent[..., None] * 0.85) + np.array([0.12, 0.1, 0.09]) * lent[..., None] * 0.85
    height -= lent * 2.0
    # black scars, diamond-ish, where branches were: a few per tile
    for _ in range(5):
        cx, cy = rng.integers(n), rng.integers(n)
        r = rng.uniform(40, 110)
        yy = (np.arange(n)[:, None] - cy + n // 2) % n - n // 2
        xx = (np.arange(n)[None, :] - cx + n // 2) % n - n // 2
        # a branch scar: a dark triangle drooping from a knot, its edges cracked
        q = np.abs(xx) / (r * (1.9 - 0.9 * np.clip(yy / r, -1, 1))) + np.maximum(yy, -yy * 2.2) / r
        scar = smooth(1.0, 0.85, q + periodic_noise(n, rng, 12, 80) * 0.08)
        color = color * (1 - scar[..., None]) + np.array([0.08, 0.07, 0.07]) * scar[..., None]
        height -= scar * 4.0
    save(os.path.join(out, "BirchBark_C.png"), color, "RGB")
    save(os.path.join(out, "BirchBark_N.png"), normal_map(ndimage.gaussian_filter(height, 0.8), 0.5), "RGB")
    if preview:
        save(os.path.join(preview, "birchbark_preview.png"), color[:1024, :1024], "RGB")


def birch_twigs(out, n=2048, preview=None):
    """A spray of fine birch twigs, branching and branching again, dark red-brown, with a little snow along the
    larger ones: (colour + cut-out, normal, mask) as the fir sprays are, for cards hung at the birches' limb ends.
    u runs along the spray from where it joins the limb (0) to its tips (1), v across it."""
    rng = np.random.default_rng(77)
    cv = Canvas(n)
    bark = (0.17, 0.12, 0.13)      # purple-brown, as a birch crown is in winter (redder read as olive leaves from afar)
    tip = (0.27, 0.19, 0.21)
    segments = []

    def branch(p, ang, length, width, depth):
        steps = 4
        pts = [np.array(p, np.float32)]
        a = ang
        for k in range(steps):
            a += rng.normal(0, 0.12)
            pts.append(pts[-1] + np.array([np.cos(a), np.sin(a)], np.float32) * length / steps)
        for k in range(steps):
            w0 = width * (1.0 - 0.6 * k / steps)
            w1 = width * (1.0 - 0.6 * (k + 1) / steps)
            cv.capsule(pts[k], pts[k + 1], max(w0, 0.7), max(w1, 0.6), bark, tip, 6.0 - depth * 0.6)
            if width > 2.0:
                segments.append((pts[k], pts[k + 1], width))
        if depth >= 6 or length < n * 0.02:
            return
        kids = rng.integers(2, 4)
        for c in range(kids):
            t = rng.uniform(0.3, 0.95)
            j = min(int(t * steps), steps - 1)
            at = pts[j] + (pts[j + 1] - pts[j]) * (t * steps - j)
            side = rng.choice([-1.0, 1.0])
            branch(at, a + side * rng.uniform(0.3, 0.75), length * rng.uniform(0.5, 0.72), width * 0.62, depth + 1)

    for k in range(3):
        branch((n * 0.01, n * 0.5 + rng.normal(0, n * 0.03)), rng.normal(0, 0.12), n * 0.42, n * 0.0045, 0)
    # snow: short lumps along the thicker twigs
    snow = np.zeros((n, n), np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    for a, b, w in segments:
        if rng.random() < 0.55:
            continue
        r = w * rng.uniform(0.9, 1.5)
        t0, t1 = sorted(rng.uniform(0.0, 1.0, 2))
        for t in np.linspace(t0, t1, 6):
            m = a + (b - a) * t
            x0, x1 = int(max(0, m[0] - r * 3)), int(min(n, m[0] + r * 3))
            y0, y1 = int(max(0, m[1] - r * 3)), int(min(n, m[1] + r * 3))
            if x1 <= x0 or y1 <= y0:
                continue
            X, Y = xs[y0:y1, x0:x1], ys[y0:y1, x0:x1]
            snow[y0:y1, x0:x1] = np.maximum(snow[y0:y1, x0:x1], np.exp(-(((X - m[0]) ** 2 + (Y - m[1]) ** 2) / (r * r))))
    snow_mask = smooth(0.45, 0.6, snow) * smooth(0.2, 0.6, ndimage.maximum_filter(cv.alpha, size=5))
    alpha = np.maximum(cv.alpha, snow_mask)
    height = cv.height + snow_mask * 4.0
    color = cv.color.copy()
    empty = cv.alpha < 0.05
    if empty.any():
        idx = ndimage.distance_transform_edt(empty, return_distances=False, return_indices=True)
        color = color[idx[0], idx[1]]
    nrm = normal_map(ndimage.gaussian_filter(height, 0.6), 0.6)
    nrm[empty & (snow_mask < 0.05)] = (0.5, 0.5, 1.0)
    rgba = np.concatenate([color, alpha[..., None]], -1)[::-1]
    nrm = nrm[::-1] * np.array([1, -1, 1]) + np.array([0, 1, 0])
    mask = np.stack([snow_mask, np.ones_like(snow_mask), np.full_like(snow_mask, 0.5)], -1)[::-1]
    save(os.path.join(out, "BirchTwigs_C.png"), rgba, "RGBA")
    save(os.path.join(out, "BirchTwigs_N.png"), nrm, "RGB")
    save(os.path.join(out, "BirchTwigs_M.png"), mask, "RGB")
    if preview:
        a = rgba[..., 3:4]
        c = rgba[..., :3] * (1 - mask[..., 0:1]) + np.array([0.93, 0.95, 1.0]) * mask[..., 0:1]
        save(os.path.join(preview, "birchtwigs_preview.png"), c * a + np.array([0.55, 0.65, 0.8]) * (1 - a), "RGB")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=os.path.join(REPO, "unity", "Assets", "Textures", "Trees"))
    parser.add_argument("--preview", default=None)
    parser.add_argument("--only", default=None)
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    if args.preview:
        os.makedirs(args.preview, exist_ok=True)
    if args.only in (None, "firbark"):
        fir_bark(args.out, preview=args.preview)
    if args.only in (None, "birchbark"):
        birch_bark(args.out, preview=args.preview)
    if args.only in (None, "fronds"):
        fronds(args.out, preview=args.preview)
    if args.only in (None, "birchtwigs"):
        birch_twigs(args.out, preview=args.preview)


if __name__ == "__main__":
    main()
