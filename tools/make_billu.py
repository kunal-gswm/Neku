"""
Generate Billu's spritesheet and manifest.
Billu is an orange cat with chaotic Instagram Reels / doomscrolling humor,
distinctive half-lidded eyes, smug smirk, amber eyes, and dedicated animation frames.
"""
import os
import json
import numpy as np
from PIL import Image

HERE = os.path.dirname(__file__)
SRC = os.path.join(HERE, "..", "src", "DesktopPet", "Assets", "pets")
BASE_CAT_SHEET = os.path.join(SRC, "cat", "spritesheet.png")
OUT_DIR = os.path.join(SRC, "billu")
OUT_SHEET = os.path.join(OUT_DIR, "spritesheet.png")
OUT_MANIFEST = os.path.join(OUT_DIR, "manifest.json")

CW, CH, COLS = 210, 300, 4
ROWS = 12

# Billu's vibrant ginger-tabby color stops
BILLU_FUR_STOPS = [
    (0.00, (0x4a, 0x18, 0x08)),
    (0.18, (0x84, 0x32, 0x10)),
    (0.38, (0xc4, 0x54, 0x16)),
    (0.58, (0xee, 0x7c, 0x20)),
    (0.78, (0xfc, 0xa4, 0x3c)),
    (0.92, (0xfd, 0xcc, 0x72)),
    (1.00, (0xfe, 0xec, 0xbc)),
]

# Rich amber/gold doomscrolling eyes
BILLU_EYE_STOPS = [
    (0.00, (0x6e, 0x46, 0x12)),
    (0.45, (0xb5, 0x80, 0x24)),
    (0.75, (0xd8, 0xa6, 0x3c)),
    (1.00, (0xf0, 0xd2, 0x72)),
]


def gradient_lut(stops):
    lut = np.zeros((256, 3), np.float64)
    for i in range(256):
        t = i / 255.0
        for j in range(len(stops) - 1):
            t0, c0 = stops[j]
            t1, c1 = stops[j + 1]
            if t0 <= t <= t1:
                f = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
                lut[i] = np.array(c0) * (1 - f) + np.array(c1) * f
                break
        else:
            lut[i] = stops[-1][1]
    return lut


def cell_slice(cid):
    c, r = cid % COLS, cid // COLS
    return slice(r * CH, r * CH + CH), slice(c * CW, c * CW + CW)


