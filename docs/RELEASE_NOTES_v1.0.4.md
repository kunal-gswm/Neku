# Neku v1.0.4 — Official Release Notes

**Release Date:** October 20, 2026<br/>
**Platform:** Windows 10 / 11 (x64)<br/>
**Framework:** .NET 10, C# 13, WPF

Welcome to **Neku 1.0.4**, the Daily Routines, Recurring Tasks & Reminder Reliability update!

Building directly on the local reminders and Quick Capture features introduced in v1.0.3, **v1.0.4** transforms Neku into a dependable, structured everyday desktop companion with robust recurring schedules, daily step-by-step routines, and rock-solid reminder reliability.

---

## What's New in Neku 1.0.4

### 🔄 Deterministic Recurring Tasks & Schedules
- **Expanded Recurrence Rules:** Set tasks to recur on explicit schedules:
  - **Daily:** Every calendar day.
  - **Weekdays (Mon–Fri):** Skips weekends automatically.
  - **Weekly:** Customizable multi-day selections (e.g. Mon, Wed, Fri).
  - **Monthly:** Clamped safely to month ends (e.g., 31st Jan -> 28th/29th Feb -> 31st Mar) while preserving the target day for future months.
- **Recurrence Editing Scope Safety:**
  - When changing the recurrence rule on an occurrence tied to an active schedule, Neku presents an explicit confirmation dialog asking whether to update the entire repeating schedule or detach only this occurrence as a standalone task.
- **Occurrence Catch-Up Policy:**
  - When your PC wakes up or restarts after extended downtime, Neku generates **at most one active uncompleted occurrence** per schedule.
  - Completely eliminates notification floods, duplicated tasks, and backlog clutter.
- **Occurrence Lifecycle & Skipping (⏭️):**
  - Mark an occurrence as completed or **Skip (⏭️)** it directly from the Tasks window or Dashboard.
  - Completing or skipping advances the schedule to the next occurrence without altering or erasing completed task history.
- **Schedule Management:**
  - Pause and resume schedules anytime.
  - Deleting a recurring schedule preserves all historical completed occurrences as standalone records.
  - Preserves reminder time-of-day offsets across recurring generations.

### ☀️ Daily Routines
- **Dedicated Native Routines UI:**
  - Added a full **⚡ Routines** navigation tab to the Productivity Hub / Dashboard window (also accessible via Command Palette `routines.open`).
  - Interactive two-pane interface:
    - **Routines Explorer:** Browse existing routines with real-time status badges, estimated total duration, and empty state guides.
    - **Routine Editor & Runner:** Edit routine icon, title, and description; track active step execution with live progress counters; start, pause, complete, or skip steps; and restart completed routines for subsequent cycles.
    - **Step Reordering & Management:** Add steps with duration estimates, reorder steps dynamically with up/down controls, and delete steps with instant persistence.
- **Step-by-Step Daily Rituals:**
  - Create and manage multi-step daily rituals (e.g. *Morning Setup*, *Focus Session*, *Evening Reset*).
  - Track progress step-by-step with individual step titles, notes, and estimated durations.
  - Reset routines cleanly for each new cycle while keeping routine definitions permanent.
- **Local Persistence Schema (v3):**
  - Persisted locally in `tasks.json` (Version 3) alongside tasks and schedules.
  - Fully backward compatible with legacy v1 and v2 formats with automatic migration and corrupt file protection.

### 🛡️ Reminder Reliability Hardening
- **Power Resume & Time Change Reconciliation:**
  - System resume from sleep or hibernation (`SystemEvents.PowerModeChanged`) and manual/time-zone clock adjustments (`SystemEvents.TimeChanged`) immediately trigger schedule and reminder reconciliation.
- **Notification Lifecycle & Lock Cleanup:**
  - Fixed notification window closing lifecycle: closing an alert window instantly releases internal alert locks, preventing alerts from getting trapped in memory.
  - Auto-close timeouts (45s individual, 30s consolidated) snooze reminders cleanly.
  - Keyboard shortcuts <kbd>Esc</kbd> and <kbd>Ctrl</kbd> + <kbd>W</kbd> dismiss floating reminder notifications cleanly.
  - Consolidated reminder notifications accurately register all represented task IDs with notification lifecycle managers.

### 🎨 UI & Productivity Hub Polish
- **Dedicated Skip Controls (⏭️):**
  - Added dedicated Skip buttons to `TasksWindow` and `DashboardWindow` for quick recurring task skips.
- **Comprehensive Recurrence Selectors:**
  - Updated recurrence dropdowns across the application to support *None*, *Daily*, *Weekdays (Mon-Fri)*, *Weekly*, and *Monthly*.
- **Task & Schedule Separation:**
  - Active recurring occurrences integrate seamlessly into Today, Upcoming, and All filters.

### 💻 Synchronized CLI 1.0.4
- Synchronized all assemblies and CLI tools to `1.0.4`.
- `neku --version` outputs `Neku 1.0.4`.
- `neku doctor --json` reports updated CLI and protocol metadata.

---

## ⌨️ Global Keyboard Shortcuts

| Shortcut | Action | Description |
|---|---|---|
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>C</kbd> | **Quick Capture** | Opens instant task/note capture modal from anywhere. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> | **Summon / Launch** | Restores and unhides the companion without stealing focus. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>P</kbd> | **Command Palette / Hub** | Opens the global quick launcher and productivity hub. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Q</kbd> | **Graceful Exit** | Flushes unsaved state, cleans up global hooks, and cleanly exits. |

---

## 📦 System Requirements & Privacy
- Windows 10 (version 1809 or higher) or Windows 11 (64-bit x64).
- Fully self-contained: no external .NET runtime installation required.
- **100% Local-First Architecture:** Zero accounts, zero analytics, zero telemetry, zero external network requests.
