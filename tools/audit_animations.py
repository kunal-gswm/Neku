import json
from pathlib import Path
from PIL import Image
import numpy as np

packs = [p.name for p in sorted(Path('src/DesktopPet/Assets/pets').glob('*')) if p.is_dir() and 'personal' not in p.name.lower()]
target_anims = ['wave', 'celebrate', 'confused', 'laugh', 'pout', 'facepalm', 'typing', 'stretch', 'pet', 'proud']

for p in packs:
    man_path = Path(f'src/DesktopPet/Assets/pets/{p}/manifest.json')
    if not man_path.exists():
        print(f'{p}: missing manifest')
        continue
    data = json.loads(man_path.read_text(encoding='utf-8'))
    sheet_path = Path(f'src/DesktopPet/Assets/pets/{p}/{data["sheet"]}')
    if not sheet_path.exists():
        print(f'{p}: missing sheet {sheet_path}')
        continue
    img = Image.open(sheet_path).convert('RGBA')
    cell_w = data['cellWidth']
    cell_h = data['cellHeight']
    cols = data['columns']

    print(f'\n==================================================')
    print(f'PACK: {p} (Sheet: {img.size[0]}x{img.size[1]}, Cell: {cell_w}x{cell_h}, Cols: {cols})')
    print(f'==================================================')

    # Extract all frames as numpy arrays
    frames = {}
    max_cols = img.size[0] // cell_w
    max_rows = img.size[1] // cell_h
    total_cells = max_cols * max_rows

    for idx in range(total_cells):
        c = idx % cols
        r = idx // cols
        box = (c * cell_w, r * cell_h, (c + 1) * cell_w, (r + 1) * cell_h)
        cell_img = img.crop(box)
        frames[idx] = np.array(cell_img)

    # Check for identical frames across cells
    identical_pairs = []
    for i in range(total_cells):
        for j in range(i + 1, total_cells):
            if np.array_equal(frames[i], frames[j]):
                identical_pairs.append((i, j))
    if identical_pairs:
        print(f'Identical cell pairs detected: {len(identical_pairs)}')
        # Group identical frames
        # e.g. frames that are all zeros or copies

    anims = data.get('animations', {})
    print(f'Total animations: {len(anims)}')

    print('\nTarget Animations Analysis:')
    for name in target_anims:
        if name not in anims:
            print(f'  - {name}: [MISSING IN MANIFEST]')
            continue
        anim_def = anims[name]
        f_indices = anim_def.get('frames', [])
        fps = anim_def.get('fps', 0)
        loop = anim_def.get('loop', False)

        # Check against other animations with the exact same sequence
        matching_other = []
        for other_name, other_def in anims.items():
            if other_name == name:
                continue
            if other_def.get('frames', []) == f_indices:
                matching_other.append(other_name)

        # Check if frames within f_indices are all identical (static pose)
        is_static = False
        if len(f_indices) > 1:
            first_frame = frames[f_indices[0]]
            if all(np.array_equal(first_frame, frames[idx]) for idx in f_indices[1:]):
                is_static = True

        note = ""
        if matching_other:
            note += f" [DUPLICATES SEQUENCE OF: {', '.join(matching_other)}]"
        if is_static:
            note += " [STATIC: All frames identical]"

        print(f'  - {name:<10}: frames={f_indices}, fps={fps}, loop={loop}{note}')
