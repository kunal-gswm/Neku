# Neku v1.0.1 — Official Release Notes

**Release Date:** October 20, 2026  
**Platform:** Windows 10 / 11 (x64)  
**Framework:** .NET 10, C# 13, WPF  

Welcome to **Neku 1.0.1**, the first quality, performance, and polish update for Neku!

This release prioritizes ordinary Windows users who want a delightful, lightweight, and reliable everyday desktop companion. Rather than bloating the application with complex developer features, v1.0.1 focuses on smoothness, responsiveness, visual clarity, accessibility, and background efficiency.

---

## What's New in Neku 1.0.1

### 🐱 Smooth Animation & Natural Interactions
- **Jitter-Free Cursor Tracking:** Added a 12 DIP deadzone threshold across idle, bat, and petting states. The companion no longer flip-flops rapidly when the mouse rests near its center.
- **Natural Look-Around Transitions:** Pet animations now conclude their full motion cycle before returning to idle, eliminating abrupt frame-0 snapping.
- **Calm Pause Transitions:** Toggling pause now smoothly shifts the companion into a calm seated posture (`PetState.Sit`), dampens movement velocities, and clears active decorations.
- **Reduced Motion Expansion:** Energetic behaviors (celebrations, zoomies, spins, jumps, and pounces) map to serene seated or happy poses. Toggling Reduced Motion mid-movement instantly settles the companion.
- **Everyday Greeting:** Refreshed the startup greeting to a warm, friendly *"Hey there! Good to see you! ✨"*.

### 🎨 Visual Polish, Contrast & Theme Consistency
- **Dark Theme Contrast Correction:** Replaced hardcoded light backgrounds in the Settings build integration box with dynamic theme brushes, providing high-contrast readability.
- **Quick Notes Theme Integration:** Merged the canonical `ProductivityTheme.xaml` into Quick Notes, ensuring consistent button styles, proper danger button hover colors, and a clean empty-state illustration when search filters match 0 notes.
- **Restored Window Activation:** Minimized productivity hub windows now reliably restore to normal state before activation, preventing them from staying stuck in the taskbar.

### ⌨️ Full Keyboard Navigation & Accessibility (A11y)
- **Visible Keyboard Focus Rings:** Introduced a custom 2px purple focus ring (`ThemeFocusRingStyle`) across all buttons, filter chips, and navigation items. Users navigating via <kbd>Tab</kbd> now have clear visual feedback on the active element.
- **Screen Reader Support:** Added `AutomationProperties.Name` across Settings, Tasks, Quick Notes, and Dashboard navigation rails for Windows Narrator and assistive tools.

### 🧹 Decluttered Desktop Experience
- **Developer Tools Grouping:** The `🛠️ Developer Tools` submenu (`Developer Events…`, `Developer Radar…`, `Toggle Developer HUD`) is now completely hidden from the pet right-click menu and tray menu by default. It only appears dynamically when developer diagnostics are enabled in Settings.
- **Simplified Menus:** Streamlined top-level menu entries to keep everyday desktop interactions clean and intuitive.

### ⚡ Performance & Background Efficiency
- **Zero-Overhead Logging:** Debug disk logging (`DebugLog.Enabled`) is now disabled by default and requires explicit opt-in via environment variables (`NEKU_DEBUG=1` or `PIXELPAWS_DEBUG=1`), eliminating synchronous UI-thread disk appends.
- **Redundant WPF Redraw Elimination:** Guarded image source and transform assignments in the companion view, preventing unnecessary visual tree invalidations.
- **Timer Color Caching:** Pre-allocated static structs for color conversions in the dashboard timer loop, cutting allocations during active focus sessions.
- **Measurable Gains:** Cold startup is **8.2% faster** (932.7 ms vs 1,016.5 ms), and steady-state background memory usage is **11.6% lower** (-17.88 MB Working Set).

### 💻 Full Terminal CLI Integration
- Complete integration with the lightweight `neku` CLI command line (`neku status`, `neku pet`, `neku focus`, `neku task`, `neku note`, `neku companion`, `neku doctor`).
- Self-contained portable wrapper (`neku.cmd`) included in the portable bundle.

---

## ⌨️ Global Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | **Summon / Launch** | Launches Neku if stopped, or restores and unhides the running instance without stealing focus. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd> | **Command Palette** | Opens the global quick launcher (fallback: <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Space</kbd> if combo is occupied; also in context menu). |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Q</kbd> | **Graceful Exit** | Flushes unsaved state, cleans up global hooks, and cleanly exits the application. |

---

## 📦 Downloads & Verification

| Asset | Description | Size | SHA-256 Checksum |
|---|---|---|---|
| **`Neku-Setup-v1.0.1.exe`** | Windows Setup Installer (Recommended, per-user, non-admin) | 80.5 MB | `386349afb52aaaf6f0732f97581dce0d97bc97009eb249ea81120f94544a7d97` |
| **`Neku-v1.0.1-win-x64.zip`** | Portable Release Archive (includes `Neku.exe`, `bin/neku.exe`, `neku.cmd`, `LICENSE`, `README.md`) | 105.1 MB | `b79e5c680362e6af9b9ebae00052dcf5fee9654a1959fd4dbf15a2ddf1e656f5` |
| **`SHA256SUMS.txt`** | Checksum manifest for automated verification | 180 B | — |

### Checksum Verification
You can verify your download integrity in PowerShell:
```powershell
Get-FileHash .\Neku-Setup-v1.0.1.exe -Algorithm SHA256
Get-FileHash .\Neku-v1.0.1-win-x64.zip -Algorithm SHA256
```

### System Requirements
- Windows 10 (version 1809 or higher) or Windows 11 (64-bit x64).
- No external .NET installation required (binaries are fully self-contained).
