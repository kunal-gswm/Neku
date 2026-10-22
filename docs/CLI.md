# Neku Command-Line Interface (CLI) Guide

The **Neku CLI** (`neku`) is a lightweight, native Windows terminal interface that allows developers and power users to control the Neku desktop companion, manage Pomodoro focus sprints, organize tasks, create quick notes, switch companions, and send build/test events directly from PowerShell, Windows Terminal, scripts, and CI/CD pipelines.

---

## Table of Contents

- [Overview & Architecture](#overview-architecture)
- [Installation & PATH Setup](#installation-path-setup)
- [Command Reference](#command-reference)
  - [General Commands](#general-commands)
  - [Pet Control](#pet-control)
  - [Focus Timer](#focus-timer)
  - [Task Management](#task-management)
  - [Quick Notes](#quick-notes)
  - [Companion Switching](#companion-switching)
  - [Developer Events](#developer-events)
- [Machine-Readable JSON Output](#machine-readable-json-output)
- [Exit Codes](#exit-codes)
- [Behavior When Neku Is Not Running](#behavior-when-neku-is-not-running)
- [Portable Usage](#portable-usage)
- [Security & IPC Model](#security-ipc-model)
- [Troubleshooting & Diagnostics](#troubleshooting-diagnostics)
- [Uninstalling](#uninstalling)

---

## Overview & Architecture

Neku consists of two companion executables designed to work in synergy:
1. **`Neku.exe`** — The native WPF desktop companion window, animations, physics engine, and productivity tools.
2. **`neku.exe`** — The standalone console CLI executable (`< 20ms` startup, zero WPF dependencies) that communicates with the running companion via local named pipes.

```
+-------------------------------------------------------------+
|                      PowerShell / Shell                     |
|                   `neku focus start --minutes 45`           |
+------------------------------+------------------------------+
                               | Named Pipe (Local Only)
                               | `\\.\pipe\Neku.CommandPipe.{user}`
                               v
+-------------------------------------------------------------+
|               Running Neku Desktop Companion                |
|                      (`Neku.exe` Host)                      |
|                                                             |
|  - PetWindow (Visual state, animations, speech bubbles)     |
|  - FocusTimerService (Pomodoro deadline accounting)         |
|  - TaskService (Atomic storage in %AppData%\Neku\tasks.json)|
|  - NotesService (Atomic storage in %AppData%\Neku\notes.json|
|  - Companion Pack Registry (Live sprite swapping)           |
+-------------------------------------------------------------+
```

### Key Principles
- **Local-First & Zero Telemetry:** No cloud dependencies, no AI models, no remote network ports.
- **Single Source of Truth:** Commands executed through the CLI interact directly with the running companion's state and database services, preventing race conditions or corruptions.
- **Fast & Responsive:** Small bounded payloads (`<= 64 KB`) with strict timeouts (`2500 ms`) so your terminal never hangs.

---

## Installation & PATH Setup

### Windows Installer (Recommended)
When installed via `Neku-Setup-v1.0.0.exe`:
- The desktop application is installed to `%LocalAppData%\Programs\Neku\Neku.exe`.
- The CLI executable is installed to `%LocalAppData%\Programs\Neku\bin\neku.exe`.
- The installer automatically and idempotently adds `%LocalAppData%\Programs\Neku\bin` to your **User `PATH`** environment variable (`HKCU\Environment\Path`) without requiring administrator elevation.
- **Note:** Existing open terminal windows may need to be restarted for the updated `PATH` to take effect. In newly opened PowerShell or Windows Terminal tabs, simply type:
  ```powershell
  neku --version
  ```

### Portable Installation
If using the portable `.zip` release archive:
1. Extract the `.zip` archive to a folder of your choice (e.g. `C:\Tools\Neku`).
2. Run commands using the provided root helper:
   ```powershell
   .\neku.cmd --help
   .\neku.cmd status
   ```
3. To enable global `neku` access from any directory, add the portable folder or its `bin` folder to your User `PATH`:
   ```powershell
   [Environment]::SetEnvironmentVariable(
       "Path",
       [Environment]::GetEnvironmentVariable("Path", "User") + ";C:\Tools\Neku\bin",
       "User"
   )
   ```

---

## Command Reference

### General Commands

#### `neku --help` / `neku -h`
Displays available command groups, syntax, and options.

```powershell
neku --help
```

#### `neku --version` / `neku -v`
Displays the current installed version (`Neku 1.0.0`).

```powershell
neku --version
```

#### `neku status`
Displays the real-time operational status of the desktop companion, active companion character, current animation state, focus timer, pending task count, and total notes.

```powershell
neku status
```

**Example Output:**
```text
Neku Desktop Companion (v1.0.0)
----------------------------------------
Companion   : Neku (Goblin companion)
Pet State   : Idle (Visible, Active)
Focus Timer : Focusing (23:45) - Task: Finish GameLingo tests
Tasks       : 3 pending (7 total)
Quick Notes : 12 notes
```

#### `neku doctor`
Performs an audit of the local installation, checking the CLI binary path, desktop executable resolution, named-pipe IPC connectivity, `%AppData%\Neku` storage databases (`tasks.json`, `notes.json`, `settings.json`), and User `PATH` configuration.

```powershell
neku doctor
```

**Example Output:**
```text
========================================
          Neku CLI Doctor               
========================================

 [ OK ] CLI Version      : 1.0.0 (C:\Users\username\AppData\Local\Programs\Neku\bin\neku.exe)
 [ OK ] Desktop Binary   : C:\Users\username\AppData\Local\Programs\Neku\Neku.exe
 [ OK ] Neku Running     : Yes (Active Companion: neku)
 [ OK ] Local IPC Pipe   : Neku.CommandPipe.username (Active)
 [ OK ] AppData Storage  : C:\Users\username\AppData\Roaming\Neku (OK)
 [ OK ] Tasks Database   : Found (7 tasks)
 [ OK ] Notes Database   : Found (12 notes)
 [ OK ] Settings File    : Found
 [ OK ] User PATH        : Configured

All system checks passed! Neku is healthy.
```

#### `neku launch`
Launches the Neku desktop companion if it is not running. If Neku is already running, brings it to the foreground, unhides it if hidden, and plays an acknowledgement reaction.

```powershell
neku launch
```

#### `neku quit`
Gracefully closes the running desktop companion, flushing all active notes, tasks, and settings to disk, stopping hooks, and releasing the single-instance mutex.

```powershell
neku quit
```

---

### Pet Control

Control the companion character directly from the command line without opening menus.

| Command | Description |
|:---|:---|
| `neku pet show` | Unhides the pet and restores desktop animations. |
| `neku pet hide` | Hides the pet window from view without exiting the app. |
| `neku pet pause` | Pauses physics and animation updates (reduces rendering to 0 FPS). |
| `neku pet resume` | Resumes physics and animation updates. |
| `neku pet status` | Displays current companion name, visibility, activity, and coordinates. |

**PowerShell Examples:**
```powershell
# Hide pet during a screen share or video call
neku pet hide

# Restore companion after meeting
neku pet show

# Check pet location and state
neku pet status
```

---

### Focus Timer

Manage deadline-based Pomodoro focus sessions. Companion animations synchronize automatically (focused typing during sprints, relaxation during breaks).

| Command | Description |
|:---|:---|
| `neku focus start [--minutes M] [--task T] [--project P]` | Starts a focus sprint (default: 25 mins). |
| `neku focus status` | Displays remaining time and active task. |
| `neku focus pause` | Pauses the countdown timer. |
| `neku focus resume` | Resumes the paused timer. |
| `neku focus stop` | Cancels the active focus session. |

**PowerShell Examples:**
```powershell
# Start a 45-minute focus session
neku focus start --minutes 45

# Start a 25-minute sprint linked to a task
neku focus start --minutes 25 --task "Refactor IPC parser" --project "Neku"

# Check remaining time
neku focus status

# Cancel the sprint
neku focus stop
```

---

### Task Management

Manage local tasks saved in `%AppData%\Neku\tasks.json`. Thread-safe atomic writes ensure tasks stay synchronized with the desktop UI.

| Command | Description |
|:---|:---|
| `neku task add <title> [options]` | Adds a new task (`--priority <low\|normal\|high\|urgent>`, `--project <name>`). |
| `neku task list [--all]` | Lists pending tasks (use `--all` to view completed tasks). |
| `neku task complete <id>` | Marks a task as completed (supports full ID or 8-character prefix). |
| `neku task delete <id>` | Deletes a task from the database. |

**PowerShell Examples:**
```powershell
# Add tasks with priority and project tags
neku task add "Finish GameLingo tests" --priority High --project "Testing"
neku task add "Update release notes" --priority Normal

# View pending tasks
neku task list

# Mark task as complete using short prefix
neku task complete a1b2c3d4
```

---

### Quick Notes

Create, search, and view quick scratchpad notes saved in `%AppData%\Neku\notes.json`.

| Command | Description |
|:---|:---|
| `neku note add --title <T> --body <B>` | Creates a new note. |
| `neku note list` | Lists recent notes with ID, title, and timestamp. |
| `neku note search <query>` | Searches note titles and content matching `<query>`. |
| `neku note view <id>` | Displays the complete note title and full body. |
| `neku note delete <id>` | Deletes a note by ID. |

**PowerShell Examples:**
```powershell
# Create a note
neku note add --title "Ideas" --body "CLI improvements for build scripts"

# Search notes
neku note search "CLI"

# Read note content
neku note view a1b2c3d4
```

> **Privacy Note:** `neku note list` and `neku note search` print only brief preview snippets to keep private information off terminal history. Full content is only printed when explicitly requested via `neku note view <id>`.

---

### Companion Switching

Switch the active desktop companion live at runtime.

| Command | Description |
|:---|:---|
| `neku companion list` | Discovers and lists all built-in and custom companion packs. |
| `neku companion set <name>` | Switches active companion (e.g. `neku`, `cat`, `billu`, `cat-orange`). |

**PowerShell Examples:**
```powershell
# Discover companions
neku companion list

# Switch to Billu the Reel Goblin
neku companion set billu

# Switch back to Neku
neku companion set neku
```

---

### Developer Events

Trigger companion celebrations or reactions directly from build scripts, git hooks, or CI runs.

| Command | Description |
|:---|:---|
| `neku event build success [-m <msg>]` | Triggers a celebratory reaction for a successful build. |
| `neku event build failure [-m <msg>]` | Triggers an annoyed reaction for a broken build. |
| `neku event test pass [-m <msg>]` | Triggers a proud reaction when all unit tests pass. |
| `neku event test failure [-m <msg>]` | Triggers a sympathetic reaction for failing tests. |

**PowerShell Script Integration Example:**
```powershell
# Run your build and report outcome directly to your desktop pet:
dotnet build -c Release
if ($LASTEXITCODE -eq 0) {
    neku event build success --message "Release build succeeded!"
} else {
    neku event build failure --message "Build broke in Release mode."
}
```

---

## Machine-Readable JSON Output

Every command supports the `--json` flag, producing structured JSON output ideal for automation scripts, terminal tools, and integrations.

```powershell
neku status --json
```

**JSON Output Example:**
```json
{
  "ok": true,
  "message": "Neku is running.",
  "code": 200,
  "data": {
    "running": true,
    "version": "1.0.0",
    "activeCompanion": "neku",
    "companionDisplayName": "Neku (Goblin companion)",
    "isPetHidden": false,
    "isPetPaused": false,
    "petState": "Idle",
    "focus": {
      "state": "Focusing",
      "remaining": "24:12",
      "remainingSeconds": 1452,
      "task": "CLI Polish"
    },
    "tasks": {
      "total": 5,
      "pending": 2,
      "completed": 3
    },
    "notes": {
      "total": 8
    }
  }
}
```

**PowerShell Automation Example:**
```powershell
# Extract remaining focus time using PowerShell ConvertFrom-Json:
$status = neku status --json | ConvertFrom-Json
if ($status.ok -and $status.data.focus.state -eq "Focusing") {
    Write-Host "Focus sprint remaining: $($status.data.focus.remaining)" -ForegroundColor Cyan
}
```

---

## Exit Codes

The Neku CLI adheres to standard Windows console exit code conventions:

| Exit Code | Meaning | Details |
|:---:|:---|:---|
| `0` | **Success** | The command completed successfully. |
| `1` | **Runtime Error / App Offline** | Neku companion is not running, IPC timed out, or requested entity was not found. |
| `2` | **Usage / Argument Error** | Unknown command, missing mandatory arguments, or invalid option value. |

**PowerShell Checking Example:**
```powershell
neku task complete "invalid-id"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Task completion failed with exit code $LASTEXITCODE" -ForegroundColor Red
}
```

---

## Behavior When Neku Is Not Running

When the desktop companion (`Neku.exe`) is not currently active:
- `neku launch` automatically resolves the companion binary and starts it.
- `neku doctor` runs offline system diagnostics (checking databases, files, PATH, and binary availability).
- Commands requiring an active companion (`pet`, `focus`, `status`, `event`, `quit`) gracefully return exit code `1` with an actionable message:
  ```text
  Neku desktop companion is not running.
  Start it with 'neku launch' or from the Windows Start menu.
  ```
  If `--json` is supplied, it outputs structured JSON with `"isNotRunning": true`:
  ```json
  {
    "ok": false,
    "error": "Neku desktop companion is not running.\nStart it with 'neku launch' or from the Windows Start menu.",
    "isNotRunning": true
  }
  ```

---

## Portable Usage

If you run Neku portably without installing it:
- The portable folder layout:
  ```text
  Neku-v1.0.0-win-x64/
  ├── Neku.exe           <- Desktop Companion
  ├── neku.cmd           <- Portable CLI launcher
  ├── bin/
  │   └── neku.exe       <- Standalone CLI executable
  ├── README.md
  ├── LICENSE
  └── THIRD_PARTY_NOTICES.md
  ```
- Running `.\neku.cmd status` automatically locates `bin\neku.exe` relative to the script directory, regardless of current working directory.

---

## Security & IPC Model

Neku was intentionally engineered without external web listeners or remote attack surfaces:
- **Local Named Pipes:** All IPC communication uses `\\.\pipe\Neku.CommandPipe.{username}`, scoped strictly to the current Windows user session.
- **No Open Network Ports:** The CLI does not open or listen on any TCP/UDP network ports.
- **Payload Bounds:** Request and response buffers are strictly capped at `64 KB` (`65,536 bytes`). Requests exceeding this limit are rejected immediately.
- **Timeout Protection:** Read/write operations have a strict timeout of `2500 ms` to avoid hanging processes.
- **No Remote Code Execution:** The IPC endpoint processes an explicit allowlist of strongly-typed application commands. It never executes arbitrary shell commands, scripts, or system invocations.

---

## Troubleshooting & Diagnostics

### Run the Doctor Command
Run `neku doctor` to check the status of your installation, files, and IPC pipe.

### Command `neku` Not Recognized
If PowerShell returns `"The term 'neku' is not recognized"`:
1. Ensure the installer has finished.
2. Restart your terminal window to refresh the `%PATH%` environment variable.
3. If running portably, use `.\neku.cmd` or add the folder containing `neku.exe` to your User PATH.

### Desktop App Not Responding
If commands time out:
1. Verify `Neku.exe` is running in Windows Task Manager.
2. Run `neku launch` to restore or bring the companion window to the front.
3. If necessary, quit and restart via `neku quit` followed by `neku launch`.

---

## Uninstalling

When you uninstall Neku from Windows **Settings > Apps > Installed apps**:
1. All application files in `%LocalAppData%\Programs\Neku` are removed.
2. The Inno Setup uninstaller cleanly removes `%LocalAppData%\Programs\Neku\bin` from your **User `PATH`**, preserving all other PATH entries.
3. Unrelated user data in `%AppData%\Neku` (`tasks.json`, `notes.json`) is preserved by default so your notes and tasks are never lost on upgrade or reinstall.
