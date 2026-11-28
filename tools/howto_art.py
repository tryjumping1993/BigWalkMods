# Paints the how-to-play board of each puzzle: unity/Assets/Textures/HowTo/<Name>.png, one a puzzle, from
# docs/puzzle-variety/howto.json (title, how many players, up to five short lines).
#
# Why pictures: the SDK's Sign shows no text in the game (its TextMeshPro has no material there), so until 2026-11-24
# no puzzle on the island said anything about itself, and players found them hard to make out. IsleKit.StartCorner
# hangs the picture on a board over each puzzle's start switch (BWM/Picture: the picture's alpha is its gloss).
#
#   python tools/howto_art.py             writes every board
#   python tools/howto_art.py --preview   also writes a contact sheet to Z:/Temp/claude/BigWalkMods/howto-preview.jpg
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(REPO, "docs", "puzzle-variety", "howto.json")
OUT = os.path.join(REPO, "unity", "Assets", "Textures", "HowTo")
W, H = 1024, 682                      # the board is 1.8 x 1.2 m
MARGIN = 58
GREEN = (22, 58, 40)                  # a painted board, dark so the letters carry in snow glare
CREAM = (244, 238, 220)
GOLD = (246, 196, 84)
RED = (176, 34, 30)
GLOSS = 26                            # alpha: BWM/Picture reads it as gloss (matt paint)


def font(names, size):
    for name in names:
        path = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


BOLD = ["segoeuib.ttf", "arialbd.ttf", "verdanab.ttf"]
SEMI = ["seguisb.ttf", "segoeuib.ttf", "arialbd.ttf"]


def fitted(draw, text, names, size, width):
    # the largest size up to 'size' at which the text fits the width
    while size > 18:
        f = font(names, size)
        if draw.textlength(text, font=f) <= width:
            return f
        size -= 2
    return font(names, size)


def board(entry):
    img = Image.new("RGB", (W, H), GREEN)
    d = ImageDraw.Draw(img)
    # a candy-cane border: red, then a cream line
    d.rectangle([0, 0, W - 1, H - 1], outline=RED, width=16)
    d.rectangle([20, 20, W - 21, H - 21], outline=CREAM, width=4)
    inner = W - 2 * MARGIN

    title = fitted(d, entry["title"], BOLD, 92, inner * 0.66)
    d.text((MARGIN, 44), entry["title"], font=title, fill=GOLD)
    players = "players: " + entry["players"]
    pf = fitted(d, players, SEMI, 40, inner * 0.33)
    d.text((W - MARGIN - d.textlength(players, font=pf), 82), players, font=pf, fill=CREAM)
    d.line([MARGIN, 160, W - MARGIN, 160], fill=GOLD, width=5)

    lines = entry["lines"]
    # one size for the whole board: the longest line decides
    size = 62
    for line in lines:
        size = min(size, fitted(d, line, SEMI, size, inner).size)
    f = font(SEMI, size)
    top, bottom = 186, H - 46
    pitch = min((bottom - top) / max(len(lines), 1), size * 1.5)
    y = top + ((bottom - top) - pitch * len(lines)) / 2
    for line in lines:
        d.text((MARGIN, y), line, font=f, fill=CREAM)
        y += pitch
    img.putalpha(GLOSS)
    return img, size


def main():
    with open(SOURCE, encoding="utf-8") as fh:
        data = json.load(fh)
    os.makedirs(OUT, exist_ok=True)
    made = []
    for name, entry in data.items():
        if name.startswith("_"):
            continue
        img, size = board(entry)
        img.save(os.path.join(OUT, name + ".png"))
        made.append((name, img, size))
        if size < 44:
            print("  note: %s is set in %d px letters (a long line)" % (name, size))
    print("howto_art: %d boards in %s" % (len(made), OUT))
    if "--preview" in sys.argv:
        cols = 5
        rows = (len(made) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * 416, rows * 280), (40, 40, 44))
        for i, (name, img, size) in enumerate(made):
            sheet.paste(img.convert("RGB").resize((408, 272)), ((i % cols) * 416 + 4, (i // cols) * 280 + 4))
        path = r"Z:\Temp\claude\BigWalkMods\howto-preview.jpg"
        os.makedirs(os.path.dirname(path), exist_ok=True)
        sheet.save(path, quality=85)
        print("  preview:", path)


if __name__ == "__main__":
    main()
