# Neku v1.0.5 — Official Release Notes

**Release Date:** October 22, 2026<br/>
**Platform:** Windows 10 / 11 (x64)<br/>
**Framework:** .NET 10, C# 13, WPF

Welcome to **Neku 1.0.5**, the Companion Studio 2.0 & Mascot Roster Overhaul update!

Building directly on the Daily Routines and Recurring Schedules introduced in v1.0.4, **v1.0.5** transforms Neku from a fixed mascot application into an extensible desktop companion platform. Users can now easily import, manage, and switch between original, cartoon, or anime character packs locally, backed by rigorous pre-commit security hardening and data safety.

---

## What's New in Neku 1.0.5

### 🐾 Companion Studio 2.0 & Extensible Character Packs
- **📦 One-Click Companion Pack Importer:**
  - Import companion packs directly from `.zip` archives or extracted folders into `%AppData%\Neku\Assets\pets\<packId>\` with zero code modifications or recompilation.
  - Automatic spritesheet geometry validation (positive dimensions, matching columns, and cell bounds).
  - Validates presence of the essential `idle` animation loop.
- **🔍 Filtered Catalog & Instant Search:**
  - Categorized browsing: *Mascots & Originals*, *Imported Custom Packs*, *Personal Companion*, and *Legacy Characters*.
  - Real-time search filter by companion name or pack ID.
  - Dynamic character cards displaying creator attribution, pack version, and custom badge tags.
- **Live Animated Preview & Control:**
  - Frame-accurate animation preview with interactive playback speed buttons (0.5x, 1x, 1.5x, 2x).
  - Interactive animation chips showing frame count and FPS tooltips.
  - Instant live activation: switch active companions without restarting the application.
  - In-app pack removal (with confirmation dialog and automatic fallback switch) and quick folder explorer shortcut.
- **Legacy Catalog Decluttering:**
  - Relegated older cat color variants (`cat-blue`, `cat-chocolate`, `cat-cream`, `cat-orange`, `cat-silver`, `cat-janet`) to a dedicated *Legacy Characters* view to provide a modern, uncluttered primary mascot experience while preserving 100% backward compatibility for existing saved settings.

### 💾 Automated Local Backup & Transactional Restore
- **Native Settings Integration:**
  - Accessible directly in **Settings → Data & Storage → Local Backups**, providing an integrated interface to create, inspect, validate, and restore backups without external utilities.
- **Complete Productivity Data Coverage:**
  - Packages and protects all local data files:
    - `tasks.json`: Active tasks, priority rankings, reminder states, recurring rules, and routine checklists.
    - `notes.json`: Quick Capture notes, timestamps, and pinned memos.
    - `settings.json`: Selected companion mascot, volume, auto-start, hotkeys, and window preferences.
    - `focus_history.json`: Pomodoro sessions, focus milestones, and completion statistics.
- **Embedded Manifest & SHA-256 Checksums:**
  - Every backup ZIP archive includes a structured `manifest.json` recording backup creation timestamps, app version (`1.0.5`), user descriptions, file sizes, and cryptographic SHA-256 hashes to guarantee uncorrupted restoration.
- **Pre-Restore Safety Snapshots:**
  - Prior to applying any backup, Neku automatically creates an immutable safety snapshot (`safety_backup_YYYYMMDD_HHMMSS_<id>.zip`) of the current data directory. If any restore is aborted or fails, current state is instantly recoverable.
- **Automated Rolling Retention Policy:**
  - Automatically prunes older archives while strictly preserving the 10 most recent backups to safeguard disk space without losing recovery history.
- **Corruption Auto-Recovery & Safe Rollback:**
  - On startup or manual restore, if any corrupted data file is encountered, the service stages extraction into an isolated temporary folder and validates checksums before committing changes. If validation fails, changes are cleanly discarded and active files remain intact.

### 🛡️ Pre-Commit Security Hardening & Containment
- **Path-Aware Containment Engine (`IsPathContained` & `IsDestinationContained`):**
  - Replaced naive prefix string matching with robust relative-path containment math.
  - Normalizes root paths with trailing directory separators and calculates canonical relative paths with `Path.GetRelativePath`.
  - Rejects traversal using both slash conventions (`../` and `..\`), rooted paths (`/path`, `\path`, `C:\path`, `\\server\share`), and drive-relative paths (`D:path`).
  - Completely eliminates sibling-directory prefix collision exploits (e.g. `C:\Temp\Neku_Pack_123` vs `C:\Temp\Neku_Pack_123-evil\file.png`).
- **Reparse-Point & Symbolic Link Protection (`HasReparsePointInPath` & `EnumerateSafeFiles`):**
  - Traversal checks reject attacker-controlled symbolic links or junctions in both archive extractions and folder imports.
  - Directory enumeration traverses regular directories only, preventing symlink directory escapes.
- **Strict File Type Allowlist:**
  - Permitted files in custom packs are restricted to safe data formats: `.json`, `.png`, `.bmp`, `.jpg`, `.jpeg`, `.txt`, `.md`.
  - All executable binaries, scripts, and libraries (`.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.scr`, `.msi`, `.jar`) are rejected.
- **Archive Resource Limits:**
  - Hard safety caps: Maximum 128 archive entries, 50 MB total uncompressed archive size, 20 MB single file size limit, and 4096×4096 px maximum image dimensions.
- **Atomic Staging, Deployment & Safe Rollback:**
  - Imports stage into an isolated temporary sandbox (`.staging_*` / `.zipstage_*`).
  - Two-phase replacement creates an atomic backup of existing pack directories, ensuring zero stale or orphaned files remain upon update and automatically rolling back on error.
- **Windows Reserved Device Name Sanitization:**
  - Pack IDs matching Windows reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`) are sanitized with a `-pack` suffix.
