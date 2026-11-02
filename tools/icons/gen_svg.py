"""Generates the WSLc Desktop logo SVGs (master, small-size, monochrome).
Concept: a stack of image layers (container image layers) with a small chibi penguin wrapping the lower-right corner."""
import os
import sys

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join("..", "..", "assets", "branding")
os.makedirs(OUT, exist_ok=True)

BODY = "M0,-136 C54,-136 74,-100 74,-62 C74,-22 50,-8 0,-8 C-50,-8 -74,-22 -74,-62 C-74,-100 -54,-136 0,-136 Z"
FLIPPERS = [(-76, -56, 14, 30, 16), (76, -56, 14, 30, -16)]  # cx, cy, rx, ry, rotation
FEET = [(-26, -6, 19, 9), (26, -6, 19, 9)]


def penguin(x, y, scale, flat):
    """Chibi penguin; origin is bottom-centre."""
    body, wing = ("#1F46C8", "#1A3AAE") if flat else ("url(#gb)", "#1B3DB0")
    beak = "#FF9F1C"
    beak_hi, beak_lo = "#FFB84D", "#E36F00"
    g = []
    for cx, cy, rx, ry, rot in FLIPPERS:
        g.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="{wing}" transform="rotate({rot} {cx} {cy})"/>')
    g.append(f'<path d="{BODY}" fill="{body}"/>')
    for cx, cy, rx, ry in FEET:
        g.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" fill="{beak}"/>')
    g.append('<ellipse cx="0" cy="-58" rx="52" ry="46" fill="#FFFFFF"/>')
    er = 11 if flat else 9.5
    for ex in (-23, 23):
        g.append(f'<circle cx="{ex}" cy="-80" r="{er}" fill="#0B1240"/>')
        g.append(f'<circle cx="{ex + 3.5}" cy="-84" r="{3.4 if flat else 3}" fill="#fff"/>')
    g.append(f'<path d="M-12,-66 Q0,-71 12,-66 Q9,-60 0,-58 Q-9,-60 -12,-66 Z" fill="{beak_hi}" stroke="{beak_hi}" stroke-width="4" stroke-linejoin="round"/>')
    g.append(f'<path d="M-9,-59 Q0,-56 9,-59 Q6,-50 0,-48 Q-6,-50 -9,-59 Z" fill="{beak_lo}" stroke="{beak_lo}" stroke-width="3" stroke-linejoin="round"/>')
    return f'<g transform="translate({x},{y}) scale({scale})">{"".join(g)}</g>'


def knock(px, py, sc, gap):
    """Penguin silhouette dilated by `gap`, used to cut a clear separation out of the stack."""
    st = f'fill="#000" stroke="#000" stroke-width="{gap * 2 / sc:.1f}" stroke-linejoin="round"'
    parts = [f'<path d="{BODY}" {st}/>']
    for cx, cy, rx, ry, rot in FLIPPERS:
        parts.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" {st} transform="rotate({rot} {cx} {cy})"/>')
    for cx, cy, rx, ry in FEET:
        parts.append(f'<ellipse cx="{cx}" cy="{cy}" rx="{rx}" ry="{ry}" {st}/>')
    return f'<g transform="translate({px},{py}) scale({sc})">{"".join(parts)}</g>'


def slab(cx, cy, hw, hh, d):
    t, r, b, l = (cx, cy - hh), (cx + hw, cy), (cx, cy + hh), (cx - hw, cy)
    side = [l, b, r, (r[0], r[1] + d), (b[0], b[1] + d), (l[0], l[1] + d)]
    pts = lambda p: " ".join(f"{x},{y}" for x, y in p)
    return pts(side), pts([t, r, b, l])


