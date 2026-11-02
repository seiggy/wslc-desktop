"""Builds every raster icon asset + the contact sheet from the SVG masters.
Run: python gen_svg.py && python build_assets.py   (needs `npm i` in this folder for resvg)"""
import json, os, struct, subprocess, tempfile
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BR = os.path.join(ROOT, "assets", "branding")
AS = os.path.join(ROOT, "src", "WslcDesktop", "Assets")
TRAY = os.path.join(AS, "Tray")
for d in (AS, TRAY):
    os.makedirs(d, exist_ok=True)

MASTER = os.path.join(BR, "wslc-desktop-logo.svg")
SMALL = os.path.join(BR, "wslc-desktop-logo-small.svg")
MONO_K = os.path.join(BR, "wslc-desktop-mono-black.svg")
MONO_W = os.path.join(BR, "wslc-desktop-mono-white.svg")
SCALES = (100, 125, 150, 200, 400)

jobs = []


def job(svg, size, out, fill=1.0):
    jobs.append({"svg": svg, "size": size, "out": out, "fill": fill})


def pick(px):
    return SMALL if px <= 32 else MASTER


tmp = tempfile.mkdtemp()
ico_png = {}
for px in (16, 20, 24, 32, 40, 48, 64, 256):
    p = os.path.join(tmp, f"ico{px}.png")
    job(pick(px), px, p)
    ico_png[px] = p

# Title bar icon: 16 DIP at each scale.
for sc in SCALES:
    px = round(16 * sc / 100)
    job(pick(px), px, os.path.join(AS, f"TitleBarIcon.scale-{sc}.png"))

# Square44x44Logo: scale + targetsize (plain, unplated, lightunplated).
for sc in SCALES:
    px = round(44 * sc / 100)
    job(pick(px), px, os.path.join(AS, f"Square44x44Logo.scale-{sc}.png"))
for t in (16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256):
    for suffix in ("", "_altform-unplated", "_altform-lightunplated"):
        job(pick(t), t, os.path.join(AS, f"Square44x44Logo.targetsize-{t}{suffix}.png"))

tiles = {  # name: (w, h, logo fill)
    "Square71x71Logo": (71, 71, 0.8),
    "Square150x150Logo": (150, 150, 0.72),
    "Wide310x150Logo": (310, 150, 0.8),
    "Square310x310Logo": (310, 310, 0.62),
    "StoreLogo": (50, 50, 1.0),
    "SplashScreen": (620, 300, 0.7),
}
for name, (w, h, fill) in tiles.items():
    for sc in SCALES:
        pw, ph = round(w * sc / 100), round(h * sc / 100)
        job(pick(min(pw, ph) * fill), [pw, ph], os.path.join(AS, f"{name}.scale-{sc}.png"), fill)

# Tray: black glyph for light taskbars, white for dark ones; one PNG per DPI size.
tray_png = {"light": {}, "dark": {}}
for theme, svg in (("light", MONO_K), ("dark", MONO_W)):
    for px in (16, 20, 24, 32):
        out = os.path.join(TRAY, f"tray-{theme}-{px}.png")
        job(svg, px, out)
        tray_png[theme][px] = out

# Previews for the contact sheet.
prev = {}
for px in (16, 20, 24, 32, 40, 48, 64, 128):
    for key, svg in (("color", pick(px)), ("mono_k", MONO_K), ("mono_w", MONO_W)):
        p = os.path.join(tmp, f"prev_{key}_{px}.png")
        job(svg, px, p)
        prev[(key, px)] = p

jp = os.path.join(tmp, "jobs.json")
with open(jp, "w") as f:
    json.dump(jobs, f)
subprocess.run(["node", os.path.join(HERE, "render.js"), jp], check=True)


def write_ico(path, pngs):
    entries, blobs, off = [], [], 6 + 16 * len(pngs)
    for px, p in sorted(pngs.items()):
        data = open(p, "rb").read()
        entries.append(struct.pack("<BBBBHHII", px % 256, px % 256, 0, 0, 1, 32, len(data), off))
        blobs.append(data)
        off += len(data)
    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(pngs)) + b"".join(entries) + b"".join(blobs))


write_ico(os.path.join(AS, "AppIcon.ico"), ico_png)
write_ico(os.path.join(TRAY, "tray-light.ico"), tray_png["light"])
write_ico(os.path.join(TRAY, "tray-dark.ico"), tray_png["dark"])

# ---- contact sheet ----
orig = Image.open(os.path.join(BR, "original", "Fluent W and C Layered Icon.png")).convert("RGBA")
sizes = (16, 20, 24, 32, 40, 48, 64, 128)
ZOOM = 4
font = ImageFont.load_default(size=14)
CELL = 140
labels = ["Original (downscaled)", "Refined, actual px", "Refined, 4x zoom", "Mono, actual px", "Mono, 4x zoom"]
LW = 190
W = LW + CELL * len(sizes)
panel_h = 24 + CELL * len(labels) + 10
sheet = Image.new("RGB", (W, panel_h * 2), "white")
d = ImageDraw.Draw(sheet)
for pi, (bg, fg, mono) in enumerate((((243, 243, 243), (20, 20, 20), "mono_k"), ((32, 32, 32), (240, 240, 240), "mono_w"))):
    y0 = pi * panel_h
    d.rectangle([0, y0, W, y0 + panel_h], fill=bg)
    d.text((10, y0 + 4), "Light theme" if pi == 0 else "Dark theme", fill=fg, font=font)
    for ri, lab in enumerate(labels):
        y = y0 + 24 + ri * CELL
        d.text((10, y + CELL // 2 - 8), lab, fill=fg, font=font)
        for ci, px in enumerate(sizes):
            if ri == 0:
                im = orig.resize((px, px), Image.LANCZOS)
            elif ri in (1, 2):
                im = Image.open(prev[("color", px)]).convert("RGBA")
            else:
                im = Image.open(prev[(mono, px)]).convert("RGBA")
            if ri in (2, 4):
                if px > 32:
                    continue
                im = im.resize((px * ZOOM, px * ZOOM), Image.NEAREST)
            x = LW + ci * CELL + (CELL - im.width) // 2
            sheet.paste(im, (x, y + (CELL - im.height) // 2), im)
    for ci, px in enumerate(sizes):
        d.text((LW + ci * CELL + 50, y0 + 4), f"{px}px", fill=fg, font=font)
sheet.save(os.path.join(BR, "icon-contact-sheet.png"))
print("done", len(jobs), "renders")