- **Process Invocation Hardening:**
  - Folder explorer invocation uses `ProcessStartInfo.ArgumentList` instead of shell command strings.

### 🎨 Artist & Modder Documentation
- **Comprehensive Companion Pack Guide (`docs/COMPANION_PACK_GUIDE.md`):**
  - Complete specifications for spritesheet dimensions, uniform grid layout, 32-bit RGBA alpha transparency, and row-major 0-indexed frame numbering.
  - Manifest schema reference including optional metadata (`author`, `version`, `license`, `sourceUrl`, `category`, `tags`).
  - Explanation of the engine's graceful fallback chain (e.g. missing `pet` or `typing` states fall back smoothly to `idle`).
  - Legal, copyright, and licensing guidance: explains personal local freedom for fan art while ensuring zero copyrighted assets are bundled in official releases.
- **5 Original Character Concepts for Community Sprite Artists:**
  - **Aegis (Kobo):** Futuristic hover-robot companion with an expressive teal visor and hover thrusters.
  - **Taro (Roly):** Mischievous cartoon tanuki/raccoon critter with an exaggerated bouncy trot.
  - **Ronin (Kuro):** Chibi stoic swordsman with a straw kasa hat and wooden training bokken.
  - **Pip (Kitsune):** Three-tailed ethereal spirit fox surrounded by orbiting glowing wisps.
  - **Momo (Shiori):** Diligent, cozy scholarly companion in a knit sweater with round spectacles.

### 🔒 Privacy & Personal Companion Guarantee
- **Strict Local Preservation:**
  - The personal girl companion (`src/DesktopPet/Assets/pets/neku-personal-local/`) remains preserved locally on the developer machine and selectable under the *Personal* category.
  - Verified 100% excluded from Git tracking, assembly resources, installers, portable ZIPs, and public documentation.

### 💻 Synchronized CLI 1.0.5
- Synchronized all solution projects and assemblies (`DesktopPet`, `Neku.Cli`, `Neku.Protocol`) to `1.0.5`.
- `neku --version` reports `Neku 1.0.5`.
- `neku doctor --json` reports synchronized `cliVersion: "1.0.5"`.

---

## ⌨️ Global Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl</kbd> + <kbd>I</kbd> | **Import ZIP** | Opens file picker to import a companion `.zip` archive (Companion Studio). |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>I</kbd> | **Import Folder** | Opens folder picker to import an extracted companion directory (Companion Studio). |
| <kbd>Ctrl</kbd> + <kbd>F</kbd> | **Search Companions** | Focuses the Companion Studio character search box. |
| <kbd>F5</kbd> | **Refresh Catalog** | Rescans disk and embedded packs. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd> | **Quick Capture** | Opens instant task/note capture modal from anywhere. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | **Summon / Launch** | Restores and unhides the companion without stealing focus. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd> | **Command Palette / Hub** | Opens the global quick launcher and productivity hub. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Q</kbd> | **Graceful Exit** | Flushes unsaved state, cleans up global hooks, and cleanly exits. |

---

## 📦 System Requirements & Privacy
- **OS:** Windows 10 (version 1809 or higher) or Windows 11 (64-bit x64).
- **Framework:** .NET 10, C# 13, WPF. Fully self-contained.
- **100% Local-First:** All companion packs, tasks, routines, notes, and preferences reside locally on your PC. Zero cloud dependencies, zero analytics, zero telemetry.
