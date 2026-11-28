# Paints the words of the island's signboards: unity/Assets/Textures/Signs/<key>.png, one a sign.
#
# Why pictures: the SDK's Sign shows no text in the game (see tools/howto_art.py), so a board that must say something
# carries a painted picture instead (IsleKit.PaintedWords hangs it on a board's face with BWM/Picture: the picture's
# alpha is its gloss). The boards are 1.8 x 0.7 m (2.4 x 0.7 for the wide ones); a picture is stretched to its board.
#
#   python tools/sign_art.py      writes every sign (run it by hand after changing SIGNS; the pictures are committed)
import os

from PIL import Image, ImageDraw, ImageFont

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "unity", "Assets", "Textures", "Signs")
RED = (150, 30, 26)
GREEN = (22, 58, 40)
CREAM = (244, 238, 220)
GOLD = (246, 196, 84)
GLOSS = 26

# key: (width px, height px, ground colour, small line above, big line)
SIGNS = {
    "ToPizzeria": (1024, 400, RED, "TELEPORTER  -  STAND ON THE PAD", "TO CAST IRON PIZZA"),
    "ToPlaza": (1024, 400, RED, "TELEPORTER  -  STAND ON THE PAD", "TO THE PLAZA"),
    "ToWinterIsle": (1024, 400, RED, "TELEPORTER  -  STAND ON THE PAD", "TO THE WINTER ISLE"),
    "SkiStand": (1024, 300, RED, "SKIS AND SNOWBOARDS", "PICK UP A PAIR TO WEAR IT"),
    "DiscGolf": (1024, 480, GREEN, "PICK UP A DISC AND THROW", "SUMMIT DISC GOLF"),
}
BOLD = ["segoeuib.ttf", "arialbd.ttf", "verdanab.ttf"]


def font(size):
    for name in BOLD:
        path = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def fitted(draw, text, size, width):
    while size > 12:
        f = font(size)
        if draw.textlength(text, font=f) <= width:
            return f
        size -= 2
    return font(size)


def main():
    os.makedirs(OUT, exist_ok=True)
    for key, (w, h, ground, small, big) in SIGNS.items():
        img = Image.new("RGBA", (w, h), ground + (GLOSS,))
        d = ImageDraw.Draw(img)
        d.rectangle([14, 14, w - 15, h - 15], outline=GOLD + (GLOSS,), width=6)
        fs = fitted(d, small, int(h * 0.16), w - 120)
        fb = fitted(d, big, int(h * 0.36), w - 110)
        d.text((w / 2, h * 0.27), small, font=fs, fill=GOLD + (GLOSS,), anchor="mm")
        d.text((w / 2, h * 0.63), big, font=fb, fill=CREAM + (GLOSS,), anchor="mm")
        img.save(os.path.join(OUT, key + ".png"))
        print("wrote", key)


if __name__ == "__main__":
    main()
