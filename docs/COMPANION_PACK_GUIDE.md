# Neku Companion Studio 2.0 — Custom Pack & Artist Guide

This guide details how to create, package, validate, and import custom desktop companions for **Neku**. Whether you are creating an original mascot, designing pixel art, or importing fan art for personal desktop use, this document provides the exact technical specifications, schema requirements, and best practices.

---

## 1. System Overview

**Companion Studio 2.0** provides a local-first custom companion architecture:
- **Zero code required:** Anyone can create and distribute companion packs without compiling C# or modifying application binaries.
- **Import workflows:** Load characters directly from a local folder or a `.zip` archive via the Companion Studio interface.
- **Safe isolation:** User packs are installed to per-user application storage (`%AppData%\Neku\Assets\pets\<packId>`) without modifying core application files.
- **Resilient animation engine:** Packs only need a valid spritesheet and an `idle` animation. If advanced animations (such as `pet` or `typing`) are omitted, the engine automatically falls back to base animations without interrupting playback.

```mermaid
flowchart LR
    Source["Folder or .ZIP Archive\n(manifest.json + spritesheet.png)"] --> Importer["Companion Studio 2.0\n(CompanionPackService)"]
    Importer --> Staging["Transactional Staging &\nSecurity Validation"]
    Staging --> Storage["%AppData%\\Neku\\Assets\\pets\\<id>\\]
    Storage --> Catalog["Companion Studio Catalog\n(Filter, Search, Preview)"]
    Catalog --> Runtime["Active Pet Simulation\n(DesktopPet Engine)"]
```

---

## 2. Spritesheet Geometry & Image Specifications

A companion pack requires a single contiguous 2D grid spritesheet containing all character animation frames.

### Image Requirements

| Property | Requirement | Notes |
| :--- | :--- | :--- |
| **File Format** | `.png` (recommended), `.bmp`, `.jpg` | 32-bit RGBA PNG with alpha transparency is strongly recommended. |
| **Color Depth** | 24-bit RGB or 32-bit RGBA | Full alpha channel transparency prevents black or white bounding boxes. |
| **Grid Layout** | Regular 2D Grid (Columns × Rows) | All frames must share identical cell width and cell height. |
| **Frame Indexing**| Row-major, 0-indexed | `Index = (Row × Columns) + Column`. Top-left frame is index `0`. |
| **Recommended Cell Size** | `64 × 64` px or `128 × 128` px | Any positive integer dimensions are supported. |
| **Recommended Display Scale** | `1.0` (pixel-native) to `2.0` | Controlled by the `"scale"` field in `manifest.json`. |

### Frame Layout Example

For a spritesheet with `columns = 4` and `cellWidth = 64, cellHeight = 64`:

```
+-----------+-----------+-----------+-----------+
|  Frame 0  |  Frame 1  |  Frame 2  |  Frame 3  |  <- Row 0 (Y: 0..63)
+-----------+-----------+-----------+-----------+
|  Frame 4  |  Frame 5  |  Frame 6  |  Frame 7  |  <- Row 1 (Y: 64..127)
+-----------+-----------+-----------+-----------+
|  Frame 8  |  Frame 9  |  Frame 10 |  Frame 11 |  <- Row 2 (Y: 128..191)
+-----------+-----------+-----------+-----------+
```

Total image dimensions must be at least `(columns * cellWidth)` wide and `(rows * cellHeight)` high.

---

## 3. Manifest Specification (`manifest.json`)

Every companion directory must contain a `manifest.json` file.

### Complete Schema Example

