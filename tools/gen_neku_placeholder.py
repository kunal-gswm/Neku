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

        # Cheeks blush
        draw.ellipse([cx + 30, cy + 72, cx + 44, cy + 82], fill=(255, 160, 180, 160))
        draw.ellipse([cx + 84, cy + 72, cx + 98, cy + 82], fill=(255, 160, 180, 160))

        # Facial expressions per frame type
        is_sleeping = idx in (8, 9)
        is_happy = idx in (12, 13)
        is_surprised = idx == 10
        is_blink = idx == 1

        if is_sleeping or is_happy:
            # Curved ^ ^ happy / sleeping eyes
            draw.arc([cx + 40, cy + 54, cx + 56, cy + 68], start=180, end=360, fill=TEAL_OUTLINE, width=3)
            draw.arc([cx + 72, cy + 54, cx + 88, cy + 68], start=180, end=360, fill=TEAL_OUTLINE, width=3)
        elif is_blink:
            # Closed - - eyes
            draw.line([cx + 40, cy + 62, cx + 56, cy + 62], fill=TEAL_OUTLINE, width=3)
            draw.line([cx + 72, cy + 62, cx + 88, cy + 62], fill=TEAL_OUTLINE, width=3)
        elif is_surprised:
            # Big wide round eyes
            draw.ellipse([cx + 40, cy + 52, cx + 56, cy + 70], fill=(15, 23, 42, 255), outline=TEAL_OUTLINE)
            draw.ellipse([cx + 72, cy + 52, cx + 88, cy + 70], fill=(15, 23, 42, 255), outline=TEAL_OUTLINE)
            draw.ellipse([cx + 44, cy + 56, cx + 48, cy + 60], fill=(255, 255, 255, 255))
            draw.ellipse([cx + 76, cy + 56, cx + 80, cy + 60], fill=(255, 255, 255, 255))
        else:
            # Standard alert cat eyes with highlights
            draw.ellipse([cx + 42, cy + 54, cx + 56, cy + 68], fill=(15, 23, 42, 255))
            draw.ellipse([cx + 72, cy + 54, cx + 86, cy + 68], fill=(15, 23, 42, 255))
            draw.ellipse([cx + 45, cy + 56, cx + 49, cy + 60], fill=(255, 255, 255, 255))
            draw.ellipse([cx + 75, cy + 56, cx + 79, cy + 60], fill=(255, 255, 255, 255))

        # Nose & cat :3 mouth
        draw.polygon([(center_x - 3, cy + 72), (center_x + 3, cy + 72), (center_x, cy + 76)], fill=EAR_INNER)
        draw.arc([center_x - 8, cy + 74, center_x, cy + 82], start=0, end=180, fill=TEAL_OUTLINE, width=2)
        draw.arc([center_x, cy + 74, center_x + 8, cy + 82], start=0, end=180, fill=TEAL_OUTLINE, width=2)

        # Whiskers
        draw.line([cx + 22, cy + 72, cx + 36, cy + 70], fill=TEAL_OUTLINE, width=2)
        draw.line([cx + 22, cy + 78, cx + 36, cy + 78], fill=TEAL_OUTLINE, width=2)
        draw.line([cx + 92, cy + 70, cx + 106, cy + 72], fill=TEAL_OUTLINE, width=2)
        draw.line([cx + 92, cy + 78, cx + 106, cy + 78], fill=TEAL_OUTLINE, width=2)

        # Little paws at bottom
        paw_y = cy + 104
        if idx in (4, 6): # Walk frame step
            draw.ellipse([cx + 42, paw_y - 2, cx + 56, paw_y + 10], fill=EAR_INNER, outline=TEAL_OUTLINE)
            draw.ellipse([cx + 72, paw_y, cx + 86, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
        elif idx in (5, 7): # Walk frame alternate step
            draw.ellipse([cx + 42, paw_y, cx + 56, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
            draw.ellipse([cx + 72, paw_y - 2, cx + 86, paw_y + 10], fill=EAR_INNER, outline=TEAL_OUTLINE)
        else:
            draw.ellipse([cx + 42, paw_y, cx + 56, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
            draw.ellipse([cx + 72, paw_y, cx + 86, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)

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
