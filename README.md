# Neku v0.4 — A Lightweight, Playful & Productive Windows Desktop Companion

**Neku** is a tiny, mischievous desktop companion that lives right on your Windows desktop. Native, feather-light, and expressive, Neku combines playful desktop pet physics with a local Pomodoro focus timer, a quick-notes scratchpad, developer event reactions, and a full Companion Studio.

Built with **C# / WPF on .NET 10**.

---

## 🌟 What's New in v0.4 (Companion Studio)

- 🎨 **Companion Studio:** Complete management interface for Neku and public companion packs. Browse characters, preview genuine animation frames at 60 FPS, and swap companions in-flight without restarting the application.
- 🎭 **Personality Presets & Reaction Triggers:** Tune companion temperament with `Calm`, `Playful`, `Expressive`, and `Custom` presets. Configure granular reactions for Pomodoro focus milestones, breaks, cursor petting, and optional developer builds/tests.
- 📏 **Granular Scaling & Appearance Controls:** Custom size scale (0.5x to 2.0x), animation speed multiplier, and window opacity sliders.
- ♿ **Protected Accessibility:** "Restore Defaults" safely resets companion personality without overwriting user accessibility preferences (`ReducedMotion` and `EnableQuietMode`).
- ⏱️ **Focus Timer & Quick Notes:** 25-minute Pomodoro focus timer with break alerts, HUD overlay, and built-in local desktop notes scratchpad.
- ⚡ **Optimized Memory & Lifecycle:** On-demand full-screen composition surfaces, memory-mapped single-file binary, and optional software rendering (`--software-rendering`).
- 🛡️ **Privacy & Custom Pack Isolation:** 100% offline, zero telemetry. Supports custom local companion packs stored securely in `%AppData%\Neku\Assets\pets\`.

---

## 🚀 Quick Start

### Option A: Windows Installer (Recommended)
1. Download **`Neku-Setup-0.4.0.exe`** from the latest [GitHub Releases](https://github.com/kunal-gswm/Neku/releases).
2. Run the setup wizard.
   - Installs per-user without requiring administrator privileges (`%LocalAppData%\Programs\Neku`).
   - Configures the Start Menu shortcut with the native **`Ctrl + Alt + N`** summon hotkey.
   - Optionally creates a Desktop icon and enables automatic launch on Windows sign-in.
   - Clean uninstaller included; never touches user notes, settings, or custom companions in `%AppData%\Neku`.

### Option B: Standalone Portable Executable
1. Download **`Neku.exe`** from the latest [GitHub Releases](https://github.com/kunal-gswm/Neku/releases).
2. Place it in any folder and run `Neku.exe`. That's it!
   - Packaged as a **single self-contained executable** (~156 MB).
   - No .NET runtime installation, Git, or external folders required.
   - *Note on first launch:* Windows SmartScreen may show an unrecognized app prompt. Click **More info → Run anyway**.

### Option C: Build from Source
**Requirements:** Windows 10 (1809+) or Windows 11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
# Clone the repository
git clone https://github.com/kunal-gswm/Neku.git
cd Neku

# Build and run tests
dotnet build DesktopPet.sln -c Release
dotnet test DesktopPet.sln -c Release

# Run the compiled binary
.\src\DesktopPet\bin\Release\net10.0-windows\win-x64\Neku.exe

# Or publish the single-file release artifact
.\scripts\publish.ps1

# Or build the Inno Setup Windows installer
.\scripts\build-installer.ps1
```

---

## ⌨️ Keyboard Shortcuts

Neku features dedicated global and system-level keyboard shortcuts for effortless desktop control:

| Shortcut | Scope | Action | Description |
| :--- | :--- | :--- | :--- |
| **`Ctrl + Alt + N`** | System / Start Menu | **Summon / Launch Neku** | Launches Neku if stopped. If already running, seamlessly unhides Neku or clamps Neku into the visible work area with an acknowledgment reaction (without stealing active window focus). |
| **`Ctrl + Alt + Q`** | Global (while running) | **Graceful Exit** | Instantly saves all settings, active Pomodoro focus state, and notes, releases the single-instance mutex, and cleanly exits the application. |
| **`Ctrl + Space`** | Global (configurable) | **AI Assistant Chat** | Opens the AI companion chat drawer when enabled in Settings. |
| **`Ctrl + N`** | Quick Notes Window | **New Note** | Creates a new scratchpad note. |
| **`Ctrl + F`** | Quick Notes Window | **Find / Search** | Focuses the search bar to filter notes by title or body content. |
| **`Delete`** | Quick Notes Window | **Delete Note** | Deletes the currently selected note. |

