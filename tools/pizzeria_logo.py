"""The Cast Iron Pizza logo as a decal for the pizzeria's window and fascia sign.

The user's logo (tools/art/cast-iron-logo.png) is black ink with a white pizza on a clear ground. Its skillet's handle
reaches out to the left, so the picture's middle is not the lettering's: CAST IRON and PIZZA are centred about 56 %
of the way across. This pads the right side so the lettering's axis is the decal's centre (the handle then reaches
further left, as it does on the restaurant's sign), and keeps only the ink, in cream: window vinyl and a painted sign
are the ink alone, the pizza's white is clear glass or the board showing through.

Writes unity/Assets/Textures/Pizzeria/CastIronLogoInk.png (RGBA, straight alpha). Python with numpy and Pillow.
"""
import os

import numpy as np
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, "tools", "art", "cast-iron-logo.png")
OUT = os.path.join(REPO, "unity", "Assets", "Textures", "Pizzeria", "CastIronLogoInk.png")
INK = (247, 238, 214)      # cream


def text_axis(alpha):
    """Where the lettering is centred, as a fraction of the width: the top and bottom bands hold only lettering."""
    h, w = alpha.shape
    centres = []
    for y0, y1 in ((0.0, 0.3), (0.72, 1.0)):
        cols = np.nonzero((alpha[int(y0 * h):int(y1 * h)] > 128).any(0))[0]
        centres.append((cols.min() + cols.max()) / 2.0 / w)
    return sum(centres) / len(centres)


def main():
    src = np.asarray(Image.open(SRC).convert("RGBA")).astype(np.float32) / 255.0
    h, w = src.shape[:2]
    lum = src[..., :3].mean(-1)
    ink = src[..., 3] * np.clip((0.75 - lum) / 0.5, 0.0, 1.0)      # black ink only: the white pizza becomes clear
    axis = text_axis(src[..., 3] * 255.0)
    half = max(axis, 1.0 - axis) * w
    width = int(round(2 * half))
    left = int(round(half - axis * w))      # where the source's left edge goes in the padded picture
    out = np.zeros((h, width, 4), np.float32)
    out[:, left:left + w, 3] = ink
    out[..., 0], out[..., 1], out[..., 2] = INK[0] / 255.0, INK[1] / 255.0, INK[2] / 255.0
    img = Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGBA")
    scale = 2048 / max(img.size)
    img = img.resize((int(img.size[0] * scale), int(img.size[1] * scale)), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print("logo decal %dx%d, lettering at %.3f of the source's width, padded to centre it -> %s" % (img.size[0], img.size[1], axis, OUT))


if __name__ == "__main__":
    main()
