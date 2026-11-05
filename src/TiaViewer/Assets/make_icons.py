"""Draws the app icon and writes it for each platform: icon.ico, icon.icns and icon.png.

Run it from anywhere after changing the drawing: python make_icons.py (needs Pillow). The files it writes are committed.
"""
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
SUPERSAMPLE = 8

TOP = (58, 123, 255)
BOTTOM = (24, 62, 178)
INK = (255, 255, 255, 255)
LENS = (255, 255, 255, 40)
COIL = (255, 190, 60, 255)


def draw(size, margin):
    """One icon: a magnifying glass over a rung of ladder logic. Small sizes drop what would not show."""
    n = size * SUPERSAMPLE
    box = round(n * margin)
    side = n - 2 * box

    gradient = Image.new("RGBA", (1, 256))
    for y in range(256):
        t = y / 255
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)) + (255,))
    gradient = gradient.resize((side, side), Image.BICUBIC)
    mask = Image.new("L", (side, side), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, side - 1, side - 1), radius=round(side * 0.225), fill=255)
    image = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    image.paste(gradient, (box, box), mask)

    def at(x, y):
        return box + side * x, box + side * y

    layer = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    pen = ImageDraw.Draw(layer)
    small = size < 32
    cx, cy, radius = 0.45, 0.43, 0.27
    ring = side * (0.085 if small else 0.062)

    x0, y0 = at(cx - radius, cy - radius)
    x1, y1 = at(cx + radius, cy + radius)
    pen.ellipse((x0, y0, x1, y1), fill=LENS)

    # The ladder, inside the lens: two rails, two rungs, a contact and a coil.
    line = round(side * (0.075 if small else 0.036))
    left, right = cx - radius * 0.56, cx + radius * 0.56
    rail = radius * 0.56
    upper, lower = cy - radius * 0.3, cy + radius * 0.3
    if small:
        # Too few pixels for a ladder: the coil alone keeps the colour.
        coil = radius * 0.42
        pen.ellipse((*at(cx - coil, cy - coil), *at(cx + coil, cy + coil)), fill=COIL)
    else:
        pen.line((*at(left, cy - rail), *at(left, cy + rail)), fill=INK, width=line)
        pen.line((*at(right, cy - rail), *at(right, cy + rail)), fill=INK, width=line)
        middle = cx - radius * 0.1
        gap = radius * 0.11
        pen.line((*at(left, upper), *at(middle - gap, upper)), fill=INK, width=line)
        pen.line((*at(middle + gap, upper), *at(right, upper)), fill=INK, width=line)
        tall = radius * 0.2
        pen.line((*at(middle - gap, upper - tall), *at(middle - gap, upper + tall)), fill=INK, width=line)
        pen.line((*at(middle + gap, upper - tall), *at(middle + gap, upper + tall)), fill=INK, width=line)
        pen.line((*at(left, lower), *at(right, lower)), fill=INK, width=line)
        coil = radius * 0.17
        pen.ellipse((*at(middle + 0.05 - coil, lower - coil), *at(middle + 0.05 + coil, lower + coil)), fill=COIL)

    pen.ellipse((x0, y0, x1, y1), outline=INK, width=round(ring))
    # The handle starts on the ring, at 45 degrees.
    start = cx + radius * 0.74, cy + radius * 0.74
    handle = round(side * (0.12 if small else 0.095))
    pen.line((*at(*start), *at(0.81, 0.79)), fill=INK, width=handle)
    ex, ey = at(0.81, 0.79)
    pen.ellipse((ex - handle / 2, ey - handle / 2, ex + handle / 2, ey + handle / 2), fill=INK)

    image.alpha_composite(layer)
    return image.resize((size, size), Image.LANCZOS)


def main():
    # Windows draws the icon as it is; macOS expects room around it.
    windows = [draw(size, 0.03) for size in (16, 24, 32, 48, 64, 128, 256)]
    windows[-1].save(HERE / "icon.ico", format="ICO", append_images=windows[:-1], sizes=[image.size for image in windows])
    draw(256, 0.03).save(HERE / "icon.png", optimize=True)
    mac = draw(1024, 0.098)
    mac.save(HERE / "icon.icns", format="ICNS", append_images=[draw(size, 0.098) for size in (16, 32, 64, 128, 256, 512)])


if __name__ == "__main__":
    main()
