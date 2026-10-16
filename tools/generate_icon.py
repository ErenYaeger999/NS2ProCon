"""Export our original SVG as PNG and optically adjusted Windows ICO entries.

Only Pillow is required. This renderer supports the SVG's small absolute-path
subset without a browser, installed fonts, or an external image service.
"""
from io import BytesIO
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/NS2Pro.App/Assets"
SVG = ET.parse(ASSETS / "App.svg").getroot()
SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
RED = SVG.find("{http://www.w3.org/2000/svg}rect").attrib["fill"]


def polygon(path):
    """Flatten the SVG's absolute M/L/H/V/C/Z commands for supersampled fills."""
    tokens = re.findall(r"[MLHVCZ]|-?\d+(?:\.\d+)?", path)
    points, cursor, start = [], (0.0, 0.0), (0.0, 0.0)
    i = 0
    while i < len(tokens):
        command = tokens[i]
        i += 1
        count = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "Z": 0}[command]
        values = [float(v) for v in tokens[i:i + count]]
        i += count
        if command == "C":
            a, b, end = (values[0], values[1]), (values[2], values[3]), (values[4], values[5])
            x0, y0 = cursor
            for step in range(1, 49):
                t, u = step / 48, 1 - step / 48
                points.append((u**3*x0 + 3*u*u*t*a[0] + 3*u*t*t*b[0] + t**3*end[0],
                               u**3*y0 + 3*u*u*t*a[1] + 3*u*t*t*b[1] + t**3*end[1]))
            cursor = end
            continue
        if command in ("M", "L"):
            cursor = tuple(values)
            if command == "M":
                start = cursor
        elif command == "H":
            cursor = (values[0], cursor[1])
        elif command == "V":
            cursor = (cursor[0], values[0])
        else:
            cursor = start
        points.append(cursor)
    return points


def render(size):
    sampling = 4
    image = Image.new("RGBA", (size * sampling, size * sampling))
    draw = ImageDraw.Draw(image)
    scale = size * sampling / 1024
    compact = size < 32

    def visit(element, inherited="#000000"):
        if compact and element.get("id") in ("menu", "dpad", "windows"):
            return
        fill = element.get("fill", inherited)
        kind = element.tag.split("}")[-1]
        if kind == "path":
            draw.polygon([(x * scale, y * scale) for x, y in polygon(element.attrib["d"])], fill=fill)
        elif kind == "rect":
            x, y, width, height = (float(element.get(k, 0)) for k in ("x", "y", "width", "height"))
            box = (x * scale, y * scale, (x + width) * scale, (y + height) * scale)
            draw.rounded_rectangle(box, radius=float(element.get("rx", 0)) * scale, fill=fill)
        for child in element:
            visit(child, fill)

    visit(SVG)
    image = image.resize((size, size), Image.Resampling.LANCZOS)
    if compact:
        # Whole-pixel controls stay clear; tiny function keys are omitted at these sizes.
        draw = ImageDraw.Draw(image)
        cx, cy = round(size * 322 / 1024), round(size * 500 / 1024)
        draw.rectangle((cx - 1, cy, cx + 1, cy), fill=RED)
        draw.rectangle((cx, cy - 1, cx, cy + 1), fill=RED)
        cx = round(size * 712 / 1024)
        for x in (cx - 1, cx + 1):
            for y in (cy - 1, cy + 1):
                draw.point((x, y), fill=RED)
    return image


def main():
    images = {size: render(size) for size in SIZES}
    images[256].save(ASSETS / "App.png")
    # PNG entries retain each size's own optical adjustments instead of rescaling one frame.
    chunks = []
    for size in SIZES:
        stream = BytesIO()
        images[size].save(stream, format="PNG")
        chunks.append(stream.getvalue())
    offset = 6 + 16 * len(SIZES)
    entries = []
    for size, chunk in zip(SIZES, chunks):
        entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(chunk), offset))
        offset += len(chunk)
    (ASSETS / "App.ico").write_bytes(struct.pack("<HHH", 0, 1, len(SIZES)) + b"".join(entries) + b"".join(chunks))
    with Image.open(ASSETS / "App.ico") as icon:
        assert icon.ico.sizes() == {(size, size) for size in SIZES}
    print(ASSETS / "App.ico")


if __name__ == "__main__":
    main()
