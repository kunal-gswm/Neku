"""
Generates the complete 64-frame production sprite sheet for generic Neku.
128x128 pixel cells arranged in a 4-column x 16-row grid (512x2048).
Distinctive teal goblin/cat companion mascot with fully animated poses for
every state: wave, celebrate, laugh, confused, facepalm, pout, typing, stretch, etc.
"""

import os
import sys
import math
import json
from PIL import Image, ImageDraw, ImageFont

CELL_SIZE = 128
COLS = 4
ROWS = 16
TOTAL_FRAMES = ROWS * COLS
SHEET_WIDTH = COLS * CELL_SIZE
SHEET_HEIGHT = ROWS * CELL_SIZE

# Palette
BG_COLOR       = (0, 0, 0, 0)
TEAL_FILL      = (14, 185, 180, 255)       # Bright Teal (#0eb9b4)
TEAL_OUTLINE   = (15, 118, 110, 255)      # Deep Teal (#0f766e)
TEAL_DARK      = (11, 88, 82, 255)
TEAL_HIGHLIGHT = (165, 243, 252, 200)   # Soft Cyan (#a5f3fc)
EAR_INNER      = (204, 251, 241, 255)      # Pale Mint (#ccfbf1)
EYE_DARK       = (15, 23, 42, 255)         # Deep Navy
BLUSH_COLOR    = (255, 150, 175, 190)      # Soft Pink
GOLD_STAR      = (255, 215, 50, 255)
SWEAT_BLUE     = (120, 205, 255, 240)
NOTE_PINK      = (255, 100, 160, 255)

