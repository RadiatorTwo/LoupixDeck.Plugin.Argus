"""Generates the plugin icon (../../icon.png) from the Fluent Emoji lizard (lizard_flat.svg).

The silhouette is mirrored and rotated into a diagonal pose, then shaded as polished silver
from a height map and given a soft drop shadow.

Requirements: pip install pillow numpy resvg-py
Usage:        python make_icon.py
"""
import io
import re
from pathlib import Path

import numpy as np
import resvg_py
from PIL import Image, ImageChops, ImageFilter, ImageOps

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "lizard_flat.svg"
OUTPUT = HERE.parent.parent / "icon.png"
PREVIEW = HERE / "preview.png"

S = 1024          # working resolution, downscaled to the final size at the end
FINAL = 256       # icon size
ROTATION = -35    # degrees, clockwise after mirroring
FILL = 0.86       # share of the canvas the lizard spans

svg = SOURCE.read_text(encoding="utf-8")


def render(s: str) -> Image.Image:
    data = resvg_py.svg_to_bytes(svg_string=s, width=S, height=S)
    return Image.open(io.BytesIO(bytes(data))).convert("RGBA")


# Body and eyes as separate masks: drop the other fill colour from the SVG.
body = render(re.sub(r'<path[^>]*fill="#1C1C1C"[^>]*/>', "", svg)).split()[3]
eyes = render(re.sub(r'<path[^>]*fill="#00D26A"[^>]*/>', "", svg)).split()[3]


def pose(img: Image.Image) -> Image.Image:
    return ImageOps.mirror(img).rotate(ROTATION, resample=Image.BICUBIC, center=(S / 2, S / 2))


body, eyes = pose(body), pose(eyes)

# Fit and centre on the canvas.
bbox = body.getbbox()
body, eyes = body.crop(bbox), eyes.crop(bbox)
k = (S * FILL) / max(body.size)
size = (int(body.size[0] * k), int(body.size[1] * k))
body, eyes = body.resize(size, Image.LANCZOS), eyes.resize(size, Image.LANCZOS)


def place(m: Image.Image) -> Image.Image:
    c = Image.new("L", (S, S), 0)
    c.paste(m, ((S - size[0]) // 2, (S - size[1]) // 2))
    return c


mask, eyes = place(body), place(eyes)

# Height map: a rounded tube profile from the mask blurred at several radii.
h = sum(np.asarray(mask.filter(ImageFilter.GaussianBlur(r)), dtype=np.float32) / 255 for r in (6, 14, 26)) / 3
h = np.sqrt(np.clip(h, 0, 1)) * 60
gy, gx = np.gradient(h)
n = np.dstack((-gx, -gy, np.ones_like(h)))
n /= np.linalg.norm(n, axis=2, keepdims=True)

# Light from the top left: diffuse + specular highlight.
light = np.array([-0.5, -0.6, 0.62])
light /= np.linalg.norm(light)
diffuse = np.clip((n * light).sum(2), 0, 1)
half = light + np.array([0, 0, 1.0])
half /= np.linalg.norm(half)
spec = np.clip((n * half).sum(2), 0, 1) ** 40

# Chrome look: environment banding by surface orientation (bright sky, dark horizon, ground).
env = np.interp(n[..., 1], [-1, -0.35, 0, 0.25, 0.6, 1], [235, 200, 95, 140, 70, 40])
lum = np.clip(0.55 * env + 0.45 * (60 + 170 * diffuse) + 230 * spec, 0, 255)
rgb = np.dstack((lum * 0.97, lum * 0.99, np.minimum(lum * 1.05 + 6, 255)))  # slightly cool silver

e = np.asarray(eyes, dtype=np.float32)[..., None] / 255
rgb = rgb * (1 - e) + np.array([25, 25, 30]) * e

fg = Image.fromarray(rgb.astype(np.uint8)).convert("RGBA")
fg.putalpha(mask)

shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
shadow.putalpha(ImageChops.offset(mask, 16, 22).filter(ImageFilter.GaussianBlur(14)).point(lambda v: int(v * 0.5)))

out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
out.alpha_composite(shadow)
out.alpha_composite(fg)

icon = out.resize((FINAL, FINAL), Image.LANCZOS)
icon.save(OUTPUT)

# Preview on a light and a dark background.
preview = Image.new("RGBA", (FINAL * 2, FINAL), (255, 255, 255, 255))
preview.alpha_composite(icon)
dark = Image.new("RGBA", (FINAL, FINAL), (32, 32, 36, 255))
dark.alpha_composite(icon)
preview.paste(dark, (FINAL, 0))
preview.save(PREVIEW)

print(f"Wrote {OUTPUT}")