def build_billu_sheet():
    base_img = Image.open(BASE_CAT_SHEET).convert("RGBA")
    base_arr = np.asarray(base_img)
    H, W = base_arr.shape[:2]

    new_H = ROWS * CH

    # 1. Base ginger-tabby recolor
    rgb = base_arr[:, :, :3].astype(np.float64)
    alpha = base_arr[:, :, 3]
    R, G, B = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    mx, mn = rgb.max(2), rgb.min(2)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0.0)
    val = mx / 255.0
    opaque = alpha > 0

    feature = opaque & (sat > 0.40) & (val > 0.40)
    fur = opaque & ~feature

    # Tabby luminance mapping
    lum = 0.299 * R + 0.587 * G + 0.114 * B
    fl = lum[fur]
    lo, hi = np.percentile(fl, 2), np.percentile(fl, 98)
    norm = np.clip((lum - lo) / max(hi - lo, 1e-6), 0.0, 1.0) ** 0.74
    idx = (norm * 255).astype(np.uint8)

    fur_mapped = gradient_lut(BILLU_FUR_STOPS)[idx]
    out_rgb = np.where(fur[:, :, None], fur_mapped, rgb)

    # Green eye pixels to amber/gold
    eye_mask = feature & (G > R + 12) & (G > B + 12)
    evi = (np.clip(val, 0.0, 1.0) ** 0.9 * 255).astype(np.uint8)
    amber = gradient_lut(BILLU_EYE_STOPS)[evi]
    out_rgb = np.where(eye_mask[:, :, None], amber, out_rgb)

    out_arr = np.zeros((new_H, W, 4), dtype=np.uint8)
    out_arr[:H, :, :3] = np.clip(out_rgb, 0, 255).astype(np.uint8)
    out_arr[:H, :, 3] = alpha

    # Palette tokens for custom pixel art details
    fur_lid = np.array([0xee, 0x7c, 0x20], dtype=np.uint8)
    fur_hi = np.array([0xfc, 0xa4, 0x3c], dtype=np.uint8)
    lash_line = np.array([0x4a, 0x18, 0x08], dtype=np.uint8)
    amber_iris = np.array([0xf0, 0xd2, 0x72], dtype=np.uint8)
    amber_glow = np.array([0xfd, 0xcc, 0x72], dtype=np.uint8)
    dark_pupil = np.array([0x22, 0x0c, 0x04], dtype=np.uint8)
    white_hi = np.array([0xff, 0xf6, 0xe8], dtype=np.uint8)

    # --- 2. Cell 0: Half-lidded sleepy/sarcastic idle with natural curved lash line ---
    sl0 = cell_slice(0)
    c0_rgb = out_arr[sl0[0], sl0[1], :3].copy()
    c0_eye = eye_mask[sl0[0], sl0[1]]

    # Left eye: curved eyelid contour
    for x in range(104, 127):
        t = (x - 104) / 22.0
        lash_y = int(200 + 3.0 * np.sin(t * np.pi))
        for y in range(185, lash_y + 1):
            if c0_eye[y, x] or (y >= lash_y - 1 and c0_rgb[y, x, 0] < 50):
                if y == lash_y:
                    c0_rgb[y, x] = lash_line
                elif y == lash_y - 1:
                    c0_rgb[y, x] = fur_hi
                else:
                    c0_rgb[y, x] = fur_lid

    # Right eye: curved eyelid contour
    for x in range(156, 172):
        t = (x - 156) / 15.0
        lash_y = int(200 + 2.5 * np.sin(t * np.pi))
        for y in range(185, lash_y + 1):
            if c0_eye[y, x] or (y >= lash_y - 1 and c0_rgb[y, x, 0] < 50):
                if y == lash_y:
                    c0_rgb[y, x] = lash_line
                elif y == lash_y - 1:
                    c0_rgb[y, x] = fur_hi
                else:
                    c0_rgb[y, x] = fur_lid

    # Smug smirk on mouth corner (X=145, Y=219)
    c0_rgb[219, 144:147] = lash_line
    c0_rgb[218, 146:148] = lash_line
    out_arr[sl0[0], sl0[1], :3] = c0_rgb

    # --- 3. Cell 44: Suspicious Side-Eye ---
    sl44 = cell_slice(44)
    c44_rgb = c0_rgb.copy()
    for y in range(200, 218):
        for x in range(105, 127):
            if c0_eye[y, x]:
                if x >= 118:
                    c44_rgb[y, x] = dark_pupil
                elif x >= 115:
                    c44_rgb[y, x] = amber_iris
                else:
                    c44_rgb[y, x] = amber_glow

    for y in range(200, 218):
        for x in range(157, 172):
            if c0_eye[y, x]:
                if x >= 165:
                    c44_rgb[y, x] = dark_pupil
                elif x >= 161:
                    c44_rgb[y, x] = amber_iris
                else:
                    c44_rgb[y, x] = amber_glow

    c44_rgb[204, 119] = white_hi
    c44_rgb[204, 166] = white_hi
    # Arched right skeptical eyebrow
    c44_rgb[186, 158:171] = lash_line
    c44_rgb[187, 159:170] = fur_hi
    # Skeptical mouth quirk
    c44_rgb[219, 137:148] = lash_line

    out_arr[sl44[0], sl44[1], :3] = c44_rgb
    out_arr[sl44[0], sl44[1], 3] = out_arr[sl0[0], sl0[1], 3]

    # --- 4. Cell 45: Smug Buffering Stare ---
    sl45 = cell_slice(45)
    c45_rgb = c0_rgb.copy()
    for x in range(104, 127):
        t = (x - 104) / 22.0
        lash_y = int(205 + 2.0 * np.sin(t * np.pi))
        for y in range(185, lash_y + 1):
            if c0_eye[y, x] or (y >= lash_y - 1 and c0_rgb[y, x, 0] < 50):
                if y == lash_y:
                    c45_rgb[y, x] = lash_line
                elif y == lash_y - 1:
                    c45_rgb[y, x] = fur_hi
                else:
                    c45_rgb[y, x] = fur_lid

    for x in range(156, 172):
        t = (x - 156) / 15.0
        lash_y = int(205 + 2.0 * np.sin(t * np.pi))
        for y in range(185, lash_y + 1):
            if c0_eye[y, x] or (y >= lash_y - 1 and c0_rgb[y, x, 0] < 50):
                if y == lash_y:
                    c45_rgb[y, x] = lash_line
                elif y == lash_y - 1:
                    c45_rgb[y, x] = fur_hi
                else:
                    c45_rgb[y, x] = fur_lid

    # Smug cat smile :3
    c45_rgb[219, 138:142] = lash_line
    c45_rgb[220, 141:145] = lash_line
    c45_rgb[220, 145:149] = lash_line
    c45_rgb[219, 148:152] = lash_line

    out_arr[sl45[0], sl45[1], :3] = c45_rgb
    out_arr[sl45[0], sl45[1], 3] = out_arr[sl0[0], sl0[1], 3]

    # --- 5. Cell 46: Facepaw (paw covering face in exasperation) ---
    sl35 = cell_slice(35)
    sl46 = cell_slice(46)
    c46_rgb = out_arr[sl35[0], sl35[1], :3].copy()
    c35_eye = eye_mask[sl35[0], sl35[1]]
    for x in range(104, 127):
        t = (x - 104) / 22.0
        lash_y = int(201 + 2.0 * np.sin(t * np.pi))
        for y in range(185, lash_y + 1):
            if c35_eye[y, x] or (y >= lash_y - 1 and c46_rgb[y, x, 0] < 50):
                if y == lash_y:
                    c46_rgb[y, x] = lash_line
                elif y == lash_y - 1:
                    c46_rgb[y, x] = fur_hi
                else:
                    c46_rgb[y, x] = fur_lid

    out_arr[sl46[0], sl46[1], :3] = c46_rgb
    out_arr[sl46[0], sl46[1], 3] = out_arr[sl35[0], sl35[1], 3]

    # --- 6. Cell 47: Dramatic Pancake Flop ---
    sl15 = cell_slice(15)
    sl47 = cell_slice(47)
    c47_rgb = out_arr[sl15[0], sl15[1], :3].copy()
    out_arr[sl47[0], sl47[1], :3] = c47_rgb
    out_arr[sl47[0], sl47[1], 3] = out_arr[sl15[0], sl15[1], 3]

    result_img = Image.fromarray(out_arr, "RGBA")
    os.makedirs(OUT_DIR, exist_ok=True)
    result_img.save(OUT_SHEET, "PNG")
    print(f"Generated Billu spritesheet at {OUT_SHEET} ({result_img.size})")


