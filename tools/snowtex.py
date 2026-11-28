"""Snow surface textures for the Winter Isle, generated in code: tileable height fields and the maps made from them.

    python tools/snowtex.py [--out DIR] [--preview DIR]

Writes into unity/Assets/Textures/Snow/ (not committed; tools/bwm.ps1 regenerates them when they are missing or
older than this script):
    SnowWind_N.png   2048 px over 12 m   normal map: wind-carved surface (sastrugi, ripples, clumps, undulation)
    SnowWind_H.png   2048 px over 12 m   R height, G cavity (0 trough .. 0.5 level .. 1 crest), B scoured flats
    SnowGrain_N.png  2048 px over 3.2 m  normal map: crust and grains, for the last few metres
    SnowGrain_H.png  2048 px over 3.2 m  R height, G cavity
    SnowTrack_N.png  2048 px over 6 m    normal map: trodden snow (packed, lumpy, covered in boot prints)
    SnowTrack_H.png  2048 px over 6 m    R height, G cavity
(Grain and track were 1024 px over half the size until 2026-11-22: the same sharpness, now as large as the base game's
textures and repeating half as often.)

How it is done, as snow materials usually are (one height field, every other map derived from it):
- every noise is made by spectral synthesis (shaped white noise through an FFT), so each map tiles exactly;
- sastrugi: ridges lying along the wind, from noise stretched along it, folded into sharp crests and warped;
- ripples: a narrow band of wavelengths across the wind (8 to 14 cm) gives crests that wander, fork and die out
  like real ones; their analytic signal gives a phase, so each crest can be steep on its lee side;
- each kind of relief lies in patches (scoured flats with ripples, sastrugi fields, soft undulation between);
- clumps: scattered soft lumps of fallen snow; crust and grains in the fine map;
- trodden snow: boot prints (a sole and a heel, with a pushed-up rim) stamped by convolution at many headings.
The wind blows from the north-west, as on the island (downwind is +x 0.8, z -0.6). Texture u runs along world x
and v along world z (the shader projects the maps from above).
"""
import argparse
import os

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WIND = np.array([0.8, -0.6])      # downwind, in (u, v) = (world x, world z)


