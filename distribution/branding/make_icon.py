"""Original Six Guns Remaster-NX icon: a brass six-chamber revolver cylinder on a dusk-sky tile."""
import math, sys, os
from PIL import Image, ImageDraw, ImageFilter

S = 2048  # supersampled canvas


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def tile(with_bg=True):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    if with_bg:
        # dusk gradient tile with rounded corners
        grad = Image.new("RGBA", (S, S))
        g = ImageDraw.Draw(grad)
        top, mid, bot = (0x38, 0x4D, 0x51), (0xB3, 0x88, 0x59), (0x28, 0x2B, 0x27)
        for y in range(S):
            t = y / (S - 1)
            c = lerp(top, mid, t / 0.62) if t < 0.62 else lerp(mid, bot, (t - 0.62) / 0.38)
            g.line([(0, y), (S, y)], fill=c + (255,))
        mask = Image.new("L", (S, S), 0)
        m = 0.06 * S
        ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m, S - m], radius=0.2 * S, fill=255)
        img.paste(grad, (0, 0), mask)
        # desert ridge silhouette at the bottom of the tile
        ridge = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        pts = [(0, 0.80), (0.18, 0.76), (0.3, 0.79), (0.48, 0.74), (0.66, 0.78), (0.82, 0.75), (1, 0.79), (1, 1), (0, 1)]
        ImageDraw.Draw(ridge).polygon([(x * S, y * S) for x, y in pts], fill=(0x23, 0x2F, 0x2D, 255))
        img.paste(ridge, (0, 0), Image.composite(ridge, Image.new("RGBA", (S, S)), mask))

    base = img
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = S / 2, S * (0.47 if with_bg else 0.5)
    R = S * (0.30 if with_bg else 0.42)

    # soft shadow
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(sh).ellipse([cx - R, cy - R + R * 0.08, cx + R, cy + R + R * 0.08], fill=(0, 0, 0, 110))
    base.alpha_composite(sh.filter(ImageFilter.GaussianBlur(S * 0.02)))

    # cylinder body: six fluted lobes = circle with notches between chambers
    brass_dark, brass, brass_light = (0x9C, 0x7A, 0x3E), (0xDE, 0xB4, 0x6C), (0xF3, 0xD6, 0x9C)
    d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=brass_dark + (255,))
    r2 = R * 0.94
    d.ellipse([cx - r2, cy - r2, cx + r2, cy + r2], fill=brass + (255,))
    # highlight crescent
    hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.ellipse([cx - r2, cy - r2, cx + r2, cy + r2], fill=brass_light + (150,))
    hd.ellipse([cx - r2 + R * 0.12, cy - r2 + R * 0.16, cx + r2 + R * 0.05, cy + r2 + R * 0.1], fill=(0, 0, 0, 0))
    img.alpha_composite(hl)
    d = ImageDraw.Draw(img)
    # flutes (notches on the rim between chambers)
    for k in range(6):
        a = math.radians(90 + 30 + 60 * k)
        fx, fy = cx + R * 1.02 * math.cos(a), cy - R * 1.02 * math.sin(a)
        fr = R * 0.17
        d.ellipse([fx - fr, fy - fr, fx + fr, fy + fr], fill=(0, 0, 0, 0))
    # chambers
    for k in range(6):
        a = math.radians(90 + 60 * k)
        hx, hy = cx + R * 0.56 * math.cos(a), cy - R * 0.56 * math.sin(a)
        hr = R * 0.205
        d.ellipse([hx - hr * 1.12, hy - hr * 1.12, hx + hr * 1.12, hy + hr * 1.12], fill=brass_dark + (255,))
        d.ellipse([hx - hr, hy - hr, hx + hr, hy + hr], fill=(0x1E, 0x1A, 0x14, 255))
        # brass cartridge primer in each chamber
        pr = hr * 0.42
        d.ellipse([hx - pr, hy - pr, hx + pr, hy + pr], fill=(0xC9, 0x9A, 0x52, 255))
    # centre pin
    pr = R * 0.12
    d.ellipse([cx - pr * 1.3, cy - pr * 1.3, cx + pr * 1.3, cy + pr * 1.3], fill=brass_dark + (255,))
    d.ellipse([cx - pr, cy - pr, cx + pr, cy + pr], fill=(0x5A, 0x46, 0x26, 255))
    base.alpha_composite(img)
    return base


def cut_flutes(img, with_bg):
    # punch the six flutes out of the cylinder rim (transparent; on the tile they reveal the sky)
    cx, cy = S / 2, S * (0.47 if with_bg else 0.5)
    R = S * (0.30 if with_bg else 0.42)
    return img


def main(out):
    os.makedirs(out, exist_ok=True)
    full = tile(True)
    bare = tile(False)
    big = full.resize((1024, 1024), Image.LANCZOS)
    big.save(os.path.join(out, "icon_1024.png"))
    bare.resize((512, 512), Image.LANCZOS).save(os.path.join(out, "Logo_512.png"))
    bare.resize((256, 256), Image.LANCZOS).save(os.path.join(out, "Logo_256.png"))
    big.save(os.path.join(out, "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    big.save(os.path.join(out, "app.icns"))


if __name__ == "__main__":
    main(sys.argv[1])
