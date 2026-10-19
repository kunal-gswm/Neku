# Neku v1.0.0 — Official Release Notes

**Release Date:** October 19, 2026  
**Platform:** Windows 10 / 11 (x64)  
**Framework:** .NET 10, C# 13, WPF  

Welcome to **Neku 1.0.0**, the first major production release of Neku, your mischievous cat-goblin desktop companion and local-first developer productivity ally!

---

## What's New in Neku 1.0.0

### 🐾 Desktop Pet Physics 2.0 & Desktop Toys
- **Natural Desktop Physics:** Companions now respond to screen edges, multi-monitor boundaries, and cursor proximity with gravity, jumping, pacing, and sleep behaviors.
- **Interactive Desktop Toys:** Spawn virtual toys (yarn ball, bouncy mouse, treats) that bounce across your desktop and react to your mouse and pet interactions.
- **Non-Intrusive & Configurable:** Full support for Presentation Mode, Quiet Mode, and Reduced Motion. Autonomous roaming can be toggled on/off instantly.

### 📥 Neku Dropzone
- **Drag-and-Drop Capture:** Drag files, selected text snippets, or web URLs directly onto your companion to immediately create a local note or task.
- **Safety by Design:** Zero automated file inspection, script execution, or network requests. Every drop is held in an explicit confirmation preview.

### 💻 System Health Companion
- **Ambient System Awareness:** Optional, lightweight local monitoring of CPU, RAM, and battery state.
- **Gentle Resource Indicators:** Visual companion reactions and HUD indicators signal high system pressure without blocking popups.
- **Break & Stretch Reminders:** Configurable cadence to keep developers moving during marathon coding sessions.

### 🎯 Productivity Hub & Command Palette
- **Command Palette (`Ctrl+Alt+P`):** Fast, keyboard-driven navigation to trigger focus sessions, change companions, open notes, or toggle features. If `Ctrl+Alt+P` is occupied by another application, Neku automatically attempts fallback to `Ctrl+Alt+Space`. The palette is also always accessible via the companion's right-click context menu and system tray.
- **Focus Timer (Pomodoro):** Configurable work and rest intervals with companion synchronization (focused work state vs. celebratory break state).
- **Local Tasks & Quick Notes:** Instant task tracker and scratchpad with atomic persistence to safeguard your data against application crashes.

### 🔍 DeskSense & Developer Radar
- **Workspace Awareness:** Tracks active development context and coding pace without reading keystrokes or sensitive file contents.
- **Local Coding-Agent Inspection:** Localhost-only (`127.0.0.1`) activity monitor for pairing with local agent tools.

---

## ⌨️ Global Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | **Summon / Launch** | Launches Neku if stopped, or restores and unhides the running instance without stealing focus. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd> | **Command Palette** | Opens the global quick launcher (fallback: <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Space</kbd> if combo is occupied; also in context menu). |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Q</kbd> | **Graceful Exit** | Flushes unsaved state, cleans up global hooks, and cleanly exits the application. |

---

## 🔒 Privacy & Architecture Principles

- **100% Local-First & Zero AI:** Neku runs entirely on your device. There are zero LLM calls, zero AI APIs, zero telemetry, and zero remote logging.
- **Localhost Boundary:** Inter-process communication and coding-agent monitoring are locked strictly to loopback (`127.0.0.1`).
- **Transparent Update Checking:** Optional GitHub release checks query the official public repository (`https://api.github.com/repos/kunal-gswm/Neku/releases/latest`) without sending user IDs, tokens, or telemetry.

---

## 📦 Downloads & Verification

| Asset | Description | Size | SHA-256 Checksum |
|---|---|---|---|
| **`Neku-Setup-v1.0.0.exe`** | Windows Setup Installer (Recommended, per-user, non-admin) | 57.9 MB | `49cc84f55b9b0e8da57b5e04d912037822489a23ed1f8410e00747bcb1defd33` |
| **`Neku-v1.0.0-win-x64.zip`** | Portable Release Archive (includes `Neku.exe`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `README.md`) | 73.5 MB | `c650ef0cad125365de901d5e3c3624cd0f287c472b62adec9d0f3e435deeff8a` |
| **`SHA256SUMS.txt`** | Checksum manifest for automated verification | 180 B | — |

### Checksum Verification
You can verify your download integrity in PowerShell:
```powershell
Get-FileHash .\Neku-Setup-v1.0.0.exe -Algorithm SHA256
Get-FileHash .\Neku-v1.0.0-win-x64.zip -Algorithm SHA256
```

### System Requirements
- Windows 10 (version 1809 or higher) or Windows 11 (64-bit x64).
- No external .NET installation required (binaries are fully self-contained).
