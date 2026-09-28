"""Generate icon.png for the Retro Stretch Jellyfin plugin.

Retro CRT motif: a TV set whose screen shows a dashed 4:3 frame being
stretched to the full 16:9 width by outward amber arrows, with phosphor
green glow and scanlines. Drawn at 4x supersampling for smooth edges.

Run:  python make_icon.py
Out:  icon.png (512x512 RGBA)
"""

from PIL import Image, ImageDraw, ImageFilter

S = 4                      # supersample factor
W = 512 * S                # canvas size in device pixels


def vgrad(w, h, top, bottom):
    """Vertical gradient image between two RGB colors."""
    grad = Image.new("RGBA", (1, h))
    for y in range(h):
        t = y / max(h - 1, 1)
        r = round(top[0] + (bottom[0] - top[0]) * t)
        g = round(top[1] + (bottom[1] - top[1]) * t)
        b = round(top[2] + (bottom[2] - top[2]) * t)
        grad.putpixel((0, y), (r, g, b, 255))
    return grad.resize((w, h))


def rounded_mask(w, h, radius):
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, fill=255)
    return m


# Palette
BG_TOP = (30, 30, 50)       # deep indigo
BG_BOT = (12, 12, 22)       # near-black
TV_BODY = (48, 48, 78)
TV_EDGE = (86, 86, 130)
PHOSPHOR = (57, 255, 136)   # CRT green
AMBER = (255, 179, 64)      # stretch arrows

# Base: gradient clipped to a rounded square.
base = Image.new("RGBA", (W, W), (0, 0, 0, 0))
base.paste(vgrad(W, W, BG_TOP, BG_BOT), (0, 0), rounded_mask(W, W, 96 * S))

# --- Artwork layer (crisp) + glow layer (blurred under it) ---
art = Image.new("RGBA", (W, W), (0, 0, 0, 0))
glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
da = ImageDraw.Draw(art)
dg = ImageDraw.Draw(glow)


def rrect(xy, r, fill, outline=None, width=1):
    da.rounded_rectangle(xy, radius=r, fill=fill, outline=outline, width=width)


def line(pts, fill, width, glow_too=True):
    da.line(pts, fill=fill, width=width, joint="curve")
    if glow_too:
        dg.line(pts, fill=fill, width=width, joint="curve")
    # round caps
    for x, y in (pts[0], pts[-1]):
        r = width / 2
        da.ellipse([x - r, y - r, x + r, y + r], fill=fill)
        if glow_too:
            dg.ellipse([x - r, y - r, x + r, y + r], fill=fill)


# Antenna (V of thick lines with round caps)
line([(214 * S, 148 * S), (150 * S, 78 * S)], TV_EDGE, 9 * S, glow_too=False)
line([(298 * S, 148 * S), (362 * S, 78 * S)], TV_EDGE, 9 * S, glow_too=False)

# TV body (16:9-ish)
rrect([64 * S, 148 * S, 448 * S, 404 * S], 26 * S, TV_BODY, TV_EDGE, 3 * S)

# Screen inset
rrect([86 * S, 170 * S, 426 * S, 382 * S], 16 * S, (4, 16, 9))

# Screen glow border (drawn on glow layer only)
dg.rounded_rectangle([86 * S, 170 * S, 426 * S, 382 * S], radius=16 * S,
                     outline=PHOSPHOR, width=5 * S)

# Scanlines across the screen
for y in range(174 * S, 382 * S, 7 * S):
    da.line([(88 * S, y), (424 * S, y)], fill=(0, 0, 0, 70), width=2 * S)

# Dashed 4:3 frame centered on screen: 200x150
x0, y0, x1, y1 = 156 * S, 186 * S, 356 * S, 336 * S
seg, gap = 14 * S, 8 * S


def dashed_edge(a, b, vertical=False):
    if vertical:
        lo, hi = a[1], b[1]
        p = lo
        while p < hi:
            q = min(p + seg, hi)
            da.line([(a[0], p), (a[0], q)], fill=PHOSPHOR, width=4 * S)
            dg.line([(a[0], p), (a[0], q)], fill=PHOSPHOR, width=4 * S)
            p = q + gap
    else:
        lo, hi = a[0], b[0]
        p = lo
        while p < hi:
            q = min(p + seg, hi)
            da.line([(p, a[1]), (q, a[1])], fill=PHOSPHOR, width=4 * S)
            dg.line([(p, a[1]), (q, a[1])], fill=PHOSPHOR, width=4 * S)
            p = q + gap


dashed_edge((x0, y0), (x1, y0))                      # top
dashed_edge((x0, y1), (x1, y1))                      # bottom
dashed_edge((x0, y0), (x0, y1), vertical=True)       # left
dashed_edge((x1, y0), (x1, y1), vertical=True)       # right

# Stretch arrows: double-headed, spanning past the 4:3 frame to the screen edge
ay = 261 * S
ax0, ax1 = 106 * S, 406 * S
line([(ax0, ay), (ax1, ay)], AMBER, 9 * S)
for tip, dx in ((ax0, 1), (ax1, -1)):                # arrowheads
    da.polygon([(tip, ay),
                (tip - dx * 26 * S, ay - 15 * S),
                (tip - dx * 26 * S, ay + 15 * S)], fill=AMBER)
    dg.polygon([(tip, ay),
                (tip - dx * 26 * S, ay - 15 * S),
                (tip - dx * 26 * S, ay + 15 * S)], fill=AMBER)

# TV feet
rrect([132 * S, 404 * S, 176 * S, 424 * S], 8 * S, TV_EDGE)
rrect([336 * S, 404 * S, 380 * S, 424 * S], 8 * S, TV_EDGE)

# Composite: base, then blurred glow, then crisp art.
out = base.copy()
out.alpha_composite(glow.filter(ImageFilter.GaussianBlur(10 * S)))
out.alpha_composite(glow.filter(ImageFilter.GaussianBlur(3 * S)))
out.alpha_composite(art)
out = out.resize((512, 512), Image.LANCZOS)
out.save("icon.png")
print("wrote icon.png", out.size)