```json
{
  "name": "Aegis",
  "description": "Futuristic hover-robot companion with an expressive teal visor.",
  "author": "PixelStudio",
  "version": "1.0.0",
  "license": "CC-BY-4.0",
  "sourceUrl": "https://github.com/example/aegis-companion",
  "category": "Original",
  "tags": ["robot", "cyberpunk", "futuristic", "floating"],
  "sheet": "spritesheet.png",
  "cellWidth": 64,
  "cellHeight": 64,
  "columns": 4,
  "scale": 1.2,
  "animations": {
    "idle": {
      "frames": [0, 1, 2, 3],
      "fps": 4.0,
      "loop": true
    },
    "walk": {
      "frames": [4, 5, 6, 7],
      "fps": 6.0,
      "loop": true
    },
    "sleep": {
      "frames": [8, 9, 10, 11],
      "fps": 2.0,
      "loop": true
    },
    "fall": {
      "frames": [12, 13],
      "fps": 5.0,
      "loop": true
    },
    "drag": {
      "frames": [14, 15],
      "fps": 4.0,
      "loop": true
    },
    "pet": {
      "frames": [16, 17, 18],
      "fps": 6.0,
      "loop": false
    },
    "typing": {
      "frames": [19, 20, 21, 22],
      "fps": 8.0,
      "loop": true
    }
  }
}
```

### Schema Field Reference

#### Metadata Fields

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `name` | string | **Yes** | Display name shown in Companion Studio. |
| `description`| string | Optional | Overview of character personality and visual style. |
| `author` | string | Optional | Creator or artist credit displayed in the companion card. |
| `version` | string | Optional | Semantic version (e.g. `1.0.0`). |
| `license` | string | Optional | License type (e.g. `CC-BY-4.0`, `MIT`, `Personal Use`). |
| `sourceUrl` | string | Optional | Web link or portfolio URL. |
| `category` | string | Optional | Grouping category (e.g. `Original`, `Anime`, `Cartoon`, `Gaming`). |
| `tags` | string[] | Optional | Search keywords for filtering in Companion Studio. |

#### Geometry Fields

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `sheet` | string | **Yes** | Relative filename of the spritesheet image (e.g. `spritesheet.png`). |
| `cellWidth` | integer| **Yes** | Width in pixels of a single frame cell (must be > 0). |
| `cellHeight`| integer| **Yes** | Height in pixels of a single frame cell (must be > 0). |
| `columns` | integer| **Yes** | Number of frame columns in the spritesheet grid (must be > 0). |
| `scale` | double | Optional | Rendering scale multiplier (default: `1.0`). |

#### Animation Definitions

Each animation in `"animations"` maps an animation key to an object with:

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `frames` | int[] | **Yes** | 0-indexed list of grid frame indices played in sequence. Must not be empty. |
| `fps` | double | Optional | Playback speed in frames per second (must be > 0, default: `4.0`). |
| `loop` | boolean| Optional | `true` loops continuously; `false` halts on the final frame (default: `true`). |

---

## 4. Animation States & Fallback Chain

Neku supports a rich set of interactive and ambient behaviors. However, **only `idle` is strictly required**.

### Supported Animation Keys

| Animation Key | Engine Trigger | Purpose / Description | Fallback If Missing |
| :--- | :--- | :--- | :--- |
| `idle` | Ambient | **Required.** Default standing or floating breathing loop. | *None (Validation Error)* |
| `walk` | Ambient / Motion | Moving across window tops or screen floor. | `idle` |
| `sleep` | Inactivity | Deep sleep or power-saving snooze. | `idle` |
| `fall` | Physics | Freefall drop when dragged off an edge. | `idle` |
| `drag` | User Interaction | Cursor pickup and holding state. | `fall` -> `idle` |
| `eat` | User Interaction | Feeding snack or treat interaction. | `happy` -> `idle` |
| `pet` | User Interaction | Cursor stroke or click reaction. | `happy` -> `idle` |
| `stretch` | Ergonomic Nudge | Periodic health break / stretch nudge. | `idle` |
| `typing` | Keystroke Burst | Fast reaction when user types rapidly on keyboard. | `idle` |
| `talk` | Reminder / Dialog | Active speech bubble or notification alert. | `idle` |
| `blink` | Ambient | Eye-blink variation during idle. | `idle` |
| `sit` | Ambient | Resting seated posture on taskbar or title bar. | `idle` |
| `happy` | Positive Feedback | Task completion, streak celebration, or cheer. | `idle` |
| `annoyed` | Disturbance | Fast repeated clicking or cursor agitation. | `idle` |
| `surprised` | Audio / Alert | Sudden sound or popup window reaction. | `idle` |

