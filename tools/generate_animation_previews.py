import json
import os
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np

PACKS = [p.name for p in sorted(Path('src/DesktopPet/Assets/pets').glob('*')) if p.is_dir() and 'personal' not in p.name.lower()]
TARGET_ANIMS = ['wave', 'celebrate', 'confused', 'laugh', 'pout', 'facepalm', 'typing', 'stretch', 'pet', 'proud']

FALLBACK_CHAIN = {
    "wave": ["talk", "happy", "pet", "idle"],
    "celebrate": ["happy", "jump", "proud", "idle"],
    "confused": ["lookaround", "blink", "surprised", "idle"],
    "pout": ["annoyed", "idle"],
    "laugh": ["happy", "celebrate", "idle"],
    "facepalm": ["annoyed", "lookaround", "idle"],
    "yawn": ["sleep", "siderest", "idle"],
    "stretch": ["wakeup", "idle"],
    "focus": ["typing", "idle"],
    "work": ["typing", "idle"],
    "typingfast": ["typing", "idle"],
    "crouch": ["sit", "loaf", "idle"],
    "pounce": ["jump", "walk", "idle"],
    "proud": ["happy", "sit", "idle"],
    "groom": ["pet", "sit", "idle"],
    "loaf": ["sit", "siderest", "idle"],
    "siderest": ["sleep", "sit", "idle"],
    "wakeup": ["stretch", "idle"],
    "talk": ["idle"],
    "blink": ["idle"],
    "lookaround": ["idle"],
    "sit": ["idle"],
    "happy": ["pet", "idle"],
    "annoyed": ["idle"],
    "surprised": ["idle"],
}

PREVIEWS_DIR = Path('tools/previews')
PREVIEWS_DIR.mkdir(parents=True, exist_ok=True)

audit_report = {}

for p in PACKS:
    man_path = Path(f'src/DesktopPet/Assets/pets/{p}/manifest.json')
    if not man_path.exists():
        continue
    data = json.loads(man_path.read_text(encoding='utf-8'))
    sheet_path = Path(f'src/DesktopPet/Assets/pets/{p}/{data["sheet"]}')
    if not sheet_path.exists():
        continue

    img = Image.open(sheet_path).convert('RGBA')
    cell_w = data['cellWidth']
    cell_h = data['cellHeight']
    cols = data['columns']

    pack_preview_dir = PREVIEWS_DIR / p
    pack_preview_dir.mkdir(parents=True, exist_ok=True)

    # Slice cells
    max_cols = img.size[0] // cell_w
    max_rows = img.size[1] // cell_h
    total_cells = max_cols * max_rows

    cells = {}
    for idx in range(total_cells):
        c = idx % cols
        r = idx // cols
        box = (c * cell_w, r * cell_h, (c + 1) * cell_w, (r + 1) * cell_h)
        cells[idx] = img.crop(box)

    anims = data.get('animations', {})
    audit_report[p] = {}

    for anim_name in TARGET_ANIMS:
        resolved_name = anim_name
        is_fallback = False

        if anim_name not in anims:
            # Resolve via fallback chain
            is_fallback = True
            fallbacks = FALLBACK_CHAIN.get(anim_name, ["idle"])
            for cand in fallbacks:
                if cand in anims:
                    resolved_name = cand
                    break
            else:
                resolved_name = "idle" if "idle" in anims else list(anims.keys())[0]

        adef = anims[resolved_name]
        f_indices = adef.get('frames', [])
        fps = adef.get('fps', 4.0)
        loop = adef.get('loop', False)
        duration_ms = int(1000 / fps) if fps > 0 else 250

        anim_frames = [cells[i] for i in f_indices if i in cells]
        if not anim_frames:
            audit_report[p][anim_name] = {
                'status': 'EMPTY',
                'frames': [],
                'distinct': False,
                'note': f'Frame list empty for resolved {resolved_name}'
            }
            continue

        # Check pixel deltas across frames
        np_frames = [np.array(f) for f in anim_frames]
        unique_frames_count = 0
        for i in range(len(np_frames)):
            is_unique = True
            for j in range(i):
                if np.array_equal(np_frames[i], np_frames[j]):
                    is_unique = False
                    break
            if is_unique:
                unique_frames_count += 1

        is_static = (unique_frames_count == 1 and len(f_indices) > 1) or len(f_indices) == 1

        # Check if identical sequence to another animation
        matching_other = []
        for oname, odef in anims.items():
            if oname == resolved_name:
                continue
            if odef.get('frames', []) == f_indices:
                matching_other.append(oname)

        # Generate GIF
        gif_path = pack_preview_dir / f"{anim_name}.gif"
        if len(anim_frames) > 1:
            anim_frames[0].save(
                gif_path,
                save_all=True,
                append_images=anim_frames[1:],
                duration=duration_ms,
                loop=0 if loop else 1,
                disposal=2
            )
        else:
            anim_frames[0].save(gif_path)

        # Generate filmstrip
        strip_w = cell_w * len(anim_frames)
        strip_h = cell_h + 24
        strip = Image.new('RGBA', (strip_w, strip_h), (35, 35, 45, 255))
        draw = ImageDraw.Draw(strip)
        for fi, (f_idx, f_img) in enumerate(zip(f_indices, anim_frames)):
            strip.paste(f_img, (fi * cell_w, 24), f_img)
            lbl = f"#{f_idx}"
            if is_fallback and fi == 0:
                lbl += f" ({resolved_name})"
            draw.text((fi * cell_w + 6, 4), lbl, fill=(220, 220, 240, 255))
            if fi > 0:
                draw.line([(fi * cell_w, 0), (fi * cell_w, strip_h)], fill=(60, 60, 80, 255), width=1)
        strip_path = pack_preview_dir / f"{anim_name}_strip.png"
        strip.save(strip_path)

        audit_report[p][anim_name] = {
            'resolved_name': resolved_name,
            'is_fallback': is_fallback,
            'frames': f_indices,
            'fps': fps,
            'loop': loop,
            'unique_frames': unique_frames_count,
            'total_frames': len(f_indices),
            'is_static': is_static,
            'aliases': matching_other,
            'gif': str(gif_path),
            'strip': str(strip_path)
        }

# Print summary table
print(f"{'Pack':<20} | {'Animation':<10} | {'Resolved As':<12} | {'Frames':<16} | {'Unique':<6} | {'Status/Note'}")
print("-" * 90)
for p, anims in audit_report.items():
    for aname, info in anims.items():
        frames_str = str(info['frames'])
        if len(frames_str) > 16:
            frames_str = frames_str[:13] + "..."
        uniq_str = str(info.get('unique_frames', '-'))
        res_str = info['resolved_name'] + ("*" if info['is_fallback'] else "")
        note = []
        if info['is_fallback']:
            note.append(f"FALLBACK: {info['resolved_name']}")
        if info.get('is_static'):
            note.append('STATIC POSE')
        if info.get('aliases') and not info['is_fallback']:
            note.append(f"ALIAS: {','.join(info['aliases'])}")
        if not note:
            note.append('DISTINCT MOVEMENT')
        note_str = "; ".join(note)
        print(f"{p:<20} | {aname:<10} | {res_str:<12} | {frames_str:<16} | {uniq_str:<6} | {note_str}")
