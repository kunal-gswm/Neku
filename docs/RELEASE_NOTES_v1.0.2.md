# Neku v1.0.2 — Official Release Notes

**Release Date:** October 20, 2026  
**Platform:** Windows 10 / 11 (x64)  
**Framework:** .NET 10, C# 13, WPF  

Welcome to **Neku 1.0.2**, the Everyday Usability & Desktop Experience update for Neku!

Following the stability, performance, and accessibility foundation established in v1.0.1, **v1.0.2** focuses directly on the daily rhythm of ordinary Windows users: effortless task capture, instant focus sessions, real-time note synchronization, non-intrusive pet interactions, and seamless multi-window navigation.

---

## What's New in Neku 1.0.2

### 🧭 Productivity Hub Overview
- **Unified Daily Command Center:** Added an all-new **Overview** tab to the Productivity Hub dashboard (`Ctrl+Alt+P` or via right-click menu).
- **Today's Priorities at a Glance:** Directly view and complete tasks due today without having to switch tabs.
- **Instant Focus Presets:** Added one-click focus buttons (`15m Quick`, `25m Pomodoro`, `45m Deep`) on the Overview tab that immediately launch a focus countdown.
- **Recent Notes Preview:** View your latest notes with formatted relative timestamps and quick previews directly alongside your daily tasks.
- **Clean Empty States:** Friendly visual feedback when no tasks are due or no notes exist, maintaining clean alignment across the dashboard.

### ⚡ Fast Inline Task Capture
- **Header QuickTaskBox:** Added an inline task input bar to both the Tasks Window and the Dashboard Overview.
- **Keyboard-Driven Creation:** Press <kbd>Enter</kbd> or click **+ Add** to instantly capture a task without opening modal dialogs. Blank inputs are safely rejected.
- **Context-Aware Due Dates:** New tasks created while viewing the **Today** tab automatically default to today's date; tasks created on the **Upcoming** tab default to tomorrow; tasks created on **All** are saved with no due date.
- **Flicker-Free List Updates:** New tasks appear instantly in the list via reactive collection binding without full window reloads.

### 🔄 Real-Time Cross-Window Synchronization
- **Centralized Notes Event:** Introduced `NotesService.NotesChanged` with thread-safe UI Dispatcher marshaling.
- **Zero-Latency Dashboard Sync:** Changes made in Quick Notes (`Win+Alt+N`) or via CLI (`neku note add/remove`) instantly update the Dashboard Recent Notes card without requiring manual refresh.
- **Leak-Free Event Lifecycle:** Clean subscription in constructor and unsubscription in window unload/close hooks prevents WPF event handler memory leaks.

### 🪟 Non-Intrusive Pet Summoning & Smart Window Z-Order
- **Focus-Stealing Elimination:** Summoning or unhiding Neku via system tray double-click or global shortcut unhides the companion and triggers the friendly wave animation without stealing keyboard focus from your active foreground application.
- **Preserved Window Z-Order:** Refactored `BringToForeground(Window window)` to check `wasTopmost` state before activation, guaranteeing that productivity windows (Dashboard, Tasks, Notes) return to standard non-topmost Z-order and never get stuck permanently topmost.

### 💻 Full Terminal CLI 1.0.2 Integration
- Version synchronization across the lightweight `neku` CLI (`neku --version`, `neku status`, `neku doctor`, `neku task`, `neku note`, `neku focus`).
- JSON output diagnostics (`neku doctor --json`) verified for automated workflows and environment health.

---

## ⌨️ Global Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | **Summon / Launch** | Launches Neku if stopped, or restores and unhides the companion without stealing focus. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd> | **Command Palette / Hub** | Opens the global quick launcher and productivity hub. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Q</kbd> | **Graceful Exit** | Flushes unsaved state, cleans up global hooks, and cleanly exits the application. |

---

## 📦 Downloads & Verification

| Asset | Description | Size | SHA-256 Checksum |
|---|---|---|---|
| **`Neku-Setup-v1.0.2.exe`** | Windows Setup Installer (Recommended, per-user, non-admin) | 80.5 MB | `6f779390252df83ceec32e6e20354cea0f362213fdf661a635b78f4078850bc9` |
| **`Neku-v1.0.2-win-x64.zip`** | Portable Release Archive (includes `Neku.exe`, `bin/neku.exe`, `neku.cmd`, `LICENSE`, `README.md`) | 105.1 MB | `32dadea13d9a33857ad7d7e7da3e37a85dc235ce33ff6d85e170d8cbb244bc01` |
| **`SHA256SUMS.txt`** | Checksum manifest for automated verification | 180 B | — |

### Checksum Verification
You can verify your download integrity in PowerShell:
```powershell
Get-FileHash .\Neku-Setup-v1.0.2.exe -Algorithm SHA256
Get-FileHash .\Neku-v1.0.2-win-x64.zip -Algorithm SHA256
```

### System Requirements
- Windows 10 (version 1809 or higher) or Windows 11 (64-bit x64).
- No external .NET installation required (binaries are fully self-contained).