---

## 🎮 Controls & Interactions

| Action | Control | Description |
| :--- | :--- | :--- |
| **Boop (Click)** | Left-Click | Triggers an immediate playful reaction and dialogue bubble. |
| **Drag & Throw** | Left-Click & Drag | Pick up Neku and move anywhere. Release with velocity to toss Neku into a gentle arc! |
| **Pet** | Mouse Hover | Hover over Neku to show floating hearts and boost mood. |
| **Action Menu** | Right-Click | Native WPF menu for Focus Timer, Quick Notes, Settings, Pause, Hide, and Exit. |
| **System Tray** | Tray Icon | Right-click the cat paw in the taskbar notification area for full application control. |
| **Start / Pause Focus** | Action Menu / Tray | Starts or pauses the Pomodoro focus timer with matching companion reactions. |
| **Quick Notes** | Action Menu / Tray | Opens the persistent local notes scratchpad. |
| **Click-Through** | Settings / Action Menu | Allows mouse clicks to pass straight through Neku to underlying windows. |
| **Reset Position** | Action Menu / Tray | Instantly returns Neku to the center of the primary display work area if lost. |

---

## ⏱️ Focus Timer & Quick Notes

### Pomodoro Focus Timer
- **Default Durations:** 25-minute focus session, 5-minute restorative break (configurable in Settings).
- **HUD Overlay:** Small floating timer pill attached above or below Neku showing elapsed/remaining time.
- **Deadline-Based:** Timers compute absolute UTC deadlines. If you lock your PC, sleep your laptop, or restart the app, Neku recovers the remaining time seamlessly.
- **Restrained Notifications:** When a session or break ends, Neku displays a gentle dialogue balloon and bottom-right toast banner.

### Quick Notes Scratchpad
- **Fast & Responsive:** Instant load with live search filtering across titles and note bodies.
- **Autosave & Persistence:** All changes are automatically saved to `%AppData%\Neku\notes.json`.
- **Keyboard Friendly:** Use `Ctrl+N` to create a note, `Ctrl+F` to search, and `Delete` to remove.

---

## 🛠️ Developer Event Integration

Neku can react to development lifecycle events (builds, test suites, deployments) via a local HTTP listener on port `48123` (disabled by default for zero resource usage).

### Enabling:
1. Open **Settings** (Right-click Neku → **Settings**).
2. Go to the **Developer** tab.
3. Check **Enable Developer Event Endpoint (Port 48123)** and click **Apply & Close**.

### Triggering Events from PowerShell:
```powershell
# Build succeeded!
.\tools\neku-event.ps1 build_success "Build passed in 2.1s! 🎉"

# Test failed!
.\tools\neku-event.ps1 test_fail "3 tests failed in Core.Tests 😱"

# Custom task finished
.\tools\neku-event.ps1 command_complete "Docker container deployed! ⚡"
```

### Triggering via cURL:
```bash
curl -X POST http://127.0.0.1:48123/event -H "Content-Type: application/json" -d '{"type":"build_success","message":"Great job!"}'
```

Supported event types: `build_success`, `build_failure`, `test_pass`, `test_fail`, `command_complete`.

---

## 🎨 Custom Companion Packs (Local Characters)

Public releases of Neku ship exclusively with the generic mascot (`neku`) and standard cats (`cat`, `cat-orange`, `cat-silver`, etc.). You can easily create and load your own personalized companion pack locally without modifying the code or rebuilding:

