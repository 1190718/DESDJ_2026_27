"""Builds the violet slime palette used by the setup tool (MistIslesSetup.RecolourSlime).

The RPG Monster DUO pack shares one colour atlas between the slime and the turtle. This makes a
copy of the atlas (albedo and emission) where the red swatches become violet, so the slime no
longer looks like the island's red mushrooms. Only the slime's own material uses the copy.

Run from the repository root:  python Tools/Art/recolour_slime.py   (needs Pillow)
"""
import colorsys
from pathlib import Path

from PIL import Image

PACK = Path("Assets/RPG Monster DUO PBR Polyart/Textures")
OUT = Path("Assets/MistIsles/Generated/Textures")
TARGET_HUE = 0.78  # violet


def shift(src: Path, dst: Path) -> int:
    im = Image.open(src).convert("RGB")
    px = im.load()
    changed = 0
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = (v / 255 for v in px[x, y])
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            if s > 0.35 and (h < 0.055 or h > 0.95):  # reds
                offset = h if h < 0.5 else h - 1.0  # keep the small variations between swatches
                r2, g2, b2 = colorsys.hsv_to_rgb((TARGET_HUE + offset) % 1.0, min(1, s * 0.95), min(1, v * 1.08))
                px[x, y] = (int(r2 * 255), int(g2 * 255), int(b2 * 255))
                changed += 1
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst)
    return changed


if __name__ == "__main__":
    for name in ("Albedo", "Emission"):
        n = shift(PACK / f"{name}.png", OUT / f"SlimePurple_{name}.png")
        print(f"{name}: {n} pixels recoloured")
