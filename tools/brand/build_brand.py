#!/usr/bin/env python3
"""
Genera l'identità visiva di All Sky Brain: un'unica fonte per tutti gli asset di marchio.

Uscite (percorsi dalla radice del repository):
  assets/brand/allsky-brain-mark.svg   marchio senza sfondo
  assets/brand/allsky-brain-icon.svg   icona con sfondo nero arrotondato
  assets/brand/allsky-brain.ico        icona multi-risoluzione 16, 24, 32, 48, 64, 128, 256 px
  src/AllSky.Desktop/Theme/Brand.xaml  geometrie del marchio (StreamGeometry) per il tema WPF

Il disegno è definito una volta sola su una griglia di 64 unità, con sole primitive semplici
(cerchi, segmenti, un poligono a stella). SVG, ICO e XAML leggono le stesse coordinate, quindi
non possono divergere. Le icone fino a 32 px omettono l'anello interno e l'orizzonte, che a quelle
dimensioni diventerebbero sfocati.

Uso:
  python3 tools/brand/build_brand.py           rigenera gli asset
  python3 tools/brand/build_brand.py --check   esce con 1 se gli asset committati non corrispondono

Dipendenze: Python 3 con Pillow (testato con Pillow 12.3.0). Output deterministico: stessa versione
di Pillow, stessi byte. Nessuna immagine di terzi: tutte le forme sono geometria scritta qui.
"""

from __future__ import annotations

import io
import math
import struct
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SVG_MARK = ROOT / "assets" / "brand" / "allsky-brain-mark.svg"
SVG_ICON = ROOT / "assets" / "brand" / "allsky-brain-icon.svg"
ICO_FILE = ROOT / "assets" / "brand" / "allsky-brain.ico"
XAML_FILE = ROOT / "src" / "AllSky.Desktop" / "Theme" / "Brand.xaml"

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]
SIMPLIFIED_MAX_SIZE = 32  # fino a questa dimensione: niente anello interno né orizzonte
SUPERSAMPLE = 8           # ogni icona si disegna a 8x e poi si riduce con LANCZOS

# Palette (docs/ralph/IMPLEMENTATION_PLAN.md §5.3, valori del tema scuro).
BLACK = (0, 0, 0)
BORDER = (0x18, 0x22, 0x33)
ACCENT = (0x5C, 0xC8, 0xFF)

GRID = 64.0
LENS_CENTER = (32.0, 32.0)
LENS_R = 21.0
INNER_R = 13.0
HORIZON_Y = 42.0
STAR_CENTER = (36.0, 25.0)
STAR_OUTER = 5.5
STAR_INNER = 1.2
STAR_OUTER_SIMPLE = 7.5
STAR_INNER_SIMPLE = 1.8

LENS_WIDTH = 2.6
LENS_WIDTH_SIMPLE = 4.0
INNER_WIDTH = 1.2
INNER_OPACITY = 0.45
HORIZON_WIDTH = 1.4
HORIZON_OPACITY = 0.7


def _horizon_ends() -> tuple[float, float]:
    """Punti in cui l'orizzonte incontra l'anello esterno (corda sotto il centro)."""
    dy = HORIZON_Y - LENS_CENTER[1]
    dx = math.sqrt(LENS_R * LENS_R - dy * dy)
    return LENS_CENTER[0] - dx, LENS_CENTER[0] + dx


def _star_points(cx: float, cy: float, outer: float, inner: float) -> list[tuple[float, float]]:
    """Stella a 4 punte: punte lunghe sugli assi, punte corte in diagonale. Partenza dall'alto, in senso orario."""
    pts = []
    for k in range(8):
        angle = math.radians(-90 + 45 * k)
        r = outer if k % 2 == 0 else inner
        pts.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    return pts


def _blend(fg: tuple[int, int, int], alpha: float, bg: tuple[int, int, int] = BLACK) -> tuple[int, int, int]:
    return tuple(round(f * alpha + b * (1 - alpha)) for f, b in zip(fg, bg))


def _hex(rgb: tuple[int, int, int]) -> str:
    return "#{:02X}{:02X}{:02X}".format(*rgb)


def _fmt(value: float) -> str:
    text = f"{value:.3f}".rstrip("0").rstrip(".")
    return "0" if text in ("-0", "") else text


# --- Rendering raster ---------------------------------------------------------------------------