class Field:
    """A square, tileable field of side `size` metres sampled on n x n points, indexed [v, u]."""

    def __init__(self, n, size, seed):
        self.n, self.size = n, size
        self.rng = np.random.default_rng(seed)
        k = np.fft.fftfreq(n, d=size / n)      # cycles per metre
        self.ku, self.kv = np.meshgrid(k, k)    # [v, u]
        self.k = np.hypot(self.ku, self.kv)
        self.along = self.ku * WIND[0] + self.kv * WIND[1]      # frequency along the wind
        self.across = -self.ku * WIND[1] + self.kv * WIND[0]    # ... and across it

    def shaped(self, amplitude, analytic=False):
        """White noise shaped by an amplitude spectrum; unit variance. analytic: keep only along > 0 (complex)."""
        spectrum = np.fft.fft2(self.rng.standard_normal((self.n, self.n))) * amplitude
        if analytic:
            spectrum = np.where(self.along > 0, spectrum * 2.0, 0.0)
            z = np.fft.ifft2(spectrum)
            return z / np.sqrt(np.mean(np.abs(z) ** 2) / 2.0)
        f = np.real(np.fft.ifft2(spectrum))
        return (f - f.mean()) / (f.std() + 1e-12)

    def band(self, shortest, longest, slope=1.0):
        """Isotropic noise with wavelengths between shortest and longest metres (power falling as k^-slope)."""
        k = np.maximum(self.k, 1e-6)
        amplitude = k ** (-slope) * np.exp(-(k * shortest) ** 2) * (1.0 - np.exp(-(k * longest) ** 2))
        return self.shaped(amplitude)

    def blur(self, f, sigma):
        """Gaussian blur, sigma in metres, wrapping round the tile."""
        return np.real(np.fft.ifft2(np.fft.fft2(f) * np.exp(-2.0 * (np.pi * sigma * self.k) ** 2)))

    def warp(self, f, du, dv):
        """Moves the field by (du, dv) metres at each point, wrapping."""
        scale = self.n / self.size
        iv, iu = np.mgrid[0:self.n, 0:self.n].astype(np.float64)
        return ndimage.map_coordinates(f, [iv + dv * scale, iu + du * scale], order=3, mode="grid-wrap")

    def lumps(self, per_square_metre, radius, spread=0.4):
        """Soft round lumps scattered at random, radius in metres (varying by `spread`); peaks near 1."""
        count = int(per_square_metre * self.size * self.size)
        out = np.zeros((self.n, self.n))
        scale = self.n / self.size
        for r in (radius * (1.0 - spread), radius, radius * (1.0 + spread)):
            pts = np.zeros((self.n, self.n))
            iu = self.rng.integers(0, self.n, count // 3)
            iv = self.rng.integers(0, self.n, count // 3)
            np.add.at(pts, (iv, iu), self.rng.uniform(0.4, 1.0, count // 3))
            out += self.blur(pts, r * 0.6) * (2.0 * np.pi * (r * 0.6 * scale) ** 2)
        return out


def smooth(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def wind_surface(n=2048, size=12.0, seed=1224):
    f = Field(n, size, seed)
    # where each kind of relief lies: mostly soft fresh snow; ripples on about a quarter of it, sastrugi in a
    # few patches (two independent slow fields, so the two kinds sometimes meet)
    ripple_patch = smooth(0.35, 1.2, f.band(1.8, 8.0, 0.5))
    sastrugi_patch = smooth(0.7, 1.5, f.band(1.5, 7.0, 0.5))
    ripple_patch *= 1.0 - sastrugi_patch

    # soft undulation (swells a few metres across) and pillows of fresh snow (tens of centimetres)
    undulation = f.band(1.0, 6.0, 1.2)
    pillows = f.band(0.16, 1.4, 1.5)

    # sastrugi: wind-cut drifts lying along the wind, 1 to 3 m long and a few tens of centimetres wide, in two
    # layers (each deposit a step with a rounded riser), with a prow facing into the wind and a tail downwind
    amp = np.exp(-(f.along / 0.55) ** 2) * np.exp(-((np.abs(f.across) - 1.2) / 1.0) ** 2)
    s = smooth(0.35, 1.6, f.shaped(amp))
    wander = f.band(0.4, 2.5, 1.0) * 0.06
    s = f.warp(s, wander * WIND[1], -wander * WIND[0])
    layers = s * 2.0
    s = (np.floor(layers) + smooth(0.45, 1.0, layers - np.floor(layers))) / 2.0
    tail = sum(f.warp(s, -WIND[0] * d, -WIND[1] * d) * (1.0 - d / 0.5) for d in (0.1, 0.2, 0.3, 0.4))
    s = np.maximum(s, tail * 0.3)
    s = f.blur(s, 0.02)
    s = (s - s.mean()) / s.std()

    # ripples: crests across the wind, 9 to 13 cm apart and a few tens of centimetres long; the band of
    # wavelengths is just wide enough that they fork and die out here and there; steep on the lee side
    k0 = 1.0 / 0.11
    amp = sum(np.exp(-((f.along - sgn * k0) / 1.9) ** 2 - (f.across / 2.0) ** 2) for sgn in (1, -1))
    z = f.shaped(amp, analytic=True)
    envelope, phase = np.abs(z), np.angle(z)
    ripples = np.minimum(envelope, 1.6) * (np.sin(phase) - 0.25 * np.sin(2.0 * phase))
    bend = f.band(0.5, 2.5, 1.0) * 0.03
    ripples = f.warp(ripples, bend * WIND[0], bend * WIND[1])
    ripples = (ripples - ripples.mean()) / ripples.std()

    # a few clumps of fallen snow
    clumps = f.lumps(2.0, 0.08)
    clumps = (clumps - clumps.mean()) / clumps.std()

    height = (0.025 * undulation
              + 0.013 * pillows * (1.0 - 0.6 * ripple_patch)
              + 0.035 * s * sastrugi_patch
              + 0.0028 * ripples * ripple_patch
              + 0.003 * clumps * (1.0 - 0.8 * ripple_patch))
    return f, height, ripple_patch


def track_surface(n=2048, size=6.0, seed=77):
    """Trodden snow: packed and lumpy, covered in boot prints going both ways, newer ones over older."""
    f = Field(n, size, seed)
    step = size / n
    # distance from the tile's origin, wrapped, for building kernels to convolve with
    d = (np.arange(n) + n // 2) % n - n // 2
    du, dv = np.meshgrid(d * step, d * step)
    lumpy = 0.006 * f.band(0.08, 0.6, 1.4) + 0.0009 * f.band(0.012, 0.08, 0.8)
    height = lumpy.copy()
    headings = 12
    for h in range(headings):
        a = 2.0 * np.pi * (h + f.rng.uniform(-0.3, 0.3)) / headings
        along = du * np.cos(a) + dv * np.sin(a)
        across = -du * np.sin(a) + dv * np.cos(a)
        # a boot print: the sole and the heel, each a flat-bottomed dent, with snow pushed up round the toe
        sole = np.exp(-(((along - 0.045) / 0.085) ** 2 + (across / 0.048) ** 2) ** 2)
        heel = np.exp(-(((along + 0.095) / 0.04) ** 2 + (across / 0.04) ** 2) ** 2)
        rim = np.exp(-(((along - 0.06) / 0.14) ** 2 + (across / 0.09) ** 2) ** 2) - np.maximum(sole, heel)
        kernel = -np.maximum(sole, heel * 0.9) + 0.22 * np.clip(rim, 0.0, 1.0)
        pts = np.zeros((n, n))
        count = int(0.8 * size * size)
        np.add.at(pts, (f.rng.integers(0, n, count), f.rng.integers(0, n, count)), 1.0)
        prints = np.real(np.fft.ifft2(np.fft.fft2(np.minimum(pts, 1.0)) * np.fft.fft2(kernel)))
        depth = f.rng.uniform(0.014, 0.026)
        dent = np.clip(-prints, 0.0, 1.0)
        # a new print flattens what was there before pressing its own shape in
        height = height * (1.0 - 0.75 * dent) + lumpy * 0.75 * dent * 0.3 + depth * prints
    return f, height


def grain_surface(n=2048, size=3.2, seed=2512):
    f = Field(n, size, seed)
    crust = f.band(0.018, 0.16, 1.5)                       # a soft crust, gently lumpy
    grains = f.lumps(250.0, 0.004, 0.5)                    # loose grains lying on it, here and there
    grains = (grains - grains.mean()) / grains.std()
    pits = f.lumps(25.0, 0.012, 0.4)                       # little pits where pellets bounced out
    pits = (pits - pits.mean()) / pits.std()
    height = 0.0016 * crust + 0.00035 * grains - 0.0006 * pits
    return f, height


def normal_map(f, height):
    """Tangent-space normals (u = +x, v = +z, OpenGL style as Unity expects), as an RGB image array."""
    step = f.size / f.n
    du = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / (2.0 * step)
    dv = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) / (2.0 * step)
    nrm = np.stack([-du, -dv, np.ones_like(height)], axis=-1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    return nrm


def cavity(f, height, sigma):
    """0 in a trough, 0.5 on level ground, 1 on a crest: height against its blurred surroundings."""
    c = height - f.blur(height, sigma)
    return np.clip(0.5 + c / (4.0 * c.std() + 1e-12), 0.0, 1.0)


def save_rgb(path, rgb01):
    """[v, u] array, v up: image rows run top-down, so flip (Unity's v = 0 is the bottom row)."""
    img = np.clip(np.flipud(rgb01) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(img, "RGB").save(path, optimize=True)


def shaded_preview(f, height, nrm, path, crop=None):
    """A low-sun render of the surface, to look at the relief without the game."""
    sun = np.array([-0.55, 0.35, 0.45])
    sun /= np.linalg.norm(sun)
    lit = np.clip(nrm @ sun, 0.0, 1.0) * 0.85 + 0.15
    rgb = lit[..., None] * np.array([0.96, 0.97, 1.0]) + (1.0 - lit[..., None]) * np.array([0.30, 0.38, 0.55]) * 0.4
    if crop:
        rgb = rgb[:crop, :crop]
    save_rgb(path, np.clip(rgb, 0.0, 1.0))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=os.path.join(REPO, "unity", "Assets", "Textures", "Snow"))
    parser.add_argument("--preview", default=None)
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)

    f, h, flats = wind_surface()
    nrm = normal_map(f, h)
    save_rgb(os.path.join(args.out, "SnowWind_N.png"), nrm * 0.5 + 0.5)
    hn = (h - h.min()) / (h.max() - h.min())
    save_rgb(os.path.join(args.out, "SnowWind_H.png"), np.stack([hn, cavity(f, h, 0.25), flats], axis=-1))
    print("SnowWind: %.1f cm of relief over %g m, %d px" % ((h.max() - h.min()) * 100, f.size, f.n))
    if args.preview:
        os.makedirs(args.preview, exist_ok=True)
        shaded_preview(f, h, nrm, os.path.join(args.preview, "wind-shaded.png"))
        shaded_preview(f, h, nrm, os.path.join(args.preview, "wind-shaded-crop.png"), crop=512)

    g, gh = grain_surface()
    gn = normal_map(g, gh)
    save_rgb(os.path.join(args.out, "SnowGrain_N.png"), gn * 0.5 + 0.5)
    ghn = (gh - gh.min()) / (gh.max() - gh.min())
    save_rgb(os.path.join(args.out, "SnowGrain_H.png"), np.stack([ghn, cavity(g, gh, 0.02), np.zeros_like(ghn)], axis=-1))
    print("SnowGrain: %.1f mm of relief over %g m, %d px" % ((gh.max() - gh.min()) * 1000, g.size, g.n))
    if args.preview:
        shaded_preview(g, gh, gn, os.path.join(args.preview, "grain-shaded.png"))

    t, th = track_surface()
    tn = normal_map(t, th)
    save_rgb(os.path.join(args.out, "SnowTrack_N.png"), tn * 0.5 + 0.5)
    thn = (th - th.min()) / (th.max() - th.min())
    save_rgb(os.path.join(args.out, "SnowTrack_H.png"), np.stack([thn, cavity(t, th, 0.06), np.zeros_like(thn)], axis=-1))
    print("SnowTrack: %.1f cm of relief over %g m, %d px" % ((th.max() - th.min()) * 100, t.size, t.n))
    if args.preview:
        shaded_preview(t, th, tn, os.path.join(args.preview, "track-shaded.png"))


if __name__ == "__main__":
    main()
