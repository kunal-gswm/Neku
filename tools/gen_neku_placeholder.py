"""
Generates a placeholder sprite sheet for Neku.
128x128 pixel cells arranged in a 4-column grid.
Distinctive teal/cyan colored shape with frame number text.

Uses Pillow (PIL) when available, with a pure-Python (struct + zlib) fallback.
"""

import os
import sys
import argparse

# Grid configuration
CELL_SIZE = 128
COLS = 4
ROWS = 12  # 48 frames (0-47), fully covers frames 0-43 defined in manifest.json
TOTAL_FRAMES = ROWS * COLS
SHEET_WIDTH = COLS * CELL_SIZE
SHEET_HEIGHT = ROWS * CELL_SIZE

# Palette: distinctive teal / cyan theme
BG_COLOR = (0, 0, 0, 0)                  # Transparent
TEAL_FILL = (14, 185, 180, 255)          # Bright Teal (#0eb9b4)
TEAL_OUTLINE = (15, 118, 110, 255)       # Deep Teal (#0f766e)
TEAL_HIGHLIGHT = (165, 243, 252, 200)    # Soft Cyan highlight (#a5f3fc)
EAR_INNER = (204, 251, 241, 255)         # Pale Mint ear interior (#ccfbf1)
TEXT_COLOR = (255, 255, 255, 255)        # Crisp White
TEXT_SHADOW = (15, 23, 42, 220)          # Slate navy shadow (#0f172a)


def draw_with_pillow(output_path: str):
    from PIL import Image, ImageDraw, ImageFont

    sheet = Image.new("RGBA", (SHEET_WIDTH, SHEET_HEIGHT), BG_COLOR)
    draw = ImageDraw.Draw(sheet)

    # Attempt to load a clear TrueType font, otherwise fall back to default
    font = None
    for font_candidate in ["arial.ttf", "segoeui.ttf", "calibri.ttf"]:
        try:
            font = ImageFont.truetype(font_candidate, 30)
            break
        except Exception:
            pass

    if font is None:
        try:
            font = ImageFont.load_default(size=30)
        except Exception:
            font = ImageFont.load_default()

    small_font = None
    for font_candidate in ["arial.ttf", "segoeui.ttf", "calibri.ttf"]:
        try:
            small_font = ImageFont.truetype(font_candidate, 14)
            break
        except Exception:
            pass
    if small_font is None:
        try:
            small_font = ImageFont.load_default(size=14)
        except Exception:
            small_font = font

    for idx in range(TOTAL_FRAMES):
        col = idx % COLS
        row = idx // COLS
        cx = col * CELL_SIZE
        cy = row * CELL_SIZE

        # Center coordinates within this cell
        center_x = cx + CELL_SIZE // 2
        center_y = cy + CELL_SIZE // 2 + 4

        # Ears
        left_ear = [
            (cx + 28, cy + 44),
            (cx + 42, cy + 18),
            (cx + 56, cy + 32)
        ]
        right_ear = [
            (cx + 72, cy + 32),
            (cx + 86, cy + 18),
            (cx + 100, cy + 44)
        ]
        draw.polygon(left_ear, fill=TEAL_FILL, outline=TEAL_OUTLINE)
        draw.polygon(right_ear, fill=TEAL_FILL, outline=TEAL_OUTLINE)

        # Inner ears
        draw.polygon([(cx + 34, cy + 40), (cx + 42, cy + 24), (cx + 50, cy + 34)], fill=EAR_INNER)
        draw.polygon([(cx + 78, cy + 34), (cx + 86, cy + 24), (cx + 94, cy + 40)], fill=EAR_INNER)

        # Main round body shape
        body_box = [cx + 18, cy + 26, cx + 110, cy + 118]
        draw.ellipse(body_box, fill=TEAL_FILL, outline=TEAL_OUTLINE, width=3)

        # Subtle gloss highlight
        highlight_box = [cx + 28, cy + 32, cx + 64, cy + 56]
        draw.ellipse(highlight_box, fill=TEAL_HIGHLIGHT)

        # Frame number text with shadow
        text = str(idx)
        # Shadow
        draw.text((center_x + 1, center_y + 1), text, fill=TEXT_SHADOW, font=font, anchor="mm")
        # Main text
        draw.text((center_x, center_y), text, fill=TEXT_COLOR, font=font, anchor="mm")

        # Label "neku" at bottom of cell
        draw.text((center_x, cy + 108), "neku", fill=TEAL_OUTLINE, font=small_font, anchor="mm")

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    sheet.save(output_path, "PNG")
    print(f"[Pillow] Saved sprite sheet ({SHEET_WIDTH}x{SHEET_HEIGHT}) to {output_path}")