def render_icon(size: int) -> Image.Image:
    """Icona con sfondo nero arrotondato, disegnata a SUPERSAMPLE volte la dimensione finale."""
    simplified = size <= SIMPLIFIED_MAX_SIZE
    canvas_px = size * SUPERSAMPLE
    scale = canvas_px / GRID
    img = Image.new("RGBA", (canvas_px, canvas_px), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    inset = 0.5
    draw.rounded_rectangle(
        [inset * scale, inset * scale, (GRID - inset) * scale - 1, (GRID - inset) * scale - 1],
        radius=(14 - inset) * scale,
        fill=BLACK + (255,),
        outline=BORDER + (255,),
        width=max(1, round(1.0 * scale)),
    )

    def ring(cx: float, cy: float, r: float, width: float, color: tuple[int, int, int]) -> None:
        half = width / 2
        bbox = [(cx - r - half) * scale, (cy - r - half) * scale,
                (cx + r + half) * scale, (cy + r + half) * scale]
        draw.ellipse(bbox, outline=color + (255,), width=round(width * scale))

    def segment(p1: tuple[float, float], p2: tuple[float, float], width: float,
                color: tuple[int, int, int]) -> None:
        w = width * scale
        draw.line([(p1[0] * scale, p1[1] * scale), (p2[0] * scale, p2[1] * scale)],
                  fill=color + (255,), width=round(w))
        for (x, y) in (p1, p2):  # estremità tonde
            r = w / 2
            draw.ellipse([x * scale - r, y * scale - r, x * scale + r, y * scale + r], fill=color + (255,))

    if simplified:
        ring(LENS_CENTER[0], LENS_CENTER[1], LENS_R, LENS_WIDTH_SIMPLE, ACCENT)
        star = _star_points(STAR_CENTER[0], STAR_CENTER[1], STAR_OUTER_SIMPLE, STAR_INNER_SIMPLE)
    else:
        ring(LENS_CENTER[0], LENS_CENTER[1], LENS_R, LENS_WIDTH, ACCENT)
        ring(LENS_CENTER[0], LENS_CENTER[1], INNER_R, INNER_WIDTH, _blend(ACCENT, INNER_OPACITY))
        x1, x2 = _horizon_ends()
        segment((x1, HORIZON_Y), (x2, HORIZON_Y), HORIZON_WIDTH, _blend(ACCENT, HORIZON_OPACITY))
        star = _star_points(STAR_CENTER[0], STAR_CENTER[1], STAR_OUTER, STAR_INNER)

    draw.polygon([(x * scale, y * scale) for x, y in star], fill=ACCENT + (255,))
    return img.resize((size, size), Image.LANCZOS)


# --- ICO ----------------------------------------------------------------------------------------

def _dib_entry(img: Image.Image) -> bytes:
    """Immagine BMP (DIB) a 32 bit con maschera AND vuota: il canale alfa governa la trasparenza."""
    w, h = img.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = img.tobytes("raw", "BGRA")
    # DIB: righe dal basso verso l'alto.
    xor = b"".join(rows[y * w * 4:(y + 1) * w * 4] for y in reversed(range(h)))
    and_row = ((w + 31) // 32) * 4
    and_mask = bytes(and_row * h)
    return header + xor + and_mask


def _png_entry(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def build_ico() -> bytes:
    images = [render_icon(size) for size in ICO_SIZES]
    # Il 256 px in PNG (come fanno gli strumenti standard); gli altri in DIB, leggibili da ogni versione di Windows.
    blobs = [(_png_entry(img) if img.size[0] >= 256 else _dib_entry(img)) for img in images]
    count = len(ICO_SIZES)
    out = bytearray(struct.pack("<HHH", 0, 1, count))
    offset = 6 + 16 * count
    for size, blob in zip(ICO_SIZES, blobs):
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    for blob in blobs:
        out += blob
    return bytes(out)


# --- SVG ----------------------------------------------------------------------------------------

def build_svg(with_background: bool) -> str:
    x1, x2 = _horizon_ends()
    star = _star_points(STAR_CENTER[0], STAR_CENTER[1], STAR_OUTER, STAR_INNER)
    star_points = " ".join(f"{_fmt(x)},{_fmt(y)}" for x, y in star)
    accent = _hex(ACCENT)
    lines = [
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">',
        "  <title>All Sky Brain</title>",
    ]
    if with_background:
        lines.append(
            f'  <rect x="0.5" y="0.5" width="63" height="63" rx="13.5" fill="{_hex(BLACK)}" '
            f'stroke="{_hex(BORDER)}" stroke-width="1"/>'
        )
    lines += [
        f'  <circle cx="{_fmt(LENS_CENTER[0])}" cy="{_fmt(LENS_CENTER[1])}" r="{_fmt(LENS_R)}" fill="none" '
        f'stroke="{accent}" stroke-width="{_fmt(LENS_WIDTH)}"/>',
        f'  <circle cx="{_fmt(LENS_CENTER[0])}" cy="{_fmt(LENS_CENTER[1])}" r="{_fmt(INNER_R)}" fill="none" '
        f'stroke="{accent}" stroke-width="{_fmt(INNER_WIDTH)}" stroke-opacity="{_fmt(INNER_OPACITY)}"/>',
        f'  <line x1="{_fmt(x1)}" y1="{_fmt(HORIZON_Y)}" x2="{_fmt(x2)}" y2="{_fmt(HORIZON_Y)}" '
        f'stroke="{accent}" stroke-width="{_fmt(HORIZON_WIDTH)}" stroke-linecap="round" '
        f'stroke-opacity="{_fmt(HORIZON_OPACITY)}"/>',
        f'  <polygon points="{star_points}" fill="{accent}"/>',
        "</svg>",
        "",
    ]
    return "\n".join(lines)


# --- XAML ---------------------------------------------------------------------------------------

def _circle_path(cx: float, cy: float, r: float) -> str:
    return (f"M {_fmt(cx - r)},{_fmt(cy)} A {_fmt(r)},{_fmt(r)} 0 1 0 {_fmt(cx + r)},{_fmt(cy)} "
            f"A {_fmt(r)},{_fmt(r)} 0 1 0 {_fmt(cx - r)},{_fmt(cy)} Z")


def build_xaml() -> str:
    x1, x2 = _horizon_ends()
    star = _star_points(STAR_CENTER[0], STAR_CENTER[1], STAR_OUTER, STAR_INNER)
    star_data = "M " + " L ".join(f"{_fmt(x)},{_fmt(y)}" for x, y in star) + " Z"
    horizon = f"M {_fmt(x1)},{_fmt(HORIZON_Y)} L {_fmt(x2)},{_fmt(HORIZON_Y)}"
    return "\n".join([
        '<!-- Generato da tools/brand/build_brand.py: non modificare a mano. -->',
        '<!-- Griglia 64 unità. Stroke: Lens 2.6 (4.0 sotto 32 px), Inner 1.2 con Opacity 0.45, Horizon 1.4 con',
        '     Opacity 0.7 e StrokeStartLineCap/EndLineCap Round. Star è solo riempimento. -->',
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        f'  <StreamGeometry x:Key="Brand.Lens">F1 {_circle_path(LENS_CENTER[0], LENS_CENTER[1], LENS_R)}</StreamGeometry>',
        f'  <StreamGeometry x:Key="Brand.Inner">F1 {_circle_path(LENS_CENTER[0], LENS_CENTER[1], INNER_R)}</StreamGeometry>',
        f'  <StreamGeometry x:Key="Brand.Horizon">F1 {horizon}</StreamGeometry>',
        f'  <StreamGeometry x:Key="Brand.Star">F1 {star_data}</StreamGeometry>',
        "</ResourceDictionary>",
        "",
    ])


# --- Main ---------------------------------------------------------------------------------------

def outputs() -> dict[Path, bytes]:
    return {
        SVG_MARK: build_svg(with_background=False).encode("utf-8"),
        SVG_ICON: build_svg(with_background=True).encode("utf-8"),
        ICO_FILE: build_ico(),
        XAML_FILE: build_xaml().encode("utf-8"),
    }


def main(argv: list[str]) -> int:
    expected = outputs()
    if "--check" in argv:
        stale = [p for p, data in expected.items() if not p.exists() or p.read_bytes() != data]
        for p in stale:
            print(f"Non aggiornato: {p.relative_to(ROOT)}")
        if stale:
            print("Eseguire: python3 tools/brand/build_brand.py")
            return 1
        print("Asset di marchio aggiornati.")
        return 0

    for path, data in expected.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        print(f"Scritto {path.relative_to(ROOT)} ({len(data)} byte)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
