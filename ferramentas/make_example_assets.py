"""Draws the image layers of the example wallpapers in examples/ (silhouettes with transparency).

Deterministic: the same seed draws the same picture, so rerunning only rewrites identical files.
Usage: python ferramentas\\make_example_assets.py
"""
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent / "examples"
WIDTH = 6400   # wide enough that a layer held at a fraction of the height still spans a 21:9 screen


def save(img: Image.Image, example: str, name: str) -> None:
    out = ROOT / example / "assets" / name
    out.parent.mkdir(parents=True, exist_ok=True)
    img.save(out, optimize=True)
    print(out.relative_to(ROOT.parent), img.size)


def ridge(rng: random.Random, width: int, base: float, rough: float, steps: int = 10) -> list[float]:
    """Midpoint displacement: a mountain line, heights in pixels from the top."""
    n = 2 ** steps + 1
    pts = [0.0] * n
    pts[0], pts[-1] = base + rng.uniform(-rough, rough), base + rng.uniform(-rough, rough)
    step, scale = n - 1, rough
    while step > 1:
        half = step // 2
        for i in range(half, n, step):
            pts[i] = (pts[i - half] + pts[i + half]) / 2 + rng.uniform(-scale, scale)
        step, scale = half, scale * 0.55
    out = []
    for x in range(width):
        f = x / (width - 1) * (n - 1)
        i = min(int(f), n - 2)
        out.append(pts[i] + (pts[i + 1] - pts[i]) * (f - i))
    return out