def draw_pure_python(output_path: str):
    """Fallback PNG generator using struct + zlib (no external dependencies)."""
    import struct
    import zlib
    import math

    # Buffer for RGBA pixels
    pixels = bytearray(SHEET_WIDTH * SHEET_HEIGHT * 4)

    def set_pixel(x, y, r, g, b, a):
        if 0 <= x < SHEET_WIDTH and 0 <= y < SHEET_HEIGHT:
            idx = (y * SHEET_WIDTH + x) * 4
            pixels[idx] = r
            pixels[idx + 1] = g
            pixels[idx + 2] = b
            pixels[idx + 3] = a

    # Simple 5x7 bitmap font for numbers 0-9
    FONT_5X7 = {
        '0': [
            " 111 ",
            "1   1",
            "1  11",
            "1 1 1",
            "11  1",
            "1   1",
            " 111 "
        ],
        '1': [
            "  1  ",
            " 11  ",
            "  1  ",
            "  1  ",
            "  1  ",
            "  1  ",
            " 111 "
        ],
        '2': [
            " 111 ",
            "1   1",
            "    1",
            "  11 ",
            " 1   ",
            "1    ",
            "11111"
        ],
        '3': [
            "1111 ",
            "    1",
            "   1 ",
            " 111 ",
            "    1",
            "    1",
            "1111 "
        ],
        '4': [
            "   1 ",
            "  11 ",
            " 1 1 ",
            "1  1 ",
            "11111",
            "   1 ",
            "   1 "
        ],
        '5': [
            "11111",
            "1    ",
            "1111 ",
            "    1",
            "    1",
            "1   1",
            " 111 "
        ],
        '6': [
            " 111 ",
            "1    ",
            "1    ",
            "1111 ",
            "1   1",
            "1   1",
            " 111 "
        ],
        '7': [
            "11111",
            "    1",
            "   1 ",
            "  1  ",
            " 1   ",
            " 1   ",
            " 1   "
        ],
        '8': [
            " 111 ",
            "1   1",
            "1   1",
            " 111 ",
            "1   1",
            "1   1",
            " 111 "
        ],
        '9': [
            " 111 ",
            "1   1",
            "1   1",
            " 1111",
            "    1",
            "    1",
            " 111 "
        ]
    }

    for idx in range(TOTAL_FRAMES):
        col = idx % COLS
        row = idx // COLS
        cx = col * CELL_SIZE
        cy = row * CELL_SIZE

        center_x = cx + CELL_SIZE // 2
        center_y = cy + CELL_SIZE // 2 + 4
        radius = 42

        # Draw circle
        for py in range(cy, cy + CELL_SIZE):
            for px in range(cx, cx + CELL_SIZE):
                dx = px - center_x
                dy = py - center_y
                dist = math.hypot(dx, dy)
                if dist <= radius:
                    if dist >= radius - 2:
                        set_pixel(px, py, *TEAL_OUTLINE)
                    else:
                        set_pixel(px, py, *TEAL_FILL)

        # Draw number
        text = str(idx)
        glyph_scale = 3
        char_width = 5 * glyph_scale
        char_spacing = 2 * glyph_scale
        total_text_width = len(text) * char_width + (len(text) - 1) * char_spacing
        text_start_x = center_x - total_text_width // 2
        text_start_y = center_y - (7 * glyph_scale) // 2

        curr_x = text_start_x
        for ch in text:
            pattern = FONT_5X7.get(ch, FONT_5X7['0'])
            for gy, rline in enumerate(pattern):
                for gx, char_pixel in enumerate(rline):
                    if char_pixel == '1':
                        for sy in range(glyph_scale):
                            for sx in range(glyph_scale):
                                set_pixel(curr_x + gx * glyph_scale + sx,
                                          text_start_y + gy * glyph_scale + sy,
                                          *TEXT_COLOR)
            curr_x += char_width + char_spacing

    # PNG encoding
    def png_chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    header = b"\x89PNG\r\n\x1a\n"
    ihdr = png_chunk(b"IHDR", struct.pack(">IIBBBBB", SHEET_WIDTH, SHEET_HEIGHT, 8, 6, 0, 0, 0))

    raw = bytearray()
    row_bytes = SHEET_WIDTH * 4
    for y in range(SHEET_HEIGHT):
        raw.append(0)  # filter type 0 (None)
        raw.extend(pixels[y * row_bytes : (y + 1) * row_bytes])

    idat = png_chunk(b"IDAT", zlib.compress(bytes(raw), 6))
    iend = png_chunk(b"IEND", b"")

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    with open(output_path, "wb") as f:
        f.write(header + ihdr + idat + iend)

    print(f"[Pure-Python Fallback] Saved sprite sheet ({SHEET_WIDTH}x{SHEET_HEIGHT}) to {output_path}")


def main():
    default_output = os.path.join(
        os.path.dirname(__file__),
        "..",
        "src",
        "DesktopPet",
        "Assets",
        "pets",
        "neku",
        "spritesheet.png"
    )

    parser = argparse.ArgumentParser(description="Generate Neku placeholder sprite sheet.")
    parser.add_argument(
        "--output", "-o",
        default=os.path.abspath(default_output),
        help=f"Output PNG path (default: {default_output})"
    )
    args = parser.parse_args()

    out_path = os.path.abspath(args.output)

    try:
        import PIL
        draw_with_pillow(out_path)
    except ImportError:
        print("Pillow not installed; using pure-Python fallback.")
        draw_pure_python(out_path)
    except Exception as ex:
        print(f"Pillow drawing failed ({ex}); using pure-Python fallback.")
        draw_pure_python(out_path)


if __name__ == "__main__":
    main()
