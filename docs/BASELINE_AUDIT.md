# Baseline Audit: PixelPaws Codebase

This document provides a comprehensive architectural and functional baseline audit of the **PixelPaws** codebase before its evolution into **Neku**, a desktop companion application for Windows.

---

## 1. Executive Summary

PixelPaws is a high-performance Windows desktop mascot application built with **C# and WPF**. It creates a borderless, transparent, click-through capable, always-on-top desktop pet that roams across multiple monitors, reacts to user input (typing, mouse movement, window manipulation), walks along the top edges of open windows, and simulates lifelike feline behavior.

The codebase is engineered with strict performance considerations:
- Direct Win32 P/Invoke integration for window enumeration, hooks, and virtual monitor geometry.
- Low CPU footprint achieved by detaching from WPF's render loop when paused, hidden, or idle.
- Modular architecture separating physics and simulation from UI rendering and OS interaction.
- Standalone math and logic components designed for pure unit testing without UI dependencies.

---

## 2. Feature & Functional Audit: What Works

| Feature / Subsystem | Implementation Details & Status |
|---------------------|---------------------------------|
| **Transparent Pet Window** | Borderless, transparent, top-most window implemented in [`PetWindow`](file:///d:/Neku/pixelpaws/src/DesktopPet/UI/PetWindow.xaml.cs) (`WS_EX_TOOLWINDOW`, `WS_EX_TOPMOST`, `WS_EX_NOACTIVATE`). Doesn't clutter the taskbar or Alt+Tab. |
| **Direct Dragging & Dropping** | Mouse-down hit test allows left-click dragging. Cat follows cursor with realistic offset. On release, physics handles gravity and finds landing surfaces. |
| **Comprehensive Animation Engine** | 27 distinct behavioral states implemented in [`PetState`](file:///d:/Neku/pixelpaws/src/DesktopPet/Engine/PetState.cs): `Idle`, `Walk`, `Sleep`, `Fall`, `Drag`, `Chase`, `Eat`, `Pet`, `Stretch`, `Typing`, `TypingFast`, `Play`, `Zoomies`, `Bat`, `Hunt`, `Pounce`, `Proud`, `Groom`, `Yawn`, `Loaf`, `Gift`, `Knockoff`, `SideRest`, `Wakeup`, `Spin`, `Jump`, `Talk`. |
| **System Tray Integration** | Managed via [`TrayService`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/TrayService.cs) with context menu: Settings, Pause/Resume, Bring Pet Here, AI Talk, Open Logs, and Quit. |
| **Settings Persistence** | Serialized via `System.Text.Json` into `%AppData%\PixelPaws\settings.json` via [`SettingsService`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/SettingsService.cs). |
| **Multi-Monitor Geometry** | Handled by [`DesktopGeometry`](file:///d:/Neku/pixelpaws/src/DesktopPet/Engine/DesktopGeometry.cs). Supports heterogeneous multi-monitor setups, different heights, taskbar positions, and dynamic display connect/disconnect events. |
| **Per-Monitor DPI Handling** | Native coordinate transformations from physical pixels to WPF Device Independent Pixels (DIPs) using `PerMonitorV2` DPI scaling. |
| **Physics & Collision Engine** | Real-time gravity (`2000 px/s²`), terminal velocity cap (`2600 px/s`), horizontal movement, jump arc kinematics, and edge collisions in [`PetEngine`](file:///d:/Neku/pixelpaws/src/DesktopPet/Engine/PetEngine.cs). |
| **Surface & Platform Detection** | [`SurfaceProvider`](file:///d:/Neku/pixelpaws/src/DesktopPet/Engine/SurfaceProvider.cs) interrogates Win32 z-order window bounds (excluding tooltips, cloaked windows, overlays) and desktop floors. |
| **Window Walking** | Pure functional landing and support detection via [`Platforms`](file:///d:/Neku/pixelpaws/src/DesktopPet/Engine/Platforms.cs). Pet walks along window title bars and falls when walking off edges. |
| **Low-Level Keyboard & Mouse Hooks** | Global hooks via `WH_KEYBOARD_LL` and `WH_MOUSE_LL` in [`KeyboardMonitor`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/KeyboardMonitor.cs) and [`MouseMonitor`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/MouseMonitor.cs). Computes typing velocity and detects mouse wheel scrolling for playful interactions. |
| **Hook Watchdog** | [`HookWatchdog`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/HookWatchdog.cs) continuously monitors hook health to recover if Windows unhooks low-level hooks due to timeout. |
| **Quiet Mode / Do Not Disturb** | [`SystemMonitor`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/SystemMonitor.cs) queries `SHQueryUserNotificationState` to detect full-screen gaming, presentations, screen shares, or Focus Assist. Pet calms down or completely hides. |
| **AI Companion (Optional)** | Optional chat companion with Google Gemini and local Ollama support via [`AiChatService`](file:///d:/Neku/pixelpaws/src/DesktopPet/Services/Ai/AiChatService.cs), parsing replies and emotion tags into animations. |
| **Single-Instance Enforcement** | Global named mutex (`PixelPaws.SingleInstance`) prevents duplicate running instances. |
| **Crash Logging & Diagnostics** | Unhandled dispatcher and domain exceptions captured to `%Temp%\pixelpaws_crash.log`. Verbose tracing enabled via `PIXELPAWS_DEBUG=1`. |
| **State Continuity** | Cat remembers last desktop position, energy, and hunger values across restarts to maintain behavioral consistency. |

---

## 3. Architecture Overview

The codebase is organized into cleanly demarcated layers:

```
src/DesktopPet/
├── Engine/                 # Simulation, kinematics, surface calculations, and state
│   ├── DesktopGeometry.cs  # Multi-screen bounds, monitor work area calculation
│   ├── PetEngine.cs        # Main loop, frame ticker, physics, state dispatch
│   ├── PetManifest.cs      # JSON manifest parser for sprite sheet metadata
│   ├── PetState.cs         # Behavioral state enumeration (27 states)
│   ├── Platforms.cs        # Pure landing, tolerance, and surface contact math
│   ├── SpriteAnimator.cs   # Sprite slicing, frame extraction, and playback
│   ├── StateMachine.cs     # Personality, energy/hunger drives, weighted rolls
│   └── SurfaceProvider.cs  # Win32 window edge discovery and surface generation
├── Services/               # Application-level services and OS integrations
│   ├── AppSettings.cs      # Configuration POCO model
│   ├── AppVersion.cs       # Assembly version helper
│   ├── AssetSource.cs      # Embedded resource and disk override asset loader
│   ├── AutostartService.cs # Windows registry Run key integration
│   ├── DebugLog.cs         # Tracing and diagnostic logger
│   ├── HookWatchdog.cs     # Hook health check and auto-reinstallation
│   ├── HotkeyService.cs    # Global hotkey registration via RegisterHotKey
│   ├── KeyboardMonitor.cs  # WH_KEYBOARD_LL hook & typing speed estimation
│   ├── MouseMonitor.cs     # WH_MOUSE_LL hook & scroll wheel detection
│   ├── SettingsService.cs  # JSON read/write for settings persistence
│   ├── SystemMonitor.cs    # CPU usage, battery status, foreground context, DND
│   ├── TrayService.cs      # System tray icon and context menu management
│   ├── UpdateService.cs    # GitHub release update checker and payload installer
│   └── Ai/                 # AI Companion services
│       ├── AiChatService.cs
│       ├── AiMemory.cs
│       ├── CuteTools.cs
│       ├── EmotionMap.cs
│       ├── GeminiProvider.cs
│       ├── IAiProvider.cs
│       └── OllamaProvider.cs
├── UI/                     # WPF Windows and Visual Overlays
│   ├── ChatInputWindow.xaml(.cs)  # Modal-less chat bar
│   ├── EffectsOverlay.cs          # Click-through transparent overlay (hearts, TP, etc.)
│   ├── PetWindow.xaml(.cs)        # Primary mascot transparent window
│   ├── SettingsWindow.xaml(.cs)   # Tabbed configuration interface
│   └── StretchNotification.xaml(.cs) # Periodic ergonomic nudge toast
└── Native/                 # Win32 P/Invoke interop definitions
    └── Win32.cs            # User32, Shell32, Kernel32, DwmApi declarations
```

---

## 4. Test Suite Audit

The test suite in [`tests/DesktopPet.Tests`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests) targets `net10.0-windows` and passes **66 tests with 0 failures**:

1. **[`PlatformsTests.cs`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests/PlatformsTests.cs)**: Validates platform support detection, boundary conditions, fall penetration thresholds, and landing calculations.
2. **[`DesktopGeometryTests.cs`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests/DesktopGeometryTests.cs)**: Tests multi-monitor work areas, clamp boundaries, non-rectangular desktop layouts, and DPI scaling logic.
3. **[`StateMachineTests.cs`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests/StateMachineTests.cs)**: Verifies energy/hunger drive evolution, drive recovery/drain rates, action selection probabilities, and persistence capture/restore.
4. **[`QuietModeTests.cs`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests/QuietModeTests.cs)**: Tests notification states, full-screen detection, and suppression of disruptive pet states during presentation/gaming modes.
5. **[`ReplyParsingTests.cs`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests/ReplyParsingTests.cs)**: Verifies parsing of structured AI emotion tags, trailing tags, and tool invocation markers.
6. **[`AppVersionTests.cs`](file:///d:/Neku/pixelpaws/tests/DesktopPet.Tests/AppVersionTests.cs)**: Validates version parsing and compatibility matching against release tags.

---

## 5. Migration Notes: .NET 10 Upgrade

The solution was successfully upgraded from `net8.0-windows` to `net10.0-windows` because the host environment exclusively provides the .NET 10 preview runtime.

### Build Health
- **Errors**: 0
- **Warnings**: 6 minor warnings:
  - 2× `WFO0003`: High DPI settings declared in `app.manifest` while targeting Windows Forms interoperability.
  - 4× `CA2024`: Usage of `StreamReader.EndOfStream` in async streaming methods within `OllamaProvider.cs` and `GeminiProvider.cs`.

All 66 tests execute in ~47ms and pass cleanly under .NET 10.

---

## 6. What Needs Changing for Neku

| Area | Current (PixelPaws) | Neku Target |
|------|---------------------|-------------|
| **Branding & Identity** | PixelPaws mascot, cat metaphors | **Neku** companion identity, assembly `Neku.exe`, `%AppData%\Neku` storage. |
| **Art & Sprite Assets** | Cat sprite sheet (44 frames across 7 coats) | Custom Neku character spritesheet and animation definitions. |
| **AI Companion** | External LLM chat via Google Gemini / Ollama with speech balloons | Removal or radical simplification; focus on fast, local, non-cloud companion mechanics. |
| **State Machine & Personality** | Feline drives (hunger, napping, scratching) | Expanded state machine with distinct personality traits, idle variations, and expressive reactions. |
| **Developer Events** | General system load and typing speed reactions | Integration with developer-centric events (build completion, Git actions, terminal/IDE activity). |
| **Settings UI & Schema** | AI provider keys, cat coats, stretch reminders | Reorganized settings tabs, developer event triggers, custom sound/reaction toggles. |

---

## 7. What Should Be Retained

1. **Core Engine Architecture**: The frame-tick architecture, separation of simulation (`PetEngine`) from display (`PetWindow`), and `IPetView` abstraction.
2. **Physics & Math Library**: Gravity, trajectory, jumping, falling, and the zero-allocation `Platforms` landing algorithms.
3. **Multi-Monitor & DPI Subsystem**: Robust multi-monitor geometry handling, coordinate scaling, and taskbar exclusion.
4. **Window Walking Detection**: Real-time Win32 top-level window bounding box querying via `SurfaceProvider`.
5. **System Tray & Window Management**: Single-instance mutex, tray icon menus, pause lifecycle, and autostart registry management.
6. **Low-Level Hook Framework**: Global mouse and keyboard monitoring with `HookWatchdog` resilience.
7. **Quiet Mode & System Awareness**: OS notification state detection (`SHQueryUserNotificationState`) for zero-interruption work sessions.
8. **Test Infrastructure**: Comprehensive test framework and pure unit test coverage.
