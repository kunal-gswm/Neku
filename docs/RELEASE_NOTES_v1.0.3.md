# Neku v1.0.3 — Official Release Notes

**Release Date:** October 20, 2026  
**Platform:** Windows 10 / 11 (x64)  
**Framework:** .NET 10, C# 13, WPF  

Welcome to **Neku 1.0.3**, the Daily Companion, Local Reminders & Quick Capture update!

Following the everyday usability improvements in v1.0.2, **v1.0.3** introduces two core daily companion capabilities: a dependable, non-intrusive **Local Reminder System** and an instant **Quick Capture** popup, designed from the ground up for ordinary Windows users.

---

## What's New in Neku 1.0.3

### ⏰ Reliable Local Task Reminders
- **Dedicated Reminder Scheduling:** Set explicit reminder timestamps on any task (distinct from the calendar `DueDate`), with fast presets:
  - `+5m` (Quick nudge)
  - `+15m` (Quarter-hour check)
  - `+1h` (Next hour)
  - `Later Today` (End of afternoon)
  - `Custom / Clear`
- **Native Non-Intrusive Floating Notification (`ReminderNotificationWindow`):**
  - Uses native Windows `WS_EX_NOACTIVATE` and `MA_NOACTIVATE` window styles so incoming notifications **never steal keyboard focus** or disrupt your typing, full-screen gaming, or active meetings.
  - Positioned cleanly in the lower-right corner above the Windows taskbar.
  - Action buttons: **Complete** (single task) / **Review Tasks** (consolidated view), **Snooze** (with selectable `5m`, `15m`, `60m` intervals or `Snooze All`), and **Dismiss**.
- **Companion Alert Wave:** Neku gently waves to notify you when a reminder arrives, accompanied by friendly speech bubble feedback (safely silenced if Quiet Mode or Quiet Hours are active).
- **Missed Reminder Flood Prevention:**
  - If more than 3 reminders become due simultaneously (e.g. waking your PC from overnight sleep), Neku presents a single consolidated notification summary with a **Review Tasks** action to inspect and manage them in the Tasks window instead of cascading intrusive popups.
  - Reminders older than 24 hours are automatically cleared without noisy alerts.
- **Quiet Hours & Presentation Mode Suppression:**
  - Configurable Quiet Hours (default: `22:00` to `08:00`) suppress alerts overnight and gracefully drain pending notifications once quiet hours conclude.
  - Full-screen applications and presentations automatically silence reminders.
- **Restart Persistence & Zero Data Loss:**
  - Reminder timestamps are stored in UTC format in `tasks.json`.
  - Automatic reconciliation when tasks are marked complete or deleted, immediately closing active alert windows.

### ⚡ Instant Quick Capture (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd>)
- **Lightweight Global Popup (`QuickCaptureWindow`):**
  - Press <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd> from anywhere in Windows to summon a compact, beautiful floating input modal.
  - Also accessible via the Pet context menu, system tray menu, and Command Palette (<kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd>).
- **Segmented Task & Note Modes:**
  - Easily toggle between **Task Mode** (creates a task due Today) and **Note Mode** (creates a note with the first line as title) via buttons or by pressing <kbd>Tab</kbd> on an empty input.
- **Keyboard-First Workflow:**
  - Press <kbd>Enter</kbd> to save.
  - Press <kbd>Shift</kbd> + <kbd>Enter</kbd> for multi-line notes.
  - Press <kbd>Esc</kbd> to dismiss.
  - Instant validation prevents blank submissions with subtle error/success feedback animations.

### ⚙️ Enhanced Settings & Customization
- Added reminder toggles and Quiet Hours configuration directly to the **Settings** window.
- Added configurable Quick Capture global hotkey settings.
- Validated fallbacks ensure corrupted or missing settings default to safe, reasonable values.

### 💻 Synchronized CLI 1.0.3
- Synchronized all assemblies and CLI tools to `1.0.3`.
- `neku --version` outputs `Neku 1.0.3`.
- `neku doctor --json` reports updated CLI and protocol metadata.

---

## ⌨️ Global Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd> | **Quick Capture** *(New)* | Opens instant task/note capture modal from anywhere. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | **Summon / Launch** | Restores and unhides the companion without stealing focus. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd> | **Command Palette / Hub** | Opens the global quick launcher and productivity hub. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Q</kbd> | **Graceful Exit** | Flushes unsaved state, cleans up global hooks, and cleanly exits. |

---

## 📦 System Requirements
- Windows 10 (version 1809 or higher) or Windows 11 (64-bit x64).
- Fully self-contained: no external .NET runtime installation required.
- Local-first architecture: zero accounts, telemetry, or external cloud dependencies.