def build_billu_manifest():
    manifest = {
        "name": "billu",
        "description": "Reel Goblin — chaotic orange cat with doomscrolling Reels humor, dramatic side-eyes, and buffering brain cells.",
        "sheet": "spritesheet.png",
        "cellWidth": 210,
        "cellHeight": 300,
        "columns": 4,
        "scale": 0.6,
        "animations": {
            "idle": {
                "frames": [0, 1],
                "fps": 2,
                "loop": True
            },
            "walk": {
                "frames": [4, 5, 6, 7],
                "fps": 10,
                "loop": True
            },
            "sleep": {
                "frames": [8, 9],
                "fps": 1.2,
                "loop": True
            },
            "fall": {
                "frames": [10],
                "fps": 1,
                "loop": True
            },
            "drag": {
                "frames": [22, 23, 24, 25, 26, 27, 28, 29],
                "fps": 9,
                "loop": False
            },
            "pet": {
                "frames": [12, 13, 12, 13],
                "fps": 5,
                "loop": True
            },
            "happy": {
                "frames": [12, 45, 13, 45],
                "fps": 4,
                "loop": True
            },
            "annoyed": {
                "frames": [44, 46, 44],
                "fps": 2,
                "loop": True
            },
            "surprised": {
                "frames": [10, 44],
                "fps": 3,
                "loop": False
            },
            "wave": {
                "frames": [35, 36, 35, 36],
                "fps": 4,
                "loop": True
            },
            "celebrate": {
                "frames": [12, 45, 13, 45],
                "fps": 5,
                "loop": True
            },
            "confused": {
                "frames": [44, 46, 44],
                "fps": 2,
                "loop": True
            },
            "sideeye": {
                "frames": [0, 44, 44, 44, 0],
                "fps": 2,
                "loop": True
            },
            "dramaticblink": {
                "frames": [0, 45, 1, 45, 0],
                "fps": 2,
                "loop": False
            },
            "facepaw": {
                "frames": [35, 46, 46, 35],
                "fps": 2,
                "loop": False
            },
            "flop": {
                "frames": [14, 15, 47, 15],
                "fps": 2,
                "loop": True
            },
            "stare": {
                "frames": [45],
                "fps": 1,
                "loop": True
            },
            "zoomies": {
                "frames": [4, 5, 6, 7],
                "fps": 16,
                "loop": True
            },
            "eat": {
                "frames": [2, 3, 2, 3],
                "fps": 4,
                "loop": False
            },
            "stretch": {
                "frames": [14, 15, 15, 14],
                "fps": 1.5,
                "loop": False
            },
            "typing": {
                "frames": [16, 17],
                "fps": 7,
                "loop": True
            },
            "typingfast": {
                "frames": [18, 19],
                "fps": 11,
                "loop": True
            },
            "play": {
                "frames": [20, 21],
                "fps": 5,
                "loop": True
            },
            "crouch": {
                "frames": [30, 31],
                "fps": 4,
                "loop": True
            },
            "pounce": {
                "frames": [32, 33],
                "fps": 8,
                "loop": False
            },
            "proud": {
                "frames": [45],
                "fps": 1,
                "loop": True
            },
            "groom": {
                "frames": [35, 36],
                "fps": 3,
                "loop": True
            },
            "yawn": {
                "frames": [37, 38],
                "fps": 2.5,
                "loop": False
            },
            "loaf": {
                "frames": [39],
                "fps": 1,
                "loop": True
            },
            "gift": {
                "frames": [40],
                "fps": 1,
                "loop": True
            },
            "knockoff": {
                "frames": [41],
                "fps": 1,
                "loop": True
            },
            "siderest": {
                "frames": [15, 47],
                "fps": 1,
                "loop": True
            },
            "wakeup": {
                "frames": [38, 37],
                "fps": 3,
                "loop": False
            },
            "talk": {
                "frames": [0, 42, 43, 42],
                "fps": 6,
                "loop": True
            },
            "blink": {
                "frames": [0, 1],
                "fps": 2,
                "loop": False
            },
            "lookaround": {
                "frames": [0, 44, 0],
                "fps": 2,
                "loop": False
            },
            "sit": {
                "frames": [39],
                "fps": 1,
                "loop": True
            }
        }
    }

    with open(OUT_MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print(f"Generated Billu manifest at {OUT_MANIFEST} ({len(manifest['animations'])} animations)")


if __name__ == "__main__":
    build_billu_sheet()
    build_billu_manifest()