> [!TIP]
> **Minimalist Packs:** You can build a fully functional companion pack with just 4 to 8 frames defining `idle`, `walk`, and `sleep`. Neku's engine gracefully handles missing interactive animations without throwing exceptions or showing blank frames.

---

## 5. Packaging & Distribution

### Folder Layout

Packs can be imported directly as a folder or bundled into a `.zip` file:

```
my-custom-companion/
├── manifest.json
└── spritesheet.png
```

*Note: Nested subfolders are automatically detected if `manifest.json` is located in the root or a single top-level folder of the ZIP archive.*

### Archive Safety & Limits

To protect system integrity, Neku's pack importer enforces strict security validations:
1. **Path Traversal Protection:** Entries containing `../` or absolute drive roots (`C:\`) are immediately rejected.
2. **File Size Limits:** Uncompressed archive size cannot exceed **50 MB**.
3. **Entry Count Limits:** Archives cannot contain more than **500 entries**.
4. **Executable Rejection:** Binaries and scripts (`.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.scr`, `.pif`) are strictly forbidden and will abort the import.
5. **Transactional Staging:** Files are extracted into an isolated sandbox staging directory first. Only after validation succeeds is the pack moved into `%AppData%\Neku\Assets\pets\<packId>`. If validation fails, all temporary files are purged.

---

## 6. How to Import in Companion Studio 2.0

1. Launch **Neku**.
2. Right-click the desktop pet or the system tray icon, and choose **Companion Studio** (or press the Studio shortcut).
3. In the toolbar at the top right:
   - Click **📦 Import ZIP...** to import a `.zip` archive.
   - Click **📁 Import Folder...** to select an extracted folder.
4. Companion Studio validates the assets and immediately renders the live animated preview.
5. Review the preview, test different animations using the animation chips, and click **Set Active Companion** to switch your companion instantly.
6. To remove an imported custom companion, select it in the catalog and click **🗑 Remove Pack** (requires confirmation).

---

## 7. Legal, Copyright & Licensing Rules

Neku is an open-source, local-first application. To protect contributors, distributors, and users:

- **Local & Personal Freedom:** Users are free to import, test, and use their own character sprites (including fan creations, cartoon characters, or anime sprites) locally on their own machines.
- **No Copyrighted Franchise Bundling:** Never commit copyrighted character artwork, franchise logos, or proprietary audio assets to the public Neku repository or public release binaries.
- **Clean Fallback Guarantee:** Neku always bundles official, original artwork as its built-in fallback.
- **Accurate Attribution:** When sharing custom packs, clearly state the author, source repository, and license terms in the `manifest.json` metadata.

---

## 8. Original Companion Concepts for Community Artists

For community sprite artists seeking inspiration for official or public packs, here are 5 distinct original character concepts designed specifically for Neku's desktop simulation:

### Concept 1: Aegis (Kobo) — Futuristic Hover-Robot
- **Archetype:** Helpful, high-tech cybernetic desktop assistant.
- **Visual Style:** Sleek white and gunmetal chrome casing with a glowing teal-cyan visor. Two miniature hovering micro-thrusters in place of legs.
- **Palette:** `#ECEFF1` (Chassis), `#263238` (Accents), `#00E5FF` (Visor Glow), `#76FF03` (Battery OK), `#FF1744` (Low Power).
- **Signature Animations:**
  - `idle`: Gentle vertical hover oscillation with a subtle particle wisp.
  - `typing`: Visor displays binary code streams while tiny holographic antenna spins.
  - `sleep`: Docks in place, thrusters power down, visor displays `zzZ` low-light sleep status.
  - `pet`: Emits a cheerful blue sonar ping ring and visor flashes hearts.

### Concept 2: Taro (Roly) — Mischievous Cartoon Troublemaker
- **Archetype:** Energetic, slapstick cartoon critter (raccoon/tanuki hybrid).
- **Visual Style:** Plump, rounded silhouette with oversized paws, a bushy striped tail, and expressive cartoon eyes that widen and squint dramatically.
- **Palette:** `#8D6E63` (Fur), `#4E342E` (Stripes & Mask), `#FFF9C4` (Chest Fluff), `#FF7043` (Bandana).
- **Signature Animations:**
  - `idle`: Taps foot impatiently, twitches whiskers, and looks around for snacks.
  - `walk`: Bouncy, exaggerated cartoon sneak-trot.
  - `eat`: Gulping an entire pixel melon in one bite, cheeks puffing out.
  - `annoyed`: Pouts with arms crossed and steam playfully puffing from ears.

### Concept 3: Ronin (Kuro) — Chibi Stoic Swordsman
- **Archetype:** Calm, disciplined wandering martial artist.
- **Visual Style:** Chibi wanderer wearing a wide straw kasa hat, dark indigo hakama, and carrying a wooden training bokken across the back.
- **Palette:** `#1A237E` (Hakama), `#F5F5DC` (Tunic), `#D7CCC8` (Straw Hat), `#B71C1C` (Scarf Accent).
- **Signature Animations:**
  - `idle`: Stoic arms-folded stance; gentle breeze sways the red scarf ends.
  - `walk`: Composed, silent stride across window frames.
  - `sit`: Meditates cross-legged with eyes closed.
  - `happy`: Performs a brisk ceremonial bow with bokken held respectfully.

### Concept 4: Pip (Kitsune) — Ethereal Spirit Fox
- **Archetype:** Playful mythical desktop guardian.
- **Visual Style:** Fluffy three-tailed spirit fox that floats slightly above surfaces, surrounded by soft glowing ember orbs.
- **Palette:** `#FAFAFA` (Pure White Fur), `#FF5722` (Ear & Tail Tip Vermilion), `#FFD700` (Spirit Orbs).
- **Signature Animations:**
  - `idle`: Floating lazily with tails curling rhythmically, glowing spirit orbs orbiting.
  - `stretch`: Extends front paws and curls into a crescent arc while yawning.
  - `fall`: Tumbles head over heels like a dandelion seed before recovering gracefully.
  - `pet`: Rolls upside down in midair for belly rubs, purring with sparkling micro-particles.

### Concept 5: Momo (Shiori) — Scholarly Desktop Companion
- **Archetype:** Diligent, cozy study partner and reader.
- **Visual Style:** Warm knit oversized lavender sweater, round silver glasses, and carrying a tiny bookmark-ribboned journal.
- **Palette:** `#9575CD` (Lavender Knit), `#ECEFF1` (Collar), `#6D4C41` (Hair), `#80CBC4` (Journal Cover).
- **Signature Animations:**
  - `idle`: Holds journal open, adjusting glasses periodically.
  - `typing`: Types furiously on a tiny miniature mechanical keyboard.
  - `sleep`: Curled up against an oversized teacup or book stack.
  - `happy`: Gives an enthusiastic thumbs-up with a sparkle in glasses lenses.

---

## 9. Troubleshooting & FAQ

### Q: Why does Companion Studio say "Frame index X is out of bounds"?
**A:** Your `manifest.json` references a frame number that does not exist on your spritesheet image.
- Check: Total cells = `columns * floor(imageHeight / cellHeight)`.
- If your image is 256x256 with 64x64 cells and 4 columns, there are 16 cells (indices `0` to `15`). An index of `16` or higher will fail validation.

### Q: Can I use different cell sizes for different animations?
**A:** No. All frames in a single companion pack spritesheet must share the identical `cellWidth` and `cellHeight`. If some actions require more space, increase the uniform cell size for the whole sheet and pad frames with transparent margins.

### Q: Does Neku support audio effects in companion packs?
**A:** Current releases focus on sprite-based visual simulation and haptic interactions. Custom audio triggers will be introduced in future updates with strict volume and frequency safety limits.

### Q: Where are my custom companion packs stored on Windows?
**A:** `%AppData%\Neku\Assets\pets\<packId>\`. You can open this directory anytime by selecting your custom companion in Companion Studio and clicking **📁 Open Folder**.