def draw_cell_frame(idx: int) -> Image.Image:
    cell = Image.new("RGBA", (CELL_SIZE, CELL_SIZE), BG_COLOR)
    draw = ImageDraw.Draw(cell)

    # Defaults
    cx, cy = 0, 0
    center_x = 64
    center_y = 68

    # Posture offsets
    body_y_off = 0
    body_squash = 0
    tilt_deg = 0

    # Specific frame adjustments
    if idx in (5, 7):        # walk bob
        body_y_off = -2
    elif idx == 13:           # pet / happy bob
        body_y_off = -2
    elif idx in (30, 31):     # crouch
        body_squash = 12
        body_y_off = 6
    elif idx in (52,):        # celebrate windup
        body_squash = 6
        body_y_off = 4
    elif idx in (53,):        # celebrate high hop
        body_y_off = -14
    elif idx in (54,):        # celebrate float
        body_y_off = -10
    elif idx in (10,):        # fall / surprised
        body_y_off = -8
    elif idx in (57,):        # laugh bob
        body_y_off = -2
    elif idx in (58,):        # confused tilt left
        tilt_deg = -4
    elif idx in (59,):        # confused tilt right
        tilt_deg = 3
    elif idx in (46, 62):     # annoyed / pout tilt
        tilt_deg = -3

    by = cy + body_y_off

    # 1. Ears
    ear_wobble = -2 if idx in (4, 6) else (2 if idx in (5, 7) else 0)
    left_ear = [
        (cx + 28, by + 44),
        (cx + 42 + ear_wobble, by + 18),
        (cx + 56, by + 32)
    ]
    right_ear = [
        (cx + 72, by + 32),
        (cx + 86 + ear_wobble, by + 18),
        (cx + 100, by + 44)
    ]

    # Confused flopped ear
    if idx in (58, 59):
        right_ear = [(cx + 72, by + 34), (cx + 92, by + 28), (cx + 98, by + 44)]
    # Annoyed flattened ears
    elif idx in (46, 62, 63):
        left_ear = [(cx + 22, by + 50), (cx + 34, by + 30), (cx + 52, by + 38)]
        right_ear = [(cx + 76, by + 38), (cx + 94, by + 30), (cx + 106, by + 50)]

    draw.polygon(left_ear, fill=TEAL_FILL, outline=TEAL_OUTLINE)
    draw.polygon(right_ear, fill=TEAL_FILL, outline=TEAL_OUTLINE)

    # Inner ears
    draw.polygon([(left_ear[0][0]+6, left_ear[0][1]-4), (left_ear[1][0], left_ear[1][1]+6), (left_ear[2][0]-6, left_ear[2][1]+2)], fill=EAR_INNER)
    draw.polygon([(right_ear[0][0]+6, right_ear[0][1]+2), (right_ear[1][0], right_ear[1][1]+6), (right_ear[2][0]-6, right_ear[2][1]-4)], fill=EAR_INNER)

    # 2. Main round body
    body_box = [cx + 18, by + 26 + body_squash, cx + 110, by + 118]
    draw.ellipse(body_box, fill=TEAL_FILL, outline=TEAL_OUTLINE, width=3)

    # Gloss highlight
    highlight_box = [cx + 28, by + 32 + body_squash, cx + 64, by + 54 + body_squash]
    draw.ellipse(highlight_box, fill=TEAL_HIGHLIGHT)

    # Cheeks blush
    draw.ellipse([cx + 30, by + 72 + body_squash//2, cx + 44, by + 82 + body_squash//2], fill=BLUSH_COLOR)
    draw.ellipse([cx + 84, by + 72 + body_squash//2, cx + 98, by + 82 + body_squash//2], fill=BLUSH_COLOR)

    # 3. Eyes & Expressions
    ey = by + 56 + body_squash//2
    if idx in (8, 9): # Sleeping
        draw.arc([cx + 40, ey - 2, cx + 56, ey + 10], start=180, end=360, fill=TEAL_OUTLINE, width=3)
        draw.arc([cx + 72, ey - 2, cx + 88, ey + 10], start=180, end=360, fill=TEAL_OUTLINE, width=3)
    elif idx in (12, 13, 34, 48, 49, 50, 51, 52, 55): # Happy curved ^ ^
        draw.arc([cx + 40, ey - 2, cx + 56, ey + 10], start=180, end=360, fill=TEAL_OUTLINE, width=3)
        draw.arc([cx + 72, ey - 2, cx + 88, ey + 10], start=180, end=360, fill=TEAL_OUTLINE, width=3)
    elif idx in (56, 57): # Giggling > <
        draw.line([cx + 42, ey - 4, cx + 52, ey + 2], fill=TEAL_OUTLINE, width=3)
        draw.line([cx + 52, ey + 2, cx + 42, ey + 8], fill=TEAL_OUTLINE, width=3)
        draw.line([cx + 86, ey - 4, cx + 76, ey + 2], fill=TEAL_OUTLINE, width=3)
        draw.line([cx + 76, ey + 2, cx + 86, ey + 8], fill=TEAL_OUTLINE, width=3)
    elif idx in (1, 14, 60, 61): # Blink / Closed - -
        draw.line([cx + 40, ey + 4, cx + 56, ey + 4], fill=TEAL_OUTLINE, width=3)
        draw.line([cx + 72, ey + 4, cx + 88, ey + 4], fill=TEAL_OUTLINE, width=3)
    elif idx in (10, 53, 54): # Surprised / Cheering wide round eyes
        draw.ellipse([cx + 40, ey - 4, cx + 56, ey + 14], fill=EYE_DARK, outline=TEAL_OUTLINE, width=2)
        draw.ellipse([cx + 72, ey - 4, cx + 88, ey + 14], fill=EYE_DARK, outline=TEAL_OUTLINE, width=2)
        draw.ellipse([cx + 44, ey, cx + 48, ey + 4], fill=(255, 255, 255, 255))
        draw.ellipse([cx + 76, ey, cx + 80, ey + 4], fill=(255, 255, 255, 255))
    elif idx in (44, 58, 62): # Glance left
        draw.ellipse([cx + 42, ey - 2, cx + 56, ey + 12], fill=EYE_DARK)
        draw.ellipse([cx + 72, ey - 2, cx + 86, ey + 12], fill=EYE_DARK)
        draw.ellipse([cx + 43, ey, cx + 47, ey + 4], fill=(255, 255, 255, 255))
        draw.ellipse([cx + 73, ey, cx + 77, ey + 4], fill=(255, 255, 255, 255))
    elif idx in (45, 59): # Glance right
        draw.ellipse([cx + 42, ey - 2, cx + 56, ey + 12], fill=EYE_DARK)
        draw.ellipse([cx + 72, ey - 2, cx + 86, ey + 12], fill=EYE_DARK)
        draw.ellipse([cx + 49, ey, cx + 53, ey + 4], fill=(255, 255, 255, 255))
        draw.ellipse([cx + 79, ey, cx + 83, ey + 4], fill=(255, 255, 255, 255))
    else: # Normal alert eyes
        draw.ellipse([cx + 42, ey - 2, cx + 56, ey + 12], fill=EYE_DARK)
        draw.ellipse([cx + 72, ey - 2, cx + 86, ey + 12], fill=EYE_DARK)
        draw.ellipse([cx + 45, ey, cx + 49, ey + 4], fill=(255, 255, 255, 255))
        draw.ellipse([cx + 75, ey, cx + 79, ey + 4], fill=(255, 255, 255, 255))

    # 4. Mouth
    my = by + 74 + body_squash//2
    if idx in (10, 14, 37, 38): # Open O mouth (surprised / yawn)
        draw.ellipse([center_x - 5, my - 2, center_x + 5, my + 8], fill=(220, 60, 85, 255), outline=TEAL_OUTLINE, width=2)
    elif idx in (53, 54, 56, 57): # Open cheerful D mouth (laugh / cheer)
        draw.pieslice([center_x - 6, my - 3, center_x + 6, my + 7], start=0, end=180, fill=(230, 70, 95, 255), outline=TEAL_OUTLINE, width=2)
    elif idx in (46, 62, 63): # Grumpy pout (
        draw.arc([center_x - 6, my + 1, center_x + 6, my + 9], start=190, end=350, fill=TEAL_OUTLINE, width=3)
    else: # Cute :3 mouth
        draw.polygon([(center_x - 3, my), (center_x + 3, my), (center_x, my + 4)], fill=EAR_INNER)
        draw.arc([center_x - 8, my + 2, center_x, my + 10], start=0, end=180, fill=TEAL_OUTLINE, width=2)
        draw.arc([center_x, my + 2, center_x + 8, my + 10], start=0, end=180, fill=TEAL_OUTLINE, width=2)

    # Whiskers
    draw.line([cx + 20, my, cx + 34, my - 2], fill=TEAL_OUTLINE, width=2)
    draw.line([cx + 20, my + 6, cx + 34, my + 6], fill=TEAL_OUTLINE, width=2)
    draw.line([cx + 94, my - 2, cx + 108, my], fill=TEAL_OUTLINE, width=2)
    draw.line([cx + 94, my + 6, cx + 108, my + 6], fill=TEAL_OUTLINE, width=2)

    # 5. Paws & Arms
    paw_y = by + 104

    if idx in (48, 49, 50, 51): # WAVE: Left paw down, right paw raised waving!
        draw.ellipse([cx + 36, paw_y, cx + 50, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Raised right arm
        hand_x = 94 if idx == 50 else (84 if idx in (49, 51) else 88)
        hand_y = by + 34
        draw.line([(cx + 80, by + 76), (cx + 90, by + 56)], fill=TEAL_FILL, width=12)
        draw.line([(cx + 80, by + 76), (cx + 90, by + 56)], fill=TEAL_OUTLINE, width=3)
        draw.line([(cx + 90, by + 56), (cx + hand_x, hand_y)], fill=TEAL_FILL, width=10)
        draw.ellipse([cx + hand_x - 6, hand_y - 6, cx + hand_x + 6, hand_y + 6], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Wave arc
        arc_x = cx + hand_x + (8 if idx == 50 else -8)
        draw.arc([arc_x - 6, hand_y - 8, arc_x + 6, hand_y + 4], start=190, end=330, fill=(120, 180, 240, 255), width=2)
        if idx == 51:
            draw.polygon([(cx+96, hand_y-10), (cx+98, hand_y-6), (cx+102, hand_y-6), (cx+98, hand_y-4), (cx+100, hand_y)], fill=GOLD_STAR)

    elif idx in (53, 54): # CELEBRATE: Both paws raised \o/
        # Left paw raised
        draw.line([(cx + 36, by + 76), (cx + 26, by + 46)], fill=TEAL_FILL, width=11)
        draw.line([(cx + 36, by + 76), (cx + 26, by + 46)], fill=TEAL_OUTLINE, width=3)
        draw.ellipse([cx + 20, by + 40, cx + 32, by + 52], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Right paw raised
        draw.line([(cx + 92, by + 76), (cx + 102, by + 46)], fill=TEAL_FILL, width=11)
        draw.line([(cx + 92, by + 76), (cx + 102, by + 46)], fill=TEAL_OUTLINE, width=3)
        draw.ellipse([cx + 96, by + 40, cx + 108, by + 52], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Stars
        def draw_star(sx, sy, sz):
            draw.polygon([(sx, sy-sz), (sx+sz//3, sy-sz//3), (sx+sz, sy), (sx+sz//3, sy+sz//3),
                          (sx, sy+sz), (sx-sz//3, sy+sz//3), (sx-sz, sy), (sx-sz//3, sy-sz//3)], fill=GOLD_STAR, outline=TEAL_OUTLINE)
        draw_star(cx + 18, by + 30, 7)
        draw_star(cx + 110, by + 30, 7)
        draw_star(cx + 64, by + 16, 5)

    elif idx in (16, 17, 18, 19): # TYPING: Laptop in front, alternating paws
        # Laptop
        lx, ly = cx + 46, by + 90
        draw.polygon([(lx - 4, ly + 14), (lx + 40, ly + 14), (lx + 36, ly + 22), (lx - 8, ly + 22)], fill=(200, 210, 225, 255), outline=TEAL_OUTLINE, width=2)
        draw.polygon([(lx, ly), (lx + 36, ly), (lx + 36, ly + 14), (lx, ly + 14)], fill=(80, 160, 230, 255), outline=TEAL_OUTLINE, width=2)
        # Paws tapping
        left_paw_y = ly + (16 if idx % 2 == 0 else 10)
        right_paw_y = ly + (10 if idx % 2 == 0 else 16)
        draw.ellipse([lx - 2, left_paw_y - 4, lx + 10, left_paw_y + 4], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.ellipse([lx + 26, right_paw_y - 4, lx + 38, right_paw_y + 4], fill=EAR_INNER, outline=TEAL_OUTLINE)
        if idx in (18, 19): # Speed lines
            draw.line([lx - 12, ly + 6, lx - 4, ly + 8], fill=(255, 90, 90, 255), width=2)
            draw.line([lx + 40, ly + 8, lx + 48, ly + 6], fill=(255, 90, 90, 255), width=2)

    elif idx in (60, 61): # FACEPALM: Paw over forehead, sweatdrop
        draw.ellipse([cx + 36, paw_y, cx + 50, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Right paw up to forehead
        draw.line([(cx + 80, by + 76), (cx + 70, by + 52)], fill=TEAL_FILL, width=12)
        draw.line([(cx + 80, by + 76), (cx + 70, by + 52)], fill=TEAL_OUTLINE, width=3)
        draw.ellipse([cx + 64, by + 46, cx + 76, by + 58], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Sweatdrop
        draw.polygon([(cx + 86, by + 34), (cx + 89, by + 42), (cx + 83, by + 42)], fill=SWEAT_BLUE, outline=TEAL_OUTLINE)
        draw.ellipse([cx + 82, by + 40, cx + 90, by + 48], fill=SWEAT_BLUE, outline=TEAL_OUTLINE)

    elif idx in (58, 59): # CONFUSED: Paw to chin, question mark ?
        draw.ellipse([cx + 36, paw_y, cx + 50, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Right paw to chin
        draw.line([(cx + 80, by + 76), (cx + 74, by + 68)], fill=TEAL_FILL, width=11)
        draw.ellipse([cx + 68, by + 64, cx + 78, by + 74], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Question mark
        qx, qy = cx + 88, by + 26
        draw.arc([qx, qy, qx + 10, qy + 10], start=180, end=360, fill=GOLD_STAR, width=3)
        draw.line([qx + 10, qy + 5, qx + 5, qy + 10], fill=GOLD_STAR, width=3)
        draw.line([qx + 5, qy + 10, qx + 5, qy + 13], fill=GOLD_STAR, width=3)
        draw.ellipse([qx + 4, qy + 15, qx + 6, qy + 17], fill=GOLD_STAR)

    elif idx in (62, 63): # POUT: Folded paws across body
        draw.line([(cx + 38, by + 86), (cx + 90, by + 86)], fill=TEAL_OUTLINE, width=8)
        draw.line([(cx + 38, by + 86), (cx + 90, by + 86)], fill=EAR_INNER, width=5)
        draw.ellipse([cx + 56, by + 82, cx + 72, by + 90], fill=EAR_INNER, outline=TEAL_OUTLINE)

    elif idx in (56, 57): # LAUGH: Paws clapping, musical notes
        draw.ellipse([cx + 46, paw_y - 4, cx + 60, paw_y + 8], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        draw.ellipse([cx + 68, paw_y - 4, cx + 82, paw_y + 8], fill=EAR_INNER, outline=TEAL_OUTLINE, width=2)
        # Music note
        nx, ny = (cx + 86, by + 24) if idx == 56 else (cx + 26, by + 24)
        draw.ellipse([nx, ny + 6, nx + 6, ny + 11], fill=NOTE_PINK, outline=TEAL_OUTLINE)
        draw.line([nx + 5, ny + 1, nx + 5, ny + 9], fill=TEAL_OUTLINE, width=2)
        draw.line([nx + 5, ny + 1, nx + 9, ny], fill=TEAL_OUTLINE, width=2)

    elif idx in (12, 13): # PET / HAPPY: Floating hearts
        draw.ellipse([cx + 42, paw_y, cx + 56, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.ellipse([cx + 72, paw_y, cx + 86, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
        # Hearts
        hx = cx + (86 if idx == 13 else 80)
        hy = by + (16 if idx == 13 else 24)
        draw.polygon([(hx+4, hy+8), (hx, hy+4), (hx+1, hy+1), (hx+4, hy+2), (hx+7, hy+1), (hx+8, hy+4)], fill=NOTE_PINK)
        if idx == 13:
            draw.polygon([(cx+24, by+20), (cx+22, by+23), (cx+26, by+23)], fill=GOLD_STAR)

    elif idx in (34,): # PROUD: Golden star
        draw.ellipse([cx + 42, paw_y - 2, cx + 56, paw_y + 10], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.ellipse([cx + 72, paw_y - 2, cx + 86, paw_y + 10], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.polygon([(cx+86, by+20), (cx+88, by+24), (cx+93, by+24), (cx+89, by+27), (cx+91, by+32),
                      (cx+86, by+29), (cx+81, by+32), (cx+83, by+27), (cx+79, by+24), (cx+84, by+24)], fill=GOLD_STAR)

    elif idx in (4, 6): # Walk step
        draw.ellipse([cx + 42, paw_y - 4, cx + 56, paw_y + 8], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.ellipse([cx + 72, paw_y, cx + 86, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
    elif idx in (5, 7): # Walk alternate step
        draw.ellipse([cx + 42, paw_y, cx + 56, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.ellipse([cx + 72, paw_y - 4, cx + 86, paw_y + 8], fill=EAR_INNER, outline=TEAL_OUTLINE)
    else: # Default resting paws
        draw.ellipse([cx + 42, paw_y, cx + 56, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)
        draw.ellipse([cx + 72, paw_y, cx + 86, paw_y + 12], fill=EAR_INNER, outline=TEAL_OUTLINE)

    # Rotate if tilted
    if tilt_deg != 0:
        cell = cell.rotate(tilt_deg, resample=Image.Resampling.BICUBIC, center=(64, 118))

    return cell

def generate_neku_spritesheet(output_path: str):
    sheet = Image.new("RGBA", (SHEET_WIDTH, SHEET_HEIGHT), BG_COLOR)
    for idx in range(TOTAL_FRAMES):
        col = idx % COLS
        row = idx // COLS
        f = draw_cell_frame(idx)
        sheet.paste(f, (col * CELL_SIZE, row * CELL_SIZE), f)

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    sheet.save(output_path, "PNG")
    print(f"Saved generic Neku sprite sheet ({SHEET_WIDTH}x{SHEET_HEIGHT}) to {output_path}")

def generate_neku_manifest(manifest_path: str):
    data = {
        "name": "neku",
        "sheet": "spritesheet.png",
        "cellWidth": 128,
        "cellHeight": 128,
        "columns": 4,
        "scale": 1.0,
        "animations": {
            "idle":        { "frames": [0, 0, 0, 1],                    "fps": 2.0,  "loop": True  },
            "blink":       { "frames": [0, 1, 0],                       "fps": 3.0,  "loop": False },
            "lookaround":  { "frames": [0, 44, 44, 0, 45, 45, 0],        "fps": 2.5,  "loop": False },
            "walk":        { "frames": [4, 5, 6, 7],                    "fps": 8.0,  "loop": True  },
            "sit":         { "frames": [39],                            "fps": 1.0,  "loop": True  },
            "sleep":       { "frames": [8, 9],                          "fps": 1.2,  "loop": True  },
            "happy":       { "frames": [12, 13, 12, 13],                "fps": 5.0,  "loop": True  },
            "annoyed":     { "frames": [46],                            "fps": 1.0,  "loop": True  },
            "surprised":   { "frames": [10],                            "fps": 1.0,  "loop": False },
            "fall":        { "frames": [10],                            "fps": 1.0,  "loop": True  },
            "drag":        { "frames": [22, 23, 24, 25, 26, 27, 28, 29], "fps": 8.0, "loop": True  },
            "talk":        { "frames": [0, 42, 43, 42],                 "fps": 6.0,  "loop": True  },
            "eat":         { "frames": [2, 3, 2, 3],                    "fps": 4.0,  "loop": False },
            "pet":         { "frames": [12, 13, 12, 13],                "fps": 4.0,  "loop": True  },
            "stretch":     { "frames": [14, 15, 15, 14],                "fps": 2.0,  "loop": False },
            "typing":      { "frames": [16, 17],                        "fps": 7.0,  "loop": True  },
            "typingfast":  { "frames": [18, 19],                        "fps": 11.0, "loop": True  },
            "play":        { "frames": [20, 21],                        "fps": 5.0,  "loop": True  },
            "proud":       { "frames": [34],                            "fps": 1.0,  "loop": True  },
            "groom":       { "frames": [35, 36],                        "fps": 3.0,  "loop": True  },
            "yawn":        { "frames": [37, 38],                        "fps": 2.5,  "loop": False },
            "loaf":        { "frames": [39],                            "fps": 1.0,  "loop": True  },
            "siderest":    { "frames": [11],                            "fps": 1.0,  "loop": True  },
            "gift":        { "frames": [40],                            "fps": 1.0,  "loop": True  },
            "knockoff":    { "frames": [41],                            "fps": 1.0,  "loop": True  },
            "crouch":      { "frames": [30, 31],                        "fps": 4.0,  "loop": True  },
            "pounce":      { "frames": [32, 33],                        "fps": 8.0,  "loop": False },
            "wakeup":      { "frames": [47, 0],                         "fps": 2.0,  "loop": False },
            "chase":       { "frames": [4, 5, 6, 7],                    "fps": 11.0, "loop": True  },
            "zoomies":     { "frames": [4, 5, 6, 7],                    "fps": 14.0, "loop": True  },
            "bat":         { "frames": [20, 21],                        "fps": 6.0,  "loop": True  },
            "hunt":        { "frames": [30, 31],                        "fps": 4.0,  "loop": True  },
            "spin":        { "frames": [4, 5, 6, 7],                    "fps": 12.0, "loop": True  },
            "jump":        { "frames": [32, 33],                        "fps": 8.0,  "loop": False },
            "wave":        { "frames": [48, 49, 50, 49, 51, 0],         "fps": 5.0,  "loop": False },
            "celebrate":   { "frames": [52, 53, 54, 53, 55, 0],         "fps": 6.0,  "loop": False },
            "laugh":       { "frames": [56, 57, 56, 57, 56],             "fps": 6.0,  "loop": False },
            "confused":    { "frames": [58, 59, 58, 59, 0],              "fps": 2.5,  "loop": False },
            "facepalm":    { "frames": [60, 61, 61, 60, 0],              "fps": 2.0,  "loop": False },
            "pout":        { "frames": [62, 63, 62, 63],                 "fps": 2.0,  "loop": True  }
        }
    }
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    print(f"Saved generic Neku manifest to {manifest_path}")

if __name__ == "__main__":
    out_sheet = os.path.join("src", "DesktopPet", "Assets", "pets", "neku", "spritesheet.png")
    out_manifest = os.path.join("src", "DesktopPet", "Assets", "pets", "neku", "manifest.json")
    generate_neku_spritesheet(out_sheet)
    generate_neku_manifest(out_manifest)