def skyline(seed: int, height: int, color: tuple, lit: tuple, min_h: float, max_h: float, density: float) -> Image.Image:
    rng = random.Random(seed)
    img = Image.new("RGBA", (WIDTH, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x = -rng.randint(0, 60)
    while x < WIDTH:
        w = rng.randint(70, 220)
        h = int(height * rng.uniform(min_h, max_h))
        top = height - h
        d.rectangle([x, top, x + w, height], fill=color)
        if rng.random() < 0.25:   # stepped roof
            iw = int(w * rng.uniform(0.35, 0.7))
            ix = x + (w - iw) // 2
            d.rectangle([ix, top - rng.randint(20, 70), ix + iw, top], fill=color)
        if rng.random() < 0.18:   # antenna
            ax = x + rng.randint(10, w - 10)
            d.rectangle([ax, top - rng.randint(40, 120), ax + 3, top], fill=color)
        # windows: a grid, some of them lit
        # the last rows stay solid: the runtime stretches an edge row past the screen when it pans
        for wy in range(top + 18, height - 44, 26):
            for wx in range(x + 12, x + w - 14, 20):
                if rng.random() < density:
                    a = rng.randint(150, 255)
                    d.rectangle([wx, wy, wx + 8, wy + 12], fill=lit[:3] + (a,))
        x += w + rng.randint(-10, 18)
    return img


def hills(seed: int, height: int, base: float, rough: float, color: tuple) -> Image.Image:
    rng = random.Random(seed)
    line = ridge(rng, WIDTH, base * height, rough * height)
    img = Image.new("RGBA", (WIDTH, height), (0, 0, 0, 0))
    ImageDraw.Draw(img).polygon([(0, height)] + [(x, y) for x, y in enumerate(line)] + [(WIDTH - 1, height)], fill=color)
    return img


def peak_line(rng: random.Random, height: int, top: float, low: float, spacing: tuple) -> list[float]:
    """A range of distinct peaks (each a cone with its own slopes) roughened by small jagged noise."""
    peaks = []
    x = -rng.uniform(0, 300)
    while x < WIDTH + 400:
        peaks.append((x, height * rng.uniform(top, low), rng.uniform(0.45, 0.95), rng.uniform(0.45, 0.95)))
        x += rng.uniform(*spacing)
    noise = ridge(rng, WIDTH, 0, height * 0.025, steps=12)
    line, owner = [], []
    for px in range(WIDTH):
        ys = [py + (px - x0) * (rs if px >= x0 else -ls) for x0, py, ls, rs in peaks]
        k = min(range(len(ys)), key=ys.__getitem__)
        line.append(min(height - 2, ys[k] + noise[px]))
        owner.append(k)
    return line, owner, peaks


def mountains(seed: int, height: int, top: float, low: float, spacing: tuple, color: tuple, shade: tuple,
              snow: tuple, snowline: float) -> Image.Image:
    rng = random.Random(seed)
    line, owner, peaks = peak_line(rng, height, top, low, spacing)
    img = Image.new("RGBA", (WIDTH, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(0, height)] + [(x, y) for x, y in enumerate(line)] + [(WIDTH - 1, height)], fill=color)
    mask = img.split()[3]
    # light from the left: each peak's right face is in shade, split from the lit face by a line from the tip
    dark = Image.new("L", (WIDTH, height), 0)
    dd = ImageDraw.Draw(dark)
    for k, (px, py, _, _) in enumerate(peaks):
        cols = [x for x in range(WIDTH) if owner[x] == k]
        if not cols:
            continue
        x0, x1 = cols[0], cols[-1]
        lean = rng.uniform(0.15, 0.4)
        poly = [(px, py), (x1 + 1, line[x1] if x1 < WIDTH else py), (x1 + 1, height), (px + (height - py) * lean, height)]
        own = Image.new("L", (WIDTH, height), 0)
        ImageDraw.Draw(own).polygon(poly, fill=255)
        strip = Image.new("L", (WIDTH, height), 0)
        ImageDraw.Draw(strip).rectangle([x0, 0, x1, height], fill=255)
        dark.paste(own, (0, 0), strip)
    dark = Image.composite(dark.filter(ImageFilter.GaussianBlur(1.5)), Image.new("L", dark.size, 0), mask)
    img.paste(Image.new("RGBA", (WIDTH, height), shade), (0, 0), dark)
    # snow on what rises above the snow line: it runs down each slope and ends in a ragged edge
    limit = snowline * height
    cap = Image.new("L", (WIDTH, height), 0)
    cd = ImageDraw.Draw(cap)
    walk = 0.0
    for x, y in enumerate(line):
        walk = max(-1.0, min(1.0, walk + rng.uniform(-0.35, 0.35)))
        if y < limit:
            depth = (limit - y) * 0.5 + walk * 16 + rng.uniform(-3, 3)
            if depth > 0:
                cd.line([(x, y), (x, y + depth)], fill=255)
    cap = Image.composite(cap.filter(ImageFilter.GaussianBlur(1.0)), Image.new("L", cap.size, 0), mask)
    img.paste(Image.new("RGBA", (WIDTH, height), snow), (0, 0), cap)
    # the shaded face of the snow too
    snow_dark = Image.composite(dark, Image.new("L", dark.size, 0), cap).point(lambda v: v * 0.55)
    img.paste(Image.new("RGBA", (WIDTH, height), tuple(int(c * 0.82) for c in snow[:3]) + (255,)), (0, 0), snow_dark)
    return img


def pines(seed: int, height: int, color: tuple, min_h: float, max_h: float, spacing: tuple) -> Image.Image:
    rng = random.Random(seed)
    img = Image.new("RGBA", (WIDTH, height), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, height - int(height * 0.12), WIDTH, height], fill=color)
    x = -40
    while x < WIDTH + 40:
        h = height * rng.uniform(min_h, max_h)
        w = h * rng.uniform(0.28, 0.4)
        base = height - height * 0.08 + rng.uniform(-8, 8)
        tiers = rng.randint(4, 6)
        for t in range(tiers):
            ty = base - h * t / tiers
            tw = w * (1 - t / (tiers + 0.6))
            d.polygon([(x - tw / 2, ty), (x + tw / 2, ty), (x, ty - h / tiers * 1.9)], fill=color)
        d.rectangle([x - w * 0.04, base - h * 0.1, x + w * 0.04, height], fill=color)
        x += rng.uniform(*spacing)
    return img


def main() -> None:
    # Rainy night: two rows of buildings, the far one bluer and dimmer
    save(skyline(11, 900, (34, 44, 70, 255), (255, 214, 150), 0.25, 0.65, 0.10), "rainy-night", "city-far.png")
    save(skyline(12, 1000, (9, 12, 22, 255), (255, 196, 120), 0.18, 0.55, 0.16), "rainy-night", "city-near.png")
    # Fireflies: a dusk forest in three depths
    save(hills(21, 700, 0.45, 0.22, (46, 40, 78, 255)), "fireflies", "hills.png")
    save(pines(22, 800, (24, 22, 46, 255), 0.4, 0.7, (60, 130)), "fireflies", "forest-far.png")
    save(pines(23, 900, (8, 8, 16, 255), 0.42, 0.72, (130, 280)), "fireflies", "forest-near.png")
    # Snow: far and near mountains with snowy tops
    save(mountains(31, 1100, 0.06, 0.5, (450, 1000), (112, 130, 164, 255), (84, 100, 136, 255), (226, 234, 246, 255), 0.3),
         "snowfall", "mountains-far.png")
    save(mountains(32, 900, 0.18, 0.55, (650, 1400), (44, 56, 82, 255), (30, 40, 62, 255), (190, 204, 226, 255), 0.36),
         "snowfall", "mountains-near.png")


if __name__ == "__main__":
    main()