### Directory Structure
Place your pack in `%AppData%\Neku\Assets\pets\<your-pack-name>\` (or beside `Neku.exe` in `Assets\pets\<your-pack-name>\`):
```
%AppData%\Neku\Assets\pets\my-custom-companion\
├── manifest.json
└── spritesheet.png
```

### Manifest Format (`manifest.json`):
```json
{
  "name": "My Companion",
  "sheet": "spritesheet.png",
  "cellWidth": 128,
  "cellHeight": 128,
  "columns": 4,
  "scale": 1.0,
  "animations": {
    "idle": { "frames": [0, 1], "fps": 2.0, "loop": true },
    "walk": { "frames": [2, 3, 4, 5], "fps": 6.0, "loop": true },
    "sleep": { "frames": [6, 7], "fps": 1.0, "loop": true },
    "fall": { "frames": [8, 9], "fps": 4.0, "loop": true },
    "drag": { "frames": [10], "fps": 1.0, "loop": true },
    "happy": { "frames": [11, 12], "fps": 3.0, "loop": true },
    "annoyed": { "frames": [13, 14], "fps": 3.0, "loop": true },
    "surprised": { "frames": [15], "fps": 1.0, "loop": false }
  }
}
```
Open **Settings > Appearance**, select your custom pack from the dropdown, and Neku will immediately adopt the new artwork.

---

## 🔒 Privacy & Offline First

- **Zero Telemetry:** Neku has zero analytics, crash reporters, or tracking identifiers.
- **Offline By Default:** Neku makes no outbound network connections. The developer event listener binds strictly to loopback (`127.0.0.1`).
- **Private Asset Isolation:** Local personal companion packs remain strictly outside git tracking and are never embedded into public release binaries.

---

## ⚡ Performance & Power Efficiency

Neku is designed to run silently without impacting games, compilation, or battery life:
- **Detached Render Loop:** Inactive, paused, or quiet modes completely disconnect from `CompositionTarget.Rendering` for **0.0% CPU** consumption.
- **On-Demand Effects Overlay:** Full-screen transparent DirectX swap chains are only created when particles or dialogue bubbles are actively visible, freeing DWM composition surfaces during idle.
- **Optional Software Rendering:** Run Neku with `--software-rendering` or toggle Software Rendering in **Settings > Performance** to run via CPU rasterization, saving ~75 MB of NVIDIA/GPU driver memory on lightweight laptops.
- See [docs/PERFORMANCE.md](docs/PERFORMANCE.md) for full empirical benchmarks, memory profiling, and architecture details.

---

## 📁 Repository Structure

```
├── docs/
│   ├── ARCHITECTURE.md          # Architecture and component diagrams
│   ├── BASELINE_AUDIT.md        # Technical baseline audit of original codebase
│   ├── PERFORMANCE.md           # Rendering, memory profiling, and power consumption guide
│   └── TESTING.md               # Unit testing matrix and verification checklist
├── src/
│   └── DesktopPet/              # Main Neku WPF Application (.NET 10)
│       ├── Assets/pets/neku/    # Generic mascot spritesheet and animation manifests
│       ├── Engine/              # Physics, state machine, personality, platform geometry
│       ├── Native/              # Win32 P/Invoke interop wrappers (WS_EX_NOACTIVATE, hooks)
│       ├── Services/            # Focus timer, notes, settings, tray, developer webhook
│       └── UI/                  # Pet window, focus HUD, notes window, settings window
├── tests/
│   └── DesktopPet.Tests/        # 117 xUnit automated test suites
├── tools/
│   ├── benchmark_memory.ps1     # Automated memory & CPU benchmark script
│   ├── measure_interactive.ps1  # Automated interactive lifecycle benchmark script
│   ├── neku-event.ps1           # PowerShell developer event trigger CLI
│   └── neku-event.bat           # Batch developer event trigger CLI
├── scripts/
│   └── publish.ps1              # Self-contained single-file publish script
├── THIRD_PARTY_NOTICES.md       # Attribution to PixelPaws and third-party notices
├── LICENSE                      # MIT License
└── README.md                    # Project overview
```

---

## 📜 Attribution & License

- **Neku** is licensed under the [MIT License](LICENSE).
- Portions of the window management, hooks, and physics foundation are based on [PixelPaws](https://github.com/jordansbc/pixelpaws) by Jordan SBC (MIT License). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for details.
