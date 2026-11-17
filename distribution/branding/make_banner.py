"""Release banner for Six Guns Remaster-NX: original artwork only (no game assets)."""
import math, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H, SS = 1280, 640, 2
w, h = W * SS, H * SS
F = "C:/Windows/Fonts/"
GOLD, CREAM, MUTED = (0xE8, 0xC8, 0x94), (0xEC, 0xE5, 0xD7), (0xB9, 0xC2, 0xBC)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * max(0, min(1, t))) for i in range(3))


def font(name, size):
    return ImageFont.truetype(F + name, size * SS)


img = Image.new("RGB", (w, h))
d = ImageDraw.Draw(img)
# dusk sky
top, mid, low = (0x2E, 0x40, 0x45), (0xC0, 0x8E, 0x5A), (0x3A, 0x33, 0x2A)
for y in range(h):
    t = y / h
    d.line([(0, y), (w, y)], fill=lerp(top, mid, t / 0.7) if t < 0.7 else lerp(mid, low, (t - 0.7) / 0.3))

# sun with glow
glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
sx, sy, sr = int(w * 0.78), int(h * 0.60), int(h * 0.17)
ImageDraw.Draw(glow).ellipse([sx - sr * 2, sy - sr * 2, sx + sr * 2, sy + sr * 2], fill=(0xF3, 0xC9, 0x80, 90))
img.paste(glow.filter(ImageFilter.GaussianBlur(sr * 0.8)), (0, 0), glow.filter(ImageFilter.GaussianBlur(sr * 0.8)))
d = ImageDraw.Draw(img)
d.ellipse([sx - sr, sy - sr, sx + sr, sy + sr], fill=(0xEC, 0xC2, 0x7E))


def poly(points, color):
    d.polygon([(x * w, y * h) for x, y in points], fill=color)


# mesas and ridges (back to front)
poly([(0.28, 0.80), (0.40, 0.73), (0.52, 0.66), (0.56, 0.55), (0.66, 0.55), (0.69, 0.64), (0.80, 0.68), (0.86, 0.58), (0.94, 0.58),
      (0.97, 0.66), (1, 0.67), (1, 1), (0.28, 1)], (0x7A, 0x66, 0x4E))
poly([(0, 0.80), (0.2, 0.76), (0.35, 0.79), (0.55, 0.73), (0.7, 0.78), (0.85, 0.74), (1, 0.77), (1, 1), (0, 1)], (0x4A, 0x4A, 0x3C))
d.rectangle([0, int(h * 0.88), w, h], fill=(0x24, 0x2D, 0x2A))
for i in range(int(h * 0.82), int(h * 0.88)):
    pass
poly([(0, 0.88), (0.25, 0.84), (0.5, 0.88), (0.75, 0.85), (1, 0.88), (1, 1), (0, 1)], (0x24, 0x2D, 0x2A))


def cactus(x, base, s, color):
    def r(x0, y0, x1, y1):
        d.rounded_rectangle([x + x0 * s, base - y1 * s, x + x1 * s, base - y0 * s], radius=int(9 * s), fill=color)
    r(-10, 0, 10, 120)
    r(-42, 52, -26, 96); r(-42, 52, -4, 66)
    r(26, 66, 42, 108); r(4, 66, 42, 80)


cactus(w * 0.90, h * 0.90, SS * 1.25, (0x1A, 0x24, 0x22))
cactus(w * 0.63, h * 0.86, SS * 0.7, (0x2A, 0x33, 0x2E))

# readability scrim on the left
scrim = Image.new("L", (w, h), 0)
sd = ImageDraw.Draw(scrim)
for x in range(w):
    a = int(170 * max(0, 1 - x / (w * 0.62)) ** 1.3)
    sd.line([(x, 0), (x, h)], fill=a)
img.paste((0x10, 0x15, 0x17), (0, 0), scrim)
d = ImageDraw.Draw(img)

# emblem (same design as the app icon)
emb = Image.open(sys.argv[1]).convert("RGBA").resize((118 * SS, 118 * SS), Image.LANCZOS)
x0, y0 = 72 * SS, 70 * SS
img.paste(emb, (x0, y0), emb)
d = ImageDraw.Draw(img)


def spaced(xy, text, f, fill, spacing):
    x, y = xy
    for ch in text:
        d.text((x, y), ch, font=f, fill=fill)
        x += d.textlength(ch, font=f) + spacing * SS


spaced((x0 + 140 * SS, y0 + 30 * SS), "THE FRONTIER, ON DESKTOP", font("bahnschrift.ttf", 17), GOLD, 3.5)
spaced((x0 + 140 * SS, y0 + 60 * SS), "Windows  ·  macOS (Apple Silicon & Intel)", font("bahnschrift.ttf", 17), MUTED, 0.5)

title = font("seguibl.ttf", 104)
ty = 205 * SS
shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
ImageDraw.Draw(shadow).text((x0 + 4 * SS, ty + 6 * SS), "SIX GUNS", font=title, fill=(0, 0, 0, 150))
shadow = shadow.filter(ImageFilter.GaussianBlur(6 * SS))
img.paste(shadow, (0, 0), shadow)
d = ImageDraw.Draw(img)
d.text((x0, ty), "SIX GUNS", font=title, fill=CREAM)
spaced((x0 + 6 * SS, ty + 140 * SS), "REMASTER — NX", font("bahnschrift.ttf", 30), GOLD, 9)
d.rectangle([x0 + 6 * SS, ty + 196 * SS, x0 + 62 * SS, ty + 199 * SS], fill=GOLD)

# feature chips
chips = ["Full controller support", "LAN multiplayer + server browser", "Mods & custom maps"]
cf = font("segoeuib.ttf", 17)
cx, cy = x0, ty + 222 * SS
for c in chips:
    tw = d.textlength(c, font=cf)
    d.rounded_rectangle([cx, cy, cx + tw + 32 * SS, cy + 38 * SS], radius=19 * SS, fill=(0x12, 0x17, 0x1A), outline=(0x9C, 0x7A, 0x3E), width=SS * 2)
    d.text((cx + 16 * SS, cy + 7 * SS), c, font=cf, fill=CREAM)
    cx += tw + 44 * SS

d.text((x0, h - 52 * SS), "Unofficial fan project · Six Guns © Gameloft · bring your own Switch keys, firmware and official APK/OBB",
       font=font("bahnschrift.ttf", 14), fill=(0xC9, 0xC2, 0xB2))

out = img.resize((W, H), Image.LANCZOS)
out.save(sys.argv[2], optimize=True)
print("saved", sys.argv[2])
