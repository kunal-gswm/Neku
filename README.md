# Neku — A Lightweight, Playful Windows Desktop Companion

**Neku** is a tiny, mischievous desktop companion that lives right on your Windows desktop. Native, feather-light, and expressive, Neku brings your workspace to life with retro pixel animations, desktop physics, deterministic personality drives, and optional developer build/test event reactions.

Built with **C# / WPF on .NET 10**.

---

## 🌟 Highlights

- **Native & Feather-Light:** Runs lean on the Windows desktop. When paused, hidden, or in quiet mode, rendering callbacks are detached completely — dropping CPU usage to **0.0%**.
- **Playful Character & Personality:** Neku is an original, expressive cat-like goblin with local state drives (`Mood`, `Energy`, `Familiarity`, `Mischief`) and original dialogue lines. No AI models or internet connections required for normal operation.
- **Desktop Physics & Window Walking:** Walks along the top edges of open application windows and the taskbar. Gravity, momentum-bounded throwing on drag release, and collision detection keep Neku grounded.
- **Developer Event Reactions (Optional):** Celebrate green builds and panic at test failures! Neku includes a local HTTP endpoint and CLI tools (`tools/neku-event.ps1`, `tools/neku-event.bat`) to hook into your CI, build scripts, or terminal workflows. Disabled by default with zero background threads.
- **Respectful of Your Focus:** Automatically enters Quiet Mode during full-screen games, video calls, or presentations. Supports optional **Click-Through Mode** so Neku never intercepts your work.
- **Multi-Monitor & Mixed DPI:** Smoothly roams multi-monitor workspaces and handles mixed DPI scaling factors.

---

## 🚀 Quick Start

### 1. Requirements
- Windows 10 (version 1809+) or Windows 11.
- .NET 10 Runtime (or install the .NET 10 SDK if building from source).

### 2. Building from Source
```powershell
git clone https://github.com/kunal-gswm/Neku.git
cd Neku
dotnet build DesktopPet.sln -c Release
dotnet test DesktopPet.sln -c Release
```

To launch Neku after building:
```powershell
.\src\DesktopPet\bin\Release\net10.0-windows\win-x64\Neku.exe
```

---

## 🎮 Controls & Interactions

| Action | Control | Description |
| :--- | :--- | :--- |
| **Boop (Click)** | Left-Click | Triggers an immediate playful reaction and dialogue bubble. |
| **Drag & Throw** | Left-Click & Drag | Pick up Neku and move anywhere. Release with velocity to toss Neku into a gentle arc! |
| **Pet** | Mouse Hover | Hover over Neku to show floating hearts and boost mood. |
| **Context Menu** | Right-Click | Access quick controls: Settings, Pause/Resume, Hide, Click-through, Quiet mode, Reset position. |
| **Tray Menu** | System Tray Icon | Right-click the cat paw in the taskbar notification area for full application control. |
| **Click-Through** | Tray / Context Menu | Allows mouse clicks to pass straight through Neku to underlying windows. |
| **Reset Position** | Tray / Context Menu | Instantly returns Neku to the center of the primary display work area if lost. |

---

## 🛠️ Developer Event Integration

Neku can react to your development environment events (builds, test suites, deployments) via a local HTTP listener on port `48123`.

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

### Triggering Events from Batch / CMD:
```cmd
tools\neku-event.bat build_success "Compilation succeeded!"
tools\neku-event.bat test_fail "Tests red!"
```

### Triggering via cURL or any HTTP client:
```bash
curl -X POST http://127.0.0.1:48123/event -H "Content-Type: application/json" -d '{"type":"build_success","message":"Great job!"}'
```

Supported event types: `build_success`, `build_failure`, `test_pass`, `test_fail`, `command_complete`.

---

## 🧠 Personality & State Machine

Neku's behavior is guided by an explicit deterministic state machine and local emotional drives:

- **States:** `Idle`, `Blink`, `LookAround`, `Walk`, `Sit`, `Sleep`, `Loaf`, `Zoomies`, `Fall`, `Drag`, `Happy`, `Annoyed`, `Surprised`, `Typing`, `TypingFast`, `Play`.
- **Drives:**
  - `Mood` (0.0 to 1.0): Influenced by petting, dragging, boops, and developer events. Drives dialogue choices.
  - `Energy` (0.0 to 1.0): Depletes during runs and zoomies, recharges during sleep and sit.
  - `Familiarity` (0.0 to 1.0): Increases gradually with interactions, unlocking more expressive lines.
  - `Mischief` (0.0 to 1.0): Governs spontaneity and chaotic reactions.
- **Original Dialogue:** Neku speaks through humorous, quirky speech bubbles tailored to its current mood.

---

## ⚡ Performance Profile

Neku is strictly engineered to minimize hardware and battery impact:
- **Zero-Cost Pause & Quiet Mode:** When paused, hidden, or presenting, `CompositionTarget.Rendering` is detached entirely (0.0% CPU).
- **Throttled Surface Scanning:** Window edge detection is evaluated every 3 seconds while stationary, and every 0.25 seconds while moving.
- **Cached Visuals:** Spritesheets are loaded and decoded into memory once at startup.
- **Memory Footprint:** ~40–55 MB working set under .NET 10.
- See [docs/PERFORMANCE.md](docs/PERFORMANCE.md) for detailed benchmarks and architectural notes.

---

## 📁 Repository Structure

```
├── docs/
│   ├── ARCHITECTURE.md          # Complete architecture and component diagrams
│   ├── BASELINE_AUDIT.md        # Technical baseline audit of original codebase
│   ├── PERFORMANCE.md           # Rendering, lifecycle, and power consumption guide
│   └── TESTING.md               # Unit testing matrix and verification checklist
├── src/
│   └── DesktopPet/              # Main Neku WPF Application (.NET 10)
│       ├── Assets/pets/neku/    # Neku spritesheet and animation manifests
│       ├── Engine/              # Physics, state machine, personality, platform geometry
│       ├── Native/              # Win32 P/Invoke interop wrappers
│       ├── Services/            # Settings, tray, monitors, developer event endpoint
│       └── UI/                  # Pet window, settings window, effects overlay
├── tests/
│   └── DesktopPet.Tests/        # 90 xUnit automated test suites
├── tools/
│   ├── neku-event.ps1           # PowerShell developer event trigger CLI
│   ├── neku-event.bat           # Batch developer event trigger CLI
│   └── gen_neku_placeholder.py  # Original sprite asset generation tool
├── THIRD_PARTY_NOTICES.md       # Attribution to PixelPaws and third-party notices
├── LICENSE                      # MIT License
└── README.md                    # Project overview
```

---

## 📜 Attribution & License

- **Neku** is licensed under the [MIT License](LICENSE).
- Portions of the window management, hooks, and physics foundation are based on [PixelPaws](https://github.com/jordansbc/pixelpaws) by Jordan SBC (MIT License). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for details.