def stack_svg(flat, title):
    cx, hw, hh, d, s = 256, 150, 86, 52, 36
    cys = (154, 236, 318)
    px, py, sc, gap = 372, 482, 1.0, 12
    if flat:
        hw, hh, d, s = 152, 84, 56, 40
        cys = (150, 236, 322)
        px, py, sc, gap = 372, 486, 1.05, 16
    layers = [
        (cys[2], ("#5CE3FF", "#1FB8F2"), "#27BDF5", "#0E8DD0"),
        (cys[1], ("#9A8CFF", "#6A50F2"), "#7358F5", "#4630BE"),
        (cys[0], ("#52B6FF", "#2062E8"), "#2A6DF0", "#1646B8"),
    ]
    defs, body = [], []
    for i, (cy, (g1, g2), flat_fill, side) in enumerate(layers):
        face = flat_fill if flat else f"url(#g{i})"
        if not flat:
            defs.append(f'<linearGradient id="g{i}" x1="0.15" y1="0" x2="0.85" y2="1">'
                        f'<stop offset="0" stop-color="{g1}"/><stop offset="1" stop-color="{g2}"/></linearGradient>')
        sp, fp = slab(cx, cy, hw, hh, d)
        body.append(f'<polygon points="{sp}" fill="{side}" stroke="{side}" stroke-width="{s}" stroke-linejoin="round"/>')
        body.append(f'<polygon points="{fp}" fill="{face}" stroke="{face}" stroke-width="{s}" stroke-linejoin="round"/>')
    if not flat:
        defs.append('<linearGradient id="gb" x1="0" y1="0" x2="0.3" y2="1"><stop offset="0" stop-color="#3B6BF0"/><stop offset="1" stop-color="#1B34A8"/></linearGradient>')
        defs.append('<linearGradient id="gk" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7DEBFF"/><stop offset="1" stop-color="#17C1F8"/></linearGradient>')
    defs.append('<mask id="cut" maskUnits="userSpaceOnUse" x="-100" y="-100" width="800" height="800">'
                f'<rect x="-100" y="-100" width="800" height="800" fill="#fff"/>{knock(px, py, sc, gap)}</mask>')
    vb = "30 31 468 468" if not flat else "34 36 456 456"
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" width="512" height="512">\n'
            f'<title>{title}</title>\n<defs>{"".join(defs)}</defs>\n'
            f'<g mask="url(#cut)">{"".join(body)}</g>{penguin(px, py, sc, flat)}\n</svg>\n')


def mono_svg(color, title):
    # Three slabs; top faces solid, side walls at reduced opacity for depth, gaps between layers.
    cx, hw, hh, d, s, gap = 256, 150, 80, 38, 30, 20
    cys = (120, 212, 304)
    defs, body = [], []
    for k, cy in enumerate(cys):
        m = ['<rect x="-100" y="-100" width="800" height="800" fill="#fff"/>']
        for j in range(k):
            sp, _ = slab(cx, cys[j], hw, hh, d)
            m.append(f'<polygon points="{sp}" fill="#000" stroke="#000" stroke-width="{s + gap * 2}" stroke-linejoin="round"/>')
        defs.append(f'<mask id="m{k}" maskUnits="userSpaceOnUse" x="-100" y="-100" width="800" height="800">{"".join(m)}</mask>')
        sp, fp = slab(cx, cy, hw, hh, d)
        body.append(f'<g mask="url(#m{k})">'
                    f'<g opacity="0.55"><polygon points="{sp}" fill="{color}" stroke="{color}" stroke-width="{s}" stroke-linejoin="round"/></g>'
                    f'<polygon points="{fp}" fill="{color}" stroke="{color}" stroke-width="{s}" stroke-linejoin="round"/></g>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="36 6 440 440" width="512" height="512">\n'
            f'<title>{title}</title>\n<defs>{"".join(defs)}</defs>\n{"".join(body)}\n</svg>\n')


def write(name, text):
    with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


write("wslc-desktop-logo.svg", stack_svg(False, "WSLc Desktop logo"))
write("wslc-desktop-logo-small.svg", stack_svg(True, "WSLc Desktop logo (small sizes)"))
write("wslc-desktop-mono-black.svg", mono_svg("#000000", "WSLc Desktop monochrome (for light backgrounds)"))
write("wslc-desktop-mono-white.svg", mono_svg("#FFFFFF", "WSLc Desktop monochrome (for dark backgrounds)"))
